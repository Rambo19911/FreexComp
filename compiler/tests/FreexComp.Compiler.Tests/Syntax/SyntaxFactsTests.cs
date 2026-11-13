using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>SyntaxFacts (api.md §4, CMP-223).</summary>
[Trait("Phase", "1")]
[Trait("Req", "CMP-223")]
public sealed class SyntaxFactsTests
{
    private static string[] Sorted(IEnumerable<string> words) => [.. words.Order(StringComparer.Ordinal)];

    /// <summary>The keyword lists equal the code blocks after LANG-115 and LANG-116 of lexical.md.</summary>
    [Fact]
    [Trait("Category", "Spec")]
    [Trait("Req", "LANG-115")]
    [Trait("Req", "LANG-116")]
    public void KeywordSets()
    {
        var lexical = File.ReadAllLines(System.IO.Path.Combine(RepoPaths.RequireDocs(), "spec", "language", "lexical.md"));

        Assert.Equal(Sorted(CodeBlockAfter(lexical, "**LANG-115**")), SyntaxFacts.ReservedWords);
        Assert.Equal(Sorted(CodeBlockAfter(lexical, "**LANG-116**")), SyntaxFacts.ContextualKeywords);
    }

    [Fact]
    [Trait("Req", "LANG-115")]
    [Trait("Req", "LANG-116")]
    public void KeywordSets_MatchTheVerbatimCopies()
    {
        Assert.Equal(Sorted(LexerTests.Words(LexerTests.ReservedWordsText)), SyntaxFacts.ReservedWords);
        Assert.Equal(Sorted(LexerTests.Words(LexerTests.ContextualKeywordsText)), SyntaxFacts.ContextualKeywords);
        Assert.Equal(62, SyntaxFacts.ReservedWords.Length);
        Assert.Empty(SyntaxFacts.ReservedWords.Intersect(SyntaxFacts.ContextualKeywords));
    }

    private static IEnumerable<string> CodeBlockAfter(string[] lines, string marker)
    {
        var i = Array.FindIndex(lines, l => l.Contains(marker, StringComparison.Ordinal));
        Assert.True(i >= 0, marker + " not found");
        i = Array.FindIndex(lines, i, l => l.TrimStart().StartsWith("```", StringComparison.Ordinal)) + 1;
        for (; !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++)
        {
            foreach (var word in lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                yield return word;
            }
        }
    }

    [Theory]
    [InlineData("begin", true)]
    [InlineData("BEGIN", true)]
    [InlineData("String", true)]
    [InlineData("begın", false)]
    [InlineData("virtual", false)]
    [InlineData("Self", false)]
    [InlineData("", false)]
    public void IsReservedWord_IsAsciiCaseInsensitive(string text, bool expected)
    {
        Assert.Equal(expected, SyntaxFacts.IsReservedWord(text));
    }

    [Theory]
    [InlineData("virtual", true)]
    [InlineData("OVERRIDE", true)]
    [InlineData("begin", false)]
    [InlineData("x", false)]
    public void IsContextualKeyword_Text(string text, bool expected)
    {
        Assert.Equal(expected, SyntaxFacts.IsContextualKeyword(text));
    }

    [Theory]
    [Trait("Req", "LANG-110")]
    [InlineData("x", true)]
    [InlineData("_", true)]
    [InlineData("Foo_12", true)]
    [InlineData("日本", true)]
    [InlineData("virtual", true)]
    [InlineData("Self", true)]
    [InlineData("begin", false)]
    [InlineData("1x", false)]
    [InlineData("&x", false)]
    [InlineData("a b", false)]
    [InlineData("a.b", false)]
    [InlineData("€", false)]
    [InlineData("", false)]
    public void IsValidIdentifier(string text, bool expected)
    {
        Assert.Equal(expected, SyntaxFacts.IsValidIdentifier(text));
    }

    [Theory]
    [InlineData("begin", SyntaxKind.BeginKeyword)]
    [InlineData("Begin", SyntaxKind.BeginKeyword)]
    [InlineData("virtual", SyntaxKind.VirtualKeyword)]
    [InlineData("READ", SyntaxKind.ReadKeyword)]
    [InlineData("x", SyntaxKind.None)]
    public void GetKeywordKind_ReservedThenContextual(string text, SyntaxKind kind)
    {
        Assert.Equal(kind, SyntaxFacts.GetKeywordKind(text));
    }

    public static TheoryData<SyntaxKind> FixedTextKinds =>
        [.. Enum.GetValues<SyntaxKind>().Where(k => SyntaxFacts.GetText(k).Length > 0)];

    [Theory]
    [MemberData(nameof(FixedTextKinds))]
    public void GetText_LexesBackToTheSameKind(SyntaxKind kind)
    {
        var text = SyntaxFacts.GetText(kind);
        var token = Lexer.Tokenize(SourceText.From(LexerTests.Path, text))[0];

        var expected = SyntaxFacts.IsContextualKeyword(kind) ? SyntaxKind.IdentifierToken : kind;
        Assert.Equal(expected, token.Kind);
        Assert.Equal(text, token.Text);
    }

    [Fact]
    public void GetText_CoversEverySymbolAndKeyword()
    {
        var withText = Enum.GetValues<SyntaxKind>().Where(k => SyntaxFacts.GetText(k).Length > 0).ToHashSet();

        Assert.All(Enum.GetValues<SyntaxKind>().Where(k => SyntaxFacts.IsKeyword(k) || SyntaxFacts.IsContextualKeyword(k)
                                                     || SyntaxFacts.IsPunctuation(k) || SyntaxFacts.IsOperator(k)),
                   k => Assert.Contains(k, withText));
        Assert.Equal("begin", SyntaxFacts.GetText(SyntaxKind.BeginKeyword));
        Assert.Equal("[", SyntaxFacts.GetText(SyntaxKind.OpenBracketToken));
        Assert.Equal("#", SyntaxFacts.GetText(SyntaxKind.HashToken));
        Assert.Equal("", SyntaxFacts.GetText(SyntaxKind.IdentifierToken));
        Assert.Equal("", SyntaxFacts.GetText(SyntaxKind.WhitespaceTrivia));
    }

    [Fact]
    public void KindPredicates_ClassifyEveryKind()
    {
        Assert.Equal(62, Enum.GetValues<SyntaxKind>().Count(SyntaxFacts.IsKeyword));
        Assert.Equal(SyntaxFacts.ContextualKeywords.Length, Enum.GetValues<SyntaxKind>().Count(SyntaxFacts.IsContextualKeyword));
        Assert.Equal(
            [SyntaxKind.DotToken, SyntaxKind.CommaToken, SyntaxKind.SemicolonToken, SyntaxKind.ColonToken,
             SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken],
            Enum.GetValues<SyntaxKind>().Where(SyntaxFacts.IsPunctuation));
        Assert.Equal(
            [SyntaxKind.PlusToken, SyntaxKind.MinusToken, SyntaxKind.AsteriskToken, SyntaxKind.SlashToken, SyntaxKind.EqualsToken,
             SyntaxKind.LessThanGreaterThanToken, SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken,
             SyntaxKind.LessThanEqualsToken, SyntaxKind.GreaterThanEqualsToken, SyntaxKind.ColonEqualsToken,
             SyntaxKind.DotDotToken, SyntaxKind.CaretToken, SyntaxKind.AtToken],
            Enum.GetValues<SyntaxKind>().Where(SyntaxFacts.IsOperator));
        Assert.Equal(
            [SyntaxKind.IntegerLiteralToken, SyntaxKind.RealLiteralToken, SyntaxKind.StringLiteralToken],
            Enum.GetValues<SyntaxKind>().Where(SyntaxFacts.IsLiteral));
        Assert.Equal(9, Enum.GetValues<SyntaxKind>().Count(SyntaxFacts.IsTrivia));
        Assert.Equal(
            [SyntaxKind.LineCommentTrivia, SyntaxKind.BlockCommentTrivia, SyntaxKind.DocCommentTrivia],
            Enum.GetValues<SyntaxKind>().Where(SyntaxFacts.IsComment));
        Assert.True(SyntaxFacts.IsKeyword(SyntaxKind.StringKeyword));
        Assert.False(SyntaxFacts.IsKeyword(SyntaxKind.VirtualKeyword));
        Assert.False(SyntaxFacts.IsKeyword(SyntaxKind.IdentifierToken));
    }

    [Theory]
    [Trait("Req", "LANG-170")]
    [InlineData(SyntaxKind.EqualsToken, 2)]
    [InlineData(SyntaxKind.LessThanGreaterThanToken, 2)]
    [InlineData(SyntaxKind.LessThanToken, 2)]
    [InlineData(SyntaxKind.GreaterThanToken, 2)]
    [InlineData(SyntaxKind.LessThanEqualsToken, 2)]
    [InlineData(SyntaxKind.GreaterThanEqualsToken, 2)]
    [InlineData(SyntaxKind.InKeyword, 2)]
    [InlineData(SyntaxKind.IsKeyword, 2)]
    [InlineData(SyntaxKind.PlusToken, 3)]
    [InlineData(SyntaxKind.MinusToken, 3)]
    [InlineData(SyntaxKind.OrKeyword, 3)]
    [InlineData(SyntaxKind.XorKeyword, 3)]
    [InlineData(SyntaxKind.AsteriskToken, 4)]
    [InlineData(SyntaxKind.SlashToken, 4)]
    [InlineData(SyntaxKind.DivKeyword, 4)]
    [InlineData(SyntaxKind.ModKeyword, 4)]
    [InlineData(SyntaxKind.AndKeyword, 4)]
    [InlineData(SyntaxKind.ShlKeyword, 4)]
    [InlineData(SyntaxKind.ShrKeyword, 4)]
    [InlineData(SyntaxKind.AsKeyword, 4)]
    [InlineData(SyntaxKind.NotKeyword, 0)]
    [InlineData(SyntaxKind.AtToken, 0)]
    [InlineData(SyntaxKind.ColonEqualsToken, 0)]
    [InlineData(SyntaxKind.IdentifierToken, 0)]
    public void GetBinaryPrecedence_FollowsTheGrammar(SyntaxKind kind, int precedence)
    {
        Assert.Equal(precedence, SyntaxFacts.GetBinaryPrecedence(kind));
    }
}
