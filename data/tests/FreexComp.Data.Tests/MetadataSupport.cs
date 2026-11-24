using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FreexComp.Compiler;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Data.Tests;

/// <summary>One <c>index.json</c> or <c>*.components.json</c> file as read; <see cref="IsData"/> = it belongs to <c>data/design</c>.</summary>
public sealed record MetaFile(string Name, string Path, string Text, JsonElement Json, bool IsData)
{
    public string Unit => Json.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString()! : "";

    public IEnumerable<JsonProperty> Section(string key) =>
        Json.TryGetProperty(key, out var t) && t.ValueKind == JsonValueKind.Object ? t.EnumerateObject() : [];
}

/// <summary>A component entry with the file that declares it.</summary>
public sealed record MetaComponent(string Class, string? Ancestor, MetaFile File, JsonElement Json)
{
    public bool Abstract => Json.TryGetProperty("abstract", out var a) && a.ValueKind == JsonValueKind.True;

    public string? Text(string key) => Json.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// The metadata roots in load order (component-metadata.md §1): <c>ui-framework/design</c>, then <c>data/design</c>, so that
/// data entries can name framework classes and types. Loading never throws for bad content: problems are collected.
/// </summary>
public sealed class MetadataCatalog
{
    public const string BuiltInRoot = "TComponent";

    public static readonly JsonDocumentOptions Strict = new() { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false };

    private MetadataCatalog(string dataDirectory) => DataDirectory = dataDirectory;

    public string DataDirectory { get; }

    /// <summary>The index of the data root; null when missing or unreadable.</summary>
    public MetaFile? Index { get; private set; }

    public ImmutableArray<MetaFile> Files { get; private set; } = [];

    public ImmutableArray<MetaComponent> Components { get; private set; } = [];

    public ImmutableArray<string> Problems { get; private set; } = [];

    public IEnumerable<MetaFile> DataFiles => Files.Where(f => f.IsData);

    public IEnumerable<MetaComponent> DataComponents => Components.Where(c => c.File.IsData);

    public static MetadataCatalog Load(string frameworkDirectory, string dataDirectory)
    {
        var catalog = new MetadataCatalog(dataDirectory);
        var problems = new List<string>();
        var files = new List<MetaFile>();
        foreach (var (directory, isData) in new[] { (frameworkDirectory, false), (dataDirectory, true) })
        {
            var index = Read(directory, "index.json", isData, problems);
            if (isData)
            {
                catalog.Index = index;
            }

            if (index is null || !index.Json.TryGetProperty("files", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var name in list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!))
            {
                if (Read(directory, name, isData, problems) is { } file)
                {
                    files.Add(file);
                }
            }
        }

        var components = new List<MetaComponent>();
        foreach (var file in files)
        {
            if (!file.Json.TryGetProperty("components", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var c in array.EnumerateArray())
            {
                var cls = c.TryGetProperty("class", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString()! : "";
                var ancestor = c.TryGetProperty("ancestor", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                if (components.Any(x => string.Equals(x.Class, cls, StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add($"{file.Name}: {cls}: duplicate class");
                    continue;
                }

                components.Add(new MetaComponent(cls, ancestor, file, c));
            }
        }

        catalog.Files = [.. files];
        catalog.Components = [.. components];
        catalog.Problems = [.. problems];
        return catalog;
    }

    private static MetaFile? Read(string directory, string name, bool isData, List<string> problems)
    {
        var path = System.IO.Path.Combine(directory, name);
        if (!File.Exists(path))
        {
            problems.Add($"{name}: missing in {directory}");
            return null;
        }

        var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        try
        {
            using var doc = JsonDocument.Parse(text, Strict);
            return new MetaFile(name, path, text, doc.RootElement.Clone(), isData);
        }
        catch (JsonException e)
        {
            problems.Add($"{name}: not strict JSON: {e.Message}");
            return null;
        }
    }

    public MetaComponent? Find(string className) =>
        Components.FirstOrDefault(c => string.Equals(c.Class, className, StringComparison.OrdinalIgnoreCase));

    /// <summary>The ancestor chain of <paramref name="c"/> (itself first) through declared classes.</summary>
    public IEnumerable<MetaComponent> Chain(MetaComponent c)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var k = c; k is not null && seen.Add(k.Class); k = k.Ancestor is { } a ? Find(a) : null)
        {
            yield return k;
        }
    }

    /// <summary>
    /// component-metadata.md §2.2: the effective <c>properties</c> or <c>events</c> list: the ancestor's list, then own entries; a
    /// redeclaration replaces the inherited entry in place, merging keys (redeclared keys win).
    /// </summary>
    public ImmutableArray<Dictionary<string, JsonElement>> Effective(MetaComponent c, string key)
    {
        var list = new List<Dictionary<string, JsonElement>>();
        foreach (var k in Chain(c).Reverse())
        {
            foreach (var entry in Array(k.Json, key).Where(e => e.ValueKind == JsonValueKind.Object))
            {
                Merge(list, entry);
            }
        }

        return [.. list];
    }

    /// <summary>A name under <c>types</c>: the first declaration in load order.</summary>
    public JsonElement? FindType(string name)
    {
        foreach (var t in Files.SelectMany(f => f.Section("types")))
        {
            if (string.Equals(t.Name, name, StringComparison.Ordinal))
            {
                return t.Value;
            }
        }

        return null;
    }

    /// <summary>The sub-properties of an object type, its metadata ancestor's first (component-metadata.md §4).</summary>
    public List<Dictionary<string, JsonElement>> ObjectProperties(JsonElement type)
    {
        var list = type.TryGetProperty("ancestor", out var a) && a.ValueKind == JsonValueKind.String && FindType(a.GetString()!) is { } parent
            ? ObjectProperties(parent)
            : [];
        foreach (var entry in Array(type, "properties").Where(e => e.ValueKind == JsonValueKind.Object))
        {
            Merge(list, entry);
        }

        return list;
    }

    private static void Merge(List<Dictionary<string, JsonElement>> list, JsonElement entry)
    {
        var own = entry.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        var name = own.TryGetValue("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "";
        var at = list.FindIndex(x => x.TryGetValue("name", out var m) && string.Equals(m.GetString(), name, StringComparison.OrdinalIgnoreCase));
        if (at < 0)
        {
            list.Add(own);
            return;
        }

        var merged = new Dictionary<string, JsonElement>(list[at], StringComparer.Ordinal);
        foreach (var (key, value) in own)
        {
            merged[key] = value;
        }

        list[at] = merged;
    }

    public static IEnumerable<JsonElement> Array(JsonElement e, string key) =>
        e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray() : [];
}

/// <summary>One published property of a source class in metadata terms; events have <see cref="IsEvent"/> and the method-pointer type name.</summary>
public sealed record PublishedProperty(string Name, string Type, string? Kind, bool IsEvent, PropertySymbol Symbol)
{
    /// <summary>For a collection: the class of its items (the type of the collection class's <c>Items</c> array property).</summary>
    public string? ItemClass { get; init; }
}

/// <summary>
/// The Pascal side of <c>MetadataMatchesSource</c>: the data units parsed with the compiler API (the framework and RTL units
/// they use are found through the search path), with the mapping of Pascal property types to metadata types.
/// </summary>
public sealed partial class PascalSource
{
    private static readonly string[] NamedOrdinals = ["TColor", "TCursor", "TShortCut"];

    private static readonly string[] RtlTypeUnits = ["System", "Types", "Classes", "SysUtils"];

    private readonly Compilation compilation;
    private readonly string dataDirectory;
    private readonly string frameworkDirectory;
    private readonly string rtlDirectory;

    public PascalSource(string dataDirectory, string frameworkDirectory, string rtlDirectory)
    {
        this.dataDirectory = dataDirectory;
        this.frameworkDirectory = frameworkDirectory;
        this.rtlDirectory = rtlDirectory;
        compilation = Compilation.Create(
            new CompilationOptions
            {
                MainFile = Directory.EnumerateFiles(dataDirectory, "*.pas").Order(StringComparer.Ordinal).First(),
                SearchPaths = [dataDirectory, frameworkDirectory],
                RtlDirectory = rtlDirectory,
            },
            [],
            PhysicalFileSystem.Instance);
    }

    public string UnitPath(string unit) => Path.Combine(dataDirectory, unit + ".pas");

    public ImmutableArray<Diagnostic> Errors(string unit) =>
        File.Exists(UnitPath(unit)) ? [.. compilation.GetDiagnostics(UnitPath(unit)).Where(d => d.Severity == DiagnosticSeverity.Error)] : [];

    private IEnumerable<TypeSymbol> InterfaceTypes(string path) =>
        compilation.GetSemanticModel(path).Unit.InterfaceMembers.OfType<TypeSymbol>().Where(t => t is not ClassTypeSymbol { IsForward: true });

    /// <summary>A class declared in the interface of the data unit <paramref name="unit"/>, or null.</summary>
    public ClassTypeSymbol? FindClass(string unit, string name) =>
        File.Exists(UnitPath(unit))
            ? InterfaceTypes(UnitPath(unit)).OfType<ClassTypeSymbol>().FirstOrDefault(c => c.Name == name)
            : null;

    /// <summary>The types named <paramref name="name"/> declared in an interface section: data units, framework units, then RTL units.</summary>
    public ImmutableArray<TypeSymbol> FindTypes(string name)
    {
        var paths = Directory.EnumerateFiles(dataDirectory, "*.pas").Order(StringComparer.Ordinal)
            .Concat(Directory.EnumerateFiles(frameworkDirectory, "*.pas").Order(StringComparer.Ordinal))
            .Concat(RtlTypeUnits.Select(u => Path.Combine(rtlDirectory, u + ".pas")).Where(File.Exists));
        return [.. paths.SelectMany(InterfaceTypes).Where(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>The interface of <paramref name="unit"/> (data, framework or RTL) declares a type named <paramref name="name"/>.</summary>
    public bool DeclaresType(string unit, string name)
    {
        var path = new[] { dataDirectory, frameworkDirectory, rtlDirectory }.Select(d => Path.Combine(d, unit + ".pas")).FirstOrDefault(File.Exists);
        return unit.Length > 0 && path is not null && InterfaceTypes(path).Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsComponent(ClassTypeSymbol c) => c.SelfAndAncestors().Any(a => a.Name == "TComponent");

    /// <summary>
    /// The effective published list of <paramref name="c"/> (ancestor first, a redeclaration keeps the ancestor's position)
    /// without <c>TComponent</c>'s <c>Name</c> and <c>Tag</c>, which the schema builds in.
    /// </summary>
    public static ImmutableArray<PublishedProperty> Effective(ClassTypeSymbol c)
    {
        var list = new List<PublishedProperty>();
        foreach (var k in c.SelfAndAncestors().Reverse().Where(k => k.Name != "TComponent"))
        {
            foreach (var p in k.Properties.Where(p => p.Visibility == Visibility.Published && !p.IsClassProperty && !p.IsArrayProperty).Select(Map))
            {
                var at = list.FindIndex(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                if (at >= 0)
                {
                    list[at] = p;
                }
                else
                {
                    list.Add(p);
                }
            }
        }

        return [.. list];
    }

    private static string? ItemClassOf(ClassTypeSymbol collection) =>
        collection.SelfAndAncestors().SelectMany(c => c.Properties)
            .FirstOrDefault(p => p.IsArrayProperty && string.Equals(p.Name, "Items", StringComparison.OrdinalIgnoreCase))?.Type.Name;

    /// <summary>metadata-authoring.md §2.2: the metadata <c>type</c> and <c>kind</c> of a Pascal property type.</summary>
    public static PublishedProperty Map(PropertySymbol p)
    {
        var t = p.Type;
        if (t is MethodPointerTypeSymbol)
        {
            return new(p.Name, t.Name, null, true, p);
        }

        if (NamedOrdinals.Contains(t.Name, StringComparer.Ordinal))
        {
            return new(p.Name, t.Name, null, false, p);
        }

        switch (t.TypeKind)
        {
            case TypeKind.Boolean:
                return new(p.Name, "Boolean", null, false, p);
            case TypeKind.Char:
                return new(p.Name, "Char", null, false, p);
            case TypeKind.Real:
                return new(p.Name, "Double", null, false, p);
            case TypeKind.String:
            case TypeKind.ShortString:
                return new(p.Name, "string", null, false, p);
            case TypeKind.Enum:
            case TypeKind.Set:
                return new(p.Name, t.Name, null, false, p);
            case TypeKind.Integer:
            case TypeKind.Subrange:
                return t is EnumTypeSymbol
                    ? new(p.Name, t.Name, null, false, p)
                    : new(p.Name, t.Size == 8 ? "Int64" : "Integer", null, false, p);
            case TypeKind.Class when t is ClassTypeSymbol k:
                if (k.SelfAndAncestors().Any(a => a.Name == "TStrings"))
                {
                    return new(p.Name, "TStrings", null, false, p);
                }

                var kind = k.SelfAndAncestors().Any(a => a.Name == "TComponent") ? "component"
                    : k.SelfAndAncestors().Any(a => a.Name == "TCollection") ? "collection"
                    : "object";
                return new(p.Name, k.Name, kind, false, p) { ItemClass = kind == "collection" ? ItemClassOf(k) : null };
            default:
                return new(p.Name, "?" + t.Name, null, false, p);                 // unmapped: always a mismatch
        }
    }

    /// <summary>
    /// The declaration text <c>procedure(…)</c> of a method-pointer type, normalised as metadata writes it: single spaces,
    /// <c>; </c> between parameters, no <c>of object</c>.
    /// </summary>
    public static string Signature(MethodPointerTypeSymbol t)
    {
        var loc = t.Locations[0];
        var text = File.ReadAllText(loc.Path);
        var start = text.IndexOf('(', loc.Span.Start + loc.Span.Length);
        var depth = 0;
        var end = start;
        for (; end < text.Length; end++)
        {
            depth += text[end] == '(' ? 1 : text[end] == ')' ? -1 : 0;
            if (depth == 0)
            {
                break;
            }
        }

        var keyword = Keyword().Match(text[(loc.Span.Start + loc.Span.Length)..start]).Value.ToLowerInvariant();
        var inner = Spaces().Replace(text[(start + 1)..end], " ").Trim();
        inner = Semicolon().Replace(inner, "; ");
        inner = Colon().Replace(inner, ": ");
        inner = Comma().Replace(inner, ", ");
        return $"{keyword}({inner})";
    }

    [GeneratedRegex("procedure|function", RegexOptions.IgnoreCase)]
    private static partial Regex Keyword();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s*;\s*")]
    private static partial Regex Semicolon();

    [GeneratedRegex(@"\s*:\s*")]
    private static partial Regex Colon();

    [GeneratedRegex(@"\s*,\s*")]
    private static partial Regex Comma();
}

/// <summary>The checks behind the metadata tests of the data area (datasource-and-controls.md §5); each returns problem lines.</summary>
public static class MetadataChecks
{
    public static readonly string[] Categories = ["Action", "Appearance", "Behavior", "Data", "Layout", "Input", "Misc"];

    public static readonly string[] Editors = ["color", "font", "strings", "picture", "filename", "shortcut", "collection", "fieldname", "tablename"];

    public static readonly string[] BuiltInTypes = ["Integer", "Int64", "Double", "Boolean", "string", "Char", "TColor", "TCursor", "TStrings", "TShortCut"];

    /// <summary>Predeclared types: a handler signature never needs a unit for them (component-metadata.md §5).</summary>
    private static readonly HashSet<string> SystemTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Integer", "Cardinal", "ShortInt", "SmallInt", "LongInt", "Int64", "Byte", "Word", "LongWord", "UInt64", "NativeInt", "NativeUInt",
        "Boolean", "ByteBool", "WordBool", "LongBool", "Char", "AnsiChar", "WideChar", "string", "AnsiString", "UnicodeString", "WideString",
        "ShortString", "Single", "Double", "Extended", "Currency", "Real", "Comp", "Pointer", "Variant", "TObject", "TClass",
    };

    /// <summary>
    /// Source published properties that metadata leaves out. DAT-282: the parameter collection of a query is a plain object,
    /// not design-time data in Phase 7 (card DAT-863).
    /// </summary>
    public static readonly (string Class, string Property)[] NotInMetadata = [("TSQLiteQuery", "Params")];

    public static string Report(IEnumerable<string> problems) => string.Join("\n", problems);

    private static string? Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Str(Dictionary<string, JsonElement> e, string key) => e.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ---- files, index, schema ----

    /// <summary>
    /// The index and every listed file exist and are strict JSON written as UTF-8 without BOM, with LF line ends; no components
    /// file is left unlisted; ancestors are loaded first; names, pages, orders, icons, types, categories and editors are valid.
    /// </summary>
    public static List<string> FilesAndSchema(MetadataCatalog catalog, IReadOnlyList<string> expectedPages, IReadOnlyList<string> expectedFiles)
    {
        var problems = new List<string>(catalog.Problems);
        if (catalog.Index is not { } index)
        {
            return problems;
        }

        CheckText("index.json", index.Path, problems);
        CheckKeyOrder("index.json", index.Json, ["formatVersion", "palettePages", "files"], problems);
        if (!index.Json.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1)
        {
            problems.Add("index.json: formatVersion must be 1");
        }

        var pages = MetadataCatalog.Array(index.Json, "palettePages").Select(p => p.GetString()!).ToList();
        if (!pages.SequenceEqual(expectedPages, StringComparer.Ordinal))
        {
            problems.Add($"index.json: palettePages [{string.Join(", ", pages)}], expected [{string.Join(", ", expectedPages)}]");
        }

        var listed = MetadataCatalog.Array(index.Json, "files").Select(p => p.GetString()!).ToList();
        if (!listed.SequenceEqual(expectedFiles, StringComparer.Ordinal))
        {
            problems.Add($"index.json: files [{string.Join(", ", listed)}], expected [{string.Join(", ", expectedFiles)}]");
        }

        foreach (var path in Directory.EnumerateFiles(catalog.DataDirectory, "*.json").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (name != "index.json" && !listed.Contains(name, StringComparer.Ordinal))
            {
                problems.Add($"{name} exists but is not listed in index.json");
            }
        }

        var usedIcons = new HashSet<string>(StringComparer.Ordinal);
        var orders = new Dictionary<(string Page, int Order), string>();
        foreach (var file in catalog.DataFiles)
        {
            CheckText(file.Name, file.Path, problems);
            CheckKeyOrder(file.Name, file.Json, ["formatVersion", "unit", "types", "components", "eventSignatures", "eventSignatureUnits"], problems);
            if (!file.Json.TryGetProperty("formatVersion", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != 1)
            {
                problems.Add($"{file.Name}: formatVersion must be 1");
            }

            if (file.Name != file.Unit + ".components.json")
            {
                problems.Add($"{file.Name}: unit '{file.Unit}' does not match the file name");
            }

            foreach (var key in new[] { "types", "components", "eventSignatures" })
            {
                if (!file.Json.TryGetProperty(key, out _))
                {
                    problems.Add($"{file.Name}: key '{key}' is missing");
                }
            }

            foreach (var type in file.Section("types"))
            {
                CheckType(catalog, $"{file.Name}: types.{type.Name}", type.Value, problems);
            }
        }

        foreach (var c in catalog.DataComponents)
        {
            CheckComponent(catalog, c, pages, orders, usedIcons, problems);
        }

        var prefixes = catalog.Components.Where(c => !c.Abstract).GroupBy(c => c.Text("namePrefix") ?? c.Class[1..], StringComparer.OrdinalIgnoreCase);
        foreach (var g in prefixes.Where(g => g.Count() > 1 && g.Any(c => c.File.IsData)))
        {
            problems.Add($"namePrefix {g.Key} is used by {string.Join(", ", g.Select(c => c.Class))}");
        }

        var iconDirectory = Path.Combine(catalog.DataDirectory, "icons");
        if (Directory.Exists(iconDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(iconDirectory).Order(StringComparer.Ordinal))
            {
                if (!usedIcons.Contains("icons/" + Path.GetFileName(path)))
                {
                    problems.Add($"icons/{Path.GetFileName(path)} is not used by a palette entry");
                }
            }
        }

        return problems;
    }

    private static void CheckText(string name, string path, List<string> problems)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            problems.Add($"{name}: starts with a byte-order mark");
        }

        if (bytes.Contains((byte)'\r'))
        {
            problems.Add($"{name}: contains CR (LF line ends only)");
        }

        if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
        {
            problems.Add($"{name}: must end with LF");
        }

        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (ArgumentException)
        {
            problems.Add($"{name}: is not valid UTF-8");
        }
    }

    private static void CheckKeyOrder(string at, JsonElement e, string[] order, List<string> problems)
    {
        var last = -1;
        foreach (var p in e.EnumerateObject())
        {
            var i = System.Array.IndexOf(order, p.Name);
            if (i < 0)
            {
                continue;                                  // readers ignore keys they do not know
            }

            if (i < last)
            {
                problems.Add($"{at}: key '{p.Name}' out of order");
            }

            last = Math.Max(last, i);
        }
    }

    private static readonly string[] ComponentKeys =
        ["class", "ancestor", "abstract", "palette", "kind", "defaultSize", "namePrefix", "acceptsChildren", "renderer", "designerVerbs", "properties", "events"];

    private static readonly string[] PropertyKeys =
        ["name", "type", "kind", "itemClass", "default", "category", "initial", "readOnly", "hidden", "stored", "editor", "editorOptions", "min", "max", "affectsLayout", "localizable"];

    private static void CheckComponent(MetadataCatalog catalog, MetaComponent c, List<string> pages,
        Dictionary<(string, int), string> orders, HashSet<string> usedIcons, List<string> problems)
    {
        var at = $"{c.File.Name}: {c.Class}";
        CheckKeyOrder(at, c.Json, ComponentKeys, problems);
        if (c.Ancestor is null)
        {
            problems.Add($"{at}: ancestor is missing");
        }
        else if (c.Ancestor != MetadataCatalog.BuiltInRoot)
        {
            var ancestor = catalog.Find(c.Ancestor);
            if (ancestor is null || catalog.Components.IndexOf(ancestor) > catalog.Components.IndexOf(c))
            {
                problems.Add($"{at}: ancestor {c.Ancestor} is not declared in this or an earlier-loaded file");
            }
        }

        var kind = catalog.Chain(c).Select(k => k.Text("kind")).FirstOrDefault(k => k is not null);
        if (kind is not ("control" or "wincontrol" or "container" or "nonvisual" or "form"))
        {
            problems.Add($"{at}: kind '{kind}' is not a component kind");
        }

        if (!c.Abstract)
        {
            var sized = c.Json.TryGetProperty("defaultSize", out var size) && size.ValueKind == JsonValueKind.Object
                && size.TryGetProperty("width", out var w) && w.GetInt32() > 0 && size.TryGetProperty("height", out var h) && h.GetInt32() > 0;
            if (kind == "nonvisual" ? c.Json.TryGetProperty("defaultSize", out _) : !sized)
            {
                problems.Add($"{at}: defaultSize is required for controls and omitted for non-visual components");
            }
        }

        if (c.Json.TryGetProperty("palette", out var palette))
        {
            var page = Str(palette, "page") ?? "";
            var order = palette.TryGetProperty("order", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : -1;
            var icon = Str(palette, "icon") ?? "";
            if (c.Abstract)
            {
                problems.Add($"{at}: an abstract class has no palette entry");
            }

            if (!pages.Contains(page, StringComparer.Ordinal))
            {
                problems.Add($"{at}: palette.page '{page}' is not a page of index.json");
            }

            if (!orders.TryAdd((page, order), c.Class))
            {
                problems.Add($"{at}: palette.order {order} on page '{page}' is also used by {orders[(page, order)]}");
            }

            if (icon != $"icons/{c.Class}.svg")
            {
                problems.Add($"{at}: palette.icon '{icon}', expected icons/{c.Class}.svg");
            }

            usedIcons.Add(icon);
            CheckIcon(at, Path.Combine(catalog.DataDirectory, icon.Replace('/', Path.DirectorySeparatorChar)), problems);
        }
        else if (!c.Abstract)
        {
            problems.Add($"{at}: palette is missing");
        }

        foreach (var p in MetadataCatalog.Array(c.Json, "properties"))
        {
            CheckProperty(catalog, $"{at}.{Str(p, "name")}", p, true, problems);
        }

        var signatures = c.File.Section("eventSignatures").Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var e in MetadataCatalog.Array(c.Json, "events"))
        {
            CheckKeyOrder($"{at}.{Str(e, "name")}", e, ["name", "signature", "default"], problems);
            if (Str(e, "name") is null || Str(e, "signature") is not { } signature || !signatures.Contains(signature))
            {
                problems.Add($"{at}.{Str(e, "name")}: signature must name an entry of the file's eventSignatures");
            }
        }

        var defaults = MetadataCatalog.Array(c.Json, "events").Count(e => e.TryGetProperty("default", out var d) && d.ValueKind == JsonValueKind.True);
        if (defaults > 1)
        {
            problems.Add($"{at}: more than one default event");
        }

        foreach (var verb in MetadataCatalog.Array(c.Json, "designerVerbs"))
        {
            CheckKeyOrder($"{at}: designerVerbs", verb, ["id", "caption", "command", "arg"], problems);
        }
    }

    private static void CheckProperty(MetadataCatalog catalog, string at, JsonElement p, bool component, List<string> problems)
    {
        CheckKeyOrder(at, p, PropertyKeys, problems);
        var type = Str(p, "type");
        var kind = Str(p, "kind");
        if (Str(p, "name") is null || type is null)
        {
            problems.Add($"{at}: name and type are required");
            return;
        }

        var declared = catalog.FindType(type);
        var declaredKind = declared is { } d ? Str(d, "kind") : null;
        var known = kind switch
        {
            "component" => catalog.Find(type) is not null,
            "object" => declaredKind == "object",
            "collection" => Str(p, "itemClass") is { } item && catalog.FindType(item) is { } it && Str(it, "kind") == "object",
            null => BuiltInTypes.Contains(type, StringComparer.Ordinal) || declaredKind is "enum" or "set",
            _ => false,
        };
        if (!known)
        {
            problems.Add($"{at}: type {type}{(kind is null ? "" : "/" + kind)} is not a built-in type or a declared one of that kind");
        }

        if (component && Str(p, "category") is { } category && !Categories.Contains(category, StringComparer.Ordinal))
        {
            problems.Add($"{at}: category '{category}' is not one of the fixed list");
        }

        if (Str(p, "editor") is { } editor && !Editors.Contains(editor, StringComparer.Ordinal))
        {
            problems.Add($"{at}: editor '{editor}' is not a property editor id");
        }

        if (!p.TryGetProperty("default", out var value))
        {
            return;
        }

        var ok = (kind, type, declaredKind) switch
        {
            (not null, _, _) or (_, "TStrings", _) => false,
            (_, "Boolean", _) => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            (_, "Integer" or "Int64" or "Double" or "TShortCut", _) => value.ValueKind == JsonValueKind.Number,
            (_, _, "enum") => value.ValueKind == JsonValueKind.String && Members(declared!.Value).Contains(value.GetString()),
            (_, _, "set") => value.ValueKind == JsonValueKind.Array
                && catalog.FindType(Str(declared!.Value, "of") ?? "") is { } of && value.EnumerateArray().All(m => Members(of).Contains(m.GetString())),
            _ => value.ValueKind == JsonValueKind.String,
        };
        if (!ok)
        {
            problems.Add($"{at}: default {value.GetRawText()} is not a value of {type}");
        }
    }

    private static List<string?> Members(JsonElement enumType) => [.. MetadataCatalog.Array(enumType, "values").Select(v => v.GetString())];

    private static void CheckType(MetadataCatalog catalog, string at, JsonElement t, List<string> problems)
    {
        switch (Str(t, "kind"))
        {
            case "enum":
                if (Members(t).Count == 0)
                {
                    problems.Add($"{at}: an enum needs values");
                }

                break;
            case "set":
                if (catalog.FindType(Str(t, "of") ?? "") is not { } of || Str(of, "kind") != "enum")
                {
                    problems.Add($"{at}: 'of' must name a declared enum");
                }

                break;
            case "object":
                foreach (var p in MetadataCatalog.Array(t, "properties"))
                {
                    CheckProperty(catalog, $"{at}.{Str(p, "name")}", p, false, problems);
                }

                break;
            default:
                problems.Add($"{at}: kind must be enum, set or object");
                break;
        }
    }

    /// <summary>
    /// metadata-authoring.md UIF-953: an icon is an SVG with a 24 × 24 view box that paints with <c>currentColor</c> only and
    /// holds no text, raster image, style or reference to other content.
    /// </summary>
    public static void CheckIcon(string at, string path, List<string> problems)
    {
        if (!File.Exists(path))
        {
            problems.Add($"{at}: icon file {Path.GetFileName(path)} does not exist");
            return;
        }

        CheckText($"{at}: icon", path, problems);
        XElement root;
        try
        {
            root = XDocument.Parse(File.ReadAllText(path)).Root!;
        }
        catch (System.Xml.XmlException e)
        {
            problems.Add($"{at}: icon is not well-formed XML: {e.Message}");
            return;
        }

        if (root.Name.LocalName != "svg" || (string?)root.Attribute("viewBox") != "0 0 24 24")
        {
            problems.Add($"{at}: icon root must be <svg viewBox=\"0 0 24 24\">");
        }

        string[] shapes = ["svg", "g", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon"];
        foreach (var e in root.DescendantsAndSelf())
        {
            if (!shapes.Contains(e.Name.LocalName, StringComparer.Ordinal))
            {
                problems.Add($"{at}: icon element <{e.Name.LocalName}> is not allowed");
            }

            foreach (var a in e.Attributes())
            {
                if (a.Name.LocalName is "fill" or "stroke" && a.Value is not ("currentColor" or "none"))
                {
                    problems.Add($"{at}: icon {a.Name.LocalName}=\"{a.Value}\": only currentColor or none");
                }

                if (a.Name.LocalName is "href" or "style" or "class")
                {
                    problems.Add($"{at}: icon attribute {a.Name.LocalName} is not allowed");
                }
            }
        }
    }

    // ---- metadata against the Pascal source ----

    /// <summary>
    /// DAT-284: every entry of the file of <paramref name="unit"/> against the class of that unit: ancestor, effective published
    /// properties (names, order, type, kind, item class, <c>default</c> directive, <c>stored False</c>) and events (signature
    /// type names); then the file's <c>types</c>, <c>eventSignatures</c> and <c>eventSignatureUnits</c> against the declarations.
    /// </summary>
    public static List<string> MatchesSource(MetadataCatalog catalog, PascalSource source, string unit, IEnumerable<string> requiredClasses)
    {
        var problems = new List<string>();
        problems.AddRange(source.Errors(unit).Take(5).Select(e => $"{unit}: source does not compile: {e}"));
        var file = catalog.DataFiles.FirstOrDefault(f => f.Name == unit + ".components.json");
        if (file is null)
        {
            problems.Add($"{unit}: {unit}.components.json is not listed in index.json");
            return problems;
        }

        var entries = catalog.DataComponents.Where(c => ReferenceEquals(c.File, file)).ToList();
        foreach (var cls in requiredClasses.Where(r => !entries.Any(e => e.Class == r)))
        {
            problems.Add($"{cls}: missing (no entry in {file.Name})");
        }

        foreach (var entry in entries)
        {
            if (source.FindClass(unit, entry.Class) is not { } cls || !PascalSource.IsComponent(cls))
            {
                problems.Add($"{entry.Class}: extra (no such component class in the interface of {unit})");
                continue;
            }

            CompareClass(catalog, entry, cls, problems);
        }

        foreach (var s in file.Section("eventSignatures"))
        {
            var texts = source.FindTypes(s.Name).OfType<MethodPointerTypeSymbol>().Select(PascalSource.Signature).Distinct(StringComparer.Ordinal).ToList();
            var meta = s.Value.ValueKind == JsonValueKind.String ? s.Value.GetString()! : s.Value.GetRawText();
            if (!texts.Contains(meta, StringComparer.Ordinal))
            {
                problems.Add($"{file.Name}: eventSignatures.{s.Name}: metadata \"{meta}\", source \"{string.Join("\" | \"", texts)}\"");
            }
        }

        var units = file.Section("eventSignatureUnits").ToDictionary(u => u.Name, u => u.Value.GetString() ?? "", StringComparer.OrdinalIgnoreCase);
        foreach (var type in file.Section("eventSignatures").Where(s => s.Value.ValueKind == JsonValueKind.String)
            .SelectMany(s => SignatureTypes(s.Value.GetString()!)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!units.ContainsKey(type))
            {
                problems.Add($"{file.Name}: eventSignatures: type {type} has no eventSignatureUnits entry");
            }
        }

        foreach (var (type, declaring) in units)
        {
            if (!source.DeclaresType(declaring, type))
            {
                problems.Add($"{file.Name}: eventSignatureUnits.{type}: unit '{declaring}' does not declare {type} in its interface");
            }
        }

        foreach (var t in file.Section("types"))
        {
            CompareType(catalog, $"{file.Name}: types.{t.Name}", t.Name, t.Value, source, problems);
        }

        return problems;
    }

    private static void CompareClass(MetadataCatalog catalog, MetaComponent entry, ClassTypeSymbol cls, List<string> problems)
    {
        // metadata names the nearest source ancestor that has an entry: a class in between that publishes nothing needs none
        var ancestor = cls.SelfAndAncestors().Skip(1).Select(a => a.Name)
            .FirstOrDefault(a => a == MetadataCatalog.BuiltInRoot || catalog.Find(a) is not null);
        if (entry.Ancestor != ancestor)
        {
            problems.Add($"{entry.Class}: ancestor: metadata {entry.Ancestor}, source {ancestor}");
        }

        var effective = PascalSource.Effective(cls)
            .Where(p => !NotInMetadata.Any(x => cls.SelfAndAncestors().Any(a => a.Name == x.Class) && x.Property == p.Name)).ToList();
        ComparePropertyLists(catalog, entry.Class, effective.Where(p => !p.IsEvent).ToList(), [.. catalog.Effective(entry, "properties")], problems);

        var sourceEvents = effective.Where(p => p.IsEvent).ToDictionary(p => p.Name, p => p.Type, StringComparer.Ordinal);
        var metaEvents = catalog.Effective(entry, "events").ToDictionary(e => Str(e, "name") ?? "", e => Str(e, "signature"), StringComparer.Ordinal);
        foreach (var (name, signature) in sourceEvents.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!metaEvents.TryGetValue(name, out var meta))
            {
                problems.Add($"{entry.Class}.{name}: missing event ({signature} in source)");
            }
            else if (meta != signature)
            {
                problems.Add($"{entry.Class}.{name}: signature mismatch: metadata {meta}, source {signature}");
            }
        }

        foreach (var name in metaEvents.Keys.Where(e => !sourceEvents.ContainsKey(e)).Order(StringComparer.Ordinal))
        {
            problems.Add($"{entry.Class}.{name}: extra event (not a published event of the source)");
        }
    }

    private static void ComparePropertyLists(MetadataCatalog catalog, string owner, List<PublishedProperty> source,
        List<Dictionary<string, JsonElement>> meta, List<string> problems)
    {
        for (var i = 0; i < Math.Max(source.Count, meta.Count); i++)
        {
            var s = i < source.Count ? source[i] : null;
            var m = i < meta.Count ? meta[i] : null;
            var name = m is null ? null : Str(m, "name");
            if (s is null)
            {
                problems.Add($"{owner}.{name}: extra (not a published property of the source)");
                continue;
            }

            if (m is null)
            {
                problems.Add($"{owner}.{s.Name}: missing (published {s.Type} in source)");
                continue;
            }

            if (name != s.Name)
            {
                problems.Add(meta.Any(x => Str(x, "name") == s.Name)
                    ? $"{owner}.{s.Name}: order mismatch (metadata position {i + 1} is {name})"
                    : $"{owner}.{s.Name}: missing (metadata position {i + 1} is {name})");
                continue;
            }

            var type = Str(m, "type");
            var kind = Str(m, "kind");
            if (type != s.Type || kind != s.Kind)
            {
                problems.Add($"{owner}.{s.Name}: type mismatch: metadata {type}{(kind is null ? "" : "/" + kind)}, source {s.Type}{(s.Kind is null ? "" : "/" + s.Kind)}");
            }

            if (Str(m, "itemClass") != s.ItemClass)
            {
                problems.Add($"{owner}.{s.Name}: itemClass mismatch: metadata {Str(m, "itemClass") ?? "(none)"}, source {s.ItemClass ?? "(none)"}");
            }

            var stored = !m.TryGetValue("stored", out var st) || st.ValueKind != JsonValueKind.False;
            var sourceStored = s.Symbol.StoredConstant || s.Symbol.StoredField is not null || s.Symbol.StoredMethod is not null;
            if (stored != sourceStored)
            {
                problems.Add($"{owner}.{s.Name}: stored: metadata {stored}, source {sourceStored}");
            }

            if (s.Symbol.DefaultValue is { } ordinal && DefaultOrdinal(catalog, type, m) is { } metaOrdinal && metaOrdinal != ordinal)
            {
                problems.Add($"{owner}.{s.Name}: default: metadata {(m.TryGetValue("default", out var d) ? d.GetRawText() : "(none)")}, source 'default' directive ordinal {ordinal}");
            }
        }
    }

    /// <summary>The ordinal of a metadata <c>default</c> of a Boolean, integer, enum or set property; null when it cannot be told (colours, missing).</summary>
    private static long? DefaultOrdinal(MetadataCatalog catalog, string? type, Dictionary<string, JsonElement> property)
    {
        if (!property.TryGetValue("default", out var value))
        {
            return type is "Boolean" or "Integer" or "Int64" || catalog.FindType(type ?? "") is not null ? long.MinValue : null;   // a directive without a metadata default
        }

        var declared = catalog.FindType(type ?? "");
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                return 1;
            case JsonValueKind.False:
                return 0;
            case JsonValueKind.Number when type is "Integer" or "Int64":
                return value.GetInt64();
            case JsonValueKind.String when declared is { } e && Str(e, "kind") == "enum":
                return Members(e).IndexOf(value.GetString());
            case JsonValueKind.Array when declared is { } s && Str(s, "kind") == "set" && catalog.FindType(Str(s, "of") ?? "") is { } of:
                return value.EnumerateArray().Aggregate(0L, (mask, member) => mask | (1L << Members(of).IndexOf(member.GetString())));
            default:
                return null;
        }
    }

    private static void CompareType(MetadataCatalog catalog, string at, string name, JsonElement t, PascalSource source, List<string> problems)
    {
        var found = source.FindTypes(name);
        switch (Str(t, "kind"))
        {
            case "enum":
                if (found.OfType<EnumTypeSymbol>().FirstOrDefault() is not { } e)
                {
                    problems.Add($"{at}: no enumeration {name} in the source");
                    return;
                }

                var expected = e.EnumMembers.Where(m => m.Ordinal >= e.Low && m.Ordinal <= e.High).Select(m => m.Name).ToList();
                if (!Members(t).SequenceEqual(expected, StringComparer.Ordinal))
                {
                    problems.Add($"{at}: values [{string.Join(", ", Members(t))}], source [{string.Join(", ", expected)}]");
                }

                break;
            case "set":
                if (found.OfType<SetTypeSymbol>().FirstOrDefault() is not { } s)
                {
                    problems.Add($"{at}: no set type {name} in the source");
                }
                else if (Str(t, "of") != s.ElementType?.Name)
                {
                    problems.Add($"{at}: of {Str(t, "of")}, source {s.ElementType?.Name}");
                }

                break;
            case "object":
                if (found.OfType<ClassTypeSymbol>().FirstOrDefault() is not { } c)
                {
                    problems.Add($"{at}: no class {name} in the source");
                    return;
                }

                ComparePropertyLists(catalog, at, [.. PascalSource.Effective(c).Where(p => !p.IsEvent)], catalog.ObjectProperties(t), problems);
                break;
        }
    }

    /// <summary>The distinct non-system type identifiers of the parameters of a <c>procedure(&lt;params&gt;)</c> text.</summary>
    public static List<string> SignatureTypes(string signature)
    {
        var open = signature.IndexOf('(');
        var close = signature.LastIndexOf(')');
        var types = new List<string>();
        if (open < 0 || close < open)
        {
            return types;
        }

        foreach (var group in signature[(open + 1)..close].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = group.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var type = group[(colon + 1)..].Trim();
            if (!SystemTypes.Contains(type) && !types.Contains(type, StringComparer.OrdinalIgnoreCase))
            {
                types.Add(type);
            }
        }

        return types;
    }

    /// <summary>A name declared under <c>types</c>, <c>eventSignatures</c> or <c>eventSignatureUnits</c> of several files (of both roots) has identical JSON in all of them.</summary>
    public static List<string> SharedDeclsAgree(MetadataCatalog catalog)
    {
        var problems = new List<string>();
        foreach (var section in new[] { "types", "eventSignatures", "eventSignatureUnits" })
        {
            var groups = catalog.Files.SelectMany(f => f.Section(section).Select(p => (File: f, p.Name, Text: Regex.Replace(p.Value.GetRawText(), @"\s+", " "))))
                .GroupBy(d => d.Name, StringComparer.Ordinal);
            foreach (var g in groups.Where(g => g.Any(d => d.File.IsData) && g.Select(d => d.Text).Distinct(StringComparer.Ordinal).Count() > 1))
            {
                problems.Add($"{section}.{g.Key}: differs between files: {string.Join(", ", g.Select(d => d.File.Name))}");
            }
        }

        return problems;
    }
}
