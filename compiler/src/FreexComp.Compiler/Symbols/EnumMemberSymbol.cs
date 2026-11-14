using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Symbols;

/// <summary>An element of an enumeration (LANG-210): a constant of the enumeration type.</summary>
public sealed class EnumMemberSymbol : Symbol
{
    internal EnumMemberSymbol(string name, EnumTypeSymbol type, long ordinal, ImmutableArray<Location> locations)
        : base(SymbolKind.EnumMember, name, type, locations)
    {
        Type = type;
        Ordinal = ordinal;
    }

    public TypeSymbol Type { get; }

    public long Ordinal { get; }

    /// <summary>The element as a constant of its enumeration type.</summary>
    internal ConstantValue Value => new(new BigInteger(Ordinal), Type);

    public override string ToDisplayString() => $"{Type.Name}.{Name} = {Ordinal}";
}
