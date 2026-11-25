using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Targets;
using FreexComp.Compiler.Text;

namespace FreexComp.Build;

/// <summary>
/// Maps a project configuration to compiler options (CMP-812). Every per-configuration value is the effective one of
/// <see cref="ConfigurationResolver.Resolve"/> (configurations.md CMP-1161); a platform adds its lists and has its own output
/// directory (platform-model.md CMP-1364…1368).
/// </summary>
public static class ProjectOptions
{
    /// <summary>The macros of <see cref="PlatformOptions.OutputDir"/> (CMP-1366).</summary>
    private static readonly ImmutableArray<string> OutputDirMacros = ["Config", "Platform"];

    /// <summary>
    /// CMP-1364, the only statement of the rule: the platform a build of <paramref name="project"/> uses —
    /// <paramref name="requested"/> (<see cref="BuildRequest.Platform"/>) when given, else the project's active platform, else its
    /// first listed platform, else the baseline platform. The result need not be listed or installed: the build engine checks
    /// that (E9012, E9016).
    /// </summary>
    public static string GetEffectivePlatform(Project project, string? requested = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        return requested ?? project.ActivePlatform ?? (project.Platforms.IsDefaultOrEmpty ? TargetRegistry.Default.Id : project.Platforms[0]);
    }

    /// <exception cref="ArgumentException">
    /// The project has no file path, the configuration does not exist, or the model holds a value the loader
    /// would have rejected (optimization not 0..2, appType not console/gui/library).
    /// </exception>
    /// <remarks>
    /// CMP-1368: for the registered target of the effective platform (<see cref="GetEffectivePlatform"/>), or for the baseline
    /// target when that platform is not installed.
    /// </remarks>
    public static CompilationOptions ToCompilationOptions(Project project, string configuration)
    {
        ArgumentNullException.ThrowIfNull(project);
        return ToCompilationOptions(
            project, configuration, TargetRegistry.TryGet(GetEffectivePlatform(project), out var installed) ? installed : TargetRegistry.Default);
    }

    /// <summary>
    /// As <see cref="ToCompilationOptions(Project, string)"/> for <paramref name="target"/> (CMP-1019 rule 3): the baseline target
    /// keeps <c>obj\&lt;config&gt;</c>, any other target gets <c>obj\&lt;target id&gt;\&lt;config&gt;</c> (CMP-1020). The lists of
    /// <c>platformOptions.&lt;target id&gt;</c> come after those of the project and of the configuration chain (CMP-1365).
    /// </summary>
    public static CompilationOptions ToCompilationOptions(Project project, string configuration, TargetInfo target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var config = GetConfiguration(project, configuration);
        var dir = RequireDirectory(project);
        var platform = project.PlatformOptions.GetValueOrDefault(target.Id);

        var searchPaths = new List<string>();
        void AddPath(string path)
        {
            if (!searchPaths.Contains(path, PathUtil.Comparer))
            {
                searchPaths.Add(path);
            }
        }

        foreach (var path in WithPlatformList(config.SearchPaths, platform?.SearchPaths))       // project level, the chain, the platform
        {
            AddPath(Resolve(dir, path));
        }

        foreach (var file in project.Files.Where(f => f.EffectiveKind == "unit"))
        {
            var full = Resolve(dir, file.Path);
            AddPath(full.LastIndexOf('\\') is var i && i == 2 ? full[..3] : full[..i]);
        }

        // CMP-1113: the object form of `manifest`; a library ignores the two settings (its custom file is embedded as a path)
        var settings = project.ManifestSettings;
        if (settings is not null && project.Manifest is not null)
        {
            throw new ArgumentException("A project holds either 'Manifest' or 'ManifestSettings', not both", nameof(project));
        }

        var generated = settings is not null && project.AppType != "library" ? settings : null;

        return new CompilationOptions
        {
            MainFile = Resolve(dir, project.MainSource),
            SearchPaths = [.. searchPaths],
            LibraryPaths = [.. WithPlatformList(project.LibraryPaths, platform?.LibraryPaths).Select(path => Resolve(dir, path))],   // CMP-1082
            // CMP-1314: the resource files in file order; a script gets Defines and SearchPaths
            ResourceFiles = [.. project.Files.Where(f => f.EffectiveKind == "resource").Select(f => Resolve(dir, f.Path))],
            Defines = WithPlatformList(config.Defines, platform?.Defines),                      // CMP-1160: project level, then the chain; CMP-1365
            Optimization = config.Optimization switch
            {
                0 => OptimizationLevel.O0,
                1 => OptimizationLevel.O1,
                2 => OptimizationLevel.O2,
                _ => throw new ArgumentException($"Invalid optimization level {config.Optimization}", nameof(project)),
            },
            DebugInfo = config.DebugInfo,
            AppType = project.AppType switch
            {
                "console" => AppType.Console,
                "gui" => AppType.Gui,
                "library" => AppType.Library,                                                   // CMP-1080
                _ => throw new ArgumentException($"Unsupported application type '{project.AppType}'", nameof(project)),
            },
            ObjectDirectory = PathUtil.Normalize(TargetRegistry.ObjectDirectory(dir, target, configuration)),
            Target = target,
            UnitScopeNames = project.UnitScopeNames,
            Rtti = config.Rtti,                                                                 // C-240, CMP-1160: chain, then project, then broad
            Manifest = settings is not null
                ? (settings.CustomFile is { } customFile ? Resolve(dir, customFile) : "default")
                : project.Manifest switch                                                       // C-240: absent → "" = default per appType
                {
                    null => "",
                    "default" or "none" => project.Manifest,
                    var path => Resolve(dir, path),
                },
            ManifestExecutionLevel = generated?.ExecutionLevel switch
            {
                null or "asInvoker" => ManifestExecutionLevel.AsInvoker,
                "highestAvailable" => ManifestExecutionLevel.HighestAvailable,
                "requireAdministrator" => ManifestExecutionLevel.RequireAdministrator,
                var level => throw new ArgumentException($"Invalid manifest execution level '{level}'", nameof(project)),
            },
            ManifestDpiAwareness = generated?.DpiAwareness switch
            {
                null => ManifestDpiAwareness.Default,
                "perMonitorV2" => ManifestDpiAwareness.PerMonitorV2,
                "system" => ManifestDpiAwareness.System,
                "unaware" => ManifestDpiAwareness.Unaware,
                var dpi => throw new ArgumentException($"Invalid manifest DPI awareness '{dpi}'", nameof(project)),
            },
        };
    }

    /// <summary>
    /// Output: <c>&lt;project dir&gt;/&lt;outputDir&gt;/&lt;name&gt;.exe</c>, or <c>&lt;name&gt;.dll</c> for <c>appType</c>
    /// <c>library</c> (project-format.md §1, CMP-1081), for the effective platform (<see cref="GetEffectivePlatform"/>, CMP-1368).
    /// </summary>
    public static string GetOutputPath(Project project, string configuration)
    {
        ArgumentNullException.ThrowIfNull(project);
        return GetOutputPath(project, configuration, GetEffectivePlatform(project));
    }

    /// <summary>The output file of <paramref name="configuration"/> on platform <paramref name="platformId"/>: in <see cref="GetOutputDirectory"/>.</summary>
    /// <exception cref="ArgumentException">The project has no file path or the configuration does not exist.</exception>
    public static string GetOutputPath(Project project, string configuration, string platformId) =>
        PathUtil.Normalize(
            GetOutputDirectory(project, configuration, platformId) + @"\" + project.Name + (project.AppType == "library" ? ".dll" : ".exe"));

    /// <summary>
    /// CMP-1366: the output directory of <paramref name="configuration"/> (exact name) on platform <paramref name="platformId"/>,
    /// absolute. (1) <c>platformOptions.&lt;id&gt;.outputDir</c> when present, with <c>$(Config)</c> and <c>$(Platform)</c>
    /// replaced; (2) else, for the baseline platform, the configuration's <c>outputDir</c> — also when the project lists more
    /// platforms (CMP-1367); (3) else the configuration's <c>outputDir</c> with the id inserted before its last segment when that
    /// segment is the configuration name (ignoring case: <c>bin/Debug</c> → <c>bin/win32/Debug</c>), otherwise appended
    /// (<c>build</c> → <c>build/win32</c>). The platform need not be listed or installed.
    /// </summary>
    /// <exception cref="ArgumentException">The project has no file path or the configuration does not exist.</exception>
    public static string GetOutputDirectory(Project project, string configuration, string platformId)
    {
        ArgumentNullException.ThrowIfNull(platformId);
        var config = GetConfiguration(project, configuration);
        var directory = RequireDirectory(project);
        if (project.PlatformOptions.GetValueOrDefault(platformId)?.OutputDir is { } own)
        {
            var macros = new Dictionary<string, string> { [OutputDirMacros[0]] = configuration, [OutputDirMacros[1]] = platformId };
            return Resolve(directory, EventMacros.Expand(own, macros).Text);
        }

        if (platformId == TargetRegistry.Default.Id)
        {
            return Resolve(directory, config.OutputDir);
        }

        var shared = config.OutputDir.Replace('\\', '/').TrimEnd('/');
        var lastSegment = shared.LastIndexOf('/') + 1;
        return Resolve(directory, string.Equals(shared[lastSegment..], configuration, StringComparison.OrdinalIgnoreCase)
            ? shared[..lastSegment] + platformId + "/" + shared[lastSegment..]
            : shared.Length == 0 ? platformId : shared + "/" + platformId);
    }

    /// <summary>
    /// CMP-1366: whether <paramref name="outputDir"/> is a valid <c>platformOptions.&lt;id&gt;.outputDir</c> — every <c>$(</c> in it
    /// starts a reference to <c>Config</c> or <c>Platform</c> (names ignore case, as in build events) or is the escape <c>$$(</c>.
    /// </summary>
    internal static bool IsPlatformOutputDir(string outputDir)
    {
        for (var i = 0; i < outputDir.Length; i++)
        {
            if (outputDir[i] != '$')
            {
                continue;
            }

            if (string.CompareOrdinal(outputDir, i, "$$(", 0, 3) == 0)
            {
                i += 2;
            }
            else if (i + 1 < outputDir.Length && outputDir[i + 1] == '(')
            {
                var name = OutputDirMacros.FirstOrDefault(
                    macro => string.Compare(outputDir, i + 2, macro + ")", 0, macro.Length + 1, StringComparison.OrdinalIgnoreCase) == 0);
                if (name is null)
                {
                    return false;
                }

                i += name.Length + 2;
            }
        }

        return true;
    }

    /// <summary>
    /// CMP-1083: the executable target <c>Run</c> starts for a library project (<c>run.hostApplication</c>, relative to the
    /// project directory or absolute); null when none is set.
    /// </summary>
    public static string? GetHostApplicationPath(Project project) =>
        project.Run.HostApplication.Length == 0 ? null : Resolve(RequireDirectory(project), project.Run.HostApplication);

    /// <summary>
    /// The events <paramref name="configuration"/> (exact name) is built with (build-events.md CMP-1252): walking from the
    /// configuration itself to its root ancestor, the first one that has an <c>events</c> key supplies the whole value — lists and
    /// <c>postBuildWhen</c>; when none has one, the project-level events. Nothing is merged.
    /// </summary>
    /// <exception cref="ArgumentException">The name is unknown, a parent is missing, or the <c>inherits</c> links form a cycle.</exception>
    public static ProjectEvents GetEffectiveEvents(Project project, string configuration)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(configuration);
        return ConfigurationResolver.ChainOf(project, configuration).Reverse()
            .Select(name => project.Configurations[name].Events)
            .FirstOrDefault(events => events is not null) ?? project.Events;
    }

    /// <summary>
    /// CMP-1365: <paramref name="values"/> (project level, then the configuration chain) followed by the platform's own list; a
    /// leading <c>"!"</c> of that list discards <paramref name="values"/>.
    /// </summary>
    private static ImmutableArray<string> WithPlatformList(ImmutableArray<string> values, ImmutableArray<string>? platformList)
    {
        if (platformList is not { IsDefaultOrEmpty: false } own)
        {
            return values;
        }

        var all = new List<string>(values.IsDefault ? [] : values);
        ConfigurationResolver.Append(all, own);
        return [.. all];
    }

    private static EffectiveConfiguration GetConfiguration(Project project, string configuration)
    {
        ArgumentNullException.ThrowIfNull(project);
        return ConfigurationResolver.Resolve(project, configuration);
    }

    private static string RequireDirectory(Project project) =>
        project.Directory.Length > 0 ? project.Directory : throw new ArgumentException("The project has no file path", nameof(project));

    /// <summary>A project path ('/' or '\', relative to the project directory or absolute) → normalised absolute path.</summary>
    private static string Resolve(string directory, string path)
    {
        var p = path.Replace('/', '\\');
        var absolute = p.StartsWith(@"\\", StringComparison.Ordinal) || (p.Length >= 3 && p[1] == ':' && p[2] == '\\');
        return PathUtil.Normalize(absolute ? p : directory + @"\" + p);
    }
}
