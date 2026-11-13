using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Text;

[Trait("Phase", "1")]
public sealed class TextTests
{
    // --- TextSpan

    [Fact]
    public void TextSpan_End_IsStartPlusLength()
    {
        Assert.Equal(7, new TextSpan(3, 4).End);
        Assert.Equal(new TextSpan(3, 4), TextSpan.FromBounds(3, 7));
    }

    [Fact]
    public void TextSpan_Contains_IsHalfOpen()
    {
        var span = new TextSpan(3, 4);

        Assert.False(span.Contains(2));
        Assert.True(span.Contains(3));
        Assert.True(span.Contains(6));
        Assert.False(span.Contains(7));
        Assert.False(new TextSpan(3, 0).Contains(3));
    }

    [Fact]
    public void TextSpan_IntersectsWith_IncludesTouchingSpans()
    {
        var span = new TextSpan(3, 4); // [3,7)

        Assert.True(span.IntersectsWith(new TextSpan(5, 10)));
        Assert.True(span.IntersectsWith(new TextSpan(7, 2)));
        Assert.True(span.IntersectsWith(new TextSpan(0, 3)));
        Assert.True(span.IntersectsWith(new TextSpan(5, 0)));
        Assert.False(span.IntersectsWith(new TextSpan(8, 1)));
        Assert.False(span.IntersectsWith(new TextSpan(0, 2)));
    }

    [Fact]
    public void TextSpan_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextSpan.FromBounds(5, 4));
    }

    // --- LineMap (LANG-102: CR LF, LF and lone CR each count as one line break)

    [Theory]
    [Trait("Req", "LANG-102")]
    [InlineData("a\r\nbb\r\nccc")]
    [InlineData("a\nbb\nccc")]
    [InlineData("a\rbb\rccc")]
    [InlineData("a\r\nbb\nccc")]
    [InlineData("a\rbb\r\nccc")]
    public void LineMap_AllLineTerminators_GiveThreeLines(string text)
    {
        var lines = SourceText.From(@"C:\p\a.pas", text).Lines;

        Assert.Equal(3, lines.LineCount);
        Assert.Equal("a", Slice(text, lines.GetLineSpan(0)));
        Assert.Equal("bb", Slice(text, lines.GetLineSpan(1)));
        Assert.Equal("ccc", Slice(text, lines.GetLineSpan(2)));
    }

    [Fact]
    [Trait("Req", "LANG-102")]
    public void LineMap_LfCr_IsTwoLineBreaks()
    {
        var lines = SourceText.From(@"C:\p\a.pas", "a\n\rb").Lines;

        Assert.Equal(3, lines.LineCount);
        Assert.Equal(new TextSpan(2, 0), lines.GetLineSpan(1));
        Assert.Equal(new TextSpan(3, 1), lines.GetLineSpan(2));
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("x", 1)]
    [InlineData("x\r\n", 2)]
    [InlineData("\n\n", 3)]
    [InlineData("\r\r\n\r", 4)]
    public void LineMap_LineCount_IsLineBreaksPlusOne(string text, int expected)
    {
        Assert.Equal(expected, SourceText.From(@"C:\p\a.pas", text).Lines.LineCount);
    }

    [Fact]
    [Trait("Req", "LANG-101")]
    public void LineMap_GetLinePosition_IsZeroBasedUtf16()
    {
        // "😀" is two UTF-16 code units; a tab counts as one column (LANG-103).
        const string text = "ab\r\n\t😀x\rlast";
        var lines = SourceText.From(@"C:\p\a.pas", text).Lines;

        Assert.Equal(new LinePosition(0, 0), lines.GetLinePosition(0));
        Assert.Equal(new LinePosition(0, 2), lines.GetLinePosition(2));   // the CR
        Assert.Equal(new LinePosition(0, 3), lines.GetLinePosition(3));   // the LF of CR LF
        Assert.Equal(new LinePosition(1, 0), lines.GetLinePosition(4));   // tab
        Assert.Equal(new LinePosition(1, 3), lines.GetLinePosition(7));   // 'x' after the surrogate pair
        Assert.Equal(new LinePosition(2, 0), lines.GetLinePosition(9));
        Assert.Equal(new LinePosition(2, 4), lines.GetLinePosition(text.Length)); // end of text
    }

    [Theory]
    [InlineData("ab\r\n\t😀x\rlast")]
    [InlineData("\n\r\n\r")]
    [InlineData("")]
    [InlineData("single line")]
    public void LineMap_OffsetsAndPositions_RoundTrip(string text)
    {
        var lines = SourceText.From(@"C:\p\a.pas", text).Lines;

        for (var offset = 0; offset <= text.Length; offset++)
        {
            Assert.Equal(offset, lines.GetOffset(lines.GetLinePosition(offset)));
        }
    }

    [Fact]
    public void LineMap_OutOfRange_Throws()
    {
        var lines = SourceText.From(@"C:\p\a.pas", "ab\ncd").Lines;

        Assert.Throws<ArgumentOutOfRangeException>(() => lines.GetLinePosition(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => lines.GetLinePosition(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => lines.GetOffset(new LinePosition(2, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => lines.GetOffset(new LinePosition(0, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => lines.GetOffset(new LinePosition(1, 3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => lines.GetLineSpan(2));
    }

    [Fact]
    public void SourceText_Lines_IsCachedAndValueEqualityIgnoresIt()
    {
        var a = SourceText.From(@"C:\p\a.pas", "x\ny");
        var b = SourceText.From(@"c:/P/A.pas", "x\ny");

        Assert.Same(a.Lines, a.Lines);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, SourceText.From(@"C:\p\a.pas", "x\nz"));
    }

    // --- Paths (CMP-205)

    [Theory]
    [Trait("Req", "CMP-205")]
    [InlineData(@"C:\p\a.pas", @"C:\p\a.pas")]
    [InlineData(@"c:\p\a.pas", @"C:\p\a.pas")]
    [InlineData("c:/p/sub/a.pas", @"C:\p\sub\a.pas")]
    [InlineData(@"C:\p\.\sub\..\a.pas", @"C:\p\a.pas")]
    [InlineData(@"C:\p\\sub\\a.pas", @"C:\p\sub\a.pas")]
    [InlineData(@"C:\p\sub\", @"C:\p\sub")]
    [InlineData(@"C:\..\a.pas", @"C:\a.pas")]
    [InlineData(@"c:\", @"C:\")]
    [InlineData(@"\\server\share\x\..\y.pas", @"\\server\share\y.pas")]
    [InlineData("//server/share/y.pas", @"\\server\share\y.pas")]
    [InlineData(@"C:\Mixed\Case.PAS", @"C:\Mixed\Case.PAS")]
    public void PathUtil_Normalize_ProducesCanonicalAbsolutePath(string input, string expected)
    {
        Assert.Equal(expected, PathUtil.Normalize(input));
    }

    [Theory]
    [Trait("Req", "CMP-205")]
    [InlineData("a.pas")]
    [InlineData(@"sub\a.pas")]
    [InlineData(@"..\a.pas")]
    [InlineData(@"\rooted\no\drive.pas")]
    [InlineData(@"C:relative.pas")]
    [InlineData("C:")]
    [InlineData(@"\\server")]
    [InlineData("")]
    public void PathUtil_Normalize_RelativePath_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => PathUtil.Normalize(input));
    }

    [Fact]
    [Trait("Req", "CMP-205")]
    public void PathUtil_Comparison_IsOrdinalCaseInsensitive()
    {
        Assert.True(PathUtil.AreEqual(@"C:\P\A.pas", @"c:/p/a.PAS"));
        Assert.False(PathUtil.AreEqual(@"C:\p\a.pas", @"C:\p\b.pas"));
        Assert.Equal(StringComparer.OrdinalIgnoreCase, PathUtil.Comparer);
    }

    [Fact]
    [Trait("Req", "CMP-205")]
    public void SourceText_From_NormalisesPath()
    {
        Assert.Equal(@"C:\p\a.pas", SourceText.From(@"c:/p/./a.pas", "").Path);
        Assert.Throws<ArgumentException>(() => SourceText.From("a.pas", ""));
    }

    private static string Slice(string text, TextSpan span) => text.Substring(span.Start, span.Length);
}
