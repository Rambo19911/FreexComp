namespace FreexComp.Compiler.Syntax;

/// <summary>
/// Double-dispatch visitor (api.md §4): one virtual <c>VisitXxx</c> per node class, each defaulting to
/// <see cref="DefaultVisit"/>. Node classes added later add their method here.
/// </summary>
public abstract class SyntaxVisitor<TResult>
{
    /// <summary><paramref name="node"/>.Accept(this), or <c>default</c> for null.</summary>
    public virtual TResult Visit(SyntaxNode? node) => node is null ? default! : node.Accept(this);

    public virtual TResult DefaultVisit(SyntaxNode node) => default!;

    public virtual TResult VisitIdentifierName(IdentifierNameSyntax node) => DefaultVisit(node);

    public virtual TResult VisitQualifiedName(QualifiedNameSyntax node) => DefaultVisit(node);
}

/// <summary>Depth-first walk over every node, token and trivia piece in source order.</summary>
public abstract class SyntaxWalker : SyntaxVisitor<object?>
{
    /// <summary>Visits the children: nodes through <see cref="SyntaxVisitor{TResult}.Visit"/>, tokens through <see cref="VisitToken"/>.</summary>
    public override object? DefaultVisit(SyntaxNode node)
    {
        foreach (var child in node.ChildNodesAndTokens())
        {
            if (child.AsNode() is { } childNode)
            {
                Visit(childNode);
            }
            else
            {
                VisitToken(child.AsToken());
            }
        }

        return null;
    }

    /// <summary>Visits the leading, then the trailing trivia.</summary>
    public virtual void VisitToken(SyntaxToken token)
    {
        foreach (var trivia in token.LeadingTrivia)
        {
            VisitTrivia(trivia);
        }

        foreach (var trivia in token.TrailingTrivia)
        {
            VisitTrivia(trivia);
        }
    }

    public virtual void VisitTrivia(SyntaxTrivia trivia)
    {
    }
}
