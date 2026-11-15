using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.LanguageService;

/// <summary>Go to definition / implementation (CMP-740, 741, C-137).</summary>
public static partial class NavigationService
{
    /// <summary>
    /// CMP-740: a routine or method reference → its implementation heading (else its declaration); on a declaration heading → the
    /// implementation, on the implementation heading → the declaration; other symbols → the declaring identifier; a unit (uses
    /// entry) → the start of its file; symbols from a <c>.pcu</c> → the recorded declaration when the source exists; built-ins → empty.
    /// </summary>
    public static ImmutableArray<Location> GoToDefinition(Document doc, int position, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ct.ThrowIfCancellationRequested();
        if (QuickInfoService.SymbolAt(doc.SemanticModel, position) is not ({ } symbol, var token))
        {
            return [];
        }

        if (symbol is UnitSymbol unit)
        {
            return unit.Locations.IsEmpty ? [] : [FileStart(unit.Locations[0].Path)];
        }

        var locations = Sites(doc.Compilation, symbol);
        if (locations.IsEmpty)
        {
            return [];
        }

        if (symbol is not RoutineSymbol || locations.Length == 1)
        {
            return [locations[0]];
        }

        var on = locations.IndexOf(locations.FirstOrDefault(l => PathUtil.AreEqual(l.Path, doc.SemanticModel.SyntaxTree.Text.Path)
                                                                  && l.Span.Start <= token.Span.Start && token.Span.Start < Math.Max(l.Span.End, l.Span.Start + 1)));
        return on == locations.Length - 1 ? [locations[0]] : [locations[^1]];
    }

    /// <summary>
    /// CMP-741: a virtual or abstract method → every override in the compilation; an interface method → every implementing method;
    /// a class → its descendants; an interface → the classes implementing it. Sorted by path, then position.
    /// </summary>
    public static ImmutableArray<Location> GoToImplementation(Document doc, int position, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ct.ThrowIfCancellationRequested();
        if (QuickInfoService.SymbolAt(doc.SemanticModel, position) is not ({ } symbol, _))
        {
            return [];
        }

        var classes = AllTypes(doc.Compilation).OfType<ClassTypeSymbol>().ToList();
        IEnumerable<Symbol> targets = symbol switch
        {
            MethodSymbol { ContainingInterface: not null } method => classes.SelectMany(c => c.Interfaces).SelectMany(i => i.Interface.AllMethods
                .Select((m, index) => (m, index)).Where(p => SymbolKey.AreSame(p.m, method) && p.index < i.Implementations.Length)
                .Select(p => i.Implementations[p.index])),
            MethodSymbol method => classes.SelectMany(c => c.Methods).Where(m => Overrides(m, method)),
            ClassTypeSymbol type => classes.Where(c => !SymbolKey.AreSame(c, type) && c.SelfAndAncestors().Any(a => SymbolKey.AreSame(a, type))),
            InterfaceTypeSymbol type => classes.Where(c => c.SelfAndAncestors().SelectMany(a => a.Interfaces).Any(i => i.Interface.SelfAndAncestors().Any(x => SymbolKey.AreSame(x, type)))),
            _ => [],
        };
        var seen = new HashSet<SymbolKey>();
        return [.. targets.Where(t => seen.Add(SymbolKey.Create(t)))
            .Select(t => Sites(doc.Compilation, t) is { IsEmpty: false } sites ? sites[t is RoutineSymbol ? ^1 : 0] : (Location?)null)
            .OfType<Location>()
            .OrderBy(l => l.Path, PathUtil.Comparer).ThenBy(l => l.Span.Start)];
    }

    /// <summary><paramref name="method"/> or an ancestor it overrides is <paramref name="target"/> (and <paramref name="method"/> is not it).</summary>
    private static bool Overrides(MethodSymbol method, MethodSymbol target)
    {
        if (SymbolKey.AreSame(method, target))
        {
            return false;
        }

        for (var m = method.OverriddenMethod; m is not null; m = m.OverriddenMethod)
        {
            if (SymbolKey.AreSame(m, target))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every type declared at unit level (and nested in classes and records) in the compilation's units.</summary>
    internal static IEnumerable<TypeSymbol> AllTypes(Compilation compilation)
    {
        var seen = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Symbol>(compilation.UnitSymbols.SelectMany(u => u.InterfaceMembers.Concat(u.ImplementationMembers)).Reverse());
        while (pending.Count > 0)
        {
            var symbol = pending.Pop();
            if (symbol is not TypeSymbol type || !seen.Add(type))
            {
                continue;
            }

            yield return type;
            var nested = type switch
            {
                ClassTypeSymbol c => c.NestedMembers,
                RecordTypeSymbol r => r.NestedMembers,
                _ => [],
            };
            foreach (var member in nested.Reverse())
            {
                pending.Push(member);
            }
        }
    }

    /// <summary>The declaration sites whose file can be read, with line positions (a <c>.pcu</c> records only offsets).</summary>
    private static ImmutableArray<Location> Sites(Compilation compilation, Symbol symbol)
    {
        var builder = ImmutableArray.CreateBuilder<Location>();
        foreach (var location in symbol.Locations)
        {
            if (location.HasPosition)
            {
                builder.Add(location);
            }
            else if (compilation.TryGetSyntaxTree(location.Path, out var tree) && location.Span.End <= tree.Text.Text.Length)
            {
                var lines = tree.Text.Lines;
                builder.Add(location with
                {
                    LineSpan = new LinePositionSpan(lines.GetLinePosition(location.Span.Start), lines.GetLinePosition(location.Span.End)),
                });
            }
        }

        return builder.ToImmutable();
    }

    private static Location FileStart(string path) => new(path, new TextSpan(0, 0), new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0)));
}
