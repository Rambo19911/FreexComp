namespace FreexComp.Compiler.Syntax;

// grammar.md §6 (C-017). CompoundStatementSyntax and EmptyStatementSyntax are in Blocks.cs.

/// <summary><c>designator := expression</c>.</summary>
public sealed class AssignmentStatementSyntax : StatementSyntax
{
    internal AssignmentStatementSyntax(ExpressionSyntax target, SyntaxToken colonEqualsToken, ExpressionSyntax value)
        : base(SyntaxKind.AssignmentStatement)
    {
        Target = Add(target);
        ColonEqualsToken = Add(colonEqualsToken);
        Value = Add(value);
    }

    public ExpressionSyntax Target { get; }

    public SyntaxToken ColonEqualsToken { get; }

    public ExpressionSyntax Value { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitAssignmentStatement(this);
}

/// <summary>A designator used as a statement: a procedure, function or method call (LANG-307, 352).</summary>
public sealed class ExpressionStatementSyntax : StatementSyntax
{
    internal ExpressionStatementSyntax(ExpressionSyntax expression)
        : base(SyntaxKind.ExpressionStatement)
    {
        Expression = Add(expression);
    }

    public ExpressionSyntax Expression { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitExpressionStatement(this);
}

/// <summary>
/// <c>inherited [ ident [ ( [ argList ] ) ] ]</c>: <see cref="Expression"/> is an <see cref="InheritedExpressionSyntax"/>
/// or an <see cref="InvocationExpressionSyntax"/> of one. Other designators starting with <c>inherited</c> (such as
/// <c>inherited Foo.Bar</c>) are expression statements.
/// </summary>
public sealed class InheritedStatementSyntax : StatementSyntax
{
    internal InheritedStatementSyntax(ExpressionSyntax expression)
        : base(SyntaxKind.InheritedStatement)
    {
        Expression = Add(expression);
    }

    public ExpressionSyntax Expression { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitInheritedStatement(this);
}

/// <summary><c>goto labelId</c>.</summary>
public sealed class GotoStatementSyntax : StatementSyntax
{
    internal GotoStatementSyntax(SyntaxToken gotoKeyword, ExpressionSyntax label)
        : base(SyntaxKind.GotoStatement)
    {
        GotoKeyword = Add(gotoKeyword);
        Label = Add(label);
    }

    public SyntaxToken GotoKeyword { get; }

    /// <summary>An <see cref="IdentifierNameSyntax"/> or an integer <see cref="LiteralExpressionSyntax"/>.</summary>
    public ExpressionSyntax Label { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitGotoStatement(this);
}

/// <summary><c>labelId : [ statement ]</c>.</summary>
public sealed class LabeledStatementSyntax : StatementSyntax
{
    internal LabeledStatementSyntax(ExpressionSyntax label, SyntaxToken colonToken, StatementSyntax statement)
        : base(SyntaxKind.LabeledStatement)
    {
        Label = Add(label);
        ColonToken = Add(colonToken);
        Statement = Add(statement);
    }

    /// <summary>An <see cref="IdentifierNameSyntax"/> or an integer <see cref="LiteralExpressionSyntax"/>.</summary>
    public ExpressionSyntax Label { get; }

    public SyntaxToken ColonToken { get; }

    /// <summary>The labelled statement; an <see cref="EmptyStatementSyntax"/> when none follows.</summary>
    public StatementSyntax Statement { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitLabeledStatement(this);
}

/// <summary><c>raise [ expression [ at expression ] ]</c> (LANG-167).</summary>
public sealed class RaiseStatementSyntax : StatementSyntax
{
    internal RaiseStatementSyntax(SyntaxToken raiseKeyword, ExpressionSyntax? exception, SyntaxToken? atKeyword, ExpressionSyntax? address)
        : base(SyntaxKind.RaiseStatement)
    {
        RaiseKeyword = Add(raiseKeyword);
        Exception = AddOptional(exception);
        AtKeyword = Add(atKeyword);
        Address = AddOptional(address);
    }

    public SyntaxToken RaiseKeyword { get; }

    /// <summary>The raised object; null for a re-raise.</summary>
    public ExpressionSyntax? Exception { get; }

    /// <summary>An identifier token whose <see cref="SyntaxToken.ContextualKind"/> is <c>AtKeyword</c>.</summary>
    public SyntaxToken? AtKeyword { get; }

    public ExpressionSyntax? Address { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitRaiseStatement(this);
}

/// <summary><c>if expression then statement [ else statement ]</c> (LANG-165).</summary>
public sealed class IfStatementSyntax : StatementSyntax
{
    internal IfStatementSyntax(SyntaxToken ifKeyword, ExpressionSyntax condition, SyntaxToken thenKeyword, StatementSyntax statement,
        SyntaxToken? elseKeyword, StatementSyntax? elseStatement)
        : base(SyntaxKind.IfStatement)
    {
        IfKeyword = Add(ifKeyword);
        Condition = Add(condition);
        ThenKeyword = Add(thenKeyword);
        Statement = Add(statement);
        ElseKeyword = Add(elseKeyword);
        ElseStatement = AddOptional(elseStatement);
    }

    public SyntaxToken IfKeyword { get; }

    public ExpressionSyntax Condition { get; }

    public SyntaxToken ThenKeyword { get; }

    public StatementSyntax Statement { get; }

    public SyntaxToken? ElseKeyword { get; }

    public StatementSyntax? ElseStatement { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitIfStatement(this);
}

/// <summary><c>case expression of caseArm { ; caseArm } [ ; ] [ else stmtList ] end</c>.</summary>
public sealed class CaseStatementSyntax : StatementSyntax
{
    internal CaseStatementSyntax(SyntaxToken caseKeyword, ExpressionSyntax selector, SyntaxToken ofKeyword,
        SeparatedSyntaxList<CaseArmSyntax> arms, SyntaxToken? elseKeyword, SeparatedSyntaxList<StatementSyntax> elseStatements,
        SyntaxToken endKeyword)
        : base(SyntaxKind.CaseStatement)
    {
        CaseKeyword = Add(caseKeyword);
        Selector = Add(selector);
        OfKeyword = Add(ofKeyword);
        Arms = Add(arms);
        ElseKeyword = Add(elseKeyword);
        ElseStatements = Add(elseStatements);
        EndKeyword = Add(endKeyword);
    }

    public SyntaxToken CaseKeyword { get; }

    public ExpressionSyntax Selector { get; }

    public SyntaxToken OfKeyword { get; }

    /// <summary>The arms; a trailing <c>;</c> is kept as the last separator.</summary>
    public SeparatedSyntaxList<CaseArmSyntax> Arms { get; }

    public SyntaxToken? ElseKeyword { get; }

    public SeparatedSyntaxList<StatementSyntax> ElseStatements { get; }

    public SyntaxToken EndKeyword { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitCaseStatement(this);
}

/// <summary><c>caseLabel { , caseLabel } : statement</c>.</summary>
public sealed class CaseArmSyntax : SyntaxNode
{
    internal CaseArmSyntax(SeparatedSyntaxList<CaseLabelSyntax> labels, SyntaxToken colonToken, StatementSyntax statement)
        : base(SyntaxKind.CaseArm)
    {
        Labels = Add(labels);
        ColonToken = Add(colonToken);
        Statement = Add(statement);
    }

    public SeparatedSyntaxList<CaseLabelSyntax> Labels { get; }

    public SyntaxToken ColonToken { get; }

    public StatementSyntax Statement { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitCaseArm(this);
}

/// <summary><c>constExpr [ .. constExpr ]</c>.</summary>
public sealed class CaseLabelSyntax : SyntaxNode
{
    internal CaseLabelSyntax(ExpressionSyntax value, SyntaxToken? dotDotToken, ExpressionSyntax? upperBound)
        : base(SyntaxKind.CaseLabel)
    {
        Value = Add(value);
        DotDotToken = Add(dotDotToken);
        UpperBound = AddOptional(upperBound);
    }

    public ExpressionSyntax Value { get; }

    public SyntaxToken? DotDotToken { get; }

    public ExpressionSyntax? UpperBound { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitCaseLabel(this);
}

/// <summary><c>repeat stmtList until expression</c>.</summary>
public sealed class RepeatStatementSyntax : StatementSyntax
{
    internal RepeatStatementSyntax(SyntaxToken repeatKeyword, SeparatedSyntaxList<StatementSyntax> statements, SyntaxToken untilKeyword,
        ExpressionSyntax condition)
        : base(SyntaxKind.RepeatStatement)
    {
        RepeatKeyword = Add(repeatKeyword);
        Statements = Add(statements);
        UntilKeyword = Add(untilKeyword);
        Condition = Add(condition);
    }

    public SyntaxToken RepeatKeyword { get; }

    public SeparatedSyntaxList<StatementSyntax> Statements { get; }

    public SyntaxToken UntilKeyword { get; }

    public ExpressionSyntax Condition { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitRepeatStatement(this);
}

/// <summary><c>while expression do statement</c>.</summary>
public sealed class WhileStatementSyntax : StatementSyntax
{
    internal WhileStatementSyntax(SyntaxToken whileKeyword, ExpressionSyntax condition, SyntaxToken doKeyword, StatementSyntax statement)
        : base(SyntaxKind.WhileStatement)
    {
        WhileKeyword = Add(whileKeyword);
        Condition = Add(condition);
        DoKeyword = Add(doKeyword);
        Statement = Add(statement);
    }

    public SyntaxToken WhileKeyword { get; }

    public ExpressionSyntax Condition { get; }

    public SyntaxToken DoKeyword { get; }

    public StatementSyntax Statement { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitWhileStatement(this);
}

/// <summary><c>for forVar := expression ( to | downto ) expression do statement</c>; <c>forVar = ident | var ident [ : type ]</c>.</summary>
public sealed class ForStatementSyntax : StatementSyntax
{
    internal ForStatementSyntax(SyntaxToken forKeyword, SyntaxToken? varKeyword, IdentifierNameSyntax variable, SyntaxToken? colonToken,
        TypeSyntax? type, SyntaxToken colonEqualsToken, ExpressionSyntax start, SyntaxToken directionKeyword, ExpressionSyntax finish,
        SyntaxToken doKeyword, StatementSyntax statement)
        : base(SyntaxKind.ForStatement)
    {
        ForKeyword = Add(forKeyword);
        VarKeyword = Add(varKeyword);
        Variable = Add(variable);
        ColonToken = Add(colonToken);
        Type = AddOptional(type);
        ColonEqualsToken = Add(colonEqualsToken);
        Start = Add(start);
        DirectionKeyword = Add(directionKeyword);
        Finish = Add(finish);
        DoKeyword = Add(doKeyword);
        Statement = Add(statement);
    }

    public SyntaxToken ForKeyword { get; }

    /// <summary><c>var</c> of an inline loop variable (M2.6).</summary>
    public SyntaxToken? VarKeyword { get; }

    public IdentifierNameSyntax Variable { get; }

    public SyntaxToken? ColonToken { get; }

    public TypeSyntax? Type { get; }

    public SyntaxToken ColonEqualsToken { get; }

    public ExpressionSyntax Start { get; }

    /// <summary><c>to</c> or <c>downto</c>.</summary>
    public SyntaxToken DirectionKeyword { get; }

    public ExpressionSyntax Finish { get; }

    public SyntaxToken DoKeyword { get; }

    public StatementSyntax Statement { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitForStatement(this);
}

/// <summary><c>for forVar in expression do statement</c> (M2.5/M2.6).</summary>
public sealed class ForInStatementSyntax : StatementSyntax
{
    internal ForInStatementSyntax(SyntaxToken forKeyword, SyntaxToken? varKeyword, IdentifierNameSyntax variable, SyntaxToken? colonToken,
        TypeSyntax? type, SyntaxToken inKeyword, ExpressionSyntax collection, SyntaxToken doKeyword, StatementSyntax statement)
        : base(SyntaxKind.ForInStatement)
    {
        ForKeyword = Add(forKeyword);
        VarKeyword = Add(varKeyword);
        Variable = Add(variable);
        ColonToken = Add(colonToken);
        Type = AddOptional(type);
        InKeyword = Add(inKeyword);
        Collection = Add(collection);
        DoKeyword = Add(doKeyword);
        Statement = Add(statement);
    }

    public SyntaxToken ForKeyword { get; }

    public SyntaxToken? VarKeyword { get; }

    public IdentifierNameSyntax Variable { get; }

    public SyntaxToken? ColonToken { get; }

    public TypeSyntax? Type { get; }

    public SyntaxToken InKeyword { get; }

    public ExpressionSyntax Collection { get; }

    public SyntaxToken DoKeyword { get; }

    public StatementSyntax Statement { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitForInStatement(this);
}

/// <summary><c>with designator { , designator } do statement</c>.</summary>
public sealed class WithStatementSyntax : StatementSyntax
{
    internal WithStatementSyntax(SyntaxToken withKeyword, SeparatedSyntaxList<ExpressionSyntax> expressions, SyntaxToken doKeyword,
        StatementSyntax statement)
        : base(SyntaxKind.WithStatement)
    {
        WithKeyword = Add(withKeyword);
        Expressions = Add(expressions);
        DoKeyword = Add(doKeyword);
        Statement = Add(statement);
    }

    public SyntaxToken WithKeyword { get; }

    public SeparatedSyntaxList<ExpressionSyntax> Expressions { get; }

    public SyntaxToken DoKeyword { get; }

    public StatementSyntax Statement { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitWithStatement(this);
}

/// <summary><c>try stmtList finally stmtList end</c>.</summary>
public sealed class TryFinallyStatementSyntax : StatementSyntax
{
    internal TryFinallyStatementSyntax(SyntaxToken tryKeyword, SeparatedSyntaxList<StatementSyntax> statements, SyntaxToken finallyKeyword,
        SeparatedSyntaxList<StatementSyntax> finallyStatements, SyntaxToken endKeyword)
        : base(SyntaxKind.TryFinallyStatement)
    {
        TryKeyword = Add(tryKeyword);
        Statements = Add(statements);
        FinallyKeyword = Add(finallyKeyword);
        FinallyStatements = Add(finallyStatements);
        EndKeyword = Add(endKeyword);
    }

    public SyntaxToken TryKeyword { get; }

    public SeparatedSyntaxList<StatementSyntax> Statements { get; }

    public SyntaxToken FinallyKeyword { get; }

    public SeparatedSyntaxList<StatementSyntax> FinallyStatements { get; }

    public SyntaxToken EndKeyword { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitTryFinallyStatement(this);
}

/// <summary>
/// <c>try stmtList except exceptBlock end</c> (LANG-166). Handler form: <see cref="Handlers"/> and, after
/// <c>else</c>, <see cref="ExceptStatements"/>. Plain form: no handlers, no <c>else</c>, the block is
/// <see cref="ExceptStatements"/>.
/// </summary>
public sealed class TryExceptStatementSyntax : StatementSyntax
{
    internal TryExceptStatementSyntax(SyntaxToken tryKeyword, SeparatedSyntaxList<StatementSyntax> statements, SyntaxToken exceptKeyword,
        SeparatedSyntaxList<ExceptionHandlerSyntax> handlers, SyntaxToken? elseKeyword, SeparatedSyntaxList<StatementSyntax> exceptStatements,
        SyntaxToken endKeyword)
        : base(SyntaxKind.TryExceptStatement)
    {
        TryKeyword = Add(tryKeyword);
        Statements = Add(statements);
        ExceptKeyword = Add(exceptKeyword);
        Handlers = Add(handlers);
        ElseKeyword = Add(elseKeyword);
        ExceptStatements = Add(exceptStatements);
        EndKeyword = Add(endKeyword);
    }

    public SyntaxToken TryKeyword { get; }

    public SeparatedSyntaxList<StatementSyntax> Statements { get; }

    public SyntaxToken ExceptKeyword { get; }

    /// <summary>The <c>on</c> handlers; a trailing <c>;</c> is kept as the last separator.</summary>
    public SeparatedSyntaxList<ExceptionHandlerSyntax> Handlers { get; }

    public SyntaxToken? ElseKeyword { get; }

    /// <summary>The statements after <c>else</c> (handler form) or the whole except block (plain form).</summary>
    public SeparatedSyntaxList<StatementSyntax> ExceptStatements { get; }

    public SyntaxToken EndKeyword { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitTryExceptStatement(this);
}

/// <summary><c>on [ ident : ] typeId do statement</c>.</summary>
public sealed class ExceptionHandlerSyntax : SyntaxNode
{
    internal ExceptionHandlerSyntax(SyntaxToken onKeyword, SyntaxToken? identifier, SyntaxToken? colonToken, NamedTypeSyntax type,
        SyntaxToken doKeyword, StatementSyntax statement)
        : base(SyntaxKind.ExceptionHandler)
    {
        OnKeyword = Add(onKeyword);
        Identifier = Add(identifier);
        ColonToken = Add(colonToken);
        Type = Add(type);
        DoKeyword = Add(doKeyword);
        Statement = Add(statement);
    }

    /// <summary>An identifier token whose <see cref="SyntaxToken.ContextualKind"/> is <c>OnKeyword</c>.</summary>
    public SyntaxToken OnKeyword { get; }

    public SyntaxToken? Identifier { get; }

    public SyntaxToken? ColonToken { get; }

    public NamedTypeSyntax Type { get; }

    public SyntaxToken DoKeyword { get; }

    public StatementSyntax Statement { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitExceptionHandler(this);
}

/// <summary><c>var identList [ : type ] [ := expression ]</c> (M2.6; which combinations are allowed is the binder's).</summary>
public sealed class InlineVarStatementSyntax : StatementSyntax
{
    internal InlineVarStatementSyntax(SyntaxToken varKeyword, SeparatedSyntaxList<IdentifierNameSyntax> identifiers, SyntaxToken? colonToken,
        TypeSyntax? type, SyntaxToken? colonEqualsToken, ExpressionSyntax? initializer)
        : base(SyntaxKind.InlineVarStatement)
    {
        VarKeyword = Add(varKeyword);
        Identifiers = Add(identifiers);
        ColonToken = Add(colonToken);
        Type = AddOptional(type);
        ColonEqualsToken = Add(colonEqualsToken);
        Initializer = AddOptional(initializer);
    }

    public SyntaxToken VarKeyword { get; }

    public SeparatedSyntaxList<IdentifierNameSyntax> Identifiers { get; }

    public SyntaxToken? ColonToken { get; }

    public TypeSyntax? Type { get; }

    public SyntaxToken? ColonEqualsToken { get; }

    public ExpressionSyntax? Initializer { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitInlineVarStatement(this);
}

/// <summary><c>const ident [ : type ] = constExpr</c> (M2.6).</summary>
public sealed class InlineConstStatementSyntax : StatementSyntax
{
    internal InlineConstStatementSyntax(SyntaxToken constKeyword, SyntaxToken identifier, SyntaxToken? colonToken, TypeSyntax? type,
        SyntaxToken equalsToken, ExpressionSyntax value)
        : base(SyntaxKind.InlineConstStatement)
    {
        ConstKeyword = Add(constKeyword);
        Identifier = Add(identifier);
        ColonToken = Add(colonToken);
        Type = AddOptional(type);
        EqualsToken = Add(equalsToken);
        Value = Add(value);
    }

    public SyntaxToken ConstKeyword { get; }

    public SyntaxToken Identifier { get; }

    public SyntaxToken? ColonToken { get; }

    public TypeSyntax? Type { get; }

    public SyntaxToken EqualsToken { get; }

    public ExpressionSyntax Value { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitInlineConstStatement(this);
}
