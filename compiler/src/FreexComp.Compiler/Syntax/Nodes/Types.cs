namespace FreexComp.Compiler.Syntax;

// grammar.md §3. C-013 needs type identifiers and string types; the other type syntaxes follow with C-014.

/// <summary><c>typeId</c>: a (possibly qualified) type name.</summary>
public sealed class NamedTypeSyntax : TypeSyntax
{
    internal NamedTypeSyntax(NameSyntax name)
        : base(SyntaxKind.NamedType)
    {
        Name = Add(name);
    }

    public NameSyntax Name { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitNamedType(this);
}

/// <summary><c>string [ "[" constExpr "]" ]</c>; with a length it is a ShortString of that maximum length.</summary>
public sealed class StringTypeSyntax : TypeSyntax
{
    internal StringTypeSyntax(SyntaxToken stringKeyword, SyntaxToken? openBracketToken, ExpressionSyntax? length,
                              SyntaxToken? closeBracketToken)
        : base(SyntaxKind.StringType)
    {
        StringKeyword = Add(stringKeyword);
        OpenBracketToken = Add(openBracketToken);
        Length = AddOptional(length);
        CloseBracketToken = Add(closeBracketToken);
    }

    public SyntaxToken StringKeyword { get; }

    public SyntaxToken? OpenBracketToken { get; }

    public ExpressionSyntax? Length { get; }

    public SyntaxToken? CloseBracketToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitStringType(this);
}
