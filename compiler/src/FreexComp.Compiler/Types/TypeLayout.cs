using System.Collections.Immutable;
using FreexComp.Compiler.Emit;

namespace FreexComp.Compiler.Types;

/// <summary>
/// Size, alignment and LLVM storage type (the in-memory form, CMP-305) of a type, per rtl-abi.md §2: scalars, sets, static
/// arrays, records (CMP-306); classes are added with their milestone (C-080).
/// </summary>
internal sealed record TypeLayout(long Size, int Alignment, IrType StorageType)
{
    public static TypeLayout Of(TypeSymbol type)
    {
        if (type is OpenArrayTypeSymbol openArray)
        {
            return Pointer(openArray.Target);                               // ABI-080: the data pointer (C-106)
        }

        IrType storage = type switch
        {
            IntegerTypeSymbol integer => IrType.Int(integer.Bits),
            BooleanTypeSymbol or CharTypeSymbol or EnumTypeSymbol => IrType.Int(checked((int)type.Size * 8)),
            RealTypeSymbol { RealKind: RealKind.Single } => IrType.Float,
            RealTypeSymbol { RealKind: RealKind.Double } => IrType.Double,
            RealTypeSymbol { RealKind: RealKind.Currency } => IrType.I64,
            StringTypeSymbol { Variant: StringVariant.Short } => new ArrayIrType(type.Size, IrType.I8),
            StringTypeSymbol or PointerTypeSymbol or DynamicArrayTypeSymbol or ProceduralTypeSymbol => IrType.Ptr,     // CMP-378: a code address
            ClassTypeSymbol or ClassRefTypeSymbol => IrType.Ptr,            // ABI-071: the instance / VMT address
            InterfaceTypeSymbol => IrType.Ptr,                              // ABI-090: the hidden field's address
            TextTypeSymbol => new ArrayIrType(type.Size, IrType.I8),
            UntypedTypeSymbol => new ArrayIrType(0, IrType.I8),
            MethodPointerTypeSymbol => new StructIrType([IrType.Ptr, IrType.Ptr]),  // ABI §2: { Code, Data } (C-103)            // C-202: only ever reached through its address
            SetTypeSymbol { IsSmall: true } => IrType.Int(checked((int)type.Size * 8)),
            SetTypeSymbol => new ArrayIrType(type.Size, IrType.I8),          // big set (rtl-abi.md §5, CMP-336)
            ArrayTypeSymbol array => new ArrayIrType(array.Length, Of(array.ElementType).StorageType),   // CMP-308, ABI-062
            RecordTypeSymbol { HasVariantPart: true } => new ArrayIrType(type.Size, IrType.I8),     // CMP-306: byte storage (C-108)
            RecordTypeSymbol record => record.IsAnonymous ? Record(record).Body : new NamedStructIrType(Mangler.RecordType(record), Record(record).Body),
            ErrorTypeSymbol => throw new InvalidOperationException("The error type has no layout."),
            _ => throw new NotSupportedException($"No layout for type kind {type.TypeKind} yet."),
        };
        return new TypeLayout(type.Size, type.Alignment, storage);
    }

    /// <summary>A code or data address on <paramref name="target"/>.</summary>
    public static TypeLayout Pointer(TargetInfo target) => new(target.PointerSize, target.PointerAlign, IrType.Ptr);

    /// <summary><c>NativeInt</c> / <c>NativeUInt</c> on <paramref name="target"/>: <c>i&lt;PointerSize*8&gt;</c> (CMP-1007).</summary>
    public static TypeLayout NativeInt(TargetInfo target) =>
        new(target.PointerSize, target.PointerSize == 8 ? target.Int64Align : target.PointerSize, IrType.Int(target.PointerSize * 8));

    /// <summary>
    /// The layout of a variable's own storage (alloca, global, frame field): <see cref="Of"/>, except that a type of size 0 (an
    /// empty record) occupies one byte, <c>[1 x i8]</c>, so that distinct variables have distinct addresses (ABI-060).
    /// </summary>
    public static TypeLayout Allocated(TypeSymbol type) =>
        type.Size == 0 ? new TypeLayout(1, Math.Max(1, type.Alignment), new ArrayIrType(1, IrType.I8)) : Of(type);

    /// <summary>The struct index of each field of <paramref name="record"/> (padding fields shift them, CMP-307).</summary>
    public static ImmutableArray<int> FieldIndexes(RecordTypeSymbol record) => Record(record).FieldIndexes;

    /// <summary>
    /// CMP-306: the fields' storage types in order. When LLVM's natural layout of that struct gives every ABI-060 offset and
    /// the ABI size, it is the plain struct; otherwise (an <c>{$ALIGN}</c> below a field's alignment, a nested record whose
    /// LLVM form is packed) a packed struct with explicit <c>[N x i8]</c> padding before fields and at the end. A
    /// <c>packed record</c> is always the packed struct (it never needs padding).
    /// </summary>
    private static (StructIrType Body, ImmutableArray<int> FieldIndexes) Record(RecordTypeSymbol record) =>
        Struct(record.Target, [.. record.Fields.Select(f => Of(f.Type).StorageType)], [.. record.Fields.Select(f => f.Offset)],
            [.. record.Fields.Select(f => f.Type.Size)], record.Size, record.IsPacked);

    /// <summary>
    /// A struct holding fields of the given layouts at their ABI-060 offsets (alignment cap 8; frame records, CMP-345) with
    /// the struct index of each field: the plain struct when LLVM lays it out the same way, else packed with padding.
    /// </summary>
    public static (StructIrType Body, ImmutableArray<int> FieldIndexes) Struct(TargetInfo target, IReadOnlyList<TypeLayout> fields)
    {
        var (offsets, size) = StructOffsets(fields);
        return Struct(target, [.. fields.Select(f => f.StorageType)], offsets, [.. fields.Select(f => f.Size)], size, packed: false);
    }

    /// <summary>The ABI-060 byte offset of each field of <see cref="Struct(IReadOnlyList{TypeLayout})"/> (alignment cap 8) and the struct's size.</summary>
    public static (ImmutableArray<long> Offsets, long Size) StructOffsets(IReadOnlyList<TypeLayout> fields)
    {
        long end = 0;
        var alignment = 1;
        var offsets = new long[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            var fieldAlignment = Math.Min(fields[i].Alignment, 8);
            alignment = Math.Max(alignment, fieldAlignment);
            offsets[i] = (end + fieldAlignment - 1) / fieldAlignment * fieldAlignment;
            end = offsets[i] + fields[i].Size;
        }

        return ([.. offsets], (end + alignment - 1) / alignment * alignment);
    }

    private static (StructIrType Body, ImmutableArray<int> FieldIndexes) Struct(TargetInfo target, ImmutableArray<IrType> types, ImmutableArray<long> offsets,
        ImmutableArray<long> sizes, long size, bool packed)
    {
        var plain = new StructIrType(types);
        if (!packed && NaturalOffsets(target, plain).SequenceEqual(offsets) && LlvmSize(target, plain) == size)
        {
            return (plain, [.. Enumerable.Range(0, types.Length)]);
        }

        var fields = ImmutableArray.CreateBuilder<IrType>();
        var indexes = ImmutableArray.CreateBuilder<int>();
        long end = 0;
        for (var i = 0; i < types.Length; i++)
        {
            if (offsets[i] > end)
            {
                fields.Add(new ArrayIrType(offsets[i] - end, IrType.I8));
            }

            indexes.Add(fields.Count);
            fields.Add(types[i]);
            end = offsets[i] + sizes[i];
        }

        if (size > end)
        {
            fields.Add(new ArrayIrType(size - end, IrType.I8));
        }

        return (new StructIrType(fields.ToImmutable(), IsPacked: true), indexes.ToImmutable());
    }

    /// <summary>Field offsets LLVM gives a non-packed struct (each field at the next multiple of its LLVM ABI alignment).</summary>
    private static IEnumerable<long> NaturalOffsets(TargetInfo target, StructIrType type)
    {
        long end = 0;
        foreach (var field in type.Fields)
        {
            var alignment = LlvmAlignment(target, field);
            var offset = (end + alignment - 1) / alignment * alignment;
            yield return offset;
            end = offset + LlvmSize(target, field);
        }
    }

    /// <summary>The back end's ABI alignment of a storage type on <paramref name="target"/> (packed structs: 1).</summary>
    internal static int LlvmAlignment(TargetInfo target, IrType type) => type switch
    {
        IntegerIrType { Bits: >= 64 } => target.Int64Align,
        IntegerIrType i => Math.Max(1, i.Bits / 8),
        FloatingIrType { Kind: FloatingKind.Float } => 4,
        FloatingIrType => target.Float64Align,
        PointerIrType => target.PointerAlign,
        ArrayIrType a => LlvmAlignment(target, a.Element),
        StructIrType { IsPacked: true } => 1,
        StructIrType s => s.Fields.Select(f => LlvmAlignment(target, f)).DefaultIfEmpty(1).Max(),
        NamedStructIrType { Body: { } body } => LlvmAlignment(target, body),
        _ => throw new InvalidOperationException($"No LLVM alignment for {type}."),
    };

    /// <summary>The byte offset of each field of the non-packed struct <paramref name="type"/> on <paramref name="target"/> and its size (CMP-1007).</summary>
    internal static (ImmutableArray<long> Offsets, long Size) NaturalLayout(TargetInfo target, StructIrType type) =>
        ([.. NaturalOffsets(target, type)], LlvmSize(target, type));

    /// <summary>The back end's allocation size of a storage type (structs rounded up to their alignment).</summary>
    internal static long LlvmSize(TargetInfo target, IrType type)
    {
        switch (type)
        {
            case IntegerIrType i:
                return Math.Max(1, i.Bits / 8);
            case FloatingIrType { Kind: FloatingKind.Float }:
                return 4;
            case FloatingIrType:
                return 8;
            case PointerIrType:
                return target.PointerSize;
            case ArrayIrType a:
                return a.Length * LlvmSize(target, a.Element);
            case StructIrType { IsPacked: true } packed:
                return packed.Fields.Sum(f => LlvmSize(target, f));
            case StructIrType s:
            {
                var end = s.Fields.IsEmpty ? 0 : NaturalOffsets(target, s).Last() + LlvmSize(target, s.Fields[^1]);
                var alignment = LlvmAlignment(target, s);
                return (end + alignment - 1) / alignment * alignment;
            }

            case NamedStructIrType { Body: { } body }:
                return LlvmSize(target, body);
            default:
                throw new InvalidOperationException($"No LLVM size for {type}.");
        }
    }
}
