using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>Comments, whitespace and trivia attachment (LANG-103, 106, 140…142); public Lexer API (CMP-222).</summary>
public sealed partial class LexerTests
{
    private static ImmutableArray<SyntaxToken> Tokenize(string text) => Lexer.Tokenize(SourceText.From(Path, text));

    private static (SyntaxKind Kind, string Text)[] Pieces(ImmutableArray<SyntaxTrivia> trivia) =>
        [.. trivia.Select(t => (t.Kind, t.Text))];

    private static string FullText(SyntaxToken token) =>
        string.Concat(token.LeadingTrivia.Select(t => t.Text)) + token.Text + string.Concat(token.TrailingTrivia.Select(t => t.Text));

    // ------------------------------------------------------------------ attachment (LANG-106)

    [Fact]
    [Trait("Req", "LANG-106")]
    public void Trivia_SameLineTrivia_IsTrailingOfThePreviousToken()
    {
        var tokens = Tokenize("a { c } b");

        Assert.Equal(
            [(SyntaxKind.WhitespaceTrivia, " "), (SyntaxKind.BlockCommentTrivia, "{ c }"), (SyntaxKind.WhitespaceTrivia, " ")],
            Pieces(tokens[0].TrailingTrivia));
        Assert.Empty(tokens[0].LeadingTrivia);
        Assert.Empty(tokens[1].LeadingTrivia);
    }

    [Fact]
    [Trait("Req", "LANG-106")]
    public void Trivia_TrailingTrivia_EndsWithTheFirstLineBreak()
    {
        var tokens = Tokenize("a; // note\r\n\r\n  { doc } b");

        Assert.Empty(tokens[1].LeadingTrivia);
        Assert.Equal(
            [(SyntaxKind.WhitespaceTrivia, " "), (SyntaxKind.LineCommentTrivia, "// note"), (SyntaxKind.EndOfLineTrivia, "\r\n")],
            Pieces(tokens[1].TrailingTrivia));
        Assert.Equal(
            [(SyntaxKind.EndOfLineTrivia, "\r\n"), (SyntaxKind.WhitespaceTrivia, "  "), (SyntaxKind.BlockCommentTrivia, "{ doc }"), (SyntaxKind.WhitespaceTrivia, " ")],
            Pieces(tokens[2].LeadingTrivia));
        Assert.Empty(tokens[2].TrailingTrivia);
    }

    [Theory]
    [Trait("Req", "LANG-102")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void Trivia_EveryLineTerminator_IsOneEndOfLinePiece(string eol)
    {
        var tokens = Tokenize("a" + eol + eol + "b");

        Assert.Equal([(SyntaxKind.EndOfLineTrivia, eol)], Pieces(tokens[0].TrailingTrivia));
        Assert.Equal([(SyntaxKind.EndOfLineTrivia, eol)], Pieces(tokens[1].LeadingTrivia));
    }

    [Fact]
    [Trait("Req", "LANG-106")]
    public void Trivia_BeforeTheFirstToken_IsLeading()
    {
        var tokens = Tokenize("  // header\n\nprogram");

        Assert.Equal(
            [(SyntaxKind.WhitespaceTrivia, "  "), (SyntaxKind.LineCommentTrivia, "// header"), (SyntaxKind.EndOfLineTrivia, "\n"), (SyntaxKind.EndOfLineTrivia, "\n")],
            Pieces(tokens[0].LeadingTrivia));
    }

    [Fact]
    [Trait("Req", "LANG-106")]
    public void Trivia_AfterTheLastLineBreak_IsLeadingOfEndOfFile()
    {
        var tokens = Tokenize("end.\n// trailer\n");

        Assert.Equal([(SyntaxKind.EndOfLineTrivia, "\n")], Pieces(tokens[1].TrailingTrivia));
        var eof = tokens[^1];
        Assert.Equal(SyntaxKind.EndOfFileToken, eof.Kind);
        Assert.Equal([(SyntaxKind.LineCommentTrivia, "// trailer"), (SyntaxKind.EndOfLineTrivia, "\n")], Pieces(eof.LeadingTrivia));
        Assert.Empty(eof.TrailingTrivia);
        Assert.Equal(new TextSpan(5, 11), eof.FullSpan);
    }

    [Fact]
    [Trait("Req", "LANG-106")]
    public void Trivia_MultiLineCommentOnTheSameLine_StaysTrailingUntilTheNextLineBreak()
    {
        var tokens = Tokenize("a { one\ntwo } b");

        Assert.Equal(
            [(SyntaxKind.WhitespaceTrivia, " "), (SyntaxKind.BlockCommentTrivia, "{ one\ntwo }"), (SyntaxKind.WhitespaceTrivia, " ")],
            Pieces(tokens[0].TrailingTrivia));
    }

    [Fact]
    public void Trivia_FullSpan_CoversLeadingAndTrailingTrivia()
    {
        var tokens = Tokenize("  a  \n b");

        Assert.Equal(new TextSpan(2, 1), tokens[0].Span);
        Assert.Equal(new TextSpan(0, 6), tokens[0].FullSpan);
        Assert.Equal(new TextSpan(6, 2), tokens[1].FullSpan);
    }

    // ------------------------------------------------------------------ comments (LANG-140…142)

    [Theory]
    [Trait("Req", "LANG-140")]
    [InlineData("{ x }", SyntaxKind.BlockCommentTrivia)]
    [InlineData("{}", SyntaxKind.BlockCommentTrivia)]
    [InlineData("(* x *)", SyntaxKind.BlockCommentTrivia)]
    [InlineData("(**)", SyntaxKind.BlockCommentTrivia)]
    [InlineData("(*)*)", SyntaxKind.BlockCommentTrivia)]
    [InlineData("{ (* }", SyntaxKind.BlockCommentTrivia)]
    [InlineData("(* { *)", SyntaxKind.BlockCommentTrivia)]
    [InlineData("{ line1\r\nline2 }", SyntaxKind.BlockCommentTrivia)]
    [InlineData("{ 'not a string }", SyntaxKind.BlockCommentTrivia)]
    [InlineData("// x", SyntaxKind.LineCommentTrivia)]
    [InlineData("//", SyntaxKind.LineCommentTrivia)]
    [InlineData("// { not a block", SyntaxKind.LineCommentTrivia)]
    [InlineData("//// banner", SyntaxKind.LineCommentTrivia)]
    [InlineData("{* x }", SyntaxKind.BlockCommentTrivia)]
    [InlineData("(** x *)", SyntaxKind.BlockCommentTrivia)]
    [InlineData("/// summary", SyntaxKind.DocCommentTrivia)]     // LANG-142
    [InlineData("///", SyntaxKind.DocCommentTrivia)]
    [InlineData("{** summary }", SyntaxKind.DocCommentTrivia)]   // LANG-142
    [InlineData("{**}", SyntaxKind.DocCommentTrivia)]
    [InlineData("{$R+}", SyntaxKind.DirectiveTrivia)]           // LANG-141
    [InlineData("(*$R+*)", SyntaxKind.DirectiveTrivia)]
    [InlineData("{$DEFINE DEBUG}", SyntaxKind.DirectiveTrivia)]
    [InlineData("{ $R+}", SyntaxKind.BlockCommentTrivia)]
    [InlineData("(* $R+*)", SyntaxKind.BlockCommentTrivia)]
    public void Trivia_Comment_IsOnePieceOfItsKind(string comment, SyntaxKind kind)
    {
        var (tokens, diagnostics) = LexAll(comment + "\nx");

        Assert.Empty(diagnostics);
        var x = tokens[0];
        Assert.Equal("x", x.Text);
        Assert.Equal([(kind, comment), (SyntaxKind.EndOfLineTrivia, "\n")], Pieces(x.LeadingTrivia));
        Assert.True(SyntaxFacts.IsTrivia(kind));
    }

    [Theory]
    [Trait("Req", "LANG-140")]
    [InlineData("{ a { b } c }", new[] { "c" })]
    [InlineData("(* a (* b *) c *)", new[] { "c", "*", ")" })]
    public void Trivia_BlockComments_DoNotNest(string text, string[] tokensAfter)
    {
        var (tokens, diagnostics) = LexAll(text);

        Assert.Equal(tokensAfter, tokens.SkipLast(1).Select(t => t.Text));
        if (text[0] == '{')
        {
            Assert.Equal("E1002", Assert.Single(diagnostics).Code); // the stray '}'
        }
        else
        {
            Assert.Empty(diagnostics);
        }
    }

    [Theory]
    [Trait("Req", "LANG-140")]
    [InlineData("x { abc", 2, 1)]
    [InlineData("x (* abc", 2, 2)]
    [InlineData("x (*)", 2, 2)]
    [InlineData("x {$R+", 2, 1)]
    [InlineData("x (*$R+", 2, 2)]
    [InlineData("x {** doc", 2, 1)]
    public void Trivia_UnterminatedComment_IsE1030AtTheOpenerAndRunsToEndOfFile(string text, int start, int length)
    {
        var (tokens, diagnostics) = LexAll(text);

        var d = Assert.Single(diagnostics);
        Assert.Equal("E1030", d.Code);
        Assert.Equal("Unterminated comment", d.Message);
        Assert.Equal(new TextSpan(start, length), d.Location.Span);
        Assert.Equal(2, tokens.Length);
        Assert.Equal(text[start..], tokens[0].TrailingTrivia[^1].Text);
    }

    [Fact]
    [Trait("Req", "LANG-140")]
    public void Trivia_LineComment_ExcludesTheLineBreak()
    {
        var tokens = Tokenize("a // x\r\nb");

        Assert.Equal("// x", tokens[0].TrailingTrivia[1].Text);
        Assert.Equal("\r\n", tokens[0].TrailingTrivia[2].Text);
    }

    [Theory]
    [Trait("Req", "LANG-141")]
    [InlineData("{$R+}", "R", "+")]
    [InlineData("{$r-}", "R", "-")]
    [InlineData("(*$IFDEF  DEBUG *)", "IFDEF", "DEBUG")]
    [InlineData("{$ifdef Debug}", "IFDEF", "Debug")]
    [InlineData("{$I file.inc}", "I", "file.inc")]
    [InlineData("{$MESSAGE HINT 'x y'}", "MESSAGE", "HINT 'x y'")]
    [InlineData("{$}", "", "")]
    [InlineData("{$ R+}", "", "R+")]
    [InlineData("{$WARN_1 ON}", "WARN_1", "ON")]
    public void Trivia_Directive_CarriesNameAndArgument(string text, string name, string argument)
    {
        var trivia = Assert.Single(Tokenize(text)[0].LeadingTrivia);

        Assert.Equal(SyntaxKind.DirectiveTrivia, trivia.Kind);
        Assert.Equal(new DirectiveTriviaInfo(name, argument, new TextSpan(0, text.Length), IsActive: true), trivia.Directive);
    }

    [Fact]
    public void Trivia_OnlyDirectivesCarryDirectiveInfo()
    {
        Assert.All(Tokenize("{ x } // y\n(* z *)")[0].LeadingTrivia, t => Assert.Null(t.Directive));
    }

    [Fact]
    [Trait("Req", "LANG-103")]
    public void Trivia_Whitespace_IsOnePiecePerRun()
    {
        var tokens = Tokenize("a \t\f ﻿ b");

        Assert.Equal([(SyntaxKind.WhitespaceTrivia, " \t\f ﻿ ")], Pieces(tokens[0].TrailingTrivia));
    }

    [Fact]
    [Trait("Req", "LANG-104")]
    public void Trivia_IllegalCharacter_IsSkippedTokensTrivia()
    {
        var tokens = Tokenize("a ! b");

        Assert.Equal(
            [(SyntaxKind.WhitespaceTrivia, " "), (SyntaxKind.SkippedTokensTrivia, "!"), (SyntaxKind.WhitespaceTrivia, " ")],
            Pieces(tokens[0].TrailingTrivia));
    }

    [Fact]
    [Trait("Req", "LANG-140")]
    public void Trivia_SlashNotFollowedBySlash_IsAToken()
    {
        Assert.Equal(["a", "/", "b"], Tokenize("a/b").SkipLast(1).Select(t => t.Text));
        Assert.Equal(["(", "x", ")"], Tokenize("( x )").SkipLast(1).Select(t => t.Text));
    }

    // ------------------------------------------------------------------ public API (CMP-222)

    [Fact]
    [Trait("Req", "CMP-222")]
    public void Trivia_TokenizeAndGetDiagnostics_AgreeWithThePullLexer()
    {
        const string text = "program P; { c }\r\nbegin x := 12abc + 'oops\r\nend.";
        var source = SourceText.From(Path, text);
        var (tokens, diagnostics) = LexAll(text);

        Assert.Equal(tokens.Select(t => (t.Kind, t.FullSpan)), Lexer.Tokenize(source).Select(t => (t.Kind, t.FullSpan)));
        Assert.Equal(diagnostics.Select(d => d.ToText()), Lexer.GetDiagnostics(source).Select(d => d.ToText()));
        Assert.Equal(["E1013", "E1020"], Lexer.GetDiagnostics(source).Select(d => d.Code));
        Assert.All(Lexer.Tokenize(source), t => Assert.Equal(t.Kind, t.ContextualKind));
    }

    [Fact]
    [Trait("Req", "CMP-222")]
    public void Trivia_TokenizeAcceptsParseOptions()
    {
        var source = SourceText.From(Path, "a");

        Assert.Equal(2, Lexer.Tokenize(source, new ParseOptions(["DEBUG"])).Length);
        Assert.Empty(Lexer.GetDiagnostics(source, new ParseOptions([])));
    }

    // ------------------------------------------------------------------ full fidelity (CMP-120, CMP-222)

    private static readonly string[] Fragments =
    [
        "program", "Unit1", "begin", "end", "end.", "x", "&type", "日本", "_a1",
        "0", "12", "$FF", "%101", "&17", "1.5e3", "1..5", "1__", "12abc", "1e+", "99999999999999999999999",
        "'a''b'", "'unterminated", "#13", "#$41", "#$FFFFF", "''", "#",
        "{ c }", "{ open", "(* c *)", "(*", "// line", "/// doc", "{** doc }", "{$R+}", "(*$I-*)", "{$IFDEF X}",
        " ", "  ", "\t", "\r\n", "\r", "\n", " ", "\f",
        "+", "-", "*", "/", "=", "<>", "<", ">", "<=", ">=", ":=", ".", "..", ",", ";", ":", "(", ")", "[", "]",
        "^", "@", "$", "&", "(.", ".)", "!", "}", "\0", "\u001A", "%", "\"", "€",
    ];

    internal static string RandomSource(Random random)
    {
        var sb = new StringBuilder();
        var count = random.Next(0, 40);
        for (var i = 0; i < count; i++)
        {
            if (random.Next(8) == 0)
            {
                sb.Append((char)random.Next(0, 0x10000));   // any UTF-16 code unit, including lone surrogates
            }
            else
            {
                sb.Append(Fragments[random.Next(Fragments.Length)]);
            }
        }

        return sb.ToString();
    }

    private static void AssertFullFidelity(string text)
    {
        var source = SourceText.From(Path, text);
        var tokens = Lexer.Tokenize(source);

        Assert.Equal(text, string.Concat(tokens.Select(FullText)));
        Assert.Equal(SyntaxKind.EndOfFileToken, tokens[^1].Kind);
        Assert.Single(tokens, t => t.Kind == SyntaxKind.EndOfFileToken);
        var expectedStart = 0;
        foreach (var token in tokens)
        {
            Assert.Equal(expectedStart, token.FullSpan.Start);
            Assert.True(token.FullSpan.Start <= token.Span.Start && token.Span.End <= token.FullSpan.End);
            Assert.Equal(token.Text, text.Substring(token.Span.Start, token.Span.Length));
            foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
            {
                Assert.True(trivia.Span.Length > 0);
                Assert.Equal(trivia.Text, text.Substring(trivia.Span.Start, trivia.Span.Length));
            }

            expectedStart = token.FullSpan.End;
        }

        Assert.Equal(text.Length, expectedStart);
        Assert.All(Lexer.GetDiagnostics(source), d => Assert.True(d.Location.Span.End <= text.Length));
    }

    [Fact]
    [Trait("Req", "CMP-222")]
    [Trait("Req", "LANG-106")]
    public void RoundTripProperty()
    {
        var random = new Random(20261113);
        for (var i = 0; i < 500; i++)
        {
            AssertFullFidelity(RandomSource(random));
        }
    }

    [Fact]
    [Trait("Req", "CMP-222")]
    public void TokenizeNeverThrows()
    {
        var random = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var chars = new char[random.Next(0, 200)];
            for (var j = 0; j < chars.Length; j++)
            {
                // Bias towards characters that start or end constructs.
                chars[j] = random.Next(3) == 0 ? "{}()*/'#$%&._1eE\r\n "[random.Next(19)] : (char)random.Next(0, 0x10000);
            }

            AssertFullFidelity(new string(chars));
        }

        foreach (var edge in new[] { "", "{", "(*", "'", "#", "#$", "$", "%", "&", "/", "//", "(", "1e", "1.", "\uD800", "\uDFFF", "\r", "{$" })
        {
            AssertFullFidelity(edge);
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Trivia_TenThousandLineFile_TokenizesQuickly()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 10_000; i++)
        {
            sb.Append("  if Value").Append(i).Append(" > $FF then Result := 'line'#13#10 + Name; // comment ").Append(i).Append("\r\n");
        }

        var source = SourceText.From(Path, sb.ToString());
        var stopwatch = Stopwatch.StartNew();
        var tokens = Lexer.Tokenize(source);
        stopwatch.Stop();

        Assert.Equal((10_000 * 11) + 1, tokens.Length); // if Value > $FF then Result := '…'#13#10 + Name ; per line
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"took {stopwatch.Elapsed}");
    }
}
