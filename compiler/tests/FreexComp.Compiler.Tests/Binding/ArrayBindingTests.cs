using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Static arrays (task C-052: LANG-255…258, ABI-062).</summary>
[Trait("Phase", "2")]
public sealed class ArrayBindingTests
{
    [Fact]
    [Trait("Req", "LANG-255")]
    [Trait("Req", "ABI-062")]
    public void ArrayTypes_SizeBoundsAndNesting()
    {
        var (result, _) = Bind("""
            type
              TColor = (Red, Green, Blue);
              A = array[1..10] of Integer;
              M = array[0..2, TColor] of Byte;
              B = array[Boolean] of Int64;
              C = packed array[Char] of Boolean;
            begin end.
            """);
        Assert.Empty(result.Diagnostics);

        ArrayTypeSymbol Array(string name) => Assert.IsType<ArrayTypeSymbol>(result.Declarations.Scope.Lookup(name));
        Assert.Equal((TypeKind.Array, 40L, 4, 10L), (Array("A").TypeKind, Array("A").Size, Array("A").Alignment, Array("A").Length));
        Assert.Equal((9L, 1, 3L), (Array("M").Size, Array("M").Alignment, Array("M").Length));
        var row = Assert.IsType<ArrayTypeSymbol>(Array("M").ElementType);
        Assert.Equal(("array[TColor] of Byte", 3L, 3L), (row.Name, row.Size, row.Length));
        Assert.Same(result.Declarations.Scope.Lookup("TColor"), row.IndexType);
        Assert.Equal((16L, 8), (Array("B").Size, Array("B").Alignment));
        Assert.Equal(65536L, Array("C").Size);
    }

    [Fact]
    [Trait("Req", "LANG-255")]
    public void LowHighLengthSizeOf_AreConstants()
    {
        var (result, _) = Bind("""
            type TColor = (Red, Green, Blue);
            var a: array[-2..7] of Integer; e: array[Green..Blue] of Char; f: array[Boolean] of Byte;
            const
              L = Low(a); H = High(a); N = Length(a); S = SizeOf(a);
              EL = Low(e); EN = Length(e); FH = High(f); SL = Length('abc');
            begin end.
            """);
        Assert.Empty(result.Diagnostics);

        string Value(string name) => ((ConstantSymbol)result.Declarations.Scope.Lookup(name)!).Value!.ToDisplayString();
        Assert.Equal(["-2", "7", "10", "40", "1", "2", "True", "3"], new[] { "L", "H", "N", "S", "EL", "EN", "FH", "SL" }.Select(Value));
        Assert.Equal("Green..Blue", ((ConstantSymbol)result.Declarations.Scope.Lookup("EL")!).Type.Name);
    }

    [Fact]
    [Trait("Req", "LANG-255")]
    public void MultipleIndexes_AreNestedElementAccesses()
    {
        var (result, _) = Bind("var m: array[1..2, 1..3] of Integer; i: Integer; begin m[i, 2] := 1; m[i][2] := m[1, i] end.");
        Assert.Empty(result.Diagnostics);
        var dump = BoundDump.Of(result.Bodies[0]);

        Assert.Equal(
            "(block (:= ([] ([] m:array[1..2, 1..3] of Integer i:Integer):array[1..3] of Integer 2:Integer):Integer 1:Integer) " +
            "(:= ([] ([] m:array[1..2, 1..3] of Integer i:Integer):array[1..3] of Integer 2:Integer):Integer " +
            "([] ([] m:array[1..2, 1..3] of Integer 1:Integer):array[1..3] of Integer i:Integer):Integer))", dump);
    }

    [Theory]
    [Trait("Req", "LANG-256")]
    [InlineData("var a, b: array[1..3] of Integer; begin a := b end.")]
    [InlineData("type T = array[1..3] of Integer; var a: T; b: T; begin a := b; a[1] := b[3] end.")]
    [InlineData("type TColor = (Red, Green); var a: array[TColor] of Integer; c: TColor; begin a[c] := a[Green] + a[Red] end.")]
    [InlineData("var a: array['a'..'z'] of Integer; c: Char; b: Byte; begin a[c] := 1; Inc(a['q']); a[c] := a[Chr(b)] end.")]
    [InlineData("var a: array[0..3] of Byte; w: Word; i: Int64; begin a[w] := a[i] end.")]
    [InlineData("var a: array[1..3] of Integer; procedure P(var x: Integer); begin end; begin P(a[2]) end.")]
    public void Compatible(string text) => Assert.Empty(BindDiagnostics(text));

    [Theory]
    [Trait("Req", "LANG-207")]
    [Trait("Req", "LANG-258")]
    [InlineData("var a: array[1..3] of Integer; b: array[1..3] of Integer; begin a := b end.",
        "E2010(1,70): Incompatible types: 'array[1..3] of Integer' and 'array[1..3] of Integer'")]
    [InlineData("var a: array[1..3] of Integer; begin a[4] := 0 end.", "E2032(1,40): Constant index 4 is out of bounds (1..3)")]
    [InlineData("var a: array['a'..'c'] of Integer; begin a['d'] := 0 end.", "E2032(1,44): Constant index 'd' is out of bounds ('a'..'c')")]
    [InlineData("type E = (X, Y); var a: array[E] of Integer; begin a[1] := 0 end.", "E2010(1,54): Incompatible types: 'Integer' and 'E'")]
    [InlineData("var i: Integer; begin i[1] := 0 end.", "E2017(1,23): Array type required")]
    [InlineData("var a: array[1..3] of Integer; begin a[1, 2] := 0 end.", "E2019(1,43): Too many indexes for 'a'")]
    [InlineData("var a: array[Integer] of Byte; begin end.", "E2031(1,8): Data structure too large")]
    [InlineData("var a: array[0..$FFFFFFF] of Int64; begin end.", "E2031(1,8): Data structure too large")]
    [InlineData("var a: array[Single] of Byte; begin end.", "E2013(1,14): Ordinal type required")]
    public void Incompatible(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));

    [Fact]
    [Trait("Req", "LANG-256")]
    public void ElementOfAConstParameter_CannotBeAssigned()
    {
        Assert.Equal(["E2053(1,65): Left side cannot be assigned to"],
            BindDiagnostics("type T = array[1..2] of Integer; procedure P(const a: T); begin a[1] := 0 end; begin end."));
    }
}
