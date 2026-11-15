using FreexComp.Compiler;
using FreexComp.Compiler.Text;

namespace FreexComp.Build;

/// <summary>Maps a project configuration to compiler options (CMP-812).</summary>
public static class ProjectOptions
{
    /// <exception cref="ArgumentException">
    /// The project has no file path, the configuration does not exist, or the model holds a value the loader
    /// would have rejected (optimization not 0..2, appType not console/gui).
    /// </exception>
    public static CompilationOptions ToCompilationOptions(Project project, string configuration)
    {
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

        foreach (var path in project.SearchPaths)
        {
            AddPath(Resolve(dir, path));
        }

        foreach (var file in project.Files.Where(f => f.EffectiveKind == "unit"))
        {
            var full = Resolve(dir, file.Path);
            AddPath(full.LastIndexOf('\\') is var i && i == 2 ? full[..3] : full[..i]);
        }

        return new CompilationOptions
        {
            MainFile = Resolve(dir, project.MainSource),
            SearchPaths = [.. searchPaths],
            Defines = [.. project.Defines, .. config.Defines],
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
                _ => throw new ArgumentException($"Unsupported application type '{project.AppType}'", nameof(project)),
            },
            ObjectDirectory = PathUtil.Normalize(dir + @"\obj\" + configuration),
            UnitScopeNames = project.UnitScopeNames,
            Rtti = config.Rtti ?? project.Rtti ?? "broad",                                      // C-240: configuration wins over project
            Manifest = project.Manifest switch                                                  // C-240: absent → "" = default per appType
            {
                null => "",
                "default" or "none" => project.Manifest,
                var path => Resolve(dir, path),
            },
        };
    }

    /// <summary>Output exe: <c>&lt;project dir&gt;/&lt;outputDir&gt;/&lt;name&gt;.exe</c> (project-format.md §1).</summary>
    public static string GetOutputPath(Project project, string configuration)
    {
        var config = GetConfiguration(project, configuration);
        return PathUtil.Normalize(Resolve(RequireDirectory(project), config.OutputDir) + @"\" + project.Name + ".exe");
    }

    private static ProjectConfiguration GetConfiguration(Project project, string configuration) =>
        project.Configurations.TryGetValue(configuration, out var config)
            ? config
            : throw new ArgumentException($"Unknown configuration '{configuration}'", nameof(configuration));

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
