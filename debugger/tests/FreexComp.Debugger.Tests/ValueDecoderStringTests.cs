using System.Text;
using FreexComp.Debugger.Tests.TestSupport;
using T = FreexComp.Debugger.Tests.TestSupport.ValueTestBed;

namespace FreexComp.Debugger.Tests;

/// <summary>Strings of every flavour and their literal rendering (Pascal values §4: DBG-159 … DBG-161).</summary>
public sealed class ValueDecoderStringTests
{
    private const ulong Payload = 0x21000;

    /// <summary>A bench whose variable at <see cref="T.At"/> holds a pointer to <see cref="Payload"/>.</summary>
    private static T Bed(ValueFormatOptions? options = null, string? abi = null)
    {
        var bed = new T(options, abi);
        bed.Memory.PutPointer(T.At, Payload);
        return bed;
    }

    // ---- UnicodeString ----

    [Theory]
    [Trait("Req", "DBG-160")]
    // The rows of the table in §4.2.
    [InlineData("Héllo", "'Héllo'")]
    [InlineData("it's", "'it''s'")]
    [InlineData("a\r\nb", "'a'#13#10'b'")]
    [InlineData("\t", "#9")]
    [InlineData("", "''")]
    // Runs of printable and non-printable units, without separators.
    [InlineData("\r\n", "#13#10")]
    [InlineData("a\tb\0c", "'a'#9'b'#0'c'")]
    [InlineData("'", "''''")]
    [InlineData("''", "''''''")]
    [InlineData("\u007Fx\u009F", "#127'x'#159")]
    // A valid surrogate pair is printable.
    [InlineData("a😀b", "'a😀b'")]
    public async Task Unicode_LiteralRendering(string text, string expected)
    {
        var bed = Bed();
        bed.Memory.PutUnicodeString(Payload, text);

        var node = await bed.DecodeAsync(T.String, T.At, "S");

        Assert.Equal(
            new VariableInfo("S", expected, "string", ValueKind.String, null, 0, false, "S", T.At),
            bed.Info(node));
        Assert.Equal(text, node.Text);
        Assert.Equal(text.Length, node.Length);
    }

    /// <summary>"{HHHH}" stands for that code unit: such units do not survive in an attribute string or a source file.</summary>
    private static string Expand(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, "{([0-9A-F]{4})}", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());

    [Theory]
    [Trait("Req", "DBG-160")]
    // 160 and above is printable.
    [InlineData("{20AC} {00A0}", "'{20AC} {00A0}'")]
    // A valid surrogate pair is printable; a lone surrogate and the two non-characters are shown by number.
    [InlineData("a{D83D}{DE00}b", "'a{D83D}{DE00}b'")]
    [InlineData("a{D83D}b", "'a'#55357'b'")]
    [InlineData("{DE00}{D83D}", "#56832#55357")]
    [InlineData("{D83D}", "#55357")]
    [InlineData("{D83D}{D83D}{DE00}", "#55357'{D83D}{DE00}'")]
    [InlineData("x{FFFE}{FFFF}", "'x'#65534#65535")]
    [InlineData("{FFFD}", "'{FFFD}'")]
    public async Task Unicode_LiteralRendering_SpecialUnits(string text, string expected)
    {
        text = Expand(text);
        var bed = Bed();
        bed.Memory.PutUnicodeString(Payload, text);

        var node = await bed.DecodeAsync(T.String);

        // Code units are kept as they are, unpaired surrogates included.
        Assert.Equal(text, node.Text);
        Assert.Equal(Expand(expected), bed.Format(node));
    }

    [Fact]
    [Trait("Req", "DBG-159")]
    public async Task Unicode_Nil_IsEmpty()
    {
        var bed = new T();
        bed.Memory.PutPointer(T.At, 0);

        var node = await bed.DecodeAsync(T.String, T.At, "S");

        // The address is the variable's, not the payload's; a string is a leaf and not modifiable.
        Assert.Equal(new VariableInfo("S", "''", "string", ValueKind.String, null, 0, false, "S", T.At), bed.Info(node));
        // Only the variable itself was read.
        Assert.Equal(["20000+8"], bed.Memory.Reads);
    }

    [Fact]
    [Trait("Req", "DBG-160")]
    public async Task Unicode_Truncated_EllipsisAndLength()
    {
        var bed = Bed(new ValueFormatOptions { MaxStringChars = 4 });
        bed.Memory.PutUnicodeString(Payload, new string('x', 5000));

        var node = await bed.DecodeAsync(T.String);

        Assert.Equal("'xxxx'… (Length: 5000)", bed.Format(node));
        // Only the shown part of the payload is read: the variable, the length field, 4 units.
        Assert.Equal(["20000+8", "20FF4+4", "21000+8"], bed.Memory.Reads);

        // Exactly the limit: not truncated.
        var exact = Bed(new ValueFormatOptions { MaxStringChars = 4 });
        exact.Memory.PutUnicodeString(Payload, "abcd");
        Assert.Equal("'abcd'", exact.Format(await exact.DecodeAsync(T.String)));
        var over = Bed(new ValueFormatOptions { MaxStringChars = 4 });
        over.Memory.PutUnicodeString(Payload, "abc'e");
        Assert.Equal("'abc'''… (Length: 5)", over.Format(await over.DecodeAsync(T.String)));
        // The default limit is 1024 characters.
        var standard = Bed();
        standard.Memory.PutUnicodeString(Payload, new string('y', 1025));
        Assert.Equal("'" + new string('y', 1024) + "'… (Length: 1025)", standard.Format(await standard.DecodeAsync(T.String)));
    }

    [Theory]
    [Trait("Req", "DBG-159")]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(268_435_456)]
    [InlineData(int.MaxValue)]
    public async Task Unicode_InvalidLength_ErrorNode(int length)
    {
        var bed = Bed();
        bed.Memory.PutUnicodeString(Payload, "abc", length);

        var node = await bed.DecodeAsync(T.String, T.At, "S");

        Assert.Equal(
            new VariableInfo("S", "<invalid string at $0000000000021000>", "string", ValueKind.Error, null, 0, false, null, T.At),
            bed.Info(node));
    }

    [Fact]
    [Trait("Req", "DBG-159")]
    public async Task Unicode_PartialRead_PrefixAndEllipsis()
    {
        // The payload starts 16 bytes before unreadable memory (a 256-byte boundary): 8 of 20 units are readable.
        const ulong payload = 0x210F0;
        var bed = new T();
        bed.Memory.PutPointer(T.At, payload).PutInt32(payload - 12, 20).Put(payload, Encoding.Unicode.GetBytes("abcdefgh"));

        var node = await bed.DecodeAsync(T.String);

        // The readable prefix and an ellipsis; no length suffix: the string is not longer than the display limit.
        Assert.Equal("'abcdefgh'…", bed.Format(node));
        Assert.True(node.Partial);
        Assert.Equal(ValueKind.String, node.Kind);

        // Longer than the limit as well: both marks.
        var both = new T(new ValueFormatOptions { MaxStringChars = 12 });
        both.Memory.PutPointer(T.At, payload).PutInt32(payload - 12, 20).Put(payload, Encoding.Unicode.GetBytes("abcdefgh"));
        Assert.Equal("'abcdefgh'… (Length: 20)", both.Format(await both.DecodeAsync(T.String)));
    }

    [Fact]
    [Trait("Req", "DBG-152")]
    public async Task Unicode_Unreadable_ErrorNode()
    {
        // The variable itself.
        var bed = new T();
        Assert.Equal("<unreadable memory at $0000000000020000>", bed.Format(await bed.DecodeAsync(T.String)));

        // The length field of the payload (a dangling pointer).
        bed = Bed();
        var node = await bed.DecodeAsync(T.String);
        Assert.Equal(ValueKind.Error, node.Kind);
        Assert.Equal("<unreadable memory at $0000000000020FF4>", bed.Format(node));

        // The length is readable but no unit of the payload is.
        bed = Bed();
        bed.Memory.PutInt32(Payload - 12, 3);
        Assert.Equal("<unreadable memory at $0000000000021000>", bed.Format(await bed.DecodeAsync(T.String)));
    }

    [Fact]
    public async Task Unicode_LengthInBytes_DividedByCharSize()
    {
        var abi = PdiFixture.Abi
            .Replace("\"lengthOffset\": -12, \"lengthSize\": 4, \"lengthUnit\": \"chars\"", "\"lengthOffset\": -4, \"lengthSize\": 2, \"lengthUnit\": \"bytes\"", StringComparison.Ordinal);
        var bed = Bed(abi: abi);
        // A 16-bit byte count directly before the payload: 6 bytes = 3 units.
        bed.Memory.Put(Payload - 4, 6, 0).Put(Payload, Encoding.Unicode.GetBytes("abcdef"));

        Assert.Equal("'abc'", bed.Format(await bed.DecodeAsync(T.String)));
    }

    // ---- AnsiString ----

    [Theory]
    [Trait("Req", "DBG-161")]
    // UTF-8 for code page 65001 and for 0.
    [InlineData(65001, "48C3A96C6C6F", "'Héllo'")]
    [InlineData(0, "48C3A96C6C6F", "'Héllo'")]
    [InlineData(65001, "6974277309", "'it''s'#9")]
    // Invalid sequences become U+FFFD.
    [InlineData(65001, "61FF62", "'a{FFFD}b'")]
    // UTF-16LE.
    [InlineData(1200, "4800E900", "'Hé'")]
    // A single-byte code page from the code-page provider.
    [InlineData(1252, "48E96C6C6F80", "'Héllo€'")]
    [InlineData(1251, "CFF0E8E2E5F2", "'Привет'")]
    [InlineData(437, "8182", "'üé'")]
    // An unknown code page: Latin-1.
    [InlineData(12345, "48E9", "'Hé'")]
    [InlineData(65001, "", "''")]
    public async Task Ansi_CodePages(int codePage, string payloadHex, string expected)
    {
        var bed = Bed();
        bed.Memory.PutAnsiString(Payload, Convert.FromHexString(payloadHex), (ushort)codePage);

        var node = await bed.DecodeAsync(T.AnsiString, T.At, "A");

        Assert.Equal(new VariableInfo("A", Expand(expected), "AnsiString", ValueKind.String, null, 0, false, "A", T.At), bed.Info(node));
    }

    /// <summary>Code page 1252 differs from Latin-1 in 0x80 … 0x9F; the five bytes it leaves undefined are the C1 controls.</summary>
    [Theory]
    [Trait("Req", "DBG-161")]
    [InlineData("80", "'€'")]
    [InlineData("8A", "'Š'")]
    [InlineData("9F", "'Ÿ'")]
    [InlineData("808A9F", "'€ŠŸ'")]
    [InlineData("82838485868788898B8C8E", "'‚ƒ„…†‡ˆ‰‹ŒŽ'")]
    [InlineData("9192939495969798999A9B9C9E", "'‘’“”•–—˜™š›œž'")]
    [InlineData("41A0E9FF", "'A éÿ'")]
    public async Task Ansi_CodePage1252_HighRange(string payloadHex, string expected)
    {
        var bed = Bed();
        bed.Memory.PutAnsiString(Payload, Convert.FromHexString(payloadHex), 1252);

        var node = await bed.DecodeAsync(T.AnsiString, T.At, "A");

        Assert.Equal(expected, bed.Info(node).Value);
    }

    /// <summary>The 1252 table is the decoder's own: it does not depend on a code-page provider being registered.</summary>
    [Fact]
    [Trait("Req", "DBG-161")]
    public void CodePage1252_OwnTable_MatchesEveryByte()
    {
        var all = Enumerable.Range(0, 256).Select(b => (byte)b).ToArray();

        var text = FreexComp.Debugger.Values.ValueDecoder.DecodeCodePage1252(all);

        Assert.Equal(256, text.Length);
        Assert.Equal(Encoding.Latin1.GetString(all, 0, 0x80), text[..0x80]);
        Assert.Equal(
            "€‚ƒ„…†‡ˆ‰Š‹ŒŽ"
            + "‘’“”•–—˜™š›œžŸ",
            text[0x80..0xA0]);
        Assert.Equal(Encoding.Latin1.GetString(all, 0xA0, 0x60), text[0xA0..]);
    }

    [Fact]
    [Trait("Req", "DBG-161")]
    public async Task Ansi_Nil_InvalidLength_Truncated_Unreadable()
    {
        var bed = new T();
        bed.Memory.PutPointer(T.At, 0);
        Assert.Equal("''", bed.Format(await bed.DecodeAsync(T.AnsiString)));

        bed = Bed();
        bed.Memory.PutAnsiString(Payload, [65], 1252, length: -5);
        var invalid = await bed.DecodeAsync(T.AnsiString);
        Assert.Equal(ValueKind.Error, invalid.Kind);
        Assert.Equal("<invalid string at $0000000000021000>", bed.Format(invalid));

        // The length (in bytes) is shown when the text is cut at the display limit.
        bed = Bed(new ValueFormatOptions { MaxStringChars = 4 });
        bed.Memory.PutAnsiString(Payload, Encoding.ASCII.GetBytes(new string('x', 5000)), 1252);
        Assert.Equal("'xxxx'… (Length: 5000)", bed.Format(await bed.DecodeAsync(T.AnsiString)));
        // Multi-byte characters: 6 bytes are 3 characters, which fit.
        bed = Bed(new ValueFormatOptions { MaxStringChars = 4 });
        bed.Memory.PutAnsiString(Payload, Encoding.UTF8.GetBytes("ééé"), 65001);
        Assert.Equal("'ééé'", bed.Format(await bed.DecodeAsync(T.AnsiString)));

        bed = Bed();
        Assert.Equal("<unreadable memory at $0000000000020FF4>", bed.Format(await bed.DecodeAsync(T.AnsiString)));
        bed = Bed();
        bed.Memory.PutInt32(Payload - 12, 3).Put(Payload - 8, 0xE4, 0x04);
        Assert.Equal("<unreadable memory at $0000000000021000>", bed.Format(await bed.DecodeAsync(T.AnsiString)));
    }

    // ---- ShortString ----

    [Theory]
    [Trait("Req", "DBG-161")]
    [InlineData("03 616263 00000000000000", "'abc'")]
    [InlineData("00 61626300000000000000", "''")]
    // Bytes after the length are not part of the string.
    [InlineData("02 6162635A5A5A5A5A5A5A", "'ab'")]
    // The length byte is clamped to the declared maximum.
    [InlineData("C8 30313233343536373839", "'0123456789'")]
    // UTF-8; an invalid byte is shown as its Latin-1 character.
    [InlineData("03 48C3A9 00000000000000", "'Hé'")]
    [InlineData("04 48E9C3A9 000000000000", "'Héé'")]
    [InlineData("03 27090A 00000000000000", "''''#9#10")]
    public async Task Short_InlineStorage(string storageHex, string expected)
    {
        var bed = new T();

        var node = await bed.DecodeHexAsync(T.ShortString10, storageHex, "Sh");

        // The anonymous type is spelled string[N]; the storage is inline (one read of size bytes).
        Assert.Equal(new VariableInfo("Sh", expected, "string[10]", ValueKind.String, null, 0, false, "Sh", T.At), bed.Info(node));
        Assert.Equal(["20000+11"], bed.Memory.Reads);
    }

    [Fact]
    public async Task Short_Unreadable_ErrorNode()
    {
        var bed = new T();
        bed.Memory.PutHex(T.At, "03616263");

        Assert.Equal("<unreadable memory at $0000000000020000>", bed.Format(await bed.DecodeAsync(T.ShortString10)));
    }

    // ---- PChar / PAnsiChar ----

    [Fact]
    [Trait("Req", "DBG-161")]
    public async Task PChar_AddressAndText()
    {
        var bed = Bed();
        bed.Memory.Put(Payload, Encoding.Unicode.GetBytes("Hi'\t\0junk")).Map(Payload, 256);

        var node = await bed.DecodeAsync(T.PChar, T.At, "P");

        Assert.Equal(
            new VariableInfo("P", "$0000000000021000 'Hi'''#9", "PChar", ValueKind.String, null, 0, false, "P", T.At),
            bed.Info(node));

        // An empty string still shows where it is.
        bed = Bed();
        bed.Memory.Map(Payload, 256);
        Assert.Equal("$0000000000021000 ''", bed.Format(await bed.DecodeAsync(T.PChar)));
    }

    [Fact]
    [Trait("Req", "DBG-161")]
    public async Task PChar_Nil()
    {
        var bed = new T();
        bed.Memory.PutPointer(T.At, 0);

        Assert.Equal("nil", bed.Format(await bed.DecodeAsync(T.PChar)));
        Assert.Equal("nil", bed.Format(await bed.DecodeAsync(T.PAnsiChar)));
        Assert.Equal(["20000+8", "20000+8"], bed.Memory.Reads);
    }

    [Fact]
    [Trait("Req", "DBG-161")]
    public async Task PChar_ReadInSteps_UntilTerminatorOrLimit()
    {
        // 300 units starting 16 bytes before a 256-byte boundary: read in steps that end at the boundaries.
        const ulong start = 0x210F0;
        var text = string.Concat(Enumerable.Range(0, 300).Select(i => (char)('a' + i % 26)));
        var bed = new T();
        bed.Memory.PutPointer(T.At, start).Put(start, Encoding.Unicode.GetBytes(text + "\0")).Map(start, 0x310);

        Assert.Equal("$00000000000210F0 '" + text + "'", bed.Format(await bed.DecodeAsync(T.PChar)));
        Assert.Equal(["20000+8", "210F0+16", "21100+256", "21200+256", "21300+256"], bed.Memory.Reads);

        // No terminator within the display limit: cut, with an ellipsis (the length is unknown).
        var cut = new T(new ValueFormatOptions { MaxStringChars = 4 });
        cut.Memory.PutPointer(T.At, start).Put(start, Encoding.Unicode.GetBytes(text + "\0")).Map(start, 0x310);
        Assert.Equal("$00000000000210F0 'abcd'…", cut.Format(await cut.DecodeAsync(T.PChar)));
        Assert.Equal(["20000+8", "210F0+16"], cut.Memory.Reads);
    }

    [Fact]
    [Trait("Req", "DBG-152")]
    public async Task PChar_Unreadable()
    {
        // Nothing readable at the target: an error node.
        var bed = Bed();
        var node = await bed.DecodeAsync(T.PChar, T.At, "P");
        Assert.Equal(
            new VariableInfo("P", "<unreadable memory at $0000000000021000>", "PChar", ValueKind.Error, null, 0, false, null, T.At),
            bed.Info(node));

        // The text runs into unreadable memory before a terminator: the readable prefix and an ellipsis.
        const ulong start = 0x210FA;
        bed = new T();
        bed.Memory.PutPointer(T.At, start).Put(start, Encoding.Unicode.GetBytes("abc"));
        Assert.Equal("$00000000000210FA 'abc'…", bed.Format(await bed.DecodeAsync(T.PChar)));
    }

    [Fact]
    [Trait("Req", "DBG-161")]
    public async Task PChar_Ansi_Latin1Bytes()
    {
        var bed = Bed();
        bed.Memory.Put(Payload, 0x48, 0xE9, 0x27, 0x09, 0x80, 0x00, 0x41).Map(Payload, 256);

        var node = await bed.DecodeAsync(T.PAnsiChar, T.At, "P");

        // 8-bit units, each the Latin-1 character of its code.
        Assert.Equal(
            new VariableInfo("P", "$0000000000021000 'Hé'''#9#128", "PAnsiChar", ValueKind.String, null, 0, false, "P", T.At),
            bed.Info(node));

        // An odd start address: steps still end at the 256-byte boundaries.
        const ulong start = 0x210FD;
        bed = new T();
        bed.Memory.PutPointer(T.At, start).Put(start, Encoding.ASCII.GetBytes("abcdef\0")).Map(start, 0x103);
        Assert.Equal("$00000000000210FD 'abcdef'", bed.Format(await bed.DecodeAsync(T.PAnsiChar)));
        Assert.Equal(["20000+8", "210FD+3", "21100+256"], bed.Memory.Reads);
    }
}
