using System.Text.Json;

namespace FreexComp.MetadataTesting;

/// <summary>A designer verb as the spec table states it (controls-standard.md §29.1).</summary>
public sealed record SpecVerb(string Id, string Caption, string Command, string? Arg);

/// <summary>What <c>MetadataSchemaValid</c> compares with besides the schema itself: the facts of controls-standard.md §29 / §29.1.</summary>
public sealed record SchemaExpectations(
    IReadOnlyDictionary<string, string?> DefaultEvents,
    IReadOnlyDictionary<string, IReadOnlyList<SpecVerb>> Verbs,
    IReadOnlyDictionary<string, string> ComponentEditors)
{
    public static SchemaExpectations None { get; } = new(
        new Dictionary<string, string?>(), new Dictionary<string, IReadOnlyList<SpecVerb>>(), new Dictionary<string, string>());
}

/// <summary>
/// <c>MetadataSchemaValid</c> (metadata-authoring.md §4, UIF-951/952; component-metadata.md §1–§5, §7): every rule yields
/// <c>location: problem</c> lines; an empty result means valid. The icon-file rule is added with the icons (U-104).
/// </summary>
public static class MetadataSchema
{
    public static readonly string[] IndexKeys = ["formatVersion", "palettePages", "files"];
    public static readonly string[] FileKeys = ["formatVersion", "unit", "types", "components", "eventSignatures"];
    public static readonly string[] ComponentKeys =
    [
        "class", "ancestor", "abstract", "palette", "kind", "defaultSize", "namePrefix", "acceptsChildren", "childClasses",
        "parentClasses", "renderer", "designerVerbs", "componentEditor", "properties", "events",
    ];
    public static readonly string[] PropertyKeys =
    [
        "name", "type", "kind", "itemClass", "default", "category", "initial", "readOnly", "hidden", "stored", "editor",
        "editorOptions", "min", "max", "affectsLayout", "localizable", "description",
    ];
    public static readonly string[] EventKeys = ["name", "signature", "default", "description"];
    public static readonly string[] PaletteKeys = ["page", "order", "icon"];
    public static readonly string[] SizeKeys = ["width", "height"];
    public static readonly string[] VerbKeys = ["id", "caption", "command", "arg"];
    public static readonly string[] EnumKeys = ["kind", "values"];
    public static readonly string[] SetKeys = ["kind", "of"];
    public static readonly string[] ObjectKeys = ["kind", "ancestor", "properties"];

    public static readonly string[] Kinds = ["control", "wincontrol", "container", "nonvisual", "form"];
    public static readonly string[] PropertyKinds = ["object", "component", "collection"];
    public static readonly string[] Categories = ["Action", "Appearance", "Behavior", "Data", "Layout", "Input", "Misc"];
    public static readonly string[] Editors = ["color", "font", "strings", "picture", "filename", "shortcut", "collection", "fieldname", "tablename"];
    public static readonly string[] BuiltInTypes = ["Integer", "Int64", "Double", "Boolean", "string", "Char", "TColor", "TCursor", "TStrings", "TShortCut"];
    public static readonly string[] Commands =
        ["designer.newChild", "designer.deletePage", "designer.nextPage", "designer.previousPage", "designer.editCollection", "designer.editComponent"];
    public static readonly string[] ComponentEditors = ["menu", "childlist"];

    public static List<string> Validate(MetadataRoot root, SchemaExpectations expect)
    {
        var problems = new List<string>(root.LoadProblems);
        if (root.Index is not { } index)
        {
            return problems;
        }

        CheckText("index.json", root.IndexText!, problems);
        CheckKeys("index.json", index, IndexKeys, problems);
        if (!index.TryGetProperty("formatVersion", out var iv) || iv.ValueKind != JsonValueKind.Number || iv.GetInt32() != 1)
        {
            problems.Add("index.json: formatVersion must be 1");
        }

        var listed = index.TryGetProperty("files", out var fl) && fl.ValueKind == JsonValueKind.Array
            ? fl.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : "").ToList()
            : null;
        if (listed is null)
        {
            problems.Add("index.json: files must be an array");
            listed = [];
        }

        foreach (var dup in listed.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            problems.Add($"index.json: {dup.Key} listed more than once");
        }

        foreach (var onDisk in Directory.EnumerateFiles(root.Directory, "*.components.json").Select(Path.GetFileName))
        {
            if (!listed.Contains(onDisk!, StringComparer.Ordinal))
            {
                problems.Add($"index.json: {onDisk} exists but is not listed");
            }
        }

        var pages = root.PalettePages;
        var prefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var orders = new Dictionary<(string, int), string>();
        foreach (var file in root.Files)
        {
            ValidateFile(root, file, pages, prefixes, orders, expect, problems);
        }

        return problems;
    }

    private static void ValidateFile(MetadataRoot root, MetadataFile file, IReadOnlyList<string> pages, Dictionary<string, string> prefixes,
        Dictionary<(string, int), string> orders, SchemaExpectations expect, List<string> problems)
    {
        var at = file.Name;
        CheckText(at, file.Text, problems);
        CheckKeys(at, file.Json, FileKeys, problems);
        if (!file.Json.TryGetProperty("formatVersion", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != 1)
        {
            problems.Add($"{at}: formatVersion must be 1");
        }

        if (file.Name != file.Unit + ".components.json")
        {
            problems.Add($"{at}: unit '{file.Unit}' does not match the file name");
        }

        foreach (var key in new[] { "types", "eventSignatures" })
        {
            if (!file.Json.TryGetProperty(key, out var o) || o.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{at}: {key} must be an object");
            }
        }

        if (!file.Json.TryGetProperty("components", out var cs) || cs.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{at}: components must be an array");
        }

        foreach (var t in file.Types)
        {
            ValidateType(root, file, t.Name, t.Value, problems);
        }

        foreach (var s in file.EventSignatures)
        {
            if (s.Value.ValueKind != JsonValueKind.String || !s.Value.GetString()!.StartsWith("procedure(", StringComparison.Ordinal) || !s.Value.GetString()!.EndsWith(')'))
            {
                problems.Add($"{at}: eventSignatures.{s.Name}: text must be 'procedure(<params>)'");
            }
        }

        foreach (var c in root.Components.Where(c => ReferenceEquals(c.File, file)))
        {
            ValidateComponent(root, c, pages, prefixes, orders, expect, problems);
        }
    }

    private static void ValidateType(MetadataRoot root, MetadataFile file, string name, JsonElement t, List<string> problems)
    {
        var at = $"{file.Name}: types.{name}";
        var kind = Str(t, "kind");
        switch (kind)
        {
            case "enum":
                CheckKeys(at, t, EnumKeys, problems);
                if (!t.TryGetProperty("values", out var vals) || vals.ValueKind != JsonValueKind.Array || vals.GetArrayLength() == 0
                    || vals.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
                {
                    problems.Add($"{at}: values must be a non-empty array of names");
                }

                break;
            case "set":
                CheckKeys(at, t, SetKeys, problems);
                if (Str(t, "of") is not { } of || root.FindType(file, of) is not { } el || Str(el, "kind") != "enum")
                {
                    problems.Add($"{at}: 'of' must name an enum declared in this or an earlier file");
                }

                break;
            case "object":
                CheckKeys(at, t, ObjectKeys, problems);
                if (Str(t, "ancestor") is { } anc && root.FindType(file, anc) is null)
                {
                    problems.Add($"{at}: ancestor {anc} is not declared");
                }

                foreach (var p in MetadataComponent.Array(t, "properties"))
                {
                    ValidateProperty(root, file, at, p, sub: true, problems);
                }

                break;
            default:
                problems.Add($"{at}: kind must be enum, set or object");
                break;
        }
    }

    private static void ValidateComponent(MetadataRoot root, MetadataComponent c, IReadOnlyList<string> pages, Dictionary<string, string> prefixes,
        Dictionary<(string, int), string> orders, SchemaExpectations expect, List<string> problems)
    {
        var at = $"{c.File.Name}: {c.Class}";
        var j = c.Json;
        CheckKeys(at, j, ComponentKeys, problems);
        if (c.Class.Length < 2 || c.Class[0] != 'T')
        {
            problems.Add($"{at}: class must be a Pascal class name");
        }

        if (c.Ancestor is null)
        {
            problems.Add($"{at}: ancestor is required");
        }
        else if (c.Ancestor != MetadataRoot.BuiltInRoot && root.Find(c.Ancestor) is not { } anc)
        {
            problems.Add($"{at}: ancestor {c.Ancestor} is not declared");
        }
        else if (c.Ancestor != MetadataRoot.BuiltInRoot && root.Files.IndexOf(root.Find(c.Ancestor)!.File) > root.Files.IndexOf(c.File))
        {
            problems.Add($"{at}: ancestor {c.Ancestor} is declared in a later file (index.json order)");
        }

        var kind = Effective(root, c, "kind");
        if (kind is not { } k || k.ValueKind != JsonValueKind.String || !Kinds.Contains(k.GetString()))
        {
            problems.Add($"{at}: kind must be one of {string.Join(", ", Kinds)}");
        }

        var kindName = kind?.ValueKind == JsonValueKind.String ? kind.Value.GetString() : null;
        if (!c.Abstract && kindName is "control" or "wincontrol" or "container" or "form" && Effective(root, c, "defaultSize") is null)
        {
            problems.Add($"{at}: defaultSize is required for a non-abstract {kindName}");
        }

        if (j.TryGetProperty("defaultSize", out var ds))
        {
            CheckKeys(at + ".defaultSize", ds, SizeKeys, problems);
            if (kindName == "nonvisual")
            {
                problems.Add($"{at}: defaultSize must be omitted for nonvisual");
            }
        }

        if (j.TryGetProperty("abstract", out var ab) && ab.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            problems.Add($"{at}: abstract must be a Boolean");
        }

        if (j.TryGetProperty("palette", out var pal))
        {
            CheckKeys(at + ".palette", pal, PaletteKeys, problems);
            if (c.Abstract)
            {
                problems.Add($"{at}: an abstract class is never on the palette");
            }

            var page = Str(pal, "page");
            if (page is null || !pages.Contains(page))
            {
                problems.Add($"{at}: palette.page '{page}' is not in the palettePages of index.json");
            }

            if (!pal.TryGetProperty("order", out var o) || o.ValueKind != JsonValueKind.Number)
            {
                problems.Add($"{at}: palette.order must be a number");
            }
            else if (page is not null && !orders.TryAdd((page, o.GetInt32()), c.Class))
            {
                problems.Add($"{at}: palette.order {o.GetInt32()} on page {page} is also used by {orders[(page, o.GetInt32())]}");
            }

            if (Str(pal, "icon") != $"icons/{c.Class}.svg")
            {
                problems.Add($"{at}: palette.icon must be icons/{c.Class}.svg");
            }
        }

        if (!c.Abstract)
        {
            var prefix = Str(j, "namePrefix") ?? c.Class[1..];
            if (!prefixes.TryAdd(prefix, c.Class))
            {
                problems.Add($"{at}: namePrefix {prefix} is also used by {prefixes[prefix]}");
            }
        }

        foreach (var key in new[] { "acceptsChildren" })
        {
            if (j.TryGetProperty(key, out var b) && b.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                problems.Add($"{at}: {key} must be a Boolean");
            }
        }

        foreach (var key in new[] { "childClasses", "parentClasses" })
        {
            if (j.TryGetProperty(key, out var arr))
            {
                if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
                {
                    problems.Add($"{at}: {key} must be a non-empty array");
                    continue;
                }

                foreach (var x in arr.EnumerateArray())
                {
                    if (x.ValueKind != JsonValueKind.String || root.Find(x.GetString()!) is null)
                    {
                        problems.Add($"{at}: {key}: {x} is not a declared class");
                    }
                }
            }
        }

        if (j.TryGetProperty("renderer", out var r) && (r.ValueKind != JsonValueKind.String || r.GetString()!.Length == 0))
        {
            problems.Add($"{at}: renderer must be a non-empty string");
        }

        if (kindName == "nonvisual" && Effective(root, c, "renderer") is { } rr && rr.GetString() != "nonvisual")
        {
            problems.Add($"{at}: a nonvisual component uses renderer \"nonvisual\"");
        }

        ValidateVerbs(root, c, expect, problems);
        if (j.TryGetProperty("componentEditor", out var ce) && (ce.ValueKind != JsonValueKind.String || !ComponentEditors.Contains(ce.GetString())))
        {
            problems.Add($"{at}: componentEditor must be menu or childlist");
        }

        var expectedEditor = expect.ComponentEditors.TryGetValue(c.Class, out var ee) ? ee : null;
        if (expectedEditor != (ce.ValueKind == JsonValueKind.String ? ce.GetString() : null))
        {
            problems.Add($"{at}: componentEditor expected '{expectedEditor}' (controls-standard.md §29.1)");
        }

        foreach (var key in new[] { "properties", "events" })
        {
            if (!j.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                problems.Add($"{at}: {key} must be an array");
            }
        }

        foreach (var p in c.Properties)
        {
            ValidateProperty(root, c.File, at, p, sub: false, problems);
        }

        foreach (var e in c.Events)
        {
            var name = Str(e, "name") ?? "?";
            CheckKeys($"{at}.{name}", e, EventKeys, problems);
            if (Str(e, "signature") is not { } sig || !c.File.EventSignatures.Any(s => s.Name == sig))
            {
                problems.Add($"{at}.{name}: signature must name an entry of this file's eventSignatures");
            }

            if (e.TryGetProperty("default", out var d) && d.ValueKind != JsonValueKind.True)
            {
                problems.Add($"{at}.{name}: default is written only as true");
            }
        }

        var defaults = c.Events.Where(e => e.TryGetProperty("default", out _)).Select(e => Str(e, "name")).ToList();
        if (defaults.Count > 1)
        {
            problems.Add($"{at}: more than one default event ({string.Join(", ", defaults)})");
        }

        if (expect.DefaultEvents.TryGetValue(c.Class, out var expected))
        {
            // a descendant's default overrides the inherited choice (component-metadata.md §5): the last one in the chain counts
            var chosen = root.Chain(c).Select(k => k.Events.FirstOrDefault(e => e.TryGetProperty("default", out _)))
                .FirstOrDefault(e => e.ValueKind == JsonValueKind.Object);
            var actual = chosen.ValueKind == JsonValueKind.Object ? Str(chosen, "name") : null;
            if (actual != expected)
            {
                problems.Add($"{at}: default event expected '{expected ?? "—"}', actual '{actual ?? "—"}' (controls-standard.md §29)");
            }
        }
    }

    private static void ValidateVerbs(MetadataRoot root, MetadataComponent c, SchemaExpectations expect, List<string> problems)
    {
        var at = $"{c.File.Name}: {c.Class}";
        var own = MetadataComponent.Array(c.Json, "designerVerbs").ToList();
        foreach (var v in own)
        {
            var id = Str(v, "id") ?? "?";
            CheckKeys($"{at}.designerVerbs.{id}", v, VerbKeys, problems);
            if (Str(v, "id") is null || Str(v, "caption") is null || Str(v, "command") is not { } cmd)
            {
                problems.Add($"{at}.designerVerbs.{id}: id, caption and command are required");
                continue;
            }

            if (!Commands.Contains(cmd))
            {
                problems.Add($"{at}.designerVerbs.{id}: unknown command {cmd}");
            }

            var arg = Str(v, "arg");
            if (cmd == "designer.newChild" && (arg is null || root.Find(arg.Split(';')[0]) is null))
            {
                problems.Add($"{at}.designerVerbs.{id}: arg must name a declared class");
            }

            if (cmd == "designer.editCollection" && (arg is null || !root.Effective(c, "properties")
                    .Any(p => p["name"].GetString() == arg && p.TryGetValue("kind", out var pk) && pk.GetString() == "collection")))
            {
                problems.Add($"{at}.designerVerbs.{id}: arg must name a collection property of the class");
            }
        }

        var actual = own.Select(v => new SpecVerb(Str(v, "id") ?? "", Str(v, "caption") ?? "", Str(v, "command") ?? "", Str(v, "arg"))).ToList();
        var expected = expect.Verbs.TryGetValue(c.Class, out var e) ? e : [];
        if (!actual.SequenceEqual(expected))
        {
            problems.Add($"{at}: designerVerbs differ from controls-standard.md §29.1: expected [{string.Join("; ", expected)}], actual [{string.Join("; ", actual)}]");
        }
    }

    private static void ValidateProperty(MetadataRoot root, MetadataFile file, string owner, JsonElement p, bool sub, List<string> problems)
    {
        var name = Str(p, "name");
        var at = $"{owner}.{name ?? "?"}";
        CheckKeys(at, p, PropertyKeys, problems);
        if (name is null || Str(p, "type") is not { } type)
        {
            problems.Add($"{at}: name and type are required");
            return;
        }

        var kind = Str(p, "kind");
        if (kind is not null && !PropertyKinds.Contains(kind))
        {
            problems.Add($"{at}: kind must be object, component or collection");
        }

        JsonElement? declared = null;
        switch (kind)
        {
            case "component":
                if (root.Find(type) is null)
                {
                    problems.Add($"{at}: component type {type} is not a declared class");
                }

                break;
            case "object":
            case "collection":
                if (root.FindType(file, type) is not { } ot || Str(ot, "kind") != "object")
                {
                    if (kind == "object")
                    {
                        problems.Add($"{at}: object type {type} is not declared under types (this or an earlier file)");
                    }
                }

                if (kind == "collection" && (Str(p, "itemClass") is not { } item || root.FindType(file, item) is not { } it || Str(it, "kind") != "object"))
                {
                    problems.Add($"{at}: a collection needs an itemClass declared under types as an object");
                }

                break;
            default:
                if (!BuiltInTypes.Contains(type))
                {
                    declared = root.FindType(file, type);
                    if (declared is not { } d || Str(d, "kind") is not ("enum" or "set"))
                    {
                        problems.Add($"{at}: type {type} is neither built in nor an enum/set declared in this or an earlier file");
                    }
                }

                break;
        }

        if (p.TryGetProperty("category", out var cat))
        {
            if (cat.ValueKind != JsonValueKind.String || !Categories.Contains(cat.GetString()))
            {
                problems.Add($"{at}: category must be one of {string.Join(", ", Categories)}");
            }
        }

        if (p.TryGetProperty("editor", out var ed) && (ed.ValueKind != JsonValueKind.String || !Editors.Contains(ed.GetString())))
        {
            problems.Add($"{at}: unknown editor {ed}");
        }

        foreach (var flag in new[] { "readOnly", "hidden", "stored", "affectsLayout", "localizable" })
        {
            if (p.TryGetProperty(flag, out var f) && f.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                problems.Add($"{at}: {flag} must be a Boolean");
            }
        }

        foreach (var lim in new[] { "min", "max" })
        {
            if (p.TryGetProperty(lim, out var l) && l.ValueKind != JsonValueKind.Number)
            {
                problems.Add($"{at}: {lim} must be a number");
            }
        }

        if (p.TryGetProperty("default", out var def))
        {
            if (kind is not null || type == "TStrings")
            {
                problems.Add($"{at}: a {kind ?? type} property has no default");
            }
            else if (DefaultProblem(root, file, type, declared, def) is { } why)
            {
                problems.Add($"{at}: default {def.GetRawText()}: {why}");
            }
        }
    }

    private static string? DefaultProblem(MetadataRoot root, MetadataFile file, string type, JsonElement? declared, JsonElement def)
    {
        switch (type)
        {
            case "Integer":
            case "Int64":
            case "Double":
            case "TShortCut":
                return def.ValueKind == JsonValueKind.Number ? null : "must be a number";
            case "Boolean":
                return def.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "must be true or false";
            case "string":
                return def.ValueKind == JsonValueKind.String ? null : "must be a string";
            case "Char":
                return def.ValueKind == JsonValueKind.String && def.GetString()!.Length <= 1 ? null : "must be a one-character string or \"\"";
            case "TColor":
                return def.ValueKind == JsonValueKind.String && (def.GetString()!.StartsWith("cl", StringComparison.Ordinal)
                    || System.Text.RegularExpressions.Regex.IsMatch(def.GetString()!, "^\\$[0-9A-F]{8}$")) ? null : "must be a colour name or \"$00BBGGRR\"";
            case "TCursor":
                return def.ValueKind == JsonValueKind.String && def.GetString()!.StartsWith("cr", StringComparison.Ordinal) ? null : "must be a cursor name";
        }

        if (declared is not { } d)
        {
            return null;
        }

        if (Str(d, "kind") == "enum")
        {
            return def.ValueKind == JsonValueKind.String && Values(d).Contains(def.GetString()) ? null : "must be a member of the enum";
        }

        var element = Str(d, "of") is { } of ? root.FindType(file, of) : null;
        return def.ValueKind == JsonValueKind.Array && element is { } el && def.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String && Values(el).Contains(x.GetString()))
            ? null : "must be an array of members of the set's enum";
    }

    private static List<string?> Values(JsonElement e) =>
        e.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Array ? [.. v.EnumerateArray().Select(x => x.GetString())] : [];

    /// <summary>The value of <paramref name="key"/> on <paramref name="c"/> or its nearest ancestor that has it (inherited keys).</summary>
    private static JsonElement? Effective(MetadataRoot root, MetadataComponent c, string key) =>
        root.Chain(c).Select(k => k.Json.TryGetProperty(key, out var v) ? v : (JsonElement?)null).FirstOrDefault(v => v is not null);

    /// <summary>UIF-952: no BOM, LF only, final LF.</summary>
    public static void CheckText(string at, string text, List<string> problems)
    {
        if (text.Length > 0 && text[0] == '﻿')
        {
            problems.Add($"{at}: starts with a BOM");
        }

        if (text.Contains('\r', StringComparison.Ordinal))
        {
            problems.Add($"{at}: contains CR (LF line ends only)");
        }

        if (!text.EndsWith('\n'))
        {
            problems.Add($"{at}: must end with LF");
        }
    }

    /// <summary>UIF-952: only known keys, in the schema order.</summary>
    public static void CheckKeys(string at, JsonElement e, IReadOnlyList<string> order, List<string> problems)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{at}: must be an object");
            return;
        }

        var last = -1;
        foreach (var p in e.EnumerateObject())
        {
            var i = order.IndexOf(p.Name);
            if (i < 0)
            {
                problems.Add($"{at}: unknown key '{p.Name}'");
            }
            else if (i <= last)
            {
                problems.Add($"{at}: key '{p.Name}' out of order (expected order: {string.Join(", ", order)})");
            }
            else
            {
                last = i;
            }
        }
    }

    private static string? Str(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int IndexOf<T>(this IReadOnlyList<T> list, T item)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(list[i], item))
            {
                return i;
            }
        }

        return -1;
    }
}
