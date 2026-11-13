using FreexComp.Compiler.Emit;

namespace FreexComp.Compiler.Types;

/// <summary>
/// Size, alignment and LLVM storage type (the in-memory form, CMP-305) of a type, per rtl-abi.md §2. Scalars only so far;
/// records, arrays, sets and classes are added with their milestones (C-052, C-053, C-080).
/// </summary>
internal sealed record TypeLayout(long Size, int Alignment, IrType StorageType)
{
    public static TypeLayout Of(TypeSymbol type)
    {
        IrType storage = type switch
        {
            IntegerTypeSymbol integer => IrType.Int(integer.Bits),
            BooleanTypeSymbol or CharTypeSymbol => IrType.Int(checked((int)type.Size * 8)),
            RealTypeSymbol { RealKind: RealKind.Single } => IrType.Float,
            RealTypeSymbol { RealKind: RealKind.Double } => IrType.Double,
            RealTypeSymbol { RealKind: RealKind.Currency } => IrType.I64,
            StringTypeSymbol { Variant: StringVariant.Short } => new ArrayIrType(type.Size, IrType.I8),
            StringTypeSymbol or PointerTypeSymbol => IrType.Ptr,
            TextTypeSymbol => new ArrayIrType(type.Size, IrType.I8),
            ErrorTypeSymbol => throw new InvalidOperationException("The error type has no layout."),
            _ => throw new NotSupportedException($"No layout for type kind {type.TypeKind} yet."),
        };
        return new TypeLayout(type.Size, type.Alignment, storage);
    }
}
