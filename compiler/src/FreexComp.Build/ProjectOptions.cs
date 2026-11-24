using FreexComp.Compiler;
using FreexComp.Compiler.Targets;
using FreexComp.Compiler.Text;

namespace FreexComp.Build;

/// <summary>
/// Maps a project configuration to compiler options (CMP-812). Every per-configuration value is the effective one of
/// <see cref="ConfigurationResolver.Resolve"/> (configurations.md CMP-1161).
/// </summary>
public static class ProjectOptions
{
    /// <exception cref="ArgumentException">
    /// The project has no file path, the configuration does not exist, or the model holds a value the loader
    /// would have rejected (optimization not 0..2, appType not console/gui/library).
    /// </exception>
    public static CompilationOptions ToCompilationOptions(Project project, string configuration) =>
        ToCompilationOptions(project, configuration, TargetRegistry.Default);

    /// <summary>
    /// As <see cref="ToCompilationOptions(Project, string)"/> for <paramref name="target"/> (CMP-1019 rule 3): the baseline target
    /// keeps <c>obj\&lt;config&gt;</c>, any other target gets <c>obj\&lt;target id&gt;\&lt;config&gt;</c> (CMP-1020).
    /// </summary>
    public static CompilationOptions ToCompilationOptions(Project project, string configuration, TargetInfo target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var config = GetConfiguration(project, configuration);
        var dir = RequireDirectory(project);

        var searchPaths = new List<string>();
        void AddPath(string path)
        {
            if (!searchPaths.Contains(path, PathUtil.Comparer))
            {
                searchPaths.Add(path);
            }
        }

        foreach (var path in config.SearchPaths)                                                // project level, then the chain
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
            LibraryPaths = [.. project.LibraryPaths.Select(path => Resolve(dir, path))],          // CMP-1082
            Defines = config.Defines,                                                           // CMP-1160: project level, then the chain
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
    /// <c>library</c> (project-format.md §1, CMP-1081).
    /// </summary>
    public static string GetOutputPath(Project project, string configuration)
    {
        var config = GetConfiguration(project, configuration);
        return PathUtil.Normalize(
            Resolve(RequireDirectory(project), config.OutputDir) + @"\" + project.Name + (project.AppType == "library" ? ".dll" : ".exe"));
    }

    /// <summary>
    /// CMP-1083: the executable target <c>Run</c> starts for a library project (<c>run.hostApplication</c>, relative to the
    /// project directory or absolute); null when none is set.
    /// </summary>
    public static string? GetHostApplicationPath(Project project) =>
        project.Run.HostApplication.Length == 0 ? null : Resolve(RequireDirectory(project), project.Run.HostApplication);

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
