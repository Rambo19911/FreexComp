using System.Collections.Immutable;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Units;

/// <summary>One source file of a compilation (program or unit) and how far it is bound.</summary>
internal sealed class LoadedUnit(string path)
{
    public string Path { get; } = path;

    public SyntaxTree? Tree { get; set; }

    public UnitPhase Phase { get; set; }

    /// <summary>The unit-level binder bag (uses entries, declarations, unit-level bodies: one E2003 set).</summary>
    public DiagnosticBag Bag { get; } = new();

    /// <summary>Parse, heading, binder and flow diagnostics, in that order.</summary>
    public List<Diagnostic> Diagnostics { get; } = [];

    /// <summary>The first binding phase (<see cref="DeclarationBinder.BindInterface"/>).</summary>
    public DeclarationBindingResult? Interface { get; set; }

    /// <summary>Innermost scope of the interface-uses chain (a program's uses chain).</summary>
    public Scope? InterfaceUsesScope { get; set; }

    /// <summary>The units of the interface uses clause (a program's uses clause), as written, <c>System</c> left out.</summary>
    public List<LoadedUnit> InterfaceUses { get; } = [];

    /// <summary>The units of the implementation uses clause, as written.</summary>
    public List<LoadedUnit> ImplementationUses { get; } = [];

    /// <summary>Every uses entry (both clauses) by the unit it resolved to, with the name as written (E2208).</summary>
    public Dictionary<LoadedUnit, string> UsedAs { get; } = [];

    public HashSet<string> WrittenNames { get; } = new(IdentifierComparer.Instance);

    public BindingResult? Result { get; set; }

    public UnitSymbol? Symbol => Interface?.Unit;

    public bool IsProgram => Tree?.Root is ProgramSyntax;

    /// <summary>The name in the heading (the canonical name, LANG-505), else the file's base name.</summary>
    public string Name => Tree?.Root switch
    {
        UnitSyntax unit when !unit.Name.IsMissing => UnitBinder.NameOf(unit.Name),
        ProgramSyntax { Name: { IsMissing: false } name } => UnitBinder.NameOf(name),
        _ => global::System.IO.Path.GetFileNameWithoutExtension(Path),
    };
}

internal enum UnitPhase { New, Parsing, BindingInterface, InterfaceBound, Completing, Complete, Failed }

/// <summary>
/// C-069: the units of one compilation. Starting from the main file it locates (LANG-510 via <see cref="UnitResolver"/>),
/// parses and binds every unit reachable through uses clauses, each once (LANG-536: by file), in two phases per unit
/// (interface, then implementation + bodies) so that implementation-uses cycles bind (LANG-513). Uses entries build the
/// scope chain of LANG-310/515 (<see cref="Scope.Uses"/>); entry errors E2200…E2206, E2208 are reported at the entry.
/// <see cref="InitializationOrder"/> is LANG-520. Implements the quiet chains of the <c>{$IF}</c> evaluator
/// (<see cref="IUsesScopes"/>).
/// </summary>
internal sealed class UnitBinder : IUsesScopes
{
    private const string SystemName = "System";

    private readonly UnitResolver _resolver;
    private readonly Func<string, IUsesScopes?, List<Diagnostic>, SyntaxTree?> _parse;
    private readonly Dictionary<string, LoadedUnit> _byPath = new(Text.PathUtil.Comparer);
    private readonly List<LoadedUnit> _units = [];
    private readonly List<LoadedUnit> _interfaceStack = [];

    /// <summary>The uses entry each unit on <see cref="_interfaceStack"/> is binding right now (the opening entry of an E2205 cycle).</summary>
    private readonly Dictionary<LoadedUnit, UsedUnitSyntax> _entryInProgress = [];

    /// <param name="parse">Reads and parses a file with the given <c>{$IF}</c> chain provider; null (with E0002/E0003) when unreadable.</param>
    public UnitBinder(BuiltInTypes types, UnitResolver resolver, Func<string, IUsesScopes?, List<Diagnostic>, SyntaxTree?> parse)
    {
        Types = types;
        _resolver = resolver;
        _parse = parse;
    }

    public BuiltInTypes Types { get; }

    public LoadedUnit? SystemUnit { get; private set; }

    public LoadedUnit? Main { get; private set; }

    /// <summary>Every loaded file in load order.</summary>
    public IReadOnlyList<LoadedUnit> Units => _units;

    public Scope Root => SystemUnit?.Interface?.InterfaceScope is { } system ? Scope.Uses(system, Types.Scope, [SystemName]) : Types.Scope;

    /// <summary>Binds <c>System</c> (<paramref name="systemPath"/>), then the main file and every unit reachable from it.</summary>
    public void BindAll(string systemPath, string mainPath)
    {
        SystemUnit = Load(systemPath);
        EnsureInterface(SystemUnit);
        Main = Load(mainPath);
        EnsureInterface(Main);
        for (var i = 0; i < _units.Count; i++)
        {
            Complete(_units[i]);
        }
    }

    /// <summary>
    /// LANG-520: <c>System</c>, then a depth-first post-order traversal from the main file's uses clauses (interface uses
    /// left to right, then implementation uses), skipping units already visited or being visited. The main file is not part of it.
    /// </summary>
    public ImmutableArray<LoadedUnit> InitializationOrder()
    {
        var order = new List<LoadedUnit>();
        var visited = new HashSet<LoadedUnit>();
        if (SystemUnit is { } system)
        {
            visited.Add(system);
            order.Add(system);
        }

        if (Main is { } main)
        {
            visited.Add(main);
            foreach (var used in main.InterfaceUses.Concat(main.ImplementationUses))
            {
                Visit(used);
            }
        }

        return [.. order];

        void Visit(LoadedUnit unit)
        {
            if (!visited.Add(unit))
            {
                return;
            }

            foreach (var used in unit.InterfaceUses.Concat(unit.ImplementationUses))
            {
                Visit(used);
            }

            order.Add(unit);
        }
    }

    public Scope Chain(UsesClauseSyntax? clause, Scope parent)
    {
        foreach (var entry in Entries(clause))
        {
            var written = NameOf(entry.Name);
            if (IdentifierComparer.Instance.Equals(written, SystemName)
                || _resolver.Find(written, entry.Path?.Value as string) is not { } path
                || Load(path) is not { } used
                || ReferenceEquals(used, SystemUnit)
                || !EnsureInterface(used)
                || used.IsProgram)
            {
                continue;
            }

            parent = Scope.Uses(used.Interface!.InterfaceScope!, parent, [written, used.Name]);
        }

        return parent;
    }

    private LoadedUnit Load(string path)
    {
        if (!_byPath.TryGetValue(path, out var unit))
        {
            unit = new LoadedUnit(path);
            _byPath.Add(path, unit);
            _units.Add(unit);
        }

        return unit;
    }

    /// <summary>Parses and binds the interface part; false when the unit cannot be read or is on the current interface path (a cycle).</summary>
    private bool EnsureInterface(LoadedUnit unit)
    {
        switch (unit.Phase)
        {
            case UnitPhase.InterfaceBound or UnitPhase.Completing or UnitPhase.Complete:
                return true;
            case UnitPhase.Parsing or UnitPhase.BindingInterface or UnitPhase.Failed:
                return false;
        }

        unit.Phase = UnitPhase.Parsing;
        var isSystem = ReferenceEquals(unit, SystemUnit);
        unit.Tree = _parse(unit.Path, isSystem ? null : this, unit.Diagnostics);
        if (unit.Tree is null)
        {
            unit.Phase = UnitPhase.Failed;
            return false;
        }

        unit.Phase = UnitPhase.BindingInterface;
        if (unit.Tree.Root is UnitSyntax { Name: { IsMissing: false } heading } && !IdentifierComparer.Instance.Equals(unit.Name, global::System.IO.Path.GetFileNameWithoutExtension(unit.Path)))
        {
            unit.Bag.Report(heading, "E2201", unit.Name, global::System.IO.Path.GetFileNameWithoutExtension(unit.Path));     // LANG-505
        }

        _interfaceStack.Add(unit);
        var clause = unit.Tree.Root switch
        {
            UnitSyntax u => u.InterfaceSection.UsesClause,
            ProgramSyntax p => p.UsesClause,
            _ => null,
        };
        unit.InterfaceUsesScope = BindUses(unit, clause, isSystem ? Types.Scope : Root, unit.InterfaceUses);
        unit.Interface = DeclarationBinder.BindInterface(unit.Tree, Types, unit.Bag, unit.InterfaceUsesScope);
        _interfaceStack.RemoveAt(_interfaceStack.Count - 1);
        unit.Phase = UnitPhase.InterfaceBound;
        return true;
    }

    /// <summary>The implementation part, routine and unit-level bodies, then flow analysis (CMP-115).</summary>
    private void Complete(LoadedUnit unit)
    {
        if (unit.Phase != UnitPhase.InterfaceBound && !(unit.Phase == UnitPhase.New && EnsureInterface(unit)))
        {
            return;
        }

        unit.Phase = UnitPhase.Completing;
        var clause = (unit.Tree!.Root as UnitSyntax)?.ImplementationSection.UsesClause;
        var implementationUses = BindUses(unit, clause, unit.InterfaceUsesScope!, unit.ImplementationUses);
        var system = ReferenceEquals(unit, SystemUnit) ? null : SystemUnit?.Symbol;
        unit.Result = Binder.Complete(unit.Tree, unit.Interface!, Types, unit.Bag, implementationUses, system);
        unit.Diagnostics.AddRange(unit.Result.Diagnostics);
        unit.Diagnostics.AddRange(FlowAnalyzer.Analyze(unit.Result));
        unit.Phase = UnitPhase.Complete;
    }

    /// <summary>The uses chain of one clause of <paramref name="unit"/> on top of <paramref name="parent"/>, with its entry diagnostics.</summary>
    private Scope BindUses(LoadedUnit unit, UsesClauseSyntax? clause, Scope parent, List<LoadedUnit> uses)
    {
        foreach (var entry in Entries(clause))
        {
            var written = NameOf(entry.Name);
            string? inPath = null;
            if (entry.InKeyword is { } inKeyword)
            {
                if (unit.IsProgram)
                {
                    inPath = entry.Path?.Value as string;
                }
                else
                {
                    unit.Bag.Report(inKeyword, "E2200");                    // LANG-502
                }
            }

            if (!unit.IsProgram && IdentifierComparer.Instance.Equals(written, unit.Name))
            {
                unit.Bag.Report(entry.Name, "E2204", written);             // LANG-512
                continue;
            }

            if (!unit.WrittenNames.Add(written))
            {
                unit.Bag.Report(entry.Name, "E2203", written);
                continue;
            }

            if (IdentifierComparer.Instance.Equals(written, SystemName) && inPath is null)
            {
                continue;                                               // LANG-511: no effect
            }

            if (_resolver.Find(written, inPath) is not { } path)
            {
                unit.Bag.Report(entry.Name, "E2202", written);             // LANG-510, 535
                continue;
            }

            var used = Load(path);
            if (ReferenceEquals(used, SystemUnit) && SystemUnit is not null && inPath is null)
            {
                continue;                                               // LANG-537: System under an alias has no effect
            }

            if (ReferenceEquals(used, unit))
            {
                unit.Bag.Report(entry.Name, "E2204", written);
                continue;
            }

            _entryInProgress[unit] = entry;
            var bound = EnsureInterface(used);
            _entryInProgress.Remove(unit);
            if (!bound)
            {
                if (used.Phase != UnitPhase.Failed)
                {
                    // LANG-513 (C-070): an interface-uses cycle, its path in canonical names (LANG-539) from the cycle's unit first
                    // reached from the program, reported in that unit at its entry that leads into the cycle; a unit still being
                    // parsed (reached through a {$IF} chain) is not on the stack: then at this entry
                    var start = _interfaceStack.IndexOf(used);
                    IEnumerable<LoadedUnit> members = start < 0 ? [used, unit] : _interfaceStack.Skip(start);
                    var cycle = string.Join(" → ", members.Append(used).Select(u => u.Name));
                    if (start >= 0 && _entryInProgress.TryGetValue(used, out var opening))
                    {
                        used.Bag.Report(opening.Name, "E2205", cycle);
                    }
                    else
                    {
                        unit.Bag.Report(entry.Name, "E2205", cycle);
                    }
                }

                continue;
            }

            if (used.IsProgram)
            {
                unit.Bag.Report(entry.Name, "E2206", written);
                continue;
            }

            if (inPath is not null && !IdentifierComparer.Instance.Equals(used.Name, written))
            {
                unit.Bag.Report(entry.Name, "E2201", used.Name, written);    // LANG-535 step 1: the heading must equal the written name
            }

            if (unit.UsedAs.TryGetValue(used, out var previous))
            {
                unit.Bag.Report(entry.Name, "E2208", written, previous);  // LANG-537
                continue;
            }

            unit.UsedAs.Add(used, written);
            uses.Add(used);
            parent = Scope.Uses(used.Interface!.InterfaceScope!, parent, [written, used.Name]);
        }

        return parent;
    }

    private static IEnumerable<UsedUnitSyntax> Entries(UsesClauseSyntax? clause) =>
        clause is null ? [] : clause.Units.Where(e => !e.Name.IsMissing && !e.Name.ContainsDiagnostics);

    /// <summary>A dotted name as written (LANG-505).</summary>
    internal static string NameOf(SyntaxNode name) =>
        string.Join(".", name.DescendantTokens().Where(t => t.Kind == SyntaxKind.IdentifierToken).Select(t => t.Value as string ?? t.Text));
}
