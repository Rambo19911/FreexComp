using System.Collections.Immutable;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.CodeModel;

/// <summary>
/// The project <c>Compilation</c> (I-110, IDE-ARCH-087): a language-service <see cref="Workspace"/> over the IDE's file
/// system with the open project's options (active configuration, toolchain RTL, no unit cache) and every open Pascal
/// buffer as a <c>SourceText</c> override. The overrides are taken on the UI thread when a request is made; the work
/// runs on the <see cref="CodeModelWorker"/>. Nothing is ever written.
/// </summary>
public sealed class CodeModelService
{
    private readonly IProjectService _projects;
    private readonly IDocumentService _documents;
    private readonly IToolchainLocator _toolchain;
    private readonly Platform.IFileSystem _fileSystem;
    private readonly Dictionary<string, long> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private (Project? Definition, FreexComp.Compiler.Targets.TargetInfo? Target, string? Rtl, string? Framework, string? Data) _projectKey;

    public CodeModelService(IProjectService projects, IDocumentService documents, IToolchainLocator toolchain,
        Platform.IFileSystem fileSystem)
    {
        _projects = projects;
        _documents = documents;
        _toolchain = toolchain;
        _fileSystem = fileSystem;
        Workspace = new Workspace(new CompilerFileSystemAdapter(fileSystem));
    }

    public Workspace Workspace { get; }

    public CodeModelWorker Worker { get; } = new();

    /// <summary>
    /// The current compilation after bringing the project options and the open buffers up to date (UI thread).
    /// Null without a project.
    /// </summary>
    public Compilation? Synchronize()
    {
        if (_projects.Definition is not { } definition || _projects.Project is not { } project)
            return null;
        var paths = _toolchain.Resolve();
        // IDE-PLAT-006: the key holds the definition (its active platform included) and the descriptor of that platform.
        var target = Target(project.ActivePlatform);
        var key = (definition, target, paths.RtlDir, paths.FrameworkDir, paths.DataDir);
        if (key != _projectKey)
        {
            _projectKey = key;
            Workspace.SetProject(Options(definition, project.ActiveConfiguration, target, paths));
        }

        var open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in _documents.Documents.OfType<TextDocumentViewModel>())
        {
            if (document.FilePath is not { } path || !IsPascal(path))
                continue;
            open.Add(path);
            var version = document.Buffer.Version;
            if (_overrides.TryGetValue(path, out var known) && known == version)
                continue;
            if (_overrides.ContainsKey(path))
                Workspace.UpdateDocument(path, document.Buffer.Text);
            else
                Workspace.OpenDocument(path, document.Buffer.Text);
            _overrides[path] = version;
        }

        foreach (var closed in _overrides.Keys.Where(p => !open.Contains(p)).ToList())
        {
            Workspace.CloseDocument(closed);
            _overrides.Remove(closed);
        }

        return Workspace.CurrentCompilation;
    }

    /// <summary>A file of the open project (not a loose file, IDE-ED-094).</summary>
    public bool IsProjectFile(string path) => _projects.IsMember(path);

    /// <summary>IDE-P3-055: the whole program's diagnostics (parse + bind every unit) with the unsaved buffers; empty without a project.</summary>
    public Task<ImmutableArray<Diagnostic>> GetProjectDiagnosticsAsync(CancellationToken ct)
    {
        if (Synchronize() is not { } compilation)
            return Task.FromResult(ImmutableArray<Diagnostic>.Empty);
        return Worker.RunAsync("projectDiagnostics", token => compilation.GetDiagnostics(null, token), ct);
    }

    /// <summary>
    /// I-225: the canonical names of every unit of the program (the resolved unit list, library units included); empty
    /// without a project or when the front end could not start.
    /// </summary>
    public Task<ImmutableArray<string>> GetProjectUnitNamesAsync(CancellationToken ct)
    {
        if (Synchronize() is not { } compilation)
            return Task.FromResult(ImmutableArray<string>.Empty);
        return Worker.RunAsync("projectUnits", _ => compilation.Units.Select(u => u.Name).ToImmutableArray(), ct);
    }

    /// <summary>
    /// I-280: the canonical names of every unit of the program of <paramref name="projectPath"/> in
    /// <paramref name="configuration"/> — the loaded project with the open buffers (as <see cref="GetProjectUnitNamesAsync(CancellationToken)"/>),
    /// any other project (a listed member of the group) as saved on disk, with its own active platform. Empty when the
    /// project file cannot be read or the front end could not start.
    /// </summary>
    public Task<ImmutableArray<string>> GetProjectUnitNamesAsync(string projectPath, string configuration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        if (string.Equals(projectPath, _projects.Project?.FilePath, StringComparison.OrdinalIgnoreCase))
            return GetProjectUnitNamesAsync(ct);
        if (ProjectFileAccess.Load(projectPath, _fileSystem).Project is not { } definition)
            return Task.FromResult(ImmutableArray<string>.Empty);
        var options = Options(definition, configuration, Target(ProjectOptions.GetEffectivePlatform(definition)), _toolchain.Resolve());
        return Worker.RunAsync("memberUnits", _ =>
        {
            var workspace = new Workspace(new CompilerFileSystemAdapter(_fileSystem));
            workspace.SetProject(options);
            return workspace.CurrentCompilation.Units.Select(u => u.Name).ToImmutableArray();
        }, ct);
    }

    /// <summary>IDE-PLAT-007: a platform that is not installed is analysed with the baseline descriptor and its own lists.</summary>
    private FreexComp.Compiler.Targets.TargetInfo Target(string platform) =>
        _projects.PlatformCatalog.Find(platform)?.Target ?? BuiltInPlatformCatalog.Baseline with { Id = platform };

    /// <summary>The compilation options of <paramref name="definition"/>: the toolchain RTL, the library sources, no unit cache.</summary>
    private static CompilationOptions Options(Project definition, string configuration, FreexComp.Compiler.Targets.TargetInfo target, ToolchainPaths paths)
    {
        var options = ProjectOptions.ToCompilationOptions(definition, configuration, target) with
        {
            RtlDirectory = paths.RtlDir ?? "",
            UsePcuCache = false, // Check Syntax reads sources only and writes nothing (IDE-P3-055)
        };
        foreach (var library in new[] { paths.FrameworkDir, paths.DataDir }.OfType<string>()) // as BuildRequest.ExtraSearchPaths
            options = options with { SearchPaths = options.SearchPaths.Add(Path.Combine(library, "src")) };
        return options;
    }

    private static bool IsPascal(string path) =>
        Path.GetExtension(path) is var ext && (ext.Equals(".pas", StringComparison.OrdinalIgnoreCase) || ext.Equals(".inc", StringComparison.OrdinalIgnoreCase));
}
