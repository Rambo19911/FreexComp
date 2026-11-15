using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Syntax;

/// <summary>
/// C-243 (LANG-142, CMP-730): the doc comment of a declaration — the doc-comment trivia directly before its first token (only
/// whitespace between them), markers and common leading whitespace removed — recorded in <see cref="Symbol.Documentation"/>.
/// </summary>
internal static class DocCommentText
{
    /// <summary>Fills <see cref="Symbol.Documentation"/> of the unit, its members and the members of its types declared in <paramref name="tree"/>.</summary>
    public static void Fill(SyntaxTree tree, UnitSymbol unit)
    {
        var seen = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Symbol>();
        pending.Push(unit);
        while (pending.Count > 0)
        {
            var symbol = pending.Pop();
            if (!seen.Add(symbol))
            {
                continue;
            }

            var own = false;
            foreach (var location in symbol.Locations)
            {
                if (!location.HasPosition || !PathUtil.AreEqual(location.Path, tree.Text.Path))
                {
                    continue;
                }

                own = true;
                if (symbol.Documentation is null && Of(tree, location.Span.Start) is { } text)
                {
                    symbol.Documentation = text;
                }
            }

            IEnumerable<Symbol> members = symbol switch
            {
                UnitSymbol u => u.InterfaceMembers.Concat(u.ImplementationMembers),
                TypeSymbol type when own => type.Members,
                _ => [],
            };
            foreach (var member in members)
            {
                pending.Push(member);
            }
        }
    }

    /// <summary>The doc comment of the declaration containing the token at <paramref name="position"/>; null without one.</summary>
    public static string? Of(SyntaxTree tree, int position)
    {
        var node = tree.FindToken(position).Parent?.AncestorsAndSelf().FirstOrDefault(n => n is RoutineDeclarationSyntax
            or MethodDeclarationSyntax or TypeDeclarationSyntax or VarDeclarationSyntax or ConstDeclarationSyntax or FieldDeclarationSyntax
            or PropertyDeclarationSyntax or EnumMemberSyntax or UnitSyntax or ProgramSyntax);
        return node is null ? null : Text(node.FirstToken().LeadingTrivia);
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
        var openingLines = new HashSet<int>();                              // the text after a block comment's opening marker
        foreach (var piece in pieces)
        {
            var block = !piece.StartsWith("///", StringComparison.Ordinal);
            var body = !block ? piece[3..]
                : piece.StartsWith("{**", StringComparison.Ordinal) ? piece[3..^1]
                : piece.StartsWith("(**", StringComparison.Ordinal) ? piece[3..^2]
                : piece;
            var split = body.ReplaceLineEndings("\n").Split('\n');
            if (block && split.Length > 1)
            {
                openingLines.Add(lines.Count);
                split[0] = split[0].TrimStart();
            }

            lines.AddRange(split);
        }

        var indent = lines.Where((l, i) => l.Trim().Length > 0 && !openingLines.Contains(i)).Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0).Min();
        var text = lines.Select((l, i) => openingLines.Contains(i) ? l.TrimEnd() : l.Length >= indent ? l[indent..].TrimEnd() : l.Trim()).ToList();
        while (text.Count > 0 && text[^1].Length == 0)
        {
            text.RemoveAt(text.Count - 1);
        }

        while (text.Count > 0 && text[0].Length == 0)
        {
            text.RemoveAt(0);
        }

        return text.Count == 0 ? null : string.Join("\n", text);
    }
}
