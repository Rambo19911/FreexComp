using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FreexComp.Compiler;
using FreexComp.Compiler.Text;

namespace FreexComp.Build;

/// <summary>
/// The only reader/writer of <c>.pproj</c> and <c>.pgroup</c> files (project-format.md, CMP-809…811).
/// Errors are diagnostics, never exceptions; any error yields a null value.
/// </summary>
public static class ProjectSerializer
{
    public const int SupportedFormatVersion = 1;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static LoadResult<Project> LoadProject(string path, IFileSystem fs) =>
        Load(path, fs, ParseProject);

    public static LoadResult<ProjectGroup> LoadGroup(string path, IFileSystem fs) =>
        Load(path, fs, ParseGroup);

    /// <summary>No I/O: editor buffers, tests. <paramref name="path"/> must be absolute.</summary>
    public static LoadResult<Project> ParseProject(string path, string text) =>
        Parse(path, text, ReadProject);

    /// <summary>Canonical text (CMP-811): UTF-8 without BOM when encoded, LF, 2-space indent, final newline.</summary>
    public static string Save(Project project) => Write(w => WriteProject(w, project));

    public static string Save(ProjectGroup group) => Write(w => WriteObject(w, group.Source,
    [
        ("formatVersion", w => w.WriteNumberValue(group.FormatVersion)),
        ("projects", w => WriteStrings(w, group.Projects, path: true)),
        ("activeProject", w => w.WriteStringValue(Slashes(group.ActiveProject))),
    ]));

    /// <summary>A new project with the defaults of project-format.md §1 (Debug + Release); no file path yet.</summary>
    public static Project CreateDefault(string name, string mainSource, string appType) => new()
    {
        Name = name,
        AppType = appType,
        MainSource = Slashes(mainSource),
        Files = [new ProjectFile(Slashes(mainSource), null, null)],
        Configurations = DefaultConfigurations(),
    };

    // ------------------------------------------------------------------ loading

    private static LoadResult<T> Load<T>(string path, IFileSystem fs, Func<string, string, LoadResult<T>> parse)
    {
        var normalized = PathUtil.Normalize(path);
        if (!fs.FileExists(normalized))
        {
            return new(default, [Diagnostic.Create("E9003", Location.ForFile(normalized), normalized)]);
        }

        string text;
        try
        {
            text = fs.ReadAllText(normalized);
        }
        catch (IOException e)
        {
            return new(default, [Diagnostic.Create("E0003", Location.ForFile(normalized), normalized, e.Message)]);
        }

        return parse(normalized, text);
    }

    private static LoadResult<ProjectGroup> ParseGroup(string path, string text) => Parse(path, text, ReadGroup);

    private static LoadResult<T> Parse<T>(string path, string text, Func<Node, string, T> read)
    {
        var source = SourceText.From(path, text);
        try
        {
            var json = StrictJson.Parse(text);
            var root = new Node(json.Root, "", "", json);
            if (root.Element.ValueKind != JsonValueKind.Object)
            {
                throw ProjectFileException.Invalid(root.Offset, "the root value must be an object");
            }

            return new(read(root, source.Path), []);
        }
        catch (ProjectFileException e)
        {
            var span = new TextSpan(Math.Min(e.Offset, text.Length), 0);
            var position = source.Lines.GetLinePosition(span.Start);
            var location = new Location(source.Path, span, new LinePositionSpan(position, position));
            return new(default, [Diagnostic.Create(e.Code, location, e.Arguments)]);
        }
    }

    private static void CheckFormatVersion(Node root)
    {
        var version = root.Required("formatVersion");
        var value = version.Int();
        if (value < 1)
        {
            throw ProjectFileException.Invalid(version.Offset, "'formatVersion' must be at least 1");
        }

        if (value > SupportedFormatVersion)
        {
            throw new ProjectFileException("E9001", version.Offset, value, SupportedFormatVersion);
        }
    }

    private static Project ReadProject(Node root, string path)
    {
        CheckFormatVersion(root);
        var name = root.Required("name").String();
        var mainSource = Slashes(root.Required("mainSource").String());

        var languageNode = root.Optional("language");
        var language = languageNode?.String() ?? "pascal";
        if (language != "pascal")
        {
            throw new ProjectFileException("E9007", languageNode!.Value.Offset, language);
        }

        var appTypeNode = root.Optional("appType");
        var appType = appTypeNode?.String() ?? "console";
        if (appType is not ("console" or "gui"))
        {
            throw new ProjectFileException("E9008", appTypeNode!.Value.Offset, appType);
        }

        var files = root.Optional("files") is { } filesNode
            ? filesNode.Items().Select(ReadFile).ToImmutableArray()
            : [new ProjectFile(mainSource, null, null)];

        var configurations = DefaultConfigurations();
        if (root.Optional("configurations") is { } configurationsNode)
        {
            configurations = configurationsNode.Properties()
                .ToImmutableDictionary(p => p.Key, p => ReadConfiguration(p.Key, p.Value), StringComparer.Ordinal);
        }

        var run = new RunSettings("", "");
        if (root.Optional("run") is { } runNode)
        {
            runNode.RequireObject();
            run = new RunSettings(
                runNode.Optional("arguments")?.String() ?? "",
                Slashes(runNode.Optional("workingDir")?.String() ?? ""));
        }

        var events = new ProjectEvents([], []);
        if (root.Optional("events") is { } eventsNode)
        {
            eventsNode.RequireObject();
            events = new ProjectEvents(
                eventsNode.Optional("preBuild")?.Strings() ?? [],
                eventsNode.Optional("postBuild")?.Strings() ?? []);
        }

        return new Project
        {
            FormatVersion = root.Required("formatVersion").Int(),
            Name = name,
            Language = language,
            AppType = appType,
            MainSource = mainSource,
            Files = files,
            SearchPaths = root.Optional("searchPaths")?.Strings(path: true) ?? [],
            UnitScopeNames = root.Optional("unitScopeNames")?.Strings() ?? [],
            Rtti = root.Optional("rtti")?.Rtti(),
            Manifest = root.Optional("manifest")?.String() is { } manifest ? ManifestValue(manifest) : null,
            Defines = root.Optional("defines")?.Strings() ?? [],
            Platforms = root.Optional("platforms")?.Strings() ?? ["win64"],
            Jobs = root.Optional("jobs") is { } jobs ? ReadJobs(jobs) : 0,
            Configurations = configurations,
            ActiveConfiguration = root.Optional("activeConfiguration")?.String() ?? "Debug",
            Run = run,
            Events = events,
            Dependencies = root.Optional("dependencies")?.Strings(path: true) ?? [],
            FilePath = path,
            Source = root.Element,
        };
    }

    /// <summary>CMP-1133: an integer from 0 to 256 (0 = automatic).</summary>
    private static int ReadJobs(Node node)
    {
        var jobs = node.Int();
        return jobs is >= 0 and <= 256 ? jobs : throw ProjectFileException.Invalid(node.Offset, $"'{node.Display}' must be an integer from 0 to 256");
    }

    private static ProjectFile ReadFile(Node node)
    {
        node.RequireObject();
        var kind = node.Optional("kind");
        if (kind is { } k && k.String() is not ("unit" or "form" or "other"))
        {
            throw ProjectFileException.Invalid(k.Offset, $"'{k.Display}' must be \"unit\", \"form\" or \"other\"");
        }

        return new ProjectFile(
            Slashes(node.Required("path").String()),
            node.Optional("form")?.String() is { } form ? Slashes(form) : null,
            kind?.String());
    }

    private static ProjectConfiguration ReadConfiguration(string name, Node node)
    {
        node.RequireObject();
        var optimization = 0;
        if (node.Optional("optimization") is { } o)
        {
            optimization = o.Int();
            if (optimization is < 0 or > 2)
            {
                throw ProjectFileException.Invalid(o.Offset, $"'{o.Display}' must be 0, 1 or 2");
            }
        }

        return new ProjectConfiguration(
            optimization,
            node.Optional("debugInfo")?.Bool() ?? false,
            node.Optional("defines")?.Strings() ?? [],
            Slashes(node.Optional("outputDir")?.String() ?? "bin/" + name),
            node.Optional("rtti")?.Rtti());
    }

    private static ProjectGroup ReadGroup(Node root, string path)
    {
        CheckFormatVersion(root);
        var projects = root.Required("projects").Strings(path: true);
        return new ProjectGroup(
            root.Required("formatVersion").Int(),
            projects,
            Slashes(root.Optional("activeProject")?.String() ?? ""),
            path)
        {
            Source = root.Element,
        };
    }

    private static ImmutableDictionary<string, ProjectConfiguration> DefaultConfigurations() =>
        ImmutableDictionary.CreateRange(StringComparer.Ordinal, new[]
        {
            KeyValuePair.Create("Debug", new ProjectConfiguration(0, true, ["DEBUG"], "bin/Debug")),
            KeyValuePair.Create("Release", new ProjectConfiguration(2, false, ["RELEASE"], "bin/Release")),
        });

    private static string ManifestValue(string value) => value is "default" or "none" ? value : Slashes(value);

    internal static string Slashes(string path) => path.Replace('\\', '/');

    // ------------------------------------------------------------------ saving

    private static string Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    private static void WriteProject(Utf8JsonWriter w, Project p)
    {
        var source = p.Source;
        WriteObject(w, source,
        [
            ("formatVersion", w => w.WriteNumberValue(p.FormatVersion)),
            ("name", w => w.WriteStringValue(p.Name)),
            ("language", w => w.WriteStringValue(p.Language)),
            ("appType", w => w.WriteStringValue(p.AppType)),
            ("mainSource", w => w.WriteStringValue(Slashes(p.MainSource))),
            ("files", w => WriteFiles(w, p.Files, Child(source, "files"))),
            ("searchPaths", w => WriteStrings(w, p.SearchPaths, path: true)),
            ("unitScopeNames", Has(source, "unitScopeNames") || !p.UnitScopeNames.IsEmpty ? w => WriteStrings(w, p.UnitScopeNames) : null),
            ("rtti", p.Rtti is { } rtti ? w => w.WriteStringValue(rtti) : null),
            ("manifest", p.Manifest is { } manifest ? w => w.WriteStringValue(ManifestValue(manifest)) : null),
            ("defines", w => WriteStrings(w, p.Defines)),
            ("platforms", w => WriteStrings(w, p.Platforms)),
            ("jobs", Has(source, "jobs") || p.Jobs != 0 ? w => w.WriteNumberValue(p.Jobs) : null),
            ("configurations", w => WriteConfigurations(w, p, Child(source, "configurations"))),
            ("activeConfiguration", w => w.WriteStringValue(p.ActiveConfiguration)),
            ("run", w => WriteObject(w, Child(source, "run"),
            [
                ("arguments", w => w.WriteStringValue(p.Run.Arguments)),
                ("workingDir", w => w.WriteStringValue(Slashes(p.Run.WorkingDir))),
            ])),
            ("events", w => WriteObject(w, Child(source, "events"),
            [
                ("preBuild", w => WriteStrings(w, p.Events.PreBuild)),
                ("postBuild", w => WriteStrings(w, p.Events.PostBuild)),
            ])),
            ("dependencies", w => WriteStrings(w, p.Dependencies, path: true)),
        ]);
    }

    /// <summary>File entries keep their unknown keys by path (ordinal ignore-case), so reordering is safe.</summary>
    private static void WriteFiles(Utf8JsonWriter w, ImmutableArray<ProjectFile> files, JsonElement? source)
    {
        var unused = source is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList()
            : [];
        w.WriteStartArray();
        foreach (var file in files)
        {
            var match = unused.FindIndex(e =>
                e.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String
                && PathUtil.Comparer.Equals(Slashes(path.GetString()!), Slashes(file.Path)));
            JsonElement? entry = null;
            if (match >= 0)
            {
                entry = unused[match];
                unused.RemoveAt(match);
            }

            WriteObject(w, entry,
            [
                ("path", w => w.WriteStringValue(Slashes(file.Path))),
                ("form", file.Form is { } form ? w => w.WriteStringValue(Slashes(form)) : null),
                ("kind", file.Kind is { } kind ? w => w.WriteStringValue(kind) : null),
            ]);
        }

        w.WriteEndArray();
    }

    private static void WriteConfigurations(Utf8JsonWriter w, Project p, JsonElement? source)
    {
        var known = p.ConfigurationNames.Select(name =>
        {
            var c = p.Configurations[name];
            var entry = Child(source, name);
            return (name, (Action<Utf8JsonWriter>?)(w => WriteObject(w, entry,
            [
                ("optimization", w => w.WriteNumberValue(c.Optimization)),
                ("debugInfo", w => w.WriteBooleanValue(c.DebugInfo)),
                ("defines", w => WriteStrings(w, c.Defines)),
                ("outputDir", w => w.WriteStringValue(Slashes(c.OutputDir))),
                ("rtti", c.Rtti is { } rtti ? w => w.WriteStringValue(rtti) : null),
            ])));
        }).ToList();

        // Configurations removed from the model must not survive as "unknown" keys.
        var names = (source is { ValueKind: JsonValueKind.Object } s ? s.EnumerateObject().Select(e => e.Name) : [])
            .Except(p.ConfigurationNames, StringComparer.Ordinal)
            .Select(name => (name, (Action<Utf8JsonWriter>?)null));
        WriteObject(w, source, [.. known, .. names]);
    }

    /// <summary>
    /// Writes the known keys in the given (canonical) order, skipping those whose writer is null. Unknown keys of
    /// <paramref name="source"/> stay where they were: each is emitted after the known key that preceded it in
    /// the source; those before the first known key come first, those after the last known key come last.
    /// </summary>
    private static void WriteObject(Utf8JsonWriter w, JsonElement? source, IReadOnlyList<(string Key, Action<Utf8JsonWriter>? Write)> known)
    {
        var knownKeys = known.Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
        var leading = new List<JsonProperty>();
        var after = new Dictionary<string, List<JsonProperty>>(StringComparer.Ordinal);
        var trailing = new List<JsonProperty>();
        if (source is { ValueKind: JsonValueKind.Object } obj)
        {
            string? anchor = null;
            foreach (var property in obj.EnumerateObject())
            {
                if (knownKeys.Contains(property.Name))
                {
                    anchor = property.Name;
                }
                else if (anchor is null)
                {
                    leading.Add(property);
                }
                else
                {
                    (after.TryGetValue(anchor, out var list) ? list : after[anchor] = []).Add(property);
                }
            }

            // Unknown keys after the last known key stay at the end of the object.
            if (anchor is not null && after.Remove(anchor, out var tail))
            {
                trailing = tail;
            }
        }

        w.WriteStartObject();
        leading.ForEach(p => p.WriteTo(w));
        foreach (var (key, write) in known)
        {
            if (write is not null)
            {
                w.WritePropertyName(key);
                write(w);
            }

            if (after.TryGetValue(key, out var unknown))
            {
                unknown.ForEach(p => p.WriteTo(w));
            }
        }

        trailing.ForEach(p => p.WriteTo(w));
        w.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter w, ImmutableArray<string> values, bool path = false)
    {
        w.WriteStartArray();
        foreach (var value in values)
        {
            w.WriteStringValue(path ? Slashes(value) : value);
        }

        w.WriteEndArray();
    }

    private static JsonElement? Child(JsonElement? parent, string key) =>
        parent is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(key, out var child) ? child : null;

    private static bool Has(JsonElement? parent, string key) => Child(parent, key) is not null;

    // ------------------------------------------------------------------ typed access with positions

    private readonly record struct Node(JsonElement Element, string Pointer, string Display, StrictJson Json)
    {
        public int Offset => Json.OffsetOf(Pointer);

        public Node? Optional(string key) =>
            Element.TryGetProperty(key, out var child)
                ? new Node(child, Pointer + "/" + key, Display.Length == 0 ? key : Display + "." + key, Json)
                : null;

        public Node Required(string key) =>
            Optional(key) ?? throw ProjectFileException.Invalid(Offset, $"missing required key '{key}'");

        public void RequireObject() => Expect(JsonValueKind.Object, "an object");

        public string String()
        {
            Expect(JsonValueKind.String, "a string");
            return Element.GetString()!;
        }

        public int Int()
        {
            Expect(JsonValueKind.Number, "an integer");
            return Element.TryGetInt32(out var value) ? value : throw Wrong("an integer");
        }

        public bool Bool()
        {
            if (Element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw Wrong("a Boolean");
            }

            return Element.GetBoolean();
        }

        public string Rtti() =>
            String() is var value && value is "broad" or "published" ? value : throw Wrong("\"broad\" or \"published\"");

        public IEnumerable<Node> Items()
        {
            Expect(JsonValueKind.Array, "an array");
            var self = this;
            return Element.EnumerateArray().Select((e, i) => new Node(e, self.Pointer + "/" + i, self.Display + "[" + i + "]", self.Json));
        }

        public IEnumerable<KeyValuePair<string, Node>> Properties()
        {
            RequireObject();
            var self = this;
            return Element.EnumerateObject().Select(p => KeyValuePair.Create(p.Name, self.Optional(p.Name)!.Value)).ToList();
        }

        public ImmutableArray<string> Strings(bool path = false) =>
            [.. Items().Select(n => path ? Slashes(n.String()) : n.String())];

        private void Expect(JsonValueKind kind, string what)
        {
            if (Element.ValueKind != kind)
            {
                throw Wrong(what);
            }
        }

        private ProjectFileException Wrong(string what) => ProjectFileException.Invalid(Offset, $"'{Display}' must be {what}");
    }
}
