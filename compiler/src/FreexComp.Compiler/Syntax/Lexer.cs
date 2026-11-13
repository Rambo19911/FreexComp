using System.Collections.Immutable;

namespace FreexComp.Compiler.Syntax;

public sealed record ParseOptions(ImmutableArray<string> Defines)
{
    /// <summary>Absolute directories searched for <c>{$I file}</c> after the including file's directory (LANG-875).</summary>
    public ImmutableArray<string> SearchPaths { get; init; } = [];

    /// <summary>LANG-862: evaluates <c>{$IF}</c> conditions; null = they count as True (pure syntax operations).</summary>
    internal IConditionEvaluator? ConditionEvaluator { get; init; }
}

/// <summary>
/// Whole-text tokenization (api.md §4, CMP-222): the Phase 1 entry point for syntax highlighting. Never throws;
/// every character belongs to exactly one token or trivia piece; the last token is EndOfFileToken; tokens are
/// parentless and <see cref="SyntaxToken.ContextualKind"/> equals <see cref="SyntaxToken.Kind"/>.
/// </summary>
public static class Lexer
{
    public static ImmutableArray<SyntaxToken> Tokenize(SourceText text, ParseOptions? options = null) =>
        Run(text, options).Tokens;

    /// <summary>Lexical (and, once the preprocessor exists, directive) diagnostics.</summary>
    public static ImmutableArray<Diagnostic> GetDiagnostics(SourceText text, ParseOptions? options = null) =>
        Run(text, options).Diagnostics;

    private static (ImmutableArray<SyntaxToken> Tokens, ImmutableArray<Diagnostic> Diagnostics) Run(SourceText text, ParseOptions? options)
    {
        var lexer = new PascalLexer(text, options);
        var tokens = ImmutableArray.CreateBuilder<SyntaxToken>();
        SyntaxToken token;
        do
        {
            token = lexer.Lex();
            tokens.Add(token);
        }
        while (token.Kind != SyntaxKind.EndOfFileToken);

        return (tokens.ToImmutable(), lexer.Diagnostics);
    }
}
