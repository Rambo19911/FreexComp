using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService;

/// <summary>Formatting helpers (language-service.md §10).</summary>
public static class IndentationService
{
    /// <summary>
    /// CMP-765: the indentation (in columns) of the line containing the first token of the innermost construct whose body holds
    /// <paramref name="lineNumber"/> (0-based) — <c>begin</c>, <c>record</c>, <c>class</c>, <c>interface</c>, <c>case</c>,
    /// <c>try</c>, <c>repeat</c>, a declaration section, or the statement after a trailing <c>then</c> / <c>else</c> /
    /// <c>do</c> — plus <paramref name="indentSize"/>; 0 outside every construct. A tab counts as <paramref name="indentSize"/>.
    /// </summary>
    public static int GetDesiredIndentation(SyntaxTree tree, int lineNumber, int indentSize)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var lines = tree.Text.Lines;
        if (lineNumber <= 0 || lineNumber >= lines.LineCount)
        {
            return 0;
        }

        var lineStart = lines.GetLineSpan(lineNumber).Start;
        var previous = default(SyntaxToken);
        foreach (var token in tree.Root.OwnTokens())
        {
            if (token.IsMissing || token.Kind == SyntaxKind.EndOfFileToken)
            {
                continue;
            }

            if (token.Span.End > lineStart)
            {
                break;
            }

            previous = token;
        }

        if (previous.Parent is not { } parent)
        {
            return 0;
        }

        if (previous.Kind is SyntaxKind.ThenKeyword or SyntaxKind.ElseKeyword or SyntaxKind.DoKeyword)
        {
            return IndentationOfLineAt(tree, parent.Span.Start, indentSize) + indentSize;
        }

        for (SyntaxNode? node = parent; node is not null; node = node.Parent)
        {
            if (Body(node) is var (open, close) && open.Span.End <= lineStart && (close is not { } end || end.IsMissing || end.Span.Start >= lineStart))
            {
                return IndentationOfLineAt(tree, node.Span.Start, indentSize) + indentSize;
            }
        }

        return 0;
    }

    /// <summary>
    /// The span of the token matching the bracket or block keyword at <paramref name="position"/> (<c>( )</c>, <c>[ ]</c>,
    /// <c>begin</c>/<c>case</c>/<c>try</c>/<c>record</c>/<c>class</c>/<c>interface</c>/<c>repeat</c> with their
    /// <c>end</c>/<c>until</c>); null when there is none.
    /// </summary>
    public static TextSpan? GetMatchingBracket(SyntaxTree tree, int position)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var token = tree.FindToken(position);
        if (!token.Span.Contains(position) && position > 0)
        {
            token = tree.FindToken(position - 1);
        }

        if (token.Parent is not { } parent || token.IsMissing)
        {
            return null;
        }

        if (Body(parent) is var (open, close) && close is { IsMissing: false } end)
        {
            var opening = parent is CaseStatementSyntax @case ? @case.CaseKeyword : open;
            if (token == opening)
            {
                return end.Span;
            }

            if (token == end)
            {
                return opening.Span;
            }
        }

        var (openKind, closeKind, forward) = token.Kind switch
        {
            SyntaxKind.OpenParenToken => (SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, true),
            SyntaxKind.CloseParenToken => (SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken, false),
            SyntaxKind.OpenBracketToken => (SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken, true),
            SyntaxKind.CloseBracketToken => (SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken, false),
            _ => (SyntaxKind.None, SyntaxKind.None, false),
        };
        if (openKind == SyntaxKind.None)
        {
            return null;
        }

        // the partner is a direct token of the same node, at the same bracket depth among those tokens
        var siblings = parent.ChildNodesAndTokens().Where(c => !c.IsNode).Select(c => c.AsToken()).Where(t => !t.IsMissing).ToList();
        var index = siblings.IndexOf(token);
        var depth = 0;
        for (var i = index; i >= 0 && i < siblings.Count; i += forward ? 1 : -1)
        {
            var kind = siblings[i].Kind;
            depth += kind == openKind ? (forward ? 1 : -1) : kind == closeKind ? (forward ? -1 : 1) : 0;
            if (depth == 0)
            {
                return siblings[i].Span;
            }
        }

        return null;
    }

    /// <summary>The token opening a construct's body and the token closing it (null: no closing token, a declaration section).</summary>
    private static (SyntaxToken Open, SyntaxToken? Close)? Body(SyntaxNode node) => node switch
    {
        CompoundStatementSyntax compound => (compound.BeginKeyword, compound.EndKeyword),
        RecordTypeSyntax record => (record.RecordKeyword, record.EndKeyword),
        ClassTypeSyntax { EndKeyword: { } end } @class => (@class.ClassKeyword, end),
        InterfaceTypeSyntax { EndKeyword: { } end } @interface => (@interface.InterfaceKeyword, end),
        CaseStatementSyntax @case => (@case.OfKeyword, @case.EndKeyword),
        TryFinallyStatementSyntax @try => (@try.TryKeyword, @try.EndKeyword),
        TryExceptStatementSyntax @try => (@try.TryKeyword, @try.EndKeyword),
        RepeatStatementSyntax repeat => (repeat.RepeatKeyword, repeat.UntilKeyword),
        TypeSectionSyntax section => (section.TypeKeyword, null),
        ConstSectionSyntax section => (section.Keyword, null),
        VarSectionSyntax section => (section.Keyword, null),
        _ => null,
    };

    private static int IndentationOfLineAt(SyntaxTree tree, int offset, int indentSize)
    {
        var lines = tree.Text.Lines;
        var span = lines.GetLineSpan(lines.GetLinePosition(offset).Line);
        var columns = 0;
        for (var i = span.Start; i < span.End; i++)
        {
            switch (tree.Text.Text[i])
            {
                case ' ':
                    columns++;
                    break;
                case '\t':
                    columns += indentSize;
                    break;
                default:
                    return columns;
            }
        }

        return columns;
    }
}
