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
    public Symbol? ContainingSymbol { get; internal set; }

    /// <summary>
    /// The unit that declares this symbol; null for built-ins. A named pointer type has no containing symbol (C-366): its unit is
    /// that of <see cref="Types.PointerTypeSymbol.DeclaredIn"/> (C-398: also when read back from a <c>.pcu</c>).
    /// </summary>
    public UnitSymbol? ContainingUnit
    {
        get
        {
            for (var s = this is Types.PointerTypeSymbol { DeclaredIn: { } declaredIn } ? declaredIn : ContainingSymbol; s != null; s = s.ContainingSymbol)
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

    /// <summary>Doc comment text (LANG-142); filled when the unit is bound (C-243) or read from its <c>.pcu</c>.</summary>
    public string? Documentation { get; internal set; }

    public bool IsDeprecated { get => _isDeprecated; init => _isDeprecated = value; }

    private bool _isDeprecated;

    /// <summary>LANG-472 (C-127): the text of <c>deprecated 'text'</c>, if any.</summary>
    internal string? DeprecationMessage { get; set; }

    /// <summary>LANG-472 (C-127): the <c>platform</c> hint directive.</summary>
    internal bool IsPlatform { get; set; }

    /// <summary>LANG-472 (C-127): the <c>experimental</c> hint directive.</summary>
    internal bool IsExperimental { get; set; }

    /// <summary>LANG-472 (C-232): the hint directives of the declaration; null when it has none.</summary>
    internal SymbolHints? Hints
    {
        get => IsDeprecated || IsPlatform || IsExperimental ? new SymbolHints(IsDeprecated, DeprecationMessage, IsPlatform, IsExperimental) : null;
        set => (_isDeprecated, DeprecationMessage, IsPlatform, IsExperimental) =
            (value?.IsDeprecated == true, value?.DeprecationMessage, value?.IsPlatform == true, value?.IsExperimental == true);
    }

    /// <summary>The unit that declares the symbol (through its containers); null for a built-in.</summary>
    internal UnitSymbol? DeclaringUnit
    {
        get
        {
            for (var symbol = this; symbol is not null; symbol = symbol.ContainingSymbol)
            {
                if (symbol is UnitSymbol unit)
                {
                    return unit;
                }
            }

            return null;
        }
    }

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
    internal UnitSymbol(string name, bool isProgram, ImmutableArray<Location> locations, bool isLibrary = false)
        : base(SymbolKind.Unit, name, containingSymbol: null, locations)
    {
        IsProgram = isProgram;
        IsLibrary = isLibrary;
    }

    public ImmutableArray<Symbol> InterfaceMembers { get; internal set; } = [];

    public ImmutableArray<Symbol> ImplementationMembers { get; internal set; } = [];

    /// <summary>The main file of a compilation, not a unit: a program file or a library file (LANG-541).</summary>
    public bool IsProgram { get; }

    /// <summary>LANG-540: the main file is a library file (then <see cref="IsProgram"/> is true as well: it follows the rules of a program file).</summary>
    internal bool IsLibrary { get; }

    /// <summary>LANG-545…549: the exports of a library file in ordinal order (set by the export binder); empty otherwise.</summary>
    internal ImmutableArray<ExportInfo> Exports { get; set; } = [];

    /// <summary>The location of the export item of each entry of <see cref="Exports"/>, by export name (emit-time diagnostics, C-293).</summary>
    internal IReadOnlyDictionary<string, Text.Location> ExportLocations { get; set; } = new Dictionary<string, Text.Location>();

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

    /// <summary>
    /// C-268 (debug-info-sidecar.md §4): the plain type aliases (<c>type T = U</c>) declared at the unit's level, in declaration
    /// order. Only a record for the sidecar: the alias is no symbol and the same type as its target (LANG-205).
    /// </summary>
    internal List<TypeAlias> TypeAliases { get; } = [];

    public override string ToDisplayString() => (IsLibrary ? "library " : IsProgram ? "program " : "unit ") + Name;
}

/// <summary>
/// C-268: a plain type alias of a unit — its name, the type it denotes (<see cref="Target"/>, identical to the target's symbol) and,
/// for an alias of an earlier alias of the same unit, that alias (<see cref="Of"/>).
/// </summary>
internal sealed record TypeAlias(string Name, Types.TypeSymbol Target, TypeAlias? Of, Location Location);

/// <summary>
/// LANG-472: the hint directives of a declaration — <c>deprecated ['text']</c>, <c>platform</c>, <c>experimental</c>. Variables,
/// constants, routines and methods carry them on their symbol; a type carries them on its declared name (<see cref="Scope.SetHints"/>),
/// because <c>type TOld = TNew deprecated</c> declares no symbol of its own (LANG-205).
/// </summary>
internal sealed record SymbolHints(bool IsDeprecated, string? DeprecationMessage, bool IsPlatform, bool IsExperimental);
