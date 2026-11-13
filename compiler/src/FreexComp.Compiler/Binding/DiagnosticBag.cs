using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Binding;

/// <summary>Collects binder diagnostics; locations use the file each token was read from (includes, LANG-876).</summary>
internal sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = [];

    public int Count => _diagnostics.Count;

    public void Report(SyntaxToken token, string code, params object[] args) =>
        _diagnostics.Add(Diagnostic.Create(code, LocationOf(token), args));

    public void Report(SyntaxNode node, string code, params object[] args) =>
        _diagnostics.Add(Diagnostic.Create(code, LocationOf(node), args));

    public ImmutableArray<Diagnostic> ToImmutable() => [.. _diagnostics];

    public static Location LocationOf(SyntaxToken token) => LocationOf(FileOf(token), token.Span);

    /// <summary>From the node's first to its last token; only the first token when the node spans several files.</summary>
    public static Location LocationOf(SyntaxNode node)
    {
        var first = node.FirstToken();
        var last = node.LastToken();
        var file = FileOf(first);
        return PathUtil.Comparer.Equals(file.Path, FileOf(last).Path)
            ? LocationOf(file, TextSpan.FromBounds(first.Span.Start, Math.Max(first.Span.Start, last.Span.End)))
            : LocationOf(first);
    }

    private static SourceText FileOf(SyntaxToken token) =>
        token.File ?? token.Parent?.SyntaxTree.Text ?? throw new InvalidOperationException("Token outside a syntax tree.");

    private static Location LocationOf(SourceText file, TextSpan span) =>
        new(file.Path, span, new LinePositionSpan(file.Lines.GetLinePosition(span.Start), file.Lines.GetLinePosition(span.End)));
}
