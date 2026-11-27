using System.Collections.Immutable;
using FreexComp.Compiler.Targets;
using System.Text.Json;

namespace FreexComp.Build;

/// <summary>
/// Typed, immutable view of a <c>.pproj</c> file (docs/interfaces/project-format.md §1, build-engine.md §2).
/// Edit with <c>with</c> expressions and write with <see cref="ProjectSerializer.Save(Project)"/>; the JSON the
/// project was loaded from is kept so that unknown keys survive (CMP-811). Paths are stored with '/'.
/// </summary>
public sealed record Project
{
    public int FormatVersion { get; init; } = ProjectSerializer.SupportedFormatVersion;

    public string Name { get; init; } = "";

    public string Language { get; init; } = "pascal";

    public string AppType { get; init; } = "console";

    public string MainSource { get; init; } = "";

    public ImmutableArray<ProjectFile> Files { get; init; } = [];

    public ImmutableArray<string> SearchPaths { get; init; } = [];

    /// <summary>
    /// Optional key <c>libraryPaths</c> (CMP-1082): directories of ready-made import libraries, relative to the project directory
    /// or absolute, in order. Written when non-empty or present in the loaded file.
    /// </summary>
    public ImmutableArray<string> LibraryPaths { get; init; } = [];

    /// <summary>Optional key; written when non-empty or present in the loaded file.</summary>
    public ImmutableArray<string> UnitScopeNames { get; init; } = [];

    /// <summary>Optional key ("broad" | "published"); null = absent.</summary>
    public string? Rtti { get; init; }

    /// <summary>
    /// Optional key in its string form ("default" | "none" | path); null = absent (default per appType) or the object form
    /// (<see cref="ManifestSettings"/>).
    /// </summary>
    public string? Manifest { get; init; }

    /// <summary>
    /// The object form of the key <c>manifest</c> (CMP-1110…1112); non-null exactly when the file has that form, and then
    /// <see cref="Manifest"/> is null. A model with both set is rejected by <see cref="ProjectSerializer.Save(Project)"/>.
    /// </summary>
    public ManifestSettings? ManifestSettings { get; init; }

    public ImmutableArray<string> Defines { get; init; } = [];

    /// <summary>
    /// The listed platforms (platform-model.md CMP-1360): target ids, checked for their form only — a listed platform need not be
    /// installed (CMP-1361).
    /// </summary>
    public ImmutableArray<string> Platforms { get; init; } = [TargetRegistry.Default.Id];

    /// <summary>
    /// Optional key <c>activePlatform</c> (CMP-1362): one of <see cref="Platforms"/>; null = key absent. The platform a build uses
    /// comes from <see cref="ProjectOptions.GetEffectivePlatform"/>.
    /// </summary>
    public string? ActivePlatform { get; init; }

    /// <summary>
    /// Optional key <c>platformOptions</c> (CMP-1363): platform id → its options; every key is one of <see cref="Platforms"/>.
    /// Written in the order of <see cref="Platforms"/>; an entry that holds nothing is not written.
    /// </summary>
    public ImmutableDictionary<string, PlatformOptions> PlatformOptions { get; init; } = ImmutableDictionary<string, PlatformOptions>.Empty;

    /// <summary>
    /// Optional key <c>jobs</c> (CMP-1133): the project-wide default number of parallel compile jobs, 0…256; 0 = automatic (also
    /// when the key is absent). Written when non-zero or present in the loaded file.
    /// </summary>
    public int Jobs { get; init; }

    /// <summary>Order: see <see cref="ConfigurationNames"/>.</summary>
    public ImmutableDictionary<string, ProjectConfiguration> Configurations { get; init; } =
        ImmutableDictionary<string, ProjectConfiguration>.Empty;

    public string ActiveConfiguration { get; init; } = "Debug";

    public RunSettings Run { get; init; } = new("", "");

    public ProjectEvents Events { get; init; } = new([], []);

    /// <summary>
    /// Optional key <c>icon</c> (resource-manager.md CMP-1427): the application icon (<c>.ico</c>, relative to the project directory
    /// or absolute, '/'), linked as icon group 1; null = absent.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>Optional key <c>versionInfo</c> (CMP-1427): null = absent, no generated version resource.</summary>
    public ProjectVersionInfo? VersionInfo { get; init; }

    /// <summary>
    /// Optional key <c>resources</c> (CMP-1427): named data files linked into the image, in file order. Written when non-empty or
    /// present in the loaded file.
    /// </summary>
    public ImmutableArray<ProjectResource> Resources { get; init; } = [];

    public ImmutableArray<string> Dependencies { get; init; } = [];

    /// <summary>Absolute, normalised path of the .pproj; not serialised. "" for a project not yet saved.</summary>
    public string FilePath { get; init; } = "";

    /// <summary>Directory of <see cref="FilePath"/> ("" when it is empty).</summary>
    public string Directory => FilePath.LastIndexOf('\\') is var i and >= 0 ? (i == 2 ? FilePath[..3] : FilePath[..i]) : "";

    /// <summary>
    /// Configuration names in file order: names in the order of the loaded file, then names added since,
    /// in ordinal order. This is also the order in which they are saved.
    /// </summary>
    public ImmutableArray<string> ConfigurationNames
    {
        get
        {
            var ordered = new List<string>();
            if (Source is { } source
                && source.TryGetProperty("configurations", out var configurations)
                && configurations.ValueKind == JsonValueKind.Object)
            {
                ordered.AddRange(configurations.EnumerateObject().Select(p => p.Name).Where(Configurations.ContainsKey));
            }

            ordered.AddRange(Configurations.Keys.Except(ordered, StringComparer.Ordinal).Order(StringComparer.Ordinal));
            return [.. ordered];
        }
    }

    /// <summary>The JSON root the project was loaded from (null for a new project).</summary>
    internal JsonElement? Source { get; init; }
}

/// <param name="Kind">As written in the file (null = absent); see <see cref="EffectiveKind"/>.</param>
public sealed record ProjectFile(string Path, string? Form, string? Kind)
{
    /// <summary><see cref="Kind"/>, or the default from the extension: .pas → unit, .form → form, .rc and .res → resource (CMP-1313), else other.</summary>
    public string EffectiveKind =>
        Kind ?? System.IO.Path.GetExtension(Path).ToLowerInvariant() switch
        {
            ".pas" => "unit",
            ".form" => "form",
            ".rc" or ".res" => "resource",
            _ => "other",
        };
}

/// <summary>
/// One entry of <c>resources</c> (resource-manager.md CMP-1427, 1428): <paramref name="Path"/> relative to the project directory or
/// absolute ('/'), <paramref name="Type"/> one of <see cref="ProjectResources.Types"/>, <paramref name="Name"/> an identifier or a
/// number 1…65535 (<see cref="ProjectResources.IsValidName"/>).
/// </summary>
public sealed record ProjectResource(string Path, string Type, string Name);

/// <summary>
/// The <c>.pproj</c> <c>versionInfo</c> object (CMP-1427, 1430): each member null = key absent. A version is 1–4 numbers
/// 0…65535 separated by '.' (<see cref="ProjectResources.IsValidVersion"/>); an absent <see cref="FileVersion"/> builds as
/// <c>1.0.0.0</c>, an absent <see cref="ProductVersion"/> as the file version, an absent text as "".
/// </summary>
public sealed record ProjectVersionInfo
{
    public string? FileVersion { get; init; }

    public string? ProductVersion { get; init; }

    public string? CompanyName { get; init; }

    public string? FileDescription { get; init; }

    public string? ProductName { get; init; }

    public string? LegalCopyright { get; init; }

    public string? Comments { get; init; }
}

/// <summary>
/// The <c>.pproj</c> <c>manifest</c> object (CMP-1110): each member null = key absent. <paramref name="ExecutionLevel"/> is
/// "asInvoker" | "highestAvailable" | "requireAdministrator", <paramref name="DpiAwareness"/> "perMonitorV2" | "system" |
/// "unaware", <paramref name="CustomFile"/> a manifest file (relative to the project or absolute, '/'), which excludes the other two.
/// </summary>
public sealed record ManifestSettings(string? ExecutionLevel, string? DpiAwareness, string? CustomFile);

/// <summary>
/// One entry of <c>configurations</c>. The members hold the configuration's <b>own</b> values (configurations.md CMP-1156); the
/// values it is built with — merged with its ancestors and the project level — come from <see cref="ConfigurationResolver.Resolve"/>.
/// </summary>
/// <param name="Optimization">Own value; 0 and unused when <see cref="InheritsOptimization"/>.</param>
/// <param name="DebugInfo">Own value; false and unused when <see cref="InheritsDebugInfo"/>.</param>
/// <param name="Defines">Own entries, including a leading reset marker <c>"!"</c>.</param>
/// <param name="OutputDir">Own value, or <c>bin/&lt;name&gt;</c>; never inherited.</param>
/// <param name="Rtti">Optional per-configuration RTTI scope; null = not set here.</param>
public sealed record ProjectConfiguration(int Optimization, bool DebugInfo, ImmutableArray<string> Defines, string OutputDir, string? Rtti = null)
{
    /// <summary>The key <c>inherits</c>: the parent configuration (exact name); null = root configuration.</summary>
    public string? Inherits { get; init; }

    /// <summary>Derived configurations only: no own <c>optimization</c>. False and ignored for a root configuration.</summary>
    public bool InheritsOptimization { get; init; }

    /// <summary>Derived configurations only: no own <c>debugInfo</c>. False and ignored for a root configuration.</summary>
    public bool InheritsDebugInfo { get; init; }

    /// <summary>
    /// The key <c>searchPaths</c> of the configuration (CMP-1152): own unit search directories, including a leading reset marker
    /// <c>"!"</c>; used when this configuration or one derived from it is built.
    /// </summary>
    public ImmutableArray<string> SearchPaths { get; init; } = [];

    /// <summary>
    /// The key <c>events</c> of the configuration (build-events.md CMP-1251): null = the configuration has no such key. A value
    /// replaces the project's events as a whole for this configuration and for those derived from it that have none of their own
    /// (<see cref="ProjectOptions.GetEffectiveEvents"/>).
    /// </summary>
    public ProjectEvents? Events { get; init; }
}

/// <summary>
/// One entry of <c>platformOptions</c> (platform-model.md CMP-1363, CMP-1365, CMP-1366): what a platform adds to the values of the
/// project and of the configuration being built.
/// </summary>
/// <param name="Defines">Appended after the configuration chain; a leading <c>"!"</c> discards what came before.</param>
/// <param name="SearchPaths">As <paramref name="Defines"/>, for the unit search directories.</param>
/// <param name="LibraryPaths">As <paramref name="Defines"/>, for the directories of import libraries.</param>
/// <param name="OutputDir">
/// The output directory of this platform for every configuration, with the macros <c>$(Config)</c> and <c>$(Platform)</c>; null =
/// the rule of <see cref="ProjectOptions.GetOutputDirectory"/>.
/// </param>
public sealed record PlatformOptions(
    ImmutableArray<string> Defines, ImmutableArray<string> SearchPaths, ImmutableArray<string> LibraryPaths, string? OutputDir);

public sealed record RunSettings(string Arguments, string WorkingDir)
{
    /// <summary>
    /// Optional member <c>hostApplication</c> (CMP-1083): the executable that target <c>Run</c> starts for a library project,
    /// relative to the project directory or absolute; ignored for other application types. Written when non-empty or present in
    /// the loaded file.
    /// </summary>
    public string HostApplication { get; init; } = "";
}

/// <summary>The command lines run before the build and after it (build-events.md).</summary>
public sealed record ProjectEvents(ImmutableArray<string> PreBuild, ImmutableArray<string> PostBuild)
{
    /// <summary>The key <c>postBuildWhen</c> (CMP-1250); written when it is <see cref="PostBuildWhen.Always"/> or present in the loaded file.</summary>
    public PostBuildWhen PostBuildWhen { get; init; }
}

/// <summary>When the post-build commands run (build-events.md CMP-1250, 1267).</summary>
public enum PostBuildWhen
{
    /// <summary>Only when the build of the project succeeded so far.</summary>
    Success,

    /// <summary>Also when the compile, link or deployment step failed.</summary>
    Always,
}

/// <summary>A <c>.pgroup</c> file (project-format.md §2).</summary>
public sealed record ProjectGroup(int FormatVersion, ImmutableArray<string> Projects, string ActiveProject, string FilePath)
{
    internal JsonElement? Source { get; init; }
}

public sealed record LoadResult<T>(T? Value, ImmutableArray<FreexComp.Compiler.Diagnostic> Diagnostics);
