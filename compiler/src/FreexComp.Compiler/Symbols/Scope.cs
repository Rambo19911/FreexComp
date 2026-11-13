using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Symbols;

/// <summary>Scope kinds of architecture.md §4; lookup order is LANG-310 (the binder chains the scopes in that order).</summary>
internal enum ScopeKind { BuiltIn, Unit, Uses, TypeMember, Routine, Block, With }

/// <summary>
/// One level of the chained name lookup. Names compare per LANG-110 (ASCII letters case-insensitive). A name binds
/// one symbol per scope; an alias declares an existing symbol under another name.
/// </summary>
internal sealed class Scope
{
    private readonly Dictionary<string, Symbol> _symbols = new(IdentifierComparer.Instance);
    private readonly List<(string Name, Symbol Symbol)> _ordered = [];

    public Scope(ScopeKind kind, Scope? parent, Symbol? owner = null)
    {
        Kind = kind;
        Parent = parent;
        Owner = owner;
    }

    public ScopeKind Kind { get; }

    public Scope? Parent { get; }

    /// <summary>Unit, type or routine the scope belongs to; null for the built-in scope and plain blocks.</summary>
    public Symbol? Owner { get; }

    /// <summary>Declared names in declaration order (declared spelling).</summary>
    public IEnumerable<string> Names => _ordered.Select(e => e.Name);

    /// <summary>Symbols in declaration order, one entry per declared name (an aliased symbol appears once per name).</summary>
    public IEnumerable<Symbol> Symbols => _ordered.Select(e => e.Symbol);

    public bool TryDeclare(Symbol symbol, out Symbol? existing) => TryDeclare(symbol.Name, symbol, out existing);

    /// <summary>Declares <paramref name="symbol"/> as <paramref name="name"/>; false (and the earlier symbol) if this scope already has the name.</summary>
    public bool TryDeclare(string name, Symbol symbol, out Symbol? existing)
    {
        if (_symbols.TryGetValue(name, out existing))
        {
            return false;
        }

        _symbols.Add(name, symbol);
        _ordered.Add((name, symbol));
        existing = null;
        return true;
    }

    /// <summary>The symbol declared in this scope only.</summary>
    public Symbol? LookupLocal(string name) => _symbols.GetValueOrDefault(name);

    /// <summary>The innermost symbol of that name along the parent chain.</summary>
    public Symbol? Lookup(string name)
    {
        for (var scope = this; scope != null; scope = scope.Parent)
        {
            if (scope._symbols.TryGetValue(name, out var symbol))
            {
                return symbol;
            }
        }

        return null;
    }
}
