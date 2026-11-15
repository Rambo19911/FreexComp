using FreexComp.Compiler;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;

namespace FreexComp.LanguageService;

/// <summary>CMP-730: the doc comment (LANG-142) of a symbol's declaration, markers and common leading whitespace removed.</summary>
internal static class DocComments
{
    public static string? Of(Compilation compilation, Symbol symbol)
    {
        if (!string.IsNullOrEmpty(symbol.Documentation))
        {
            return symbol.Documentation;
        }

        foreach (var location in symbol.Locations)
        {
            if (!location.HasPosition || !compilation.TryGetSyntaxTree(location.Path, out var tree))
            {
                continue;
            }

            var node = tree.FindToken(location.Span.Start).Parent?.AncestorsAndSelf().FirstOrDefault(n => n is RoutineDeclarationSyntax
                or MethodDeclarationSyntax or TypeDeclarationSyntax or VarDeclarationSyntax or ConstDeclarationSyntax or FieldDeclarationSyntax
                or PropertyDeclarationSyntax or EnumMemberSyntax or UnitSyntax or ProgramSyntax);
            if (node is not null && Text(node.FirstToken().LeadingTrivia) is { } text)
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>The doc-comment pieces directly before the token (only whitespace between them), as plain text.</summary>
    private static string? Text(IReadOnlyList<SyntaxTrivia> trivia)
    {
        var pieces = new List<string>();
        for (var i = trivia.Count - 1; i >= 0; i--)
        {
            var piece = trivia[i];
            if (piece.Kind is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia)
            {
                continue;
            }

            if (piece.Kind != SyntaxKind.DocCommentTrivia)
            {
                break;
            }

            pieces.Insert(0, piece.Text);
        }

        if (pieces.Count == 0)
        {
            return null;
        }

        var lines = new List<string>();
        foreach (var piece in pieces)
        {
            var body = piece.StartsWith("///", StringComparison.Ordinal) ? piece[3..]
                : piece.StartsWith("{**", StringComparison.Ordinal) ? piece[3..^1]
                : piece.StartsWith("(**", StringComparison.Ordinal) ? piece[3..^2]
                : piece;
            lines.AddRange(body.ReplaceLineEndings("\n").Split('\n'));
        }

        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        while (lines.Count > 0 && lines[0].Trim().Length == 0)
        {
            lines.RemoveAt(0);
        }

        var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join("\n", lines.Select(l => l.Length >= indent ? l[indent..].TrimEnd() : l.Trim()));
    }
}
