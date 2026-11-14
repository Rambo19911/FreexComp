using System.Collections.Immutable;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Symbols;

/// <summary>A record field (api.md §5): its type and byte offset in the record (ABI-060).</summary>
public sealed class FieldSymbol : Symbol
{
    internal FieldSymbol(string name, TypeSymbol type, long offset, RecordTypeSymbol record, ImmutableArray<Location> locations)
        : base(SymbolKind.Field, name, record, locations)
    {
        Type = type;
        Offset = offset;
    }

    public TypeSymbol Type { get; }

    public long Offset { get; }

    /// <summary>Class variables arrive with classes (M2.4).</summary>
    public bool IsClassVar => false;

    public override string ToDisplayString() => $"{Name}: {Type.Name}";
}
