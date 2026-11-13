namespace FreexComp.Compiler.Syntax;

/// <summary>A one-segment name: <see cref="IdentifierNameSyntax"/> or <see cref="GenericNameSyntax"/>.</summary>
public abstract class SimpleNameSyntax : NameSyntax
{
    internal SimpleNameSyntax(SyntaxKind kind)
        : base(kind)
    {
    }

    public abstract SyntaxToken Identifier { get; }
}

/// <summary><c>ident</c>.</summary>
public sealed class IdentifierNameSyntax : SimpleNameSyntax
{
    internal IdentifierNameSyntax(SyntaxToken identifier)
        : base(SyntaxKind.IdentifierName)
    {
        Identifier = Add(identifier);
    }

    public override SyntaxToken Identifier { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitIdentifierName(this);
}

/// <summary><c>ident &lt; type { , type } &gt;</c>: a generic type or method with type arguments (LANG-705, 710).</summary>
public sealed class GenericNameSyntax : SimpleNameSyntax
{
    internal GenericNameSyntax(SyntaxToken identifier, TypeArgumentListSyntax typeArgumentList)
        : base(SyntaxKind.GenericName)
    {
        Identifier = Add(identifier);
        TypeArgumentList = Add(typeArgumentList);
    }

    public override SyntaxToken Identifier { get; }

    public TypeArgumentListSyntax TypeArgumentList { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitGenericName(this);
}

/// <summary><c>Left . Right</c>: a dotted name such as a unit name <c>System.SysUtils</c> (LANG-152) or a nested type.</summary>
public sealed class QualifiedNameSyntax : NameSyntax
{
    internal QualifiedNameSyntax(NameSyntax left, SyntaxToken dotToken, SimpleNameSyntax right)
        : base(SyntaxKind.QualifiedName)
    {
        Left = Add(left);
        DotToken = Add(dotToken);
        Right = Add(right);
    }

    public NameSyntax Left { get; }

    public SyntaxToken DotToken { get; }

    public SimpleNameSyntax Right { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitQualifiedName(this);
}
