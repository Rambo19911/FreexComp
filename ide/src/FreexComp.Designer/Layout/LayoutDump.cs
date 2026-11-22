using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FreexComp.Designer.Layout;

/// <summary>One element of a dump's <c>controls</c> array (layout.md UIF-322).</summary>
public sealed record LayoutDumpControl(string Class, string Name, int Left, int Top, int Width, int Height, bool Visible,
    IReadOnlyList<LayoutDumpControl> Controls);

/// <summary>One element of a dump's <c>forms</c> array (layout.md UIF-321).</summary>
public sealed record LayoutDumpForm(string Class, string Name, int ClientWidth, int ClientHeight, IReadOnlyList<LayoutDumpControl> Controls);

/// <summary>A whole dump document (layout.md UIF-320).</summary>
public sealed record LayoutDumpDocument(int FormatVersion, int Dpi, IReadOnlyList<LayoutDumpForm> Forms);

/// <summary>The layout dump of layout.md §13: canonical writer (DSG-LAY-050), reader and structural comparison (DSG-LAY-054).</summary>
public static class LayoutDump
{
    public const int FormatVersion = 1;
    public const int Dpi = 96;

    /// <summary>
    /// The canonical text of UIF-320: UTF-8 (the caller writes it without BOM), LF, 2-space indentation, keys in the
    /// specified order, <c>"controls": []</c> / <c>"forms": []</c> on one line for an empty list, final LF.
    /// </summary>
    public static string Serialize(IEnumerable<LayoutDumpForm> forms)
    {
        var list = forms.ToList();
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"formatVersion\": {FormatVersion},\n");
        sb.Append($"  \"dpi\": {Dpi},\n");
        if (list.Count == 0)
        {
            sb.Append("  \"forms\": []\n");
        }
        else
        {
            sb.Append("  \"forms\": [\n");
            for (var i = 0; i < list.Count; i++)
            {
                var f = list[i];
                sb.Append("    {\n");
                sb.Append("      \"class\": ").Append(Quote(f.Class)).Append(",\n");
                sb.Append("      \"name\": ").Append(Quote(f.Name)).Append(",\n");
                sb.Append("      \"clientWidth\": ").Append(Number(f.ClientWidth)).Append(",\n");
                sb.Append("      \"clientHeight\": ").Append(Number(f.ClientHeight)).Append(",\n");
                WriteControls(sb, f.Controls, 6);
                sb.Append(i < list.Count - 1 ? "    },\n" : "    }\n");
            }

            sb.Append("  ]\n");
        }

        sb.Append("}\n");
        return sb.ToString();
    }

    public static string Serialize(LayoutDumpDocument document) => Serialize(document.Forms);

    /// <summary>Reads one dump document.</summary>
    /// <exception cref="FormatException">Not a dump document.</exception>
    public static LayoutDumpDocument Parse(string json)
    {
        using var doc = ParseJson(json);
        return ReadDocument(doc.RootElement);
    }

    /// <summary>Reads a corpus <c>.expected.json</c>: one dump document, or a JSON array of them (layout.md UIF-330).</summary>
    public static IReadOnlyList<LayoutDumpDocument> ParseDocuments(string json)
    {
        using var doc = ParseJson(json);
        return doc.RootElement.ValueKind == JsonValueKind.Array
            ? [.. doc.RootElement.EnumerateArray().Select(ReadDocument)]
            : [ReadDocument(doc.RootElement)];
    }

    /// <summary>
    /// UIF-324: same structure and order, <c>class</c>/<c>name</c> ASCII case-insensitive, every integer and Boolean
    /// exactly equal. Each difference reads <c>Form.Path.To.Control: field designer=… runtime=…</c>.
    /// </summary>
    public static IReadOnlyList<string> Compare(LayoutDumpDocument designer, LayoutDumpDocument runtime)
    {
        var diffs = new List<string>();
        void Field(string path, string field, object a, object b)
        {
            if (!Equals(a, b))
            {
                diffs.Add($"{path}: {field} designer={Text(a)} runtime={Text(b)}");
            }
        }

        Field("(document)", "formatVersion", designer.FormatVersion, runtime.FormatVersion);
        Field("(document)", "dpi", designer.Dpi, runtime.Dpi);
        Field("(document)", "forms.count", designer.Forms.Count, runtime.Forms.Count);
        for (var i = 0; i < Math.Min(designer.Forms.Count, runtime.Forms.Count); i++)
        {
            var (a, b) = (designer.Forms[i], runtime.Forms[i]);
            var path = a.Name;
            Name(path, "class", a.Class, b.Class);
            Name(path, "name", a.Name, b.Name);
            Field(path, "clientWidth", a.ClientWidth, b.ClientWidth);
            Field(path, "clientHeight", a.ClientHeight, b.ClientHeight);
            CompareControls(path, a.Controls, b.Controls);
        }

        return diffs;

        void Name(string path, string field, string a, string b)
        {
            if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            {
                diffs.Add($"{path}: {field} designer={a} runtime={b}");
            }
        }

        void CompareControls(string parentPath, IReadOnlyList<LayoutDumpControl> a, IReadOnlyList<LayoutDumpControl> b)
        {
            Field(parentPath, "controls.count", a.Count, b.Count);
            for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
            {
                var (x, y) = (a[i], b[i]);
                var path = parentPath + "." + x.Name;
                Name(path, "class", x.Class, y.Class);
                Name(path, "name", x.Name, y.Name);
                Field(path, "left", x.Left, y.Left);
                Field(path, "top", x.Top, y.Top);
                Field(path, "width", x.Width, y.Width);
                Field(path, "height", x.Height, y.Height);
                Field(path, "visible", x.Visible, y.Visible);
                CompareControls(path, x.Controls, y.Controls);
            }
        }
    }

    private static void WriteControls(StringBuilder sb, IReadOnlyList<LayoutDumpControl> controls, int indent)
    {
        var pad = new string(' ', indent);
        if (controls.Count == 0)
        {
            sb.Append(pad).Append("\"controls\": []\n");
            return;
        }

        sb.Append(pad).Append("\"controls\": [\n");
        var inner = new string(' ', indent + 4);
        for (var i = 0; i < controls.Count; i++)
        {
            var c = controls[i];
            sb.Append(pad).Append("  {\n");
            sb.Append(inner).Append("\"class\": ").Append(Quote(c.Class)).Append(",\n");
            sb.Append(inner).Append("\"name\": ").Append(Quote(c.Name)).Append(",\n");
            sb.Append(inner).Append("\"left\": ").Append(Number(c.Left)).Append(",\n");
            sb.Append(inner).Append("\"top\": ").Append(Number(c.Top)).Append(",\n");
            sb.Append(inner).Append("\"width\": ").Append(Number(c.Width)).Append(",\n");
            sb.Append(inner).Append("\"height\": ").Append(Number(c.Height)).Append(",\n");
            sb.Append(inner).Append("\"visible\": ").Append(c.Visible ? "true" : "false").Append(",\n");
            WriteControls(sb, c.Controls, indent + 4);
            sb.Append(pad).Append(i < controls.Count - 1 ? "  },\n" : "  }\n");
        }

        sb.Append(pad).Append("]\n");
    }

    /// <summary>UIF-320 strings: <c>"</c> and <c>\</c> escaped, control characters as <c>\u00XX</c>, everything else literally.</summary>
    private static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case < ' ': sb.Append("\\u00").Append(((int)ch).ToString("X2", CultureInfo.InvariantCulture)); break;
                default: sb.Append(ch); break;
            }
        }

        return sb.Append('"').ToString();
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(object value) => value is bool b ? (b ? "true" : "false") : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    private static JsonDocument ParseJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new FormatException("not a layout dump: " + e.Message, e);
        }
    }

    private static LayoutDumpDocument ReadDocument(JsonElement e) =>
        new(Int(e, "formatVersion"), Int(e, "dpi"), [.. Array(e, "forms").Select(f => new LayoutDumpForm(
            Str(f, "class"), Str(f, "name"), Int(f, "clientWidth"), Int(f, "clientHeight"), ReadControls(f)))]);

    private static List<LayoutDumpControl> ReadControls(JsonElement e) =>
        [.. Array(e, "controls").Select(c => new LayoutDumpControl(Str(c, "class"), Str(c, "name"), Int(c, "left"), Int(c, "top"),
            Int(c, "width"), Int(c, "height"), Bool(c, "visible"), ReadControls(c)))];

    private static JsonElement Member(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : throw new FormatException($"layout dump: missing \"{name}\"");

    private static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        Member(e, name) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : throw new FormatException($"layout dump: \"{name}\" is not an array");

    private static int Int(JsonElement e, string name) =>
        Member(e, name) is { ValueKind: JsonValueKind.Number } n && n.TryGetInt32(out var v) ? v : throw new FormatException($"layout dump: \"{name}\" is not an integer");

    private static string Str(JsonElement e, string name) =>
        Member(e, name) is { ValueKind: JsonValueKind.String } s ? s.GetString()! : throw new FormatException($"layout dump: \"{name}\" is not a string");

    private static bool Bool(JsonElement e, string name) => Member(e, name).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new FormatException($"layout dump: \"{name}\" is not a Boolean"),
    };
}
