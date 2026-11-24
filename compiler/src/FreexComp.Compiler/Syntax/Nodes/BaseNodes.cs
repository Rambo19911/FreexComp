namespace FreexComp.Compiler.Syntax;

// Abstract categories of api.md §4.1. Constructors are internal: only the parser creates nodes.

/// <summary><c>ProgramSyntax</c>, <c>LibrarySyntax</c>, <c>UnitSyntax</c> or <c>IncludeFileSyntax</c>: the root of a tree.</summary>
public abstract class CompilationUnitSyntax : SyntaxNode
{
    internal CompilationUnitSyntax(SyntaxKind kind)
        : base(kind)
    {
    }
}

/// <summary>Base of every expression node (grammar.md §7).</summary>
public abstract class ExpressionSyntax : SyntaxNode
{
    internal ExpressionSyntax(SyntaxKind kind)
        : base(kind)
    {
    }
}

/// <summary>Identifier, qualified (dotted) or generic name.</summary>
public abstract class NameSyntax : ExpressionSyntax
{
    internal NameSyntax(SyntaxKind kind)
        : base(kind)
    {
    }
}

/// <summary>Base of every statement node (grammar.md §6).</summary>
public abstract class StatementSyntax : SyntaxNode
{
    internal StatementSyntax(SyntaxKind kind)
        : base(kind)
    {
    }
}

/// <summary>Base of every type-syntax node (grammar.md §3).</summary>
public abstract class TypeSyntax : SyntaxNode
{
    internal TypeSyntax(SyntaxKind kind)
        : base(kind)
    {
    }
}

/// <summary>Base of fields, methods, properties and class-var sections of structured types (grammar.md §4).</summary>
public abstract class MemberDeclarationSyntax : SyntaxNode
{
    internal MemberDeclarationSyntax(SyntaxKind kind)
        : base(kind)
    {
    }
}
