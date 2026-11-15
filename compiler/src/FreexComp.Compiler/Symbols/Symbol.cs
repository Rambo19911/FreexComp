using System.Collections.Immutable;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Symbols;

public enum SymbolKind { Unit, Constant, Variable, Parameter, Field, Property, Routine, Method, Type,
                         EnumMember, TypeParameter, Label, Local, Intrinsic }

public enum Visibility { StrictPrivate, Private, StrictProtected, Protected, Public, Published }

/// <summary>A declared entity (api.md §5). Symbols are immutable once the declaration binder has finished them.</summary>
public abstract class Symbol
{
    private protected Symbol(
        SymbolKind kind,
        string name,
        Symbol? containingSymbol,
        ImmutableArray<Location> locations,
        Visibility visibility = Visibility.Public)
    {
        Kind = kind;
        Name = name;
        ContainingSymbol = containingSymbol;
        Locations = locations.IsDefault ? [] : locations;
        Visibility = visibility;
    }

    public SymbolKind Kind { get; }

    /// <summary>Declared casing.</summary>
    public string Name { get; }

    /// <summary>Unit, type or routine; null for built-ins.</summary>
    public Symbol? ContainingSymbol { get; }

    public UnitSymbol? ContainingUnit
    {
        get
        {
            for (var s = ContainingSymbol; s != null; s = s.ContainingSymbol)
            {
                if (s is UnitSymbol unit)
                {
                    return unit;
                }
            }

            return null;
        }
    }

    /// <summary>Declaration (and forward declaration) sites; empty for built-ins.</summary>
    public ImmutableArray<Location> Locations { get; private set; }

    /// <summary>Adds a later declaration site (the definition of a forward or interface routine).</summary>
    internal void AddLocation(Location location) => Locations = Locations.Add(location);

    public Visibility Visibility { get; }

    /// <summary>
    /// Binder bookkeeping for H2003 (C-096): a <c>private</c>/<c>strict private</c> class member has been named by an
    /// access (allowed or not) in its unit. Only set, never cleared; not part of the declared symbol.
    /// </summary>
    internal bool IsReferenced { get; set; }

    /// <summary>Doc comment text (LANG-142); filled by the declaration binder.</summary>
    public string? Documentation { get; init; }

    public bool IsDeprecated { get; init; }

    /// <summary>"Unit1.TFoo.Bar"; the bare name for symbols without a container.</summary>
    public string QualifiedName => QualifiedNameOverride ?? (ContainingSymbol is null ? Name : ContainingSymbol.QualifiedName + "." + Name);

    /// <summary>C-116 (CMP-360): a generic instance is qualified with its canonical argument names (<c>U.TList&lt;Integer&gt;</c>).</summary>
    internal string? QualifiedNameOverride { get; set; }

    /// <summary>Quick-info form of CMP-731.</summary>
    public virtual string ToDisplayString() => Name;

    public override string ToString() => ToDisplayString();
}

/// <summary>A program or unit (api.md §5); members are filled by the declaration binder.</summary>
public sealed class UnitSymbol : Symbol
{
    internal UnitSymbol(string name, bool isProgram, ImmutableArray<Location> locations)
        : base(SymbolKind.Unit, name, containingSymbol: null, locations)
    {
        IsProgram = isProgram;
    }

    public ImmutableArray<Symbol> InterfaceMembers { get; internal set; } = [];

    public ImmutableArray<Symbol> ImplementationMembers { get; internal set; } = [];

    public bool IsProgram { get; }

    /// <summary>
    /// End of the last field of the unit's threadvar record so far (ABI-180; filled while the var sections are bound, or
    /// from the <c>.pcu</c>); the descriptor's <c>Size</c> is this rounded up to 16.
    /// </summary>
    internal long ThreadVarSize { get; set; }

    /// <summary>
    /// C-116 (CMP-385): the generic instances this unit uses, in first-use order; the unit binds their method bodies and emits
    /// them (<c>linkonce_odr</c>). Guarded by its own lock (instances are made while units are bound in parallel).
    /// </summary>
    internal List<Symbol> UsedInstances { get; } = [];

    /// <summary>C-222 (CMP-508): where this unit first used each of <see cref="UsedInstances"/> ("instantiated from here"); same lock.</summary>
    internal Dictionary<Symbol, Location> InstanceSites { get; } = [];

    /// <summary>CMP-380 (C-120): the closure classes of the anonymous methods bound for this unit, in creation order; own lock.</summary>
    internal List<Types.ClassTypeSymbol> Closures { get; } = [];

    /// <summary>C-121: the forwarder makers of this unit by method reference type and "of object" (guarded by the lock of <see cref="Closures"/>).</summary>
    internal Dictionary<(Types.TypeSymbol Type, bool OfObject), RoutineSymbol> Forwarders { get; } = [];

    public override string ToDisplayString() => (IsProgram ? "program " : "unit ") + Name;
}
