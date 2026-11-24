using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary><c>System.TGUID</c> (ABI-096) in the binder: its recognition and its constant values (LANG-680, 681; C-299).</summary>
internal static class GuidValues
{
    /// <summary>True for the record <c>TGUID</c> declared in <c>System</c> (16 bytes).</summary>
    public static bool IsGuidType(TypeSymbol? type) =>
        type is RecordTypeSymbol { Size: 16 } record && IdentifierComparer.Instance.Equals(record.Name, "TGUID")
        && record.ContainingUnit is { } unit && IdentifierComparer.Instance.Equals(unit.Name, "System");

    /// <summary><c>'{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}'</c> (LANG-660) as the 16 bytes of a <c>TGUID</c> in memory order; null when malformed.</summary>
    public static ImmutableArray<byte>? Parse(string text) =>
        text.Length == 38 && Guid.TryParseExact(text, "B", out var guid) ? [.. guid.ToByteArray()] : null;

    /// <summary>
    /// The record constant of <paramref name="guidType"/> with the bytes <paramref name="bytes"/>: every field (an unsigned integer, or
    /// an array of bytes) takes the little-endian value at its offset. Null when the record has another shape.
    /// </summary>
    public static ConstantValue? Constant(RecordTypeSymbol guidType, ImmutableArray<byte> bytes)
    {
        var values = ImmutableArray.CreateBuilder<ConstantValue?>(guidType.Fields.Length);
        foreach (var field in guidType.Fields)
        {
            var offset = (int)field.Offset;
            switch (field.Type)
            {
                case IntegerTypeSymbol integer when offset + (int)integer.Size <= bytes.Length:
                    values.Add(new ConstantValue(new System.Numerics.BigInteger(bytes.AsSpan(offset, (int)integer.Size), isUnsigned: true), integer));
                    break;
                case ArrayTypeSymbol { ElementType: IntegerTypeSymbol { Size: 1 } element } array when offset + (int)array.Size <= bytes.Length:
                    values.Add(new ConstantValue(
                        bytes.AsSpan(offset, (int)array.Size).ToArray().Select(b => (ConstantValue?)new ConstantValue(new System.Numerics.BigInteger(b), element)).ToImmutableArray(),
                        array));
                    break;
                default:
                    return null;
            }
        }

        return new ConstantValue(values.ToImmutable(), guidType);
    }
}
