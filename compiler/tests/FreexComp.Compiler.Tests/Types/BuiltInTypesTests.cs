using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Tests.Types;

/// <summary>Predeclared types and constants (types.md LANG-200, LANG-201; sizes rtl-abi.md §2; api.md §5).</summary>
[Trait("Phase", "2")]
[Trait("Req", "LANG-200")]
public sealed class BuiltInTypesTests
{
    private static readonly BuiltInTypes Types = new();

    private static TypeSymbol Type(string name) => Assert.IsAssignableFrom<TypeSymbol>(Types.Scope.LookupLocal(name));

    /// <summary>Every predeclared type name: canonical type name, kind, size, alignment, ordinal, managed.</summary>
    [Theory]
    [InlineData("ShortInt", "ShortInt", TypeKind.Integer, 1, 1, true, false)]
    [InlineData("SmallInt", "SmallInt", TypeKind.Integer, 2, 2, true, false)]
    [InlineData("Integer", "Integer", TypeKind.Integer, 4, 4, true, false)]
    [InlineData("LongInt", "Integer", TypeKind.Integer, 4, 4, true, false)]
    [InlineData("Int64", "Int64", TypeKind.Integer, 8, 8, true, false)]
    [InlineData("NativeInt", "NativeInt", TypeKind.Integer, 8, 8, true, false)]
    [InlineData("Byte", "Byte", TypeKind.Integer, 1, 1, true, false)]
    [InlineData("Word", "Word", TypeKind.Integer, 2, 2, true, false)]
    [InlineData("Cardinal", "Cardinal", TypeKind.Integer, 4, 4, true, false)]
    [InlineData("LongWord", "Cardinal", TypeKind.Integer, 4, 4, true, false)]
    [InlineData("UInt64", "UInt64", TypeKind.Integer, 8, 8, true, false)]
    [InlineData("NativeUInt", "NativeUInt", TypeKind.Integer, 8, 8, true, false)]
    [InlineData("Boolean", "Boolean", TypeKind.Boolean, 1, 1, true, false)]
    [InlineData("ByteBool", "ByteBool", TypeKind.Boolean, 1, 1, true, false)]
    [InlineData("WordBool", "WordBool", TypeKind.Boolean, 2, 2, true, false)]
    [InlineData("LongBool", "LongBool", TypeKind.Boolean, 4, 4, true, false)]
    [InlineData("Char", "Char", TypeKind.Char, 2, 2, true, false)]
    [InlineData("WideChar", "Char", TypeKind.Char, 2, 2, true, false)]
    [InlineData("AnsiChar", "AnsiChar", TypeKind.Char, 1, 1, true, false)]
    [InlineData("Single", "Single", TypeKind.Real, 4, 4, false, false)]
    [InlineData("Double", "Double", TypeKind.Real, 8, 8, false, false)]
    [InlineData("Real", "Double", TypeKind.Real, 8, 8, false, false)]
    [InlineData("Extended", "Double", TypeKind.Real, 8, 8, false, false)]
    [InlineData("Currency", "Currency", TypeKind.Real, 8, 8, false, false)]
    [InlineData("string", "string", TypeKind.String, 8, 8, false, true)]
    [InlineData("UnicodeString", "string", TypeKind.String, 8, 8, false, true)]
    [InlineData("AnsiString", "AnsiString", TypeKind.String, 8, 8, false, true)]
    [InlineData("UTF8String", "UTF8String", TypeKind.String, 8, 8, false, true)]
    [InlineData("RawByteString", "RawByteString", TypeKind.String, 8, 8, false, true)]
    [InlineData("ShortString", "ShortString", TypeKind.ShortString, 256, 1, false, false)]
    [InlineData("Pointer", "Pointer", TypeKind.Pointer, 8, 8, false, false)]
    [InlineData("PChar", "PChar", TypeKind.Pointer, 8, 8, false, false)]
    [InlineData("PWideChar", "PChar", TypeKind.Pointer, 8, 8, false, false)]
    [InlineData("PAnsiChar", "PAnsiChar", TypeKind.Pointer, 8, 8, false, false)]
    [InlineData("Text", "Text", TypeKind.Text, 624, 8, false, false)]
    [InlineData("TextFile", "Text", TypeKind.Text, 624, 8, false, false)]
    public void PredeclaredType_HasKindSizeAndAlignment(
        string name, string canonical, TypeKind kind, long size, int alignment, bool isOrdinal, bool isManaged)
    {
        var type = Type(name);

        Assert.Equal(
            (canonical, SymbolKind.Type, kind, size, alignment, isOrdinal, isManaged),
            (type.Name, type.Kind, type.TypeKind, type.Size, type.Alignment, type.IsOrdinal, type.IsManaged));
    }

    [Fact]
    public void BuiltInScope_ContainsExactlyThePredeclaredNames()
    {
        string[] expected =
        [
            "ShortInt", "SmallInt", "Integer", "LongInt", "Int64", "NativeInt", "Byte", "Word", "Cardinal", "LongWord",
            "UInt64", "NativeUInt", "Boolean", "ByteBool", "WordBool", "LongBool", "Char", "WideChar", "AnsiChar",
            "Single", "Double", "Real", "Extended", "Currency", "string", "UnicodeString", "AnsiString", "UTF8String",
            "RawByteString", "ShortString", "Pointer", "PChar", "PWideChar", "PAnsiChar", "Text", "TextFile",
            "True", "False", "MaxInt", "MaxLongInt", "Pi",
            "Break", "Continue", "Exit", "Halt",                                            // intrinsics (LANG-900)
            "Inc", "Dec", "RunError", "Write", "Writeln", "Include", "Exclude", "Succ", "Pred", "Ord", "Chr", "Odd", "Abs", "Sqr", "Low", "High", "SizeOf", "Lo", "Hi", "Swap",
            "Length",                                                                       // M2.2 (C-052)
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), Types.Scope.Names.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("TObject")]
    [InlineData("TClass")]
    [InlineData("IInterface")]
    [InlineData("TGUID")]
    [InlineData("TInterfacedObject")]
    public void SystemTypes_AreNotBuiltIn(string name)
    {
        Assert.Null(Types.Scope.Lookup(name));
        Assert.Null(Types.TObject);
        Assert.Null(Types.IInterface);
    }

    [Theory]
    [InlineData("LongInt", "Integer")]
    [InlineData("LongWord", "Cardinal")]
    [InlineData("WideChar", "Char")]
    [InlineData("Real", "Double")]
    [InlineData("Extended", "Double")]
    [InlineData("UnicodeString", "string")]
    [InlineData("PWideChar", "PChar")]
    [InlineData("TextFile", "Text")]
    public void Aliases_AreTheSameType(string alias, string target) => Assert.Same(Type(target), Type(alias));

    [Theory]
    [InlineData("NativeInt", "Int64")]
    [InlineData("NativeUInt", "UInt64")]
    [InlineData("UTF8String", "AnsiString")]
    [InlineData("RawByteString", "AnsiString")]
    [InlineData("ByteBool", "Boolean")]
    [InlineData("AnsiChar", "Char")]
    public void DistinctNames_AreDistinctTypes(string a, string b) => Assert.NotSame(Type(a), Type(b));

    [Theory]
    [InlineData("ShortInt", 8, true, "-128", "127")]
    [InlineData("SmallInt", 16, true, "-32768", "32767")]
    [InlineData("Integer", 32, true, "-2147483648", "2147483647")]
    [InlineData("Int64", 64, true, "-9223372036854775808", "9223372036854775807")]
    [InlineData("NativeInt", 64, true, "-9223372036854775808", "9223372036854775807")]
    [InlineData("Byte", 8, false, "0", "255")]
    [InlineData("Word", 16, false, "0", "65535")]
    [InlineData("Cardinal", 32, false, "0", "4294967295")]
    [InlineData("UInt64", 64, false, "0", "18446744073709551615")]
    [InlineData("NativeUInt", 64, false, "0", "18446744073709551615")]
    public void IntegerTypes_HaveTheirRanges(string name, int bits, bool isSigned, string min, string max)
    {
        var type = Assert.IsType<IntegerTypeSymbol>(Type(name));

        Assert.Equal(
            (bits, isSigned, BigInteger.Parse(min), BigInteger.Parse(max)),
            (type.Bits, type.IsSigned, type.Min, type.Max));
    }

    [Theory]
    [InlineData("Boolean", false)]
    [InlineData("ByteBool", true)]
    [InlineData("WordBool", true)]
    [InlineData("LongBool", true)]
    public void BooleanTypes_AreBooleanOrCStyle(string name, bool isCStyle) =>
        Assert.Equal(isCStyle, Assert.IsType<BooleanTypeSymbol>(Type(name)).IsCStyle);

    [Theory]
    [InlineData("Char", true, 0xFFFF)]
    [InlineData("AnsiChar", false, 0xFF)]
    public void CharTypes_HaveTheirRange(string name, bool isWide, int max)
    {
        var type = Assert.IsType<CharTypeSymbol>(Type(name));

        Assert.Equal((isWide, max), (type.IsWide, type.MaxValue));
    }

    [Theory]
    [InlineData("Single", RealKind.Single)]
    [InlineData("Double", RealKind.Double)]
    [InlineData("Currency", RealKind.Currency)]
    public void RealTypes_HaveTheirKind(string name, RealKind kind) =>
        Assert.Equal(kind, Assert.IsType<RealTypeSymbol>(Type(name)).RealKind);

    [Theory]
    [Trait("Req", "ABI-027")]
    [InlineData("string", StringVariant.Unicode, 1200, 0)]
    [InlineData("AnsiString", StringVariant.Ansi, 65001, 0)]
    [InlineData("UTF8String", StringVariant.Ansi, 65001, 0)]
    [InlineData("RawByteString", StringVariant.Ansi, 0xFFFF, 0)]
    [InlineData("ShortString", StringVariant.Short, 65001, 255)]
    public void StringTypes_HaveVariantCodePageAndCapacity(string name, StringVariant variant, int codePage, int maxLength)
    {
        var type = Assert.IsType<StringTypeSymbol>(Type(name));

        Assert.Equal((variant, codePage, maxLength), (type.Variant, type.CodePage, type.MaxLength));
    }

    [Fact]
    public void PointerTypes_PointToTheirCharTypes()
    {
        var pointer = Assert.IsType<PointerTypeSymbol>(Types.Pointer);
        var pchar = Assert.IsType<PointerTypeSymbol>(Types.PChar);
        var pansi = Assert.IsType<PointerTypeSymbol>(Type("PAnsiChar"));

        Assert.Equal((null, false), (pointer.PointedType, pointer.AllowsPointerMath));
        Assert.Equal((Types.Char, true), (pchar.PointedType, pchar.AllowsPointerMath));
        Assert.Equal((Types.AnsiChar, true), (pansi.PointedType, pansi.AllowsPointerMath));
    }

    [Fact]
    public void Properties_AreTheScopeEntries()
    {
        Assert.Same(Type("Integer"), Types.Integer);
        Assert.Same(Type("Cardinal"), Types.Cardinal);
        Assert.Same(Type("Int64"), Types.Int64);
        Assert.Same(Type("UInt64"), Types.UInt64);
        Assert.Same(Type("ShortInt"), Types.ShortInt);
        Assert.Same(Type("SmallInt"), Types.SmallInt);
        Assert.Same(Type("Byte"), Types.Byte);
        Assert.Same(Type("Word"), Types.Word);
        Assert.Same(Type("NativeInt"), Types.NativeInt);
        Assert.Same(Type("NativeUInt"), Types.NativeUInt);
        Assert.Same(Type("Boolean"), Types.Boolean);
        Assert.Same(Type("Char"), Types.Char);
        Assert.Same(Type("AnsiChar"), Types.AnsiChar);
        Assert.Same(Type("Single"), Types.Single);
        Assert.Same(Type("Double"), Types.Double);
        Assert.Same(Type("string"), Types.String);
        Assert.Same(Type("AnsiString"), Types.AnsiString);
        Assert.Same(Type("ShortString"), Types.ShortString);
        Assert.Same(Type("Pointer"), Types.Pointer);
        Assert.Same(Type("PChar"), Types.PChar);
        Assert.Same(Type("Text"), Types.Text);
    }

    [Fact]
    public void BuiltIns_HaveNoLocationAndNoContainer()
    {
        foreach (var symbol in Types.Scope.Symbols)
        {
            Assert.Empty(symbol.Locations);
            Assert.Null(symbol.ContainingSymbol);
            Assert.Null(symbol.ContainingUnit);
            Assert.Equal(Visibility.Public, symbol.Visibility);
            Assert.Equal(symbol.Name, symbol.QualifiedName);
        }
    }

    [Theory]
    [Trait("Req", "LANG-201")]
    [InlineData("True", "Boolean", true)]
    [InlineData("False", "Boolean", false)]
    public void BooleanConstants_HaveBoolValues(string name, string type, bool value)
    {
        var constant = Assert.IsType<ConstantSymbol>(Types.Scope.LookupLocal(name));

        Assert.Equal((SymbolKind.Constant, Type(type), false), (constant.Kind, constant.Type, constant.IsTyped));
        Assert.Equal(value, constant.Value!.Value);
        Assert.Same(constant.Type, constant.Value.Type);
    }

    [Theory]
    [Trait("Req", "LANG-201")]
    [InlineData("MaxInt")]
    [InlineData("MaxLongInt")]
    public void MaxIntConstants_Are2147483647OfTypeInteger(string name)
    {
        var constant = Assert.IsType<ConstantSymbol>(Types.Scope.LookupLocal(name));

        Assert.Equal((Types.Integer, (object)new BigInteger(2147483647)), (constant.Type, constant.Value!.Value));
    }

    [Fact]
    [Trait("Req", "LANG-201")]
    public void Pi_IsADoubleConstant()
    {
        var pi = Assert.IsType<ConstantSymbol>(Types.Scope.LookupLocal("Pi"));

        Assert.Equal((Types.Double, (object)3.14159265358979323846), (pi.Type, pi.Value!.Value));
    }

    [Theory]
    [Trait("Req", "CMP-731")]
    [InlineData("MaxInt", "const MaxInt = 2147483647")]
    [InlineData("True", "const True = True")]
    [InlineData("Pi", "const Pi = 3.141592653589793")]
    [InlineData("Integer", "type Integer")]
    [InlineData("LongInt", "type Integer")]
    [InlineData("string", "type string")]
    public void ToDisplayString_ShowsTheDeclaration(string name, string expected) =>
        Assert.Equal(expected, Types.Scope.LookupLocal(name)!.ToDisplayString());

    [Theory]
    [InlineData(5, "5")]
    [InlineData(-7L, "-7")]
    [InlineData(true, "True")]
    [InlineData(false, "False")]
    [InlineData(2.5, "2.5")]
    [InlineData(1.0, "1.0")]
    [InlineData(1e20, "1E+20")]
    [InlineData("it's", "'it''s'")]
    [InlineData("", "''")]
    [InlineData("a\r\nb", "'a'#13#10'b'")]
    [InlineData(null, "nil")]
    public void ConstantValue_DisplaysAsPascalLiteral(object? value, string expected) =>
        Assert.Equal(expected, new ConstantValue(value, Types.Integer).ToDisplayString());

    [Fact]
    public void ConstantValue_StoresClrIntegersAsBigInteger()
    {
        Assert.Equal(new BigInteger(5), new ConstantValue(5, Types.Integer).Value);
        Assert.Equal(new BigInteger(ulong.MaxValue), new ConstantValue(ulong.MaxValue, Types.UInt64).Value);
    }

    [Fact]
    public void ErrorType_IsItsOwnKind()
    {
        Assert.Equal(TypeKind.Error, Types.Error.TypeKind);
        Assert.Null(Types.Scope.Lookup(Types.Error.Name));
    }

    [Fact]
    public void Lookup_IgnoresCase_ForBuiltIns()
    {
        Assert.Same(Types.String, Types.Scope.Lookup("STRING"));
        Assert.Same(Types.Integer, Types.Scope.Lookup("integer"));
    }
}
