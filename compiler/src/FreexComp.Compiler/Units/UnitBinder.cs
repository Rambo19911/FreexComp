using System.Collections.Immutable;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
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

    /// <summary>Innermost scope of the implementation-uses chain (on top of <see cref="InterfaceUsesScope"/>).</summary>
    public Scope? ImplementationUsesScope { get; set; }

    /// <summary>The units of the interface uses clause (a program's uses clause), as written, <c>System</c> left out.</summary>
    public List<LoadedUnit> InterfaceUses { get; } = [];

    /// <summary>The units of the implementation uses clause, as written.</summary>
    public List<LoadedUnit> ImplementationUses { get; } = [];

    /// <summary>Every uses entry (both clauses) by the unit it resolved to, with the name as written (E2208).</summary>
    public Dictionary<LoadedUnit, string> UsedAs { get; } = [];

    public HashSet<string> WrittenNames { get; } = new(IdentifierComparer.Instance);

    public BindingResult? Result { get; set; }

    /// <summary>C-244: bound with only some bodies (<see cref="UnitBinder.PartialBodies"/>); never taken over by a later run.</summary>
    public bool IsPartial { get; set; }

    /// <summary>The unit's <c>.pcu</c> <c>linkInputs</c> (C-077); set by the compilation after binding.</summary>
    public UnitLinkInputs LinkInputs { get; set; } = UnitLinkInputs.Empty;

    /// <summary>The unit's <c>{$FORM}</c> blobs (C-114).</summary>
    public ImmutableArray<Forms.EmbeddedForm> Forms { get; set; } = [];

    /// <summary>The <c>.pcu</c> a precompiled unit (no source in this compilation, C-074) was imported from; null for source units.</summary>
    public System.Text.Json.Nodes.JsonObject? Pcu { get; set; }

    /// <summary>A precompiled unit's object file (<c>&lt;Name&gt;.obj</c> next to its <c>.pcu</c>).</summary>
    public string ObjectPath => global::System.IO.Path.ChangeExtension(Path, ".obj");

    /// <summary>Imported from a <c>.pcu</c>: nothing to compile, the object is linked as it is.</summary>
    public bool IsPrecompiled => Pcu is not null;

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

internal enum UnitPhase { New, Parsing, BindingInterface, InterfaceBound, ImplementationUsesBound, Completing, Complete, Failed }

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

    private readonly Func<string, System.Text.Json.Nodes.JsonObject?> _readPcu;

    /// <param name="parse">Reads and parses a file with the given <c>{$IF}</c> chain provider; null (with E0002/E0003) when unreadable.</param>
    /// <param name="readPcu">Reads a precompiled unit's <c>.pcu</c>; null when it is unreadable or unusable (C-074).</param>
    /// <param name="reuse">
    /// C-130 (CMP-130): a completely bound unit of an earlier run (same <paramref name="types"/>) to take over as it is instead of
    /// loading the file again; null to load it.
    /// </param>
    public UnitBinder(BuiltInTypes types, UnitResolver resolver, Func<string, IUsesScopes?, List<Diagnostic>, SyntaxTree?> parse,
        Func<string, System.Text.Json.Nodes.JsonObject?>? readPcu = null, Func<string, LoadedUnit?>? reuse = null)
    {
        Types = types;
        _resolver = resolver;
        _parse = parse;
        _readPcu = readPcu ?? (_ => null);
        _reuse = reuse;
    }

    private readonly Func<string, LoadedUnit?>? _reuse;

    public BuiltInTypes Types { get; }

    /// <summary>The units taken over from an earlier run (C-130); the others were loaded and bound by this one.</summary>
    public HashSet<LoadedUnit> Reused { get; } = [];

    /// <summary>
    /// C-244: the file whose bodies are bound only where the filter accepts them (no flow analysis, no generic instance bodies), for a
    /// language-service model of one position; null binds everything.
    /// </summary>
    public (string Path, Func<SyntaxNode, bool> Filter)? PartialBodies { get; init; }

    public LoadedUnit? SystemUnit { get; private set; }

    public LoadedUnit? Main { get; private set; }

    /// <summary>Every loaded file in load order.</summary>
    public IReadOnlyList<LoadedUnit> Units => _units;

    public Scope Root => SystemUnit?.Interface?.InterfaceScope is { } system ? Scope.Uses(system, Types.Scope, [SystemName]) : Types.Scope;

    /// <summary>
    /// Binds <c>System</c> (<paramref name="systemPath"/>), then the main file and every unit reachable from it. Interfaces and
    /// uses clauses bind on this thread in load order (they load further units); then every interface any body can see is bound,
    /// so the implementation parts, bodies and flow analysis of all units run on up to <paramref name="maxParallelism"/> threads
    /// (CMP-156). Each unit's diagnostics stay in its own list, so the result does not depend on the thread count.
    /// </summary>
    public void BindAll(string systemPath, string mainPath, int maxParallelism = 1)
    {
        SystemUnit = Load(systemPath);
        EnsureInterface(SystemUnit);
        Main = Load(mainPath);
        EnsureInterface(Main);
        for (var i = 0; i < _units.Count; i++)
        {
            BindImplementationUses(_units[i]);
        }

        var ready = _units.Where(u => u.Phase == UnitPhase.ImplementationUsesBound).ToList();
        if (maxParallelism <= 1 || ready.Count <= 1)
        {
            ready.ForEach(Complete);
        }
        else
        {
            Parallel.ForEach(ready, new ParallelOptions { MaxDegreeOfParallelism = maxParallelism }, Complete);
        }

        // C-116 (CMP-385): generic instance bodies once all implementation parts are bound; in unit order (deterministic)
        foreach (var unit in ready.Where(u => u.Result is not null && !u.IsPartial))
        {
            var before = unit.Result!.Diagnostics.ToHashSet();
            unit.Result = Binder.BindInstances(unit.Result);
            unit.Diagnostics.AddRange(unit.Result.Diagnostics.Where(d => !before.Contains(d)));
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
            if (_reuse?.Invoke(path) is { Phase: UnitPhase.Complete } reused)
            {
                unit = reused;
                Reused.Add(unit);
            }
            else
            {
                unit = new LoadedUnit(path);
            }

            _byPath.Add(path, unit);
            _units.Add(unit);
            if (Reused.Contains(unit))
            {
                // the units a taken-over unit uses are part of this run too (taken over as well, or bound anew by BindAll)
                foreach (var used in unit.InterfaceUses.Concat(unit.ImplementationUses).ToList())
                {
                    Load(used.Path);
                }
            }
        }

        return unit;
    }

    /// <summary>Parses and binds the interface part; false when the unit cannot be read or is on the current interface path (a cycle).</summary>
    private bool EnsureInterface(LoadedUnit unit)
    {
        switch (unit.Phase)
        {
            case UnitPhase.InterfaceBound or UnitPhase.ImplementationUsesBound or UnitPhase.Completing or UnitPhase.Complete:
                return true;
            case UnitPhase.Parsing or UnitPhase.BindingInterface or UnitPhase.Failed:
                return false;
        }

        unit.Phase = UnitPhase.Parsing;
        var isSystem = ReferenceEquals(unit, SystemUnit);
        if (unit.Path.EndsWith(".pcu", StringComparison.OrdinalIgnoreCase))
        {
            return Import(unit, isSystem);
        }

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

    /// <summary>
    /// C-074: a precompiled unit's interface from its <c>.pcu</c>. Its recorded dependencies are loaded first (they make up its uses
    /// lists for the init order, LANG-520, and the units its <c>ref</c> types name). An unreadable or unusable file, or one that does
    /// not fit the units it names, makes the unit unavailable: E2202 at the file.
    /// </summary>
    private bool Import(LoadedUnit unit, bool isSystem)
    {
        var pcu = _readPcu(unit.Path);
        if (pcu is not null)
        {
            unit.Phase = UnitPhase.BindingInterface;
            _interfaceStack.Add(unit);
            try
            {
                foreach (var (name, section) in PcuReader.Dependencies(pcu))
                {
                    if (IdentifierComparer.Instance.Equals(name, SystemName) || _resolver.Find(name) is not { } path || Load(path) is not { } used
                        || !EnsureInterface(used))
                    {
                        continue;
                    }

                    (section == "implementation" ? unit.ImplementationUses : unit.InterfaceUses).Add(used);
                }

                var imported = PcuReader.Import(pcu, Types, isSystem ? Types.Scope : Root,
                    name => _units.FirstOrDefault(u => u.Interface is not null && IdentifierComparer.Instance.Equals(u.Name, name))?.Interface!.InterfaceScope);
                unit.Interface = new DeclarationBindingResult(imported.Unit, imported.InterfaceScope, imported.InterfaceScope, []);
                unit.InterfaceUsesScope = imported.InterfaceScope;
                unit.Pcu = pcu;
                unit.Phase = UnitPhase.Complete;
                return true;
            }
            catch (Exception e) when (e is PcuFormatException or InvalidOperationException or FormatException or NullReferenceException)
            {
                unit.InterfaceUses.Clear();                             // keys of the wrong JSON kind or missing ones: an unusable file
                unit.ImplementationUses.Clear();
            }
            finally
            {
                _interfaceStack.Remove(unit);
            }
        }

        unit.Diagnostics.Add(Diagnostic.Create("E2202", Location.ForFile(unit.Path), unit.Name));
        unit.Phase = UnitPhase.Failed;
        return false;
    }

    /// <summary>The implementation uses clause (it may load and bind the interfaces of further units).</summary>
    private void BindImplementationUses(LoadedUnit unit)
    {
        if (unit.Phase != UnitPhase.InterfaceBound && !(unit.Phase == UnitPhase.New && EnsureInterface(unit)))
        {
            return;
        }

        var clause = (unit.Tree!.Root as UnitSyntax)?.ImplementationSection.UsesClause;
        unit.ImplementationUsesScope = BindUses(unit, clause, unit.InterfaceUsesScope!, unit.ImplementationUses);
        unit.Phase = UnitPhase.ImplementationUsesBound;
    }

    /// <summary>
    /// The implementation part, routine and unit-level bodies, then flow analysis (CMP-115). Reads only bound interfaces of other
    /// units and writes only this unit's symbols, scopes and bag: safe to run for several units at once.
    /// </summary>
    private void Complete(LoadedUnit unit)
    {
        unit.Phase = UnitPhase.Completing;
        var system = ReferenceEquals(unit, SystemUnit) ? null : SystemUnit?.Symbol;
        var partial = PartialBodies is { } p && Text.PathUtil.AreEqual(p.Path, unit.Path) ? p.Filter : null;
        unit.IsPartial = partial is not null;
        unit.Result = Binder.Complete(unit.Tree!, unit.Interface!, Types, unit.Bag, unit.ImplementationUsesScope!, system, partial);
        unit.Diagnostics.AddRange(unit.Result.Diagnostics);
        if (!unit.IsPartial)
        {
            unit.Diagnostics.AddRange(FlowAnalyzer.Analyze(unit.Result));
        }

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
