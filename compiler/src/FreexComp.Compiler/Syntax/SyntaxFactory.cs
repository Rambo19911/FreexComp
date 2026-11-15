namespace FreexComp.Compiler.Syntax;

/// <summary>Construction helpers for code generation and fragment parsing (api.md §4).</summary>
public static class SyntaxFactory
{
    /// <summary>The path of the source text of a fragment tree (diagnostics carry it; spans are fragment-relative).</summary>
    public const string FragmentPath = @"C:\fragment.pas";

    /// <summary>
    /// Parses <paramref name="text"/> as one fragment: an expression (<see cref="ExpressionSyntax"/> or a subclass), a
    /// statement, a type, a class member (<see cref="MemberDeclarationSyntax"/>), a routine declaration with body, a
    /// type / var / const declaration or a uses clause. Never throws for malformed text: the returned node's
    /// <c>SyntaxTree.Diagnostics</c> hold the lexical and syntax errors, and input left over after the fragment is one
    /// E1100 on the first surplus token (api.md §4.1). The node's parent is the tree's <see cref="FragmentSyntax"/> root.
    /// </summary>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is no fragment category, or the text parses to another node class.</exception>
    public static T ParseNode<T>(string text)
        where T : SyntaxNode
    {
        ArgumentNullException.ThrowIfNull(text);
        var tree = SyntaxTree.ParseFragment(SourceText.From(FragmentPath, text), typeof(T));
        var node = ((FragmentSyntax)tree.Root).Node;
        return node as T
            ?? throw new ArgumentException($"The text parses as {node.GetType().Name}, not as {typeof(T).Name}.", nameof(text));
    }
}
