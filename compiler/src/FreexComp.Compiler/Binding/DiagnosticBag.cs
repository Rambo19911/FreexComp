using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Binding;

/// <summary>Collects binder diagnostics; locations use the file each token was read from (includes, LANG-876).</summary>
internal sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly HashSet<string> _undeclared = new(IdentifierComparer.Instance);

    public int Count => _diagnostics.Count;

    public void Report(SyntaxToken token, string code, params object[] args) =>
        _diagnostics.Add(Diagnostic.Create(code, LocationOf(token), args));

    public void Report(SyntaxNode node, string code, params object[] args) =>
        _diagnostics.Add(Diagnostic.Create(code, LocationOf(node), args));

    /// <summary>E2003, at most once per distinct name (CMP-507; the unit level counts as one routine).</summary>
    public void ReportUndeclared(SyntaxNode node, string name)
    {
        if (_undeclared.Add(name))
        {
            Report(node, "E2003", name);
        }
    }

    /// <summary>E2004 with the previous declaration as related location (CMP-508).</summary>
    public void ReportRedeclared(SyntaxToken identifier, string name, Symbols.Symbol previous)
    {
        var diagnostic = Diagnostic.Create("E2004", LocationOf(identifier), name);
        _diagnostics.Add(previous.Locations.IsEmpty
            ? diagnostic
            : diagnostic with { Related = [new RelatedLocation(previous.Locations[0], "previous declaration")] });
    }

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
