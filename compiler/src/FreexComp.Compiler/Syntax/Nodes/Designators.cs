namespace FreexComp.Compiler.Syntax;

// grammar.md §7: designators, calls, set constructors, '@', 'inherited', string casts (C-016). Anonymous methods and
// generic names follow with C-019.

/// <summary><c>Expression . Name</c>: field, method, property or (binder decides) unit-qualified access (LANG-311, 313).</summary>
public sealed class MemberAccessExpressionSyntax : ExpressionSyntax
{
    internal MemberAccessExpressionSyntax(ExpressionSyntax expression, SyntaxToken dotToken, SimpleNameSyntax name)
        : base(SyntaxKind.MemberAccessExpression)
    {
        Expression = Add(expression);
        DotToken = Add(dotToken);
        Name = Add(name);
    }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken DotToken { get; }

    public SimpleNameSyntax Name { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitMemberAccessExpression(this);
}

/// <summary><c>Expression [ index { , index } ]</c>: array element, string character or array property.</summary>
public sealed class ElementAccessExpressionSyntax : ExpressionSyntax
{
    internal ElementAccessExpressionSyntax(ExpressionSyntax expression, SyntaxToken openBracketToken,
        SeparatedSyntaxList<ExpressionSyntax> indexes, SyntaxToken closeBracketToken)
        : base(SyntaxKind.ElementAccessExpression)
    {
        Expression = Add(expression);
        OpenBracketToken = Add(openBracketToken);
        Indexes = Add(indexes);
        CloseBracketToken = Add(closeBracketToken);
    }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken OpenBracketToken { get; }

    public SeparatedSyntaxList<ExpressionSyntax> Indexes { get; }

    public SyntaxToken CloseBracketToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitElementAccessExpression(this);
}

/// <summary><c>Expression ^</c>.</summary>
public sealed class DereferenceExpressionSyntax : ExpressionSyntax
{
    internal DereferenceExpressionSyntax(ExpressionSyntax expression, SyntaxToken caretToken)
        : base(SyntaxKind.DereferenceExpression)
    {
        Expression = Add(expression);
        CaretToken = Add(caretToken);
    }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken CaretToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitDereferenceExpression(this);
}

/// <summary><c>Expression ( [ argList ] )</c>: a call, or a typecast when the target is a type (the binder decides, LANG-314).</summary>
public sealed class InvocationExpressionSyntax : ExpressionSyntax
{
    internal InvocationExpressionSyntax(ExpressionSyntax expression, ArgumentListSyntax argumentList)
        : base(SyntaxKind.InvocationExpression)
    {
        Expression = Add(expression);
        ArgumentList = Add(argumentList);
    }

    public ExpressionSyntax Expression { get; }

    public ArgumentListSyntax ArgumentList { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitInvocationExpression(this);
}

/// <summary><c>( [ argument { , argument } ] )</c> of a call.</summary>
public sealed class ArgumentListSyntax : SyntaxNode
{
    internal ArgumentListSyntax(SyntaxToken openParenToken, SeparatedSyntaxList<ArgumentSyntax> arguments, SyntaxToken closeParenToken)
        : base(SyntaxKind.ArgumentList)
    {
        OpenParenToken = Add(openParenToken);
        Arguments = Add(arguments);
        CloseParenToken = Add(closeParenToken);
    }

    public SyntaxToken OpenParenToken { get; }

    /// <summary>The arguments; empty for <c>()</c>.</summary>
    public SeparatedSyntaxList<ArgumentSyntax> Arguments { get; }

    public SyntaxToken CloseParenToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitArgumentList(this);
}

/// <summary><c>expression [ : width [ : decimals ] ]</c>; width and decimals are valid only for Write/Str (binder).</summary>
public sealed class ArgumentSyntax : SyntaxNode
{
    internal ArgumentSyntax(ExpressionSyntax expression, SyntaxToken? widthColonToken, ExpressionSyntax? width,
        SyntaxToken? decimalsColonToken, ExpressionSyntax? decimals)
        : base(SyntaxKind.Argument)
    {
        Expression = Add(expression);
        WidthColonToken = Add(widthColonToken);
        Width = AddOptional(width);
        DecimalsColonToken = Add(decimalsColonToken);
        Decimals = AddOptional(decimals);
    }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken? WidthColonToken { get; }

    public ExpressionSyntax? Width { get; }

    public SyntaxToken? DecimalsColonToken { get; }

    public ExpressionSyntax? Decimals { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitArgument(this);
}

/// <summary><c>@ Operand</c>.</summary>
public sealed class AddressOfExpressionSyntax : ExpressionSyntax
{
    internal AddressOfExpressionSyntax(SyntaxToken atToken, ExpressionSyntax operand)
        : base(SyntaxKind.AddressOfExpression)
    {
        AtToken = Add(atToken);
        Operand = Add(operand);
    }

    public SyntaxToken AtToken { get; }

    public ExpressionSyntax Operand { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitAddressOfExpression(this);
}

/// <summary><c>[ [ setElement { , setElement } ] ]</c>: a set constructor, also an open-array constructor (binder).</summary>
public sealed class SetConstructorSyntax : ExpressionSyntax
{
    internal SetConstructorSyntax(SyntaxToken openBracketToken, SeparatedSyntaxList<SetElementSyntax> elements, SyntaxToken closeBracketToken)
        : base(SyntaxKind.SetConstructor)
    {
        OpenBracketToken = Add(openBracketToken);
        Elements = Add(elements);
        CloseBracketToken = Add(closeBracketToken);
    }

    public SyntaxToken OpenBracketToken { get; }

    /// <summary>The elements; empty for <c>[]</c>.</summary>
    public SeparatedSyntaxList<SetElementSyntax> Elements { get; }

    public SyntaxToken CloseBracketToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitSetConstructor(this);
}

/// <summary><c>expression [ .. expression ]</c> inside a set constructor.</summary>
public sealed class SetElementSyntax : SyntaxNode
{
    internal SetElementSyntax(ExpressionSyntax expression, SyntaxToken? dotDotToken, ExpressionSyntax? upperBound)
        : base(SyntaxKind.SetElement)
    {
        Expression = Add(expression);
        DotDotToken = Add(dotDotToken);
        UpperBound = AddOptional(upperBound);
    }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken? DotDotToken { get; }

    public ExpressionSyntax? UpperBound { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitSetElement(this);
}

/// <summary>
/// <c>inherited [ ident ]</c>; arguments and further suffixes wrap it (<c>inherited Create(1)</c> is an
/// <see cref="InvocationExpressionSyntax"/> of this node).
/// </summary>
public sealed class InheritedExpressionSyntax : ExpressionSyntax
{
    internal InheritedExpressionSyntax(SyntaxToken inheritedKeyword, IdentifierNameSyntax? name)
        : base(SyntaxKind.InheritedExpression)
    {
        InheritedKeyword = Add(inheritedKeyword);
        Name = AddOptional(name);
    }

    public SyntaxToken InheritedKeyword { get; }

    public IdentifierNameSyntax? Name { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitInheritedExpression(this);
}

/// <summary>A bare <c>string</c> keyword in an expression, e.g. <c>SizeOf(string)</c> (the binder restricts where it is allowed).</summary>
public sealed class PredefinedTypeExpressionSyntax : ExpressionSyntax
{
    internal PredefinedTypeExpressionSyntax(SyntaxToken keyword)
        : base(SyntaxKind.PredefinedTypeExpression)
    {
        Keyword = Add(keyword);
    }

    public SyntaxToken Keyword { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitPredefinedTypeExpression(this);
}

/// <summary><c>string ( expression )</c>: a typecast to <c>string</c>; suffixes wrap it.</summary>
public sealed class StringCastExpressionSyntax : ExpressionSyntax
{
    internal StringCastExpressionSyntax(SyntaxToken stringKeyword, SyntaxToken openParenToken, ExpressionSyntax expression,
        SyntaxToken closeParenToken)
        : base(SyntaxKind.StringCastExpression)
    {
        StringKeyword = Add(stringKeyword);
        OpenParenToken = Add(openParenToken);
        Expression = Add(expression);
        CloseParenToken = Add(closeParenToken);
    }

    public SyntaxToken StringKeyword { get; }

    public SyntaxToken OpenParenToken { get; }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken CloseParenToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitStringCastExpression(this);
}
