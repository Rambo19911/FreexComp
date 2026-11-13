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
    private readonly Dictionary<string, (Symbol Symbol, int Index)> _symbols = new(IdentifierComparer.Instance);
    private readonly List<(string Name, Symbol Symbol)> _ordered = [];
    private readonly Dictionary<string, List<(RoutineSymbol Routine, int Index)>> _overloads = new(IdentifierComparer.Instance);

    /// <param name="parentVisibleCount">
    /// When set, only the first that many declarations of <paramref name="parent"/> are visible from this scope: a nested
    /// routine sees the enclosing routine's declarations up to its own definition (LANG-402).
    /// </param>
    public Scope(ScopeKind kind, Scope? parent, Symbol? owner = null, int? parentVisibleCount = null)
    {
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
