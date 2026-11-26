using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Inspector;

/// <summary>Message ids of the Object Inspector's value entry (states-and-messages.md; object-inspector.md §5.1).</summary>
public static class ValueMessages
{
    public const string InvalidInteger = "Msg.Designer.ValueInvalidInteger";
    public const string InvalidFloat = "Msg.Designer.ValueInvalidFloat";
    public const string InvalidEnum = "Msg.Designer.ValueInvalidEnum";
    public const string InvalidColor = "Msg.Designer.ValueInvalidColor";
    public const string InvalidExpression = "Msg.Designer.ValueInvalidExpression";
    public const string ReferenceNotFound = "Msg.Designer.ReferenceNotFound";
}

/// <summary>The outcome of parsing an entry: the value to store, or the message id (with its placeholders) to show.</summary>
public sealed record ParseResult(FormValue? Value, string? MessageId, IReadOnlyList<object> Args)
{
    public bool Succeeded => MessageId is null;

    public static ParseResult Ok(FormValue value) => new(value, null, []);

    public static ParseResult Fail(string messageId, params object[] args) => new(null, messageId, args);
}

/// <summary>
/// Display text and entry parsing per property type (object-inspector.md §5.1, §5.2, §5.6). All parsing is
/// culture-invariant; leading/trailing spaces are trimmed first, except for <c>string</c> (any text is a value) and a
/// single-character <c>Char</c> entry.
/// </summary>
public sealed class ValueConverter(ComponentCatalog catalog)
{
    /// <summary>§5.6: the colour constant names offered and written (component-metadata.md §6).</summary>
    public static IReadOnlyList<string> ColorNames => NamedValues.ColorNames;

    /// <summary>§5.6: the cursor constant names.</summary>
    public static IReadOnlyList<string> CursorNames => NamedValues.CursorNames;

    /// <summary>The display text of <paramref name="value"/> (null for a property that is not stored, e.g. an object row).</summary>
    public string Format(PropertyInfo property, FormValue? value)
    {
        switch (EditorOf(property))
        {
            case Editor.Picture:
                return value is null ? "(none)" : "(picture)";
            case Editor.Object:
                return "(" + property.Type + ")";
            case Editor.Strings:
                return Count(value is StringListValue list ? list.Lines.Length : 0, "line", "lines");
            case Editor.Collection:
                return Count(value is CollectionValue c ? c.Items.Length : 0, "item", "items");
        }

        value ??= PropertyAccess.DefaultValue(catalog, property);
        return EditorOf(property) switch
        {
            Editor.Component => value switch
            {
                IdentValue { Name: var n } when n.Equals("nil", StringComparison.OrdinalIgnoreCase) => "(none)",
                IdentValue { Name: var n } => n,
                QualifiedIdentValue q => string.Join('.', q.Parts),
                _ => Raw(value),
            },
            Editor.Enum => value is IdentValue ident ? DeclaredMember(property, ident.Name) ?? ident.Name : Raw(value),
            Editor.Set => value is SetValue set ? FormatSet(property, set) : Raw(value),
            Editor.Color => value switch
            {
                IdentValue ident => NamedValues.CanonicalColorName(ident.Name) ?? ident.Name,
                IntegerValue i => NamedValues.ColorName(i.Value) ?? "$" + unchecked((uint)i.Value).ToString("X8", CultureInfo.InvariantCulture),
                _ => Raw(value),
            },
            Editor.Cursor => value switch
            {
                IdentValue ident => NamedValues.CanonicalCursorName(ident.Name) ?? ident.Name,
                IntegerValue i => NamedValues.CursorName(i.Value) ?? i.Value.ToString(CultureInfo.InvariantCulture),
                _ => Raw(value),
            },
            Editor.ShortCut => value is IntegerValue s ? ShortCutText.Format(s.Value) : Raw(value),
            Editor.Integer => value is IntegerValue n ? n.Value.ToString(CultureInfo.InvariantCulture) : Raw(value),
            Editor.Double => value switch
            {
                FloatValue f => f.Value.ToString("R", CultureInfo.InvariantCulture),
                IntegerValue n => n.Value.ToString(CultureInfo.InvariantCulture),
                _ => Raw(value),
            },
            Editor.Boolean => value is IdentValue b ? (b.Name.Equals("True", StringComparison.OrdinalIgnoreCase) ? "True" : "False") : Raw(value),
            Editor.Char => value is StringValue c ? FormatChar(c.Value) : Raw(value),
            _ => value is StringValue str ? FormatString(str.Value) : Raw(value),
        };
    }

    /// <summary>
    /// Parses an entry for <paramref name="property"/>. <paramref name="current"/>: the component's own current value, needed
    /// by relative integer edits (DSG-OI-041). <paramref name="references"/>: the component names a <c>kind: component</c>
    /// property may name (§5.1; computed by the caller with <c>ComponentCatalog.IsAssignable</c>). <paramref name="qualifier"/>:
    /// in a frame document the header name of its root, with which a reference may be written qualified (RTL-1104, D-126).
    /// </summary>
    /// <exception cref="NotSupportedException">The property is not text-editable (object, strings, collection, picture).</exception>
    public ParseResult Parse(PropertyInfo property, string text, FormValue? current = null, IReadOnlyList<string>? references = null,
        string? qualifier = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        return EditorOf(property) switch
        {
            Editor.Integer => ParseInteger(property, trimmed, current),
            Editor.Double => ParseDouble(property, trimmed),
            Editor.Boolean => ParseBoolean(trimmed, text),
            Editor.Char => ParseChar(text),
            Editor.Enum => MatchMember(property, trimmed) is { } member ? ParseResult.Ok(new IdentValue(member)) : ParseResult.Fail(ValueMessages.InvalidEnum, text.Trim()),
            Editor.Set => ParseSet(property, trimmed),
            Editor.Color => ParseColor(trimmed),
            Editor.Cursor => ParseCursor(trimmed),
            Editor.ShortCut => ShortCutText.TryParse(trimmed, out var shortCut)
                ? ParseResult.Ok(new IntegerValue(shortCut, false))
                : ParseResult.Fail(ValueMessages.InvalidEnum, trimmed),
            Editor.Component => ParseReference(trimmed, references ?? [], qualifier),
            Editor.String => ParseResult.Ok(new StringValue(FirstLine(text))),
            var other => throw new NotSupportedException($"{property.Name} ({other}) is not edited as text"),
        };
    }

    /// <summary>DSG-OI-041: the entry has the exact form <c>+= n</c>, <c>-= n</c>, <c>*= n</c> or <c>/= n</c>.</summary>
    public static bool IsRelativeEdit(string text) => text.TrimStart() is [('+' or '-' or '*' or '/'), '=', ..];

    private enum Editor { String, Integer, Double, Boolean, Char, Enum, Set, Color, Cursor, ShortCut, Strings, Object, Picture, Collection, Component }

    /// <summary>The §5.1 row of a property: kind first, then the editor key, then the type name.</summary>
    private static Editor EditorOf(PropertyInfo p)
    {
        var editor = p.Editor?.ToLowerInvariant();
        if (editor == "picture")
        {
            return Editor.Picture;
        }

        switch (p.Kind)
        {
            case PropertyKind.Object:
                return Editor.Object;
            case PropertyKind.Collection:
                return Editor.Collection;
            case PropertyKind.Component:
                return Editor.Component;
            case PropertyKind.Enum:
                return Editor.Enum;
            case PropertyKind.Set:
                return Editor.Set;
        }

        return (editor, p.Type.ToLowerInvariant()) switch
        {
            ("strings", _) or (_, "tstrings") => Editor.Strings,
            ("color", _) or (_, "tcolor") => Editor.Color,
            ("shortcut", _) or (_, "tshortcut") => Editor.ShortCut,
            (_, "tcursor") => Editor.Cursor,
            (_, "integer" or "int64") => Editor.Integer,
            (_, "double") => Editor.Double,
            (_, "boolean") => Editor.Boolean,
            (_, "char") => Editor.Char,
            _ => Editor.String,
        };
    }

    /// <summary>§5.1 Integer: an expression (DSG-OI-040) or a relative edit (DSG-OI-041), then the range check.</summary>
    private static ParseResult ParseInteger(PropertyInfo p, string text, FormValue? current)
    {
        long value;
        if (IsRelativeEdit(text))
        {
            if (current is not IntegerValue { Value: var cur } || !IntegerExpression.TryEvaluate(text[2..], out var operand))
            {
                return ParseResult.Fail(ValueMessages.InvalidExpression);
            }

            Int128 r = text[0] switch
            {
                '+' => (Int128)cur + operand,
                '-' => (Int128)cur - operand,
                '*' => (Int128)cur * operand,
                _ when operand == 0 => Int128.MaxValue, // division by zero
                _ => (Int128)cur / operand,
            };
            if (r < long.MinValue || r > long.MaxValue)
            {
                return ParseResult.Fail(ValueMessages.InvalidExpression);
            }

            value = (long)r;
        }
        else if (!IntegerExpression.TryEvaluate(text, out value))
        {
            return ParseResult.Fail(ValueMessages.InvalidExpression);
        }

        var int64 = p.Type.Equals("Int64", StringComparison.OrdinalIgnoreCase);
        var min = p.Min is { } lo ? (long)Math.Ceiling(lo) : int64 ? long.MinValue : int.MinValue;
        var max = p.Max is { } hi ? (long)Math.Floor(hi) : int64 ? long.MaxValue : int.MaxValue;
        return value < min || value > max
            ? ParseResult.Fail(ValueMessages.InvalidInteger, min, max)
            : ParseResult.Ok(new IntegerValue(value, false));
    }

    /// <summary>§5.1 Double: decimal or exponent notation, one <c>.</c> or <c>,</c> as decimal separator, no expressions.</summary>
    private static ParseResult ParseDouble(PropertyInfo p, string text)
    {
        var separators = text.Count(c => c is '.' or ',');
        var normalized = separators == 1 ? text.Replace(',', '.') : text;
        if (separators > 1 || !double.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
        {
            return ParseResult.Fail(ValueMessages.InvalidFloat);
        }

        return (p.Min is { } min && value < min) || (p.Max is { } max && value > max)
            ? ParseResult.Fail(ValueMessages.InvalidInteger, p.Min ?? double.MinValue, p.Max ?? double.MaxValue)
            : ParseResult.Ok(new FloatValue(value));
    }

    /// <summary>§5.1 Boolean: <c>True</c>/<c>False</c> or a prefix of one (typing <c>t</c>/<c>f</c> selects).</summary>
    private static ParseResult ParseBoolean(string trimmed, string text)
    {
        if (trimmed.Length > 0 && "True".StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return ParseResult.Ok(new IdentValue("True"));
        }

        return trimmed.Length > 0 && "False".StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
            ? ParseResult.Ok(new IdentValue("False"))
            : ParseResult.Fail(ValueMessages.InvalidEnum, text.Trim());
    }

    /// <summary>§5.1 Char: one character, or <c>#</c> + decimal code (0 … 65535).</summary>
    private static ParseResult ParseChar(string text)
    {
        if (text.Length == 1)
        {
            return ParseResult.Ok(new StringValue(text));
        }

        var t = text.Trim();
        if (t.Length == 1)
        {
            return ParseResult.Ok(new StringValue(t));
        }

        return t is ['#', .. var digits] && digits.All(char.IsAsciiDigit) && digits.Length is > 0 and <= 5
               && int.Parse(digits, CultureInfo.InvariantCulture) is var code and <= char.MaxValue
            ? ParseResult.Ok(new StringValue(((char)code).ToString()))
            : ParseResult.Fail(ValueMessages.InvalidEnum, t);
    }

    /// <summary>The declared member equal to <paramref name="text"/> (case-insensitive), else the only member it is a prefix of.</summary>
    private string? MatchMember(PropertyInfo p, string text)
    {
        var members = Members(p);
        if (DeclaredMember(members, text) is { } exact)
        {
            return exact;
        }

        var prefixed = text.Length == 0 ? [] : members.Where(m => m.StartsWith(text, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return prefixed.Count == 1 ? prefixed[0] : null;
    }

    /// <summary>§5.1 set: brackets optional, separators <c>,</c> or spaces, members only, duplicates ignored, declaration order.</summary>
    private ParseResult ParseSet(PropertyInfo p, string text)
    {
        var inner = text;
        if (inner.StartsWith('['))
        {
            inner = inner[1..];
        }

        if (inner.EndsWith(']'))
        {
            inner = inner[..^1];
        }

        var members = Members(p);
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in inner.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (DeclaredMember(members, element) is not { } member)
            {
                return ParseResult.Fail(ValueMessages.InvalidEnum, element);
            }

            chosen.Add(member);
        }

        return ParseResult.Ok(new SetValue([.. members.Where(chosen.Contains)]));
    }

    private string FormatSet(PropertyInfo p, SetValue set)
    {
        var members = Members(p);
        var declared = set.Elements.Select(e => DeclaredMember(members, e) ?? e).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ordered = members.Where(declared.Contains).Concat(declared.Where(e => !members.Contains(e)));
        return "[" + string.Join(", ", ordered) + "]";
    }

    /// <summary>
    /// §5.1 TColor: a constant name (stored by name, DSG-OI-058), <c>$</c> + 1 … 8 hex digits, <c>#RRGGBB</c> (→
    /// <c>$00BBGGRR</c>) or a decimal integer; numbers are stored as hex integers in the signed 32-bit pattern of DSG-FD-023.
    /// </summary>
    private static ParseResult ParseColor(string text)
    {
        if (NamedValues.CanonicalColorName(text) is { } name)
        {
            return ParseResult.Ok(new IdentValue(name));
        }

        uint? value = text switch
        {
            ['$', .. var hex] when hex.Length is >= 1 and <= 8 && hex.All(char.IsAsciiHexDigit) =>
                uint.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
            ['#', .. var rgb] when rgb.Length == 6 && rgb.All(char.IsAsciiHexDigit) => Bgr(uint.Parse(rgb, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
            _ when long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && n is >= int.MinValue and <= uint.MaxValue =>
                unchecked((uint)n),
            _ => null,
        };
        return value is { } v ? ParseResult.Ok(new IntegerValue(unchecked((int)v), true)) : ParseResult.Fail(ValueMessages.InvalidColor);

        static uint Bgr(uint rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | (rgb >> 16);
    }

    /// <summary>§5.1 TCursor: a constant name or an integer −32 768 … 32 767.</summary>
    private static ParseResult ParseCursor(string text)
    {
        if (NamedValues.CanonicalCursorName(text) is { } name)
        {
            return ParseResult.Ok(new IdentValue(name));
        }

        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && n is >= short.MinValue and <= short.MaxValue
            ? ParseResult.Ok(new IntegerValue(n, false))
            : ParseResult.Fail(ValueMessages.InvalidEnum, text);
    }

    /// <summary>
    /// §5.1 component reference: empty or <c>(none)</c> = <c>nil</c>; else one of the candidates (case-insensitive), or in a
    /// frame document a candidate qualified with the root's header name, kept qualified as written (<c>Frame.Child</c>, D-126).
    /// </summary>
    private static ParseResult ParseReference(string text, IReadOnlyList<string> references, string? qualifier)
    {
        if (text.Length == 0 || text.Equals("(none)", StringComparison.OrdinalIgnoreCase))
        {
            return ParseResult.Ok(new IdentValue("nil"));
        }

        if (qualifier is not null && text.Split('.') is [var head, var tail] && head.Trim().Equals(qualifier, StringComparison.OrdinalIgnoreCase)
            && !tail.Trim().Equals(qualifier, StringComparison.OrdinalIgnoreCase)
            && references.FirstOrDefault(r => r.Equals(tail.Trim(), StringComparison.OrdinalIgnoreCase)) is { } member)
        {
            return ParseResult.Ok(new QualifiedIdentValue([qualifier, member]));
        }

        var match = references.FirstOrDefault(r => r.Equals(text, StringComparison.OrdinalIgnoreCase));
        return match is not null ? ParseResult.Ok(new IdentValue(match)) : ParseResult.Fail(ValueMessages.ReferenceNotFound, text);
    }

    /// <summary>The members of an enum property, or of a set property's element enum, in declaration order.</summary>
    private ImmutableArray<string> Members(PropertyInfo p)
    {
        var type = catalog.FindType(p.Type);
        if (type is { Kind: TypeKind.Set, ElementType: { } element })
        {
            type = catalog.FindType(element);
        }

        return type?.Values ?? [];
    }

    private string? DeclaredMember(PropertyInfo p, string name) => DeclaredMember(Members(p), name);

    private static string? DeclaredMember(IEnumerable<string> members, string name) =>
        members.FirstOrDefault(m => m.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string Count(int n, string one, string many) =>
        n == 0 ? "(empty)" : string.Create(CultureInfo.InvariantCulture, $"({n} {(n == 1 ? one : many)})");

    /// <summary>§5.1 string display: control characters shown as <c>·</c>.</summary>
    private static string FormatString(string s)
    {
        var builder = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            builder.Append(char.IsControl(c) ? '·' : c);
        }

        return builder.ToString();
    }

    /// <summary>§5.1 Char display: the character, or <c>#n</c> for a control character.</summary>
    private static string FormatChar(string s) =>
        s.Length == 0 ? "#0" : char.IsControl(s[0]) ? "#" + ((int)s[0]).ToString(CultureInfo.InvariantCulture) : s[..1];

    /// <summary>Text with line breaks keeps only its first line (§5.1 string).</summary>
    private static string FirstLine(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text : text[..end];
    }

    /// <summary>A value of an unexpected kind for the type (preserved files): its literal form.</summary>
    private static string Raw(FormValue value) => value switch
    {
        RawValue raw => raw.Text,
        IntegerValue i => i.Value.ToString(CultureInfo.InvariantCulture),
        FloatValue f => f.Value.ToString("R", CultureInfo.InvariantCulture),
        StringValue s => FormatString(s.Value),
        IdentValue id => id.Name,
        _ => value.ToString(),
    };
}
