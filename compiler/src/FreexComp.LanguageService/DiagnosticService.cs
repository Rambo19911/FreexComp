using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;

namespace FreexComp.LanguageService;

/// <summary>Diagnostics of one document (language-service.md §3).</summary>
public static class DiagnosticService
{
    /// <summary>Lexical, syntax and directive diagnostics of the document's tree.</summary>
    public static ImmutableArray<Diagnostic> GetSyntaxDiagnostics(Document doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return doc.SyntaxTree.Diagnostics;
    }

    /// <summary>
    /// Everything else <c>opc --check</c> reports for the file (CMP-715: together with <see cref="GetSyntaxDiagnostics"/> the same
    /// codes, messages and positions), plus the language-service-only hint H2004 for each uses entry nothing in the file refers to.
    /// </summary>
    public static ImmutableArray<Diagnostic> GetSemanticDiagnostics(Document doc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var model = doc.SemanticModel;
        var syntax = model.SyntaxTree.Diagnostics.ToHashSet();
        var result = ImmutableArray.CreateBuilder<Diagnostic>();
        result.AddRange(model.GetDiagnostics(ct: ct).Where(d => !syntax.Contains(d)));
        result.AddRange(UnusedUnits(doc, ct));
        return result.ToImmutable();
    }

    /// <summary>H2004: a uses entry (not <c>System</c>) whose unit no name outside the uses clauses binds to.</summary>
    private static IEnumerable<Diagnostic> UnusedUnits(Document doc, CancellationToken ct)
    {
        var model = doc.SemanticModel;
        var root = model.SyntaxTree.Root;
        var entries = root.DescendantNodes().OfType<UsedUnitSyntax>().ToList();
        if (entries.Count == 0)
        {
            return [];
        }

        var referenced = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
        foreach (var name in root.DescendantNodes(n => n is not UsesClauseSyntax).OfType<SimpleNameSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (model.GetSymbolInfo(name, ct).Symbol is { } symbol)
            {
                referenced.Add(symbol as UnitSymbol ?? (Symbol?)symbol.ContainingUnit ?? symbol);
            }
        }

        var unused = new List<Diagnostic>();
        foreach (var entry in entries)
        {
            if (model.GetSymbolInfo(entry, ct).Symbol is UnitSymbol unit && !referenced.Contains(unit)
                && !string.Equals(unit.Name, "System", StringComparison.OrdinalIgnoreCase))
            {
                unused.Add(Diagnostic.Create("H2004", Compiler.Binding.DiagnosticBag.LocationOf(entry.Name), entry.Name.ToString()));
            }
        }

        return unused;
    }
}
