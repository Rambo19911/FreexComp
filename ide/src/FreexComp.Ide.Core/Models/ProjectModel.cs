namespace FreexComp.Ide.Core.Models;

/// <summary>One entry of <see cref="ProjectModel.Files"/>; <paramref name="Kind"/>: <c>unit</c> | <c>form</c> | <c>other</c>.</summary>
public sealed record ProjectFileItem(string Path, string? Form, string Kind);

/// <summary>
/// Read-only projection of the open project's <c>FreexComp.Build.Project</c> (project-manager.md §1, IDE-PM-001),
/// rebuilt after each change by the project service. Paths in <see cref="Files"/> are as written (relative, '/').
/// </summary>
public sealed class ProjectModel
{
    public ProjectModel(string filePath, string directory, string name, string language, string appType, string mainSource,
        IReadOnlyList<ProjectFileItem> files, IReadOnlyList<string> configurations, string activeConfiguration,
        IReadOnlyList<string> platforms, bool isModified)
    {
        FilePath = filePath;
        Directory = directory;
        Name = name;
        Language = language;
        AppType = appType;
        MainSource = mainSource;
        Files = files;
        Configurations = configurations;
        ActiveConfiguration = activeConfiguration;
        Platforms = platforms;
        IsModified = isModified;
    }

    /// <summary>Absolute path of the <c>.pproj</c>.</summary>
    public string FilePath { get; }

    public string Directory { get; }

    public string Name { get; }

    public string Language { get; }

    public string AppType { get; }

    /// <summary>Relative, '/'.</summary>
    public string MainSource { get; }

    /// <summary>The main source first when the file does not list it; later duplicates left out (IDE-PM-012).</summary>
    public IReadOnlyList<ProjectFileItem> Files { get; }

    /// <summary>Names in file order.</summary>
    public IReadOnlyList<string> Configurations { get; }

    public string ActiveConfiguration { get; }

    public IReadOnlyList<string> Platforms { get; }

    /// <summary>The in-memory state differs from the disk (IDE-PM-004).</summary>
    public bool IsModified { get; }
}
