using System.Collections.Immutable;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Symbols;

/// <summary>
/// A property of a class (LANG-641…647): reads and writes compile to its accessors. A field accessor is a path
/// (<see cref="ReadPath"/>/<see cref="WritePath"/>): an instance field (or, for a class property, a <c>class var</c>) followed by
/// record fields (<c>read FRect.Left</c>); a method accessor is <see cref="ReadMethod"/>/<see cref="WriteMethod"/>. Index
/// parameters of an array property come first in accessor calls, then <see cref="Index"/> (LANG-643), then the written value.
/// </summary>
public sealed class PropertySymbol : Symbol
{
    internal PropertySymbol(string name, TypeSymbol containingType, ImmutableArray<Location> locations, Visibility visibility)
        : base(SymbolKind.Property, name, containingType, locations, visibility)
    {
    }

    /// <summary>The declaring class; never read it for an interface property (<see cref="ContainingInterface"/>).</summary>
    public ClassTypeSymbol ContainingType => (ClassTypeSymbol)ContainingSymbol!;

    /// <summary>The declaring interface of an interface property (LANG-660, C-101): its accessors are interface methods.</summary>
    public InterfaceTypeSymbol? ContainingInterface => ContainingSymbol as InterfaceTypeSymbol;

    public TypeSymbol Type { get; internal set; } = null!;

    /// <summary>The index parameters of an array property (LANG-642); empty otherwise.</summary>
    public ImmutableArray<ParameterSymbol> Parameters { get; internal set; } = [];

    public bool IsArrayProperty => !Parameters.IsEmpty;

    /// <summary>The <c>index N</c> value (LANG-643); null without the specifier.</summary>
    public int? Index { get; internal set; }

    /// <summary><c>class property</c> (LANG-647).</summary>
    public bool IsClassProperty { get; internal set; }

    /// <summary><c>; default;</c> on an array property (LANG-642).</summary>
    public bool IsDefault { get; internal set; }

    /// <summary>The field path of a <c>read</c> field accessor; empty otherwise.</summary>
    public ImmutableArray<Symbol> ReadPath { get; internal set; } = [];

    public MethodSymbol? ReadMethod { get; internal set; }

    /// <summary>The field path of a <c>write</c> field accessor; empty otherwise.</summary>
    public ImmutableArray<Symbol> WritePath { get; internal set; } = [];

    public MethodSymbol? WriteMethod { get; internal set; }

    public bool IsReadable => ReadMethod is not null || !ReadPath.IsEmpty;

    public bool IsWritable => WriteMethod is not null || !WritePath.IsEmpty;

    /// <summary><c>property Name: Type</c> (CMP-731).</summary>
    public override string ToDisplayString() =>
        (IsClassProperty ? "class " : "") + "property " + Name
        + (Parameters.IsEmpty ? "" : "[" + string.Join("; ", Parameters.Select(p => $"{ParameterSymbol.Prefix(p.Mode)}{p.Name}: {p.Type.Name}")) + "]")
        + ": " + Type.Name;
}
