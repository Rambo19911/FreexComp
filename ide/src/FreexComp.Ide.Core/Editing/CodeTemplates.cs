using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Editing;

/// <summary>A code template (IDE-ED-060): abbreviation (unique per language, case-insensitive), description, language, body.</summary>
public sealed record CodeTemplate(string Abbreviation, string Description, string Language, string Body);

/// <summary>A field of an expansion: its name and the occurrences (offset, length) relative to the expansion; the first is the edit point.</summary>
public sealed record TemplateField(string Name, IReadOnlyList<(int Offset, int Length)> Occurrences);

/// <summary>The text of an expanded template, its fields in order of first occurrence and the <c>$end$</c> offset.</summary>
public sealed record TemplateExpansion(string Text, IReadOnlyList<TemplateField> Fields, int EndOffset);

/// <summary>IDE-ED-060: expands a template body.</summary>
public static class CodeTemplateExpander
{
    /// <param name="lineIndent">The indentation of the line where the template is expanded (prefixed to every later line).</param>
    /// <param name="indentUnit">One indent unit (a leading tab of a body line).</param>
    /// <param name="newLine">The document's line ending.</param>
    /// <param name="selection">The text for <c>$selection$</c> (Surround With, P8+).</param>
    public static TemplateExpansion Expand(string body, string lineIndent, string indentUnit, string newLine, string selection = "")
    {
        ArgumentNullException.ThrowIfNull(body);
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in Placeholders(body))
        {
            if (value is not null && !defaults.ContainsKey(name))
                defaults[name] = value;
        }

        var text = new StringBuilder();
        var fields = new List<(string Name, List<(int, int)> Occurrences)>();
        int? end = null;
        var lines = body.ReplaceLineEndings("\n").Split('\n');
        for (var l = 0; l < lines.Length; l++)
        {
            var line = lines[l];
            if (l > 0)
            {
                text.Append(newLine).Append(lineIndent);
                var tabs = 0;
                while (tabs < line.Length && line[tabs] == '\t')
                    tabs++;
                for (var t = 0; t < tabs; t++)
                    text.Append(indentUnit);
                line = line[tabs..];
            }

            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] != '$')
                {
                    text.Append(line[i]);
                    continue;
                }

                if (i + 1 < line.Length && line[i + 1] == '$')
                {
                    text.Append('$');
                    i++;
                    continue;
                }

                var close = line.IndexOf('$', i + 1);
                if (close < 0 || ParsePlaceholder(line[(i + 1)..close]) is not { } placeholder)
                {
                    text.Append('$');
                    continue;
                }

                i = close;
                switch (placeholder.Name)
                {
                    case "end":
                        end ??= text.Length;
                        break;
                    case "selection":
                        text.Append(selection);
                        break;
                    default:
                        var value = defaults.GetValueOrDefault(placeholder.Name, "");
                        var field = fields.Find(f => f.Name == placeholder.Name);
                        if (field.Name is null)
                            fields.Add(field = (placeholder.Name, []));
                        field.Occurrences.Add((text.Length, value.Length));
                        text.Append(value);
                        break;
                }
            }
        }

        return new TemplateExpansion(text.ToString(), [.. fields.Select(f => new TemplateField(f.Name, f.Occurrences))], end ?? text.Length);
    }

    private static IEnumerable<(string Name, string? Default)> Placeholders(string body)
    {
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != '$')
                continue;
            if (i + 1 < body.Length && body[i + 1] == '$')
            {
                i++;
                continue;
            }

            var close = body.IndexOf('$', i + 1);
            if (close < 0 || body.AsSpan(i + 1, close - i - 1).IndexOfAny('\n', '\r') >= 0 || ParsePlaceholder(body[(i + 1)..close]) is not { } p)
                continue;
            yield return p;
            i = close;
        }
    }

    /// <summary><c>name</c> or <c>name:default</c> with an identifier name; null when it is not a placeholder.</summary>
    private static (string Name, string? Default)? ParsePlaceholder(string inner)
    {
        var colon = inner.IndexOf(':', StringComparison.Ordinal);
        var name = colon < 0 ? inner : inner[..colon];
        if (!IsIdentifier(name))
            return null;
        return (name, colon < 0 ? null : inner[(colon + 1)..]);
    }

    /// <summary><c>[A-Za-z_][A-Za-z0-9_]*</c> (dialogs.md §9.3).</summary>
    public static bool IsIdentifier(string text) =>
        text.Length > 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_') && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}

/// <summary>
/// The code templates (settings.md §7, IDE-SET-080): the embedded built-ins, replaced or hidden by <c>R\templates.json</c>
/// (user-added templates, modified built-ins with the same abbreviation and language, and <c>"deleted": true</c>
/// tombstones). Loaded on first use; saved atomically by <see cref="Save"/>.
/// </summary>
public sealed class CodeTemplateService(IUserPaths paths, IFileSystem fileSystem)
{
    private const string FileName = "templates.json";
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly Lazy<IReadOnlyList<CodeTemplate>> BuiltInList = new(LoadBuiltIns);
    private List<CodeTemplate>? _templates;

    /// <summary>The embedded built-in templates in their resource order.</summary>
    public static IReadOnlyList<CodeTemplate> BuiltIns => BuiltInList.Value;

    /// <summary>The templates changed (a save).</summary>
    public event EventHandler? Changed;

    /// <summary>The effective templates: built-ins (in order, replaced where modified, without the deleted ones), then the user's own.</summary>
    public IReadOnlyList<CodeTemplate> Templates => _templates ??= Load();

    private string FilePath => Path.Combine(paths.RoamingDirectory, FileName);

    /// <summary>The template of <paramref name="language"/> with the abbreviation (case-insensitive); null when none.</summary>
    public CodeTemplate? Find(string abbreviation, string language = "pascal") =>
        Templates.FirstOrDefault(t => t.Language == language && t.Abbreviation.Equals(abbreviation, StringComparison.OrdinalIgnoreCase));

    /// <summary>The built-in with that abbreviation and language, or null.</summary>
    public static CodeTemplate? BuiltIn(string abbreviation, string language = "pascal") =>
        BuiltIns.FirstOrDefault(t => t.Language == language && t.Abbreviation.Equals(abbreviation, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Replaces the effective templates with <paramref name="templates"/> and writes the differences from the built-ins
    /// to <c>templates.json</c> (an unchanged built-in is not written; a missing one gets a tombstone).
    /// </summary>
    public void Save(IReadOnlyList<CodeTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        var entries = new List<Dictionary<string, object>>();
        foreach (var template in templates)
        {
            if (BuiltIn(template.Abbreviation, template.Language) == template)
                continue; // an unchanged built-in
            entries.Add(Entry(template, deleted: false));
        }

        foreach (var builtIn in BuiltIns.Where(b => !templates.Any(t => t.Language == b.Language && t.Abbreviation.Equals(b.Abbreviation, StringComparison.OrdinalIgnoreCase))))
            entries.Add(Entry(builtIn with { Body = "" }, deleted: true));

        var json = JsonSerializer.Serialize(new Dictionary<string, object> { ["formatVersion"] = 1, ["templates"] = entries }, WriteOptions);
        fileSystem.CreateDirectory(paths.RoamingDirectory);
        var temp = FilePath + ".tmp";
        fileSystem.WriteAllBytes(temp, Encoding.UTF8.GetBytes(json.ReplaceLineEndings("\n") + "\n"));
        fileSystem.MoveFile(temp, FilePath, overwrite: true);
        _templates = [.. templates];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static Dictionary<string, object> Entry(CodeTemplate t, bool deleted) => new()
    {
        ["abbreviation"] = t.Abbreviation, ["description"] = t.Description, ["language"] = t.Language, ["body"] = t.Body, ["deleted"] = deleted,
    };

    private List<CodeTemplate> Load()
    {
        var result = BuiltIns.ToList();
        if (!fileSystem.FileExists(FilePath))
            return result;
        IReadOnlyList<(CodeTemplate Template, bool Deleted)> user;
        try
        {
            user = Parse(fileSystem.ReadAllBytes(FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return result; // unreadable: the built-ins only
        }

        foreach (var (template, deleted) in user)
        {
            var at = result.FindIndex(t => t.Language == template.Language && t.Abbreviation.Equals(template.Abbreviation, StringComparison.OrdinalIgnoreCase));
            if (deleted)
            {
                if (at >= 0)
                    result.RemoveAt(at);
            }
            else if (at >= 0)
            {
                result[at] = template;
            }
            else
            {
                result.Add(template);
            }
        }

        return result;
    }

    private static IReadOnlyList<(CodeTemplate, bool)> Parse(byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.StartsWith(Encoding.UTF8.Preamble))
            span = span[Encoding.UTF8.Preamble.Length..];
        using var document = JsonDocument.Parse(span.ToArray());
        var list = new List<(CodeTemplate, bool)>();
        foreach (var entry in document.RootElement.GetProperty("templates").EnumerateArray())
        {
            string Text(string name) => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
            var abbreviation = Text("abbreviation");
            if (!CodeTemplateExpander.IsIdentifier(abbreviation))
                continue;
            var language = Text("language");
            var deleted = entry.TryGetProperty("deleted", out var d) && d.ValueKind == JsonValueKind.True;
            list.Add((new CodeTemplate(abbreviation, Text("description"), language.Length > 0 ? language : "pascal", Text("body")), deleted));
        }

        return list;
    }

    private static IReadOnlyList<CodeTemplate> LoadBuiltIns()
    {
        using var stream = typeof(CodeTemplateService).Assembly.GetManifestResourceStream("BuiltInTemplates.json")
            ?? throw new InvalidOperationException("BuiltInTemplates.json is not embedded.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return [.. Parse(memory.ToArray()).Select(e => e.Item1)];
    }
}

/// <summary>
/// Field mode after an expansion (IDE-ED-065): the fields' occurrences as ranges that follow the edits, the current
/// field and the <c>$end$</c> position. An edit inside an occurrence grows or shrinks it; any other edit ends field mode.
/// </summary>
internal sealed class TemplateFieldMode
{
    private readonly List<List<int[]>> _fields; // per field, per occurrence: [start, end]
    private int _end;

    public TemplateFieldMode(int start, TemplateExpansion expansion)
    {
        _fields = [.. expansion.Fields.Select(f => f.Occurrences.Select(o => new[] { start + o.Offset, start + o.Offset + o.Length }).ToList())];
        _end = start + expansion.EndOffset;
    }

    public int Current { get; set; }

    public int Count => _fields.Count;

    public bool IsActive { get; private set; } = true;

    /// <summary>The buffer version after the last field edit (field edits join one undo step); -1 = none yet.</summary>
    public long LastEditVersion { get; set; } = -1;

    /// <summary>The field and occurrence the last edit was in, to mirror (null = none pending).</summary>
    public (int Field, int Occurrence)? Edited { get; set; }

    public bool Mirroring { get; set; }

    public int End => _end;

    /// <summary>The primary (first) occurrence of field <paramref name="index"/>.</summary>
    public (int Start, int End) Primary(int index) => (_fields[index][0][0], _fields[index][0][1]);

    public IEnumerable<(int Start, int End)> AllOccurrences => _fields.SelectMany(f => f.Select(o => (o[0], o[1])));

    public IReadOnlyList<(int Start, int End)> Occurrences(int field) => [.. _fields[field].Select(o => (o[0], o[1]))];

    public bool Contains(int offset) => _fields.Any(f => f.Any(o => o[0] <= offset && offset <= o[1]));

    public void Deactivate() => IsActive = false;

    /// <summary>Follows one buffer change; false when it was outside all fields (field mode ends).</summary>
    public bool OnChanged(int offset, int removed, int inserted)
    {
        var delta = inserted - removed;
        (int, int)? inside = null;
        for (var f = 0; f < _fields.Count && inside is null; f++)
        {
            for (var o = 0; o < _fields[f].Count; o++)
            {
                var range = _fields[f][o];
                if (range[0] <= offset && offset + removed <= range[1])
                {
                    inside = (f, o);
                    break;
                }
            }
        }

        if (inside is not { } hit)
            return false;
        var edited = _fields[hit.Item1][hit.Item2];
        var editStart = edited[0];
        foreach (var range in _fields.SelectMany(f => f))
        {
            if (ReferenceEquals(range, edited))
            {
                range[1] += delta;
            }
            else if (range[0] >= offset + removed)
            {
                range[0] += delta;
                range[1] += delta;
            }
        }

        if (_end >= offset + removed && !(removed == 0 && _end == offset && editStart == offset))
            _end += delta;
        if (!Mirroring)
            Edited = hit;
        return true;
    }
}
