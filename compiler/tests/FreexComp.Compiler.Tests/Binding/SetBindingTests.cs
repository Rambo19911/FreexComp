using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Small sets (task C-050: LANG-250…253, LANG-280 rule 8).</summary>
[Trait("Phase", "2")]
public sealed class SetBindingTests
{
    [Fact]
    [Trait("Req", "LANG-250")]
    public void SetType_ElementTypeAndSize()
    {
        var (result, _) = Bind("type E = (A, B, C); S = set of E; D = set of 0..9; begin end.");
        Assert.Empty(result.Diagnostics);

        var s = Assert.IsType<SetTypeSymbol>(result.Declarations.Scope.Lookup("S"));
        var d = Assert.IsType<SetTypeSymbol>(result.Declarations.Scope.Lookup("D"));
        Assert.Same(result.Declarations.Scope.Lookup("E"), s.ElementType);
        Assert.Equal((TypeKind.Set, 1L, 2L), (s.TypeKind, s.Size, d.Size));
    }

    [Fact]
    [Trait("Req", "LANG-251")]
    [Trait("Req", "LANG-252")]
    public void SetConstants_FoldToBitMasks()
    {
        var (result, _) = Bind("""
            type E = (A, B, C);
            const
              U = [1, 3] + [5];
              I = [1..3] * [2..5];
              D = [A..C] - [B];
              Empty = [3..1];
              Member = 3 in [1..5];
              Outside = 300 in [1..5];
              Same = [1, 2] = [2, 1];
              Sub = [1] <= [1, 2];
              Sup = [1] >= [1, 2];
            begin end.
            """);
        Assert.Empty(result.Diagnostics);

        string Value(string name) => ((ConstantSymbol)result.Declarations.Scope.Lookup(name)!).Value!.ToDisplayString();
        Assert.Equal(["42", "12", "5", "0", "True", "False", "True", "True", "False"],
            new[] { "U", "I", "D", "Empty", "Member", "Outside", "Same", "Sub", "Sup" }.Select(Value));
    }

    [Theory]
    [Trait("Req", "LANG-280")]
    [InlineData("type S = set of 0..7; T = set of 0..31; var a: S; b: T; begin a := b; b := a; b := a + b end.")]
    [InlineData("type E = (X, Y, Z); S = set of E; R = set of X..Y; var a: S; b: R; begin a := b; b := a; a := [] end.")]
    [InlineData("type S = set of 0..15; var a: S; i: Integer; ok: Boolean; begin a := [i, i + 1..i + 3]; ok := (i in a) and (a <= [1]) and (a = []) end.")]
    [InlineData("type E = (X, Y); var s: set of E; v: E; begin Include(s, v); Exclude(s, Y) end.")]
    public void Compatible(string text) => Assert.Empty(BindDiagnostics(text));

    [Theory]
    [Trait("Req", "LANG-252")]
    [InlineData("type A = set of (X, Y); B = set of 0..3; var va: A; vb: B; begin va := vb end.", "E2010(1,72): Incompatible types: 'B' and 'A'")]
    [InlineData("type S = set of 0..3; var a, b: S; ok: Boolean; begin ok := a < b end.", "E2011(1,63): Operator '<' not applicable to operand type(s) 'S' and 'S'")]
    [InlineData("type E = (X, Y); var s: set of E; ok: Boolean; begin ok := 1 in s end.", "E2010(1,62): Incompatible types: 'Integer' and 'E'")]
    [InlineData("type E = (X, Y); var s: set of E; begin Include(s, 1) end.", "E2010(1,52): Incompatible types: 'Integer' and 'E'")]
    [InlineData("type S = set of 0..9; var v: S; begin v := [1, 10] end.", "E2012(1,48): Constant value 10 is out of range for type '0..9'")]
    [InlineData("var I: Integer; begin Include(I, 1) end.", "E2014(1,31): Type of expression must be set")]
    public void Incompatible(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));

    [Fact]
    [Trait("Req", "LANG-251")]
    public void Constructor_TakesTheTypeOfItsContext()
    {
        var (result, _) = Bind("type S = set of 0..15; var a: S; i: Integer; begin a := [i]; a := a + [i] end.");
        Assert.Empty(result.Diagnostics);
        var dump = BoundDump.Of(result.Bodies[0]);

        Assert.Contains("(set S", dump);
        Assert.DoesNotContain("set of", dump);
    }

    /// <summary>types.md lead note 2026-11-14: <c>set of Char</c> and wide Char subranges are 256-bit sets without a diagnostic.</summary>
    [Fact]
    [Trait("Req", "LANG-250")]
    public void SetOfChar_IsABigSetOfOrdinals0To255()
    {
        var (result, _) = Bind("""
            type S = set of Char; L = set of 'a'..'z'; W = set of #100..#300; B = set of Byte;
            var vs: S; vw: W; c: Char; ok: Boolean;
            begin vs := ['a'..'z', c, #300]; vw := [c]; ok := (c in vs) and (vs <= ['a']); Include(vs, c); Exclude(vw, #200) end.
            """);
        Assert.Empty(result.Diagnostics);

        SetTypeSymbol Set(string name) => Assert.IsType<SetTypeSymbol>(result.Declarations.Scope.Lookup(name));
        Assert.Equal([(32L, 256, false), (32L, 256, false), (32L, 256, false), (32L, 256, false)],
            new[] { "S", "L", "W", "B" }.Select(n => (Set(n).Size, Set(n).Capacity, Set(n).IsSmall)));
        Assert.Equal(1, Set("S").Alignment);
    }

    [Fact]
    [Trait("Req", "LANG-250")]
    public void ConstantSet_BitsAreTheElementOrdinals()
    {
        var (result, _) = Bind("type E = (A, B, C = 7); const S = [B, C]; begin end.");
        var constant = (ConstantSymbol)result.Declarations.Scope.Lookup("S")!;

        Assert.Equal(new BigInteger(2 + 128), constant.Value!.Value);
        Assert.Equal(1L, constant.Type.Size);
    }
}
