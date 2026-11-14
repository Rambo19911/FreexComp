using System.Collections.Immutable;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Symbols;

/// <summary>
/// A record field (api.md §5) or a class instance field (LANG-615): its type and byte offset in the record (ABI-060) or in the
/// instance (ABI §7.1, from the object reference).
/// </summary>
public sealed class FieldSymbol : Symbol
{
    internal FieldSymbol(string name, TypeSymbol type, long offset, TypeSymbol containingType, ImmutableArray<Location> locations,
        Visibility visibility = Visibility.Public)
        : base(SymbolKind.Field, name, containingType, locations, visibility)
    {
        Type = type;
        Offset = offset;
    }

    public TypeSymbol Type { get; }

    public long Offset { get; }

    /// <summary>Class variables (<c>class var</c>) are not modelled yet (E2900).</summary>
    public bool IsClassVar => false;

    public override string ToDisplayString() => $"{Name}: {Type.Name}";
}
