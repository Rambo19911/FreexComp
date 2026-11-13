namespace FreexComp.Compiler.Syntax;

// grammar.md §7: genericParams, genericArgs, attributeList, anonymousMethod (C-019).

/// <summary><c>&lt; type { , type } &gt;</c> (use side, LANG-705).</summary>
public sealed class TypeArgumentListSyntax : SyntaxNode
{
    internal TypeArgumentListSyntax(SyntaxToken lessThanToken, SeparatedSyntaxList<TypeSyntax> arguments, SyntaxToken greaterThanToken)
        : base(SyntaxKind.TypeArgumentList)
    {
        LessThanToken = Add(lessThanToken);
        Arguments = Add(arguments);
        GreaterThanToken = Add(greaterThanToken);
    }

    public SyntaxToken LessThanToken { get; }

    public SeparatedSyntaxList<TypeSyntax> Arguments { get; }

    /// <summary>May be the first half of a split <c>&gt;=</c> (LANG-706).</summary>
    public SyntaxToken GreaterThanToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitTypeArgumentList(this);
}

/// <summary><c>&lt; genericParam { ; genericParam } &gt;</c> (declaration side).</summary>
public sealed class TypeParameterListSyntax : SyntaxNode
{
    internal TypeParameterListSyntax(SyntaxToken lessThanToken, SeparatedSyntaxList<TypeParameterSyntax> parameters, SyntaxToken greaterThanToken)
        : base(SyntaxKind.TypeParameterList)
    {
        LessThanToken = Add(lessThanToken);
        Parameters = Add(parameters);
        GreaterThanToken = Add(greaterThanToken);
    }

    public SyntaxToken LessThanToken { get; }

    /// <summary>Separated by <c>;</c>.</summary>
    public SeparatedSyntaxList<TypeParameterSyntax> Parameters { get; }

    /// <summary>May be the first half of a split <c>&gt;=</c> (LANG-706).</summary>
    public SyntaxToken GreaterThanToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitTypeParameterList(this);
}

/// <summary><c>identList [ : constraint { , constraint } ]</c> (LANG-702).</summary>
public sealed class TypeParameterSyntax : SyntaxNode
{
    internal TypeParameterSyntax(SeparatedSyntaxList<IdentifierNameSyntax> identifiers, SyntaxToken? colonToken,
                                 SeparatedSyntaxList<SyntaxNode> constraints)
        : base(SyntaxKind.TypeParameter)
    {
        Identifiers = Add(identifiers);
        ColonToken = Add(colonToken);
        Constraints = Add(constraints);
    }

    public SeparatedSyntaxList<IdentifierNameSyntax> Identifiers { get; }

    public SyntaxToken? ColonToken { get; }

    /// <summary><see cref="TypeSyntax"/> (a class or interface type) or <see cref="KeywordConstraintSyntax"/> items.</summary>
    public SeparatedSyntaxList<SyntaxNode> Constraints { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitTypeParameter(this);
}

/// <summary>The constraint <c>class</c>, <c>record</c> or <c>constructor</c>.</summary>
public sealed class KeywordConstraintSyntax : SyntaxNode
{
    internal KeywordConstraintSyntax(SyntaxToken keyword)
        : base(SyntaxKind.KeywordConstraint)
    {
        Keyword = Add(keyword);
    }

    public SyntaxToken Keyword { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitKeywordConstraint(this);
}

/// <summary><c>"[" attribute { , attribute } "]"</c> (LANG-760).</summary>
public sealed class AttributeListSyntax : SyntaxNode
{
    internal AttributeListSyntax(SyntaxToken openBracketToken, SeparatedSyntaxList<AttributeSyntax> attributes, SyntaxToken closeBracketToken)
        : base(SyntaxKind.AttributeList)
    {
        OpenBracketToken = Add(openBracketToken);
        Attributes = Add(attributes);
        CloseBracketToken = Add(closeBracketToken);
    }

    public SyntaxToken OpenBracketToken { get; }

    public SeparatedSyntaxList<AttributeSyntax> Attributes { get; }

    public SyntaxToken CloseBracketToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitAttributeList(this);
}

/// <summary><c>qualIdent [ ( [ constExpr { , constExpr } ] ) ]</c></summary>
public sealed class AttributeSyntax : SyntaxNode
{
    internal AttributeSyntax(NameSyntax name, ArgumentListSyntax? argumentList)
        : base(SyntaxKind.Attribute)
    {
        Name = Add(name);
        ArgumentList = AddOptional(argumentList);
    }

    public NameSyntax Name { get; }

    public ArgumentListSyntax? ArgumentList { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitAttribute(this);
}

/// <summary><c>procedure [ formalParams ] block</c> or <c>function [ formalParams ] : resultType block</c> (LANG-720).</summary>
public sealed class AnonymousMethodExpressionSyntax : ExpressionSyntax
{
    internal AnonymousMethodExpressionSyntax(SyntaxToken keyword, ParameterListSyntax? parameterList, SyntaxToken? colonToken,
                                             TypeSyntax? resultType, BlockSyntax block)
        : base(SyntaxKind.AnonymousMethodExpression)
    {
        Keyword = Add(keyword);
        ParameterList = AddOptional(parameterList);
        ColonToken = Add(colonToken);
        ResultType = AddOptional(resultType);
        Block = Add(block);
    }

    /// <summary><c>procedure</c> or <c>function</c>.</summary>
    public SyntaxToken Keyword { get; }

    public ParameterListSyntax? ParameterList { get; }

    public SyntaxToken? ColonToken { get; }

    public TypeSyntax? ResultType { get; }

    public BlockSyntax Block { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitAnonymousMethodExpression(this);
}
