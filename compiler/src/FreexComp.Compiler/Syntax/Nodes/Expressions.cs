namespace FreexComp.Compiler.Syntax;

// grammar.md §7. C-013 needs constant expressions; designators, calls, set constructors etc. follow with C-016.

/// <summary>An integer, real, string/char literal or <c>nil</c>.</summary>
public sealed class LiteralExpressionSyntax : ExpressionSyntax
{
    internal LiteralExpressionSyntax(SyntaxToken token)
        : base(SyntaxKind.LiteralExpression)
    {
        Token = Add(token);
    }

    public SyntaxToken Token { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitLiteralExpression(this);
}

/// <summary><c>Left op Right</c> for the relational, additive and multiplicative operators (LANG-170).</summary>
public sealed class BinaryExpressionSyntax : ExpressionSyntax
{
    internal BinaryExpressionSyntax(ExpressionSyntax left, SyntaxToken operatorToken, ExpressionSyntax right)
        : base(SyntaxKind.BinaryExpression)
    {
        Left = Add(left);
        OperatorToken = Add(operatorToken);
        Right = Add(right);
    }

    public ExpressionSyntax Left { get; }

    public SyntaxToken OperatorToken { get; }

    public ExpressionSyntax Right { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitBinaryExpression(this);
}

/// <summary><c>not</c>, <c>+</c> or <c>-</c> applied to an operand (a leading sign covers the whole first term, LANG-171).</summary>
public sealed class UnaryExpressionSyntax : ExpressionSyntax
{
    internal UnaryExpressionSyntax(SyntaxToken operatorToken, ExpressionSyntax operand)
        : base(SyntaxKind.UnaryExpression)
    {
        OperatorToken = Add(operatorToken);
        Operand = Add(operand);
    }

    public SyntaxToken OperatorToken { get; }

    public ExpressionSyntax Operand { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitUnaryExpression(this);
}

/// <summary><c>( expression )</c>; in a typed constant also a one-element array aggregate (the binder decides).</summary>
public sealed class ParenthesizedExpressionSyntax : ExpressionSyntax
{
    internal ParenthesizedExpressionSyntax(SyntaxToken openParenToken, ExpressionSyntax expression, SyntaxToken closeParenToken)
        : base(SyntaxKind.ParenthesizedExpression)
    {
        OpenParenToken = Add(openParenToken);
        Expression = Add(expression);
        CloseParenToken = Add(closeParenToken);
    }

    public SyntaxToken OpenParenToken { get; }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken CloseParenToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitParenthesizedExpression(this);
}
