using System.Text;
using System.Text.Json;

namespace FreexComp.Designer.Tests;

/// <summary>
/// The test component metadata (testing.md §2) follows the shape of docs/interfaces/component-metadata.md, independently
/// of the designer's own loader (D-002).
/// </summary>
public sealed class TestDataTests
{
    private static readonly string[] BuiltInTypes =
        ["Integer", "Int64", "Double", "Boolean", "string", "Char", "TColor", "TCursor", "TStrings", "TShortCut"];

    private static readonly string[] Kinds = ["control", "wincontrol", "container", "nonvisual", "form"];

    private static readonly string[] Categories = ["Action", "Appearance", "Behavior", "Data", "Layout", "Input", "Misc"];

    private static readonly string[] RequiredClasses =
    [
        "TControl", "TWinControl", "TForm", "TLabel", "TEdit", "TMemo", "TButton", "TCheckBox", "TRadioButton",
        "TListBox", "TComboBox", "TGroupBox", "TPanel", "TImage", "TTimer", "TMainMenu", "TOpenDialog", "TSaveDialog",
    ];

    private static readonly string[] RequiredEvents = ["OnClick", "OnChange", "OnCreate", "OnTimer", "OnKeyPress"];

    private static readonly string[] RequiredUnits = ["Controls", "Forms", "StdCtrls", "ExtCtrls", "Dialogs", "Menus"];

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-002")]
    public void TestMetadataIsWellFormed()
    {
        var folder = TestPaths.Metadata;
        var problems = new List<string>();
        var index = ReadStrictJson(Path.Combine(folder, "index.json"), problems);
        Assert.Equal(1, index.GetProperty("formatVersion").GetInt32());
        Assert.All(index.GetProperty("palettePages").EnumerateArray(), p => Assert.Equal(JsonValueKind.String, p.ValueKind));
        var files = index.GetProperty("files").EnumerateArray().Select(f => f.GetString()!).ToList();
        Assert.Equal(
            Directory.EnumerateFiles(folder, "*.components.json").Select(Path.GetFileName).Order(StringComparer.Ordinal),
            files.Order(StringComparer.Ordinal));

        var classes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["TComponent"] = "Classes" };
        var types = new Dictionary<string, string>(StringComparer.Ordinal); // name → raw JSON
        var events = new HashSet<string>(StringComparer.Ordinal);
        var pendingReferenceTypes = new List<(string File, string Class, string Property, string Type)>();
        foreach (var file in files)
        {
            var root = ReadStrictJson(Path.Combine(folder, file), problems);
            void Problem(string text) => problems.Add($"{file}: {text}");

            if (root.GetProperty("formatVersion").GetInt32() != 1)
                Problem("formatVersion must be 1");
            var unit = root.GetProperty("unit").GetString()!;
            if (file != unit + ".components.json")
                Problem($"unit {unit} does not match the file name");

            var signatures = root.GetProperty("eventSignatures").EnumerateObject().ToDictionary(s => s.Name, s => s.Value.GetString()!);
            Assert.All(signatures.Values, s => Assert.StartsWith("procedure(", s, StringComparison.Ordinal));

            foreach (var type in root.GetProperty("types").EnumerateObject())
            {
                if (types.TryGetValue(type.Name, out var earlier) && earlier != type.Value.GetRawText())
                    Problem($"type {type.Name} differs from its earlier declaration");
                types[type.Name] = type.Value.GetRawText();
                switch (type.Value.GetProperty("kind").GetString())
                {
                    case "enum":
                        Assert.NotEmpty(type.Value.GetProperty("values").EnumerateArray());
                        break;
                    case "set":
                        var of = type.Value.GetProperty("of").GetString()!;
                        if (!types.TryGetValue(of, out var element) || !element.Contains("\"enum\"", StringComparison.Ordinal))
                            Problem($"set {type.Name} refers to undeclared enum {of}");
                        break;
                    case "object":
                        foreach (var property in type.Value.GetProperty("properties").EnumerateArray())
                            CheckProperty(property, type.Name, isComponent: false, Problem, types, classes, pendingReferenceTypes, file);
                        break;
                    default:
                        Problem($"type {type.Name} has an invalid kind");
                        break;
                }
            }

            foreach (var component in root.GetProperty("components").EnumerateArray())
            {
                var name = component.GetProperty("class").GetString()!;
                var ancestor = component.GetProperty("ancestor").GetString()!;
                if (!classes.ContainsKey(ancestor))
                    Problem($"{name}: ancestor {ancestor} is not declared before it");
                if (!classes.TryAdd(name, unit))
                    Problem($"{name}: duplicate class");

                var isAbstract = component.TryGetProperty("abstract", out var a) && a.GetBoolean();
                if (component.TryGetProperty("kind", out var kind) && !Kinds.Contains(kind.GetString()))
                    Problem($"{name}: invalid kind");
                var effectiveKind = EffectiveKind(files, folder, name);
                if (!isAbstract && effectiveKind is "control" or "wincontrol" or "container" or "form"
                    && !HasInChain(folder, files, name, "defaultSize"))
                    Problem($"{name}: defaultSize missing");
                if (component.TryGetProperty("palette", out var palette))
                {
                    Assert.False(isAbstract, $"{name}: abstract class on the palette");
                    palette.GetProperty("page").GetString();
                    palette.GetProperty("order").GetInt32();
                }

                foreach (var property in component.GetProperty("properties").EnumerateArray())
                    CheckProperty(property, name, isComponent: true, Problem, types, classes, pendingReferenceTypes, file);

                var defaults = 0;
                foreach (var ev in component.GetProperty("events").EnumerateArray())
                {
                    var evName = ev.GetProperty("name").GetString()!;
                    events.Add(evName);
                    if (!signatures.ContainsKey(ev.GetProperty("signature").GetString()!))
                        Problem($"{name}.{evName}: signature not declared in this file");
                    if (ev.TryGetProperty("default", out var d) && d.GetBoolean())
                        defaults++;
                }

                if (defaults > 1)
                    Problem($"{name}: more than one default event");
            }
        }

        foreach (var (file, owner, property, type) in pendingReferenceTypes)
        {
            if (!classes.ContainsKey(type))
                problems.Add($"{file}: {owner}.{property}: referenced class {type} is not declared");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.All(RequiredClasses, c => Assert.True(classes.ContainsKey(c), c));
        Assert.All(RequiredEvents, e => Assert.Contains(e, events));
        Assert.All(RequiredUnits, u => Assert.Contains(u, classes.Values));
        Assert.Equal("Classes", classes["TComponent"]);
    }

    private static void CheckProperty(JsonElement property, string owner, bool isComponent, Action<string> problem, Dictionary<string, string> types,
        Dictionary<string, string> classes, List<(string, string, string, string)> referenceTypes, string file)
    {
        var name = property.GetProperty("name").GetString()!;
        var type = property.GetProperty("type").GetString()!;
        var kind = property.TryGetProperty("kind", out var k) ? k.GetString() : null;
        switch (kind)
        {
            case null when !BuiltInTypes.Contains(type) && !types.ContainsKey(type):
                problem($"{owner}.{name}: unknown type {type}");
                break;
            case "object" when !types.ContainsKey(type):
                problem($"{owner}.{name}: object type {type} not declared");
                break;
            case "component":
                referenceTypes.Add((file, owner, name, type)); // may be declared by a later file
                break;
            case "collection" when !(property.TryGetProperty("itemClass", out var item) && types.TryGetValue(item.GetString()!, out var itemType)
                && itemType.Contains("\"object\"", StringComparison.Ordinal)):
                problem($"{owner}.{name}: collection item class not declared as an object type");
                break;
            case null or "object" or "component" or "collection":
                break;
            default:
                problem($"{owner}.{name}: invalid kind {kind}");
                break;
        }

        if (property.TryGetProperty("category", out var category) && !Categories.Contains(category.GetString()))
            problem($"{owner}.{name}: invalid category");
        if (kind is "object" or "component" or "collection" && property.TryGetProperty("default", out _))
            problem($"{owner}.{name}: {kind} properties have no default");
        if (isComponent && name is "Left" or "Top" or "Width" or "Height" or "ClientWidth" or "ClientHeight" or "PixelsPerInch"
            && property.TryGetProperty("default", out _))
            problem($"{owner}.{name}: always-written geometry has no default");
    }

    private static JsonElement ReadStrictJson(string path, List<string> problems)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
            problems.Add($"{Path.GetFileName(path)}: BOM");
        if (bytes.Contains((byte)'\r'))
            problems.Add($"{Path.GetFileName(path)}: CR line ends");
        var text = new UTF8Encoding(false, true).GetString(bytes);
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        return document.RootElement.Clone();
    }

    /// <summary>Walks the ancestor chain through all files and returns the first <c>kind</c> found.</summary>
    private static string? EffectiveKind(List<string> files, string folder, string className)
    {
        for (var current = Find(folder, files, className); current is { } c; current = Find(folder, files, c.GetProperty("ancestor").GetString()!))
        {
            if (c.TryGetProperty("kind", out var kind))
                return kind.GetString();
        }

        return null;
    }

    private static bool HasInChain(string folder, List<string> files, string className, string key)
    {
        for (var current = Find(folder, files, className); current is { } c; current = Find(folder, files, c.GetProperty("ancestor").GetString()!))
        {
            if (c.TryGetProperty(key, out _))
                return true;
        }

        return false;
    }

    private static JsonElement? Find(string folder, List<string> files, string className)
    {
        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, file)));
            foreach (var component in document.RootElement.GetProperty("components").EnumerateArray())
            {
                if (string.Equals(component.GetProperty("class").GetString(), className, StringComparison.OrdinalIgnoreCase))
                    return component.Clone();
            }
        }

        return null;
    }
}
