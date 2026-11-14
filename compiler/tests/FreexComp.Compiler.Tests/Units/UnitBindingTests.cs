using FreexComp.Compiler.Tests.Emit;

namespace FreexComp.Compiler.Tests.Units;

/// <summary>
/// C-069: unit binding through <see cref="Compilation"/> (in memory, mini RTL <c>System</c>): uses scopes and their order
/// (LANG-310, 515, 516), privacy of implementation symbols, no transitive import (LANG-517), unit-qualified names incl.
/// dotted unit names and the longest prefix (LANG-311, 505, 538), implementation-uses cycles (LANG-513), the uses-entry
/// errors E2200…E2206/E2208, the unit graph's init and link order (LANG-520, CMP-625) and the uses chain of <c>{$IF}</c>.
/// </summary>
[Trait("Phase", "2")]
public sealed class UnitBindingTests
{
    private const string Dir = @"C:\p";

    private static InMemoryFileSystem Files(string program, params (string Name, string Source)[] units)
    {
        var fs = new InMemoryFileSystem().Add(Path.Combine(Dir, "P.pas"), program)
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")));
        foreach (var (name, source) in units)
        {
            fs.Add(Path.Combine(Dir, name), source);
        }

        return fs;
    }

    private static EmitResult Run(InMemoryFileSystem fs, EmitStage stage = EmitStage.Check, string[]? unitScopeNames = null)
    {
        var options = new CompilationOptions
        {
            MainFile = Path.Combine(Dir, "P.pas"), RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj", UnitScopeNames = [.. unitScopeNames ?? []],
        };
        return Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = stage }, TestContext.Current.CancellationToken);
    }

    /// <summary>"File(line,col): CODE message" of every error and warning (hints left out).</summary>
    private static string[] Check(string program, params (string Name, string Source)[] units) => Diagnostics(Run(Files(program, units)));

    private static string[] Diagnostics(EmitResult result) =>
        [.. result.Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Hint)
            .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}")];

    private const string AliasTable = """{ "aliases": { "System.SysUtils": "SysUtils", "Old.SysUtils": "SysUtils", "Winapi.Msgs": "Messages", "Sys.Core": "System" } }""";

    private const string SysUtils = "unit SysUtils; interface function Trim(I: Integer): Integer; var Count: Integer; implementation function Trim(I: Integer): Integer; begin Result := I end; initialization Inc(Count); end.";

    /// <summary>C-164: <see cref="Check"/> with the alias table <see cref="AliasTable"/> in the RTL root and the given unit scope names.</summary>
    private static string[] CheckAliased(string[] scopes, string program, params (string Name, string Source)[] units)
    {
        var fs = Files(program, units).Add(@"C:\rtl\unit-aliases.json", AliasTable);
        return Diagnostics(Run(fs, unitScopeNames: scopes));
    }

    private const string UnitA = """
        unit A;
        interface
        type TColor = (Red, Green); TRec = record X, Y: Integer; end; TSet = set of TColor;
        const K = 1; Name = 'a';
        var V: Integer; R: TRec;
        function F(I: Integer): Integer;
        procedure P;
        implementation
        const Hidden = 2;
        function F(I: Integer): Integer; begin Result := I + Hidden; end;
        procedure P; begin end;
        end.
        """;

    private const string UnitB = """
        unit B;
        interface
        const K = 'bb';
        implementation
        end.
        """;

    [Fact]
    [Trait("Req", "LANG-515")]
    public void InterfaceSymbols_AreVisibleInTheImporter() =>
        Assert.Empty(Check("""
            program P;
            uses A;
            var C: TColor; T: TRec; S: TSet; I: Integer;
            begin
              C := Green; T.X := K; S := [Red]; I := F(V) + R.Y; P; Writeln(Name);
            end.
            """, ("A.pas", UnitA)));

    [Fact]
    [Trait("Req", "LANG-515")]
    public void ImplementationSymbols_ArePrivate() =>
        Assert.Equal(["P.pas(3,15): E2003 Undeclared identifier 'Hidden'"], Check("""
            program P;
            uses A;
            begin Writeln(Hidden) end.
            """, ("A.pas", UnitA)));

    [Fact]
    [Trait("Req", "LANG-517")]
    public void Importing_IsNotTransitive() =>
        Assert.Equal(["P.pas(3,15): E2003 Undeclared identifier 'K'"], Check("""
            program P;
            uses C;
            begin Writeln(K) end.
            """, ("A.pas", UnitA), ("C.pas", "unit C; interface uses A; var W: TRec; implementation end.")));

    /// <summary>LANG-517: types of a unit not used reach the importer through the symbols of a unit that is.</summary>
    [Fact]
    [Trait("Req", "LANG-517")]
    public void TypesOfAnIndirectUnit_AreUsableThroughSymbols() =>
        Assert.Empty(Check("""
            program P;
            uses C;
            begin W.X := 1; Writeln(W.Y) end.
            """, ("A.pas", UnitA), ("C.pas", "unit C; interface uses A; var W: TRec; implementation end.")));

    [Theory]
    [Trait("Req", "LANG-516")]
    [InlineData("B, A", new string[0])]
    [InlineData("A, B", new[] { "P.pas(4,12): E2010 Incompatible types: 'string' and 'Integer'" })]
    public void SameName_TheLaterUnitWins(string uses, string[] expected) =>
        Assert.Equal(expected, Check($"""
            program P;
            uses {uses};
            var I: Integer;
            begin I := K end.
            """, ("A.pas", UnitA), ("B.pas", UnitB)));

    [Fact]
    [Trait("Req", "LANG-311")]
    [Trait("Req", "LANG-516")]
    public void QualifiedNames_SelectTheUnit() =>
        Assert.Empty(Check("""
            program P;
            uses A, B;
            var I: Integer; S: string; C: A.TColor; T: A.TRec;
            const Q = A.K + 1;
            begin
              I := A.K; S := B.K; C := A.TColor.Green; C := A.Red; T.X := A.F(A.V); A.P; A.R.X := Q; I := SizeOf(A.TRec);
              I := System.Length(S); Writeln(System.Output, A.Name);
            end.
            """, ("A.pas", UnitA), ("B.pas", UnitB)));

    [Fact]
    [Trait("Req", "LANG-538")]
    public void QualifiedName_UndeclaredMember_IsE2003AtTheMember() =>
        Assert.Equal(["P.pas(3,17): E2003 Undeclared identifier 'Hidden'"], Check("""
            program P;
            uses A;
            begin Writeln(A.Hidden) end.
            """, ("A.pas", UnitA)));

    /// <summary>
    /// C-214 (LANG-538 lead decision): inside a unit (and a program) its own name qualifies its own identifiers — interface and
    /// implementation ones, types too; a member it does not declare stays E2003.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-538")]
    public void OwnUnitNameQualifier() =>
        Assert.Equal(["U.pas(11,13): E2003 Undeclared identifier 'Missing'"], Check("""
            program P;
            uses U;
            type TLocal = Integer;
            var X: P.TLocal;
            begin Writeln(U.Twice(P.X)) end.
            """, ("U.pas", """
            unit U;
            interface
            type TNum = Integer;
            function Twice(N: U.TNum): U.TNum;
            implementation
            const Factor = 2;
            function Twice(N: U.TNum): U.TNum;
            begin
              Result := N * U.Factor;
              Writeln(U.Twice(0) = 0);
              Writeln(U.Missing);
            end;
            end.
            """)));

    /// <summary>LANG-311: a declaration of the unit itself named like a used unit hides the unit name.</summary>
    [Fact]
    [Trait("Req", "LANG-311")]
    public void LocalSymbolNamedLikeAUnit_Wins() =>
        Assert.Equal(["P.pas(4,17): E2005 'TRec' does not contain a member named 'K'"], Check("""
            program P;
            uses A;
            var A: TRec;
            begin Writeln(A.K) end.
            """, ("A.pas", UnitA)));

    [Fact]
    [Trait("Req", "LANG-515")]
    public void ImplementationUses_AreVisibleOnlyInTheImplementation() =>
        Assert.Equal(["U.pas(1,26): E2003 Undeclared identifier 'TRec'"], Check("""
            program P;
            uses U;
            begin end.
            """, ("A.pas", UnitA), ("U.pas", """
            unit U; interface var Z: TRec;
            implementation uses A;
            var Y: TRec;
            initialization Y.X := K;
            end.
            """)));

    [Fact]
    [Trait("Req", "LANG-513")]
    public void ImplementationUsesCycle_Binds() =>
        Assert.Empty(Check("""
            program P;
            uses X;
            begin Writeln(FX) end.
            """,
            ("X.pas", "unit X; interface function FX: Integer; implementation uses Y; function FX: Integer; begin Result := FY + 1 end; end."),
            ("Y.pas", "unit Y; interface uses X; function FY: Integer; implementation function FY: Integer; begin Result := 1 end; end.")));

    /// <summary>C-070 settled the location (LANG-513): in the first-reached unit X, at its entry that opens the cycle (C-069: in Y).</summary>
    [Fact]
    [Trait("Req", "LANG-513")]
    public void InterfaceUsesCycle_IsE2205WithThePath() =>
        Assert.Equal(["X.pas(1,24): E2205 Circular unit reference: X → Y → X"], Check("""
            program P;
            uses X;
            begin end.
            """,
            ("X.pas", "unit X; interface uses Y; implementation end."),
            ("Y.pas", "unit Y; interface uses X; implementation end.")));

    [Fact]
    [Trait("Req", "LANG-510")]
    public void UnitNotFound_IsE2202AtTheEntry() =>
        Assert.Equal(["P.pas(2,9): E2202 Unit 'Missing' not found"], Check("""
            program P;
            uses A, Missing;
            begin end.
            """, ("A.pas", UnitA)));

    [Fact]
    [Trait("Req", "LANG-505")]
    public void HeadingNotMatchingTheFileName_IsE2201() =>
        Assert.Equal(["W.pas(1,6): E2201 Unit name 'Other' does not match file name 'W'"], Check("""
            program P;
            uses W;
            begin end.
            """, ("W.pas", "unit Other; interface implementation end.")));

    [Fact]
    [Trait("Req", "LANG-502")]
    public void InClause_InAUnit_IsE2200() =>
        Assert.Equal(["U.pas(1,26): E2200 'in' clause is only allowed in a program's uses clause"], Check("""
            program P;
            uses U;
            begin end.
            """, ("A.pas", UnitA), ("U.pas", "unit U; interface uses A in 'A.pas'; implementation end.")));

    [Fact]
    [Trait("Req", "LANG-502")]
    public void InClause_InAProgram_NamesTheFile() =>
        Assert.Empty(Check("""
            program P;
            uses Q in 'sub\Q.pas';
            begin Writeln(QV) end.
            """, (@"sub\Q.pas", "unit Q; interface const QV = 7; implementation end.")));

    [Theory]
    [Trait("Req", "LANG-512")]
    [InlineData("uses A, A;", "P.pas(2,9): E2203 Unit 'A' is already used")]
    [InlineData("uses Prog2;", "P.pas(2,6): E2206 'Prog2' is a program, not a unit")]
    [InlineData("uses A, A2 in 'A.pas';", "P.pas(2,9): E2208 Unit 'A2' is already used as 'A'")]
    public void UsesEntryErrors(string uses, string expected)
    {
        var diagnostics = Check($"program P;\n{uses}\nbegin end.", ("A.pas", UnitA), ("Prog2.pas", "program Prog2; begin end."));
        Assert.Contains(expected, diagnostics);
    }

    [Fact]
    [Trait("Req", "LANG-512")]
    public void UnitUsingItself_IsE2204() =>
        Assert.Equal(["U.pas(1,24): E2204 Unit 'U' cannot use itself"], Check("""
            program P;
            uses U;
            begin end.
            """, ("U.pas", "unit U; interface uses U; implementation end.")));

    [Fact]
    [Trait("Req", "LANG-511")]
    public void SystemNamedExplicitly_HasNoEffect() =>
        Assert.Empty(Check("program P; uses System; begin Writeln(System.MaxInt) end."));

    [Fact]
    [Trait("Req", "LANG-505")]
    [Trait("Req", "LANG-538")]
    public void DottedUnitName_LongestPrefixQualifies() =>
        Assert.Empty(Check("""
            program P;
            uses Foo, Foo.Bar.Baz;
            var I: Integer;
            begin I := Foo.Bar.Baz.Value + Length(Foo.Value) + Foo.Bar; I := Value end.
            """,
            ("Foo.pas", "unit Foo; interface const Value = 'foo'; var Bar: Integer; implementation end."),
            ("Foo.Bar.Baz.pas", "unit Foo.Bar.Baz; interface const Value = 3; implementation end.")));

    /// <summary>LANG-536: <c>SysUtils</c> and its alias <c>System.SysUtils</c> are one unit, compiled and emitted once.</summary>
    [Fact]
    [Trait("Req", "LANG-536")]
    public void Alias_AndCanonicalName_ShareOneUnit()
    {
        var fs = Files("program P; uses A, SysUtils; begin Writeln(Count, A.Twice) end.",
            ("A.pas", "unit A; interface uses System.SysUtils; function Twice: Integer; implementation function Twice: Integer; begin Result := Trim(2) * Count end; end."),
            ("SysUtils.pas", SysUtils)).Add(@"C:\rtl\unit-aliases.json", AliasTable);
        var result = Run(fs, EmitStage.Ll);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(["P", "System", "SysUtils", "A"], result.CompiledUnits);
    }

    /// <summary>LANG-538: after <c>uses System.SysUtils</c> both the written and the canonical name qualify; System still qualifies.</summary>
    [Fact]
    [Trait("Req", "LANG-538")]
    public void Alias_WrittenAndCanonicalNamesQualify() =>
        Assert.Empty(CheckAliased([], """
            program P;
            uses System.SysUtils;
            var I: Integer;
            begin I := System.SysUtils.Trim(1) + SysUtils.Trim(2) + Trim(3) + System.SysUtils.Count + System.MaxInt end.
            """, ("SysUtils.pas", SysUtils)));

    /// <summary>LANG-538: an alias that was not written is not a qualifier.</summary>
    [Fact]
    [Trait("Req", "LANG-538")]
    public void Alias_NotWritten_IsNotAQualifier() =>
        Assert.Equal(["P.pas(3,15): E2003 Undeclared identifier 'Old'"], CheckAliased([], """
            program P;
            uses System.SysUtils;
            begin Writeln(Old.SysUtils.Count) end.
            """, ("SysUtils.pas", SysUtils)));

    /// <summary>LANG-537: two entries reaching one unit under different written names, also across the two clauses of a unit.</summary>
    [Fact]
    [Trait("Req", "LANG-537")]
    public void Alias_DuplicateUnderDifferentNames_IsE2208() =>
        Assert.Equal(
            ["U.pas(1,54): E2208 Unit 'Old.SysUtils' is already used as 'SysUtils'", "P.pas(2,19): E2208 Unit 'System.SysUtils' is already used as 'SysUtils'"],
            CheckAliased([], """
                program P;
                uses U, SysUtils, System.SysUtils;
                begin end.
                """, ("SysUtils.pas", SysUtils), ("U.pas", "unit U; interface uses SysUtils; implementation uses Old.SysUtils; end.")));

    /// <summary>LANG-537, 511: naming the implicit System under an alias has no effect.</summary>
    [Fact]
    [Trait("Req", "LANG-537")]
    public void Alias_OfSystem_HasNoEffect() =>
        Assert.Empty(CheckAliased([], "program P; uses Sys.Core, System; begin Writeln(System.MaxInt) end."));

    /// <summary>LANG-512: a unit cannot use itself through an alias either.</summary>
    [Fact]
    [Trait("Req", "LANG-512")]
    public void Alias_UnitUsingItself_IsE2204() =>
        Assert.Equal(["SysUtils.pas(1,31): E2204 Unit 'System.SysUtils' cannot use itself"], CheckAliased([], "program P; uses SysUtils; begin end.",
            ("SysUtils.pas", "unit SysUtils; interface uses System.SysUtils; implementation end.")));

    [Fact]
    [Trait("Req", "LANG-510")]
    public void Alias_CanonicalUnitMissing_IsE2202WithTheWrittenName() =>
        Assert.Equal(["P.pas(1,17): E2202 Unit 'System.SysUtils' not found"], CheckAliased([], "program P; uses System.SysUtils; begin end."));

    /// <summary>LANG-535 step 4, 538: <c>uses Tools</c> finds <c>Lib.Tools</c>; <c>Tools</c> and <c>Lib.Tools</c> qualify.</summary>
    [Fact]
    [Trait("Req", "LANG-535")]
    public void ScopeNames_FindTheScopedUnit() =>
        Assert.Empty(CheckAliased(["Lib"], """
            program P;
            uses Tools;
            var I: Integer;
            begin I := Tools.V + Lib.Tools.V + V end.
            """, ("Lib.Tools.pas", "unit Lib.Tools; interface const V = 1; implementation end.")));

    /// <summary>LANG-535 step 4: <c>S.N</c> as an alias; the scoped alias itself was not written, so it does not qualify.</summary>
    [Fact]
    [Trait("Req", "LANG-535")]
    public void ScopeNames_ScopedAlias() =>
        Assert.Equal(["P.pas(4,34): E2003 Undeclared identifier 'Winapi'"], CheckAliased(["Winapi"], """
            program P;
            uses Msgs;
            var I: Integer;
            begin I := Msgs.M + Messages.M + Winapi.Msgs.M end.
            """, ("Messages.pas", "unit Messages; interface const M = 2; implementation end.")));

    /// <summary>LANG-536: <c>uses Lib.Tools</c> and <c>uses Tools</c> through the scope name reach one unit (E2208 within one unit).</summary>
    [Fact]
    [Trait("Req", "LANG-536")]
    public void ScopeNames_ReachTheSameUnit_IsE2208() =>
        Assert.Equal(["P.pas(1,28): E2208 Unit 'Tools' is already used as 'Lib.Tools'"], CheckAliased(["Lib"], "program P; uses Lib.Tools, Tools; begin end.",
            ("Lib.Tools.pas", "unit Lib.Tools; interface implementation end.")));

    /// <summary>LANG-535 step 1: the heading of an <c>in</c> file must equal the written name N; E2201 names N.</summary>
    [Fact]
    [Trait("Req", "LANG-535")]
    public void InClause_HeadingDiffersFromTheWrittenName_IsE2201NamingIt() =>
        Assert.Equal(["P.pas(2,6): E2201 Unit name 'X' does not match file name 'Q'"], Check("""
            program P;
            uses Q in 'X.pas';
            begin end.
            """, ("X.pas", "unit X; interface implementation end.")));

    /// <summary>C-164: diagnostics in a used unit carry the file's on-disk spelling, not the uses-clause spelling.</summary>
    [Fact]
    public void DiagnosticPath_HasTheOnDiskCasing()
    {
        var result = Run(Files("program P; uses HELPER; begin end.", ("Helper.pas", "unit Helper; interface var X: Missing; implementation end.")));
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "E2003");
        Assert.Equal(@"C:\p\Helper.pas", diagnostic.Location.Path);
    }

    [Fact]
    [Trait("Req", "LANG-862")]
    public void IfDirective_SeesTheUsedUnits() =>
        Assert.Empty(Check("""
            program P;
            uses A;
            {$IF Declared(K) and Declared(A.TRec) and not Declared(A.Hidden) and (K = 1)}
            begin end.
            {$ELSE}
            begin Missing end.
            {$IFEND}
            """, ("A.pas", UnitA)));

    /// <summary>LANG-520 example (P uses A, B; A interface-uses C; B implementation-uses A): link order program, System, C, A, B.</summary>
    [Fact]
    [Trait("Req", "LANG-520")]
    [Trait("Req", "CMP-625")]
    public void Units_AreEmittedOnceInInitOrder()
    {
        var fs = Files("program P; uses A, B; begin end.",
            ("A.pas", "unit A; interface uses C; implementation end."),
            ("B.pas", "unit B; interface implementation uses A; end."),
            ("C.pas", "unit C; interface implementation end."));
        var result = Run(fs, EmitStage.Ll);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Equal(["P", "System", "C", "A", "B"], result.CompiledUnits);
        var program = fs.ReadAllText(@"C:\p\obj\P.ll");
        Assert.Contains("@\"$units\" = internal constant [4 x { ptr, ptr }] [", program);
        Assert.Contains("call void @\"System._Startup\"(ptr @\"$units\", i64 4)", program);
        Assert.Contains("define void @\"C.$init\"() #0 {", fs.ReadAllText(@"C:\p\obj\C.ll"));
    }

    /// <summary>
    /// C-161 (CMP-355, ABI-100): every unit with an initialization or finalization section — System included — has its
    /// <c>$init</c>/<c>$final</c> in <c>$units</c>; a unit without sections has two nulls.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-355")]
    [Trait("Req", "ABI-100")]
    public void UnitTable_HoldsInitAndFinal_OfEveryUnitWithSections()
    {
        var fs = Files("program P; uses A, B; begin end.",
            ("A.pas", "unit A; interface implementation initialization finalization end."),
            ("B.pas", "unit B; interface implementation end."));
        var system = File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas"));
        fs.Add(@"C:\rtl\System.pas", system[..system.LastIndexOf("end.", StringComparison.Ordinal)] + "initialization\nend.\n");
        var result = Run(fs, EmitStage.Ll);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        Assert.Contains("@\"$units\" = internal constant [3 x { ptr, ptr }] [" +
            "{ ptr, ptr } { ptr @\"System.$init\", ptr @\"System.$final\" }, " +
            "{ ptr, ptr } { ptr @\"A.$init\", ptr @\"A.$final\" }, " +
            "{ ptr, ptr } { ptr null, ptr null }], align 8", fs.ReadAllText(@"C:\p\obj\P.ll"));
    }

    /// <summary>
    /// C-071 (CMP-325 rule 7, CMP-350, LANG-523): a unit's managed globals are released in its <c>$final</c> after the finalization
    /// statements, in reverse declaration order; a unit whose only reason for a <c>$final</c> is a managed global still gets
    /// a unit table entry. The program's own globals: C-183 (lead decision 2026-11-14) replaced "at the end of <c>$main</c>" by
    /// a program <c>$final</c> as the last table entry (<see cref="ProgramGlobals_ReleasedAfterExitProcChain"/>).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-325")]
    [Trait("Req", "CMP-350")]
    [Trait("Req", "LANG-523")]
    public void ManagedGlobals_AreReleasedAtTheEndOfFinal()
    {
        var fs = Files("program P; uses A, B; var S: string; begin S := T + V end.",
            ("A.pas", "unit A; interface var T: string; D: array of Integer; implementation var U: string; initialization finalization Writeln(U); end."),
            ("B.pas", "unit B; interface var V: string; implementation end."));
        var result = Run(fs, EmitStage.Ll);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));

        var unit = fs.ReadAllText(@"C:\p\obj\A.ll");
        // CMP-393/394: the globals are finalised in the function-level cleanup scope, so Writeln's calls are invokes
        var final = unit[unit.IndexOf("define void @\"A.$final\"() #0 personality ptr @__C_specific_handler {", StringComparison.Ordinal)..];
        Assert.Matches("""
            (?s)invoke void @"System._WriteEnd"\(ptr @"System.Output"\).*
            cleanup:
              call void @"System._UStrClr"\(ptr @"A.U"\)
              call void @"System._DynArrayClr"\(ptr @"A.D", ptr @[^)]+\)
              call void @"System._UStrClr"\(ptr @"A.T"\)
              br label %exit
            """.Replace("\r\n", "\n"), final);
        Assert.Contains("define void @\"A.$init\"() #0 {\nentry:\n  br label %exit\n", unit);

        var other = fs.ReadAllText(@"C:\p\obj\B.ll");
        Assert.Contains("define void @\"B.$final\"() #0 {\nentry:\n  br label %cleanup\ncleanup:\n  call void @\"System._UStrClr\"(ptr @\"B.V\")\n  br label %exit\n", other);

        var program = fs.ReadAllText(@"C:\p\obj\P.ll");
        Assert.Contains("define internal void @\"P.$final\"() #0 {\nentry:\n  br label %cleanup\ncleanup:\n  call void @\"System._UStrClr\"(ptr @\"P.S\")\n  br label %exit\n", program);
        Assert.Contains("{ ptr, ptr } { ptr @\"A.$init\", ptr @\"A.$final\" }, { ptr, ptr } { ptr @\"B.$init\", ptr @\"B.$final\" }, " +
            "{ ptr, ptr } { ptr null, ptr @\"P.$final\" }]", program);
    }

    /// <summary>
    /// C-183 (lead decision 2026-11-14, ABI-104 steps 1–2): a program's managed globals are released by a program
    /// <c>$final</c> registered as the <b>last</b> <c>$units</c> entry (no <c>$init</c>), so the shutdown finalises it first —
    /// after the ExitProc chain, before the units' <c>$final</c> — and not at the end of <c>$main</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "ABI-104")]
    [Trait("Req", "CMP-355")]
    [Trait("Req", "LANG-523")]
    public void ProgramGlobals_ReleasedAfterExitProcChain()
    {
        var fs = Files("program P; uses B; var S: string; D: array of Integer; begin S := V + V end.",
            ("B.pas", "unit B; interface var V: string; implementation end."));
        var result = Run(fs, EmitStage.Ll);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));

        var program = fs.ReadAllText(@"C:\p\obj\P.ll");
        var main = program[program.IndexOf("define internal void @\"$main\"() #0 {", StringComparison.Ordinal)..];
        main = main[..main.IndexOf("\n}\n", StringComparison.Ordinal)];
        Assert.Contains("call void @\"System._UStrCat\"(ptr @\"P.S\"", main);
        Assert.DoesNotContain("_UStrClr\"(ptr @\"P.S\")", main);
        Assert.DoesNotContain("_DynArrayClr\"(ptr @\"P.D\"", main);
        Assert.Matches("""
            (?s)define internal void @"P\.\$final"\(\) #0 \{
            entry:
              br label %cleanup
            cleanup:
              call void @"System._DynArrayClr"\(ptr @"P.D", ptr @[^)]+\)
              call void @"System._UStrClr"\(ptr @"P.S"\)
              br label %exit
            """.Replace("\r\n", "\n"), program);
        Assert.Contains("@\"$units\" = internal constant [3 x { ptr, ptr }] [" +
            "{ ptr, ptr } { ptr null, ptr null }, " +                                        // the mini RTL's System has no sections
            "{ ptr, ptr } { ptr @\"B.$init\", ptr @\"B.$final\" }, " +
            "{ ptr, ptr } { ptr null, ptr @\"P.$final\" }], align 8", program);
        Assert.Contains("call void @\"System._Startup\"(ptr @\"$units\", i64 3)", program);
        Assert.DoesNotContain("declare void @\"P.$final\"", program);
    }

    /// <summary>A unit's interface routine and variable are referenced from the program module as external declarations.</summary>
    [Fact]
    [Trait("Req", "CMP-301")]
    public void CrossUnitReferences_AreDeclaredExternal()
    {
        var fs = Files("program P; uses A; begin V := F(K); P end.", ("A.pas", UnitA));
        var result = Run(fs, EmitStage.Ll);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.ToText())));
        var program = fs.ReadAllText(@"C:\p\obj\P.ll");
        Assert.Contains("declare i32 @\"A.F\"(i32)", program);
        Assert.Contains("@\"A.V\" = external global i32, align 4", program);
        var unit = fs.ReadAllText(@"C:\p\obj\A.ll");
        Assert.Contains("define i32 @\"A.F\"(i32 %I) #0 {", unit);
        Assert.Contains("@\"A.V\" = global i32 zeroinitializer, align 4", unit);                      // interface: external linkage
    }
}
