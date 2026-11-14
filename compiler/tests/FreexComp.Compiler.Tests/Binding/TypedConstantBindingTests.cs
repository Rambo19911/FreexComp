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
    [InlineData("const S: string = 'x'; begin end.", "E2900(1,10): Feature not supported: initialised string")]
    public void Errors(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));

    [Theory]
    [Trait("Req", "LANG-344")]
    [InlineData("procedure P; const T: array[0..1] of Char = ('a', 'b'); var c: Char; begin c := T[1] end; begin P end.")]
    [InlineData("const A: Integer = 1; procedure P(x: Integer; const y: Integer); begin end; begin P(A, A) end.")]
    [InlineData("type T = array[1..2] of Integer; const A: T = (1, 2); var B: T; begin B := A end.")]
    [InlineData("const R: record a: Byte; b: Word end = (a: 1; b: 2); var x: Integer; begin x := R.a + SizeOf(R) end.")]
    public void Valid(string text) => Assert.Empty(BindDiagnostics(text));
}
