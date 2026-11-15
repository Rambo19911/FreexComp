using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Emit;

/// <summary>
/// C-198: the memory image of a typed constant of a record with a variant part (CMP-306 byte storage): every given field
/// written at its offset, little-endian; omitted fields and padding zero. Only constants without addresses have an image
/// (ordinals, Booleans, reals other than Currency, sets, short strings, and records and static arrays of those).
/// </summary>
internal static class ConstantBytes
{
    /// <summary>The image of <paramref name="constant"/> of <paramref name="type"/>; null when some part has none.</summary>
    public static byte[]? Of(ConstantValue constant, TypeSymbol type)
    {
        var buffer = new byte[type.Size];
        return TryWrite(constant, type, buffer, 0) ? buffer : null;
    }

    /// <summary>The first type in <paramref name="constant"/> without a byte image, or null.</summary>
    public static TypeSymbol? Unencodable(ConstantValue constant, TypeSymbol type) => type switch
    {
        RecordTypeSymbol record when constant.Value is ImmutableArray<ConstantValue?> fields =>
            fields.Select((f, i) => f is null ? null : Unencodable(f, record.Fields[i].Type)).FirstOrDefault(t => t is not null),
        ArrayTypeSymbol array when constant.Value is ImmutableArray<ConstantValue?> elements =>
            elements.Select(e => e is null ? null : Unencodable(e, array.ElementType)).FirstOrDefault(t => t is not null),
        _ => TryWrite(constant, type, new byte[Math.Max(type.Size, 1)], 0) ? null : type,
    };

    private static bool TryWrite(ConstantValue constant, TypeSymbol type, byte[] buffer, long offset)
    {
        switch (type, constant.Value)
        {
            case (RecordTypeSymbol record, ImmutableArray<ConstantValue?> fields):
                for (var i = 0; i < fields.Length; i++)
                {
                    if (fields[i] is { } field && !TryWrite(field, record.Fields[i].Type, buffer, offset + record.Fields[i].Offset))
                    {
                        return false;
                    }
                }

                return true;
            case (ArrayTypeSymbol array, ImmutableArray<ConstantValue?> elements):
                for (var i = 0; i < elements.Length; i++)
                {
                    if (elements[i] is { } element && !TryWrite(element, array.ElementType, buffer, offset + i * array.ElementType.Size))
                    {
                        return false;
                    }
                }

                return true;
            case (BooleanTypeSymbol boolean, var value) when value is bool or BigInteger:
                var truth = value is bool b ? b : !((BigInteger)value).IsZero;
                return Integer(truth ? boolean.IsCStyle ? BigInteger.MinusOne : BigInteger.One : BigInteger.Zero, type.Size, buffer, offset);
            case (IntegerTypeSymbol or CharTypeSymbol or EnumTypeSymbol or SetTypeSymbol, BigInteger ordinal):
                return Integer(ordinal, type.Size, buffer, offset);
            case (RealTypeSymbol { RealKind: RealKind.Single }, BigInteger or double):
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan((int)offset), (float)Real(constant.Value));
                return true;
            case (RealTypeSymbol { RealKind: RealKind.Currency }, BigInteger or double):                 // C-218
                return Integer(IrEmitter.CurrencyScaled(Real(constant.Value)), 8, buffer, offset);
            case (RealTypeSymbol { RealKind: RealKind.Double }, BigInteger or double):
                BinaryPrimitives.WriteDoubleLittleEndian(buffer.AsSpan((int)offset), Real(constant.Value));
                return true;
            case (StringTypeSymbol { Variant: StringVariant.Short } shortString, string text):
                var bytes = Strings.ShortBytes(text, shortString.MaxLength);
                buffer[offset] = (byte)bytes.Length;
                bytes.CopyTo(buffer, (int)offset + 1);
                return true;
            default:
                return false;
        }
    }

    private static double Real(object? value) => value is double d ? d : (double)(BigInteger)value!;

    private static bool Integer(BigInteger value, long size, byte[] buffer, long offset)
    {
        for (var i = 0; i < size; i++)
        {
            buffer[offset + i] = (byte)(value & 0xFF);
            value >>= 8;
        }

        return true;
    }
}
