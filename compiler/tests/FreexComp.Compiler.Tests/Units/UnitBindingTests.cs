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

    private static EmitResult Run(InMemoryFileSystem fs, EmitStage stage = EmitStage.Check)
    {
        var options = new CompilationOptions { MainFile = Path.Combine(Dir, "P.pas"), RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        return Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = stage }, TestContext.Current.CancellationToken);
    }

    /// <summary>"File(line,col): CODE message" of every error and warning (hints left out).</summary>
    private static string[] Check(string program, params (string Name, string Source)[] units) =>
        [.. Run(Files(program, units)).Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Hint)
            .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}")];

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

    [Fact]
    [Trait("Req", "LANG-513")]
    public void InterfaceUsesCycle_IsE2205WithThePath() =>
        Assert.Equal(["Y.pas(1,24): E2205 Circular unit reference: X → Y → X"], Check("""
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
