using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Binding;

// LANG-380 (C-110), LANG-571 (C-310): labels and goto.
internal sealed partial class BodyBinder
{
    /// <summary>
    /// A construct a <c>goto</c> may not enter from outside — a part of a <c>try</c> statement, a <c>for</c>, <c>with</c> or
    /// <c>case</c> statement — and, for a part of a <c>try</c> statement, not leave either (E2072).
    /// </summary>
    private sealed class JumpRegion(JumpRegion? outer, bool isTryPart)
    {
        public JumpRegion? Outer { get; } = outer;

        public bool IsTryPart { get; } = isTryPart;
    }

    private sealed class LabelInfo(GotoLabel label, SyntaxNode declaration)
    {
        public GotoLabel Label { get; } = label;

        public SyntaxNode Declaration { get; } = declaration;

        /// <summary>The labelled statement; null until the label is defined.</summary>
        public LabeledStatementSyntax? Definition { get; set; }

        public JumpRegion? Region { get; set; }
    }

    /// <summary>The labels of the body's <c>label</c> sections by name (an integer label by its digits).</summary>
    private readonly Dictionary<string, LabelInfo> _labels = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<(GotoStatementSyntax Syntax, LabelInfo Label, JumpRegion? Region)> _gotos = [];

    /// <summary>The declaration scopes of the body's inline declarations in source order, with the first declared name (LANG-560).</summary>
    private readonly List<(int Start, int End, string Name)> _inlineScopes = [];

    private JumpRegion? _region;

    /// <summary>
    /// Binds the statement list of a block whose <c>label</c> sections are among <paramref name="declarations"/>: a label is
    /// declared there (E2070) once (E2004), defined once (E2071, E2073) and its <c>goto</c>s obey LANG-380 (E2072) and LANG-571 (E2076).
    /// </summary>
    public BoundBlock BindBody(SyntaxNode syntax, SeparatedSyntaxList<StatementSyntax> statements, SyntaxList<SyntaxNode> declarations)
    {
        foreach (var section in declarations.OfType<LabelSectionSyntax>())
        {
            foreach (var label in section.Labels)
            {
                if (LabelName(label) is not { } name)
                {
                    continue;
                }

                if (_labels.TryGetValue(name, out var previous))
                {
                    // LANG-380 (C-321): one declaration per label in a block (the first one stays)
                    _diagnostics.ReportRedeclared(label.FirstToken(), name, DiagnosticBag.LocationOf(previous.Declaration));
                    continue;
                }

                _labels.Add(name, new LabelInfo(new GotoLabel(new LabelSymbol(name, Container, [DiagnosticBag.LocationOf(label)])), label));
            }
        }

        var block = BindBlock(syntax, statements);
        foreach (var (statement, label, region) in _gotos)
        {
            if (label.Definition is { } definition)
            {
                CheckJump(statement, label, definition, region);
            }
        }

        foreach (var label in _labels.Values.Where(l => l.Definition is null))
        {
            if (_prunedLabels?.Contains(label.Label.Name) != true)
            {
                _diagnostics.Report(label.Declaration, "E2073", label.Label.Name);
                continue;
            }

            // LANG-977 rule 5 (C-417): placed only in a statement pruned from this instance, the label counts as defined; the kept code
            // cannot jump to it — the pruned statement is not there (rule 2), as for a jump into a statement (E2072)
            foreach (var (statement, target, _) in _gotos)
            {
                if (ReferenceEquals(target, label))
                {
                    _diagnostics.Report(statement.GotoKeyword, "E2072");
                }
            }
        }

        return block;
    }

    /// <summary>The name of a label; null for a missing one (already reported).</summary>
    private static string? LabelName(ExpressionSyntax label) => label switch
    {
        IdentifierNameSyntax { Identifier: { IsMissing: false } identifier } => identifier.Value as string ?? identifier.Text,
        LiteralExpressionSyntax literal => literal.FirstToken().Value?.ToString() ?? literal.FirstToken().Text,
        _ => null,
    };

    private T InRegion<T>(bool isTryPart, Func<T> bind)
    {
        var outer = _region;
        _region = new JumpRegion(outer, isTryPart);
        try
        {
            return bind();
        }
        finally
        {
            _region = outer;
        }
    }

    private BoundStatement BindLabeled(LabeledStatementSyntax syntax)
    {
        LabelInfo? defined = null;
        if (LabelName(syntax.Label) is { } name)
        {
            if (!_labels.TryGetValue(name, out var label))
            {
                _diagnostics.Report(syntax.Label, "E2070", name);
            }
            else if (label.Definition is not null)
            {
                _diagnostics.Report(syntax.Label, "E2071", name);
            }
            else
            {
                label.Definition = syntax;
                label.Region = _region;
                defined = label;
            }
        }

        var statement = BindStatement(syntax.Statement);
        return defined is null ? statement : new BoundLabelStatement(syntax, defined.Label, statement);
    }

    private BoundStatement BindGoto(GotoStatementSyntax syntax)
    {
        if (LabelName(syntax.Label) is not { } name)
        {
            return new BoundErrorStatement(syntax);
        }

        if (!_labels.TryGetValue(name, out var label))
        {
            _diagnostics.Report(syntax.Label, "E2070", name);
            return new BoundErrorStatement(syntax);
        }

        _gotos.Add((syntax, label, _region));
        return new BoundGotoStatement(syntax, label.Label);
    }

    /// <summary>
    /// LANG-380: no jump into or out of a part of a <c>try</c> statement, nor into a <c>for</c>/<c>with</c>/<c>case</c> statement
    /// (E2072). LANG-571: no jump from outside into the declaration scope of an inline declaration (E2076, naming the first such
    /// declaration in source order); the implicit regions of declaration scopes do not count as <c>try</c> statements (CMP-1185).
    /// </summary>
    private void CheckJump(GotoStatementSyntax statement, LabelInfo label, LabeledStatementSyntax definition, JumpRegion? from)
    {
        var source = Regions(from);
        var target = Regions(label.Region);
        if (target.Any(r => !source.Contains(r)) || source.Any(r => r.IsTryPart && !target.Contains(r)))
        {
            _diagnostics.Report(statement.GotoKeyword, "E2072");
            return;
        }

        var (at, to) = (statement.Span.Start, definition.Span.Start);
        foreach (var (start, end, name) in _inlineScopes)
        {
            if (to >= start && to < end && !(at >= start && at < end))
            {
                _diagnostics.Report(statement.GotoKeyword, "E2076", label.Label.Name, name);
                return;
            }
        }
    }

    private static HashSet<JumpRegion> Regions(JumpRegion? innermost)
    {
        var regions = new HashSet<JumpRegion>(ReferenceEqualityComparer.Instance);
        for (var region = innermost; region is not null; region = region.Outer)
        {
            regions.Add(region);
        }

        return regions;
    }
}
