using FreexComp.Debugger.Tests.TestSupport;
using T = FreexComp.Debugger.Tests.TestSupport.ValueTestBed;

namespace FreexComp.Debugger.Tests;

/// <summary>Sets (Pascal values §3: DBG-157, DBG-158).</summary>
[Trait("Req", "DBG-157")]
[Trait("Req", "DBG-158")]
public sealed class ValueDecoderSetTests
{
    /// <summary>The storage of a set with the given member bits (bit <c>v mod 8</c> of byte <c>v div 8</c>).</summary>
    private static string Bits(int size, params int[] members)
    {
        var bytes = new byte[size];
        foreach (var member in members)
            bytes[member / 8] |= (byte)(1 << member % 8);
        return Convert.ToHexString(bytes);
    }

    [Theory]
    // The rows of the table in §3.
    [InlineData("05", "[clRed, clBlue]")]
    [InlineData("00", "[]")]
    [InlineData("01", "[clRed]")]
    // Enum members are never collapsed to a range.
    [InlineData("07", "[clRed, clGreen, clBlue]")]
    // An ordinal without a name.
    [InlineData("0D", "[clRed, clBlue, TColor(3)]")]
    [InlineData("80", "[TColor(7)]")]
    public async Task EnumSet_Rendering(string storage, string expected)
    {
        Assert.Equal(expected, await new T().ValueAsync(T.TColors, storage));
        Assert.Equal(expected, await new T(new ValueFormatOptions { HexIntegers = true }).ValueAsync(T.TColors, storage));
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 7, 9, 10 }, "[1..3, 7, 9, 10]")]
    [InlineData(new int[0], "[]")]
    [InlineData(new[] { 0 }, "[0]")]
    [InlineData(new[] { 255 }, "[255]")]
    // Two neighbours stay separate; three or more become a range, also across a byte boundary.
    [InlineData(new[] { 4, 5 }, "[4, 5]")]
    [InlineData(new[] { 6, 7, 8, 9 }, "[6..9]")]
    [InlineData(new[] { 0, 1, 2, 4, 5, 6, 8 }, "[0..2, 4..6, 8]")]
    [InlineData(new[] { 253, 254, 255 }, "[253..255]")]
    public async Task ByteSet_ConsecutiveMembersCollapsed(int[] members, string expected)
    {
        Assert.Equal(expected, await new T().ValueAsync(T.SetOfByte, Bits(32, members)));
    }

    [Fact]
    public async Task ByteSet_AllMembers()
    {
        Assert.Equal("[0..255]", await new T().ValueAsync(T.SetOfByte, Bits(32, [.. Enumerable.Range(0, 256)])));
    }

    [Fact]
    public async Task CharSet_QuotedMembersAndRanges()
    {
        int[] digitsAndA = [.. Enumerable.Range(48, 10), 65];

        Assert.Equal("['0'..'9', 'A']", await new T().ValueAsync(T.SetOfAnsiChar, Bits(32, digitsAndA)));
        // Characters by the character rule: apostrophe doubled, others by number.
        Assert.Equal("[#9, #10, #13, '''', 'a', 'b', #200]", await new T().ValueAsync(T.SetOfAnsiChar, Bits(32, 9, 10, 13, 39, 97, 98, 200)));
        Assert.Equal("[#0..#31]", await new T().ValueAsync(T.SetOfAnsiChar, Bits(32, [.. Enumerable.Range(0, 32)])));
        // A set of wide characters holds the first 256 ordinals.
        Assert.Equal("['x'..'z', 'é']", await new T().ValueAsync(T.SetOfChar, Bits(32, 120, 121, 122, 233)));
    }

    [Theory]
    [InlineData("00", "[]")]
    [InlineData("01", "[False]")]
    [InlineData("02", "[True]")]
    [InlineData("03", "[False, True]")]
    public async Task BooleanSet_Rendering(string storage, string expected)
    {
        Assert.Equal(expected, await new T().ValueAsync(T.SetOfBoolean, storage));
    }

    [Fact]
    public async Task BitBase_OffsetsTheOrdinals()
    {
        // Bit 0 stands for ordinal 8.
        Assert.Equal("[8..10, 15]", await new T().ValueAsync(T.SetFrom8, "87"));
    }

    [Fact]
    public async Task Set_Node_IsLeaf_NotModifiable()
    {
        var bed = new T();

        var named = bed.Info(await bed.DecodeHexAsync(T.TColors, "05", "Colors"));
        var anonymous = bed.Info(await bed.DecodeHexAsync(T.SetOfByte, Bits(32, 1), "Small"));
        var ofChar = bed.Info(await bed.DecodeHexAsync(T.SetOfAnsiChar, Bits(32, 65), "Letters"));

        Assert.Equal(new VariableInfo("Colors", "[clRed, clBlue]", "TColors", ValueKind.Set, null, 0, false, "Colors", T.At), named);
        // An anonymous set type gets its Pascal spelling.
        Assert.Equal(new VariableInfo("Small", "[1]", "set of Byte", ValueKind.Set, null, 0, false, "Small", T.At), anonymous);
        Assert.Equal("set of AnsiChar", ofChar.TypeName);
        // The whole set is read with one request.
        Assert.Equal(["20000+1", "20000+32", "20000+32"], bed.Memory.Reads);
    }

    [Fact]
    [Trait("Req", "DBG-152")]
    public async Task Set_Unreadable_ErrorNode()
    {
        var bed = new T();
        bed.Memory.PutHex(T.At, "0102");

        var node = await bed.DecodeAsync(T.SetOfByte);

        Assert.Equal(ValueKind.Error, node.Kind);
        Assert.Equal("<unreadable memory at $0000000000020000>", bed.Format(node));
    }
}
