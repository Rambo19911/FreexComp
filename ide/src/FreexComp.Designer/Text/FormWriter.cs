using System.Globalization;
using System.Text;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Text;

/// <summary>
/// Writes the canonical <c>.form</c> text (docs/interfaces/form-format.md §2; form-document.md §4): LF, 2 spaces per
/// level, properties in metadata order, defaults omitted, geometry always written, untouched values with their original
/// text, preserved objects verbatim.
/// </summary>
public static class FormWriter
{
    /// <summary>Most characters on one string line (DSG-FD-062).</summary>
    public const int StringChunkLength = 64;

    /// <summary>Bytes per line of a binary value (form-format.md §2.4).</summary>
    public const int BinaryBytesPerLine = 32;

    /// <summary>The whole file.</summary>
    public static string Write(FormDocument document)
    {
        var output = new StringBuilder();
        WriteObject(output, document.Root, 0);
        return output.ToString();
    }

    /// <summary>A clipboard fragment: the objects at indent level 0.</summary>
    public static string WriteObjects(IEnumerable<FormObject> objects)
    {
        var output = new StringBuilder();
        foreach (var obj in objects)
        {
            WriteObject(output, obj, 0);
        }

        return output.ToString();
    }

    /// <summary>
    /// The text lines of a value set by the designer (§4.1), each with its level relative to the property line; the first
    /// line follows <c>Path = </c>. <paramref name="info"/> orders set elements and collection item properties.
    /// </summary>
    public static IReadOnlyList<(int Level, string Text)> FormatValue(FormValue value, PropertyInfo? info, ComponentCatalog catalog)
    {
        var lines = new List<(int, string)>();
        AppendValue(lines, value, info, catalog, 0);
        return lines;
    }

    private static void WriteObject(StringBuilder output, FormObject obj, int level)
    {
        var indent = new string(' ', level * 2);
        if (obj.PreservedText is { } preserved)
        {
            // DSG-FD-048: verbatim, re-indented to the current level (relative indentation kept).
            foreach (var line in preserved.Split('\n'))
            {
                var trimmed = line.TrimEnd(' ', '\t');
                if (trimmed.Length > 0)
                {
                    output.Append(indent).Append(trimmed).Append('\n');
                }
            }

            return;
        }

        var keyword = obj.Kind switch
        {
            FormObjectKind.Inherited => "inherited",
            FormObjectKind.Inline => "inline",
            _ => "object",
        };
        output.Append(indent).Append(keyword).Append(' ').Append(obj.Name).Append(": ").Append(obj.TypeName).Append('\n');

        var propertyIndent = indent + "  ";
        foreach (var (path, lines) in PropertyLines(obj))
        {
            output.Append(propertyIndent).Append(path).Append(" = ").Append(lines[0].Text).Append('\n');
            for (var i = 1; i < lines.Count; i++)
            {
                output.Append(propertyIndent).Append(' ', lines[i].Level * 2).Append(lines[i].Text).Append('\n');
            }
        }

        foreach (var child in obj.Children)
        {
            WriteObject(output, child, level + 1);
        }

        output.Append(indent).Append("end\n");
    }

    /// <summary>DSG-FD-042…046: the property lines of an object in canonical order.</summary>
    private static List<(string Path, IReadOnlyList<(int Level, string Text)> Lines)> PropertyLines(FormObject obj)
    {
        var catalog = obj.Catalog;
        var known = new List<(int[] Key, string Path, IReadOnlyList<(int, string)> Lines)>();
        var preserved = new List<(string, IReadOnlyList<(int, string)>)>();
        var events = new List<(int Key, string Path, IReadOnlyList<(int, string)> Lines)>();
        var always = AlwaysWritten(obj);

        foreach (var property in obj.Properties)
        {
            if (property.IsPreserved || obj.Info is null || (property.Info is null && property.EventInfo is null))
            {
                preserved.Add((property.Path, Lines(property, catalog)));
            }
            else if (property.EventInfo is { } ev)
            {
                events.Add((obj.Info.Events.IndexOf(ev), property.Path, Lines(property, catalog)));
            }
            else
            {
                var info = property.Info!;
                var isAlways = always.Any(a => a.Path.Equals(property.Path, StringComparison.OrdinalIgnoreCase));
                var stored = PropertyAccess.IsStoredPath(obj, property.Path, info);
                if (!stored && property.RawText is null)
                {
                    continue; // "stored": false values are never written; a line read from a file is kept (DSG-FD-043)
                }

                if (!isAlways && stored && PropertyAccess.IsDefault(catalog, info, property.Value))
                {
                    continue; // DSG-FD-043: equal to the default = not written
                }

                known.Add((SortKey(obj.Info, catalog, property.Path), property.Path, Lines(property, catalog)));
            }
        }

        foreach (var (path, value) in always)
        {
            if (obj.FindProperty(path) is null)
            {
                known.Add((SortKey(obj.Info!, catalog, path), path, FormatValue(value, null, catalog)));
            }
        }

        var result = new List<(string, IReadOnlyList<(int, string)>)>();
        result.AddRange(known.OrderBy(k => k.Key, KeyComparer.Instance).Select(k => (k.Path, k.Lines)));
        result.AddRange(preserved);
        result.AddRange(events.OrderBy(e => e.Key).Select(e => (e.Path, e.Lines)));
        return result;
    }

    /// <summary>
    /// DSG-FD-044: the always-written properties of this object with the value used when the file has none: every
    /// control's bounds; the root's Left/Top (new-form template 200/120), client size and PixelsPerInch = 96; the tray
    /// position of a non-visual direct child of the root. <c>"stored": false</c> wins.
    /// </summary>
    private static List<(string Path, FormValue Value)> AlwaysWritten(FormObject obj)
    {
        var result = new List<(string, FormValue)>();
        if (obj.Info is not { } info)
        {
            return result;
        }

        void Add(string path, long value)
        {
            if (obj.Catalog.FindPropertyPath(info, path) is { Stored: true })
            {
                result.Add((path, new IntegerValue(value, false)));
            }
        }

        if (obj.Parent is null)
        {
            Add("Left", 200);
            Add("Top", 120);
            if (obj.FindProperty("ClientWidth") is not null || obj.FindProperty("Width") is null)
            {
                Add("ClientWidth", 0);
            }

            if (obj.FindProperty("ClientHeight") is not null || obj.FindProperty("Height") is null)
            {
                Add("ClientHeight", 0);
            }

            Add("PixelsPerInch", 96);
        }
        else if (info.Kind == ComponentKind.NonVisual)
        {
            if (obj.Parent.Parent is null)
            {
                Add("DesignLeft", 0);
                Add("DesignTop", 0);
            }
        }
        else
        {
            Add("Left", 0);
            Add("Top", 0);
            Add("Width", 0);
            Add("Height", 0);
        }

        return result;
    }

    /// <summary>Metadata position of a path: the index of each part in its (sub-)property list; DesignLeft/Top after all.</summary>
    private static int[] SortKey(ComponentInfo info, ComponentCatalog catalog, string path)
    {
        if (path.Equals("DesignLeft", StringComparison.OrdinalIgnoreCase) || path.Equals("DesignTop", StringComparison.OrdinalIgnoreCase))
        {
            return [info.Properties.Length, path.Equals("DesignLeft", StringComparison.OrdinalIgnoreCase) ? 0 : 1];
        }

        var parts = path.Split('.');
        var key = new int[parts.Length];
        var list = info.Properties;
        for (var i = 0; i < parts.Length; i++)
        {
            var at = list.IndexOf(list.FirstOrDefault(p => p.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase))!);
            key[i] = at < 0 ? int.MaxValue : at;
            list = at < 0 ? [] : catalog.SubProperties(list[at]);
        }

        return key;
    }

    private sealed class KeyComparer : IComparer<int[]>
    {
        public static KeyComparer Instance { get; } = new();

        public int Compare(int[]? x, int[]? y)
        {
            for (var i = 0; i < Math.Min(x!.Length, y!.Length); i++)
            {
                if (x[i] != y[i])
                {
                    return x[i].CompareTo(y[i]);
                }
            }

            return x.Length.CompareTo(y.Length);
        }
    }

    /// <summary>DSG-FD-046: the original text of an untouched value, else the formatted value.</summary>
    private static IReadOnlyList<(int Level, string Text)> Lines(FormProperty property, ComponentCatalog catalog) =>
        property.RawText is not null && !property.RawLines.IsDefaultOrEmpty
            ? property.RawLines
            : FormatValue(property.Value, property.Info, catalog);

    // ---- value formatting (§4.1) ---------------------------------------------------------------------------

    private static void AppendValue(List<(int, string)> lines, FormValue value, PropertyInfo? info, ComponentCatalog catalog, int level)
    {
        switch (value)
        {
            case IntegerValue integer:
                lines.Add((level, FormatInteger(integer)));
                break;
            case FloatValue f:
                lines.Add((level, FormatFloat(f.Value)));
                break;
            case StringValue s:
                AppendString(lines, s.Value, level);
                break;
            case IdentValue ident:
                lines.Add((level, CanonicalIdent(ident.Name)));
                break;
            case QualifiedIdentValue qualified:
                lines.Add((level, string.Join('.', qualified.Parts)));
                break;
            case SetValue set:
                lines.Add((level, "[" + string.Join(", ", OrderSet(set, info, catalog)) + "]"));
                break;
            case StringListValue list:
                lines.Add((level, "("));
                foreach (var line in list.Lines)
                {
                    AppendString(lines, line, level + 1);
                }

                lines.Add((level, ")"));
                break;
            case BinaryValue binary:
                lines.Add((level, "{"));
                for (var at = 0; at < binary.Bytes.Length; at += BinaryBytesPerLine)
                {
                    var count = Math.Min(BinaryBytesPerLine, binary.Bytes.Length - at);
                    lines.Add((level + 1, Convert.ToHexString(binary.Bytes.AsSpan(at, count))));
                }

                lines.Add((level, "}"));
                break;
            case CollectionValue collection:
                AppendCollection(lines, collection, info, catalog, level);
                break;
            case RawValue raw:
                var rawLines = raw.Text.Split('\n');
                lines.Add((level, rawLines[0].Trim(' ', '\t')));
                lines.AddRange(rawLines.Skip(1).Select(l => l.Trim(' ', '\t')).Where(l => l.Length > 0).Select(l => (level + 1, l)));
                break;
            default:
                throw new ArgumentException("Unknown value " + value, nameof(value));
        }
    }

    private static void AppendCollection(List<(int, string)> lines, CollectionValue collection, PropertyInfo? info, ComponentCatalog catalog, int level)
    {
        var itemProperties = info?.ItemClass is { } itemClass && catalog.FindType(itemClass) is { Kind: TypeKind.Object } type
            ? type.Properties : [];
        lines.Add((level, "<"));
        foreach (var item in collection.Items)
        {
            lines.Add((level + 1, "item"));
            var ordered = item.Properties
                .Select((p, index) => (p, index, Info: itemProperties.FirstOrDefault(i => i.Name.Equals(p.Path, StringComparison.OrdinalIgnoreCase))))
                .Where(x => x.Info is null || !PropertyAccess.IsDefault(catalog, x.Info, x.p.Value))
                .OrderBy(x => x.Info is null ? int.MaxValue : itemProperties.IndexOf(x.Info))
                .ThenBy(x => x.index);
            foreach (var (property, _, propertyInfo) in ordered)
            {
                var valueLines = new List<(int, string)>();
                if (property.RawText is { } raw && !raw.Contains('\n', StringComparison.Ordinal))
                {
                    valueLines.Add((0, raw.Trim(' ', '\t')));
                }
                else
                {
                    AppendValue(valueLines, property.Value, propertyInfo, catalog, 0);
                }

                lines.Add((level + 2, property.Path + " = " + valueLines[0].Item2));
                lines.AddRange(valueLines.Skip(1).Select(l => (l.Item1 + level + 2, l.Item2)));
            }

            lines.Add((level + 1, "end"));
        }

        lines.Add((level, ">"));
    }

    private static IEnumerable<string> OrderSet(SetValue set, PropertyInfo? info, ComponentCatalog catalog)
    {
        var members = info is { Kind: PropertyKind.Set } && catalog.FindType(info.Type)?.ElementType is { } element
            ? catalog.FindType(element)?.Values ?? [] : [];
        return set.Elements
            .Select((e, index) => (e, index, Position: members.IndexOf(members.FirstOrDefault(m => m.Equals(e, StringComparison.OrdinalIgnoreCase))!)))
            .OrderBy(x => x.Position < 0 ? int.MaxValue : x.Position)
            .ThenBy(x => x.index)
            .Select(x => x.e);
    }

    private static string CanonicalIdent(string name) =>
        name.Equals("True", StringComparison.OrdinalIgnoreCase) ? "True"
        : name.Equals("False", StringComparison.OrdinalIgnoreCase) ? "False"
        : name.Equals("nil", StringComparison.OrdinalIgnoreCase) ? "nil"
        : name;

    /// <summary>Decimal; hex as <c>$</c> + 8 upper-case digits (32-bit two's complement), 16 outside the 32-bit range.</summary>
    public static string FormatInteger(IntegerValue value)
    {
        if (!value.Hex)
        {
            return value.Value.ToString(CultureInfo.InvariantCulture);
        }

        return value.Value is >= int.MinValue and <= uint.MaxValue
            ? "$" + unchecked((uint)value.Value).ToString("X8", CultureInfo.InvariantCulture)
            : "$" + unchecked((ulong)value.Value).ToString("X16", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Shortest round-trip decimal, always with a <c>.</c>; exponent form (<c>1.5E+20</c>) when |x| ≥ 1e15 or
    /// 0 &lt; |x| &lt; 1e-5 (form-format.md §2.4).
    /// </summary>
    public static string FormatFloat(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "NaN and infinity have no form text");
        }

        // Decompose the shortest round-trip text into sign, digits and decimal exponent (value = 0.d1d2… × 10^exp).
        var r = value.ToString("R", CultureInfo.InvariantCulture);
        var negative = r.StartsWith('-');
        if (negative)
        {
            r = r[1..];
        }

        var exponent = 0;
        var e = r.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            exponent = int.Parse(r.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            r = r[..e];
        }

        var dot = r.IndexOf('.', StringComparison.Ordinal);
        var intPart = dot < 0 ? r : r[..dot];
        var fraction = dot < 0 ? "" : r[(dot + 1)..];
        var digits = (intPart + fraction).TrimStart('0');
        var leadingZeros = (intPart + fraction).Length - digits.Length;
        digits = digits.TrimEnd('0');
        var sign = negative ? "-" : "";
        if (digits.Length == 0)
        {
            return sign + "0.0";
        }

        // Position of the decimal point relative to the first significant digit.
        var pointAfter = intPart.Length + exponent - leadingZeros;
        var magnitude = Math.Abs(value);
        if (magnitude >= 1e15 || magnitude < 1e-5)
        {
            var mantissa = digits[..1] + "." + (digits.Length > 1 ? digits[1..] : "0");
            var exp = pointAfter - 1;
            return sign + mantissa + "E" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString(CultureInfo.InvariantCulture);
        }

        if (pointAfter <= 0)
        {
            return sign + "0." + new string('0', -pointAfter) + digits;
        }

        if (pointAfter >= digits.Length)
        {
            return sign + digits + new string('0', pointAfter - digits.Length) + ".0";
        }

        return sign + digits[..pointAfter] + "." + digits[pointAfter..];
    }

    // ---- strings (§4.2) ------------------------------------------------------------------------------------

    /// <summary>The string as one or more lines (chunks of at most 64 characters, continued with <c> +</c>).</summary>
    public static IReadOnlyList<string> FormatString(string value)
    {
        var lines = new List<(int, string)>();
        AppendString(lines, value, 0);
        return [.. lines.Select(l => l.Item2)];
    }

    private static void AppendString(List<(int, string)> lines, string value, int level)
    {
        if (value.Length == 0)
        {
            lines.Add((level, "''"));
            return;
        }

        // Units: one character, a surrogate pair, or one control code (each counts as one source character).
        var units = new List<(string Text, bool IsCode)>();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                units.Add((value.Substring(i, 2), false));
                i++;
            }
            else if (IsCode(c))
            {
                units.Add(("#" + ((int)c).ToString(CultureInfo.InvariantCulture), true));
            }
            else
            {
                units.Add((c == '\'' ? "''" : c.ToString(), false));
            }
        }

        for (var start = 0; start < units.Count; start += StringChunkLength)
        {
            var chunk = new StringBuilder();
            var quoted = false;
            foreach (var (text, isCode) in units.Skip(start).Take(StringChunkLength))
            {
                if (isCode == quoted)
                {
                    chunk.Append('\'');
                    quoted = !quoted;
                }

                chunk.Append(text);
            }

            if (quoted)
            {
                chunk.Append('\'');
            }

            var last = start + StringChunkLength >= units.Count;
            lines.Add((start == 0 ? level : level + 1, last ? chunk.ToString() : chunk + " +"));
        }
    }

    /// <summary>DSG-FD-060: written as <c>#n</c> outside quotes.</summary>
    private static bool IsCode(char c) =>
        c < ' ' || c is >= '\u007F' and <= '\u009F' || c is (char)0x2028 or (char)0x2029 || char.IsSurrogate(c);
}
