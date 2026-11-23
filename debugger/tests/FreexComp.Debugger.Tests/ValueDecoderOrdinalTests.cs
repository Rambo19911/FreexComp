using FreexComp.Debugger.Tests.TestSupport;
using T = FreexComp.Debugger.Tests.TestSupport.ValueTestBed;

namespace FreexComp.Debugger.Tests;

/// <summary>Integers, Booleans and enumerations (Pascal values §2, DBG-152 … DBG-154).</summary>
public sealed class ValueDecoderOrdinalTests
{
    [Theory]
    [InlineData(T.Integer, "2A000000", "42", "$0000002A")]
    [InlineData(T.Integer, "F9FFFFFF", "-7", "$FFFFFFF9")]
    [InlineData(T.Integer, "00000080", "-2147483648", "$80000000")]
    [InlineData(T.Integer, "00000000", "0", "$00000000")]
    [InlineData(T.Cardinal, "FFFFFFFF", "4294967295", "$FFFFFFFF")]
    [InlineData(T.ShortInt, "80", "-128", "$80")]
    [InlineData(T.ShortInt, "7F", "127", "$7F")]
    [InlineData(T.Byte, "FF", "255", "$FF")]
    [InlineData(T.SmallInt, "0080", "-32768", "$8000")]
    [InlineData(T.Word, "FFFF", "65535", "$FFFF")]
    [InlineData(T.Word, "3412", "4660", "$1234")]
    [InlineData(T.Int64, "0000000000000080", "-9223372036854775808", "$8000000000000000")]
    [InlineData(T.Int64, "15CD5B0700000000", "123456789", "$00000000075BCD15")]
    [InlineData(T.UInt64, "FFFFFFFFFFFFFFFF", "18446744073709551615", "$FFFFFFFFFFFFFFFF")]
    public async Task Integer_DecimalAndHex(int type, string storage, string decimalText, string hexText)
    {
        // Little-endian, two's complement; no separators.
        Assert.Equal(decimalText, await new T().ValueAsync(type, storage));
        // HexIntegers: "$" and exactly two digits per byte of the raw bits.
        Assert.Equal(hexText, await new T(new ValueFormatOptions { HexIntegers = true }).ValueAsync(type, storage));
    }

    [Theory]
    [InlineData(T.Boolean, "00", "False")]
    [InlineData(T.Boolean, "01", "True")]
    [InlineData(T.Boolean, "02", "Boolean(2)")]
    [InlineData(T.Boolean, "FF", "Boolean(255)")]
    // The wide Boolean types: every non-zero value is True.
    [InlineData(T.ByteBool, "00", "False")]
    [InlineData(T.ByteBool, "02", "True")]
    [InlineData(T.ByteBool, "FF", "True")]
    [InlineData(T.LongBool, "00000000", "False")]
    [InlineData(T.LongBool, "01000000", "True")]
    [InlineData(T.LongBool, "00010000", "True")]
    [InlineData(T.LongBool, "FFFFFFFF", "True")]
    public async Task Boolean_Rendering(int type, string storage, string expected)
    {
        Assert.Equal(expected, await new T().ValueAsync(type, storage));
        // Unchanged by the hexadecimal option.
        Assert.Equal(expected, await new T(new ValueFormatOptions { HexIntegers = true }).ValueAsync(type, storage));
    }

    [Theory]
    [InlineData(T.TColor, "00", "clRed")]
    [InlineData(T.TColor, "01", "clGreen")]
    [InlineData(T.TColor, "02", "clBlue")]
    // An ordinal without a name: the type name and the value.
    [InlineData(T.TColor, "07", "TColor(7)")]
    [InlineData(T.TColor, "FF", "TColor(255)")]
    // The ordinal is unsigned and as wide as the type.
    [InlineData(T.TBig, "0000", "bgFirst")]
    [InlineData(T.TBig, "2C01", "bgLast")]
    [InlineData(T.TBig, "FFFF", "TBig(65535)")]
    public async Task Enum_Rendering(int type, string storage, string expected)
    {
        Assert.Equal(expected, await new T().ValueAsync(type, storage));
        Assert.Equal(expected, await new T(new ValueFormatOptions { HexIntegers = true }).ValueAsync(type, storage));
    }

    [Theory]
    [InlineData(T.Integer, "2A000000", ValueKind.Integer, "Integer")]
    [InlineData(T.UInt64, "0000000000000000", ValueKind.Integer, "UInt64")]
    [InlineData(T.Boolean, "01", ValueKind.Boolean, "Boolean")]
    [InlineData(T.LongBool, "01000000", ValueKind.Boolean, "LongBool")]
    [InlineData(T.TColor, "01", ValueKind.Enum, "TColor")]
    public async Task Ordinal_Node_IsModifiableLeaf(int type, string storage, ValueKind kind, string typeName)
    {
        var bed = new T();

        var node = await bed.DecodeHexAsync(type, storage, "Total");

        Assert.Equal(Convert.FromHexString(storage), node.Bytes);
        Assert.Null(node.ChildAt);
        Assert.Empty(await bed.ChildrenAsync(node));
        var info = bed.Info(node);
        Assert.Equal(new VariableInfo("Total", info.Value, typeName, kind, null, 0, true, "Total", T.At), info);
        // Without memory writing in the adapter nothing is modifiable (DBG-176).
        Assert.False(bed.Info(node, canWriteMemory: false).CanModify);
    }

    [Fact]
    [Trait("Req", "DBG-152")]
    public async Task Unreadable_ErrorNode()
    {
        var bed = new T();
        bed.Memory.PutHex(0x30000, "2A0000");   // one byte short

        var node = await bed.DecodeAsync(T.Integer, 0x30000, "Total");

        Assert.Equal(
            new VariableInfo("Total", "<unreadable memory at $0000000000030000>", "Integer", ValueKind.Error, null, 0, false, null, 0x30000),
            bed.Info(node));
        Assert.Equal(["30000+4"], bed.Memory.Reads);
    }
}
