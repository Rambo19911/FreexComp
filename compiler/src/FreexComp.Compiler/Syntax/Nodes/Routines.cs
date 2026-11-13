namespace FreexComp.Compiler.Syntax;

// grammar.md §5 (formal parameters are shared by routine headings and procedural types).

/// <summary><c>( [ formalParam { ; formalParam } ] )</c></summary>
public sealed class ParameterListSyntax : SyntaxNode
{
    internal ParameterListSyntax(SyntaxToken openParenToken, SeparatedSyntaxList<ParameterSyntax> parameters, SyntaxToken closeParenToken)
        : base(SyntaxKind.ParameterList)
    {
        OpenParenToken = Add(openParenToken);
        Parameters = Add(parameters);
        CloseParenToken = Add(closeParenToken);
    }

    public SyntaxToken OpenParenToken { get; }

    /// <summary>Separated by <c>;</c>.</summary>
    public SeparatedSyntaxList<ParameterSyntax> Parameters { get; }

    public SyntaxToken CloseParenToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitParameterList(this);
}

/// <summary><c>[ const | var | out ] identList [ : paramType [ = constExpr ] ]</c> (no type = untyped).</summary>
public sealed class ParameterSyntax : SyntaxNode
{
    internal ParameterSyntax(SyntaxToken? modifier, SeparatedSyntaxList<IdentifierNameSyntax> identifiers, SyntaxToken? colonToken,
                             TypeSyntax? type, SyntaxToken? equalsToken, ExpressionSyntax? defaultValue)
        : base(SyntaxKind.Parameter)
    {
        Modifier = Add(modifier);
        Identifiers = Add(identifiers);
        ColonToken = Add(colonToken);
        Type = AddOptional(type);
        EqualsToken = Add(equalsToken);
        DefaultValue = AddOptional(defaultValue);
    }

    /// <summary><c>const</c>, <c>var</c>, or an identifier with contextual kind <see cref="SyntaxKind.OutKeyword"/>.</summary>
    public SyntaxToken? Modifier { get; }

    public SeparatedSyntaxList<IdentifierNameSyntax> Identifiers { get; }

    public SyntaxToken? ColonToken { get; }

    /// <summary>Any type syntax (<c>array of T</c> is an open array); the binder restricts it to paramType.</summary>
    public TypeSyntax? Type { get; }

    public SyntaxToken? EqualsToken { get; }

    public ExpressionSyntax? DefaultValue { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitParameter(this);
}
