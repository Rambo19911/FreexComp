using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Symbols;

/// <summary>Scope kinds of architecture.md §4; lookup order is LANG-310 (the binder chains the scopes in that order).</summary>
internal enum ScopeKind { BuiltIn, Unit, Uses, TypeMember, Routine, Block, With }

/// <summary>
/// One level of the chained name lookup. Names compare per LANG-110 (ASCII letters case-insensitive). A name binds
/// one symbol per scope, except an overload set: several routines of one name (LANG-440), added with
/// <see cref="AddOverload"/>. An alias declares an existing symbol under another name.
/// </summary>
internal sealed class Scope
{
    private readonly Dictionary<string, (Symbol Symbol, int Index)> _symbols;
    private readonly List<(string Name, Symbol Symbol)> _ordered;
    private readonly Dictionary<string, List<(RoutineSymbol Routine, int Index)>> _overloads;

    /// <summary>A view: the same declarations as <paramref name="source"/> (also those added later) under another parent.</summary>
    private Scope(Scope source, ScopeKind kind, Scope? parent, ImmutableArray<string> qualifiers)
    {
        _symbols = source._symbols;
        _ordered = source._ordered;
        _overloads = source._overloads;
        Kind = kind;
        Parent = parent;
        Owner = source.Owner;
        ParentVisibleCount = int.MaxValue;
        Qualifiers = qualifiers;
        Source = source;
    }

    /// <summary>
    /// LANG-310, 515: the interface scope <paramref name="unitInterface"/> of a used unit as a link of an importer's scope
    /// chain (kind <see cref="ScopeKind.Uses"/>), qualifiable by <paramref name="qualifiers"/> (LANG-311, 538: the name as
    /// written and the canonical name).
    /// </summary>
    public static Scope Uses(Scope unitInterface, Scope parent, ImmutableArray<string> qualifiers) =>
        new(unitInterface, ScopeKind.Uses, parent, qualifiers);

    /// <summary>A unit's own interface scope seen from its implementation, chained to the implementation-uses scopes.</summary>
    public static Scope Reparented(Scope scope, Scope parent) => new(scope, scope.Kind, parent, []);

    /// <summary>
    /// C-214 (LANG-538): the scope a name qualified by the unit's own name is looked up in — views of the unit's own scopes
    /// (<paramref name="unitScopes"/>, innermost first: implementation, then interface) above <paramref name="builtIns"/> when the unit is
    /// <c>System</c> (the built-ins belong to it), never the used units.
    /// </summary>
    public static Scope OwnUnit(IReadOnlyList<Scope> unitScopes, Scope? builtIns)
    {
        var system = unitScopes[0].Owner is UnitSymbol { Name: var name } && string.Equals(name, "System", StringComparison.OrdinalIgnoreCase);
        var chain = system ? builtIns : null;
        for (var i = unitScopes.Count - 1; i >= 0; i--)
        {
            chain = new Scope(unitScopes[i], ScopeKind.Unit, chain, []);
        }

        return chain!;
    }

    /// <summary>Names that qualify this used unit's interface symbols (<see cref="ScopeKind.Uses"/> only).</summary>
    public ImmutableArray<string> Qualifiers { get; } = [];

    /// <summary>The scope this one is a view of; null for an ordinary scope.</summary>
    public Scope? Source { get; }

    /// <summary>
    /// The scope a unit-qualified name <c>U.Name</c> is looked up in (LANG-311): this used unit's interface only — for
    /// <c>System</c> also the built-ins, which belong to it (LANG-200).
    /// </summary>
    public Scope Qualified
    {
        get
        {
            if (Kind != ScopeKind.Uses)
            {
                return this;                                            // C-214: an own-unit chain (OwnUnit) is already the lookup scope
            }

            if (_qualified is null)
            {
                var builtIns = Parent is { Kind: ScopeKind.BuiltIn } parent ? parent : null;
                _qualified = new Scope(this, ScopeKind.Uses, builtIns, Qualifiers);
            }

            return _qualified;
        }
    }

    private Scope? _qualified;

    /// <summary>The innermost scope along the parent chain that declares <paramref name="name"/> visibly; null when none does.</summary>
    public Scope? DeclaringScope(string name)
    {
        var visible = int.MaxValue;
        for (var scope = this; scope != null; visible = scope.ParentVisibleCount, scope = scope.Parent)
        {
            if (scope.TryGetVisible(name, visible, out _))
            {
                return scope;
            }
        }

        return null;
    }

    /// <param name="parentVisibleCount">
    /// When set, only the first that many declarations of <paramref name="parent"/> are visible from this scope: a nested
    /// routine sees the enclosing routine's declarations up to its own definition (LANG-402).
    /// </param>
    public Scope(ScopeKind kind, Scope? parent, Symbol? owner = null, int? parentVisibleCount = null)
    {
        _symbols = new(IdentifierComparer.Instance);
        _ordered = [];
        _overloads = new(IdentifierComparer.Instance);
        Kind = kind;
        Parent = parent;
        Owner = owner;
        ParentVisibleCount = parentVisibleCount ?? int.MaxValue;
    }

    public ScopeKind Kind { get; }

    public Scope? Parent { get; }

    /// <summary>Unit, type or routine the scope belongs to; null for the built-in scope and plain blocks.</summary>
    public Symbol? Owner { get; }

    /// <summary>Number of declarations made so far (overloads included).</summary>
    public int Count => _ordered.Count;

    private int ParentVisibleCount { get; }

    /// <summary>Declared names in declaration order (declared spelling).</summary>
    public IEnumerable<string> Names => _ordered.Select(e => e.Name);

    /// <summary>Symbols in declaration order, one entry per declared name (an aliased symbol appears once per name).</summary>
    public IEnumerable<Symbol> Symbols => _ordered.Select(e => e.Symbol);

    public bool TryDeclare(Symbol symbol, out Symbol? existing) => TryDeclare(symbol.Name, symbol, out existing);

    /// <summary>Declares <paramref name="symbol"/> as <paramref name="name"/>; false (and the earlier symbol) if this scope already has the name.</summary>
    public bool TryDeclare(string name, Symbol symbol, out Symbol? existing)
    {
        if (_symbols.TryGetValue(name, out var entry))
        {
            existing = entry.Symbol;
            return false;
        }

        _symbols.Add(name, (symbol, _ordered.Count));
        _ordered.Add((name, symbol));
        existing = null;
        return true;
    }

    /// <summary>Adds <paramref name="routine"/> to the overload set of its name; the scope must already declare a routine of that name.</summary>
    public void AddOverload(RoutineSymbol routine)
    {
        if (!_overloads.TryGetValue(routine.Name, out var set))
        {
            var first = _symbols[routine.Name];
            set = [((RoutineSymbol)first.Symbol, first.Index)];
            _overloads.Add(routine.Name, set);
        }

        set.Add((routine, _ordered.Count));
        _ordered.Add((routine.Name, routine));
    }

    /// <summary>The routines of that name declared in this scope, in declaration order (empty when the name is something else).</summary>
    public ImmutableArray<RoutineSymbol> LookupRoutinesLocal(string name) => LookupRoutinesLocal(name, int.MaxValue);

    private ImmutableArray<RoutineSymbol> LookupRoutinesLocal(string name, int visible) =>
        _overloads.TryGetValue(name, out var set) ? [.. set.Where(e => e.Index < visible).Select(e => e.Routine)]
        : TryGetVisible(name, visible, out var symbol) && symbol is RoutineSymbol routine ? [routine]
        : [];

    /// <summary>
    /// LANG-441: the routines named <paramref name="name"/> in the innermost scope declaring it; while all of them carry
    /// <c>overload</c>, the next outer scope's routines of that name are added (a scope where the name is not a routine
    /// stops the search).
    /// </summary>
    public ImmutableArray<RoutineSymbol> LookupCandidates(string name)
    {
        var candidates = ImmutableArray.CreateBuilder<RoutineSymbol>();
        var visible = int.MaxValue;
        for (var scope = this; scope != null; visible = scope.ParentVisibleCount, scope = scope.Parent)
        {
            if (!scope.TryGetVisible(name, visible, out _))
            {
                continue;
            }

            var routines = scope.LookupRoutinesLocal(name, visible);
            candidates.AddRange(routines);
            if (routines.IsEmpty || routines.Any(r => !r.IsOverload))
            {
                break;
            }
        }

        return candidates.ToImmutable();
    }

    /// <summary>The symbol declared in this scope only.</summary>
    public Symbol? LookupLocal(string name) => _symbols.TryGetValue(name, out var entry) ? entry.Symbol : null;

    /// <summary>The innermost visible symbol of that name along the parent chain.</summary>
    public Symbol? Lookup(string name)
    {
        var visible = int.MaxValue;
        for (var scope = this; scope != null; visible = scope.ParentVisibleCount, scope = scope.Parent)
        {
            if (scope.TryGetVisible(name, visible, out var symbol))
            {
                return symbol;
            }
        }

        return null;
    }

    private bool TryGetVisible(string name, int visible, out Symbol? symbol)
    {
        if (_symbols.TryGetValue(name, out var entry) && entry.Index < visible)
        {
            symbol = entry.Symbol;
            return true;
        }

        symbol = null;
        return false;
    }
}
