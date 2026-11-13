using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>docs/spec/language/lexical.md. Test groups: Identifiers*, Symbols*, Numbers*, Strings*, Trivia*.</summary>
[Trait("Phase", "1")]
public sealed partial class LexerTests
{
    internal const string Path = @"C:\p\test.pas";

    /// <summary>The 62 reserved words of LANG-115, verbatim.</summary>
    internal const string ReservedWordsText = """
        and array as begin case class const constructor destructor div do downto else end except
        exports file finalization finally for function goto if implementation in inherited
        initialization inline interface is label library mod nil not object of or packed procedure
        program property raise record repeat resourcestring set shl shr string then threadvar to try
        type unit until uses var while with xor
        """;

    /// <summary>The contextual keywords of LANG-116, verbatim.</summary>
    internal const string ContextualKeywordsText = """
        absolute abstract assembler at automated cdecl contains default delayed deprecated dispid dynamic
        experimental export external far final forward helper implements index local message name near
        nodefault noreturn on operator out overload override package pascal platform private protected
        public published read readonly reference register reintroduce requires resident safecall sealed
        static stdcall stored strict unsafe varargs virtual winapi write writeonly
        """;

    internal static string[] Words(string text) =>
        text.Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    public static TheoryData<string> ReservedWords => [.. Words(ReservedWordsText)];

    public static TheoryData<string> ContextualKeywords => [.. Words(ContextualKeywordsText)];

    /// <summary>All tokens including the final EndOfFileToken, plus the lexical diagnostics.</summary>
    internal static (ImmutableArray<SyntaxToken> Tokens, ImmutableArray<Diagnostic> Diagnostics) LexAll(string text)
    {
        var lexer = new PascalLexer(SourceText.From(Path, text));
        var tokens = ImmutableArray.CreateBuilder<SyntaxToken>();
        SyntaxToken token;
        do
        {
            token = lexer.Lex();
            tokens.Add(token);
        }
        while (token.Kind != SyntaxKind.EndOfFileToken && tokens.Count <= text.Length + 1);

        return (tokens.ToImmutable(), lexer.Diagnostics);
    }

    /// <summary>Tokens without the EndOfFileToken; asserts no diagnostics.</summary>
    internal static ImmutableArray<SyntaxToken> Lex(string text)
    {
        var (tokens, diagnostics) = LexAll(text);
        Assert.Empty(diagnostics);
        Assert.Equal(SyntaxKind.EndOfFileToken, tokens[^1].Kind);
        return tokens.RemoveAt(tokens.Length - 1);
    }

    internal static SyntaxToken LexSingle(string text) => Assert.Single(Lex(text));

    internal static SyntaxKind[] Kinds(string text) => [.. Lex(text).Select(t => t.Kind)];

    // ------------------------------------------------------------------ identifiers (LANG-110…112)

    [Theory]
    [Trait("Req", "LANG-110")]
    [InlineData("abc")]
    [InlineData("_")]
    [InlineData("_x1")]
    [InlineData("A_B_9")]
    [InlineData("ÄÖüß")]
    [InlineData("日本語")]
    [InlineData("Ⅻ")]          // U+216B, category Nl
    [InlineData("x𝒜y")]        // U+1D49C (Lu) as a surrogate pair
    [InlineData("averyveryveryveryveryveryveryveryveryveryveryveryveryveryveryveryverylongidentifier")]
    public void Identifiers_AreOneTokenWithTheNameAsValue(string text)
    {
        var token = LexSingle(text);

        Assert.Equal(SyntaxKind.IdentifierToken, token.Kind);
        Assert.Equal(text, token.Text);
        Assert.Equal(text, token.Value);
        Assert.Equal(new TextSpan(0, text.Length), token.Span);
    }

    [Fact]
    [Trait("Req", "LANG-110")]
    public void Identifiers_StopAtNonIdentifierCharacters()
    {
        var tokens = Lex("abc+d1.e");

        Assert.Equal(
            [SyntaxKind.IdentifierToken, SyntaxKind.PlusToken, SyntaxKind.IdentifierToken, SyntaxKind.DotToken, SyntaxKind.IdentifierToken],
            tokens.Select(t => t.Kind));
        Assert.Equal(["abc", "+", "d1", ".", "e"], tokens.Select(t => t.Text));
        Assert.Equal([new TextSpan(0, 3), new TextSpan(3, 1), new TextSpan(4, 2), new TextSpan(6, 1), new TextSpan(7, 1)], tokens.Select(t => t.Span));
    }

    [Fact]
    [Trait("Req", "LANG-112")]
    public void Identifiers_QualifiedNameIsSeveralTokens()
    {
        Assert.Equal([SyntaxKind.IdentifierToken, SyntaxKind.DotToken, SyntaxKind.IdentifierToken], Kinds("Unit1.Foo"));
    }

    [Theory]
    [Trait("Req", "LANG-110")]
    [InlineData("€", "€", "20AC")]     // currency symbol, category Sc
    [InlineData("·", "·", "00B7")]     // middle dot, category Po
    public void Identifiers_NonLetterAboveAscii_IsIllegalCharacter(string text, string shown, string hex)
    {
        var (tokens, diagnostics) = LexAll("a" + text + "b");

        Assert.Equal([SyntaxKind.IdentifierToken, SyntaxKind.IdentifierToken, SyntaxKind.EndOfFileToken], tokens.Select(t => t.Kind));
        var d = Assert.Single(diagnostics);
        Assert.Equal($"Illegal character '{shown}' (U+{hex})", d.Message);
        Assert.Equal(new TextSpan(1, text.Length), d.Location.Span);
    }

    [Theory]
    [Trait("Req", "LANG-111")]
    [InlineData("&type", "type")]
    [InlineData("&begin", "begin")]
    [InlineData("&Foo", "Foo")]
    [InlineData("&virtual", "virtual")]
    [InlineData("&日本", "日本")]
    public void Identifiers_AmpersandEscape_MakesAnIdentifier(string text, string name)
    {
        var token = LexSingle(text);

        Assert.Equal(SyntaxKind.IdentifierToken, token.Kind);
        Assert.Equal(text, token.Text);
        Assert.Equal(name, token.Value);
    }

    [Fact]
    [Trait("Req", "LANG-111")]
    public void Identifiers_AmpersandNotFollowedByIdentifier_IsSymbol()
    {
        Assert.Equal([SyntaxKind.AmpersandToken, SyntaxKind.IdentifierToken], Kinds("& x"));
        Assert.Equal([SyntaxKind.AmpersandToken], Kinds("&"));
    }

    [Theory]
    [Trait("Req", "LANG-115")]
    [MemberData(nameof(ReservedWords))]
    public void Identifiers_ReservedWord_IsItsKeywordKind(string word)
    {
        foreach (var text in new[] { word, word.ToUpperInvariant(), char.ToUpperInvariant(word[0]) + word[1..] })
        {
            var token = LexSingle(text);

            Assert.Equal(word + "keyword", token.Kind.ToString(), ignoreCase: true);
            Assert.Equal(token.Kind, token.ContextualKind);
            Assert.Equal(text, token.Text);
            Assert.Null(token.Value);
        }
    }

    [Theory]
    [Trait("Req", "LANG-116")]
    [MemberData(nameof(ContextualKeywords))]
    public void Identifiers_ContextualKeyword_IsAnIdentifierToken(string word)
    {
        var token = LexSingle(word);

        Assert.Equal(SyntaxKind.IdentifierToken, token.Kind);
        Assert.Equal(SyntaxKind.IdentifierToken, token.ContextualKind);
        Assert.Equal(word, token.Value);
    }

    [Theory]
    [Trait("Req", "LANG-110")]
    [InlineData("BeGiN", SyntaxKind.BeginKeyword)]
    [InlineData("END", SyntaxKind.EndKeyword)]
    [InlineData("begın", SyntaxKind.IdentifierToken)]    // dotless i: only ASCII letters fold
    [InlineData("ſhl", SyntaxKind.IdentifierToken)]       // long s folds to 'S' under Unicode rules, not here
    [InlineData("String", SyntaxKind.StringKeyword)]
    [InlineData("nil", SyntaxKind.NilKeyword)]
    [InlineData("Self", SyntaxKind.IdentifierToken)]
    [InlineData("Result", SyntaxKind.IdentifierToken)]
    [InlineData("True", SyntaxKind.IdentifierToken)]
    [InlineData("MaxInt", SyntaxKind.IdentifierToken)]
    [InlineData("beginx", SyntaxKind.IdentifierToken)]
    [InlineData("asm", SyntaxKind.IdentifierToken)]
    public void Identifiers_KeywordRecognition_IsAsciiCaseInsensitive(string text, SyntaxKind kind)
    {
        Assert.Equal(kind, LexSingle(text).Kind);
    }

    [Fact]
    public void Identifiers_EndOfFile_IsProducedExactlyOnceAtTheEnd()
    {
        var (tokens, _) = LexAll("x");

        Assert.Equal(2, tokens.Length);
        Assert.Equal(SyntaxKind.EndOfFileToken, tokens[1].Kind);
        Assert.Equal("", tokens[1].Text);
        Assert.Equal(new TextSpan(1, 0), tokens[1].Span);

        var empty = Assert.Single(LexAll("").Tokens);
        Assert.Equal(SyntaxKind.EndOfFileToken, empty.Kind);
        Assert.Equal(new TextSpan(0, 0), empty.Span);
    }

    [Fact]
    public void Identifiers_LexAfterEndOfFile_KeepsReturningEndOfFile()
    {
        var lexer = new PascalLexer(SourceText.From(Path, "a"));
        lexer.Lex();

        Assert.Equal(SyntaxKind.EndOfFileToken, lexer.Lex().Kind);
        Assert.Equal(SyntaxKind.EndOfFileToken, lexer.Lex().Kind);
    }

    // ------------------------------------------------------------------ symbols (LANG-120, 121)

    [Theory]
    [Trait("Req", "LANG-120")]
    [InlineData("+", SyntaxKind.PlusToken)]
    [InlineData("-", SyntaxKind.MinusToken)]
    [InlineData("*", SyntaxKind.AsteriskToken)]
    [InlineData("/", SyntaxKind.SlashToken)]
    [InlineData("=", SyntaxKind.EqualsToken)]
    [InlineData("<>", SyntaxKind.LessThanGreaterThanToken)]
    [InlineData("<", SyntaxKind.LessThanToken)]
    [InlineData(">", SyntaxKind.GreaterThanToken)]
    [InlineData("<=", SyntaxKind.LessThanEqualsToken)]
    [InlineData(">=", SyntaxKind.GreaterThanEqualsToken)]
    [InlineData(":=", SyntaxKind.ColonEqualsToken)]
    [InlineData(".", SyntaxKind.DotToken)]
    [InlineData("..", SyntaxKind.DotDotToken)]
    [InlineData(",", SyntaxKind.CommaToken)]
    [InlineData(";", SyntaxKind.SemicolonToken)]
    [InlineData(":", SyntaxKind.ColonToken)]
    [InlineData("(", SyntaxKind.OpenParenToken)]
    [InlineData(")", SyntaxKind.CloseParenToken)]
    [InlineData("[", SyntaxKind.OpenBracketToken)]
    [InlineData("]", SyntaxKind.CloseBracketToken)]
    [InlineData("^", SyntaxKind.CaretToken)]
    [InlineData("@", SyntaxKind.AtToken)]
    [InlineData("#", SyntaxKind.HashToken)]
    [InlineData("$", SyntaxKind.DollarToken)]
    [InlineData("&", SyntaxKind.AmpersandToken)]
    [InlineData("(.", SyntaxKind.OpenBracketToken)]
    [InlineData(".)", SyntaxKind.CloseBracketToken)]
    public void Symbols_EachSymbol_IsOneToken(string text, SyntaxKind kind)
    {
        var token = LexSingle(text);

        Assert.Equal(kind, token.Kind);
        Assert.Equal(text, token.Text);
        Assert.Equal(new TextSpan(0, text.Length), token.Span);
        Assert.Null(token.Value);
    }

    [Theory]
    [Trait("Req", "LANG-105")]
    [InlineData("a:=b<>c;", new[] { "a", ":=", "b", "<>", "c", ";" })]
    [InlineData("<=>=", new[] { "<=", ">=" })]
    [InlineData("<>>", new[] { "<>", ">" })]
    [InlineData(":=:", new[] { ":=", ":" })]
    [InlineData("...", new[] { "..", "." })]
    [InlineData("..)", new[] { "..", ")" })]
    [InlineData("x.)", new[] { "x", ".)" })]
    [InlineData("(..)", new[] { "(.", ".)" })]
    [InlineData("+=", new[] { "+", "=" })]          // LANG-120: not supported, the parser reports E1100
    [InlineData("-=*=/=", new[] { "-", "=", "*", "=", "/", "=" })]
    [InlineData(">>", new[] { ">", ">" })]          // LANG-121
    [InlineData("^@", new[] { "^", "@" })]
    public void Symbols_MaximalMunch(string text, string[] expected)
    {
        Assert.Equal(expected, Lex(text).Select(t => t.Text));
    }

    [Theory]
    [Trait("Req", "LANG-104")]
    [InlineData("!", "!", "0021")]
    [InlineData("?", "?", "003F")]
    [InlineData("~", "~", "007E")]
    [InlineData("\"", "\"", "0022")]
    [InlineData("\\", "\\", "005C")]
    [InlineData("`", "`", "0060")]
    [InlineData("}", "}", "007D")]
    [InlineData("\0", "", "0000")]
    [InlineData("\u001A", "", "001A")]
    [InlineData("\u0007", "", "0007")]
    public void Symbols_IllegalCharacter_IsE1002AndSkipped(string bad, string shown, string hex)
    {
        var (tokens, diagnostics) = LexAll("a" + bad + ";");

        Assert.Equal([SyntaxKind.IdentifierToken, SyntaxKind.SemicolonToken, SyntaxKind.EndOfFileToken], tokens.Select(t => t.Kind));
        var d = Assert.Single(diagnostics);
        Assert.Equal("E1002", d.Code);
        Assert.Equal($"Illegal character '{shown}' (U+{hex})", d.Message);
        Assert.Equal(new TextSpan(1, 1), d.Location.Span);
        Assert.Equal(new LinePosition(0, 1), d.Location.LineSpan.Start);
        Assert.Equal(new TextSpan(2, 1), tokens[1].Span);
    }

    [Fact]
    [Trait("Req", "LANG-104")]
    public void Symbols_LoneSurrogates_AreIllegalCharacters()
    {
        // Built at run time: attribute arguments are stored as UTF-8 and cannot hold lone surrogates.
        foreach (var (bad, hex) in new[] { ("\uD800", "D800"), ("\uDC00", "DC00") })
        {
            Symbols_IllegalCharacter_IsE1002AndSkipped(bad, "", hex);
        }

        var (tokens, diagnostics) = LexAll("x\uD800");
        Assert.Equal([SyntaxKind.IdentifierToken, SyntaxKind.EndOfFileToken], tokens.Select(t => t.Kind));
        Assert.Equal(new TextSpan(1, 1), Assert.Single(diagnostics).Location.Span);
    }

    [Theory]
    [Trait("Req", "LANG-104")]
    [InlineData("𝄞", "1D11E")]      // U+1D11E, category So: one diagnostic for the surrogate pair
    public void Symbols_IllegalSupplementaryCharacter_IsOneDiagnostic(string bad, string hex)
    {
        var (_, diagnostics) = LexAll(bad);

        var d = Assert.Single(diagnostics);
        Assert.Equal($"Illegal character '{bad}' (U+{hex})", d.Message);
        Assert.Equal(new TextSpan(0, 2), d.Location.Span);
    }

    [Fact]
    [Trait("Req", "LANG-104")]
    public void Symbols_ConsecutiveIllegalCharacters_EachReported()
    {
        var (tokens, diagnostics) = LexAll("!!x");

        Assert.Equal([SyntaxKind.IdentifierToken, SyntaxKind.EndOfFileToken], tokens.Select(t => t.Kind));
        Assert.Equal([new TextSpan(0, 1), new TextSpan(1, 1)], diagnostics.Select(d => d.Location.Span));
    }

    [Fact]
    [Trait("Req", "LANG-103")]
    public void Symbols_WhitespaceSeparatesTokens()
    {
        var tokens = Lex("a \t\f\u00A0\uFEFF:=\r\n\rb\n");

        Assert.Equal(["a", ":=", "b"], tokens.Select(t => t.Text));
        Assert.Equal([new TextSpan(0, 1), new TextSpan(6, 2), new TextSpan(11, 1)], tokens.Select(t => t.Span));
    }
}
