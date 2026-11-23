using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Values;

/// <summary>
/// Renders decoded values as text (Pascal values §2–§8). Pure: everything it needs was read by
/// <see cref="ValueDecoder"/>.
/// </summary>
internal sealed class ValueFormatter(ValueFormatOptions options)
{
    /// <summary>Characters of a string inside another value's summary (DBG-174).</summary>
    public const int NestedStringChars = 32;

    private const string Ellipsis = "…";
    private const int MaxOpaqueBytes = 16;

    /// <summary>The node's one-line summary: the value column of Locals and Watches.</summary>
    public string Format(ValueNode node) => Render(node, nested: false);

    /// <summary>The form used inside another value's summary (DBG-174).</summary>
    public string FormatNested(ValueNode node) => Render(node, nested: true);

    /// <param name="node">Decoded value.</param>
    /// <param name="children">Handle for the node's children; ignored for a leaf.</param>
    /// <param name="canWriteMemory">The adapter can modify memory (DBG-176).</param>
    public VariableInfo ToVariableInfo(ValueNode node, VariableHandle? children, bool canWriteMemory) =>
        new(node.Name, Format(node), node.TypeName, node.Kind,
            node.ChildCount > 0 ? children : null,
            node.ChildCount,
            canWriteMemory && node.IsModifiableKind,
            node.Kind is ValueKind.Raw or ValueKind.Error ? null : node.EvaluateName,
            node.Address);

    private string Render(ValueNode node, bool nested)
    {
        var type = node.Type;
        switch (node.Kind)
        {
            case ValueKind.Error:
                return node.Error ?? "";
            case ValueKind.Integer:
                return options.HexIntegers ? "$" + Hex(node.Bytes) : Integer(type, node.Bytes);
            case ValueKind.Float:
                return Float(type, node.Bytes);
            case ValueKind.Boolean:
                return Boolean(type, node.Bytes);
            case ValueKind.Char:
            case ValueKind.Enum:
                return Ordinal(type, (long)Unsigned(node.Bytes));
            case ValueKind.Set:
                return Set(type, node.Bytes);
            case ValueKind.String when type.Flavor is PdiStringFlavor.PChar or PdiStringFlavor.PAnsiChar:
                return node.Pointer == 0 ? "nil" : Messages.Address(node.Pointer) + " " + Text(node, nested);
            case ValueKind.String:
                return Text(node, nested);
            case ValueKind.StaticArray or ValueKind.DynamicArray:
                if (node.Text is not null)
                    return Text(node, nested);
                if (node.Length == 0)
                    return "()";
                if (nested)
                    return "(" + Ellipsis + ")";
                var elided = node.Length > node.SummaryChildren.Count;
                return "(" + string.Join(", ", node.SummaryChildren.Select(FormatNested)) + (elided ? ", " + Ellipsis + ")" : ")")
                    + (elided ? LengthSuffix(node.Length) : "");
            case ValueKind.Record:
                if (node.ChildCount == 0)
                    return "()";
                if (nested)
                    return "(" + Ellipsis + ")";
                return "(" + string.Join("; ", node.SummaryChildren.Select(child => child.Name + ": " + FormatNested(child)))
                    + (node.ChildCount > node.SummaryChildren.Count ? "; " + Ellipsis + ")" : ")");
            case ValueKind.ClassInstance:
                return node.Pointer == 0
                    ? "nil"
                    : node.Text + "(" + Messages.Address(node.Pointer) + ")" + (node.InvalidInstance ? " {invalid instance}" : "");
            case ValueKind.ClassReference:
                return node.Pointer == 0 ? "nil" : node.Text ?? "";
            case ValueKind.Procedural when type.Method:
            {
                var code = Unsigned(node.Bytes.AsSpan(0, node.Bytes.Length / 2));
                var data = Unsigned(node.Bytes.AsSpan(node.Bytes.Length / 2));
                return code == 0 && data == 0 ? "nil" : "(Code: " + Messages.Address(code) + "; Data: " + Messages.Address(data) + ")";
            }

            case ValueKind.Pointer or ValueKind.Interface or ValueKind.Procedural:
                return node.Pointer == 0 ? "nil" : Messages.Address(node.Pointer);
            default:
                return Opaque(node.Bytes, type.Size);
        }
    }

    // ---- strings (DBG-160) ----

    private string Text(ValueNode node, bool nested)
    {
        var text = node.Text ?? "";
        var limit = Math.Max(options.MaxStringChars, 0);
        var shown = Math.Min(text.Length, nested ? Math.Min(limit, NestedStringChars) : limit);
        var cut = node.Partial || node.Capped || shown < text.Length;
        // The length is appended when the value is longer than the display limit, not for a nested value and not
        // when only the read was incomplete.
        var withLength = !nested && node.Length >= 0 && (node.Capped || text.Length > limit);
        return StringLiteral(text.AsSpan(0, shown)) + (cut ? Ellipsis : "") + (withLength ? LengthSuffix(node.Length) : "");
    }

    private static string LengthSuffix(long length) => " (Length: " + length.ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>
    /// Runs of printable code units in quotes (apostrophes doubled), every other unit as <c>#n</c>; a valid surrogate
    /// pair is printable, a lone surrogate is not.
    /// </summary>
    public static string StringLiteral(ReadOnlySpan<char> units)
    {
        if (units.IsEmpty)
            return "''";
        var result = new StringBuilder(units.Length + 2);
        var quoted = false;
        for (var i = 0; i < units.Length; i++)
        {
            var unit = units[i];
            var pair = char.IsHighSurrogate(unit) && i + 1 < units.Length && char.IsLowSurrogate(units[i + 1]);
            if (pair || IsPrintable(unit))
            {
                if (!quoted)
                    result.Append('\'');
                quoted = true;
                result.Append(unit);
                if (unit == '\'')
                    result.Append('\'');
                if (pair)
                    result.Append(units[++i]);
            }
            else
            {
                if (quoted)
                    result.Append('\'');
                quoted = false;
                result.Append('#').Append(((int)unit).ToString(CultureInfo.InvariantCulture));
            }
        }

        if (quoted)
            result.Append('\'');
        return result.ToString();
    }

    /// <summary>DBG-155: 32..126, or 160 and above except surrogates, U+FFFE and U+FFFF.</summary>
    private static bool IsPrintable(char unit) =>
        unit is >= (char)32 and <= (char)126 || (unit >= 160 && !char.IsSurrogate(unit) && unit is not ((char)0xFFFE or (char)0xFFFF));

    // ---- ordinals ----

    /// <summary>
    /// An ordinal of an ordinal type by that type's rule: enum name, quoted character, False/True, decimal. Used for
    /// values, set members and array index names.
    /// </summary>
    public static string Ordinal(PdiType type, long ordinal)
    {
        switch (type.Kind)
        {
            case PdiTypeKind.Enum:
                foreach (var value in type.Values)
                {
                    if (value.Value == ordinal)
                        return value.Name;
                }

                return TypeName(type) + "(" + ordinal.ToString(CultureInfo.InvariantCulture) + ")";
            case PdiTypeKind.Char when type.Size == 1:
                return ordinal == 39 ? "''''"
                    : ordinal is >= 32 and <= 126 ? "'" + (char)ordinal + "'"
                    : "#" + ordinal.ToString(CultureInfo.InvariantCulture);
            case PdiTypeKind.Char:
                return ordinal == 39 ? "''''"
                    : ordinal is >= 0 and <= char.MaxValue && IsPrintable((char)ordinal) ? "'" + (char)ordinal + "'"
                    : "#" + ordinal.ToString(CultureInfo.InvariantCulture);
            case PdiTypeKind.Boolean when ordinal is 0 or 1:
                return ordinal == 0 ? "False" : "True";
            default:
                return ordinal.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string Integer(PdiType type, byte[] bytes) =>
        bytes.Length is not (1 or 2 or 4 or 8) ? Opaque(bytes, bytes.Length)
        : type.Signed ? Signed(bytes).ToString(CultureInfo.InvariantCulture)
        : Unsigned(bytes).ToString(CultureInfo.InvariantCulture);

    private static string Boolean(PdiType type, byte[] bytes)
    {
        var value = Unsigned(bytes);
        if (value <= 1)
            return value == 0 ? "False" : "True";
        // The wide Boolean types are True for every non-zero value.
        return IsWideBoolean(type) ? "True" : "Boolean(" + value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private static bool IsWideBoolean(PdiType type)
    {
        if (type.Size > 1)
            return true;
        for (var depth = 0; type is not null && depth < 16; type = type.AliasOf!, depth++)
        {
            if (type.Name.Equals("ByteBool", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // ---- floating point (DBG-156) ----

    private static string Float(PdiType type, byte[] bytes)
    {
        switch (type.FloatFormat)
        {
            case PdiFloatFormat.Single when bytes.Length == 4:
                return Float(BinaryPrimitives.ReadSingleLittleEndian(bytes), value => value.ToString("R", CultureInfo.InvariantCulture));
            case PdiFloatFormat.Double or PdiFloatFormat.DateTime when bytes.Length == 8:
                return Float(BinaryPrimitives.ReadDoubleLittleEndian(bytes), value => value.ToString("R", CultureInfo.InvariantCulture));
            case PdiFloatFormat.Currency when bytes.Length == 8:
                // A 64-bit integer scaled by 10000: four decimals, trailing zeros trimmed to at least two.
                return (BinaryPrimitives.ReadInt64LittleEndian(bytes) / 10000m).ToString("0.00##", CultureInfo.InvariantCulture);
            default:
                return Opaque(bytes, bytes.Length);
        }
    }

    private static string Float<T>(T value, Func<T, string> shortest) where T : System.Numerics.IFloatingPointIeee754<T> =>
        T.IsNaN(value) ? "NaN"
        : T.IsPositiveInfinity(value) ? "+Inf"
        : T.IsNegativeInfinity(value) ? "-Inf"
        : value == T.Zero ? (T.IsNegative(value) ? "-0" : "0")
        : shortest(value).Replace("E+", "E", StringComparison.Ordinal);

    // ---- sets (DBG-157, DBG-158) ----

    private static string Set(PdiType type, byte[] bytes)
    {
        var element = type.Base ?? type;
        var collapse = element.Kind is PdiTypeKind.Integer or PdiTypeKind.Char;
        var result = new StringBuilder("[");
        for (var bit = 0; bit < bytes.Length * 8; bit++)
        {
            if (!IsMember(bytes, bit))
                continue;
            var last = bit;
            while (last + 1 < bytes.Length * 8 && IsMember(bytes, last + 1))
                last++;
            if (result.Length > 1)
                result.Append(", ");
            if (collapse && last - bit >= 2)
            {
                // Three or more consecutive members of an integer or character set.
                result.Append(Ordinal(element, type.BitBase + bit)).Append("..").Append(Ordinal(element, type.BitBase + last));
                bit = last;
            }
            else
            {
                result.Append(Ordinal(element, type.BitBase + bit));
            }
        }

        return result.Append(']').ToString();
    }

    private static bool IsMember(byte[] bytes, int bit) => (bytes[bit / 8] >> (bit % 8) & 1) != 0;

    // ---- bytes ----

    private static string Opaque(byte[] bytes, int size)
    {
        var shown = Math.Min(bytes.Length, MaxOpaqueBytes);
        var result = new StringBuilder("{");
        for (var i = 0; i < shown; i++)
            result.Append(i == 0 ? "" : " ").Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        if (size > shown)
            result.Append(shown == 0 ? "" : " ").Append(Ellipsis);
        return result.Append('}').ToString();
    }

    /// <summary>The raw bits as hexadecimal digits, most significant first (two per byte).</summary>
    private static string Hex(byte[] bytes)
    {
        var result = new StringBuilder(bytes.Length * 2);
        for (var i = bytes.Length - 1; i >= 0; i--)
            result.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        return result.ToString();
    }

    /// <summary>Little-endian unsigned value of up to eight bytes (DBG-153).</summary>
    public static ulong Unsigned(ReadOnlySpan<byte> bytes)
    {
        ulong value = 0;
        for (var i = Math.Min(bytes.Length, 8) - 1; i >= 0; i--)
            value = value << 8 | bytes[i];
        return value;
    }

    /// <summary>Little-endian two's complement value of 1, 2, 4 or 8 bytes.</summary>
    public static long Signed(ReadOnlySpan<byte> bytes)
    {
        var value = Unsigned(bytes);
        var bits = Math.Min(bytes.Length, 8) * 8;
        return bits is 0 or 64 ? (long)value : (long)(value << (64 - bits)) >> (64 - bits);
    }

    // ---- type names (DBG-154) ----

    /// <summary>The declared name, or for an anonymous type its synthesised Pascal spelling.</summary>
    public static string TypeName(PdiType type) => TypeName(type, 0);

    private static string TypeName(PdiType? type, int depth)
    {
        if (type is null || depth > 8)
            return "?";
        if (type.Name.Length > 0)
            return type.Name;
        switch (type.Kind)
        {
            case PdiTypeKind.DynArray or PdiTypeKind.OpenArray:
                return "array of " + TypeName(type.Element, depth + 1);
            case PdiTypeKind.StaticArray:
            {
                var index = type.IndexType ?? type;
                return "array[" + IndexName(index, type.ArrayLow) + ".." + IndexName(index, type.ArrayLow + type.Count - 1) + "] of "
                    + TypeName(type.Element, depth + 1);
            }

            case PdiTypeKind.Set:
                return "set of " + TypeName(type.Base, depth + 1);
            case PdiTypeKind.Pointer:
                return type.To is null ? "Pointer" : "^" + TypeName(type.To, depth + 1);
            case PdiTypeKind.ClassRef:
                return "class of " + TypeName(type.Of, depth + 1);
            case PdiTypeKind.Procedural:
                return type.Method ? "procedure of object" : "procedure";
            case PdiTypeKind.String:
                return type.Flavor switch
                {
                    PdiStringFlavor.Short => "string[" + type.MaxLength.ToString(CultureInfo.InvariantCulture) + "]",
                    PdiStringFlavor.Ansi => "AnsiString",
                    PdiStringFlavor.PChar => "PChar",
                    PdiStringFlavor.PAnsiChar => "PAnsiChar",
                    _ => "string",
                };
            case PdiTypeKind.Integer when type is { Low: { } low, High: { } high }:
                return low.ToString(CultureInfo.InvariantCulture) + ".." + high.ToString(CultureInfo.InvariantCulture);
            case PdiTypeKind.Enum:
                return "(" + string.Join(", ", type.Values.Select(value => value.Name)) + ")";
            case PdiTypeKind.Record:
                return "record";
            case PdiTypeKind.Class:
                return "class";
            case PdiTypeKind.Interface:
                return "interface";
            default:
                return type.Kind.ToString();
        }
    }

    /// <summary>
    /// The name of an array index: by the rule of an enum or character index type, else decimal.
    /// </summary>
    public static string IndexName(PdiType? indexType, long index) =>
        indexType is { Kind: PdiTypeKind.Enum or PdiTypeKind.Char or PdiTypeKind.Boolean }
            ? Ordinal(indexType, index)
            : index.ToString(CultureInfo.InvariantCulture);
}
