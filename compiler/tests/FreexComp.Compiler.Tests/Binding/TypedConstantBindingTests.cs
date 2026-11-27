using FreexComp.Compiler.Symbols;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Typed constants and initialised globals (task C-054: LANG-344, LANG-522, CMP-341).</summary>
[Trait("Phase", "2")]
public sealed class TypedConstantBindingTests
{
    private static ConstantSymbol Constant(BindingResultView result, string name) =>
        Assert.IsType<ConstantSymbol>(result.Lookup(name));

    private sealed record BindingResultView(Compiler.Binding.BindingResult Result)
    {
        public Symbol? Lookup(string name) => Result.Declarations.Scope.Lookup(name);
    }

    private static BindingResultView BindClean(string text)
    {
        var (result, _) = Bind(text);
        Assert.Empty(Format(result.Diagnostics));
        return new BindingResultView(result);
    }

    [Fact]
    [Trait("Req", "LANG-344")]
    public void TypedConstants_HoldTheirConvertedValueAndAReadOnlyStorage()
    {
        var result = BindClean("""
            type
              TP = record X, Y: Integer; C: Char end;
              TColor = (Red, Green, Blue);
            const
              N: Byte = 7;
              A: array[1..3] of Int64 = (1, 2, 3);
              M: array[0..1, 0..1] of Word = ((1, 2), (3, 4));
              P: TP = (X: 1; Y: -2);
              E: TP = ();
              S: set of 0..9 = [1, 2];
              One: array[1..1] of Integer = (5);
            begin end.
            """);

        Assert.Equal(["7", "(1, 2, 3)", "((1, 2), (3, 4))", "(X: 1; Y: -2)", "()"],
            new[] { "N", "A", "M", "P", "E" }.Select(n => Constant(result, n).Value!.ToDisplayString()));
        var n = Constant(result, "N");
        Assert.True(n.IsTyped);
        Assert.Equal(("Byte", true, true, "7"), (n.Storage!.Type.Name, n.Storage.IsReadOnly, n.Storage.IsGlobal, n.Storage.Initializer!.ToDisplayString()));
        Assert.Equal("(5)", Constant(result, "One").Value!.ToDisplayString());
        Assert.Equal("Integer", Assert.IsType<FreexComp.Compiler.Types.ArrayTypeSymbol>(Constant(result, "One").Type).ElementType.Name);
        Assert.Contains("E2037", BindDiagnostics("type TP = record X, Y: Integer; C: Char end; const L: array[0..2] of TP = ((X: 1), (Y: 2), (C: 'c')); begin end.")[0]);
    }

    [Fact]
    [Trait("Req", "LANG-344")]
    public void TypedConstant_ReferencesBindToItsStorage()
    {
        var (result, _) = Bind("const T: array[1..2] of Integer = (4, 5); var i: Integer; begin i := T[2] + High(T) end.");
        Assert.Empty(result.Diagnostics);

        Assert.Equal("(block (:= i:Integer (Add:Integer ([] T:array[1..2] of Integer 2:Integer):Integer 2:Integer)))", BoundDump.Of(result.Bodies[0]));
    }

    [Fact]
    [Trait("Req", "LANG-522")]
    public void InitialisedGlobals_TakeArrayAndRecordConstants()
    {
        var (result, _) = Bind("type TP = record X, Y: Integer end; var A: array[1..2] of Byte = (1, 2); P: TP = (X: 3); begin end.");
        Assert.Empty(result.Diagnostics);

        string Initializer(string name) => Assert.IsType<VariableSymbol>(result.Declarations.Scope.Lookup(name)).Initializer!.ToDisplayString();
        Assert.Equal(("(1, 2)", "(X: 3)"), (Initializer("A"), Initializer("P")));
        Assert.False(Assert.IsType<VariableSymbol>(result.Declarations.Scope.Lookup("A")).IsReadOnly);
    }

    [Theory]
    [Trait("Req", "LANG-344")]
    [InlineData("const A: array[1..3] of Integer = (1, 2); begin end.", "E2035(1,35): Not enough array elements in constant (expected 3, found 2)")]
    [InlineData("const A: array[1..2] of Integer = (1, 2, 3); begin end.", "E2036(1,35): Too many array elements in constant (expected 2, found 3)")]
    [InlineData("const A: array[1..2] of Integer = (); begin end.", "E2035(1,35): Not enough array elements in constant (expected 2, found 0)")]
    [InlineData("const A: array[1..2] of Integer = (5); begin end.", "E2035(1,35): Not enough array elements in constant (expected 2, found 1)")]
    [InlineData("type T = record X, Y: Integer end; const P: T = (Z: 1); begin end.", "E2037(1,50): Unknown record field 'Z' in constant")]
    [InlineData("type T = record X, Y: Integer end; const P: T = (Y: 1; X: 2); begin end.", "E2037(1,50): Unknown record field 'Y' in constant")]
    [InlineData("type T = record X, Y: Integer end; const P: T = (1, 2); begin end.", "E2017(1,49): Array type required")]
    [InlineData("const B: Integer = (X: 1); begin end.", "E2016(1,20): Record, class or interface type required")]
    [InlineData("const A: array[1..2] of Byte = (1, 300); begin end.", "E2012(1,36): Constant value 300 is out of range for type 'Byte'")]
    [InlineData("type T = record X: Integer end; const P: T = 5; begin end.", "E2010(1,46): Incompatible types: 'Integer' and 'T'")]
    [InlineData("const A: Integer = 1; B = A + 1; begin end.", "E2050(1,27): Constant expression expected")]
    [InlineData("const A: Integer = 1; begin A := 2 end.", "E2053(1,29): Left side cannot be assigned to")]
    [InlineData("const A: array[1..2] of Integer = (1, 2); begin A[1] := 2 end.", "E2053(1,49): Left side cannot be assigned to")]
    [InlineData("type T = record X: Integer end; const P: T = (X: 1); begin P.X := 2 end.", "E2053(1,60): Left side cannot be assigned to")]
    [InlineData("const A: Integer = 1; begin Inc(A) end.", "E2112(1,33): Constant object cannot be passed as var parameter")]
    [InlineData("const A: Integer = 1; procedure P(var x: Integer); begin end; begin P(A) end.", "E2112(1,71): Constant object cannot be passed as var parameter")]
    [InlineData("var D: array of Integer = nil; begin end.", "E2900(1,8): Feature not supported: initialised array of Integer")]   // strings: C-203; dynamic-array constants: LANG-274 (C-389)
    public void Errors(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));

    /// <summary>C-203: typed constants (and initialised variables) of strings and of arrays/records of strings bind their literals.</summary>
    [Theory]
    [Trait("Req", "LANG-344")]
    [InlineData("const S: string = 'x'; A: array[0..1] of string = ('a', ''); R: record N: AnsiString end = (N: 'n'); var G: string = 'g'; begin end.")]
    [InlineData("procedure P; const L: array[1..2] of string = ('p', 'q'); begin Writeln(L[1]) end; begin end.")]
    public void ManagedStringConstants_Bind(string text) => Assert.Empty(BindDiagnostics(text));

    [Theory]
    [Trait("Req", "LANG-344")]
    [InlineData("procedure P; const T: array[0..1] of Char = ('a', 'b'); var c: Char; begin c := T[1] end; begin P end.")]
    [InlineData("const A: Integer = 1; procedure P(x: Integer; const y: Integer); begin end; begin P(A, A) end.")]
    [InlineData("type T = array[1..2] of Integer; const A: T = (1, 2); var B: T; begin B := A end.")]
    [InlineData("const R: record a: Byte; b: Word end = (a: 1; b: 2); var x: Integer; begin x := R.a + SizeOf(R) end.")]
    public void Valid(string text) => Assert.Empty(BindDiagnostics(text));

    // ---- C-389 (F10-024 part 2): typed dynamic-array constants (LANG-274) ----

    private const string DynTypes = "type TInts = array of Integer; TGrid = array of TInts; TStrs = array of string; TPair = record N: Integer; S: string end; ";

    /// <summary>The one diagnostic of <paramref name="text"/>: its code, the text from its column on, its message.</summary>
    private static string DiagnosticAt(string text)
    {
        var diagnostic = Assert.Single(BindDiagnostics(text));
        var match = System.Text.RegularExpressions.Regex.Match(diagnostic, @"^(\w+)\(1,(\d+)\): (.*)$");
        Assert.True(match.Success, diagnostic);
        var column = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        return match.Groups[1].Value + " at '" + text[(column - 1)..] + "': " + match.Groups[3].Value;
    }

    /// <summary>
    /// LANG-274: a bracket constructor of constant elements (converted to the element type; nested constructors for nested arrays,
    /// record constants for record elements) at unit, routine and inline level; <c>[]</c> (and <c>nil</c>) is the empty array.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-274")]
    [InlineData("const Primes: array of Integer = [2, 3, 5, 7];", "Primes", "[2, 3, 5, 7]")]
    [InlineData("const Reals: array of Double = [1, 2.5];", "Reals", "[1.0, 2.5]")]
    [InlineData("const Names: TStrs = ['a', '', 'bc'];", "Names", "['a', '', 'bc']")]
    [InlineData("const Pairs: array of TPair = [(N: 1; S: 'x'), (N: 2)];", "Pairs", "[(N: 1; S: 'x'), (N: 2)]")]
    [InlineData("const Grid: TGrid = [[1], [2, 3], []];", "Grid", "[[1], [2, 3], nil]")]
    [InlineData("const None: TInts = [];", "None", "nil")]
    [InlineData("const Nothing: TInts = nil;", "Nothing", "nil")]
    [InlineData("procedure P; const L: TInts = [1, 2]; var a: TInts; begin a := L; Writeln(L[1], Length(L)) end;", "", "")]
    [InlineData("procedure P; begin const C: TInts = [4, 5]; const G: TGrid = [[1], []]; Writeln(C[0], G[0][0], High(C)); for var X in C do Writeln(X) end;", "", "")]
    public void DynArrayConstant_Accepted(string declarations, string name, string expected)
    {
        var result = BindClean(DynTypes + declarations + " begin end.");
        if (name.Length == 0)
        {
            return;
        }

        var constant = Constant(result, name);
        Assert.True(constant.IsTyped);
        Assert.Equal(expected, constant.Value!.ToDisplayString());
        Assert.Equal((true, true, expected), (constant.Storage!.IsReadOnly, constant.Storage.IsGlobal, constant.Storage.Initializer!.ToDisplayString()));
    }

    /// <summary>
    /// LANG-274 rule 1: the constant is read-only at every depth of indexing (E2053); it or an element as a <c>var</c> argument,
    /// also of <c>Inc</c>, <c>SetLength</c>, <c>Insert</c>, <c>Delete</c>, is E2112. Routine-level and inline constants alike.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-274")]
    [InlineData("Primes := nil", "E2053 at 'Primes := nil end; begin end.': Left side cannot be assigned to")]
    [InlineData("Primes[0] := 1", "E2053 at 'Primes[0] := 1 end; begin end.': Left side cannot be assigned to")]
    [InlineData("Grid[1][0] := 1", "E2053 at 'Grid[1][0] := 1 end; begin end.': Left side cannot be assigned to")]
    [InlineData("Grid[1, 0] := 1", "E2053 at 'Grid[1, 0] := 1 end; begin end.': Left side cannot be assigned to")]
    [InlineData("Grid[0] := nil", "E2053 at 'Grid[0] := nil end; begin end.': Left side cannot be assigned to")]
    [InlineData("V(Primes)", "E2112 at 'Primes) end; begin end.': Constant object cannot be passed as var parameter")]
    [InlineData("W(Grid[0][1])", "E2112 at 'Grid[0][1]) end; begin end.': Constant object cannot be passed as var parameter")]
    [InlineData("Inc(Primes[0])", "E2112 at 'Primes[0]) end; begin end.': Constant object cannot be passed as var parameter")]
    [InlineData("SetLength(Primes, 1)", "E2112 at 'Primes, 1) end; begin end.': Constant object cannot be passed as var parameter")]
    [InlineData("SetLength(Grid[0], 1)", "E2112 at 'Grid[0], 1) end; begin end.': Constant object cannot be passed as var parameter")]
    [InlineData("Insert(1, Primes, 0)", "E2112 at 'Primes, 0) end; begin end.': Constant object cannot be passed as var parameter")]
    [InlineData("Delete(Primes, 0, 1)", "E2112 at 'Primes, 0, 1) end; begin end.': Constant object cannot be passed as var parameter")]
    [InlineData("L[0] := 1", "E2053 at 'L[0] := 1 end; begin end.': Left side cannot be assigned to")]
    [InlineData("begin const C: TInts = [1]; C[0] := 2 end", "E2053 at 'C[0] := 2 end end; begin end.': Left side cannot be assigned to")]
    [InlineData("begin const C: TGrid = [[1]]; SetLength(C[0], 2) end", "E2112 at 'C[0], 2) end end; begin end.': Constant object cannot be passed as var parameter")]
    public void DynArrayConstant_ReadOnly(string statement, string expected) =>
        Assert.Equal(expected, DiagnosticAt(DynTypes + "const Primes: TInts = [2, 3]; Grid: TGrid = [[1, 2]]; procedure V(var A: TInts); begin end; " +
            "procedure W(var X: Integer); begin end; procedure P; const L: TInts = [1]; begin " + statement + " end; begin end."));

    /// <summary>LANG-274: other managed element types (interfaces, records with dynamic-array fields) and initialised dynamic-array variables stay E2900.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-274")]
    [InlineData("type II = interface end; const I: array of II = [];", "E2900 at 'array of II = []; begin end.': Feature not supported: initialised array of II")]
    [InlineData("type TR = record A: TInts end; const R: array of TR = [];", "E2900 at 'array of TR = []; begin end.': Feature not supported: initialised array of TR")]
    [InlineData("type TR = record A: TInts end; const R: TR = ();", "E2900 at 'TR = (); begin end.': Feature not supported: initialised TR")]
    [InlineData("const S: array[0..0] of TInts = ([1]);", "E2900 at 'array[0..0] of TInts = ([1]); begin end.': Feature not supported: initialised array[0..0] of TInts")]
    [InlineData("var G: TInts = [1];", "E2900 at 'TInts = [1]; begin end.': Feature not supported: initialised TInts")]
    public void DynArrayConstant_ManagedElement_E2900(string declarations, string expected) =>
        Assert.Equal(expected, DiagnosticAt(DynTypes + declarations + " begin end."));

    /// <summary>LANG-274: the parenthesised aggregate form of static typed constants is not a dynamic-array constant (E2010); a parenthesised value is converted as usual.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-274")]
    [InlineData("const P: TInts = (1, 2);", "E2010 at '(1, 2); begin end.': Incompatible types: 'array constant' and 'TInts'")]
    [InlineData("const P: TGrid = [(1, 2)];", "E2010 at '(1, 2)]; begin end.': Incompatible types: 'array constant' and 'TInts'")]
    [InlineData("const P: TInts = (1);", "E2010 at '(1); begin end.': Incompatible types: 'Integer' and 'TInts'")]
    public void DynArrayConstant_ParenthesisedForm_E2010(string declarations, string expected) =>
        Assert.Equal(expected, DiagnosticAt(DynTypes + declarations + " begin end."));
}
