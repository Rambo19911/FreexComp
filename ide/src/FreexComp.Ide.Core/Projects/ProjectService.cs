using System.Globalization;
using FreexComp.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ViewModels;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Projects;

/// <inheritdoc />
public sealed class ProjectService : IProjectService
{
    private const string LogCategory = "project";

    private readonly IdeFileSystem _fileSystem;
    private readonly IDocumentService _documents;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly ILog _log;
    private readonly FilePrompts _prompts;
    private readonly HashSet<string> _members = new(StringComparer.OrdinalIgnoreCase);
    private ICommandService? _commands;
    private Project? _project;

    public ProjectService(IdeFileSystem fileSystem, IDocumentService documents, IDialogService dialogs, IStatusService status,
        ILog log, FilePrompts prompts)
    {
        _fileSystem = fileSystem;
        _documents = documents;
        _dialogs = dialogs;
        _status = status;
        _log = log;
        _prompts = prompts;
    }

    public ProjectModel? Project { get; private set; }

    public event EventHandler? ProjectChanged;

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        commands.RegisterHandler("file.openProject", new Handler(_ => true, _ => PickAndOpenAsync()));
        commands.RegisterHandler("file.closeProject", new Handler(_ => Project is not null, _ => CloseAsync()));
        commands.RegisterHandler("project.viewSource", new Handler(_ => Project is not null, _ => ViewSourceAsync()));
    }

    public async Task<bool> OpenAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var fullPath = Path.GetFullPath(path);
        if (_project is not null && string.Equals(_project.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
            return true;

        // Steps 1-2 off the UI thread (IDE-ARCH-081) with the status "Opening <name>…".
        ProjectLoadResult load;
        using (_status.ShowStickyMessage(string.Format(CultureInfo.CurrentCulture, Strings.Status_OpeningProject, Path.GetFileNameWithoutExtension(fullPath))))
        {
            load = await Task.Run(() => ProjectFileAccess.Load(fullPath, _fileSystem));
        }

        var error = load.Project is { } loaded ? Validate(loaded) : load.Error;
        if (!error.Success)
        {
            if (await _dialogs.ShowMessageAsync(new MessageRequest(error.MessageId!, error.Args)) == "OpenAsText")
                await OpenDocumentAsync(fullPath);
            return false;
        }

        if (!await CloseAsync()) // IDE-PM-010: canceled → nothing changes
            return false;

        var project = load.Project!;
        if (!project.Configurations.ContainsKey(project.ActiveConfiguration))
            project = project with { ActiveConfiguration = project.ConfigurationNames[0] }; // IDE-PM-012: in memory
        SetProject(project);

        // Step 4: the session (I-073) restores documents; without one the main source opens. A missing main source is
        // reported by the missing-files check (step 7, UI-TW-107), not by a message box.
        var main = await _documents.OpenAsync(Resolve(project.MainSource));
        if (!main.Success)
            _log.Warning(LogCategory, $"{project.FilePath}: main source not opened ({main.MessageId})");
        return true;
    }

    public async Task<bool> CloseAsync()
    {
        if (_project is null)
            return true;

        // UI-DLG-063: the project file first, then the member documents in tab order.
        var members = _documents.Documents.Where(d => IsMember(d.FilePath)).ToList();
        var ordered = members.Where(IsProjectFile).Concat(members.Where(d => !IsProjectFile(d))).ToList();
        if (!await _documents.PromptSaveChangesAsync(ordered))
            return false;

        // The session write (I-073), Messages/Structure clearing (P3) and the file watcher (I-072) join here.
        foreach (var document in ordered)
        {
            _documents.Close(document);
        }

        SetProject(null);
        return true;
    }

    public bool IsMember(string? path) => path is not null && _members.Contains(Path.GetFullPath(path));

    public async Task<bool> ViewSourceAsync() =>
        _project is not null && await OpenDocumentAsync(Resolve(_project.MainSource));

    private async Task PickAndOpenAsync()
    {
        if (await _prompts.OpenProjectAsync() is { } path)
            await OpenAsync(path);
    }

    /// <summary>IDE-PM-012 beyond the serializer's checks.</summary>
    private static OperationResult Validate(Project project)
    {
        if (!IsIdentifier(project.Name))
            return OperationResult.Fail("Msg.Project.OpenFailed", project.FilePath, Strings.Msg_Project_NameInvalid);
        if (project.Configurations.IsEmpty)
            return OperationResult.Fail("Msg.Project.OpenFailed", project.FilePath, Strings.Reason_NoConfigurations);
        return OperationResult.Ok;
    }

    /// <summary><c>^[A-Za-z_][A-Za-z0-9_]*$</c> (IDE-PM-021's identifier rule).</summary>
    private static bool IsIdentifier(string name) =>
        name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private void SetProject(Project? project)
    {
        _project = project;
        _members.Clear();
        Project = project is null ? null : BuildModel(project);
        if (project is not null)
        {
            _members.Add(project.FilePath);
            foreach (var file in Project!.Files)
            {
                _members.Add(Resolve(file.Path));
                if (file.Form is { } form)
                    _members.Add(Resolve(form));
            }
        }

        ProjectChanged?.Invoke(this, EventArgs.Empty);
        _commands?.InvalidateCanExecute();
    }

    /// <summary>IDE-PM-012: the main source is listed first when missing; later duplicates are ignored with a warning.</summary>
    private ProjectModel BuildModel(Project project)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<ProjectFileItem>();
        if (!project.Files.Any(f => string.Equals(Resolve(f.Path), Resolve(project.MainSource), StringComparison.OrdinalIgnoreCase)))
            project = project with { Files = project.Files.Insert(0, new ProjectFile(project.MainSource, null, null)) };

        foreach (var file in project.Files)
        {
            if (!seen.Add(Resolve(file.Path)))
            {
                _log.Warning(LogCategory, $"{project.FilePath}: duplicate file entry '{file.Path}' ignored");
                continue;
            }

            files.Add(new ProjectFileItem(file.Path, file.Form, file.EffectiveKind));
        }

        return new ProjectModel(project.FilePath, project.Directory, project.Name, project.Language, project.AppType,
            project.MainSource, files, project.ConfigurationNames, project.ActiveConfiguration, project.Platforms,
            isModified: false);
    }

    /// <summary>A project path ('/', relative to the project directory or absolute) as an absolute path.</summary>
    private string Resolve(string projectPath) =>
        Path.GetFullPath(Path.Combine(_project!.Directory, projectPath.Replace('/', Path.DirectorySeparatorChar)));

    private bool IsProjectFile(DocumentViewModel document) =>
        string.Equals(document.FilePath, _project?.FilePath, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> OpenDocumentAsync(string path)
    {
        var result = await _documents.OpenAsync(path);
        if (!result.Success)
            await _dialogs.ShowMessageAsync(new MessageRequest(result.MessageId!, result.Args));
        return result.Success;
    }

    private sealed class Handler(Func<string?, bool> canExecute, Func<string?, Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument) => execute(argument);
    }
}
