using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>lexical.md §6, LANG-125…129.</summary>
public sealed partial class LexerTests
{
    [Theory]
    [Trait("Req", "LANG-125")]
    [InlineData("0", 0L)]
    [InlineData("7", 7L)]
    [InlineData("1234567890", 1234567890L)]
    [InlineData("1_000_000", 1000000L)]
    [InlineData("007", 7L)]
    [InlineData("$FF", 255L)]
    [InlineData("$ff", 255L)]
    [InlineData("$FF_FF", 65535L)]      // lexical.md §9
    [InlineData("$0", 0L)]
    [InlineData("%1010", 10L)]          // lexical.md §9
    [InlineData("%1111_0000", 240L)]
    [InlineData("&17", 15L)]
    [InlineData("&0", 0L)]
    [InlineData("&777", 511L)]
    [InlineData("2147483648", 2147483648L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("$7FFFFFFFFFFFFFFF", long.MaxValue)]
    public void Numbers_Integer_ValueIsLong(string text, long value)
    {
        var token = LexSingle(text);

        Assert.Equal(SyntaxKind.IntegerLiteralToken, token.Kind);
        Assert.Equal(text, token.Text);
        Assert.Equal(value, Assert.IsType<long>(token.Value));
    }

    [Theory]
    [Trait("Req", "LANG-126")]
    [InlineData("9223372036854775808", 9223372036854775808UL)]
    [InlineData("18446744073709551615", ulong.MaxValue)]
    [InlineData("$FFFFFFFFFFFFFFFF", ulong.MaxValue)]
    [InlineData("$8000_0000_0000_0000", 9223372036854775808UL)]
    public void Numbers_IntegerAboveInt64_ValueIsULong(string text, ulong value)
    {
        Assert.Equal(value, Assert.IsType<ulong>(LexSingle(text).Value));
    }

    [Theory]
    [Trait("Req", "LANG-126")]
    [InlineData("18446744073709551616")]
    [InlineData("99999999999999999999999")]
    [InlineData("$1_0000_0000_0000_0000")]
    [InlineData("%1_0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("&2000000000000000000000")]
    public void Numbers_IntegerAboveUInt64_IsE1011(string text)
    {
        var (tokens, diagnostics) = LexAll(text);

        Assert.Equal(SyntaxKind.IntegerLiteralToken, tokens[0].Kind);
        Assert.Equal(text, tokens[0].Text);
        var d = Assert.Single(diagnostics);
        Assert.Equal("E1011", d.Code);
        Assert.Equal("Integer constant too large", d.Message);
        Assert.Equal(new TextSpan(0, text.Length), d.Location.Span);
    }

    [Theory]
    [Trait("Req", "LANG-127")]
    [InlineData("1.5e3", 1500.0)]       // lexical.md §9
    [InlineData("1.5", 1.5)]
    [InlineData("0.1", 0.1)]
    [InlineData("1e10", 1e10)]
    [InlineData("1E-3", 0.001)]
    [InlineData("2.5E+2", 250.0)]
    [InlineData("1_000.000_5", 1000.0005)]
    [InlineData("1e-400", 0.0)]         // underflow is not an error
    [InlineData("1.7976931348623157e308", double.MaxValue)]
    public void Numbers_Real_ValueIsCorrectlyRoundedDouble(string text, double value)
    {
        var token = LexSingle(text);

        Assert.Equal(SyntaxKind.RealLiteralToken, token.Kind);
        Assert.Equal(text, token.Text);
        Assert.Equal(value, Assert.IsType<double>(token.Value));
    }

    [Fact]
    [Trait("Req", "LANG-127")]
    public void Numbers_Real_RoundsHalfToEven()
    {
        // 2^53 + 1 lies exactly halfway between 2^53 and 2^53 + 2: ties go to the even mantissa (2^53).
        Assert.Equal(9007199254740992.0, LexSingle("9007199254740993.0").Value);
        // 2^53 + 3 lies halfway between 2^53 + 2 and 2^53 + 4: ties go to 2^53 + 4.
        Assert.Equal(9007199254740996.0, LexSingle("9007199254740995.0").Value);
    }

    [Theory]
    [Trait("Req", "LANG-127")]
    [InlineData("1e309")]
    [InlineData("1.8e308")]
    public void Numbers_RealOverflow_IsE1012(string text)
    {
        var (tokens, diagnostics) = LexAll(text);

        Assert.Equal(SyntaxKind.RealLiteralToken, tokens[0].Kind);
        var d = Assert.Single(diagnostics);
        Assert.Equal("E1012", d.Code);
        Assert.Equal(new TextSpan(0, text.Length), d.Location.Span);
    }

    [Theory]
    [Trait("Req", "LANG-125")]
    [InlineData("1_", 1)]
    [InlineData("1__2", 1)]
    [InlineData("1_000_", 5)]
    [InlineData("$FF_", 3)]
    [InlineData("$F__F", 2)]
    [InlineData("%1_", 2)]
    [InlineData("1_.5", 1)]
    [InlineData("1.5_", 3)]
    [InlineData("1._5", -1)]   // '.' not followed by a digit: integer 1, '.', identifier _5 — no E1010
    public void Numbers_MisplacedSeparator_IsE1010AtTheUnderscore(string text, int at)
    {
        var (tokens, diagnostics) = LexAll(text);

        if (at < 0)
        {
            Assert.Empty(diagnostics);
            Assert.Equal([SyntaxKind.IntegerLiteralToken, SyntaxKind.DotToken, SyntaxKind.IdentifierToken, SyntaxKind.EndOfFileToken], tokens.Select(t => t.Kind));
            return;
        }

        Assert.Equal(text, tokens[0].Text);
        var d = Assert.Single(diagnostics);
        Assert.Equal("E1010", d.Code);
        Assert.Equal("Misplaced digit separator '_'", d.Message);
        Assert.Equal(new TextSpan(at, 1), d.Location.Span);
    }

    [Fact]
    [Trait("Req", "LANG-125")]
    public void Numbers_SeparatorInExponent_IsE1013()
    {
        var (tokens, diagnostics) = LexAll("3.0e0_0");

        Assert.Equal("3.0e0_0", Assert.Single(tokens, t => t.Kind == SyntaxKind.RealLiteralToken).Text);
        Assert.Equal("Invalid numeric literal '3.0e0_0'", Assert.Single(diagnostics).Message);
    }

    [Theory]
    [Trait("Req", "LANG-128")]
    [InlineData("12abc", SyntaxKind.IntegerLiteralToken, 12L)]
    [InlineData("1_x", SyntaxKind.IntegerLiteralToken, 1L)]
    [InlineData("$FFG", SyntaxKind.IntegerLiteralToken, 255L)]
    [InlineData("$FFxyz9", SyntaxKind.IntegerLiteralToken, 255L)]
    [InlineData("%102", SyntaxKind.IntegerLiteralToken, 2L)]
    [InlineData("%1a", SyntaxKind.IntegerLiteralToken, 1L)]
    [InlineData("&78", SyntaxKind.IntegerLiteralToken, 7L)]
    [InlineData("1e", SyntaxKind.IntegerLiteralToken, 1L)]
    [InlineData("1ex", SyntaxKind.IntegerLiteralToken, 1L)]
    [InlineData("1.5e3x", SyntaxKind.RealLiteralToken, 1500.0)]
    [InlineData("1.5ü", SyntaxKind.RealLiteralToken, 1.5)]
    public void Numbers_FollowedByLetter_IsOneE1013AndLettersAreSkipped(string text, SyntaxKind kind, object value)
    {
        var (tokens, diagnostics) = LexAll(text + " ;");

        Assert.Equal([kind, SyntaxKind.SemicolonToken, SyntaxKind.EndOfFileToken], tokens.Select(t => t.Kind));
        Assert.Equal(text, tokens[0].Text);
        Assert.Equal(value, tokens[0].Value);
        var d = Assert.Single(diagnostics);
        Assert.Equal("E1013", d.Code);
        Assert.Equal($"Invalid numeric literal '{text}'", d.Message);
        Assert.Equal(new TextSpan(0, text.Length), d.Location.Span);
    }

    [Theory]
    [Trait("Req", "LANG-105")]
    [Trait("Req", "LANG-128")]
    [InlineData("1..5", new[] { "1", "..", "5" })]          // lexical.md §9
    [InlineData("x.1", new[] { "x", ".", "1" })]            // lexical.md §9
    [InlineData("(.1.)", new[] { "(.", "1", ".)" })]        // lexical.md §9
    [InlineData("1.", new[] { "1", "." })]
    [InlineData("1.e5", new[] { "1", ".", "e5" })]
    [InlineData("1.x", new[] { "1", ".", "x" })]
    [InlineData("1.5.3", new[] { "1.5", ".", "3" })]
    [InlineData("1.5..2", new[] { "1.5", "..", "2" })]
    [InlineData("a[1..2]", new[] { "a", "[", "1", "..", "2", "]" })]
    [InlineData("1e+", new[] { "1e", "+" })]               // incomplete exponent: covered by E1013 (see below)
    [InlineData("-5", new[] { "-", "5" })]                  // LANG-129: no sign in a literal
    [InlineData("1+2", new[] { "1", "+", "2" })]
    [InlineData("1e5+2", new[] { "1e5", "+", "2" })]
    [InlineData("1E5-2", new[] { "1E5", "-", "2" })]
    [InlineData("$", new[] { "$" })]
    [InlineData("$ 1", new[] { "$", "1" })]
    [InlineData("$_1", new[] { "$", "_1" })]
    [InlineData("&8", new[] { "&", "8" })]
    [InlineData("&_1", new[] { "&_1" })]
    public void Numbers_TokenBoundaries(string text, string[] expected)
    {
        var (tokens, _) = LexAll(text);

        Assert.Equal(expected, tokens.SkipLast(1).Select(t => t.Text));
    }

    [Theory]
    [InlineData("1e", "1e")]
    [InlineData("1e+", "1e")]
    [InlineData("1.5e-", "1.5e")]
    public void Numbers_IncompleteExponent_IsE1013(string text, string literal)
    {
        var d = Assert.Single(LexAll(text).Diagnostics);

        Assert.Equal($"Invalid numeric literal '{literal}'", d.Message);
    }

    [Fact]
    public void Numbers_PercentWithoutBinaryDigit_IsIllegalCharacter()
    {
        var (tokens, diagnostics) = LexAll("% 1");

        Assert.Equal([SyntaxKind.IntegerLiteralToken, SyntaxKind.EndOfFileToken], tokens.Select(t => t.Kind));
        Assert.Equal("Illegal character '%' (U+0025)", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void Numbers_ValueTypesAreLongULongOrDouble()
    {
        Assert.IsType<long>(LexSingle("1").Value);
        Assert.IsType<ulong>(LexSingle("$FFFFFFFFFFFFFFFF").Value);
        Assert.IsType<double>(LexSingle("1.0").Value);
    }
}
