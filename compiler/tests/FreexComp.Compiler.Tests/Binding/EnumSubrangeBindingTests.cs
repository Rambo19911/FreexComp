using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Type sections with enumerations and subranges (task C-049: LANG-205, 210…213, 280 rules 4/5, 303).</summary>
[Trait("Phase", "2")]
public sealed class EnumSubrangeBindingTests
{
    private static T TypeOf<T>(string text, string name) where T : TypeSymbol
    {
        var (result, _) = Bind(text);
        Assert.Empty(result.Diagnostics);
        return Assert.IsType<T>(result.Declarations.Scope.Lookup(name));
    }

    [Fact]
    [Trait("Req", "LANG-210")]
    public void Enum_ImplicitAndExplicitOrdinals_LowIsFirstHighIsLast()
    {
        var type = TypeOf<EnumTypeSymbol>("type T = (A, B = 5, C, D = 9); begin end.", "T");

        Assert.Equal(["A=0", "B=5", "C=6", "D=9"], type.EnumMembers.Select(m => $"{m.Name}={m.Ordinal}"));
        Assert.Equal((BigInteger.Zero, new BigInteger(9)), (type.Low, type.High));
        Assert.Equal((TypeKind.Enum, false), (type.TypeKind, type.IsScoped));
    }

    [Fact]
    [Trait("Req", "LANG-210")]
    public void Enum_ElementsAreConstantsOfTheEnumInTheEnclosingScope()
    {
        var (result, _) = Bind("type T = (A, B); begin end.");

        var member = Assert.IsType<EnumMemberSymbol>(result.Declarations.Scope.Lookup("b"));
        Assert.Same(result.Declarations.Scope.Lookup("T"), member.Type);
        Assert.Equal(1, member.Ordinal);
    }

    [Fact]
    [Trait("Req", "LANG-210")]
    public void ScopedEnum_ElementsAreOnlyReachableThroughTheType()
    {
        var (result, _) = Bind("{$SCOPEDENUMS ON} type T = (A, B); {$SCOPEDENUMS OFF} const A = 7; var X: T; begin X := T.B end.");

        Assert.Empty(result.Diagnostics);
        Assert.IsType<ConstantSymbol>(result.Declarations.Scope.Lookup("A"));
        Assert.True(Assert.IsType<EnumTypeSymbol>(result.Declarations.Scope.Lookup("T")).IsScoped);
    }

    [Fact]
    [Trait("Req", "LANG-205")]
    public void Alias_IsTheSameType()
    {
        var (result, _) = Bind("type T = (A); U = T; V = Integer; begin end.");

        Assert.Same(result.Declarations.Scope.Lookup("T"), result.Declarations.Scope.Lookup("U"));
        Assert.Same(result.Types.Integer, result.Declarations.Scope.Lookup("V"));
    }

    [Theory]
    [Trait("Req", "LANG-212")]
    [InlineData("0..100", "Byte")]
    [InlineData("-1..100", "ShortInt")]
    [InlineData("0..200", "Byte")]
    [InlineData("-1..200", "SmallInt")]
    [InlineData("0..65535", "Word")]
    [InlineData("-40000..40000", "Integer")]
    [InlineData("0..3000000000", "Cardinal")]
    [InlineData("-1..3000000000", "Int64")]
    [InlineData("0..5000000000", "UInt64")]
    public void IntegerSubrange_RepresentationFollowsLang212(string range, string representation)
    {
        var type = TypeOf<IntegerTypeSymbol>($"type T = {range}; begin end.", "T");

        Assert.Equal((TypeKind.Subrange, representation), (type.TypeKind, type.BaseType!.Name));
        Assert.Equal(range, $"{type.Min}..{type.Max}");
    }

    [Fact]
    [Trait("Req", "LANG-212")]
    public void EnumAndCharSubranges_UseTheHostRepresentation()
    {
        var (result, _) = Bind("type E = (A, B, C, D); S = B..C; L = 'a'..'z'; begin end.");
        Assert.Empty(result.Diagnostics);

        var s = Assert.IsType<EnumTypeSymbol>(result.Declarations.Scope.Lookup("S"));
        var l = Assert.IsType<CharTypeSymbol>(result.Declarations.Scope.Lookup("L"));
        Assert.Equal((TypeKind.Subrange, 1L, "E", 1, 2), (s.TypeKind, s.Size, s.BaseType!.Name, (int)s.Low, (int)s.High));
        Assert.Equal((TypeKind.Subrange, 2L, "Char", 'a', 'z'), (l.TypeKind, l.Size, l.BaseType!.Name, (char)l.MinValue, (char)l.MaxValue));
    }

    [Theory]
    [Trait("Req", "LANG-280")]
    [InlineData("type E = (A, B, C); S = B..C; var X: E; Y: S; begin X := Y; Y := X end.")]
    [InlineData("type L = 'a'..'z'; var C: Char; X: L; begin C := X; X := C end.")]
    [InlineData("type P = 1..10; var I: Integer; X: P; begin I := X + 1; X := I end.")]
    public void Subranges_AreAssignmentCompatibleWithTheirHost(string text) => Assert.Empty(BindDiagnostics(text));

    [Theory]
    [Trait("Req", "LANG-280")]
    [InlineData("type E = (A, B); F = (C, D); var X: E; begin X := C end.", "E2010(1,51): Incompatible types: 'F' and 'E'")]
    [InlineData("type E = (A, B); var X: E; begin X := 0 end.", "E2010(1,39): Incompatible types: 'Integer' and 'E'")]
    [InlineData("type E = (A, B); var I: Integer; begin I := A end.", "E2010(1,45): Incompatible types: 'E' and 'Integer'")]
    [InlineData("type E = (A, B, C); S = B..C; var X: S; begin X := A end.", "E2012(1,52): Constant value 0 is out of range for type 'S'")]
    [InlineData("type L = 'a'..'z'; var X: L; begin X := 'A' end.", "E2012(1,41): Constant value 'A' is out of range for type 'L'")]
    [InlineData("type E = (A, B); F = (C, D); var X: E; Y: F; Z: Boolean; begin Z := X < Y end.", "E2010(1,71): Incompatible types: 'E' and 'F'")]
    [InlineData("type E = (A, B); var X: E; begin X := X + 1 end.", "E2011(1,41): Operator '+' not applicable to operand type(s) 'E' and 'Integer'")]
    public void Incompatible(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));

    [Fact]
    [Trait("Req", "LANG-213")]
    public void OrdinalIntrinsics_FoldOnEnumConstants()
    {
        var (result, _) = Bind("type E = (A, B = 4, C); const Lo = Ord(Low(E)); Hi = Ord(High(E)); N = Ord(Succ(B)); S = SizeOf(E); X = E(4); begin end.");
        Assert.Empty(result.Diagnostics);

        string Value(string name) => ((ConstantSymbol)result.Declarations.Scope.Lookup(name)!).Value!.ToDisplayString();
        Assert.Equal(["0", "5", "5", "1", "4"], new[] { "Lo", "Hi", "N", "S", "X" }.Select(Value));
    }

    [Fact]
    [Trait("Req", "LANG-210")]
    public void EnumSize_FollowsTheHighestOrdinalAndMinEnumSize()
    {
        var (result, _) = Bind("type A = (A1, A2 = 255); B = (B1 = 256); C = (C1 = 65536); {$Z2} D = (D1); {$MINENUMSIZE 4} E = (E1); begin end.");
        Assert.Empty(result.Diagnostics);

        Assert.Equal([1L, 2L, 4L, 2L, 4L], new[] { "A", "B", "C", "D", "E" }.Select(n => ((TypeSymbol)result.Declarations.Scope.Lookup(n)!).Size));
    }
}
