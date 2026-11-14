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

    /// <summary>
    /// C-053, ABI-060…064 (hand-computed Win64 C layouts): field offsets, size, alignment and the CMP-306 storage type — a
    /// named struct, plain when LLVM's natural layout gives the ABI offsets, else packed with explicit <c>[N x i8]</c> padding.
    /// The directive in front of <c>record</c> sets the packing.
    /// </summary>
    [Theory]
    [Trait("Req", "ABI-060")]
    [Trait("Req", "ABI-063")]
    [Trait("Req", "ABI-064")]
    [Trait("Req", "CMP-306")]
    [InlineData("record a: Byte; b: Integer; c: Word end", "0 4 8", 12, 4, "%test.T = { i8, i32, i16 }")]
    [InlineData("record a: Byte; d: Double; c: Char end", "0 8 16", 24, 8, "%test.T = { i8, double, i16 }")]
    [InlineData("record a, b: Byte; c: Int64; d: Byte end", "0 1 8 16", 24, 8, "%test.T = { i8, i8, i64, i8 }")]
    [InlineData("record p: Pointer; s: set of 0..9; c: AnsiChar end", "0 8 10", 16, 8, "%test.T = { ptr, i16, i8 }")]
    [InlineData("record a: Byte; v: array[0..2] of Word end", "0 2", 8, 2, "%test.T = { i8, [3 x i16] }")]
    [InlineData("record end", "", 0, 1, "%test.T = {}")]
    [InlineData("packed record a: Byte; b: Integer; c: Word end", "0 1 5", 7, 1, "%test.T = <{ i8, i32, i16 }>")]
    [InlineData("{$A1} record a: Byte; b: Integer end", "0 1", 5, 1, "%test.T = <{ i8, i32 }>")]
    [InlineData("{$A-} record a: Byte; b: Integer end", "0 1", 5, 1, "%test.T = <{ i8, i32 }>")]
    [InlineData("{$A2} record a: Byte; b: Integer; c: Byte end", "0 2 6", 8, 2, "%test.T = <{ i8, [1 x i8], i32, i8, [1 x i8] }>")]
    [InlineData("{$A4} record a: Byte; b: Int64 end", "0 4", 12, 4, "%test.T = <{ i8, [3 x i8], i64 }>")]
    [InlineData("{$ALIGN 4} record a: Integer; b: Int64; c: Word end", "0 4 12", 16, 4, "%test.T = <{ i32, i64, i16, [2 x i8] }>")]
    [InlineData("{$A4} record a: Byte; b: Word end", "0 2", 4, 2, "%test.T = { i8, i16 }")]
    public void Records_FollowTheWin64CLayout(string definition, string offsets, long size, int alignment, string llvm)
    {
        var (result, _) = Binding.BinderTestHelper.Bind($"type T = {definition}; begin end.");
        Assert.Empty(result.Diagnostics);
        var record = Assert.IsType<RecordTypeSymbol>(result.Declarations.Scope.Lookup("T"));
        var layout = TypeLayout.Of(record);

        Assert.Equal((offsets, size, alignment, llvm),
            (string.Join(" ", record.Fields.Select(f => f.Offset)), layout.Size, layout.Alignment, Describe(layout.StorageType, body: true)));
        Assert.Equal((record.Size, record.Alignment), (layout.Size, layout.Alignment));
    }

    /// <summary>
    /// Nested records keep their own alignment (ABI-064); a packed record places them at any byte; a record whose LLVM form is
    /// packed (alignment 1 in LLVM) but has ABI alignment 4 is placed at offset 4 through explicit padding.
    /// </summary>
    [Theory]
    [Trait("Req", "ABI-060")]
    [Trait("Req", "ABI-064")]
    [Trait("Req", "CMP-306")]
    [InlineData("record b: Byte; a: TA; c: Char end", "0 4 16", 20, 4, "%test.T = { i8, %test.TA, i16 }")]
    [InlineData("packed record b: Byte; a: TA end", "0 1", 13, 1, "%test.T = <{ i8, %test.TA }>")]
    [InlineData("record c: Byte; i: T4 end", "0 4", 16, 4, "%test.T = <{ i8, [3 x i8], %test.T4 }>")]
    [InlineData("record x: array[1..2] of TA; y: Byte end", "0 24", 28, 4, "%test.T = { [2 x %test.TA], i8 }")]
    public void Records_NestedRecordsKeepTheirAlignment(string definition, string offsets, long size, int alignment, string llvm)
    {
        var (result, _) = Binding.BinderTestHelper.Bind($$"""
            type
              TA = record b: Byte; i: Integer; w: Word end;
            {$A4}
              T4 = record b: Byte; d: Int64 end;
            {$A8}
              T = {{definition}};
            begin end.
            """);
        Assert.Empty(result.Diagnostics);
        var record = Assert.IsType<RecordTypeSymbol>(result.Declarations.Scope.Lookup("T"));
        var layout = TypeLayout.Of(record);

        Assert.Equal((offsets, size, alignment, llvm),
            (string.Join(" ", record.Fields.Select(f => f.Offset)), layout.Size, layout.Alignment, Describe(layout.StorageType, body: true)));
    }

    /// <summary>An anonymous record (no type declaration) gets a literal struct type (CMP-306 names declared records only).</summary>
    [Fact]
    [Trait("Req", "CMP-306")]
    public void Records_AnonymousRecordIsALiteralStruct()
    {
        var (result, _) = Binding.BinderTestHelper.Bind("var R: record a: Byte; b: Int64 end; begin end.");
        Assert.Empty(result.Diagnostics);
        var variable = Assert.IsType<FreexComp.Compiler.Symbols.VariableSymbol>(result.Declarations.Scope.Lookup("R"));

        Assert.Equal("{ i8, i64 }", Describe(TypeLayout.Of(variable.Type).StorageType));
    }

    /// <summary>Structural description of the storage type, independent of the text writer.</summary>
    private static string Describe(IrType type, bool body = false) => type switch
    {
        IntegerIrType i => "i" + i.Bits,
        FloatingIrType { Kind: FloatingKind.Float } => "float",
        FloatingIrType { Kind: FloatingKind.Double } => "double",
        PointerIrType => "ptr",
        ArrayIrType a => $"[{a.Length} x {Describe(a.Element)}]",
        StructIrType { Fields.IsEmpty: true, IsPacked: false } => "{}",
        StructIrType s => (s.IsPacked ? "<{ " : "{ ") + string.Join(", ", s.Fields.Select(f => Describe(f))) + (s.IsPacked ? " }>" : " }"),
        NamedStructIrType n when body => $"%{n.Name.Text} = {Describe(n.Body!)}",
        NamedStructIrType n => "%" + n.Name.Text,
        _ => throw new InvalidOperationException(type.ToString()),
    };
}
