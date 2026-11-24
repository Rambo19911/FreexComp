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

    /// <summary>Optional key; written when non-empty or present in the loaded file.</summary>
    public ImmutableArray<string> UnitScopeNames { get; init; } = [];

    /// <summary>Optional key ("broad" | "published"); null = absent.</summary>
    public string? Rtti { get; init; }

    /// <summary>Optional key ("default" | "none" | path); null = absent (default per appType).</summary>
    public string? Manifest { get; init; }

    public ImmutableArray<string> Defines { get; init; } = [];

    public ImmutableArray<string> Platforms { get; init; } = [TargetRegistry.Default.Id];

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
    /// <summary><see cref="Kind"/>, or the default from the extension: .pas → unit, .form → form, else other.</summary>
    public string EffectiveKind =>
        Kind ?? System.IO.Path.GetExtension(Path).ToLowerInvariant() switch
        {
            ".pas" => "unit",
            ".form" => "form",
            _ => "other",
        };
}

/// <param name="Rtti">Optional per-configuration RTTI scope; null = absent.</param>
public sealed record ProjectConfiguration(int Optimization, bool DebugInfo, ImmutableArray<string> Defines, string OutputDir, string? Rtti = null);

public sealed record RunSettings(string Arguments, string WorkingDir);

public sealed record ProjectEvents(ImmutableArray<string> PreBuild, ImmutableArray<string> PostBuild);

/// <summary>A <c>.pgroup</c> file (project-format.md §2).</summary>
public sealed record ProjectGroup(int FormatVersion, ImmutableArray<string> Projects, string ActiveProject, string FilePath)
{
    internal JsonElement? Source { get; init; }
}

public sealed record LoadResult<T>(T? Value, ImmutableArray<FreexComp.Compiler.Diagnostic> Diagnostics);
