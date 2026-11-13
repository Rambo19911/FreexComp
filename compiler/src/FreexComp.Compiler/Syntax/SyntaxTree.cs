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

    /// <summary>
    /// Parses a program or unit; never throws for malformed text (LANG-180). With a file system, <c>{$I file}</c> includes
    /// are read (defines and switches carry across them, their diagnostics are part of <see cref="Diagnostics"/>). The
    /// included tokens are ordinary children of the main tree at the directive's place, keeping their own file and
    /// positions; text, span and FindToken queries skip them, so <c>Root.ToFullString() == Text.Text</c> (CMP-220).
    /// Each include occurrence also becomes an <see cref="IncludedTrees"/> entry with a flat <see cref="IncludeFileSyntax"/>.
    /// </summary>
    public static SyntaxTree Parse(SourceText text, ParseOptions? options = null, IFileSystem? fs = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var preprocessor = new Preprocessor(text, options, fs);
        var tokens = ImmutableArray.CreateBuilder<SyntaxToken>();
        SyntaxToken token;
        do
        {
            token = preprocessor.Lex();
            tokens.Add(token);
        }
        while (token.Kind != SyntaxKind.EndOfFileToken);     // include files' end-of-file tokens are not in the stream

        var parser = new Parser(text, tokens.ToImmutable());
        var root = parser.ParseCompilationUnit();
        var lexical = preprocessor.Diagnostics;
        var diagnostics = lexical.Concat(parser.Diagnostics)
            .OrderBy(d => PathUtil.AreEqual(d.Location.Path, text.Path) ? 0 : 1)
            .ThenBy(d => d.Location.Span.Start)
            .ToImmutableArray();

        var included = preprocessor.Includes
            .Select(i => new SyntaxTree(
                i.Text,
                new IncludeFileSyntax(i.Tokens),
                [.. lexical.Where(d => PathUtil.AreEqual(d.Location.Path, i.Text.Path))],
                []))
            .ToImmutableArray();
        return new SyntaxTree(text, root, diagnostics, included);
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
        foreach (var token in Root.OwnTokens())
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
