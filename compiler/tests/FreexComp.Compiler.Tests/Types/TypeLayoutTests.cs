using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Tests.Types;

/// <summary>Scalar type layouts: size, alignment and LLVM storage type (rtl-abi.md §2, codegen.md CMP-305).</summary>
[Trait("Phase", "2")]
[Trait("Req", "CMP-305")]
public sealed class TypeLayoutTests
{
    private static readonly BuiltInTypes Types = new();

    private static TypeSymbol Type(string name) => Assert.IsAssignableFrom<TypeSymbol>(Types.Scope.LookupLocal(name));

    /// <summary>Every row of the rtl-abi.md §2 table that names a predeclared scalar type (aliases included).</summary>
    [Theory]
    [InlineData("ShortInt", 1, 1, "i8")]
    [InlineData("Byte", 1, 1, "i8")]
    [InlineData("SmallInt", 2, 2, "i16")]
    [InlineData("Word", 2, 2, "i16")]
    [InlineData("Integer", 4, 4, "i32")]
    [InlineData("LongInt", 4, 4, "i32")]
    [InlineData("Cardinal", 4, 4, "i32")]
    [InlineData("LongWord", 4, 4, "i32")]
    [InlineData("Int64", 8, 8, "i64")]
    [InlineData("UInt64", 8, 8, "i64")]
    [InlineData("NativeInt", 8, 8, "i64")]
    [InlineData("NativeUInt", 8, 8, "i64")]
    [InlineData("Boolean", 1, 1, "i8")]
    [InlineData("ByteBool", 1, 1, "i8")]
    [InlineData("WordBool", 2, 2, "i16")]
    [InlineData("LongBool", 4, 4, "i32")]
    [InlineData("Char", 2, 2, "i16")]
    [InlineData("WideChar", 2, 2, "i16")]
    [InlineData("AnsiChar", 1, 1, "i8")]
    [InlineData("Single", 4, 4, "float")]
    [InlineData("Double", 8, 8, "double")]
    [InlineData("Real", 8, 8, "double")]
    [InlineData("Extended", 8, 8, "double")]
    [InlineData("Currency", 8, 8, "i64")]
    [InlineData("Pointer", 8, 8, "ptr")]
    [InlineData("PChar", 8, 8, "ptr")]
    [InlineData("PWideChar", 8, 8, "ptr")]
    [InlineData("PAnsiChar", 8, 8, "ptr")]
    [InlineData("string", 8, 8, "ptr")]
    [InlineData("UnicodeString", 8, 8, "ptr")]
    [InlineData("AnsiString", 8, 8, "ptr")]
    [InlineData("RawByteString", 8, 8, "ptr")]
    [InlineData("UTF8String", 8, 8, "ptr")]
    [InlineData("ShortString", 256, 1, "[256 x i8]")]
    [InlineData("Text", 624, 8, "[624 x i8]")]
    [InlineData("TextFile", 624, 8, "[624 x i8]")]
    public void Scalars_MatchTheRtlAbiTable(string name, long size, int alignment, string llvm)
    {
        var layout = TypeLayout.Of(Type(name));

        Assert.Equal((size, alignment, llvm), (layout.Size, layout.Alignment, Describe(layout.StorageType)));
    }

    /// <summary>The layout agrees with the size and alignment every predeclared type symbol reports.</summary>
    [Fact]
    public void Scalars_AgreeWithTypeSymbolSizes()
    {
        var types = Types.Scope.Symbols.OfType<TypeSymbol>().Distinct().ToList();
        Assert.NotEmpty(types);
        Assert.All(types, t => Assert.Equal((t.Size, t.Alignment), (TypeLayout.Of(t).Size, TypeLayout.Of(t).Alignment)));
    }

    /// <summary>rtl-abi.md §2 enumeration and subrange rows (C-049): size = alignment, stored as iN.</summary>
    [Theory]
    [Trait("Req", "LANG-212")]
    [InlineData("(A, B)", 1, "i8")]
    [InlineData("(A, B = 256)", 2, "i16")]
    [InlineData("(A, B = 65536)", 4, "i32")]
    [InlineData("-1..200", 2, "i16")]
    [InlineData("0..3000000000", 4, "i32")]
    [InlineData("-1..3000000000", 8, "i64")]
    [InlineData("'a'..'z'", 2, "i16")]
    public void EnumsAndSubranges_MatchTheRtlAbiTable(string definition, long size, string llvm)
    {
        var (result, _) = Binding.BinderTestHelper.Bind($"type T = {definition}; begin end.");
        var layout = TypeLayout.Of(Assert.IsAssignableFrom<TypeSymbol>(result.Declarations.Scope.Lookup("T")));

        Assert.Equal((size, (int)size, llvm), (layout.Size, layout.Alignment, Describe(layout.StorageType)));
    }

    /// <summary>rtl-abi.md §5 (C-050): N = Ord(High) + 1 bits; small sets are iN with alignment = size, big sets [32 x i8] align 1.</summary>
    [Theory]
    [Trait("Req", "LANG-250")]
    [InlineData("set of 0..7", 1, 1, "i8")]
    [InlineData("set of 5..7", 1, 1, "i8")]
    [InlineData("set of 0..8", 2, 2, "i16")]
    [InlineData("set of 0..16", 4, 4, "i32")]
    [InlineData("set of 0..32", 8, 8, "i64")]
    [InlineData("set of 0..63", 8, 8, "i64")]
    [InlineData("set of 0..64", 32, 1, "[32 x i8]")]
    [InlineData("set of Byte", 32, 1, "[32 x i8]")]
    [InlineData("set of Boolean", 1, 1, "i8")]
    public void Sets_MatchTheRtlAbiTable(string definition, long size, int alignment, string llvm)
    {
        var (result, _) = Binding.BinderTestHelper.Bind($"type T = {definition}; begin end.");
        Assert.Empty(result.Diagnostics);
        var layout = TypeLayout.Of(Assert.IsAssignableFrom<TypeSymbol>(result.Declarations.Scope.Lookup("T")));

        Assert.Equal((size, alignment, llvm), (layout.Size, layout.Alignment, Describe(layout.StorageType)));
    }

    [Fact]
    public void Scalars_ErrorTypeHasNoLayout() => Assert.Throws<InvalidOperationException>(() => TypeLayout.Of(Types.Error));

    /// <summary>Structural description of the storage type, independent of the text writer.</summary>
    private static string Describe(IrType type) => type switch
    {
        IntegerIrType i => "i" + i.Bits,
        FloatingIrType { Kind: FloatingKind.Float } => "float",
        FloatingIrType { Kind: FloatingKind.Double } => "double",
        PointerIrType => "ptr",
        ArrayIrType a => $"[{a.Length} x {Describe(a.Element)}]",
        _ => throw new InvalidOperationException(type.ToString()),
    };
}
