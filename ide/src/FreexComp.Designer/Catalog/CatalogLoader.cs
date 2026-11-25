using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace FreexComp.Designer.Catalog;

/// <summary>One validation problem of a metadata file (DSG-TP-005); shown as <c>Msg.Designer.MetadataError</c>.</summary>
public sealed record CatalogProblem(string File, int Line, int Column, string Message)
{
    public override string ToString() => $"{File}({Line},{Column}): {Message}";
}

/// <summary>The catalog built from every valid item, and the problems of the skipped ones.</summary>
public sealed record CatalogLoadResult(ComponentCatalog Catalog, ImmutableArray<CatalogProblem> Problems);

/// <summary>
/// Loads component metadata (tool-palette.md §2, component-metadata.md): index files in root order, component files in
/// listed order, validation per item (DSG-TP-005), flattening (DSG-TP-006) and root defaults (DSG-TP-007).
/// </summary>
public static class CatalogLoader
{
    private const int FormatVersion = 1;

    private static readonly string[] BuiltInTypes =
        ["Integer", "Int64", "Double", "Boolean", "string", "Char", "TColor", "TCursor", "TStrings", "TShortCut"];

    private static readonly string[] Categories = ["Action", "Appearance", "Behavior", "Data", "Layout", "Input", "Misc"];

    /// <summary>Loads the roots whose <c>index.json</c> paths are given, in that order.</summary>
    /// <param name="readFile">Reads a file's bytes, or returns null when it does not exist (default: the disk).</param>
    public static CatalogLoadResult Load(IEnumerable<string> indexPaths, Func<string, byte[]?>? readFile = null)
    {
        readFile ??= ReadFromDisk;
        var state = new LoadState(readFile);
        foreach (var indexPath in indexPaths)
        {
            state.LoadRoot(Path.GetFullPath(indexPath));
        }

        return state.Build();
    }

    private static byte[]? ReadFromDisk(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    /// <summary>A component as declared in its file (own entries only), already checked for class and ancestor.</summary>
    private sealed record RawComponent(string ClassName, string Ancestor, string Unit, string File, string Path,
        JsonElement Element, List<RawMember> Properties, List<RawMember> Events);

    /// <summary>A property or event declaration with the keys present in it.</summary>
    private sealed record RawMember(string Name, Dictionary<string, JsonElement> Keys, string File, string Path, string DeclaringClass);

    private sealed record RawType(string Name, JsonElement Element, string File, string Path);

    private sealed class LoadState(Func<string, byte[]?> readFile)
    {
        private readonly List<CatalogProblem> _problems = [];
        private readonly List<RawComponent> _components = [];
        private readonly Dictionary<string, RawComponent> _byName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RawType> _rawTypes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _signatures = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _signatureUnits = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _palettePages = [];
        private readonly Dictionary<string, JsonPositions> _positions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TypeInfo> _types = new(StringComparer.OrdinalIgnoreCase);

        public void LoadRoot(string indexPath)
        {
            if (ReadJson(indexPath) is not { } index)
            {
                return;
            }

            if (!CheckFormatVersion(index, indexPath))
            {
                return;
            }

            if (index.TryGetProperty("palettePages", out var pages) && pages.ValueKind == JsonValueKind.Array)
            {
                foreach (var page in pages.EnumerateArray())
                {
                    if (page.ValueKind == JsonValueKind.String && page.GetString() is { Length: > 0 } name
                        && !_palettePages.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        _palettePages.Add(name);
                    }
                }
            }

            if (!index.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            {
                Problem(indexPath, "", "index has no \"files\" array");
                return;
            }

            var folder = Path.GetDirectoryName(indexPath)!;
            foreach (var file in files.EnumerateArray())
            {
                if (file.ValueKind == JsonValueKind.String && file.GetString() is { Length: > 0 } relative)
                {
                    LoadFile(Path.GetFullPath(Path.Combine(folder, relative)));
                }
            }
        }

        private void LoadFile(string path)
        {
            if (ReadJson(path) is not { } root || !CheckFormatVersion(root, path))
            {
                return;
            }

            var unit = root.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetString()!
                : System.IO.Path.GetFileName(path).Split('.')[0];

            if (root.TryGetProperty("eventSignatures", out var signatures) && signatures.ValueKind == JsonValueKind.Object)
            {
                foreach (var signature in signatures.EnumerateObject())
                {
                    var text = signature.Value.ValueKind == JsonValueKind.String ? signature.Value.GetString()! : "";
                    if (_signatures.TryGetValue(signature.Name, out var earlier))
                    {
                        if (earlier != text)
                        {
                            Problem(path, "eventSignatures", $"event signature {signature.Name} differs from its earlier declaration (the first is used)");
                        }
                    }
                    else
                    {
                        _signatures[signature.Name] = text;
                    }
                }
            }

            // DSG-CS-055, A-CS-2: optional "eventSignatureUnits": { "<signature type>": "<unit>" }; the first declaration wins.
            if (root.TryGetProperty("eventSignatureUnits", out var signatureUnits) && signatureUnits.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in signatureUnits.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.String || entry.Value.GetString() is not { Length: > 0 } signatureUnit)
                    {
                        Problem(path, "eventSignatureUnits." + entry.Name, "the unit must be a non-empty string");
                    }
                    else if (_signatureUnits.TryGetValue(entry.Name, out var earlier))
                    {
                        if (!string.Equals(earlier, signatureUnit, StringComparison.OrdinalIgnoreCase))
                        {
                            Problem(path, "eventSignatureUnits", $"the unit of {entry.Name} differs from its earlier declaration (the first is used)");
                        }
                    }
                    else
                    {
                        _signatureUnits[entry.Name] = signatureUnit;
                    }
                }
            }

            if (root.TryGetProperty("types", out var types) && types.ValueKind == JsonValueKind.Object)
            {
                foreach (var type in types.EnumerateObject())
                {
                    var typePath = "types." + type.Name;
                    if (_rawTypes.TryGetValue(type.Name, out var earlier))
                    {
                        if (earlier.Element.GetRawText() != type.Value.GetRawText())
                        {
                            Problem(path, typePath, $"type {type.Name} differs from its earlier declaration (the first is used)");
                        }

                        continue;
                    }

                    _rawTypes[type.Name] = new RawType(type.Name, type.Value.Clone(), path, typePath);
                }
            }

            if (root.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var component in components.EnumerateArray())
                {
                    ReadComponent(component.Clone(), unit, path, $"components[{i++}]");
                }
            }
        }

        private void ReadComponent(JsonElement element, string unit, string file, string path)
        {
            if (element.ValueKind != JsonValueKind.Object || String(element, "class") is not { Length: > 0 } className)
            {
                Problem(file, path, "component without \"class\" (skipped)");
                return;
            }

            if (string.Equals(className, ComponentCatalog.RootClassName, StringComparison.OrdinalIgnoreCase))
            {
                Problem(file, path, $"{ComponentCatalog.RootClassName} is built in and must not be declared (skipped)");
                return;
            }

            if (_byName.ContainsKey(className))
            {
                Problem(file, path, $"duplicate class {className} (the first declaration is used)");
                return;
            }

            var ancestor = String(element, "ancestor") ?? ComponentCatalog.RootClassName;
            if (!string.Equals(ancestor, ComponentCatalog.RootClassName, StringComparison.OrdinalIgnoreCase) && !_byName.ContainsKey(ancestor))
            {
                Problem(file, path, $"unknown ancestor {ancestor} of {className} (skipped with its descendants)");
                return;
            }

            if (element.TryGetProperty("kind", out var kind) && ParseKind(kind) is null)
            {
                Problem(file, path, $"invalid kind {kind.GetRawText()} of {className} (skipped)");
                return;
            }

            var raw = new RawComponent(className, ancestor, unit, file, path, element,
                ReadMembers(element, "properties", className, file, path, "property"),
                ReadMembers(element, "events", className, file, path, "event"));
            _components.Add(raw);
            _byName[className] = raw;
        }

        private List<RawMember> ReadMembers(JsonElement component, string key, string className, string file, string path, string what)
        {
            var members = new List<RawMember>();
            if (!component.TryGetProperty(key, out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return members;
            }

            var i = 0;
            foreach (var member in list.EnumerateArray())
            {
                var memberPath = $"{path}.{key}[{i++}]";
                if (member.ValueKind != JsonValueKind.Object || String(member, "name") is not { Length: > 0 } name)
                {
                    Problem(file, memberPath, $"{what} of {className} without \"name\" (skipped)");
                    continue;
                }

                members.Add(new RawMember(name, member.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()), file, memberPath, className));
            }

            return members;
        }

        public CatalogLoadResult Build()
        {
            foreach (var raw in _rawTypes.Values)
            {
                ResolveType(raw.Name);
            }

            var built = new Dictionary<string, Flattened>(StringComparer.OrdinalIgnoreCase);
            var infos = new List<ComponentInfo>();
            foreach (var raw in _components)
            {
                var ancestor = built.GetValueOrDefault(raw.Ancestor);
                var flattened = Flatten(raw, ancestor);
                built[raw.ClassName] = flattened;
                infos.Add(flattened.Info);
            }

            var catalog = new ComponentCatalog(infos, _types.Values, _signatures, _palettePages, _signatureUnits);
            return new CatalogLoadResult(catalog, [.. _problems]);
        }

        /// <summary>The effective (flattened) declaration lists of a component, kept for its descendants.</summary>
        private sealed record Flattened(ComponentInfo Info, List<RawMember> Properties, List<RawMember> Events);

        private Flattened Flatten(RawComponent raw, Flattened? ancestor)
        {
            var e = raw.Element;
            var properties = Merge(ancestor?.Properties ?? [RawTag], raw.Properties);
            var events = Merge(ancestor?.Events ?? [], raw.Events);
            if (raw.Events.Any(ev => Bool(ev.Keys, "default") == true))
            {
                // A descendant that sets the default event overrides the inherited choice (component-metadata.md §5).
                var chosen = raw.Events.Last(ev => Bool(ev.Keys, "default") == true).Name;
                events = [.. events.Select(ev => string.Equals(ev.Name, chosen, StringComparison.OrdinalIgnoreCase) || Bool(ev.Keys, "default") != true
                    ? ev
                    : ev with { Keys = new Dictionary<string, JsonElement>(ev.Keys) { ["default"] = FalseElement } })];
            }

            var inherited = ancestor?.Info;
            var kind = e.TryGetProperty("kind", out var k) ? ParseKind(k)!.Value : inherited?.Kind ?? ComponentKind.Control;
            var acceptsChildren = Bool(e, "acceptsChildren") ?? inherited?.AcceptsChildren ?? false;
            var renderer = String(e, "renderer") ?? inherited?.RendererId ?? "generic";
            var defaultSize = ReadSize(e) ?? inherited?.DefaultSize ?? new Size(75, 25);
            var namePrefix = String(e, "namePrefix")
                ?? (raw.ClassName.Length > 1 && raw.ClassName[0] is 'T' or 't' ? raw.ClassName[1..] : raw.ClassName);

            PaletteInfo? palette = null;
            if (e.TryGetProperty("palette", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                if (String(p, "page") is { Length: > 0 } page)
                {
                    var icon = String(p, "icon") is { Length: > 0 } relative
                        ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(raw.File)!, relative))
                        : null;
                    palette = new PaletteInfo(page, Int(p, "order") ?? 0, icon);
                }
                else
                {
                    Problem(raw.File, raw.Path, $"palette of {raw.ClassName} without \"page\" (not on the palette)");
                }
            }

            // DSG-SUR-207, 210: the own value, else the inherited one; an invalid value is reported and ignored.
            var baseline = inherited?.Baseline;
            if (e.TryGetProperty("baseline", out var b))
            {
                if (ParseBaseline(b) is { } rule)
                {
                    baseline = rule;
                }
                else
                {
                    Problem(raw.File, raw.Path, $"invalid \"baseline\" of {raw.ClassName} (ignored)");
                }
            }

            var info = new ComponentInfo(raw.ClassName, raw.Ancestor, raw.Unit, Bool(e, "abstract") ?? false, kind, palette,
                defaultSize, namePrefix, acceptsChildren, renderer,
                [.. properties.Select(m => BuildProperty(m, raw.ClassName))],
                [.. events.Select(m => new EventInfo(m.Name, String(m.Keys, "signature") ?? "", Bool(m.Keys, "default") ?? false, m.DeclaringClass))],
                Strings(e, "childClasses"), Strings(e, "parentClasses"),
                Verbs(e, inherited?.DesignerVerbs ?? []), String(e, "componentEditor") ?? inherited?.ComponentEditor)
            {
                Baseline = baseline,
            };
            return new Flattened(info, properties, events);
        }

        /// <summary>
        /// DSG-SUR-207: <c>"none"</c>, <c>{ "align": "top", "inset": 0 … 255 }</c> (inset optional) or
        /// <c>{ "align": "center" }</c>; null for anything else (wrong type, unknown align or member, inset out of range).
        /// </summary>
        private static BaselineRule? ParseBaseline(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() == "none" ? new BaselineRule(BaselineAlign.None) : null;
            }

            if (value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? align = null;
            int? inset = null;
            foreach (var member in value.EnumerateObject())
            {
                if (member.Name == "align" && member.Value.ValueKind == JsonValueKind.String)
                {
                    align = member.Value.GetString();
                }
                else if (member.Name == "inset" && member.Value.ValueKind == JsonValueKind.Number
                    && member.Value.TryGetInt32(out var number) && number is >= 0 and <= 255)
                {
                    inset = number;
                }
                else
                {
                    return null;
                }
            }

            return align switch
            {
                "top" => new BaselineRule(BaselineAlign.Top, inset ?? 0),
                "center" when inset is null => new BaselineRule(BaselineAlign.Center),
                _ => null,
            };
        }

        /// <summary>DSG-TP-006: inherited entries first; a redeclaration replaces in place, merging keys.</summary>
        private static List<RawMember> Merge(List<RawMember> inherited, List<RawMember> own)
        {
            var result = new List<RawMember>(inherited);
            foreach (var member in own)
            {
                var at = result.FindIndex(m => string.Equals(m.Name, member.Name, StringComparison.OrdinalIgnoreCase));
                if (at < 0)
                {
                    result.Add(member);
                    continue;
                }

                var keys = new Dictionary<string, JsonElement>(result[at].Keys);
                foreach (var (key, value) in member.Keys)
                {
                    keys[key] = value;
                }

                // The first declaration keeps its position, name casing and declaring class; problems point at the redeclaration.
                result[at] = result[at] with { Keys = keys, File = member.File, Path = member.Path };
            }

            return result;
        }

        private PropertyInfo BuildProperty(RawMember m, string owner)
        {
            var keys = m.Keys;
            var type = String(keys, "type") ?? "";
            var kindText = String(keys, "kind");
            var readOnly = Bool(keys, "readOnly") ?? false;
            PropertyKind kind;
            var known = true;
            switch (kindText)
            {
                case "object":
                    kind = PropertyKind.Object;
                    known = TypeOf(type) is { Kind: TypeKind.Object };
                    break;
                case "component":
                    kind = PropertyKind.Component;
                    known = _byName.ContainsKey(type) || string.Equals(type, ComponentCatalog.RootClassName, StringComparison.OrdinalIgnoreCase);
                    break;
                case "collection":
                    kind = PropertyKind.Collection;
                    known = String(keys, "itemClass") is { } item && TypeOf(item) is { Kind: TypeKind.Object };
                    break;
                case null:
                    if (BuiltInTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                    {
                        kind = PropertyKind.Simple;
                    }
                    else if (TypeOf(type) is { Kind: not TypeKind.Object } declared)
                    {
                        kind = declared.Kind == TypeKind.Enum ? PropertyKind.Enum : PropertyKind.Set;
                    }
                    else
                    {
                        kind = PropertyKind.Simple;
                        known = false;
                    }

                    break;
                default:
                    kind = PropertyKind.Simple;
                    known = false;
                    break;
            }

            if (!known)
            {
                // DSG-TP-005: kept, with its type treated as string, read-only. Reported once, at the declaring class.
                if (FirstReport(m))
                {
                    Problem(m.File, m.Path, $"unknown type {type} of property {owner}.{m.Name} (treated as read-only string)");
                }

                type = "string";
                kind = PropertyKind.Simple;
                readOnly = true;
            }

            var hasDefault = keys.TryGetValue("default", out var defaultElement);
            var category = String(keys, "category") is { } c && Categories.Contains(c, StringComparer.Ordinal) ? c : "Misc";
            return new PropertyInfo(m.Name, type, kind, hasDefault ? ConvertDefault(defaultElement, type) : null, hasDefault,
                category, String(keys, "initial"), readOnly, Bool(keys, "hidden") ?? false, String(keys, "editor"),
                Number(keys, "min"), Number(keys, "max"), Bool(keys, "affectsLayout") ?? false, String(keys, "itemClass"),
                String(keys, "description"), m.DeclaringClass, Bool(keys, "stored") ?? true, Bool(keys, "localizable") ?? false,
                EditorOptions(keys));
        }

        private readonly HashSet<(string File, string Path)> _reported = [];

        /// <summary>True the first time a declaration is reported (descendants share their ancestor's entries).</summary>
        private bool FirstReport(RawMember m) => _reported.Add((m.File, m.Path));

        private readonly HashSet<string> _resolving = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failedTypes = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The resolved type named <paramref name="name"/>, resolving it on first use.</summary>
        private TypeInfo? TypeOf(string name) => _types.GetValueOrDefault(name) ?? ResolveType(name);

        private TypeInfo? ResolveType(string name)
        {
            if (_types.TryGetValue(name, out var done))
            {
                return done;
            }

            if (_failedTypes.Contains(name) || !_rawTypes.TryGetValue(name, out var raw) || !_resolving.Add(name))
            {
                return null;
            }

            var e = raw.Element;
            TypeInfo? result = null;
            switch (String(e, "kind"))
            {
                case "enum":
                    var values = Strings(e, "values");
                    if (values.IsEmpty)
                    {
                        Problem(raw.File, raw.Path, $"enum {name} without values (skipped)");
                    }
                    else
                    {
                        result = new TypeInfo(raw.Name, TypeKind.Enum, values, null, null, []);
                    }

                    break;
                case "set":
                    var of = String(e, "of");
                    if (of is not null && ResolveType(of) is { Kind: TypeKind.Enum } element)
                    {
                        result = new TypeInfo(raw.Name, TypeKind.Set, element.Values, element.Name, null, []);
                    }
                    else
                    {
                        Problem(raw.File, raw.Path, $"set {name} refers to undeclared enum {of} (skipped)");
                    }

                    break;
                case "object":
                    var ancestorName = String(e, "ancestor");
                    TypeInfo? ancestor = null;
                    if (ancestorName is not null && (ancestor = ResolveType(ancestorName)) is not { Kind: TypeKind.Object })
                    {
                        Problem(raw.File, raw.Path, $"unknown ancestor {ancestorName} of object class {name} (skipped)");
                        break;
                    }

                    var own = new List<RawMember>();
                    if (e.TryGetProperty("properties", out var list) && list.ValueKind == JsonValueKind.Array)
                    {
                        var i = 0;
                        foreach (var member in list.EnumerateArray())
                        {
                            var memberPath = $"{raw.Path}.properties[{i++}]";
                            if (member.ValueKind != JsonValueKind.Object || String(member, "name") is not { Length: > 0 } memberName)
                            {
                                Problem(raw.File, memberPath, $"property of {name} without \"name\" (skipped)");
                                continue;
                            }

                            own.Add(new RawMember(memberName, member.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()), raw.File, memberPath, raw.Name));
                        }
                    }

                    var inherited = ancestor is null ? [] : _objectMembers[ancestor.Name];
                    var merged = Merge(inherited, own);
                    _objectMembers[raw.Name] = merged;
                    // Placeholder first so that a sub-property may refer to this class; properties are built below.
                    result = new TypeInfo(raw.Name, TypeKind.Object, [], null, ancestor?.Name, []);
                    _types[raw.Name] = result;
                    result = result with { Properties = [.. merged.Select(m => BuildProperty(m, raw.Name))] };
                    break;
                default:
                    Problem(raw.File, raw.Path, $"invalid kind of type {name} (skipped)");
                    break;
            }

            _resolving.Remove(name);
            if (result is null)
            {
                _failedTypes.Add(name);
                _types.Remove(name);
                return null;
            }

            _types[raw.Name] = result;
            return result;
        }

        private readonly Dictionary<string, List<RawMember>> _objectMembers = new(StringComparer.OrdinalIgnoreCase);

        private JsonElement? ReadJson(string path)
        {
            var bytes = readFile(path);
            if (bytes is null)
            {
                _problems.Add(new CatalogProblem(path, 1, 1, "file not found"));
                return null;
            }

            try
            {
                var text = new UTF8Encoding(false, true).GetString(bytes);
                using var document = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
                _positions[path] = JsonPositions.Of(Encoding.UTF8.GetBytes(text));
                return document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                _problems.Add(new CatalogProblem(path, (int)(ex.LineNumber ?? 0) + 1, (int)(ex.BytePositionInLine ?? 0) + 1,
                    "JSON syntax error (file skipped)"));
                return null;
            }
            catch (DecoderFallbackException)
            {
                _problems.Add(new CatalogProblem(path, 1, 1, "invalid UTF-8 (file skipped)"));
                return null;
            }
        }

        private bool CheckFormatVersion(JsonElement root, string path)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                Problem(path, "", "the file is not a JSON object (skipped)");
                return false;
            }

            var version = Int(root, "formatVersion");
            if (version is null or > FormatVersion)
            {
                Problem(path, "", $"unsupported formatVersion {(version?.ToString() ?? "(none)")} (file skipped)");
                return false;
            }

            return true;
        }

        private void Problem(string file, string path, string message)
        {
            var (line, column) = _positions.TryGetValue(file, out var positions) ? positions.Find(path) : (1, 1);
            _problems.Add(new CatalogProblem(file, line, column, message));
        }

    }

    private static readonly RawMember RawTag = new("Tag",
        new Dictionary<string, JsonElement>
        {
            ["name"] = Element("\"Tag\""), ["type"] = Element("\"Int64\""), ["default"] = Element("0"), ["category"] = Element("\"Data\""),
        },
        "", "", ComponentCatalog.RootClassName);

    private static readonly JsonElement FalseElement = Element("false");

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static ComponentKind? ParseKind(JsonElement kind) => kind.ValueKind != JsonValueKind.String ? null : kind.GetString() switch
    {
        "control" => ComponentKind.Control,
        "wincontrol" => ComponentKind.WinControl,
        "container" => ComponentKind.Container,
        "nonvisual" => ComponentKind.NonVisual,
        "form" => ComponentKind.Form,
        _ => null,
    };

    private static Size? ReadSize(JsonElement e) =>
        e.TryGetProperty("defaultSize", out var size) && size.ValueKind == JsonValueKind.Object
            && Int(size, "width") is { } w && Int(size, "height") is { } h
            ? new Size(w, h)
            : null;

    private static ImmutableArray<DesignerVerb> Verbs(JsonElement e, ImmutableArray<DesignerVerb> inherited)
    {
        var verbs = inherited.ToList();
        if (e.TryGetProperty("designerVerbs", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in list.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Object || String(v, "id") is not { Length: > 0 } id)
                {
                    continue;
                }

                var verb = new DesignerVerb(id, String(v, "caption") ?? id, String(v, "command") ?? "", String(v, "arg"));
                var at = verbs.FindIndex(x => x.Id == id);
                if (at < 0)
                {
                    verbs.Add(verb);
                }
                else
                {
                    verbs[at] = verb;
                }
            }
        }

        return [.. verbs];
    }

    /// <summary>The JSON default converted per <c>PropertyInfo.Default</c>.</summary>
    private static object? ConvertDefault(JsonElement value, string type) => value.ValueKind switch
    {
        JsonValueKind.Number when string.Equals(type, "Double", StringComparison.OrdinalIgnoreCase) => value.GetDouble(),
        JsonValueKind.Number => value.TryGetInt64(out var l) ? (object)l : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Array => Strings(value),
        _ => null,
    };

    private static IReadOnlyDictionary<string, object>? EditorOptions(Dictionary<string, JsonElement> keys)
    {
        if (!keys.TryGetValue("editorOptions", out var options) || options.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var option in options.EnumerateObject())
        {
            if (ConvertDefault(option.Value, "") is { } converted)
            {
                result[option.Name] = converted;
            }
        }

        return result;
    }

    private static string? String(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? String(Dictionary<string, JsonElement> keys, string key) =>
        keys.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? Bool(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static bool? Bool(Dictionary<string, JsonElement> keys, string key) =>
        keys.TryGetValue(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static int? Int(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static double? Number(Dictionary<string, JsonElement> keys, string key) =>
        keys.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static ImmutableArray<string> Strings(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) ? Strings(v) : [];

    private static ImmutableArray<string> Strings(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)]
            : [];
}
