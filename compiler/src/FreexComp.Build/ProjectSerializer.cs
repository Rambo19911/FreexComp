using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FreexComp.Compiler;
using FreexComp.Compiler.Targets;
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
    /// <exception cref="ArgumentException">
    /// The model holds both forms of the key <c>manifest</c> (CMP-1112), its <c>Inherits</c> links name a missing configuration
    /// or form a cycle (CMP-1157), or its <c>ActivePlatform</c> or a key of its <c>PlatformOptions</c> is not one of its
    /// <c>Platforms</c> (CMP-1362, CMP-1363).
    /// </exception>
    public static string Save(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Manifest is not null && project.ManifestSettings is not null)
        {
            throw new ArgumentException("A project holds either 'Manifest' or 'ManifestSettings', not both", nameof(project));
        }

        ProjectConfigurations.RequireValidLinks(project);
        var platforms = project.Platforms.IsDefault ? [] : project.Platforms;
        if (project.ActivePlatform is { } active && !platforms.Contains(active, StringComparer.Ordinal))
        {
            throw new ArgumentException($"The active platform '{active}' is not listed in 'Platforms'", nameof(project));
        }

        if (project.PlatformOptions.Keys.Order(StringComparer.Ordinal).FirstOrDefault(id => !platforms.Contains(id, StringComparer.Ordinal)) is { } unlisted)
        {
            throw new ArgumentException($"'PlatformOptions' has an entry for '{unlisted}', which is not listed in 'Platforms'", nameof(project));
        }

        return Write(w => WriteProject(w, project));
    }

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
            Diagnostic At(ProjectFileException error)
            {
                var span = new TextSpan(Math.Min(error.Offset, text.Length), 0);
                var position = source.Lines.GetLinePosition(span.Start);
                return Diagnostic.Create(error.Code, new Location(source.Path, span, new LinePositionSpan(position, position)), error.Arguments);
            }

            return new(default, [At(e), .. e.Additional.Select(At)]);
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
        if (appType is not ("console" or "gui" or "library"))                   // CMP-1080: E9008 remains for package and unknown values
        {
            throw new ProjectFileException("E9008", appTypeNode!.Value.Offset, appType);
        }

        var files = root.Optional("files") is { } filesNode
            ? filesNode.Items().Select(ReadFile).ToImmutableArray()
            : [new ProjectFile(mainSource, null, null)];

        var configurations = DefaultConfigurations();
        if (root.Optional("configurations") is { } configurationsNode)
        {
            var entries = configurationsNode.Properties().ToList();
            configurations = entries.ToImmutableDictionary(p => p.Key, p => ReadConfiguration(p.Key, p.Value), StringComparer.Ordinal);
            CheckInheritance(entries, configurations);
        }

        // CMP-1154: the reset marker belongs to a configuration's list only
        foreach (var key in new[] { "defines", "searchPaths" })
        {
            if (root.Optional(key) is { } list)
            {
                ReadList(list, path: false, resetAllowed: false);
            }
        }

        var platforms = root.Optional("platforms") is { } platformsNode ? ReadPlatforms(platformsNode) : [TargetRegistry.Default.Id];
        string? activePlatform = null;
        if (root.Optional("activePlatform") is { } activeNode)
        {
            // CMP-1362: one of the listed platforms
            activePlatform = activeNode.String();
            if (!platforms.Contains(activePlatform, StringComparer.Ordinal))
            {
                throw ProjectFileException.Invalid(activeNode.Offset, $"active platform '{activePlatform}' is not listed in platforms");
            }
        }

        var platformOptions = root.Optional("platformOptions") is { } optionsNode
            ? ReadPlatformOptions(optionsNode, platforms)
            : ImmutableDictionary<string, PlatformOptions>.Empty;

        var run = new RunSettings("", "");
        if (root.Optional("run") is { } runNode)
        {
            runNode.RequireObject();
            run = new RunSettings(
                runNode.Optional("arguments")?.String() ?? "",
                Slashes(runNode.Optional("workingDir")?.String() ?? ""))
            {
                HostApplication = Slashes(runNode.Optional("hostApplication")?.String() ?? ""),
            };
        }

        var events = root.Optional("events") is { } eventsNode ? ReadEvents(eventsNode) : new ProjectEvents([], []);

        // CMP-1110: the string forms, or an object with the settings of the generated manifest
        string? manifest = null;
        ManifestSettings? manifestSettings = null;
        if (root.Optional("manifest") is { } manifestNode)
        {
            switch (manifestNode.Element.ValueKind)
            {
                case JsonValueKind.String:
                    manifest = ManifestValue(manifestNode.String());
                    break;
                case JsonValueKind.Object:
                    manifestSettings = ReadManifestSettings(manifestNode);
                    break;
                default:
                    throw ProjectFileException.Invalid(manifestNode.Offset, "'manifest' must be a string or an object");
            }
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
            LibraryPaths = root.Optional("libraryPaths")?.Strings(path: true) ?? [],
            UnitScopeNames = root.Optional("unitScopeNames")?.Strings() ?? [],
            Rtti = root.Optional("rtti")?.Rtti(),
            Manifest = manifest,
            ManifestSettings = manifestSettings,
            Defines = root.Optional("defines")?.Strings() ?? [],
            Platforms = platforms,
            ActivePlatform = activePlatform,
            PlatformOptions = platformOptions,
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

    internal static readonly ImmutableArray<string> ExecutionLevels = ["asInvoker", "highestAvailable", "requireAdministrator"];

    internal static readonly ImmutableArray<string> DpiAwarenessModes = ["perMonitorV2", "system", "unaware"];

    /// <summary>CMP-1111: the keys of the <c>manifest</c> object, each validated at its token.</summary>
    private static ManifestSettings ReadManifestSettings(Node node)
    {
        string? Word(string key, ImmutableArray<string> allowed)
        {
            if (node.Optional(key) is not { } child)
            {
                return null;
            }

            var value = child.String();
            return allowed.Contains(value, StringComparer.Ordinal)
                ? value
                : throw ProjectFileException.Invalid(child.Offset,
                    $"'{child.Display}' must be \"{allowed[0]}\", \"{allowed[1]}\" or \"{allowed[2]}\"");
        }

        var level = Word("executionLevel", ExecutionLevels);
        var dpi = Word("dpiAwareness", DpiAwarenessModes);
        string? customFile = null;
        if (node.Optional("customFile") is { } file)
        {
            customFile = Slashes(file.String());
            if (customFile.Length == 0)
            {
                throw ProjectFileException.Invalid(file.Offset, $"'{file.Display}' must not be empty");
            }

            if (level is not null || dpi is not null)
            {
                throw ProjectFileException.Invalid(file.Offset, $"'{file.Display}' cannot be combined with 'executionLevel' or 'dpiAwareness'");
            }
        }

        return new ManifestSettings(level, dpi, customFile);
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
        if (kind is { } k && k.String() is not ("unit" or "form" or "resource" or "other"))
        {
            throw ProjectFileException.Invalid(k.Offset, $"'{k.Display}' must be \"unit\", \"form\", \"resource\" or \"other\"");
        }

        return new ProjectFile(
            Slashes(node.Required("path").String()),
            node.Optional("form")?.String() is { } form ? Slashes(form) : null,
            kind?.String());
    }

    private static ProjectConfiguration ReadConfiguration(string name, Node node)
    {
        node.RequireObject();
        string? inherits = null;
        if (node.Optional("inherits") is { } parent)
        {
            // CMP-1150, CMP-1154: the exact name of another configuration
            inherits = parent.Element.ValueKind == JsonValueKind.String && parent.Element.GetString() is { Length: > 0 } parentName
                ? parentName
                : throw ProjectFileException.Invalid(parent.Offset, "'inherits' must be the name of another configuration");
        }

        var optimization = 0;
        var optimizationNode = node.Optional("optimization");
        if (optimizationNode is { } o)
        {
            optimization = o.Int();
            if (optimization is < 0 or > 2)
            {
                throw ProjectFileException.Invalid(o.Offset, $"'{o.Display}' must be 0, 1 or 2");
            }
        }

        var debugInfo = node.Optional("debugInfo");
        return new ProjectConfiguration(
            optimization,
            debugInfo?.Bool() ?? false,
            node.Optional("defines") is { } defines ? ReadList(defines, path: false, resetAllowed: true) : [],
            Slashes(node.Optional("outputDir")?.String() ?? "bin/" + name),
            node.Optional("rtti")?.Rtti())
        {
            Inherits = inherits,
            InheritsOptimization = inherits is not null && optimizationNode is null,      // CMP-1151: absent = the parent's value
            InheritsDebugInfo = inherits is not null && debugInfo is null,
            SearchPaths = node.Optional("searchPaths") is { } searchPaths ? ReadList(searchPaths, path: true, resetAllowed: true) : [],
            Events = node.Optional("events") is { } events ? ReadEvents(events) : null,       // CMP-1251
        };
    }

    /// <summary>
    /// The <c>events</c> object of the project or of a configuration (build-events.md CMP-1250, 1251): every member is optional,
    /// the lists default to empty and <c>postBuildWhen</c> to <c>success</c>.
    /// </summary>
    private static ProjectEvents ReadEvents(Node node)
    {
        node.RequireObject();
        var when = PostBuildWhen.Success;
        if (node.Optional("postBuildWhen") is { } whenNode)
        {
            when = whenNode.String() switch
            {
                "success" => PostBuildWhen.Success,
                "always" => PostBuildWhen.Always,
                _ => throw ProjectFileException.Invalid(whenNode.Offset, $"'{whenNode.Display}' must be \"success\" or \"always\""),
            };
        }

        return new ProjectEvents(node.Optional("preBuild")?.Strings() ?? [], node.Optional("postBuild")?.Strings() ?? []) { PostBuildWhen = when };
    }

    /// <summary>
    /// A list of defines or search paths; the reset marker <c>"!"</c> (CMP-1153) is allowed only as the first entry of a
    /// configuration's list or of a platform's list (CMP-1365; <paramref name="owner"/> names which), else E9002 at its token.
    /// </summary>
    private static ImmutableArray<string> ReadList(Node node, bool path, bool resetAllowed, string owner = "configuration")
    {
        var index = 0;
        var values = ImmutableArray.CreateBuilder<string>();
        foreach (var item in node.Items())
        {
            var value = item.String();
            if (value == ConfigurationResolver.ResetMarker && (index > 0 || !resetAllowed))
            {
                throw ProjectFileException.Invalid(item.Offset, $"'!' is allowed only as the first entry of a {owner}'s list");
            }

            values.Add(path ? Slashes(value) : value);
            index++;
        }

        return values.ToImmutable();
    }

    /// <summary>
    /// CMP-1154: every <c>inherits</c> names a key of <c>configurations</c> (else E9011) and no chain returns to its start (else
    /// E9015, once per cycle, at the <c>inherits</c> token of the member that comes first in the file, the chain written from it).
    /// </summary>
    private static void CheckInheritance(
        List<KeyValuePair<string, Node>> entries, ImmutableDictionary<string, ProjectConfiguration> configurations)
    {
        var errors = new List<ProjectFileException>();
        int InheritsOffset(string name) => entries.First(e => e.Key == name).Value.Optional("inherits")!.Value.Offset;

        foreach (var (name, _) in entries)
        {
            if (configurations[name].Inherits is { } parent && !configurations.ContainsKey(parent))
            {
                errors.Add(new ProjectFileException("E9011", InheritsOffset(name), name, parent));
            }
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, _) in entries)
        {
            if (reported.Contains(name))
            {
                continue;
            }

            var chain = new List<string> { name };
            for (var parent = configurations[name].Inherits;
                 parent is not null && configurations.TryGetValue(parent, out var next);
                 parent = next.Inherits)
            {
                if (parent == name)
                {
                    reported.UnionWith(chain);
                    errors.Add(new ProjectFileException("E9015", InheritsOffset(name), string.Join(" -> ", chain.Append(name))));
                    break;
                }

                if (chain.Contains(parent, StringComparer.Ordinal))
                {
                    break;                                              // leads into a cycle it is not a member of
                }

                chain.Add(parent);
            }
        }

        if (errors.Count > 0)
        {
            errors.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            throw new ProjectFileException(errors[0].Code, errors[0].Offset, errors[0].Arguments) { Additional = errors.GetRange(1, errors.Count - 1) };
        }
    }

    /// <summary>
    /// CMP-1360: every entry of <c>platforms</c> has the form of a target id and stands once, else E9002 at its token. The ids are
    /// not compared with the registry: a listed platform need not be installed (CMP-1361).
    /// </summary>
    private static ImmutableArray<string> ReadPlatforms(Node node)
    {
        var ids = ImmutableArray.CreateBuilder<string>();
        foreach (var item in node.Items())
        {
            var id = item.String();
            if (!IsPlatformId(id))
            {
                throw ProjectFileException.Invalid(item.Offset, $"invalid platform id '{id}'");
            }

            if (ids.Contains(id, StringComparer.Ordinal))
            {
                throw ProjectFileException.Invalid(item.Offset, $"platform '{id}' is listed twice");
            }

            ids.Add(id);
        }

        return ids.ToImmutable();
    }

    /// <summary>The form of a target id (targets.md §2): <c>^[a-z][a-z0-9]{1,15}$</c>.</summary>
    private static bool IsPlatformId(string id) =>
        id.Length is >= 2 and <= 16 && char.IsAsciiLetterLower(id[0]) && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c));

    /// <summary>
    /// CMP-1363: the object <c>platformOptions</c>. Each key is a listed platform, else E9002 at the key; each value an object with
    /// the optional keys <c>defines</c>, <c>searchPaths</c>, <c>libraryPaths</c> and <c>outputDir</c> (CMP-1366: no macro other
    /// than <c>$(Config)</c> and <c>$(Platform)</c>). Other keys are kept by the writer and ignored here.
    /// </summary>
    private static ImmutableDictionary<string, PlatformOptions> ReadPlatformOptions(Node node, ImmutableArray<string> platforms)
    {
        var result = ImmutableDictionary.CreateBuilder<string, PlatformOptions>(StringComparer.Ordinal);
        foreach (var (id, entry) in node.Properties())
        {
            if (!platforms.Contains(id, StringComparer.Ordinal))
            {
                throw ProjectFileException.Invalid(entry.KeyOffset, $"platform options for '{id}', which is not listed in platforms");
            }

            entry.RequireObject();
            ImmutableArray<string> List(string key, bool path) =>
                entry.Optional(key) is { } list ? ReadList(list, path, resetAllowed: true, owner: "platform") : [];

            var defines = List("defines", path: false);
            var searchPaths = List("searchPaths", path: true);
            var libraryPaths = List("libraryPaths", path: true);
            string? outputDir = null;
            if (entry.Optional("outputDir") is { } directory)
            {
                outputDir = Slashes(directory.String());
                if (!ProjectOptions.IsPlatformOutputDir(outputDir))
                {
                    throw ProjectFileException.Invalid(directory.Offset, $"'{directory.Display}' may use only the macros $(Config) and $(Platform)");
                }
            }

            result.Add(id, new PlatformOptions(defines, searchPaths, libraryPaths, outputDir));
        }

        return result.ToImmutable();
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
            ("libraryPaths", Has(source, "libraryPaths") || !p.LibraryPaths.IsEmpty ? w => WriteStrings(w, p.LibraryPaths, path: true) : null),
            ("unitScopeNames", Has(source, "unitScopeNames") || !p.UnitScopeNames.IsEmpty ? w => WriteStrings(w, p.UnitScopeNames) : null),
            ("rtti", p.Rtti is { } rtti ? w => w.WriteStringValue(rtti) : null),
            ("manifest", p.Manifest is { } manifest
                ? w => w.WriteStringValue(ManifestValue(manifest))
                : p.ManifestSettings is { } settings
                    ? w => WriteObject(w, Child(source, "manifest"),                           // CMP-1110: this key order
                    [
                        ("executionLevel", settings.ExecutionLevel is { } level ? w => w.WriteStringValue(level) : null),
                        ("dpiAwareness", settings.DpiAwareness is { } dpi ? w => w.WriteStringValue(dpi) : null),
                        ("customFile", settings.CustomFile is { } file ? w => w.WriteStringValue(Slashes(file)) : null),
                    ])
                    : null),
            ("defines", w => WriteStrings(w, p.Defines)),
            ("platforms", w => WriteStrings(w, p.Platforms)),
            ("activePlatform", p.ActivePlatform is { } activePlatform ? w => w.WriteStringValue(activePlatform) : null),     // CMP-1362
            ("platformOptions", PlatformOptionsWriter(p, Child(source, "platformOptions"))),
            ("jobs", Has(source, "jobs") || p.Jobs != 0 ? w => w.WriteNumberValue(p.Jobs) : null),
            ("configurations", w => WriteConfigurations(w, p, Child(source, "configurations"))),
            ("activeConfiguration", w => w.WriteStringValue(p.ActiveConfiguration)),
            ("run", w => WriteObject(w, Child(source, "run"),
            [
                ("arguments", w => w.WriteStringValue(p.Run.Arguments)),
                ("workingDir", w => w.WriteStringValue(Slashes(p.Run.WorkingDir))),
                ("hostApplication", Has(Child(source, "run"), "hostApplication") || p.Run.HostApplication.Length > 0
                    ? w => w.WriteStringValue(Slashes(p.Run.HostApplication))
                    : null),
            ])),
            ("events", w => WriteEvents(w, p.Events, Child(source, "events"), always: true)),
            ("dependencies", w => WriteStrings(w, p.Dependencies, path: true)),
        ]);
    }

    /// <summary>
    /// CMP-1363: the entries in the order of <c>platforms</c>, the keys of an entry in the order <c>defines</c>,
    /// <c>searchPaths</c>, <c>libraryPaths</c>, <c>outputDir</c>; a list is written when it is non-empty or present in the loaded
    /// file. An entry without any key (known or unknown) is not written; null = no entry is written, so the key is not either.
    /// </summary>
    private static Action<Utf8JsonWriter>? PlatformOptionsWriter(Project p, JsonElement? source)
    {
        var entries = new List<(string Key, Action<Utf8JsonWriter>? Write)>();
        foreach (var id in p.Platforms.IsDefault ? [] : p.Platforms)
        {
            if (!p.PlatformOptions.TryGetValue(id, out var options))
            {
                continue;
            }

            var entry = Child(source, id);
            Action<Utf8JsonWriter>? List(string key, ImmutableArray<string> values, bool path) =>
                !values.IsDefaultOrEmpty || Has(entry, key) ? w => WriteStrings(w, values, path) : null;

            (string Key, Action<Utf8JsonWriter>? Write)[] keys =
            [
                ("defines", List("defines", options.Defines, path: false)),
                ("searchPaths", List("searchPaths", options.SearchPaths, path: true)),
                ("libraryPaths", List("libraryPaths", options.LibraryPaths, path: true)),
                ("outputDir", options.OutputDir is { } outputDir ? w => w.WriteStringValue(Slashes(outputDir)) : null),
            ];
            var hasUnknownKeys = entry is { ValueKind: JsonValueKind.Object } e && e.EnumerateObject().Any(m => keys.All(k => k.Key != m.Name));
            if (hasUnknownKeys || keys.Any(k => k.Write is not null))
            {
                entries.Add((id, w => WriteObject(w, entry, keys)));
            }
        }

        if (entries.Count == 0)
        {
            return null;
        }

        // Entries that are not written (empty, or of a platform that left the model) must not survive as "unknown" keys.
        var dropped = (source is { ValueKind: JsonValueKind.Object } s ? s.EnumerateObject().Select(m => m.Name) : [])
            .Except(entries.Select(entry => entry.Key), StringComparer.Ordinal)
            .Select(name => (name, (Action<Utf8JsonWriter>?)null));
        return w => WriteObject(w, source, [.. entries, .. dropped]);
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
            var derived = c.Inherits is not null;

            // CMP-1155: a root configuration is written as before; a derived one only with what it holds itself
            return (name, (Action<Utf8JsonWriter>?)(w => WriteObject(w, entry,
            [
                ("inherits", derived ? w => w.WriteStringValue(c.Inherits) : null),
                ("optimization", !derived || !c.InheritsOptimization ? w => w.WriteNumberValue(c.Optimization) : null),
                ("debugInfo", !derived || !c.InheritsDebugInfo ? w => w.WriteBooleanValue(c.DebugInfo) : null),
                ("defines", !derived || !c.Defines.IsDefaultOrEmpty || Has(entry, "defines") ? w => WriteStrings(w, c.Defines) : null),
                ("searchPaths", !c.SearchPaths.IsDefaultOrEmpty || Has(entry, "searchPaths") ? w => WriteStrings(w, c.SearchPaths, path: true) : null),
                ("outputDir", !derived || Slashes(c.OutputDir) != "bin/" + name || Has(entry, "outputDir")
                    ? w => w.WriteStringValue(Slashes(c.OutputDir))
                    : null),
                ("rtti", c.Rtti is { } rtti ? w => w.WriteStringValue(rtti) : null),
                ("events", c.Events is { } events ? w => WriteEvents(w, events, Child(entry, "events"), always: false) : null),   // CMP-1251
            ])));
        }).ToList();

        // Configurations removed from the model must not survive as "unknown" keys.
        var names = (source is { ValueKind: JsonValueKind.Object } s ? s.EnumerateObject().Select(e => e.Name) : [])
            .Except(p.ConfigurationNames, StringComparer.Ordinal)
            .Select(name => (name, (Action<Utf8JsonWriter>?)null));
        WriteObject(w, source, [.. known, .. names]);
    }

    /// <summary>
    /// An <c>events</c> object: <c>preBuild</c>, <c>postBuild</c>, <c>postBuildWhen</c> (CMP-1250). The lists are written
    /// <paramref name="always"/> (project level) or when they are non-empty or present in the loaded file (a configuration, so
    /// that <c>{}</c> stays <c>{}</c>); <c>postBuildWhen</c> when it is <c>always</c> or present in the loaded file.
    /// </summary>
    private static void WriteEvents(Utf8JsonWriter w, ProjectEvents events, JsonElement? source, bool always)
    {
        Action<Utf8JsonWriter>? List(string key, ImmutableArray<string> commands) =>
            always || !commands.IsDefaultOrEmpty || Has(source, key) ? w => WriteStrings(w, commands) : null;

        WriteObject(w, source,
        [
            ("preBuild", List("preBuild", events.PreBuild)),
            ("postBuild", List("postBuild", events.PostBuild)),
            ("postBuildWhen", events.PostBuildWhen == PostBuildWhen.Always || Has(source, "postBuildWhen")
                ? w => w.WriteStringValue(events.PostBuildWhen == PostBuildWhen.Always ? "always" : "success")
                : null),
        ]);
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
        foreach (var value in values.IsDefault ? [] : values)
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

        /// <summary>The offset of the property name this value belongs to (0 for the root and for array items).</summary>
        public int KeyOffset => Json.KeyOffsetOf(Pointer);

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
