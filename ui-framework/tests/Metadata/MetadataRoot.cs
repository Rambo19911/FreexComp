using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace FreexComp.MetadataTesting;

/// <summary>One <c>*.components.json</c> file as parsed (component-metadata.md §2); <see cref="Json"/> keeps the raw document.</summary>
public sealed record MetadataFile(string Name, string Path, string Text, JsonElement Json)
{
    public string Unit => Json.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString()! : "";

    public IEnumerable<JsonElement> Components =>
        Json.TryGetProperty("components", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray() : [];

    public IEnumerable<JsonProperty> Types =>
        Json.TryGetProperty("types", out var t) && t.ValueKind == JsonValueKind.Object ? t.EnumerateObject() : [];

    public IEnumerable<JsonProperty> EventSignatures =>
        Json.TryGetProperty("eventSignatures", out var t) && t.ValueKind == JsonValueKind.Object ? t.EnumerateObject() : [];

    /// <summary>The optional <c>eventSignatureUnits</c> object (component-metadata.md §5): signature type → declaring unit.</summary>
    public IEnumerable<JsonProperty> EventSignatureUnits =>
        Json.TryGetProperty("eventSignatureUnits", out var t) && t.ValueKind == JsonValueKind.Object ? t.EnumerateObject() : [];
}

/// <summary>A component entry with the file that declares it.</summary>
public sealed record MetadataComponent(string Class, string? Ancestor, MetadataFile File, JsonElement Json)
{
    public bool Abstract => Json.TryGetProperty("abstract", out var a) && a.ValueKind == JsonValueKind.True;

    public IEnumerable<JsonElement> Properties => Array(Json, "properties");

    public IEnumerable<JsonElement> Events => Array(Json, "events");

    internal static IEnumerable<JsonElement> Array(JsonElement e, string key) =>
        e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray() : [];
}

/// <summary>
/// One metadata root (<c>ui-framework/design/</c>): <c>index.json</c> and the files it lists, in load order (component-metadata.md §1).
/// Loading never throws for bad content: problems are collected in <see cref="LoadProblems"/>.
/// </summary>
public sealed class MetadataRoot
{
    /// <summary>The built-in root of every chain (component-metadata.md §2.1); no file declares it.</summary>
    public const string BuiltInRoot = "TComponent";

    private MetadataRoot(string directory) => Directory = directory;

    public string Directory { get; }

    public JsonElement? Index { get; private set; }

    public string? IndexText { get; private set; }

    public ImmutableArray<MetadataFile> Files { get; private set; } = [];

    public ImmutableArray<string> LoadProblems { get; private set; } = [];

    public ImmutableArray<string> PalettePages =>
        Index is { } i && i.TryGetProperty("palettePages", out var p) && p.ValueKind == JsonValueKind.Array
            ? [.. p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)]
            : [];

    /// <summary>Every component entry of every file in load order (first declaration of a class wins).</summary>
    public ImmutableArray<MetadataComponent> Components { get; private set; } = [];

    public MetadataComponent? Find(string className) =>
        Components.FirstOrDefault(c => string.Equals(c.Class, className, StringComparison.OrdinalIgnoreCase));

    public MetadataFile? FindFile(string unit) =>
        Files.FirstOrDefault(f => string.Equals(f.Name, unit + ".components.json", StringComparison.Ordinal));

    public static readonly JsonDocumentOptions Strict = new() { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false };

    public static MetadataRoot Load(string directory)
    {
        var root = new MetadataRoot(directory);
        var problems = new List<string>();
        var indexPath = System.IO.Path.Combine(directory, "index.json");
        if (!File.Exists(indexPath))
        {
            root.LoadProblems = [$"index.json: missing in {directory}"];
            return root;
        }

        root.IndexText = Encoding.UTF8.GetString(File.ReadAllBytes(indexPath));
        if (Parse("index.json", root.IndexText, problems) is not { } index)
        {
            root.LoadProblems = [.. problems];
            return root;
        }

        root.Index = index;
        var files = new List<MetadataFile>();
        if (index.TryGetProperty("files", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in list.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                {
                    problems.Add("index.json: files: entries must be strings");
                    continue;
                }

                var name = entry.GetString()!;
                var path = System.IO.Path.Combine(directory, name);
                if (!File.Exists(path))
                {
                    problems.Add($"index.json: listed file {name} does not exist");
                    continue;
                }

                var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
                if (Parse(name, text, problems) is { } json)
                {
                    files.Add(new MetadataFile(name, path, text, json));
                }
            }
        }

        root.Files = [.. files];
        var components = new List<MetadataComponent>();
        foreach (var file in files)
        {
            foreach (var c in file.Components)
            {
                var cls = c.TryGetProperty("class", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString()! : "";
                var anc = c.TryGetProperty("ancestor", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                if (components.Any(x => string.Equals(x.Class, cls, StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add($"{file.Name}: {cls}: duplicate class (first declaration wins)");
                    continue;
                }

                components.Add(new MetadataComponent(cls, anc, file, c));
            }
        }

        root.Components = [.. components];
        root.LoadProblems = [.. problems];
        return root;
    }

    private static JsonElement? Parse(string name, string text, List<string> problems)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, Strict);
            return doc.RootElement.Clone();
        }
        catch (JsonException e)
        {
            problems.Add($"{name}: not strict JSON: {e.Message}");
            return null;
        }
    }

    /// <summary>The ancestor chain of <paramref name="c"/> (itself first) through declared classes; stops at an undeclared ancestor.</summary>
    public IEnumerable<MetadataComponent> Chain(MetadataComponent c)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var k = c; k is not null && seen.Add(k.Class); k = k.Ancestor is { } a ? Find(a) : null)
        {
            yield return k;
        }
    }

    /// <summary>
    /// component-metadata.md §2.2: the effective list of <paramref name="key"/> (<c>properties</c> or <c>events</c>): the ancestor's
    /// list, then own entries; a redeclaration replaces the inherited entry in place, merging keys (redeclared keys win).
    /// </summary>
    public ImmutableArray<Dictionary<string, JsonElement>> Effective(MetadataComponent c, string key)
    {
        var list = new List<Dictionary<string, JsonElement>>();
        foreach (var k in Chain(c).Reverse())
        {
            foreach (var entry in MetadataComponent.Array(k.Json, key))
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var own = entry.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                var name = own.TryGetValue("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "";
                var at = list.FindIndex(x => x.TryGetValue("name", out var m) && string.Equals(m.GetString(), name, StringComparison.OrdinalIgnoreCase));
                if (at < 0)
                {
                    list.Add(own);
                    continue;
                }

                var merged = new Dictionary<string, JsonElement>(list[at], StringComparer.Ordinal);
                foreach (var (k2, v) in own)
                {
                    merged[k2] = v;
                }

                list[at] = merged;
            }
        }

        return [.. list];
    }

    /// <summary>A name under <c>types</c> of <paramref name="file"/> or of a file loaded before it.</summary>
    public JsonElement? FindType(MetadataFile file, string name)
    {
        foreach (var f in Files)
        {
            foreach (var t in f.Types)
            {
                if (string.Equals(t.Name, name, StringComparison.Ordinal))
                {
                    return t.Value;
                }
            }

            if (ReferenceEquals(f, file))
            {
                break;
            }
        }

        return null;
    }
}
