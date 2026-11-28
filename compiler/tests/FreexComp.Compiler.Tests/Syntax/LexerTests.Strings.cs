using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>lexical.md §7, LANG-130…134.</summary>
public sealed partial class LexerTests
{
    [Theory]
    [Trait("Req", "LANG-130")]
    [Trait("Req", "LANG-131")]
    [InlineData("'It''s'#33", "It's!")]         // lexical.md §9
    [InlineData("#65", "A")]                    // lexical.md §9: character literal
    [InlineData("''", "")]                      // lexical.md §9: empty string literal
    [InlineData("'abc'", "abc")]
    [InlineData("''''", "'")]
    [InlineData("'abc'''", "abc'")]
    [InlineData("'a'#13#10'b'", "a\r\nb")]      // LANG-131: 4 code units
    [InlineData("#13#10", "\r\n")]
    [InlineData("#$41", "A")]
    [InlineData("#$41#$42", "AB")]
    [InlineData("#$ffff", "￿")]
    [InlineData("#65535", "￿")]
    [InlineData("#0", "\0")]
    [InlineData("#00065", "A")]
    [InlineData("'x'#9'y'#$20", "x\ty ")]
    [InlineData("'{ not a comment }'", "{ not a comment }")]
    [InlineData("'// nor this'", "// nor this")]
    [InlineData("'äöü 日本'", "äöü 日本")]
    [InlineData("'\t'", "\t")]
    public void Strings_Literal_IsOneTokenWithConcatenatedValue(string text, string value)
    {
        var token = LexSingle(text);

        Assert.Equal(SyntaxKind.StringLiteralToken, token.Kind);
        Assert.Equal(text, token.Text);
        Assert.Equal(value, Assert.IsType<string>(token.Value));
        Assert.Equal(new TextSpan(0, text.Length), token.Span);
    }

    [Fact]
    [Trait("Req", "LANG-132")]
    public void Strings_CharacterAboveBmp_IsStoredAsSurrogatePair()
    {
        var token = LexSingle("'😀'");

        Assert.Equal("😀", token.Value);
        Assert.Equal(2, ((string)token.Value!).Length);
    }

    [Theory]
    [Trait("Req", "LANG-131")]
    [Trait("Req", "LANG-997")]
    [InlineData("'a' 'b'", new[] { "'a'", "'b'" })]
    [InlineData("'a'\r\n'b'", new[] { "'a'", "'b'" })]
    [InlineData("#13 #10", new[] { "#13", "#10" })]
    [InlineData("'a'+'b'", new[] { "'a'", "+", "'b'" })]
    [InlineData("'a'b", new[] { "'a'", "b" })]
    [InlineData("'a'#", new[] { "'a'", "#" })]
    [InlineData("#", new[] { "#" })]
    [InlineData("# 1", new[] { "#", "1" })]
    [InlineData("#$", new[] { "#", "$" })]
    [InlineData("#$G", new[] { "#", "$", "G" })]
    [InlineData("#65x", new[] { "#65", "x" })]
    [InlineData("#$41G", new[] { "#$41", "G" })]
    [InlineData("x:='a';", new[] { "x", ":=", "'a'", ";" })]
    [InlineData("'abc'.Len", new[] { "'abc'", ".", "Len" })]               // LANG-997 (e)
    [InlineData("#65.IsDigit", new[] { "#65", ".", "IsDigit" })]
    public void Strings_TokenBoundaries(string text, string[] expected)
    {
        Assert.Equal(expected, Lex(text).Select(t => t.Text));
    }

    [Theory]
    [Trait("Req", "LANG-130")]
    [InlineData("'abc\nx", "'abc", "abc", 0)]
    [InlineData("'abc\r\nx", "'abc", "abc", 0)]
    [InlineData("'abc\rx", "'abc", "abc", 0)]
    [InlineData("'abc", "'abc", "abc", 0)]                   // end of file
    [InlineData("'", "'", "", 0)]
    [InlineData("'ok'#10'bad\nx", "'ok'#10'bad", "ok\nbad", 7)]
    [InlineData("'it''s\nx", "'it''s", "it's", 0)]
    public void Strings_Unterminated_IsE1020AtTheOpeningQuote(string text, string literal, string value, int quote)
    {
        var (tokens, diagnostics) = LexAll(text);

        Assert.Equal(SyntaxKind.StringLiteralToken, tokens[0].Kind);
        Assert.Equal(literal, tokens[0].Text);
        Assert.Equal(value, tokens[0].Value);
        var d = Assert.Single(diagnostics);
        Assert.Equal("E1020", d.Code);
        Assert.Equal("Unterminated string", d.Message);
        Assert.Equal(new TextSpan(quote, 1), d.Location.Span);
        if (text.Contains('x', StringComparison.Ordinal))
        {
            // The line break ends the literal; lexing continues normally on the next line.
            Assert.Equal("x", tokens[1].Text);
            Assert.Equal(SyntaxKind.IdentifierToken, tokens[1].Kind);
        }
    }

    [Theory]
    [Trait("Req", "LANG-132")]
    [InlineData("#65536", 0, 6)]
    [InlineData("#$10000", 0, 7)]
    [InlineData("#99999999999999999999999", 0, 24)]
    [InlineData("'a'#70000'b'", 3, 6)]
    public void Strings_CharCodeOutOfRange_IsE1021AtThePart(string text, int start, int length)
    {
        var (tokens, diagnostics) = LexAll(text);

        Assert.Equal(SyntaxKind.StringLiteralToken, tokens[0].Kind);
        Assert.Equal(text, tokens[0].Text);
        var d = Assert.Single(diagnostics);
        Assert.Equal("E1021", d.Code);
        Assert.Equal("Character code out of range (0..65535)", d.Message);
        Assert.Equal(new TextSpan(start, length), d.Location.Span);
    }

    [Fact]
    [Trait("Req", "LANG-132")]
    public void Strings_CharCodeOutOfRange_ContributesReplacementCharacter()
    {
        var (tokens, _) = LexAll("'a'#70000'b'");

        Assert.Equal("a�b", tokens[0].Value);
    }

    [Fact]
    [Trait("Req", "LANG-134")]
    public void Strings_TripleQuote_FollowsTheQuotedGrammar()
    {
        // Multiline literals are Phase 8; ''' is "opening quote, doubled quote" and runs to the line break.
        var (tokens, diagnostics) = LexAll("'''\nx");

        Assert.Equal("'''", tokens[0].Text);
        Assert.Equal("'", tokens[0].Value);
        Assert.Equal("E1020", Assert.Single(diagnostics).Code);
    }
}
