using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Targets;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// dll-output.md §2.2 (C-286): the exports of a library file through <see cref="Compilation.Exports"/> — resolution (LANG-545),
/// export names (LANG-547, 548), ordinals (LANG-549) and the boundary-safety warning W1082 (LANG-551).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class ExportBinderTests
{
    private const string Dir = @"C:\p";

    private static Compilation Compile(string library, params (string Name, string Source)[] units)
    {
        var fs = new InMemoryFileSystem().Add(Path.Combine(Dir, "L.pas"), library).AddSystem();
        foreach (var (name, source) in units)
        {
            fs.Add(Path.Combine(Dir, name), source);
        }

        return Compilation.Create(new CompilationOptions { MainFile = Path.Combine(Dir, "L.pas"), RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" }, [], fs);
    }

    /// <summary>"File(line,col): CODE message" of every error and warning.</summary>
    private static string[] Diagnostics(Compilation compilation) =>
        [.. compilation.GetDiagnostics(null, TestContext.Current.CancellationToken).Where(d => d.Severity != DiagnosticSeverity.Hint)
            .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}")];

    private static string[] Listing(Compilation compilation) => [.. compilation.Exports.Select(e => $"{e.Name} @{e.Ordinal} = {e.RoutineSymbol.QualifiedName}")];

    private const string FourRoutines = "procedure A; begin end;\nprocedure B; begin end;\nprocedure C; begin end;\nprocedure D; begin end;\n";

    [Fact]
    [Trait("Req", "LANG-549")]
    [Trait("Req", "CMP-1056")]
    public void OrdinalsAssignedAroundExplicitIndexes()
    {
        var compilation = Compile("library L;\n" + FourRoutines + "exports A, B index 1, C index 3, D;\nend.");

        Assert.Empty(Diagnostics(compilation));
        Assert.Equal(ModuleKind.Library, compilation.MainModuleKind);
        // source order A, B, C, D receives 2, 1, 3, 4; the list is in ordinal order
        Assert.Equal(["B @1 = L.B", "A @2 = L.A", "C @3 = L.C", "D @4 = L.D"], Listing(compilation));
    }

    [Fact]
    [Trait("Req", "LANG-549")]
    public void OrdinalsAssigned_ExplicitIndexOfALaterClauseIsNeverTaken()
    {
        // the explicit indexes of the whole file are reserved before any item is numbered (clauses in file order)
        var compilation = Compile("library L;\n" + FourRoutines + "exports A, B;\nexports C index 1 + 1, D index 65535;\nend.");

        Assert.Empty(Diagnostics(compilation));
        Assert.Equal(["A @1 = L.A", "C @2 = L.C", "B @3 = L.B", "D @65535 = L.D"], Listing(compilation));
    }

    [Fact]
    [Trait("Req", "LANG-549")]
    public void Index_OutOfRangeNotConstantOrDuplicate_IsReported()
    {
        var compilation = Compile("library L;\nvar V: Integer;\n" + FourRoutines + "exports A index 0, B index 65536, C index V, D index 'x';\nexports A index 5, B index 2 + 3;\nend.");

        Assert.Equal(
            [
                "L.pas(7,17): E2215 Export index must be a constant in the range 1..65535",
                "L.pas(7,28): E2215 Export index must be a constant in the range 1..65535",
                "L.pas(7,43): E2215 Export index must be a constant in the range 1..65535",
                "L.pas(7,54): E2215 Export index must be a constant in the range 1..65535",
                "L.pas(8,20): E2216 Duplicate export index 5",
            ],
            Diagnostics(compilation));
    }

    [Fact]
    [Trait("Req", "LANG-547")]
    [Trait("Req", "LANG-550")]
    public void DefaultNameUsesDeclaredCasing()
    {
        var compilation = Compile("library L;\nfunction MixedCase(A: Integer): Integer; stdcall;\nbegin Result := A; end;\nexports mixedCASE;\nend.");

        Assert.Empty(Diagnostics(compilation));
        var export = Assert.Single(compilation.Exports);
        Assert.Equal("MixedCase", export.Name);                 // the declared casing, not the one written in the clause
        Assert.Equal(1, export.Ordinal);
        Assert.Equal(CallConvDirective.StdCall, export.RoutineSymbol.CallingConvention);     // LANG-550: the declaration's convention
    }

    private const string MathUtil = "unit Math.Util;\ninterface\nfunction Twice(A: Integer): Integer;\nimplementation\nfunction Twice(A: Integer): Integer; begin Result := A * 2; end;\nfunction Hidden: Integer; begin Result := 0; end;\nend.";

    [Fact]
    [Trait("Req", "LANG-545")]
    public void QualifiedUnitRoutine()
    {
        var compilation = Compile(
            "library L;\nuses Math.Util;\nfunction Twice(A: Integer): Integer; begin Result := A + A; end;\nexports Math.Util.Twice name 'unit_twice', L.Twice, Twice name 'own_twice';\nend.",
            ("Math.Util.pas", MathUtil));

        Assert.Empty(Diagnostics(compilation));
        // the unit qualifier selects the used unit's routine; the library's own name qualifies its own (LANG-538)
        Assert.Equal(["unit_twice @1 = Math.Util.Twice", "Twice @2 = L.Twice", "own_twice @3 = L.Twice"], Listing(compilation));
    }

    [Fact]
    [Trait("Req", "LANG-545")]
    public void UndeclaredRoutine_IsE2003AtTheItem()
    {
        // only the interface of a used unit is visible, and a clause sees only the declarations before it
        var compilation = Compile(
            "library L;\nuses Math.Util;\nexports Math.Util.Hidden, Later, NoUnit.Twice;\nprocedure Later; begin end;\nend.",
            ("Math.Util.pas", MathUtil));

        Assert.Equal(
            [
                "L.pas(3,9): E2003 Undeclared identifier 'Math.Util.Hidden'",
                "L.pas(3,27): E2003 Undeclared identifier 'Later'",
                "L.pas(3,34): E2003 Undeclared identifier 'NoUnit'",
            ],
            Diagnostics(compilation));
        Assert.Empty(compilation.Exports);
    }

    [Fact]
    [Trait("Req", "LANG-548")]
    public void SameRoutineTwoNames()
    {
        var compilation = Compile("library L;\nprocedure P; begin end;\nexports P, P name 'p', P name 'P2' index 9;\nend.");

        Assert.Empty(Diagnostics(compilation));
        // export names are case-sensitive: 'P' and 'p' are two exports of the one routine
        Assert.Equal(["P @1 = L.P", "p @2 = L.P", "P2 @9 = L.P"], Listing(compilation));
        Assert.Same(compilation.Exports[0].RoutineSymbol, compilation.Exports[1].RoutineSymbol);
    }

    [Fact]
    [Trait("Req", "LANG-548")]
    public void DuplicateName_IsE2214AtTheSecondWithTheFirstAsRelatedLocation()
    {
        var compilation = Compile("library L;\nprocedure P; begin end;\nprocedure Q; begin end;\nexports P, Q name 'P', Q name 'q';\nend.");

        var diagnostic = Assert.Single(compilation.GetDiagnostics(null, TestContext.Current.CancellationToken), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("E2214", diagnostic.Code);
        Assert.Equal("Duplicate export name 'P'", diagnostic.Message);
        Assert.Equal((3, 11), (diagnostic.Location.LineSpan.Start.Line, diagnostic.Location.LineSpan.Start.Column));
        Assert.Equal((3, 8), (diagnostic.Related[0].Location.LineSpan.Start.Line, diagnostic.Related[0].Location.LineSpan.Start.Column));
    }

    [Theory]
    [Trait("Req", "LANG-547")]
    [InlineData("''", "")]
    [InlineData("'a@4'", "a@4")]
    [InlineData("'a.b'", "a.b")]
    [InlineData("'1a'", "1a")]
    [InlineData("'a b'", "a b")]
    [InlineData("'_start'", "_start")]
    [InlineData("'_dllstart'", "_dllstart")]
    [InlineData("'memcpy'", "memcpy")]
    [InlineData("'__chkstk'", "__chkstk")]
    public void InvalidExportName_IsE2213(string expression, string name)
    {
        var compilation = Compile($"library L;\nprocedure P; begin end;\nexports P name {expression};\nend.");

        Assert.Equal([$"L.pas(3,16): E2213 Invalid export name '{name}'"], Diagnostics(compilation));
    }

    [Fact]
    [Trait("Req", "LANG-547")]
    public void ExportName_ConstantExpression_DollarAndUnderscoreAllowed_AtMost255Characters()
    {
        var longest = new string('x', 255);
        var compilation = Compile($"library L;\nconst Prefix = 'lib_';\nprocedure P; begin end;\nprocedure memset; begin end;\nexports P name Prefix + 'p$1', P name '{longest}',\n  P name '{longest}y',\n  memset;\nend.");

        Assert.Equal(
            [
                "L.pas(6,10): E2213 Invalid export name '" + longest + "y'",                   // 256 characters
                "L.pas(7,3): E2213 Invalid export name 'memset'",                              // a default name is checked too
            ],
            Diagnostics(compilation));
    }

    private const string Declarations = """
        type
          TColor = (Red, Green);
          TSmall = 1..10;
          TPoint = record X, Y: Integer; end;
          PNested = ^TNested;
          TNested = record P: TPoint; A: array[0..3] of Double; C: set of TColor; N: PNested; end;
          TGrid = array[0..2, 0..2] of Single;
          TProc = procedure(A: Integer);
          TShort = string[20];
          IThing = interface procedure Foo; end;
          TThing = class end;
          TThingClass = class of TThing;
          TDyn = array of Integer;
          TMethod = procedure(A: Integer) of object;
          TRef = reference to procedure;
          TWithString = record A: Integer; S: string; end;
          TWithDyn = record A: TPoint; D: array[0..1] of TDyn; end;
          TStrings3 = array[0..2] of AnsiString;
        """;

    public static TheoryData<string, string?> Signatures => new()
    {
        // boundary-safe (LANG-551, first list): no warning
        { "procedure P(A: Integer; B: Int64; C: Byte; D: Boolean; E: Char; F: AnsiChar; G: TColor; H: TSmall)", null },
        { "procedure P(A: Single; B: Double)", null },
        { "procedure P(A: Pointer; B: PChar; C: PWideChar; D: PAnsiChar; E: PNested)", null },
        { "procedure P(A: TProc)", null },
        { "procedure P(A: TPoint; const B: TNested; var C: TGrid)", null },
        { "procedure P(A: TShort; B: ShortString)", null },
        { "procedure P(const A: IThing)", null },
        { "procedure P(var A; const B; out C: Integer; var D: TPoint)", null },
        { "procedure P", null },
        { "function P: Integer", null },
        { "function P: Double", null },
        { "function P: PChar", null },
        { "function P: TNested", null },
        { "function P: ShortString", null },
        { "function P: IThing", null },

        // not boundary-safe (second list): one warning naming the first offending type
        { "procedure P(A: Integer; S: string)", "string" },
        { "function P: string", "string" },
        { "procedure P(const S: AnsiString; W: string)", "AnsiString" },
        { "procedure P(A: TDyn)", "TDyn" },
        { "procedure P(const A: array of Integer)", "array of Integer" },
        { "procedure P(const A: array of const)", "array of const" },
        { "procedure P(A: TRef)", "TRef" },
        { "procedure P(A: TMethod)", "TMethod" },
        { "function P: TThing", "TThing" },
        { "procedure P(A: TThingClass)", "TThingClass" },
        { "procedure P(var F: Text)", "Text" },
        { "procedure P(A: TPoint; var R: TWithString; S: string)", "TWithString" },
        { "function P(A: Integer): TWithDyn", "TWithDyn" },
        { "procedure P(const A: TStrings3)", "TStrings3" },
    };

    [Theory]
    [MemberData(nameof(Signatures))]
    [Trait("Req", "LANG-551")]
    public void UnsafeTypes_WarnOncePerItem(string heading, string? offending)
    {
        var source = "library L;\n" + Declarations + "\n" + heading + ";\nbegin end;\nexports P, P name 'again';\nend.";
        var compilation = Compile(source);
        var line = source.Split('\n').Length - 1;

        // one warning per export item (both items name P), at the item; the library still has its exports
        string[] expected = offending is null ? []
            :
            [
                $"L.pas({line},9): W1082 Exported routine 'P' passes '{offending}' across the library boundary; caller and library do not share a memory manager",
                $"L.pas({line},12): W1082 Exported routine 'P' passes '{offending}' across the library boundary; caller and library do not share a memory manager",
            ];
        string[] actual = [.. Diagnostics(compilation).Where(d => !d.Contains(": W1042 ", StringComparison.Ordinal))];   // the empty function bodies
        Assert.Equal(expected, actual);
        Assert.Equal(2, compilation.Exports.Length);
    }

    [Fact]
    [Trait("Req", "LANG-551")]
    public void UnsafeTypes_WarnDirectiveSilencesAndPromotes()
    {
        const string routine = "library L;\nprocedure P(S: string); begin end;\n";

        Assert.Empty(Diagnostics(Compile(routine + "{$WARN EXPORT_UNSAFE_TYPE OFF}\nexports P;\nend.")));
        Assert.Single(Compile(routine + "{$WARN EXPORT_UNSAFE_TYPE OFF}\nexports P;\nend.").Exports);
        var promoted = Compile(routine + "{$WARN EXPORT_UNSAFE_TYPE ERROR}\nexports P;\nend.").GetDiagnostics(null, TestContext.Current.CancellationToken);
        Assert.Equal(("W1082", DiagnosticSeverity.Error), (promoted.Single().Code, promoted.Single().Severity));
    }

    [Fact]
    [Trait("Req", "LANG-545")]
    [Trait("Req", "CMP-1054")]
    public void NotExportable_OneReasonEach()
    {
        const string source = """
            library L;
            type
              TRec = record A, B, C: Integer; class procedure M; static; end;
            const K = 1;
            var V: Integer;
            procedure Over(A: Integer); overload; begin end;
            procedure Over(A: Double); overload; begin end;
            function Imported: Integer; external 'other.dll';
            function Big: TRec; public name 'big_rec'; begin Result.A := 1; end;
            procedure Outer; procedure Inner; begin end; begin end;
            class procedure TRec.M; begin end;
            exports V, K, TRec, System, TRec.M, Outer.Inner, Over, WriteLn, Imported, Big name 'big_rec', Big, Big name 'other';
            end.
            """;

        var compilation = Compile(source);

        Assert.Equal(
            [
                "L.pas(12,9): E2212 'V' cannot be exported: not a procedure or function",
                "L.pas(12,12): E2212 'K' cannot be exported: not a procedure or function",
                "L.pas(12,15): E2212 'TRec' cannot be exported: not a procedure or function",
                "L.pas(12,21): E2212 'System' cannot be exported: not a procedure or function",
                "L.pas(12,29): E2212 'TRec.M' cannot be exported: it is a method",
                "L.pas(12,37): E2212 'Outer.Inner' cannot be exported: it is a nested routine",
                "L.pas(12,50): E2212 'Over' cannot be exported: it is overloaded",
                "L.pas(12,56): E2212 'WriteLn' cannot be exported: it is a compiler intrinsic",
                "L.pas(12,65): E2212 'Imported' cannot be exported: it is imported (declared external)",
                "L.pas(12,75): E2212 'Big' cannot be exported: the result type needs an export thunk; export it under another name",
            ],
            Diagnostics(compilation));
        // under another name the routine with a public name is exported through a thunk
        Assert.Equal(["Big @1 = L.Big", "other @2 = L.Big"], Listing(compilation));
    }

    [Fact]
    [Trait("Req", "LANG-542")]
    [Trait("Req", "CMP-1056")]
    public void Program_HasNoExports_AndIsAProgramModule()
    {
        var fs = new InMemoryFileSystem().Add(Path.Combine(Dir, "P.pas"), "program P;\nbegin\nend.").AddSystem();
        var compilation = Compilation.Create(new CompilationOptions { MainFile = Path.Combine(Dir, "P.pas"), RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" }, [], fs);

        Assert.Equal(ModuleKind.Program, compilation.MainModuleKind);
        Assert.Empty(compilation.Exports);
    }
}
