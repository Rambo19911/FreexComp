namespace FreexComp.Compiler.Syntax;

/// <summary><c>{ declSection } compoundStmt</c> (grammar.md §2).</summary>
public sealed class BlockSyntax : SyntaxNode
{
    internal BlockSyntax(SyntaxList<SyntaxNode> declarations, CompoundStatementSyntax body)
        : base(SyntaxKind.Block)
    {
        Declarations = Add(declarations);
        Body = Add(body);
    }

    /// <summary>Declaration sections, routines and attribute lists in source order.</summary>
    public SyntaxList<SyntaxNode> Declarations { get; }

    public CompoundStatementSyntax Body { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitBlock(this);
}

/// <summary><c>begin stmtList end</c>; <c>stmtList = [ statement ] { ; [ statement ] }</c> (grammar.md §6).</summary>
public sealed class CompoundStatementSyntax : StatementSyntax
{
    internal CompoundStatementSyntax(SyntaxToken beginKeyword, SeparatedSyntaxList<StatementSyntax> statements, SyntaxToken endKeyword)
        : base(SyntaxKind.CompoundStatement)
    {
        BeginKeyword = Add(beginKeyword);
        Statements = Add(statements);
        EndKeyword = Add(endKeyword);
    }

    public SyntaxToken BeginKeyword { get; }

    /// <summary>
    /// Statements separated by <c>;</c>. An absent statement next to a <c>;</c> is an <see cref="EmptyStatementSyntax"/>;
    /// an empty list (<c>begin end</c>) has no element.
    /// </summary>
    public SeparatedSyntaxList<StatementSyntax> Statements { get; }

    public SyntaxToken EndKeyword { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitCompoundStatement(this);
}

/// <summary>The absent statement of <c>stmtList</c> next to a <c>;</c>: no tokens, zero width.</summary>
public sealed class EmptyStatementSyntax : StatementSyntax
{
    internal EmptyStatementSyntax()
        : base(SyntaxKind.EmptyStatement)
    {
    }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitEmptyStatement(this);
}

/// <summary><c>deprecated [ stringLiteral ] | platform | experimental</c> (grammar.md §2).</summary>
public sealed class HintDirectiveSyntax : SyntaxNode
{
    internal HintDirectiveSyntax(SyntaxToken keyword, SyntaxToken? message)
        : base(SyntaxKind.HintDirective)
    {
        Keyword = Add(keyword);
        Message = Add(message);
    }

    /// <summary>An identifier token whose <see cref="SyntaxToken.ContextualKind"/> is the directive keyword.</summary>
    public SyntaxToken Keyword { get; }

    /// <summary>The string literal after <c>deprecated</c>.</summary>
    public SyntaxToken? Message { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitHintDirective(this);
}
