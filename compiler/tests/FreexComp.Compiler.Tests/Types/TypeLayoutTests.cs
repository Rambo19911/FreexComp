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

    /// <summary>
    /// C-108, ABI-061 (hand-computed): every variant starts at the end of the fixed part (tag included when named), aligned to the
    /// largest alignment of all variant fields (nested ones too); the variant part is as large as its largest variant; the
    /// storage is <c>[Size x i8]</c> (CMP-306). Fields listed in declaration order (fixed, tag, then each variant's).
    /// </summary>
    [Theory]
    [Trait("Req", "ABI-061")]
    [Trait("Req", "CMP-306")]
    [InlineData("record case VType: Byte of 0: (VInteger: Integer); 1: (VPointer: Pointer) end", "0 8 8", 16, 8)]
    [InlineData("record case Byte of 0: (a: Byte); 1: (b: Word) end", "0 0", 2, 2)]
    [InlineData("record x: Byte; case Integer of 0: (a, b: Byte); 1: (c: Integer; d: Byte) end", "0 4 5 4 8", 12, 4)]
    [InlineData("record x: Word; case t: Boolean of True: (d: Double); False: (s: array[0..2] of Byte) end", "0 2 8 8", 16, 8)]
    [InlineData("packed record x: Byte; case Byte of 0: (a: Integer); 1: (b: Int64) end", "0 1 1", 9, 1)]
    [InlineData("record case Byte of 0: (a: Byte; case k: Byte of 1: (b: Int64); 2: (c: Word)); 3: (d: Byte) end", "0 1 8 8 0", 16, 8)]
    public void Variants_OverlayAtTheAlignedEndOfTheFixedPart(string definition, string offsets, long size, int alignment)
    {
        var (result, _) = Binding.BinderTestHelper.Bind($"type T = {definition}; begin end.");
        Assert.Empty(result.Diagnostics);
        var record = Assert.IsType<RecordTypeSymbol>(result.Declarations.Scope.Lookup("T"));
        var layout = TypeLayout.Of(record);

        Assert.Equal((offsets, size, alignment, $"[{size} x i8]", true),
            (string.Join(" ", record.Fields.Select(f => f.Offset)), layout.Size, layout.Alignment, Describe(layout.StorageType), record.HasVariantPart));
    }

    /// <summary>
    /// C-163 (LANG-263, ABI-060…064): records mirroring Win32 structures and C structures under <c>#pragma pack(1/2/4/8)</c>
    /// get exactly the Win64 C layout. Every row was computed by hand from the C rules (scalar alignment = min(size, pack),
    /// nested aggregates keep their own alignment capped by the pack, size rounded up to the struct alignment) and
    /// cross-checked once against the pinned clang (<c>--target=x86_64-pc-windows-msvc</c>, <c>_Static_assert</c> on
    /// <c>offsetof</c>/<c>sizeof</c>/<c>_Alignof</c>). Offsets list every field in declaration order.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-263")]
    [Trait("Req", "ABI-060")]
    [Trait("Req", "ABI-062")]
    [Trait("Req", "ABI-063")]
    [Trait("Req", "ABI-064")]
    // POINT
    [InlineData("record X, Y: Integer end", "0 4", 8, 4)]
    // MSG
    [InlineData("record hwnd: Pointer; message: Cardinal; wParam: NativeUInt; lParam: NativeInt; time: Cardinal; pt: TPoint; lPrivate: Cardinal end",
        "0 8 16 24 32 36 44", 48, 8)]
    // WNDCLASSEXW
    [InlineData("record cbSize, style: Cardinal; lpfnWndProc: Pointer; cbClsExtra, cbWndExtra: Integer; hInstance, hIcon, hCursor, hbrBackground: Pointer; lpszMenuName, lpszClassName: PWideChar; hIconSm: Pointer end",
        "0 4 8 16 20 24 32 40 48 56 64 72", 80, 8)]
    // SYSTEMTIME
    [InlineData("record wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds: Word end", "0 2 4 6 8 10 12 14", 16, 2)]
    // LOGFONTW
    [InlineData("record lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight: Integer; lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily: Byte; lfFaceName: array[0..31] of WideChar end",
        "0 4 8 12 16 20 21 22 23 24 25 26 27 28", 92, 4)]
    // struct { char c; short s; char d; double x; char e; }
    [InlineData("record c: AnsiChar; s: SmallInt; d: AnsiChar; x: Double; e: AnsiChar end", "0 2 4 8 16", 24, 8)]
    // nested struct { char a; int b; } keeps alignment 4
    [InlineData("record c: AnsiChar; i: TIn4; d: AnsiChar end", "0 4 12", 16, 4)]
    // array of a nested struct
    [InlineData("record w: Word; r: array[0..1] of TRect; b: Byte end", "0 4 36", 40, 4)]
    // WCHAR name[3] then a pointer
    [InlineData("record name: array[0..2] of WideChar; p: Pointer end", "0 8", 16, 8)]
    // #pragma pack(1)
    [InlineData("packed record c: AnsiChar; i: Integer; s: SmallInt; d: Double end", "0 1 5 7", 15, 1)]
    [InlineData("{$ALIGN 1} record c: AnsiChar; i: Integer; s: SmallInt; d: Double end", "0 1 5 7", 15, 1)]
    [InlineData("packed record c: AnsiChar; i: TIn4 end", "0 1", 9, 1)]
    // a pack(1) struct nested in a default-packed one
    [InlineData("record c: AnsiChar; p: TPk; x: Integer end", "0 1 8", 12, 4)]
    // #pragma pack(2)
    [InlineData("{$ALIGN 2} record c: AnsiChar; i: Integer; d: AnsiChar; x: Double end", "0 2 6 8", 16, 2)]
    // #pragma pack(4), also capping a nested 8-aligned struct
    [InlineData("{$ALIGN 4} record c: AnsiChar; x: Double; s: SmallInt end", "0 4 12", 16, 4)]
    [InlineData("{$ALIGN 4} record c: AnsiChar; i: TIn8 end", "0 4", 20, 4)]
    // #pragma pack(8) = the default
    [InlineData("{$ALIGN 8} record c: AnsiChar; x: Double; y: Int64; s: SmallInt end", "0 8 16 24", 32, 8)]
    public void Win64CLayout_MatchesHandComputedCStructures(string definition, string offsets, long size, int alignment)
    {
        var (result, _) = Binding.BinderTestHelper.Bind($$"""
            type
              TPoint = record X, Y: Integer end;
              TRect = record L, T, R, B: Integer end;
              TIn4 = record A: AnsiChar; B: Integer end;
              TIn8 = record C: AnsiChar; D: Double end;
              TPk = packed record C: AnsiChar; I: Integer end;
              T = {{definition}};
            begin end.
            """);
        Assert.Empty(result.Diagnostics);
        var record = Assert.IsType<RecordTypeSymbol>(result.Declarations.Scope.Lookup("T"));
        var layout = TypeLayout.Of(record);

        Assert.Equal((offsets, size, alignment),
            (string.Join(" ", record.Fields.Select(f => f.Offset)), layout.Size, layout.Alignment));
        Assert.Equal((record.Size, record.Alignment), (layout.Size, layout.Alignment));
    }

    /// <summary>C-163: the helper structures of <see cref="Win64CLayout_MatchesHandComputedCStructures"/> themselves.</summary>
    [Theory]
    [Trait("Req", "ABI-064")]
    [InlineData("TPoint", 8, 4)]
    [InlineData("TRect", 16, 4)]
    [InlineData("TIn4", 8, 4)]
    [InlineData("TIn8", 16, 8)]
    [InlineData("TPk", 5, 1)]
    public void Win64CLayout_NestedHelperStructures(string name, long size, int alignment)
    {
        var (result, _) = Binding.BinderTestHelper.Bind("""
            type
              TPoint = record X, Y: Integer end;
              TRect = record L, T, R, B: Integer end;
              TIn4 = record A: AnsiChar; B: Integer end;
              TIn8 = record C: AnsiChar; D: Double end;
              TPk = packed record C: AnsiChar; I: Integer end;
            begin end.
            """);
        Assert.Empty(result.Diagnostics);
        var layout = TypeLayout.Of(Assert.IsType<RecordTypeSymbol>(result.Declarations.Scope.Lookup(name)));

        Assert.Equal((size, alignment), (layout.Size, layout.Alignment));
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

    /// <summary>
    /// C-080, ABI §7.1 / ABI-070 (hand-computed): fields after the VMT pointer (offset 8), each at the next multiple of its
    /// alignment (cap 8, independent of <c>{$A}</c>), a descendant's group after the parent's <c>InstanceSize</c>; InstanceSize =
    /// end rounded up to 8 (8 without fields). Bound without System, so a class without heritage is a root class.
    /// </summary>
    [Theory]
    [Trait("Req", "ABI-070")]
    [Trait("Req", "LANG-615")]
    [InlineData("TA = class end", "", 8)]
    [InlineData("TA = class a: Byte end", "8", 16)]
    [InlineData("TA = class a: Byte; b: Integer; c: Word end", "8 12 16", 24)]
    [InlineData("TA = class a, b: Byte; c: Int64; d: Char end", "8 9 16 24", 32)]
    [InlineData("TA = class s: string; d: Double; p: TA end", "8 16 24", 32)]
    [InlineData("TA = class r: record x: Byte; y: Word end; z: Byte end", "8 12", 16)]
    [InlineData("{$A1} TA = class a: Byte; b: Integer end", "8 12", 16)]
    [InlineData("TA = class private a: Byte; public b: Int64; protected c: Byte end", "8 16 24", 32)]
    public void Classes_InstanceLayoutFollowsTheAbi(string definition, string offsets, long instanceSize)
    {
        var (result, _) = Binding.BinderTestHelper.Bind($"type {definition}; begin end.");
        Assert.Empty(result.Diagnostics);
        var type = Assert.IsType<ClassTypeSymbol>(result.Declarations.Scope.Lookup("TA"));

        Assert.Equal((offsets, instanceSize), (string.Join(" ", type.Fields.Select(f => f.Offset)), type.InstanceSize));
    }

    /// <summary>ABI §7.1: a class's fields start at its parent's InstanceSize, the root-most ancestor's fields first.</summary>
    [Theory]
    [Trait("Req", "ABI-070")]
    [InlineData("TB = class(TA) x: Byte end", "16", 24)]
    [InlineData("TB = class(TA) end", "", 16)]
    [InlineData("TB = class(TA) d: Double; e: Byte end", "16 24", 32)]
    [InlineData("TB = class(TC) x: Integer end", "24", 32)]
    public void Classes_DescendantFieldsFollowTheParentInstance(string definition, string offsets, long instanceSize)
    {
        var (result, _) = Binding.BinderTestHelper.Bind($"type TA = class a: Integer end; TC = class(TA) c: Byte end; {definition}; begin end.");
        Assert.Empty(result.Diagnostics);
        var type = Assert.IsType<ClassTypeSymbol>(result.Declarations.Scope.Lookup("TB"));

        Assert.Equal((offsets, instanceSize), (string.Join(" ", type.Fields.Select(f => f.Offset)), type.InstanceSize));
        Assert.Equal(16, ((ClassTypeSymbol)result.Declarations.Scope.Lookup("TA")!).InstanceSize);
        Assert.Equal(24, ((ClassTypeSymbol)result.Declarations.Scope.Lookup("TC")!).InstanceSize);
    }

    /// <summary>ABI-071 / CMP-367: a class value is a reference, 8 bytes, stored as <c>ptr</c>.</summary>
    [Fact]
    [Trait("Req", "ABI-071")]
    public void Classes_AReferenceIsAPointer()
    {
        var (result, _) = Binding.BinderTestHelper.Bind("type TA = class a, b, c: Int64 end; begin end.");
        var layout = TypeLayout.Of(Assert.IsAssignableFrom<TypeSymbol>(result.Declarations.Scope.Lookup("TA")));

        Assert.Equal((8L, 8, "ptr"), (layout.Size, layout.Alignment, Describe(layout.StorageType)));
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
