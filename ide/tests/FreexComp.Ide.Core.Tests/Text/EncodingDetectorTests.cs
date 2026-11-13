using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Text;

[Trait("Phase", "1")]
public sealed class EncodingDetectorTests
{
    private const string Full = "unit Enc;\n\ninterface\n\nconst\n  S = 'Grüße – ņ';\n\nimplementation\n\nend.\n";
    private const string Short = "unit Enc;\n\ninterface\n\nconst\n  S = 'Grüße';\n\nimplementation\n\nend.\n";

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "files", "encodings", name));

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-070")]
    [Trait("Req", "IDE-P1-071")]
    [Trait("Req", "IDE-P1-084")]
    public void DetectsBomAndUtf8AndFallback()
    {
        (string File, FileEncoding Encoding, string Text, bool Fallback)[] cases =
        [
            ("utf8.pas", FileEncoding.Utf8, Full, false),
            ("utf8bom.pas", FileEncoding.Utf8Bom, Full, false),
            ("utf16le.pas", FileEncoding.Utf16Le, Full, false),
            ("utf16be.pas", FileEncoding.Utf16Be, Full, false),
            ("cp1252.pas", FileEncoding.Windows1252, Short, true),
        ];
        foreach (var (file, encoding, text, fallback) in cases)
        {
            var bytes = Fixture(file);
            var result = EncodingDetector.Detect(bytes);
            Assert.False(result.IsBinary, file);
            Assert.Equal(encoding, result.Encoding);
            Assert.Equal(fallback, result.IsFallback);
            Assert.Equal(text, result.Text);                                // BOM stripped, nothing else changed (IDE-P1-078)
            Assert.Equal(bytes, FileEncodings.Encode(result.Text, encoding));  // BOM written back exactly when the encoding has one
            Assert.Equal(text, FileEncodings.Decode(bytes, encoding));
        }

        // Ids are the files.defaultEncoding values (settings.md).
        Assert.Equal(["utf-8", "utf-8-bom", "utf-16le", "utf-16be", "windows-1252"], Enum.GetValues<FileEncoding>().Select(FileEncodings.ToId));
        Assert.All(Enum.GetValues<FileEncoding>(), e => Assert.Equal(e, FileEncodings.FromId(FileEncodings.ToId(e))));
        Assert.Null(FileEncodings.FromId("utf-32"));

        // Strict UTF-8: overlong forms and encoded surrogates fall back to Windows-1252.
        Assert.Equal(FileEncoding.Windows1252, EncodingDetector.Detect([0x41, 0xC0, 0xAF]).Encoding);          // overlong '/'
        Assert.Equal(FileEncoding.Windows1252, EncodingDetector.Detect([0xED, 0xA0, 0x80]).Encoding);          // U+D800
        Assert.Equal(FileEncoding.Windows1252, EncodingDetector.Detect([0xE2, 0x82]).Encoding);                // truncated sequence
        Assert.Equal("AÀ¯", EncodingDetector.Detect([0x41, 0xC0, 0xAF]).Text);

        // Empty file and plain ASCII are UTF-8 without fallback; a UTF-8 BOM alone is an empty utf-8-bom file.
        var empty = EncodingDetector.Detect([]);
        Assert.Equal((FileEncoding.Utf8, false, ""), (empty.Encoding, empty.IsFallback, empty.Text));
        var bomOnly = EncodingDetector.Detect([0xEF, 0xBB, 0xBF]);
        Assert.Equal((FileEncoding.Utf8Bom, ""), (bomOnly.Encoding, bomOnly.Text));

        // UTF-16 without BOM is not detected: its NUL bytes make it binary (IDE-P1-071 rule 4).
        Assert.True(EncodingDetector.Detect(System.Text.Encoding.Unicode.GetBytes("abc")).IsBinary);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-073")]
    [Trait("Req", "IDE-P1-083")]
    public void Windows1252RoundTripsAllBytes()
    {
        var all = Enumerable.Range(0, 256).Select(b => (byte)b).ToArray();
        var text = FileEncodings.Decode(all, FileEncoding.Windows1252);
        Assert.Equal(256, text.Length);
        Assert.Equal(all, FileEncodings.Encode(text, FileEncoding.Windows1252));
        Assert.True(FileEncodings.CanEncode(text, FileEncoding.Windows1252));

        // Spot checks of the table: the defined 0x80-0x9F characters and the five undefined bytes as C1 controls.
        Assert.Equal('€', text[0x80]);
        Assert.Equal('–', text[0x96]);
        Assert.Equal('Ÿ', text[0x9F]);
        foreach (var undefined in new[] { 0x81, 0x8D, 0x8F, 0x90, 0x9D })
            Assert.Equal((char)undefined, text[undefined]);
        Assert.Equal('ü', text[0xFC]);

        // Characters outside the code page are detected, never written lossily.
        Assert.False(FileEncodings.CanEncode("Grüße ņ", FileEncoding.Windows1252));
        Assert.False(FileEncodings.CanEncode("\u0080", FileEncoding.Windows1252));                 // C1 U+0080 has no byte (0x80 is the euro sign)
        Assert.Throws<System.Text.EncoderFallbackException>(() => FileEncodings.Encode("ņ", FileEncoding.Windows1252));
        Assert.All(Enum.GetValues<FileEncoding>().Where(e => e != FileEncoding.Windows1252), e => Assert.True(FileEncodings.CanEncode("ņ😀", e)));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-072")]
    public void BinaryFileIsRejected()
    {
        Assert.True(EncodingDetector.Detect(Fixture("binary.bin")).IsBinary);

        // A NUL within the first 8 000 bytes makes the file binary, also with a UTF-8 BOM ...
        var nulAtEnd = Enumerable.Repeat((byte)'a', 8000).ToArray();
        nulAtEnd[7999] = 0;
        Assert.True(EncodingDetector.Detect(nulAtEnd).IsBinary);
        Assert.True(EncodingDetector.Detect([0xEF, 0xBB, 0xBF, (byte)'a', 0]).IsBinary);

        // ... but not after them, and not with a UTF-16 BOM.
        var nulLater = Enumerable.Repeat((byte)'a', 8001).ToArray();
        nulLater[8000] = 0;
        var late = EncodingDetector.Detect(nulLater);
        Assert.False(late.IsBinary);
        Assert.Equal(FileEncoding.Utf8, late.Encoding);
        Assert.False(EncodingDetector.Detect(Fixture("utf16le.pas")).IsBinary);
        Assert.False(EncodingDetector.Detect(Fixture("utf16be.pas")).IsBinary);
    }
}
