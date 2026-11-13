namespace FreexComp.Compiler.Syntax;

/// <summary><c>ident</c>.</summary>
public sealed class IdentifierNameSyntax : NameSyntax
{
    internal IdentifierNameSyntax(SyntaxToken identifier)
        : base(SyntaxKind.IdentifierName)
    {
        Identifier = Add(identifier);
    }

    public SyntaxToken Identifier { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitIdentifierName(this);
}

/// <summary><c>Left . Right</c>: a dotted name such as a unit name <c>System.SysUtils</c> (LANG-152).</summary>
public sealed class QualifiedNameSyntax : NameSyntax
{
    internal QualifiedNameSyntax(NameSyntax left, SyntaxToken dotToken, IdentifierNameSyntax right)
        : base(SyntaxKind.QualifiedName)
    {
        Left = Add(left);
        DotToken = Add(dotToken);
        Right = Add(right);
    }

    public NameSyntax Left { get; }

    public SyntaxToken DotToken { get; }

    public IdentifierNameSyntax Right { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitQualifiedName(this);
}
