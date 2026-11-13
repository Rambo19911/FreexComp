using System.Collections.Immutable;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Syntax;

/// <summary>A parsed file: root node, source text and its lexical, directive and syntax diagnostics (api.md §4).</summary>
public sealed class SyntaxTree
{
    internal SyntaxTree(SourceText text, CompilationUnitSyntax root, ImmutableArray<Diagnostic> diagnostics,
                        ImmutableArray<SyntaxTree> includedTrees)
    {
        Text = text;
        Root = root;
        Diagnostics = diagnostics.IsDefault ? [] : diagnostics;
        IncludedTrees = includedTrees.IsDefault ? [] : includedTrees;
        root.AttachTree(this);
    }

    public SourceText Text { get; }

    public CompilationUnitSyntax Root { get; }

    /// <summary>Lexical, directive and syntax diagnostics (codes E10xx–E12xx).</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public ImmutableArray<SyntaxTree> IncludedTrees { get; }

    /// <summary>
    /// The token whose full span (trivia included) contains <paramref name="position"/>: the token itself, or the
    /// token owning the trivia there. At or after the end of the text: the EndOfFileToken.
    /// </summary>
    public SyntaxToken FindToken(int position) => Root.FindToken(position);

    /// <summary>The innermost node whose full span contains <paramref name="span"/>; the root if no child does.</summary>
    public SyntaxNode FindNode(TextSpan span) => Root.FindNode(span);

    public IEnumerable<SyntaxTrivia> DescendantTrivia()
    {
        foreach (var token in Root.DescendantTokens())
        {
            foreach (var trivia in token.LeadingTrivia)
            {
                yield return trivia;
            }

            foreach (var trivia in token.TrailingTrivia)
            {
                yield return trivia;
            }
        }
    }

    /// <summary>The active directives (each its own trivia piece) in source order, include directives included.</summary>
    public IEnumerable<DirectiveTriviaInfo> Directives =>
        DescendantTrivia().Where(t => t.Directive is not null).Select(t => t.Directive!);

    /// <summary>Spans of the text of inactive conditional branches.</summary>
    public IEnumerable<TextSpan> DisabledRegions =>
        DescendantTrivia().Where(t => t.Kind == SyntaxKind.DisabledTextTrivia).Select(t => t.Span);
}
