using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Binding;

/// <summary>Collects binder diagnostics; locations use the file each token was read from (includes, LANG-876).</summary>
internal sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly HashSet<string> _undeclared = new(IdentifierComparer.Instance);
    private readonly HashSet<(string Path, int Start, string Name)> _hinted = [];
    private readonly HashSet<object> _uncapturable = [];

    public int Count => _diagnostics.Count;

    /// <summary>Adds an already made diagnostic (warning control was applied where it was reported).</summary>
    public void Add(Diagnostic diagnostic) => _diagnostics.Add(diagnostic);

    /// <summary>
    /// Removes the diagnostics with <paramref name="code"/> added since the bag held <paramref name="count"/> entries; true when an
    /// error with another code was added since then (C-416: the operand of <c>IsConstValue</c> may fail to be constant, LANG-975).
    /// </summary>
    public bool RemoveSince(int count, string code)
    {
        var otherError = false;
        for (var i = _diagnostics.Count - 1; i >= count; i--)
        {
            if (_diagnostics[i].Code == code)
            {
                _diagnostics.RemoveAt(i);
            }
            else
            {
                otherError |= _diagnostics[i].Severity == DiagnosticSeverity.Error;
            }
        }

        return otherError;
    }

    /// <summary>
    /// LANG-711 (C-118, C-222): an error found while instantiating <paramref name="instance"/> becomes E2710 at the same place with
    /// <paramref name="site"/> as related location "instantiated from here" (CMP-508); E2710 (of a nested instance) and E2712 pass
    /// unchanged. A warning or hint keeps its code and gets the same related location, but only the first one per (code, location)
    /// in <paramref name="reported"/> (the declaration itself is never checked, so it reports nothing; null = dropped).
    /// </summary>
    public static Diagnostic? InInstance(Diagnostic diagnostic, string instance, Location? site, HashSet<(string Code, Location Location)> reported)
    {
        ImmutableArray<RelatedLocation> related = site is { } at ? diagnostic.Related.Add(new RelatedLocation(at, "instantiated from here")) : diagnostic.Related;
        if (diagnostic.Severity != DiagnosticSeverity.Error)
        {
            lock (reported)
            {
                return reported.Add((diagnostic.Code, diagnostic.Location)) ? diagnostic with { Related = related } : null;
            }
        }

        return diagnostic.Code is "E2710" or "E2712" ? diagnostic
            : Diagnostic.Create("E2710", diagnostic.Location, instance, diagnostic.Message) with { Related = related };
    }

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

    /// <summary>
    /// LANG-970 (C-413): E2720 for <paramref name="variable"/> named at <paramref name="node"/> — once per variable and outermost
    /// anonymous-method expression (<paramref name="expression"/>: its method, placed at its keyword), so at the first reference
    /// bound here. A routine nested in the anonymous method is bound with a bag of its own: the key lets the unit drop the later
    /// one (<see cref="Diagnostic.OnceKey"/>).
    /// </summary>
    public void ReportUncapturable(SyntaxNode node, Symbols.Symbol expression, Symbols.Symbol variable)
    {
        object key = (expression.Locations[0], variable.Name);
        if (_uncapturable.Add(key))
        {
            _diagnostics.Add(Diagnostic.Create("E2720", LocationOf(node), variable.Name) with { OnceKey = key });
        }
    }

    /// <summary>
    /// LANG-472: W1050 / W1051 / W1052 for a use at <paramref name="node"/> of the symbol <paramref name="name"/> declared with
    /// <paramref name="hints"/> (the deprecation text follows as <c>: text</c>) — once per place, however often the name is bound.
    /// </summary>
    public void ReportHints(SyntaxNode node, string name, Symbols.SymbolHints hints)
    {
        var location = LocationOf(node);
        if (!_hinted.Add((location.Path, location.Span.Start, name)))
        {
            return;
        }

        if (hints.IsDeprecated)
        {
            Report(node, "W1050", name, hints.DeprecationMessage is { } text ? ": " + text : "");
        }

        if (hints.IsPlatform)
        {
            Report(node, "W1051", name);
        }

        if (hints.IsExperimental)
        {
            Report(node, "W1052", name);
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

    /// <summary>E2004 for a name whose previous declaration has no symbol (a label, LANG-569).</summary>
    public void ReportRedeclared(SyntaxToken identifier, string name, Location previous) =>
        _diagnostics.Add(Diagnostic.Create("E2004", LocationOf(identifier), name) with { Related = [new RelatedLocation(previous, "previous declaration")] });

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
