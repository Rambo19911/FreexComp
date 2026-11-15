using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService;

/// <summary>
/// The kind of a folding range (CMP-760): <c>Section</c> = interface / implementation, <c>Uses</c> = uses clause,
/// <c>Declarations</c> = a type / const / var section; the others as named in CMP-760.
/// </summary>
public enum FoldingKind { Section, Uses, Declarations, Type, Routine, Block, Region, Comment, Disabled }

public readonly record struct FoldingRange(TextSpan Span, FoldingKind Kind, string CollapsedText);

/// <summary>Folding (language-service.md §9).</summary>
public static class FoldingService
{
    /// <summary>CMP-760: every range spanning at least two lines, sorted by start (longer first at the same start).</summary>
    public static ImmutableArray<FoldingRange> GetFoldingRanges(SyntaxTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var text = tree.Text;
        var ranges = new List<FoldingRange>();
        void Add(TextSpan span, FoldingKind kind, string? collapsed = null)
        {
            if (span.Length > 0 && text.Lines.GetLinePosition(span.Start).Line < text.Lines.GetLinePosition(span.End).Line)
            {
                ranges.Add(new FoldingRange(span, kind, collapsed ?? FirstLine(text.Text, span) + " …"));
            }
        }

        foreach (var node in tree.Root.DescendantNodes())
        {
            switch (node)
            {
                case InterfaceSectionSyntax or ImplementationSectionSyntax:
                    Add(node.Span, FoldingKind.Section);
                    break;
                case UsesClauseSyntax:
                    Add(node.Span, FoldingKind.Uses);
                    break;
                case TypeSectionSyntax or ConstSectionSyntax or VarSectionSyntax:
                    Add(node.Span, FoldingKind.Declarations);
                    break;
                case ClassTypeSyntax { EndKeyword: { IsMissing: false } } or InterfaceTypeSyntax { EndKeyword: { IsMissing: false } } or RecordTypeSyntax or HelperTypeSyntax:
                    Add(node.Span, FoldingKind.Type);
                    break;
                case RoutineDeclarationSyntax { Block: not null }:
                    Add(node.Span, FoldingKind.Routine);
                    break;
                case CompoundStatementSyntax when node.Parent is BlockSyntax { Parent: RoutineDeclarationSyntax }:
                    break;                                                           // the routine range covers its body
                case CompoundStatementSyntax or CaseStatementSyntax or TryFinallyStatementSyntax or TryExceptStatementSyntax or RepeatStatementSyntax:
                    Add(node.Span, FoldingKind.Block);
                    break;
            }
        }

        AddTriviaRanges(tree, Add);
        return [.. ranges.OrderBy(r => r.Span.Start).ThenByDescending(r => r.Span.Length).ThenBy(r => r.Kind)];
    }

    /// <summary>Regions, multi-line comments, runs of three or more <c>//</c> lines and disabled text.</summary>
    private static void AddTriviaRanges(SyntaxTree tree, Action<TextSpan, FoldingKind, string?> add)
    {
        var lines = tree.Text.Lines;
        var regions = new Stack<(int Start, string Title)>();
        var lineComments = new List<TextSpan>();
        void FlushLineComments()
        {
            if (lineComments.Count >= 3)
            {
                add(TextSpan.FromBounds(lineComments[0].Start, lineComments[^1].End), FoldingKind.Comment, null);
            }

            lineComments.Clear();
        }

        foreach (var trivia in tree.DescendantTrivia())
        {
            switch (trivia.Kind)
            {
                case SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia:
                    continue;
                case SyntaxKind.LineCommentTrivia or SyntaxKind.DocCommentTrivia when trivia.Text.StartsWith("//", StringComparison.Ordinal):
                    if (lineComments.Count > 0 && lines.GetLinePosition(trivia.Span.Start).Line != lines.GetLinePosition(lineComments[^1].Start).Line + 1)
                    {
                        FlushLineComments();
                    }

                    lineComments.Add(trivia.Span);
                    continue;
            }

            FlushLineComments();
            switch (trivia.Kind)
            {
                case SyntaxKind.BlockCommentTrivia or SyntaxKind.DocCommentTrivia:
                    add(trivia.Span, FoldingKind.Comment, null);
                    break;
                case SyntaxKind.DisabledTextTrivia:
                    add(trivia.Span, FoldingKind.Disabled, null);
                    break;
                case SyntaxKind.DirectiveTrivia when trivia.Directive is { Name: "REGION" } region:
                    regions.Push((trivia.Span.Start, region.Argument.Trim().Trim('\'')));
                    break;
                case SyntaxKind.DirectiveTrivia when trivia.Directive is { Name: "ENDREGION" } && regions.Count > 0:
                    var (start, title) = regions.Pop();
                    add(TextSpan.FromBounds(start, trivia.Span.End), FoldingKind.Region, title);
                    break;
            }
        }

        FlushLineComments();
    }

    private static string FirstLine(string text, TextSpan span)
    {
        var end = text.IndexOfAny(['\r', '\n'], span.Start, span.Length);
        return text[span.Start..(end < 0 ? span.End : end)].TrimEnd();
    }
}
