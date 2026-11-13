using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>docs/spec/language/directives.md. Groups: Conditionals*, Switches*, Messages*, Include*.</summary>
[Trait("Phase", "1")]
public sealed partial class PreprocessorTests
{
    private const string Path = @"C:\p\test.pas";

    private static ImmutableArray<SyntaxToken> Tokenize(string text, params string[] defines) =>
        Lexer.Tokenize(SourceText.From(Path, text), new ParseOptions([.. defines]));

    private static ImmutableArray<Diagnostic> Diagnose(string text, params string[] defines) =>
        Lexer.GetDiagnostics(SourceText.From(Path, text), new ParseOptions([.. defines]));

    /// <summary>Texts of the active tokens (without EndOfFile); asserts there are no diagnostics.</summary>
    private static string[] Active(string text, params string[] defines)
    {
        Assert.Empty(Diagnose(text, defines));
        return [.. Tokenize(text, defines).SkipLast(1).Select(t => t.Text)];
    }

    private static IEnumerable<SyntaxTrivia> AllTrivia(ImmutableArray<SyntaxToken> tokens) =>
        tokens.SelectMany(t => t.LeadingTrivia.Concat(t.TrailingTrivia));

    private static string[] DisabledTexts(string text, params string[] defines) =>
        [.. AllTrivia(Tokenize(text, defines)).Where(t => t.Kind == SyntaxKind.DisabledTextTrivia).Select(t => t.Text)];

    // ------------------------------------------------------------------ IFDEF / IFNDEF / ELSE / ENDIF (LANG-861)

    [Theory]
    [Trait("Req", "LANG-861")]
    [InlineData("{$IFDEF X} a {$ENDIF} b", new string[0], new[] { "b" })]
    [InlineData("{$IFDEF X} a {$ENDIF} b", new[] { "X" }, new[] { "a", "b" })]
    [InlineData("{$IFNDEF X} a {$ENDIF} b", new string[0], new[] { "a", "b" })]
    [InlineData("{$IFNDEF X} a {$ENDIF} b", new[] { "X" }, new[] { "b" })]
    [InlineData("{$IFDEF X} a {$ELSE} c {$ENDIF}", new string[0], new[] { "c" })]
    [InlineData("{$IFDEF X} a {$ELSE} c {$ENDIF}", new[] { "X" }, new[] { "a" })]
    [InlineData("{$ifdef x} a {$else} c {$endif}", new[] { "X" }, new[] { "a" })]          // names are case-insensitive
    [InlineData("(*$IFDEF X*) a (*$ELSE*) c (*$ENDIF*)", new string[0], new[] { "c" })]
    [InlineData("{$IFDEF A}{$IFDEF B} ab {$ELSE} a {$ENDIF}{$ELSE}{$IFDEF B} b {$ELSE} none {$ENDIF}{$ENDIF}", new[] { "A" }, new[] { "a" })]
    [InlineData("{$IFDEF A}{$IFDEF B} ab {$ELSE} a {$ENDIF}{$ELSE}{$IFDEF B} b {$ELSE} none {$ENDIF}{$ENDIF}", new[] { "B" }, new[] { "b" })]
    [InlineData("{$IFDEF A}{$IFDEF B} ab {$ELSE} a {$ENDIF}{$ELSE}{$IFDEF B} b {$ELSE} none {$ENDIF}{$ENDIF}", new[] { "A", "B" }, new[] { "ab" })]
    [InlineData("{$IFDEF A}{$IFDEF B} ab {$ELSE} a {$ENDIF}{$ELSE}{$IFDEF B} b {$ELSE} none {$ENDIF}{$ENDIF}", new string[0], new[] { "none" })]
    public void Conditionals_SelectTheActiveBranch(string text, string[] defines, string[] active)
    {
        Assert.Equal(active, Active(text, defines));
    }

    [Theory]
    [Trait("Req", "LANG-860")]
    [InlineData("{$DEFINE X}{$IFDEF X} a {$ENDIF}", new[] { "a" })]
    [InlineData("{$DEFINE X}{$UNDEF X}{$IFDEF X} a {$ENDIF} b", new[] { "b" })]
    [InlineData("{$define Fast}{$IFDEF FAST} a {$ENDIF}", new[] { "a" })]
    [InlineData("{$IFDEF Y}{$DEFINE X}{$ENDIF}{$IFDEF X} a {$ENDIF} b", new[] { "b" })]    // DEFINE in an inactive branch
    [InlineData("{$IFNDEF Y}{$DEFINE X}{$ENDIF}{$IFDEF X} a {$ENDIF}", new[] { "a" })]
    [InlineData("a {$DEFINE X} {$IFDEF X} b {$ENDIF}", new[] { "a", "b" })]
    public void Conditionals_DefineAndUndef_TakeEffectFromTheirPosition(string text, string[] active)
    {
        Assert.Equal(active, Active(text));
    }

    [Fact]
    [Trait("Req", "LANG-860")]
    public void Conditionals_UndefRemovesACommandLineDefine()
    {
        Assert.Equal(["b"], Active("{$UNDEF DEBUG}{$IFDEF DEBUG} a {$ENDIF} b", "DEBUG"));
    }

    [Theory]
    [Trait("Req", "LANG-870")]
    [InlineData("OPC")]
    [InlineData("OPC_1")]
    [InlineData("MSWINDOWS")]
    [InlineData("WINDOWS")]
    [InlineData("WIN64")]
    [InlineData("CPUX64")]
    [InlineData("CPU64BITS")]
    [InlineData("UNICODE")]
    [InlineData("ASSERTIONS")]
    [InlineData("win64")]
    public void Conditionals_PredefinedSymbols_AreDefined(string symbol)
    {
        Assert.Equal(["a"], Active($"{{$IFDEF {symbol}}} a {{$ENDIF}}"));
    }

    [Theory]
    [Trait("Req", "LANG-870")]
    [InlineData("CONSOLE")]
    [InlineData("DEBUG")]
    [InlineData("RELEASE")]
    [InlineData("WIN32")]
    public void Conditionals_ContextSymbols_AreNotPredefined(string symbol)
    {
        Assert.Empty(Active($"{{$IFDEF {symbol}}} a {{$ENDIF}}"));
        Assert.Equal(["a"], Active($"{{$IFDEF {symbol}}} a {{$ENDIF}}", symbol));
    }

    [Fact]
    [Trait("Req", "LANG-870")]
    public void Conditionals_Assertions_FollowsTheCSwitch()
    {
        Assert.Equal(["b"], Active("{$C-}{$IFDEF ASSERTIONS} a {$ENDIF} b"));
        Assert.Equal(["a"], Active("{$C-}{$C+}{$IFDEF ASSERTIONS} a {$ENDIF}"));
    }

    // ------------------------------------------------------------------ IFOPT (LANG-861, 881)

    [Theory]
    [Trait("Req", "LANG-861")]
    [Trait("Req", "LANG-881")]
    [InlineData("{$IFOPT R+} a {$ELSE} b {$ENDIF}", new[] { "b" })]          // default off
    [InlineData("{$IFOPT R-} a {$ELSE} b {$ENDIF}", new[] { "a" })]
    [InlineData("{$R+}{$IFOPT R+} a {$ELSE} b {$ENDIF}", new[] { "a" })]
    [InlineData("{$R+,Q-}{$IFOPT Q+} a {$ELSE} b {$ENDIF}", new[] { "b" })]
    [InlineData("{$Q-,R+}{$IFOPT R+} a {$ELSE} b {$ENDIF}", new[] { "a" })]
    [InlineData("{$IFOPT C+} a {$ENDIF}", new[] { "a" })]                    // default on
    [InlineData("{$IFOPT I+} a {$ENDIF}", new[] { "a" })]
    [InlineData("{$IFOPT H+} a {$ENDIF}", new[] { "a" })]
    [InlineData("{$IFOPT X+} a {$ENDIF}", new[] { "a" })]
    [InlineData("{$IFOPT A+} a {$ENDIF}", new[] { "a" })]
    [InlineData("{$IFOPT B+} a {$ENDIF} b", new[] { "b" })]
    [InlineData("{$IFOPT M+} a {$ENDIF} b", new[] { "b" })]
    [InlineData("{$IFOPT J+} a {$ENDIF} b", new[] { "b" })]
    [InlineData("{$IFOPT T+} a {$ENDIF} b", new[] { "b" })]
    [InlineData("{$IFOPT Q+} a {$ENDIF} b", new[] { "b" })]
    [InlineData("{$ifopt r-} a {$ENDIF}", new[] { "a" })]
    [InlineData("{$IFDEF X}{$R+}{$ENDIF}{$IFOPT R+} a {$ELSE} b {$ENDIF}", new[] { "b" })]   // switch in inactive branch
    public void Conditionals_IfOpt_TestsTheSwitchState(string text, string[] active)
    {
        Assert.Equal(active, Active(text));
    }

    // ------------------------------------------------------------------ IF / ELSEIF without the binder (LANG-862)

    [Theory]
    [Trait("Req", "LANG-862")]
    [InlineData("{$IF Defined(X)} a {$ELSE} b {$ENDIF}", new[] { "a" })]
    [InlineData("{$IF False} a {$ELSE} b {$ENDIF}", new[] { "a" })]
    [InlineData("{$IF A} a {$ELSEIF B} b {$ELSE} c {$ENDIF}", new[] { "a" })]
    [InlineData("{$IF A} a {$IFEND} z", new[] { "a", "z" })]
    [InlineData("{$IFDEF X} a {$ELSEIF B} b {$ELSE} c {$ENDIF}", new[] { "b" })]
    [InlineData("{$IFDEF X} a {$ELSEIF B} b {$ELSEIF C} c {$ENDIF}", new[] { "b" })]
    public void Conditionals_UnevaluatedIf_IsTrueWithoutTheBinder(string text, string[] active)
    {
        Assert.Equal(active, Active(text));
    }

    // ------------------------------------------------------------------ disabled text (LANG-864)

    [Fact]
    [Trait("Req", "LANG-864")]
    public void Conditionals_InactiveBranch_IsOneDisabledTextTrivia()
    {
        const string text = "x;\r\n{$IFDEF X}\r\n  y := 1;\r\n{$ENDIF}\r\nz";
        var tokens = Tokenize(text);

        Assert.Equal(["x", ";", "z", ""], tokens.Select(t => t.Text));
        Assert.Equal(["\r\n  y := 1;\r\n"], DisabledTexts(text));
        Assert.Equal(
            [SyntaxKind.DirectiveTrivia, SyntaxKind.DisabledTextTrivia, SyntaxKind.DirectiveTrivia, SyntaxKind.EndOfLineTrivia],
            tokens[2].LeadingTrivia.Select(t => t.Kind));
        Assert.Equal(text, string.Concat(tokens.Select(FullText)));
    }

    [Fact]
    [Trait("Req", "LANG-864")]
    public void Conditionals_NestedConditionalsInsideInactiveText_AreSkippedTogether()
    {
        const string text = "{$IFDEF X} a {$IFDEF Y} b {$ELSE} c {$ENDIF} d {$ELSE} e {$ENDIF}";

        Assert.Equal(["e"], Active(text));
        Assert.Equal([" a {$IFDEF Y} b {$ELSE} c {$ENDIF} d "], DisabledTexts(text));
    }

    [Theory]
    [Trait("Req", "LANG-864")]
    [InlineData("{$IFDEF X} '{$ENDIF}' {$ENDIF} a")]
    [InlineData("{$IFDEF X} { {$ENDIF} } {$ENDIF} a")]
    [InlineData("{$IFDEF X} (* {$ENDIF} *) {$ENDIF} a")]
    [InlineData("{$IFDEF X} // {$ENDIF}\n {$ENDIF} a")]
    [InlineData("{$IFDEF X} 'it''s {$ENDIF}' {$ENDIF} a")]
    public void Conditionals_DirectivesInsideCommentsOrStringsOfInactiveText_AreNotSeen(string text)
    {
        Assert.Equal(["a"], Active(text));
    }

    [Theory]
    [Trait("Req", "LANG-864")]
    [InlineData("{$IFDEF X} 'unterminated\n {$ENDIF} a")]
    [InlineData("{$IFDEF X} 12abc #99999 ! € \0 {$ENDIF} a")]
    [InlineData("{$IFDEF X} (* open\n*) {$ENDIF} a")]
    public void Conditionals_InactiveText_IsNotTokenisedSoItHasNoLexicalErrors(string text)
    {
        Assert.Equal(["a"], Active(text));
    }

    [Fact]
    [Trait("Req", "LANG-864")]
    public void Conditionals_UnterminatedCommentInInactiveText_RunsToTheEnd()
    {
        var diagnostics = Diagnose("{$IFDEF X} { {$ENDIF} a");

        Assert.Equal(["E1200"], diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void Conditionals_DirectivesCarryActiveInfo()
    {
        var tokens = Tokenize("{$IFDEF X} a {$ELSE} b {$ENDIF}");
        var directives = AllTrivia(tokens).Where(t => t.Kind == SyntaxKind.DirectiveTrivia).Select(t => t.Directive!).ToList();

        Assert.Equal(["IFDEF", "ELSE", "ENDIF"], directives.Select(d => d.Name));
        Assert.Equal(["X", "", ""], directives.Select(d => d.Argument));
    }

    // ------------------------------------------------------------------ balance errors (LANG-863)

    [Theory]
    [Trait("Req", "LANG-863")]
    [InlineData("a {$IFDEF X} b", 2, 10)]
    [InlineData("a {$IFNDEF X} b", 2, 11)]
    [InlineData("a {$IF X} b {$ELSE} c", 2, 7)]
    [InlineData("a (*$IFOPT R+*) b", 2, 13)]
    public void Conditionals_Unterminated_IsE1200AtTheOpeningDirective(string text, int start, int length)
    {
        var d = Assert.Single(Diagnose(text));

        Assert.Equal("E1200", d.Code);
        Assert.Equal("Unterminated conditional directive", d.Message);
        Assert.Equal(new TextSpan(start, length), d.Location.Span);
    }

    [Fact]
    [Trait("Req", "LANG-863")]
    public void Conditionals_SeveralUnterminated_EachReported()
    {
        // A is defined, so the inner {$IFDEF B} is processed (in an inactive outer branch it would be skipped text).
        var diagnostics = Diagnose("{$IFDEF A}\n{$IFDEF B}\n", "A");

        Assert.Equal(["E1200", "E1200"], diagnostics.Select(d => d.Code));
        Assert.Equal([0, 11], diagnostics.Select(d => d.Location.Span.Start).Order());
        Assert.Equal(["E1200"], Diagnose("{$IFDEF A}\n{$IFDEF B}\n").Select(d => d.Code));
    }

    [Theory]
    [Trait("Req", "LANG-863")]
    [InlineData("a {$ENDIF} b", "ENDIF", 2)]
    [InlineData("a {$ELSE} b", "ELSE", 2)]
    [InlineData("a {$IFEND} b", "IFEND", 2)]
    [InlineData("a {$ELSEIF X} b", "ELSEIF", 2)]
    [InlineData("{$IFDEF X}{$ELSE} a {$ELSE} b {$ENDIF}", "ELSE", 20)]
    [InlineData("{$IFDEF X}{$ELSE} a {$ELSEIF Y} b {$ENDIF}", "ELSEIF", 20)]
    public void Conditionals_Unexpected_IsE1201(string text, string name, int start)
    {
        var d = Assert.Single(Diagnose(text));

        Assert.Equal("E1201", d.Code);
        Assert.Equal($"Unexpected '{name}' directive without matching {{$IF…}}", d.Message);
        Assert.Equal(start, d.Location.Span.Start);
    }

    [Fact]
    [Trait("Req", "LANG-863")]
    public void Conditionals_UnexpectedDirective_IsIgnored()
    {
        Assert.Equal(["a", "b"], Tokenize("a {$ENDIF} b").SkipLast(1).Select(t => t.Text));
    }

    [Theory]
    [InlineData("{$DEFINE}")]
    [InlineData("{$UNDEF}")]
    [InlineData("{$IFDEF} a {$ENDIF}")]
    [InlineData("{$DEFINE 1X}")]
    public void Conditionals_MissingOrInvalidName_IsE1206(string text)
    {
        Assert.Contains(Diagnose(text), d => d.Code == "E1206");
    }

    // ------------------------------------------------------------------ examples and fidelity

    [Fact]
    public void Conditionals_DirectivesMdExample_ActiveTokens()
    {
        const string text = """
            program D;
            {$DEFINE FAST}
            const Limit = 10;
            begin
              {$IFDEF FAST} Writeln('fast'); {$ELSE} Writeln('slow'); {$ENDIF}
              {$IFDEF WIN64} Writeln(SizeOf(Pointer)); {$ENDIF}
            end.
            """;

        Assert.Equal(
            "program D ; const Limit = 10 ; begin Writeln ( 'fast' ) ; Writeln ( SizeOf ( Pointer ) ) ; end .",
            string.Join(' ', Active(text)));
    }

    [Fact]
    [Trait("Req", "CMP-222")]
    public void Conditionals_RoundTripWithRandomDirectives()
    {
        string[] pieces =
        [
            "{$IFDEF A}", "{$IFNDEF B}", "{$ELSE}", "{$ENDIF}", "{$IF X}", "{$ELSEIF Y}", "{$IFEND}", "{$IFOPT R+}",
            "{$DEFINE A}", "{$UNDEF A}", "{$R+}", "x", " ", "\n", "'s'", "'open", "{ c }", "{ open", "// l", "(*", "*)", "}", "1",
        ];
        var random = new Random(42);
        for (var i = 0; i < 500; i++)
        {
            var text = string.Concat(Enumerable.Range(0, random.Next(0, 30)).Select(_ => pieces[random.Next(pieces.Length)]));
            var defines = random.Next(2) == 0 ? new[] { "A" } : [];
            var tokens = Tokenize(text, defines);

            Assert.Equal(text, string.Concat(tokens.Select(FullText)));
            Assert.Single(tokens, t => t.Kind == SyntaxKind.EndOfFileToken);
            _ = Diagnose(text, defines);
        }
    }

    private static string FullText(SyntaxToken token) =>
        string.Concat(token.LeadingTrivia.Select(t => t.Text)) + token.Text + string.Concat(token.TrailingTrivia.Select(t => t.Text));
}
