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

    /// <summary>Adds an already made diagnostic (warning control was applied where it was reported).</summary>
    public void Add(Diagnostic diagnostic) => _diagnostics.Add(diagnostic);

    /// <summary>
    /// LANG-711 (C-118): an error found while instantiating <paramref name="instance"/> becomes E2710 at the same place; E2710 (of a
    /// nested instance) and E2712 pass unchanged. Warnings and hints of instances are dropped (the declaration reports nothing itself).
    /// </summary>
    public static Diagnostic? InInstance(Diagnostic diagnostic, string instance) =>
        diagnostic.Severity != DiagnosticSeverity.Error ? null
        : diagnostic.Code is "E2710" or "E2712" ? diagnostic
        : Diagnostic.Create("E2710", diagnostic.Location, instance, diagnostic.Message) with { Related = diagnostic.Related };

    public void Report(SyntaxToken token, string code, params object[] args) =>
        Add(token.DirectiveState, Diagnostic.Create(code, LocationOf(token), args));

    public void Report(SyntaxNode node, string code, params object[] args) =>
        Add(node.FirstToken().DirectiveState, Diagnostic.Create(code, LocationOf(node), args));

    /// <summary>A diagnostic with related locations (CMP-508), e.g. the tied candidates of E2122.</summary>
    public void Report(SyntaxNode node, string code, ImmutableArray<RelatedLocation> related, params object[] args) =>
        Add(node.FirstToken().DirectiveState, Diagnostic.Create(code, LocationOf(node), args) with { Related = related });

    private void Add(DirectiveState state, Diagnostic diagnostic)
    {
        if (ApplyWarningControl(diagnostic, state) is { } kept)
        {
            _diagnostics.Add(kept);
        }
    }

    /// <summary>
    /// CMP-505 (C-196): <c>{$WARN code …}</c>, then <c>{$WARNINGS}</c>/<c>{$HINTS}</c>, in force at the diagnostic's first
    /// token; null = suppressed. Errors pass unchanged.
    /// </summary>
    public static Diagnostic? ApplyWarningControl(Diagnostic diagnostic, DirectiveState state)
    {
        if (diagnostic.Severity == DiagnosticSeverity.Error)
        {
            return diagnostic;
        }

        if (state.WarnOverrides.TryGetValue(diagnostic.Code, out var setting))
        {
            return setting switch
            {
                WarnSetting.Off => null,
                WarnSetting.Error => diagnostic with { Severity = DiagnosticSeverity.Error },
                _ => diagnostic,
            };
        }

        return (diagnostic.Severity == DiagnosticSeverity.Warning && !state.WarningsOn) || (diagnostic.Severity == DiagnosticSeverity.Hint && !state.HintsOn)
            ? null
            : diagnostic;
    }

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
