using FreexComp.Compiler.Syntax;
using CompilerText = FreexComp.Compiler.Text;

namespace FreexComp.Ide.Core.Debugging;

/// <summary>
/// DBG-258: the expression a value tooltip evaluates, from the syntax tree — the identifier under the pointer, or the
/// full designator (<c>A.B[i]^</c>) up to the hovered token: hovering <c>B</c> gives <c>A.B</c>, the closing bracket
/// <c>A.B[i]</c>, the caret <c>A.B[i]^</c>. Never a call: a routine that is called, or a designator that contains a
/// call, gives nothing.
/// </summary>
public static class HoverExpression
{
    /// <summary>The hovered token's range and the expression to evaluate; null when there is none at <paramref name="offset"/>.</summary>
    public static (int Start, int Length, string Expression)? At(string path, string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (offset < 0 || offset >= text.Length)
            return null;
        var tree = SyntaxTree.Parse(FreexComp.Compiler.SourceText.From(CompilerText.PathUtil.Normalize(path ?? ""), text));
        var token = tree.FindToken(offset);
        if (token.IsMissing || offset < token.Span.Start || offset >= token.Span.End)
            return null;
        ExpressionSyntax? expression = token.Kind switch
        {
            SyntaxKind.IdentifierToken => token.Parent is IdentifierNameSyntax name
                ? name.Parent is MemberAccessExpressionSyntax member && ReferenceEquals(member.Name, name) ? member : name
                : null,
            SyntaxKind.CaretToken => token.Parent as DereferenceExpressionSyntax,
            SyntaxKind.CloseBracketToken => token.Parent as ElementAccessExpressionSyntax,
            _ => null,
        };
        if (expression is null)
        {
            // An identifier outside an expression (a declared variable, a parameter): the identifier alone.
            return token.Kind == SyntaxKind.IdentifierToken && token.Parent is not IdentifierNameSyntax
                ? (token.Span.Start, token.Span.Length, token.Text)
                : null;
        }

        if (!IsDesignator(expression) || expression.Parent is InvocationExpressionSyntax call && ReferenceEquals(call.Expression, expression))
            return null;
        return (token.Span.Start, token.Span.Length, text.Substring(expression.Span.Start, expression.Span.Length));
    }

    /// <summary>Identifiers joined by <c>.</c>, <c>[…]</c> and <c>^</c>, without a call anywhere (also not in an index).</summary>
    private static bool IsDesignator(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax => true,
        MemberAccessExpressionSyntax member => member.Name is IdentifierNameSyntax && IsDesignator(member.Expression),
        ElementAccessExpressionSyntax element => IsDesignator(element.Expression)
            && !element.Indexes.Any(index => index is InvocationExpressionSyntax || index.DescendantNodes().OfType<InvocationExpressionSyntax>().Any()),
        DereferenceExpressionSyntax dereference => IsDesignator(dereference.Expression),
        _ => false,
    };
}
