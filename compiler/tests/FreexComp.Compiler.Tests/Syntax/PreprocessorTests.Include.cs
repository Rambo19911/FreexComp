using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>{$I file} / {$INCLUDE file} expansion (LANG-875, 876).</summary>
public sealed partial class PreprocessorTests
{
    private const string MainPath = @"C:\p\main.pas";

    /// <summary>All tokens of the expanded stream (up to and including the main file's EndOfFile) and its diagnostics.</summary>
    private static (ImmutableArray<SyntaxToken> Tokens, ImmutableArray<Diagnostic> Diagnostics) Expand(
        string main, InMemoryFileSystem fs, ParseOptions? options = null)
    {
        var stream = new Preprocessor(SourceText.From(MainPath, main), options, fs);
        var tokens = ImmutableArray.CreateBuilder<SyntaxToken>();
        SyntaxToken token;
        do
        {
            token = stream.Lex();
            tokens.Add(token);
        }
        while (token.Kind != SyntaxKind.EndOfFileToken && tokens.Count < 10_000);

        return (tokens.ToImmutable(), stream.Diagnostics);
    }

    private static string[] Texts(ImmutableArray<SyntaxToken> tokens) => [.. tokens.SkipLast(1).Select(t => t.Text)];

    [Fact]
    [Trait("Req", "LANG-875")]
    public void Include_TokensAreInsertedAtTheDirective()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\inc.inc", "x y");

        var (tokens, diagnostics) = Expand("a {$I inc.inc} b", fs);

        Assert.Empty(diagnostics);
        Assert.Equal(["a", "x", "y", "b"], Texts(tokens));
        Assert.Equal([MainPath, @"C:\p\inc.inc", @"C:\p\inc.inc", MainPath, MainPath], tokens.Select(t => t.File!.Path));
    }

    [Theory]
    [Trait("Req", "LANG-875")]
    [InlineData("{$I inc}")]                   // default extension .inc
    [InlineData("{$I inc.inc}")]
    [InlineData("{$i INC.INC}")]
    [InlineData("{$INCLUDE inc.inc}")]
    [InlineData("{$include inc}")]
    [InlineData("(*$I inc.inc*)")]
    [InlineData("{$I 'inc.inc'}")]
    [InlineData(@"{$I C:\p\inc.inc}")]
    [InlineData("{$I sub/../inc.inc}")]
    public void Include_DirectiveForms(string directive)
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\inc.inc", "x");

        var (tokens, diagnostics) = Expand("a " + directive + " b", fs);

        Assert.Empty(diagnostics);
        Assert.Equal(["a", "x", "b"], Texts(tokens));
    }

    [Fact]
    [Trait("Req", "LANG-875")]
    public void Include_QuotedNameMayContainSpaces()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\my file.inc", "x");

        Assert.Equal(["x"], Texts(Expand("{$I 'my file.inc'}", fs).Tokens));
    }

    [Fact]
    [Trait("Req", "LANG-875")]
    public void Include_LookupIsIncludingFileDirectoryThenSearchPath()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\lib\a.inc", "fromLib {$I b.inc}")
            .Add(@"C:\lib\b.inc", "libB")
            .Add(@"C:\p\b.inc", "mainB")
            .Add(@"C:\other\c.inc", "fromOther");
        var options = new ParseOptions([]) { SearchPaths = [@"C:\lib", @"C:\other"] };

        Assert.Equal(["fromLib", "libB"], Texts(Expand("{$I a.inc}", fs, options).Tokens));     // b.inc next to a.inc
        Assert.Equal(["mainB"], Texts(Expand("{$I b.inc}", fs, options).Tokens));                // main file's directory first
        Assert.Equal(["fromOther"], Texts(Expand("{$I c}", fs, options).Tokens));
    }

    [Fact]
    [Trait("Req", "LANG-875")]
    public void Include_NotFound_IsE1202AtTheDirective()
    {
        var (tokens, diagnostics) = Expand("a {$I missing.inc} b", new InMemoryFileSystem());

        Assert.Equal(["a", "b"], Texts(tokens));
        var d = Assert.Single(diagnostics);
        Assert.Equal(@"C:\p\main.pas(1,3): error E1202: Include file 'missing.inc' not found", d.ToText());
        Assert.Equal(new TextSpan(2, 16), d.Location.Span);
    }

    [Fact]
    [Trait("Req", "LANG-875")]
    public void Include_EmptyName_IsE1206()
    {
        Assert.Equal(["E1206"], Expand("{$INCLUDE}", new InMemoryFileSystem()).Diagnostics.Select(d => d.Code));
        Assert.Equal(["E1206"], Expand("{$I ''}", new InMemoryFileSystem()).Diagnostics.Select(d => d.Code));
    }

    [Fact]
    [Trait("Req", "LANG-875")]
    public void Include_NestingDeeperThanSixteen_IsE1204()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\self.inc", "s {$I self.inc}");

        var (tokens, diagnostics) = Expand("{$I self.inc} end", fs);

        Assert.Equal(16, tokens.Count(t => t.Text == "s"));
        Assert.Equal("end", Texts(tokens)[^1]);
        var d = Assert.Single(diagnostics);
        Assert.Equal("E1204", d.Code);
        Assert.Equal(@"C:\p\self.inc", d.Location.Path);
    }

    [Fact]
    [Trait("Req", "LANG-875")]
    public void Include_PositionsAndDiagnosticsAreThoseOfTheIncludeFile()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\inc.inc", "\r\n  x := 12abc;");

        var (tokens, diagnostics) = Expand("program P;\r\n{$I inc.inc}\r\nend.", fs);

        var x = tokens.Single(t => t.Text == "x");
        Assert.Equal(new TextSpan(4, 1), x.Span);
        Assert.Equal(new LinePosition(1, 2), x.File!.Lines.GetLinePosition(x.Span.Start));
        var d = Assert.Single(diagnostics);
        Assert.Equal(@"C:\p\inc.inc(2,8): error E1013: Invalid numeric literal '12abc'", d.ToText());
    }

    [Fact]
    [Trait("Req", "LANG-860")]
    [Trait("Req", "LANG-852")]
    public void Include_DefinesAndSwitchesCrossTheBoundaryBothWays()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\defs.inc", "{$IFOPT R+} rOn {$ENDIF} {$DEFINE FROMINC} {$Q+}");

        var (tokens, diagnostics) = Expand("{$R+}{$I defs.inc}{$IFDEF FROMINC} seen {$ENDIF} q", fs);

        Assert.Empty(diagnostics);
        Assert.Equal(["rOn", "seen", "q"], Texts(tokens));
        Assert.True(tokens.Single(t => t.Text == "q").DirectiveState.OverflowChecks);
    }

    [Fact]
    [Trait("Req", "LANG-863")]
    public void Include_ConditionalsMustBalanceWithinEachFile()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\open.inc", "{$IFDEF NOPE} hidden")
            .Add(@"C:\p\close.inc", "{$ENDIF}");

        var open = Expand("{$I open.inc} visible", fs);
        Assert.Equal(["visible"], Texts(open.Tokens));
        var e1200 = Assert.Single(open.Diagnostics);
        Assert.Equal(("E1200", @"C:\p\open.inc"), (e1200.Code, e1200.Location.Path));

        var close = Expand("{$IFDEF WIN64} a {$I close.inc} b {$ENDIF}", fs);
        Assert.Equal(["a", "b"], Texts(close.Tokens));
        var e1201 = Assert.Single(close.Diagnostics);
        Assert.Equal(("E1201", @"C:\p\close.inc"), (e1201.Code, e1201.Location.Path));
    }

    [Fact]
    [Trait("Req", "LANG-864")]
    public void Include_InInactiveBranch_IsNotExpanded()
    {
        var (tokens, diagnostics) = Expand("{$IFDEF NOPE}{$I missing.inc}{$ENDIF} a", new InMemoryFileSystem());

        Assert.Empty(diagnostics);
        Assert.Equal(["a"], Texts(tokens));
    }

    [Theory]
    [Trait("Req", "LANG-875")]
    [InlineData("{$I+} a", true)]
    [InlineData("{$I-} a", false)]
    [InlineData("{$I-,R+} a", false)]
    public void Include_IPlusMinus_IsTheIoCheckSwitch(string text, bool ioChecks)
    {
        var (tokens, diagnostics) = Expand(text, new InMemoryFileSystem());

        Assert.Empty(diagnostics);
        Assert.Equal(ioChecks, tokens[0].DirectiveState.IoChecks);
        Assert.Equal(SyntaxKind.DirectiveTrivia, tokens[0].LeadingTrivia[0].Kind);
    }

    [Theory]
    [InlineData("{$I a.inc}{$I b.inc} z", new[] { "A", "B", "z" })]
    [InlineData("{$I a.inc}", new[] { "A" })]
    [InlineData("z\n{$I a.inc}", new[] { "z", "A" })]
    [InlineData("z {$I a.inc}\nw", new[] { "z", "A", "w" })]
    [InlineData("{$I empty.inc} z", new[] { "z" })]
    [InlineData("{$I nested.inc} z", new[] { "N", "A", "N2", "z" })]
    public void Include_AnywhereInTheTrivia(string main, string[] expected)
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\a.inc", "A")
            .Add(@"C:\p\b.inc", "B")
            .Add(@"C:\p\empty.inc", "{ only a comment }")
            .Add(@"C:\p\nested.inc", "N {$I a.inc} N2");

        var (tokens, diagnostics) = Expand(main, fs);

        Assert.Empty(diagnostics);
        Assert.Equal(expected, Texts(tokens));
    }

    [Theory]
    [Trait("Req", "LANG-876")]
    [InlineData("a {$I a.inc} b")]
    [InlineData("a {$I a.inc}\r\n  {$I b.inc} b // c\r\n")]
    [InlineData("{$I a.inc}")]
    public void Include_MainFileTokensStillReproduceTheMainFile(string main)
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\a.inc", "A").Add(@"C:\p\b.inc", "B");

        var mainTokens = Expand(main, fs).Tokens.Where(t => t.File!.Path == MainPath);

        Assert.Equal(main, string.Concat(mainTokens.Select(FullText)));
    }

    [Fact]
    [Trait("Req", "LANG-876")]
    public void Include_DirectiveIsIncludeDirectiveTrivia_AlsoWhenNotExpanded()
    {
        foreach (var tokens in new[] { Expand("{$I a.inc} b", new InMemoryFileSystem().Add(@"C:\p\a.inc", "A")).Tokens, Tokenize("{$I a.inc} b") })
        {
            var b = tokens.Single(t => t.Text == "b");
            var trivia = b.LeadingTrivia[0];
            Assert.Equal(SyntaxKind.IncludeDirectiveTrivia, trivia.Kind);
            Assert.Equal(new DirectiveTriviaInfo("I", "a.inc", new TextSpan(0, 10), IsActive: true), trivia.Directive);
        }
    }

    [Fact]
    [Trait("Req", "CMP-222")]
    public void Include_TokenizeDoesNotExpandAndHasNoIncludeErrors()
    {
        Assert.Equal(["b"], Tokenize("{$I missing.inc} b").SkipLast(1).Select(t => t.Text));
        Assert.Empty(Diagnose("{$I missing.inc} b"));
    }

    [Fact]
    [Trait("Req", "LANG-106")]
    public void Include_DirectiveAlwaysStartsLeadingTrivia()
    {
        // An include directive is never trailing trivia, so the stream can insert the included tokens before the
        // token that follows it; Tokenize uses the same attachment (CMP-222).
        var tokens = Tokenize("a; {$I x.inc} // c\nb");

        Assert.Equal([SyntaxKind.WhitespaceTrivia], tokens[1].TrailingTrivia.Select(t => t.Kind));
        Assert.Equal(
            [SyntaxKind.IncludeDirectiveTrivia, SyntaxKind.WhitespaceTrivia, SyntaxKind.LineCommentTrivia, SyntaxKind.EndOfLineTrivia],
            tokens[2].LeadingTrivia.Select(t => t.Kind));
    }
}
