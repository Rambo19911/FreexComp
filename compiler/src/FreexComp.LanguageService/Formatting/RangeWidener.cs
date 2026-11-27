using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService.Formatting;

/// <summary>
/// CMP-1453: widens a range to whole statements and declarations — at each end the smallest enclosing statement-list item or
/// declaration (with the <c>;</c> after it; a <c>;</c> at an end counts as the end of the item before it); where there is none
/// (a unit heading, <c>interface</c>), the token itself. A range that reaches the last token reaches the end of the file.
/// </summary>
internal static class RangeWidener
{
    /// <summary>The indices (into <see cref="LayoutPlan.Tokens"/>) of the first and the last token of the widened range.</summary>
    public static (int First, int Last) Widen(LayoutPlan plan, TextSpan range)
    {
        var tokens = plan.Tokens;
        var lastReal = tokens.Length - 1;
        while (lastReal > 0 && tokens[lastReal].Kind == SyntaxKind.EndOfFileToken)
        {
            lastReal--;
        }

        if (lastReal < 0 || tokens[lastReal].Kind == SyntaxKind.EndOfFileToken)
        {
            return (0, tokens.Length - 1);
        }

        var start = 0;
        while (start < lastReal && tokens[start].Span.End <= range.Start)
        {
            start++;
        }

        var end = lastReal;
        while (end > start && tokens[end].Span.Start >= range.End)
        {
            end--;
        }

        var (a, b) = Item(plan, start);
        var (c, d) = Item(plan, end);
        var first = Math.Min(a, c);
        var last = Math.Max(b, d);
        if (last + 1 <= lastReal && tokens[last + 1].Kind == SyntaxKind.SemicolonToken)
        {
            last++;
        }

        return (first, last == lastReal ? tokens.Length - 1 : last);        // up to the last token: the end of the file too
    }

    /// <summary>The first and last token index of the smallest item around token <paramref name="i"/>.</summary>
    private static (int First, int Last) Item(LayoutPlan plan, int i)
    {
        if (i > 0 && plan.Tokens[i].Kind == SyntaxKind.SemicolonToken)
        {
            i--;        // a statement separator ends the item before it
        }

        for (var node = plan.Tokens[i].Parent; node is not null; node = node.Parent)
        {
            if (IsItem(node) && plan.IndexOf(node.FirstToken()) is var first and >= 0 && plan.IndexOf(node.LastToken()) is var last and >= 0)
            {
                return (Math.Min(first, i), Math.Max(last, i));
            }
        }

        return (i, i);
    }

    private static bool IsItem(SyntaxNode node) => node switch
    {
        StatementSyntax => node.Parent is CompoundStatementSyntax or BlockSyntax or InitializationSectionSyntax or FinalizationSectionSyntax
            or RepeatStatementSyntax or TryFinallyStatementSyntax or TryExceptStatementSyntax or CaseStatementSyntax,
        CaseArmSyntax or ExceptionHandlerSyntax => true,
        MemberDeclarationSyntax or ConstDeclarationSyntax or TypeDeclarationSyntax or VarDeclarationSyntax or RoutineDeclarationSyntax
            or UsesClauseSyntax or LabelSectionSyntax or ExportsClauseSyntax => true,
        _ => false,
    };
}
