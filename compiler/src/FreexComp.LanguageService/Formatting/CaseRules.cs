using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;

namespace FreexComp.LanguageService.Formatting;

/// <summary>
/// The letter case the formatter writes words in (formatter.md §3): keywords and directive names (CMP-1448) and identifiers
/// in the casing of their declaration (CMP-1449). Only the case changes, never the letters; string literals and comments are
/// never touched.
/// </summary>
internal static class CaseRules
{
    /// <summary>Where a file with no usable path is analysed (it is never read: the analysis uses an empty file system).</summary>
    private const string UntitledPath = @"C:\FreexComp.Format\Untitled.pas";

    /// <summary>Fills <see cref="LayoutPlan.Texts"/> for the tokens whose case <paramref name="profile"/> changes.</summary>
    public static void Apply(LayoutPlan plan, SourceText text, FormatProfile profile, CancellationToken ct)
    {
        var declared = profile.IdentifiersAsDeclared ? TryDeclaredNames(text, ct) : null;
        var tokens = plan.Tokens;
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Text.Length == 0 || token.Text[0] == '&')
            {
                continue;       // an escaped identifier is written as it is
            }

            if (profile.Keywords != KeywordCase.AsIs && IsKeyword(token))
            {
                var cased = profile.Keywords == KeywordCase.Upper ? token.Text.ToUpperInvariant() : token.Text.ToLowerInvariant();
                if (cased != token.Text)
                {
                    plan.Texts[i] = cased;
                }
            }
            else if (declared is not null && token.Kind == SyntaxKind.IdentifierToken && declared.TryGetValue(token.Span.Start, out var name)
                     && name.Length == token.Text.Length && string.Equals(name, token.Text, StringComparison.OrdinalIgnoreCase))
            {
                plan.Texts[i] = name;
            }
        }
    }

    /// <summary>
    /// CMP-1448: a reserved word, or an identifier the parser used as a directive (<c>virtual</c>, <c>override</c>, <c>read</c>,
    /// <c>private</c> …).
    /// </summary>
    private static bool IsKeyword(SyntaxToken token) =>
        SyntaxFacts.IsKeyword(token.Kind)
        || (token.Kind == SyntaxKind.IdentifierToken && token.ContextualKind != token.Kind && SyntaxFacts.IsContextualKeyword(token.ContextualKind));

    /// <summary>CMP-1449: when binding fails, every identifier is left as it is (the layout never depends on it).</summary>
    private static Dictionary<int, string>? TryDeclaredNames(SourceText text, CancellationToken ct)
    {
        try
        {
            return DeclaredNames(text, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// CMP-1449: span start → the declared spelling, for the identifier tokens that bind to a symbol whose name is written in
    /// another case. The file is bound on its own (no project context: its own declarations and the compiler's built-ins).
    /// </summary>
    private static Dictionary<int, string> DeclaredNames(SourceText text, CancellationToken ct)
    {
        var path = Path.IsPathFullyQualified(text.Path) ? text.Path : UntitledPath;
        var options = new CompilationOptions { MainFile = path, MaxParallelism = 1 };
        var compilation = Compilation.Create(options, [text with { Path = path }], new InMemoryFileSystem());
        var model = compilation.GetSemanticModel(path);
        var names = new Dictionary<int, string>();
        var count = 0;
        foreach (var token in model.SyntaxTree.Root.OwnTokens())
        {
            if ((++count & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            if (token.Kind != SyntaxKind.IdentifierToken || token.IsMissing || token.ContextualKind != token.Kind)
            {
                continue;
            }

            var symbol = model.GetDeclaredSymbol(token, ct) ?? (token.Parent is { } parent ? model.GetSymbolInfo(parent, ct).Symbol : null);
            if (symbol?.Name is { } name && name != token.Text)
            {
                names[token.Span.Start] = name;
            }
        }

        return names;
    }

    /// <summary>
    /// CMP-1448: the directive <c>{$name …}</c> / <c>(*$name …*)</c> with its name in <paramref name="wanted"/> case; the rest
    /// of the directive (arguments, file names) is kept.
    /// </summary>
    public static string Directive(string text, KeywordCase wanted)
    {
        var start = text.StartsWith("{$", StringComparison.Ordinal) ? 2 : text.StartsWith("(*$", StringComparison.Ordinal) ? 3 : -1;
        if (start < 0 || wanted == KeywordCase.AsIs)
        {
            return text;
        }

        var end = start;
        while (end < text.Length && (char.IsAsciiLetterOrDigit(text[end]) || text[end] == '_'))
        {
            end++;
        }

        var name = text[start..end];
        var cased = wanted == KeywordCase.Upper ? name.ToUpperInvariant() : name.ToLowerInvariant();
        return cased == name ? text : string.Concat(text.AsSpan(0, start), cased, text.AsSpan(end));
    }
}
