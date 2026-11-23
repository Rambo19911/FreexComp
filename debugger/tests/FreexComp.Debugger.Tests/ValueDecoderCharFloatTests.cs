using System.Buffers.Binary;
using FreexComp.Debugger.Tests.TestSupport;
using T = FreexComp.Debugger.Tests.TestSupport.ValueTestBed;

namespace FreexComp.Debugger.Tests;

/// <summary>Characters, floating-point values and Currency (Pascal values §2, §2.1 DBG-155, §2.2 DBG-156).</summary>
public sealed class ValueDecoderCharFloatTests
{
    private static readonly ValueFormatOptions Hex = new() { HexIntegers = true };

    [Theory]
    [Trait("Req", "DBG-155")]
    [InlineData(65, "'A'")]
    [InlineData(32, "' '")]
    [InlineData(126, "'~'")]
    [InlineData(39, "''''")]
    [InlineData(0, "#0")]
    [InlineData(9, "#9")]
    [InlineData(13, "#13")]
    [InlineData(31, "#31")]
    [InlineData(127, "#127")]
    [InlineData(159, "#159")]
    [InlineData(160, "Q")]
    [InlineData(233, "'é'")]
    [InlineData(0x20AC, "'€'")]
    // Surrogates and the two non-characters are shown by number.
    [InlineData(0xD7FF, "Q")]
    [InlineData(0xD800, "#55296")]
    [InlineData(55357, "#55357")]
    [InlineData(0xDFFF, "#57343")]
    [InlineData(0xE000, "Q")]
    [InlineData(0xFFFD, "Q")]
    [InlineData(0xFFFE, "#65534")]
    [InlineData(0xFFFF, "#65535")]
    public async Task WideChar_Rendering(int unit, string expected)
    {
        var storage = Convert.ToHexString([(byte)unit, (byte)(unit >> 8)]);
        // "Q": the character itself in quotes (characters that do not survive in an attribute or a source file).
        if (expected == "Q")
            expected = "'" + (char)unit + "'";

        Assert.Equal(expected, await new T().ValueAsync(T.Char, storage));
        Assert.Equal(expected, await new T(Hex).ValueAsync(T.Char, storage));
    }

    [Theory]
    [InlineData("78", "'x'")]
    [InlineData("20", "' '")]
    [InlineData("7E", "'~'")]
    [InlineData("27", "''''")]
    [InlineData("00", "#0")]
    [InlineData("1F", "#31")]
    [InlineData("7F", "#127")]
    // An 8-bit character outside ASCII has no fixed meaning: by number.
    [InlineData("A0", "#160")]
    [InlineData("C8", "#200")]
    [InlineData("FF", "#255")]
    public async Task AnsiChar_Rendering(string storage, string expected)
    {
        Assert.Equal(expected, await new T().ValueAsync(T.AnsiChar, storage));
        Assert.Equal(expected, await new T(Hex).ValueAsync(T.AnsiChar, storage));
    }

    [Theory]
    [Trait("Req", "DBG-156")]
    [InlineData(3.14, "3.14")]
    [InlineData(1E100, "1E100")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "+Inf")]
    [InlineData(double.NegativeInfinity, "-Inf")]
    [InlineData(-0.0, "-0")]
    [InlineData(0.0, "0")]
    // Integral values have no decimal point.
    [InlineData(3.0, "3")]
    [InlineData(-2.5, "-2.5")]
    [InlineData(0.1, "0.1")]
    [InlineData(1.0 / 3.0, "0.3333333333333333")]
    [InlineData(123456789.125, "123456789.125")]
    [InlineData(1E15, "1000000000000000")]
    [InlineData(1E20, "1E20")]
    [InlineData(1.5E-7, "1.5E-07")]
    [InlineData(double.MaxValue, "1.7976931348623157E308")]
    [InlineData(double.Epsilon, "5E-324")]
    public async Task Double_ShortestRoundTrip(double value, string expected)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);

        Assert.Equal(expected, await new T().ValueAsync(T.Double, Convert.ToHexString(bytes)));
        Assert.Equal(expected, await new T(Hex).ValueAsync(T.Double, Convert.ToHexString(bytes)));
        // A date/time is shown as its Double in this phase.
        Assert.Equal(expected, await new T().ValueAsync(T.TDateTime, Convert.ToHexString(bytes)));
    }

    [Theory]
    [Trait("Req", "DBG-156")]
    [InlineData(3.14f, "3.14")]
    [InlineData(0.5f, "0.5")]
    [InlineData(-7f, "-7")]
    [InlineData(0.1f, "0.1")]
    [InlineData(1E20f, "1E20")]
    [InlineData(float.NaN, "NaN")]
    [InlineData(float.PositiveInfinity, "+Inf")]
    [InlineData(float.NegativeInfinity, "-Inf")]
    [InlineData(-0.0f, "-0")]
    public async Task Single_ShortestRoundTrip(float value, string expected)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);

        Assert.Equal(expected, await new T().ValueAsync(T.Single, Convert.ToHexString(bytes)));
    }

    [Theory]
    [InlineData(125000, "12.50")]
    [InlineData(3333, "0.3333")]
    [InlineData(0, "0.00")]
    [InlineData(10000, "1.00")]
    [InlineData(1200, "0.12")]
    [InlineData(1230, "0.123")]
    [InlineData(1, "0.0001")]
    [InlineData(123456789, "12345.6789")]
    [InlineData(-125000, "-12.50")]
    [InlineData(-1, "-0.0001")]
    [InlineData(long.MaxValue, "922337203685477.5807")]
    [InlineData(long.MinValue, "-922337203685477.5808")]
    public async Task Currency_FourDecimalsTrimmedToTwo(long scaled, string expected)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, scaled);

        Assert.Equal(expected, await new T().ValueAsync(T.Currency, Convert.ToHexString(bytes)));
        Assert.Equal(expected, await new T(Hex).ValueAsync(T.Currency, Convert.ToHexString(bytes)));
    }

    [Theory]
    [InlineData(T.Char, "4100", ValueKind.Char, "Char")]
    [InlineData(T.AnsiChar, "41", ValueKind.Char, "AnsiChar")]
    [InlineData(T.Single, "0000803F", ValueKind.Float, "Single")]
    [InlineData(T.Double, "000000000000F03F", ValueKind.Float, "Double")]
    [InlineData(T.Currency, "1027000000000000", ValueKind.Float, "Currency")]
    [InlineData(T.TDateTime, "000000000000F03F", ValueKind.Float, "TDateTime")]
    public async Task CharAndFloat_Node_IsModifiableLeaf(int type, string storage, ValueKind kind, string typeName)
    {
        var bed = new T();

        var info = bed.Info(await bed.DecodeHexAsync(type, storage, "Ch"));

        Assert.Equal(new VariableInfo("Ch", info.Value, typeName, kind, null, 0, true, "Ch", T.At), info);
    }
}
