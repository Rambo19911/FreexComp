using System.Collections.Immutable;
using System.Globalization;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using IClock = FreexComp.Ide.Core.Platform.IClock;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;
using IShellService = FreexComp.Ide.Core.Platform.IShellService;
using FreexComp.Ide.Core.Layout;

namespace FreexComp.Ide.Core.Projects;

/// <inheritdoc />
public sealed class ProjectService : IProjectService
{
    private const string LogCategory = "project";
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly IdeFileSystem _fileSystem;
    private readonly IDocumentService _documents;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly ILog _log;
    private readonly FilePrompts _prompts;
    private readonly ISettingsService _settings;
    private readonly ILayoutService _layout;
    private readonly IClock _clock;
    private readonly IShellService _shell;
    private readonly HashSet<string> _members = new(StringComparer.OrdinalIgnoreCase);
    private ICommandService? _commands;
    private Project? _project;
    private DiskStamp? _projectStamp; // the project file as loaded or last written by this service (IDE-P1-117)
    private bool _checkingProjectFile;

    public ProjectService(IdeFileSystem fileSystem, IDocumentService documents, IDialogService dialogs, IStatusService status,
        ILog log, FilePrompts prompts, ISettingsService settings, IReadOnlyList<ProjectTemplate> templates, ILayoutService layout,
        IClock clock, IShellService shell)
    {
        _shell = shell;
        _layout = layout;
        _clock = clock;
        _fileSystem = fileSystem;
        _documents = documents;
        _dialogs = dialogs;
        _status = status;
        _log = log;
        _prompts = prompts;
        _settings = settings;
        Templates = templates;
    }

    public ProjectModel? Project { get; private set; }

    public Project? Definition => _project;

    /// <summary>Restores the session at open step 4 and writes it on close (IDE-PM-011, -014; set by the service graph).</summary>
    public ISessionService? Session { get; set; }

    /// <summary>Records each opened project (IDE-PM-011 step 5; set by the service graph).</summary>
    public IRecentItemsService? RecentItems { get; set; }

    public IReadOnlyList<ProjectTemplate> Templates { get; }

    /// <summary>Watches the open project's file (IDE-P1-117; set by the service graph).</summary>
    public IFileChangeService? FileChanges { get; set; }

    /// <summary>IDE-P1-086: marks the service's own writes of the project file so the watcher ignores them (set by the service graph).</summary>
    public Func<string, IDisposable>? WriteGuard { get; set; }

    /// <summary>The project group around the loaded project (IDE-PM-151; set by <see cref="ProjectGroupService"/>).</summary>
    internal ProjectGroupService? Group { get; set; }

    /// <summary>
    /// IDE-PM-163: true while the selection of the focused Project Manager is in a listed member of the group — the
    /// commands that change the open project's file list are disabled there (set by the service graph).
    /// </summary>
    public Func<bool>? ListedMemberSelected { get; set; }

    private bool OnOpenProject => Project is not null && ListedMemberSelected?.Invoke() != true;

    public event EventHandler? ProjectChanged;

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        commands.RegisterHandler("file.newProject", new Handler(_ => Templates.Count > 0, _ => NewProjectAsync()));
        commands.RegisterHandler("file.openProject", new Handler(_ => true, _ => PickAndOpenAsync()));
        // IDE-PM-158: the whole group; a saved group without an available member is open without a project.
        commands.RegisterHandler("file.closeProject", new Handler(_ => Project is not null || Group?.IsOpen == true,
            _ => Group is { } group ? group.CloseAsync() : CloseAsync()));
        commands.RegisterHandler("project.viewSource", new Handler(_ => Project is not null, _ => ViewSourceAsync()));
        commands.RegisterHandler("project.addNewUnit", new Handler(_ => OnOpenProject, _ => AddNewUnitAsync()));
        commands.RegisterHandler("project.addNewForm", new Handler(_ => OnOpenProject, _ => AddNewFormAsync()));
        commands.RegisterHandler("file.newForm", new Handler(_ => Project is not null, _ => AddNewFormAsync())); // menus.md: needs a project
        commands.RegisterHandler("project.addNewFrame", new Handler(_ => OnOpenProject, _ => AddNewFrameAsync())); // DSG-CS-115
        commands.RegisterHandler("file.newFrame", new Handler(_ => Project is not null, _ => AddNewFrameAsync()));
        commands.RegisterHandler("file.newFile", new Handler(_ => true, _ => NewFileAsync()));
        commands.RegisterHandler("project.addExistingFile", new Handler(_ => OnOpenProject, _ => PickAndAddExistingAsync()));
        commands.RegisterHandler("project.addNewFile", new Handler(_ => OnOpenProject, _ => AddNewFileAsync()));
        // IDE-PM-115: on a folder row without project files the command removes the (empty) folder.
        commands.RegisterHandler("project.removeFile", new Handler(
            _ => PendingRename is null && (RemovableFolder?.Invoke() is not null || CanOperateOnFiles()),
            _ => RemovableFolder?.Invoke() is { } removeFolder ? removeFolder() : RemoveFilesAsync(FileTargets())));
        commands.RegisterHandler("project.deleteFile", new Handler(_ => PendingRename is null && CanOperateOnFiles(), _ => DeleteFilesAsync(FileTargets())));
        commands.RegisterHandler("project.renameFile", new Handler(_ => RenameTarget() is not null, _ => RenameCommandAsync()));
        commands.RegisterHandler("project.setConfiguration", new Handler(_ => Project is not null, SetActiveConfigurationAsync));
        commands.RegisterHandler("project.setPlatform", new Handler(_ => Project is not null, SetActivePlatformAsync));
        // menus.md: enabled while a project is open and the active configuration's output folder exists (I-191).
        commands.RegisterHandler("project.openOutputFolder", new Handler(
            _ => OutputFolder() is { } dir && _fileSystem.DirectoryExists(dir),
            _ =>
            {
                if (OutputFolder() is { } dir && _fileSystem.DirectoryExists(dir) && !_shell.OpenFolder(dir))
                    _log.Warning(LogCategory, $"The shell could not open '{dir}'.");
                return Task.CompletedTask;
            }));
    }

    /// <summary>The installed platforms (IDE-PLAT-001); the composition passes the product's or a test's catalogue.</summary>
    public IPlatformCatalog PlatformCatalog { get; init; } = BuiltInPlatformCatalog.Instance;

    /// <summary>The output directory of the active configuration on the active platform (IDE-PLAT-005), or null without a project.</summary>
    public string? OutputFolder()
    {
        if (_project is null || Project is null || ConfigurationEdits.Effective(_project, _project.ActiveConfiguration) is null)
            return null;
        return Path.GetFullPath(ProjectOptions.GetOutputDirectory(_project, _project.ActiveConfiguration, Project.ActivePlatform));
    }

    /// <summary>
    /// IDE-PLAT-003 <c>project.setPlatform:&lt;id&gt;</c>: sets <c>activePlatform</c> through the mutation path of
    /// <c>project.setConfiguration</c> (IDE-PM-004). A platform that is not listed is a no-op with a log warning; a listed
    /// one that is not installed may be made active. True when changed and saved.
    /// </summary>
    public async Task<bool> SetActivePlatformAsync(string? id)
    {
        if (_project is null || id is null || id == ProjectOptions.GetEffectivePlatform(_project))
            return false;
        if (!_project.Platforms.Contains(id))
        {
            _log.Warning(LogCategory, $"Platform '{id}' is not listed in the project.");
            return false;
        }

        return await SaveChangedProjectAsync(_project with { ActivePlatform = id });
    }

    public async Task<bool> AddPlatformAsync(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_project is null || _project.Platforms.Contains(id) || PlatformCatalog.Find(id) is null)
            return false;
        return await SaveChangedProjectAsync(_project with { Platforms = _project.Platforms.Add(id) });
    }

    public async Task<bool> RemovePlatformAsync(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_project is null || _project.Platforms.Length <= 1 || !_project.Platforms.Contains(id))
            return false;
        // I-298: the question names the platform without the " (not installed)" suffix, which would stand mid-sentence.
        if (_project.PlatformOptions.ContainsKey(id)
            && await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Platform.RemoveConfirm", [PlatformLabels.Short(id)])) != "Remove")
        {
            return false;
        }

        // The model keeps an explicit active platform listed: the first remaining one takes its place.
        var platforms = _project.Platforms.Remove(id);
        return await SaveChangedProjectAsync(_project with
        {
            Platforms = platforms,
            PlatformOptions = _project.PlatformOptions.Remove(id),
            ActivePlatform = _project.ActivePlatform == id ? platforms[0] : _project.ActivePlatform,
        });
    }

    /// <summary>
    /// IDE-PM-090 <c>project.setConfiguration:&lt;name&gt;</c>: sets <c>activeConfiguration</c> and saves the project
    /// (IDE-PM-004). An unknown name is a no-op with a log warning. True when changed and saved.
    /// </summary>
    public async Task<bool> SetActiveConfigurationAsync(string? name)
    {
        if (_project is null || name is null || name == _project.ActiveConfiguration)
            return false;
        if (!_project.Configurations.ContainsKey(name))
        {
            _log.Warning(LogCategory, $"Unknown configuration '{name}'.");
            return false;
        }

        return await SaveChangedProjectAsync(_project with { ActiveConfiguration = name });
    }

    public async Task<bool> ApplyOptionsAsync(Project workingCopy, Func<string, Task>? saveFailed = null)
    {
        ArgumentNullException.ThrowIfNull(workingCopy);
        if (_project is null)
            return false;
        // The file path is the open project's; the active configuration falls back to the first one (UI-DLG-084).
        var copy = workingCopy with { FilePath = _project.FilePath };
        if (!copy.Configurations.ContainsKey(copy.ActiveConfiguration) && copy.ConfigurationNames.Length > 0)
            copy = copy with { ActiveConfiguration = copy.ConfigurationNames[0] };
        if (copy == _project)
            return true;
        if (saveFailed is null)
            return await SaveChangedProjectAsync(copy);
        var saved = SaveProjectFile(copy);
        if (!saved.Success)
        {
            await saveFailed(saved.Args[^1]);
            return false;
        }

        SetProject(copy);
        return true;
    }

    /// <summary><c>project.addExistingFile</c> (IDE-PM-050): the multi-select picker, then <see cref="AddExistingFilesAsync"/>.</summary>
    private async Task PickAndAddExistingAsync()
    {
        var picked = await _prompts.AddExistingAsync();
        if (picked.Count > 0)
            await AddExistingFilesAsync(picked);
    }

    public Task<bool> OpenAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        // IDE-PM-154: every way of opening a project also opens a project group.
        return Group is { } group && Path.GetExtension(path).Equals(".pgroup", StringComparison.OrdinalIgnoreCase)
            ? group.OpenAsync(path)
            : OpenMemberAsync(path, recordRecent: true);
    }

    /// <summary>
    /// <see cref="OpenAsync"/> for a <c>.pproj</c>; without <paramref name="recordRecent"/> for the member of a saved
    /// group, whose group file is the recent entry (IDE-PM-157).
    /// </summary>
    internal async Task<bool> OpenMemberAsync(string path, bool recordRecent)
    {
        var fullPath = Path.GetFullPath(path);
        if (_project is not null && string.Equals(_project.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
            return true;

        // Steps 1-2 off the UI thread (IDE-ARCH-081) with the status "Opening <name>…".
        ProjectLoadResult load;
        DiskStamp? stamp;
        using (_status.ShowStickyMessage(string.Format(CultureInfo.CurrentCulture, Strings.Status_OpeningProject, Path.GetFileNameWithoutExtension(fullPath))))
        {
            (load, stamp) = await Task.Run(() => (ProjectFileAccess.Load(fullPath, _fileSystem), ReadStamp(fullPath)));
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
        _projectStamp = stamp;
        SetProject(project);

        // Step 4: the session restores documents (if environment.restoreOpenFiles); without any the main source opens. A
        // missing main source is reported by the missing-files check (step 7, UI-TW-107), not by a message box.
        var restored = Session is null ? 0 : await Session.RestoreAsync(_settings.Get(SettingsCatalog.EnvironmentRestoreOpenFiles));
        if (restored == 0)
        {
            var main = await _documents.OpenAsync(Resolve(project.MainSource));
            if (!main.Success)
                _log.Warning(LogCategory, $"{project.FilePath}: main source not opened ({main.MessageId})");
        }

        if (recordRecent)
        {
            RecentItems?.Add(RecentKind.Project, fullPath); // step 5 (IDE-P1-130)
            RecentItems?.SetOpenAtExit(fullPath, true); // IDE-P1-016: until the user closes it
        }

        return true;
    }

    /// <summary>Asked first when the project closes (IDE-P3-093: stop a running program); false cancels the close.</summary>
    public Func<Task<bool>>? BeforeClose { get; set; }

    public async Task<bool> CloseAsync()
    {
        if (_project is null)
            return true;
        if (Group is { } group && !await group.ConfirmCloseAsync()) // IDE-PM-156: the unsaved group comes first
            return false;
        if (BeforeClose is { } beforeClose && !await beforeClose())
            return false;

        // UI-DLG-063: the project file first, then the member documents in tab order.
        var members = _documents.Documents.Where(d => IsMember(d.FilePath)).ToList();
        var ordered = members.Where(IsProjectFile).Concat(members.Where(d => !IsProjectFile(d))).ToList();
        if (!await _documents.PromptSaveChangesAsync(ordered))
            return false;

        // IDE-PM-014 step 3: the session, before the members close. Messages/Structure clearing (P3) joins here; the
        // project-file watch ends in SetProject(null).
        Session?.Save();
        foreach (var document in ordered)
        {
            _documents.Close(document);
        }

        var closed = _project.FilePath;
        SetProject(null);
        RecentItems?.SetOpenAtExit(closed, false); // IDE-P1-016: closed by the user (the exit flow must not come here)
        return true;
    }

    public bool IsMember(string? path) => path is not null && _members.Contains(Path.GetFullPath(path));

    public string? ResolvePath(string projectPath) => _project is null ? null : Resolve(projectPath);

    /// <summary>The folder of the selected Project Manager row when it is a folder row (IDE-PM-040; set by the service graph).</summary>
    public Func<string?>? SelectedFolder { get; set; }

    public event EventHandler<IReadOnlyList<string>>? FilesAdded;

    public async Task<bool> AddNewUnitAsync()
    {
        if (_project is null)
            return false;

        // IDE-PM-040: the selected folder inside the project directory, else the main source's folder.
        var folder = SelectedFolder?.Invoke() is { } selected && IsInside(selected, _project.Directory)
            ? Path.GetFullPath(selected)
            : Path.GetDirectoryName(Resolve(_project.MainSource))!;
        var units = UnitNames(_project);
        var n = 1;
        while (units.Contains($"Unit{n}") || _fileSystem.FileExists(Path.Combine(folder, $"Unit{n}.pas")))
            n++;
        var name = $"Unit{n}";
        var path = Path.Combine(folder, name + ".pas");
        var bytes = SourceBytes(UnitSkeleton(name));
        while (true)
        {
            try
            {
                AtomicFileWriter.Write(_fileSystem, path, bytes);
                break;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Retry and Cancel only, as in the New File dialog (UI-DLG-040): there is no document to "Save As" yet.
                if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.SaveFailed", [path, e.Message], Buttons: ["Retry", "Cancel"])) != "Retry")
                    return false;
            }
        }

        var projectPath = ToProjectPath(path);
        if (!await SaveChangedProjectAsync(_project with { Files = FilesForMutation(_project).Add(new ProjectFile(projectPath, null, null)) }))
        {
            DeleteQuietly(path);
            return false;
        }

        FilesAdded?.Invoke(this, [projectPath]);
        var opened = await _documents.OpenAsync(path);
        if (opened.Value is TextDocumentViewModel document)
            document.ViewState.Caret = new TextPosition(6, 1); // IDE-PM-041: the empty line after implementation
        else if (opened.NeedsMessage)
            await _dialogs.ShowMessageAsync(new MessageRequest(opened.MessageId!, opened.Args));
        return true;
    }

    public Task<bool> AddNewFormAsync()
    {
        if (_project is null)
            return Task.FromResult(false);

        var projectHasForm = FilesForMutation(_project).Any(f => !string.IsNullOrEmpty(f.Form));
        return AddNewFormUnitAsync(isTaken => Designer.NewFormTemplate.NextName(projectHasForm, isTaken),
            name => Designer.NewFormTemplate.UnitText(name, name), Designer.NewFormTemplate.FormText);
    }

    public Task<bool> AddNewFrameAsync() => _project is null
        ? Task.FromResult(false)
        : AddNewFormUnitAsync(Designer.NewFrameTemplate.NextName, name => Designer.NewFrameTemplate.UnitText(name, name), Designer.NewFrameTemplate.FormText);

    /// <summary>
    /// DSG-CS-110, 115: a new unit with its form in the folder of IDE-PM-040 (a name is free when no project unit and no
    /// file of the folder has it), the <c>{ path, form }</c> entry saved at once, the unit opened in the Design view.
    /// </summary>
    private async Task<bool> AddNewFormUnitAsync(Func<Func<string, bool>, string> nextName, Func<string, string> unitText, Func<string, string> formText)
    {
        if (_project is null)
            return false;

        var folder = SelectedFolder?.Invoke() is { } selected && IsInside(selected, _project.Directory)
            ? Path.GetFullPath(selected)
            : Path.GetDirectoryName(Resolve(_project.MainSource))!;
        var units = UnitNames(_project);
        var name = nextName(
            n => units.Contains(n) || _fileSystem.FileExists(Path.Combine(folder, n + ".pas")) || _fileSystem.FileExists(Path.Combine(folder, n + ".form")));
        var unitPath = Path.Combine(folder, name + ".pas");
        var formPath = Path.Combine(folder, name + ".form");
        if (!await WriteNewFileAsync(unitPath, SourceBytes(unitText(name))))
            return false;
        if (!await WriteNewFileAsync(formPath, FileEncodings.Encode(formText(name), FileEncoding.Utf8)))
        {
            DeleteQuietly(unitPath);
            return false;
        }

        var projectPath = ToProjectPath(unitPath);
        if (!await SaveChangedProjectAsync(_project with
            {
                Files = FilesForMutation(_project).Add(new ProjectFile(projectPath, ToProjectPath(formPath), null)),
            }))
        {
            DeleteQuietly(unitPath);
            DeleteQuietly(formPath);
            return false;
        }

        FilesAdded?.Invoke(this, [projectPath]);
        var opened = await _documents.OpenAsync(unitPath);
        if (opened.Value is FormDocumentViewModel document)
            document.ShowView(DocumentView.Design);
        else if (opened.NeedsMessage)
            await _dialogs.ShowMessageAsync(new MessageRequest(opened.MessageId!, opened.Args));
        return true;
    }

    public async Task<bool> AddNewFileAsync()
    {
        if (_project is null)
            return false;

        // IDE-PM-118: the folder of IDE-PM-040; IDE-PM-119: File<N>.txt with the smallest free N, zero bytes.
        var folder = SelectedFolder?.Invoke() is { } selected && IsInside(selected, _project.Directory)
            ? Path.GetFullPath(selected)
            : Path.GetDirectoryName(Resolve(_project.MainSource))!;
        var path = Path.Combine(folder, FolderNames.FirstFree(_fileSystem, folder, "File", ".txt"));
        if (!await WriteNewFileAsync(path, []))
            return false;
        var projectPath = ToProjectPath(path);
        if (!await SaveChangedProjectAsync(_project with { Files = FilesForMutation(_project).Add(new ProjectFile(projectPath, null, null)) }))
        {
            DeleteQuietly(path);
            return false;
        }

        FilesAdded?.Invoke(this, [projectPath]);
        RenameInPlace?.Invoke(projectPath, true); // UI-TW-117: the base name selected; Enter on the row opens the file
        return true;
    }

    public string? FormPathOf(string unitPath)
    {
        ArgumentNullException.ThrowIfNull(unitPath);
        if (_project is null)
            return null;
        var full = Path.GetFullPath(unitPath);
        var entry = FilesForMutation(_project).FirstOrDefault(f => !string.IsNullOrEmpty(f.Form) && !string.IsNullOrEmpty(f.Path)
            && string.Equals(Resolve(f.Path), full, StringComparison.OrdinalIgnoreCase));
        return entry is null ? null : Resolve(entry.Form!);
    }

    /// <summary>A new file of a "new" command: Retry and Cancel only on a failure (UI-DLG-040: nothing to "Save As" yet).</summary>
    private async Task<bool> WriteNewFileAsync(string path, byte[] bytes)
    {
        while (true)
        {
            try
            {
                AtomicFileWriter.Write(_fileSystem, path, bytes);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.SaveFailed", [path, e.Message], Buttons: ["Retry", "Cancel"])) != "Retry")
                    return false;
            }
        }
    }

    /// <summary>project-manager.md §4.2: the unit skeleton (LF; <see cref="SourceBytes"/> applies the line-ending setting).</summary>
    private static string UnitSkeleton(string name) => $"unit {name};\n\ninterface\n\nimplementation\n\nend.\n";

    /// <summary>
    /// <c>file.newFile</c> (UI-DLG-040): the New File dialog. Initial folder: the selected Project Manager folder inside
    /// the project, else the project directory; without a project the active document's folder, else
    /// <c>environment.defaultProjectsDir</c>.
    /// </summary>
    private async Task NewFileAsync()
    {
        string folder;
        if (_project is { } project)
        {
            folder = SelectedFolder?.Invoke() is { } selected && IsInside(selected, project.Directory)
                ? Path.GetFullPath(selected)
                : project.Directory;
        }
        else
        {
            folder = _documents.ActiveDocument?.FilePath is { } active ? Path.GetDirectoryName(active)! : _prompts.DefaultProjectsDirectory;
        }

        var dialog = new NewFileViewModel(folder, _project?.Name, _fileSystem,
            name => _project is { } p && UnitNames(p).Contains(name),
            CreateNewFileAsync,
            current => _prompts.PickFolderAsync("Folder", current));
        if (await _dialogs.ShowAsync(dialog) && _commands?.CanExecute("window.focusEditor") == true)
            await _commands.ExecuteAsync("window.focusEditor"); // UI-DLG-040: the new file's editor gets focus
    }

    /// <summary>
    /// UI-DLG-040 <c>Create</c>: writes the file (default encoding and line endings), adds it to the project when asked,
    /// opens it and focuses the editor. A write failure asks <c>Msg.File.SaveFailed</c> (Retry repeats); false keeps
    /// the dialog open.
    /// </summary>
    private async Task<bool> CreateNewFileAsync(NewFileViewModel request)
    {
        var path = Path.GetFullPath(request.FilePath);
        var isUnit = request.SelectedType.Kind == NewFileKind.Unit;
        var bytes = SourceBytes(isUnit ? UnitSkeleton(request.FileBaseName) : "");
        while (true)
        {
            try
            {
                AtomicFileWriter.Write(_fileSystem, path, bytes);
                break;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // UI-DLG-040: Retry and Cancel only (there is nothing to "Save As" yet).
                if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.SaveFailed", [path, e.Message], Buttons: ["Retry", "Cancel"])) != "Retry")
                    return false;
            }
        }

        if (request.ShowAddToProject && request.AddToProject && _project is { } project)
        {
            var projectPath = ToProjectPath(path);
            if (!await SaveChangedProjectAsync(project with { Files = FilesForMutation(project).Add(new ProjectFile(projectPath, null, null)) }))
            {
                DeleteQuietly(path);
                return false;
            }

            FilesAdded?.Invoke(this, [projectPath]);
        }

        var opened = await _documents.OpenAsync(path);
        if (opened.Value is TextDocumentViewModel document && isUnit)
            document.ViewState.Caret = new TextPosition(6, 1); // the empty line after implementation (IDE-PM-041)
        else if (opened.NeedsMessage)
            await _dialogs.ShowMessageAsync(new MessageRequest(opened.MessageId!, opened.Args));
        return true;
    }

    public async Task<int> AddExistingFilesAsync(IReadOnlyList<string> paths, bool reportFirstDuplicateOnly = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (_project is null)
            return 0;
        var units = UnitNames(_project);
        var files = FilesForMutation(_project).ToBuilder();
        var added = new List<string>();
        var addedFull = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateReported = false;
        foreach (var path in paths.Select(Path.GetFullPath))
        {
            var fileName = Path.GetFileName(path);
            var extension = Path.GetExtension(path);
            if (extension.Equals(".form", StringComparison.OrdinalIgnoreCase))
            {
                _status.ShowMessage(Strings.Status_AddFormViaUnit, StatusDuration); // IDE-PM-051
                continue;
            }

            if (_members.Contains(path) || addedFull.Contains(path))
            {
                _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_FileAlreadyInProject, fileName), StatusDuration);
                continue;
            }

            var isUnit = extension.Equals(".pas", StringComparison.OrdinalIgnoreCase);
            var unitName = Path.GetFileNameWithoutExtension(path);
            if (isUnit && !units.Add(unitName))
            {
                if (!duplicateReported || !reportFirstDuplicateOnly)
                    await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.DuplicateUnitName", [unitName])); // IDE-PM-031
                duplicateReported = true;
                continue;
            }

            var form = isUnit ? FormCompanion(path) : null;
            var projectPath = ToProjectPath(path);
            files.Add(new ProjectFile(projectPath, form is null ? null : ToProjectPath(form), null));
            added.Add(projectPath);
            addedFull.Add(path);
        }

        if (added.Count == 0 || !await SaveChangedProjectAsync(_project with { Files = files.ToImmutable() }))
            return 0;
        FilesAdded?.Invoke(this, added);
        return added.Count;
    }

    /// <summary>
    /// IDE-LIB-032: adds one existing file exactly as <see cref="AddExistingFilesAsync"/> does (by reference, with its
    /// <c>form</c> member when applicable) but shows no message: the caller reports the failure. A member is a success.
    /// </summary>
    public OperationResult AddExistingFileQuietly(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_project is null)
            return OperationResult.Fail(OperationResult.CanceledId);
        path = Path.GetFullPath(path);
        if (_members.Contains(path))
            return OperationResult.Ok;
        var isUnit = Path.GetExtension(path).Equals(".pas", StringComparison.OrdinalIgnoreCase);
        var unitName = Path.GetFileNameWithoutExtension(path);
        if (isUnit && UnitNames(_project).Contains(unitName))
            return OperationResult.Fail("Msg.Project.DuplicateUnitName", unitName); // IDE-PM-031
        var form = isUnit ? FormCompanion(path) : null;
        var projectPath = ToProjectPath(path);
        var changed = _project with { Files = FilesForMutation(_project).Add(new ProjectFile(projectPath, form is null ? null : ToProjectPath(form), null)) };
        var saved = SaveProjectFile(changed); // IDE-PM-004: on failure the in-memory project stays as it was
        if (!saved.Success)
            return saved;
        SetProject(changed);
        FilesAdded?.Invoke(this, [projectPath]);
        return OperationResult.Ok;
    }

    /// <summary>
    /// IDE-PM-051: the sibling <c>&lt;base&gt;.form</c> when it exists and the unit's text has a <c>{$FORM &lt;base&gt;.form}</c>
    /// directive (directive trivia of the compiler lexer, so a directive inside a comment does not count).
    /// </summary>
    private string? FormCompanion(string unitPath)
    {
        var form = Path.ChangeExtension(unitPath, ".form");
        if (!_fileSystem.FileExists(form))
            return null;
        string text;
        try
        {
            text = EncodingDetector.Detect(_fileSystem.ReadAllBytes(unitPath)).Text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var expected = Path.GetFileName(form);
        foreach (var token in Lexer.Tokenize(SourceText.From(unitPath, text)))
        {
            foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
            {
                if (trivia.Kind == SyntaxKind.DirectiveTrivia && trivia.Directive is { Name: "FORM" } directive
                    && string.Equals(directive.Argument.Trim().Trim('\''), expected, StringComparison.OrdinalIgnoreCase))
                    return form;
            }
        }

        return null;
    }

    /// <summary>IDE-PM-031: unit names (file base names of unit entries and the main source), case-insensitive.</summary>
    private static HashSet<string> UnitNames(Project project) =>
        FilesForMutation(project).Where(f => !string.IsNullOrEmpty(f.Path) && f.EffectiveKind == "unit")
            .Select(f => Path.GetFileNameWithoutExtension(f.Path.Replace('/', Path.DirectorySeparatorChar)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The <c>files</c> list to mutate; an omitted list stands for the main source alone (CMP-810).</summary>
    private static ImmutableArray<ProjectFile> FilesForMutation(Project project) =>
        project.Files.IsEmpty ? [new ProjectFile(project.MainSource, null, null)] : project.Files;

    /// <summary>
    /// IDE-PM-030: relative to the project directory with '/' when on the same drive and at most three <c>..</c>
    /// segments are needed; otherwise absolute with '/'.
    /// </summary>
    private string ToProjectPath(string fullPath)
    {
        var directory = _project!.Directory;
        if (string.Equals(Path.GetPathRoot(fullPath), Path.GetPathRoot(directory), StringComparison.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(directory, fullPath).Replace('\\', '/');
            if (!Path.IsPathRooted(relative) && relative.Split('/').Count(s => s == "..") <= 3)
                return relative;
        }

        return fullPath.Replace('\\', '/');
    }

    private static bool IsInside(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>A new source file's bytes: LF text converted to <c>files.defaultLineEnding</c>, encoded per <c>files.defaultEncoding</c>.</summary>
    private byte[] SourceBytes(string lfText)
    {
        if (_settings.Get(SettingsCatalog.FilesDefaultLineEnding) != "lf")
            lfText = lfText.Replace("\n", "\r\n", StringComparison.Ordinal);
        var encoding = FileEncodings.FromId(_settings.Get(SettingsCatalog.FilesDefaultEncoding)) ?? FileEncoding.Utf8;
        return FileEncodings.Encode(lfText, encoding);
    }

    private void DeleteQuietly(string path)
    {
        try
        {
            _fileSystem.DeleteFile(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogCategory, $"{path}: not removed after a failed project save", e);
        }
    }

    public async Task<bool> RemoveFromProjectAsync(IReadOnlyList<string> projectPaths)
    {
        ArgumentNullException.ThrowIfNull(projectPaths);
        if (_project is null)
            return false;
        var remove = projectPaths.Select(Resolve).ToHashSet(StringComparer.OrdinalIgnoreCase);
        remove.Remove(Resolve(_project.MainSource)); // IDE-PM-060
        var changed = false;
        var files = ImmutableArray.CreateBuilder<ProjectFile>();
        foreach (var file in _project.Files)
        {
            if (!string.IsNullOrEmpty(file.Path) && remove.Contains(Resolve(file.Path)))
            {
                changed = true;
                continue;
            }

            if (file.Form is { } form && remove.Contains(Resolve(form)))
            {
                files.Add(file with { Form = null });
                changed = true;
                continue;
            }

            files.Add(file);
        }

        return changed && await SaveChangedProjectAsync(_project with { Files = files.ToImmutable() });
    }

    /// <summary>
    /// The Project Manager selection (project paths of file and form rows) while focus is in the tree; null otherwise
    /// (IDE-PM-060; set by the service graph).
    /// </summary>
    public Func<IReadOnlyList<string>?>? SelectedFiles { get; set; }

    /// <summary>The targets of <c>project.removeFile</c> / <c>.deleteFile</c>: the tree selection, else the active document if it is listed.</summary>
    private IReadOnlyList<string> FileTargets()
    {
        if (_project is null)
            return [];
        if (SelectedFiles?.Invoke() is { } selected)
            return selected;
        return _documents.ActiveDocument?.FilePath is { } path && EntryPathOf(path) is { } entry ? [entry] : [];
    }

    /// <summary>Enabled when a target other than the main source exists (menus.md §6).</summary>
    private bool CanOperateOnFiles() => FileTargets().Any(p => !IsMainSource(Resolve(p)));

    /// <summary>The stored path of the <c>files</c> entry or form for <paramref name="fullPath"/>; null when not listed.</summary>
    private string? EntryPathOf(string fullPath)
    {
        foreach (var file in Project!.Files)
        {
            if (string.Equals(Resolve(file.Path), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase))
                return file.Path;
            if (file.Form is { } form && string.Equals(Resolve(form), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase))
                return form;
        }

        return null;
    }

    /// <summary>IDE-PM-060: a target list with the main source is refused as a whole.</summary>
    private async Task<bool> RefuseMainSourceAsync(IReadOnlyList<string> projectPaths)
    {
        if (projectPaths.FirstOrDefault(p => IsMainSource(Resolve(p))) is not { } main)
            return false;
        await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.RemoveMainSource", [Path.GetFileName(Resolve(main))]));
        return true;
    }

    public async Task<bool> RemoveFilesAsync(IReadOnlyList<string> projectPaths)
    {
        ArgumentNullException.ThrowIfNull(projectPaths);
        if (_project is null || projectPaths.Count == 0 || await RefuseMainSourceAsync(projectPaths))
            return false;
        var request = projectPaths.Count == 1
            ? new MessageRequest("Msg.Project.RemoveFileConfirm", [Path.GetFileName(Resolve(projectPaths[0]))])
            : new MessageRequest("Msg.Project.RemoveFilesConfirm", [projectPaths.Count.ToString(CultureInfo.CurrentCulture)]);
        return await _dialogs.ShowMessageAsync(request) == "Remove" && await RemoveFromProjectAsync(projectPaths);
    }

    public async Task<bool> DeleteFilesAsync(IReadOnlyList<string> projectPaths)
    {
        ArgumentNullException.ThrowIfNull(projectPaths);
        if (_project is null || projectPaths.Count == 0 || await RefuseMainSourceAsync(projectPaths))
            return false;

        // Each target with its companion form (a unit row), unit first.
        var targets = projectPaths.Select(p =>
        {
            var entry = FilesForMutation(_project).FirstOrDefault(f => !string.IsNullOrEmpty(f.Path)
                && string.Equals(Resolve(f.Path), Resolve(p), StringComparison.OrdinalIgnoreCase));
            List<string> files = [Resolve(p)];
            if (entry?.Form is { } form)
                files.Add(Resolve(form));
            return (ProjectPath: p, Files: files);
        }).ToList();
        var names = string.Join(", ", targets.SelectMany(t => t.Files).Select(Path.GetFileName));
        if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.DeleteFileConfirm", [names])) != "Delete")
            return false;

        var deleted = new List<string>();
        foreach (var (projectPath, files) in targets)
        {
            foreach (var file in files)
            {
                if (_fileSystem.FileExists(file) && !_shell.MoveToRecycleBin(file, out var reason))
                {
                    await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.DeleteFailed", [file, reason]));
                    break;
                }

                // The confirmation covers unsaved changes: the document closes without a prompt.
                foreach (var document in _documents.Documents.Where(d => string.Equals(d.FilePath, file, StringComparison.OrdinalIgnoreCase)).ToList())
                    _documents.Close(document);
                if (file == files[0])
                    deleted.Add(projectPath); // a unit that is gone loses its entry even if its form could not be recycled
            }
        }

        return deleted.Count > 0 && await RemoveFromProjectAsync(deleted) && deleted.Count == targets.Count;
    }

    /// <summary>
    /// Starts the in-place rename of a Project Manager row (UI-TW-112); false when the row cannot be edited, so the
    /// dialog is used (set by the service graph).
    /// </summary>
    public Func<string, bool, bool>? RenameInPlace { get; set; }

    /// <summary>
    /// IDE-PM-115: the removal of the selected Project Manager folder row when it holds no project file and the tree has
    /// focus; null otherwise (set by the service graph).
    /// </summary>
    public Func<Func<Task>?>? RemovableFolder { get; set; }

    public event EventHandler<string>? FileRenamed;

    /// <summary>IDE-PM-130: the language service's edit set for a unit rename (set by the service graph); null: lexer only.</summary>
    public CodeModel.UnitRenameAnalyzer? UnitRename { get; set; }

    public PendingUnitRename? PendingRename { get; private set; }

    public event EventHandler? PendingRenameChanged;

    /// <summary>The one <c>files</c> entry (not a form) <c>project.renameFile</c> acts on; null when there is none.</summary>
    private string? RenameTarget() =>
        PendingRename is null && FileTargets() is [var target] && FindEntry(target) is not null ? target : null;

    /// <summary>The <c>files</c> entry whose path resolves like <paramref name="projectPath"/>.</summary>
    private ProjectFile? FindEntry(string projectPath) =>
        FilesForMutation(_project!).FirstOrDefault(f => !string.IsNullOrEmpty(f.Path)
            && string.Equals(Resolve(f.Path), Resolve(projectPath), StringComparison.OrdinalIgnoreCase));

    /// <summary><c>project.renameFile</c>: in place inside the tree, else the <c>RenameFile</c> dialog (UI-DLG-070).</summary>
    private async Task RenameCommandAsync()
    {
        if (RenameTarget() is not { } target)
            return;
        if (SelectedFiles?.Invoke() is not null && RenameInPlace?.Invoke(target, false) == true)
            return;
        var dialog = new RenameFileViewModel(Path.GetFileName(Resolve(target)), name => ValidateRename(target, name),
            (name, update) => RenameFileAsync(target, name, update));
        await _dialogs.ShowAsync(dialog);
    }

    public OperationResult ValidateRename(string projectPath, string newName, bool newFile = false)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(newName);
        if (_project is null)
            return OperationResult.Fail("Msg.Project.RenameInvalid");
        var oldPath = Resolve(projectPath);
        var oldName = Path.GetFileName(oldPath);
        if (newName == oldName)
            return OperationResult.Ok;
        var isUnit = IsUnitFile(oldName);
        var newBase = Path.GetFileNameWithoutExtension(newName);
        if (newFile && IsUnitFile(newName))
            return OperationResult.Fail("Msg.Project.NewFileIsUnit"); // IDE-PM-119: a unit needs its skeleton
        if (newFile && Path.GetExtension(newName).Equals(".form", StringComparison.OrdinalIgnoreCase))
            return OperationResult.Fail("Status.AddFormViaUnit");
        if (isUnit)
        {
            if (!Path.GetExtension(newName).Equals(".pas", StringComparison.OrdinalIgnoreCase) || !IsIdentifier(newBase)
                || SyntaxFacts.IsReservedWord(newBase))
                return OperationResult.Fail("Msg.Project.RenameInvalid");
        }
        else if (newName.Length == 0 || newName.IndexOfAny(InvalidFileNameChars) >= 0)
        {
            return OperationResult.Fail("Msg.NewFile.NameInvalid");
        }

        // A change of letter case of the same file is allowed (UI-DLG-071).
        var folder = Path.GetDirectoryName(oldPath)!;
        var caseOnly = string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && _fileSystem.FileExists(Path.Combine(folder, newName)))
            return OperationResult.Fail("Msg.Project.RenameExists", newName);
        if (!caseOnly && FormToRename(projectPath) is { } form
            && _fileSystem.FileExists(Path.Combine(Path.GetDirectoryName(form)!, newBase + ".form")))
            return OperationResult.Fail("Msg.Project.RenameExists", newBase + ".form");
        if (isUnit && !caseOnly && UnitNames(_project).Contains(newBase))
            return OperationResult.Fail("Msg.Project.DuplicateUnitName", newBase);
        return OperationResult.Ok;
    }

    /// <summary>dialogs.md §24 <c>Msg.NewFile.NameInvalid</c>: <c>\ / : * ? " &lt; &gt; |</c>.</summary>
    private static readonly char[] InvalidFileNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private static bool IsUnitFile(string fileName) => Path.GetExtension(fileName).Equals(".pas", StringComparison.OrdinalIgnoreCase);

    /// <summary>The companion form renamed with the unit: the entry's form when it has the unit's base name (IDE-PM-071 step 3).</summary>
    private string? FormToRename(string projectPath)
    {
        if (FindEntry(projectPath)?.Form is not { } form)
            return null;
        var formPath = Resolve(form);
        return string.Equals(Path.GetFileNameWithoutExtension(formPath), Path.GetFileNameWithoutExtension(Resolve(projectPath)),
            StringComparison.OrdinalIgnoreCase) ? formPath : null;
    }

    public async Task<bool> RenameFileAsync(string projectPath, string newName, bool updateHeading = true)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(newName);
        if (PendingRename is not null)
            return false;
        try
        {
            return await RenameFileCoreAsync(projectPath, newName, updateHeading);
        }
        finally
        {
            SetPendingRename(null); // UI-TW-118: the row is busy until the rename ended
        }
    }

    private void SetPendingRename(PendingUnitRename? pending)
    {
        if (PendingRename == pending)
            return;
        PendingRename = pending;
        PendingRenameChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> RenameFileCoreAsync(string projectPath, string newName, bool updateHeading)
    {
        if (_project is null || FindEntry(projectPath) is not { } entry || !ValidateRename(projectPath, newName).Success)
            return false;
        var oldPath = Resolve(projectPath);
        var oldName = Path.GetFileName(oldPath);
        if (newName == oldName)
            return true;
        var newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, newName);
        var oldBase = Path.GetFileNameWithoutExtension(oldName);
        var newBase = Path.GetFileNameWithoutExtension(newName);
        var document = DocumentOf(oldPath);

        // (1) A modified open document is saved first; a failure (reported by the save) aborts.
        if (document is { IsModified: true } && !await _documents.SaveAsync(document))
            return false;

        // IDE-PM-130, 137: a unit whose name changes other than in letter case asks the language service for the edits.
        CodeModel.UnitRenamePlan? plan = null;
        if (updateHeading && UnitRename is { } analyzer && IsUnitFile(oldName) && !string.Equals(oldBase, newBase, StringComparison.OrdinalIgnoreCase))
        {
            plan = await AnalyzeUnitRenameAsync(analyzer, projectPath, oldPath, newPath, newName);
            if (plan is null)
                return false; // refused and reported (IDE-PM-134)
            if (plan.Outcome != CodeModel.UnitRenameOutcome.Edits)
                plan = null; // IDE-PM-135: as in Phases 1-7
        }

        var oldForm = FormToRename(projectPath);
        var newForm = oldForm is null ? null : Path.Combine(Path.GetDirectoryName(oldForm)!, newBase + ".form");
        var original = _project;
        var renamed = original with
        {
            Files = [.. FilesForMutation(original).Select(f => f == entry
                ? f with { Path = Rename(f.Path, newName), Form = newForm is null ? f.Form : Rename(f.Form!, Path.GetFileName(newForm)) }
                : f)],
            MainSource = IsMainSource(oldPath) ? Rename(original.MainSource, newName) : original.MainSource,
        };
        var undo = new Stack<Action>();
        var edited = new List<(TextDocumentViewModel Document, string Text)>();
        var written = new List<string> { oldPath, newPath };
        var headingUpdated = false;
        var references = (Places: 0, Files: 0);
        try
        {
            // (2)-(3) the files on disk.
            MoveFile(oldPath, newPath);
            undo.Push(() => MoveFile(newPath, oldPath));
            if (oldForm is not null && _fileSystem.FileExists(oldForm))
            {
                MoveFile(oldForm, newForm!);
                undo.Push(() => MoveFile(newForm!, oldForm));
            }

            // (4)+(7) the project file, saved before the texts so that a failure there can still be undone.
            var projectBytes = _fileSystem.ReadAllBytes(original.FilePath);
            SaveOrThrow(renamed);
            undo.Push(() => WriteProjectFile(original.FilePath, () => AtomicFileWriter.Write(_fileSystem, original.FilePath, projectBytes))); // the exact bytes back

            // IDE-PM-132: the references in the other files, each open buffer as one undo group and saved, each other file
            // rewritten atomically; what was written is restored when a later step fails.
            IReadOnlyList<TextEdit>? ownEdits = null;
            foreach (var edit in plan?.Edits ?? [])
            {
                var edits = edit.Changes.Select(c => new TextEdit(c.Span.Start, c.Span.Length, c.NewText)).ToList();
                if (string.Equals(edit.Path, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    ownEdits = edits;
                    var outside = edits.Count(e => e.Offset != plan!.Heading.Start);
                    references = (references.Places + outside, references.Files + (outside > 0 ? 1 : 0));
                    continue;
                }

                references = (references.Places + edits.Count, references.Files + 1);
                if (DocumentOf(edit.Path) is { } other)
                {
                    var text = other.Buffer.Text;
                    other.Buffer.ApplyEdits(edits, Strings.Cmd_project_renameFile);
                    edited.Add((other, text));
                    var saved = await _documents.SaveAsAsync(other, other.FilePath!);
                    if (!saved.Success)
                        throw new IOException(saved.Args[^1]);
                }
                else
                {
                    var bytes = _fileSystem.ReadAllBytes(edit.Path);
                    var file = EncodingDetector.Detect(bytes);
                    var text = file.Text;
                    foreach (var change in edits.OrderByDescending(e => e.Offset))
                        text = string.Concat(text.AsSpan(0, change.Offset), change.NewText, text.AsSpan(change.Offset + change.Length));
                    AtomicFileWriter.Write(_fileSystem, edit.Path, FileEncodings.Encode(text, file.Encoding));
                    undo.Push(() => AtomicFileWriter.Write(_fileSystem, edit.Path, bytes));
                    written.Add(edit.Path);
                }
            }

            // (5)-(6) heading and {$FORM} in the buffer or the file; the open document follows the new path.
            if (IsUnitFile(oldName))
                headingUpdated = await UpdateUnitTextAsync(document, newPath, oldBase, newBase,
                    oldForm is null ? null : (Path.GetFileName(oldForm), Path.GetFileName(newForm!)), updateHeading, ownEdits);
            else if (document is not null)
                await SaveDocumentAsAsync(document, newPath, document.Buffer.Text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            while (undo.TryPop(out var step))
            {
                try
                {
                    step();
                }
                catch (Exception u) when (u is IOException or UnauthorizedAccessException)
                {
                    _log.Warning(LogCategory, $"{oldPath}: rename not fully undone", u);
                }
            }

            // IDE-PM-132: every edited buffer gets its text back (the undo group is undone) and is saved again.
            foreach (var (other, text) in edited)
            {
                while (other.Buffer.Text != text && other.Buffer.CanUndo)
                    other.Buffer.Undo();
                if (!(await _documents.SaveAsAsync(other, other.FilePath!)).Success)
                    _log.Warning(LogCategory, $"{other.FilePath}: rename not fully undone");
            }

            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.RenameFailed", [oldPath, e.Message]));
            return false;
        }

        SetProject(renamed);
        UnitRename?.NotifyChanged(written);
        FileRenamed?.Invoke(this, Rename(projectPath, newName));
        if (headingUpdated)
        {
            // IDE-PM-136: the count of edited places outside the heading and of the files they are in.
            var format = references.Places == 0 ? Strings.Msg_Project_RenameUnitNote
                : references.Files > 1 ? Strings.Msg_Project_RenameUnitRefsNote
                : references.Places > 1 ? Strings.Msg_Project_RenameUnitRefsNoteOneFile
                : Strings.Msg_Project_RenameUnitRefsNoteOne;
            _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, format, oldName, newName, references.Places, references.Files), StatusDuration);
        }

        return true;
    }

    private TextDocumentViewModel? DocumentOf(string path) => _documents.Documents.OfType<TextDocumentViewModel>()
        .FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// IDE-PM-130…135: the edit set of the unit rename, with the busy state of UI-TW-118 while the language service
    /// works. Null when the rename is refused (<c>Msg.Project.RenameConflict</c> was shown): a conflict found by the
    /// service, an edit set that does not match the rename asked for, or one that touches a file the IDE must not edit.
    /// </summary>
    private async Task<CodeModel.UnitRenamePlan?> AnalyzeUnitRenameAsync(CodeModel.UnitRenameAnalyzer analyzer, string projectPath,
        string oldPath, string newPath, string newName)
    {
        var newBase = Path.GetFileNameWithoutExtension(newName);
        CodeModel.UnitRenamePlan plan;
        IDisposable? sticky = null;
        var slow = _clock.Schedule(UpdatingReferencesDelay, () => sticky = _status.ShowStickyMessage(Strings.Status_UpdatingReferences));
        SetPendingRename(new PendingUnitRename(projectPath, newName));
        try
        {
            plan = await analyzer.AnalyzeAsync(oldPath, Path.GetFileNameWithoutExtension(oldPath), newBase);
        }
        finally
        {
            slow.Dispose();
            sticky?.Dispose();
        }

        var reason = plan.Outcome == CodeModel.UnitRenameOutcome.Refused ? plan.Error : null;
        if (plan.Outcome == CodeModel.UnitRenameOutcome.Edits)
        {
            // IDE-PM-131: the file rename of the service is the one asked for; IDE-PM-133: only the project's own files
            // are edited, and the texts are still the ones the edits were computed against.
            var same = plan.FileRename is null
                || (string.Equals(Path.GetFullPath(plan.FileRename.OldPath), oldPath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetFullPath(plan.FileRename.NewPath), newPath, StringComparison.OrdinalIgnoreCase));
            if (!same || plan.Edits.Any(e => !MayEditForRename(analyzer, e.Path)))
            {
                reason = Strings.Reason_RenameOutsideProject;
            }
            else if (plan.Edits.FirstOrDefault(e => CurrentText(e.Path) != plan.Texts[e.Path]) is { } stale)
            {
                await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.RenameFailed", [stale.Path, Strings.Msg_File_ExternalChange]));
                return null;
            }
        }

        if (reason is null)
            return plan;
        await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.RenameConflict", [newBase, reason]));
        return null;
    }

    /// <summary>UI-TW-118: a wait longer than this shows "Updating references…" in the status bar.</summary>
    private static readonly TimeSpan UpdatingReferencesDelay = TimeSpan.FromMilliseconds(400);

    private string? CurrentText(string path)
    {
        try
        {
            return DocumentOf(path)?.Buffer.Text ?? EncodingDetector.Detect(_fileSystem.ReadAllBytes(path)).Text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>IDE-PM-133: a project member, or a file under the project directory or its search paths that is not a library source.</summary>
    private bool MayEditForRename(CodeModel.UnitRenameAnalyzer analyzer, string path)
    {
        if (IsMember(path))
            return true;
        return !analyzer.IsLibrarySource(path)
            && (IsInside(path, _project!.Directory) || EffectiveSearchPaths().Any(s => IsInside(path, Resolve(s))));
    }

    /// <summary>The search paths the active configuration is built with (CMP-1160): project level, then its chain.</summary>
    private IEnumerable<string> EffectiveSearchPaths()
    {
        return ConfigurationEdits.Effective(_project!, _project!.ActiveConfiguration)?.SearchPaths ?? _project.SearchPaths;
    }

    /// <summary>One entry of a move (IDE-PM-123): the unit or file and its companion form, before and after.</summary>
    private sealed record FileMove(ProjectFile Entry, string From, string To, string? FormFrom, string? FormTo);

    public async Task<IReadOnlyList<string>> MoveFilesAsync(IReadOnlyList<string> projectPaths, string targetDirectory)
    {
        ArgumentNullException.ThrowIfNull(projectPaths);
        ArgumentNullException.ThrowIfNull(targetDirectory);
        if (_project is null || !IsInside(targetDirectory, _project.Directory))
            return [];
        var original = _project;
        var target = Path.GetFullPath(targetDirectory);
        var moves = new List<FileMove>();
        foreach (var projectPath in projectPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (FindEntry(projectPath) is not { } entry || moves.Any(m => m.Entry == entry))
                continue;
            var from = Resolve(entry.Path);
            // IDE-PM-120: only files inside the project directory move; IDE-PM-121: a file already there stays.
            if (!IsInside(from, original.Directory) || string.Equals(Path.GetDirectoryName(from), target, StringComparison.OrdinalIgnoreCase))
                continue;
            var formFrom = string.IsNullOrEmpty(entry.Form) ? null : Resolve(entry.Form);
            moves.Add(new FileMove(entry, from, Path.Combine(target, Path.GetFileName(from)), formFrom,
                formFrom is null ? null : Path.Combine(target, Path.GetFileName(formFrom))));
        }

        if (moves.Count == 0)
            return [];

        // IDE-PM-122: validation over all files before anything changes.
        var folderName = string.Equals(target, original.Directory, StringComparison.OrdinalIgnoreCase)
            ? original.Name
            : Path.GetRelativePath(original.Directory, target).Replace('\\', '/');
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var to in moves.SelectMany(m => new[] { m.To, m.FormTo }).OfType<string>())
        {
            if (!targets.Add(to) || _fileSystem.FileExists(to) || _fileSystem.DirectoryExists(to))
            {
                await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.MoveExists", [Path.GetFileName(to), folderName]));
                return [];
            }
        }

        if (targets.MaxBy(t => t.Length) is { Length: > NewProjectValidation.MaxPathLength } longest)
        {
            var reason = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_PathTooLong, NewProjectValidation.MaxPathLength);
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.MoveFailed", [longest, reason]));
            return [];
        }

        // (1) every open, modified document of a moved file is saved first; a failure (reported by the save) aborts.
        foreach (var move in moves)
        {
            if (DocumentOf(move.From) is { IsModified: true } modified && !await _documents.SaveAsync(modified))
                return [];
        }

        var moved = original with
        {
            Files = [.. FilesForMutation(original).Select(f => moves.FirstOrDefault(m => m.Entry == f) is { } m
                ? f with { Path = ToProjectPath(m.To), Form = m.FormTo is null ? f.Form : ToProjectPath(m.FormTo) }
                : f)],
            MainSource = moves.FirstOrDefault(m => IsMainSource(m.From)) is { } main ? ToProjectPath(main.To) : original.MainSource,
        };
        var undo = new Stack<(string Path, Action Step)>();
        var followed = new List<(TextDocumentViewModel Document, string From)>();
        string failedPath = moves[0].From;
        try
        {
            // (2) the files on disk.
            foreach (var move in moves)
            {
                failedPath = move.From;
                _fileSystem.MoveFile(move.From, move.To, overwrite: false);
                undo.Push((move.To, () => _fileSystem.MoveFile(move.To, move.From, overwrite: false)));
                if (move.FormFrom is not null && _fileSystem.FileExists(move.FormFrom))
                {
                    failedPath = move.FormFrom;
                    _fileSystem.MoveFile(move.FormFrom, move.FormTo!, overwrite: false);
                    undo.Push((move.FormTo!, () => _fileSystem.MoveFile(move.FormTo!, move.FormFrom, overwrite: false)));
                }
            }

            // (3)+(5) the project file, saved before the documents follow so that a failure there can still be undone.
            failedPath = original.FilePath;
            var projectBytes = _fileSystem.ReadAllBytes(original.FilePath);
            SaveOrThrow(moved);
            undo.Push((original.FilePath, () => WriteProjectFile(original.FilePath, () => AtomicFileWriter.Write(_fileSystem, original.FilePath, projectBytes))));

            // (4) open documents follow their file (a form document takes its form along).
            foreach (var move in moves)
            {
                if (DocumentOf(move.From) is not { } document)
                    continue;
                failedPath = move.From;
                await SaveDocumentAsAsync(document, move.To, document.Buffer.Text);
                followed.Add((document, move.From));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // IDE-PM-124: back in reverse order; what cannot be restored is named on its own line.
            var text = e.Message;
            while (undo.TryPop(out var step))
            {
                try
                {
                    step.Step();
                }
                catch (Exception u) when (u is IOException or UnauthorizedAccessException)
                {
                    _log.Warning(LogCategory, $"{step.Path}: move not undone", u);
                    text += "\n" + step.Path;
                }
            }

            foreach (var (document, from) in followed)
            {
                if (!(await _documents.SaveAsAsync(document, from)).Success)
                    text += "\n" + from;
            }

            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.MoveFailed", [failedPath, text]));
            return [];
        }

        SetProject(moved);
        var result = moves.Select(m => ToProjectPath(m.To)).ToList();
        foreach (var path in result)
            FileRenamed?.Invoke(this, path);
        _status.ShowMessage(moves.Count == 1
            ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_MovedOne, Path.GetFileName(moves[0].To), folderName)
            : string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_MovedMany, moves.Count, folderName), StatusDuration);
        return result;
    }

    /// <summary>A stored project path with its last segment replaced (keeps relative/absolute form and '/').</summary>
    private static string Rename(string storedPath, string newFileName)
    {
        var cut = storedPath.LastIndexOfAny(['/', '\\']);
        return storedPath[..(cut + 1)] + newFileName;
    }

    /// <summary>A move on disk; a change of letter case only goes through a temporary name.</summary>
    private void MoveFile(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            var temp = from + ".~rename";
            _fileSystem.MoveFile(from, temp, overwrite: false);
            _fileSystem.MoveFile(temp, to, overwrite: false);
            return;
        }

        _fileSystem.MoveFile(from, to, overwrite: false);
    }

    private void SaveOrThrow(Project project)
    {
        var saved = SaveProjectFile(project);
        if (!saved.Success)
            throw new IOException(saved.Args[^1]);
    }

    /// <summary>Writes the open project's file as this service's own change (IDE-P1-086) and records its new stamp.</summary>
    private OperationResult SaveProjectFile(Project project)
    {
        var saved = OperationResult.Ok;
        WriteProjectFile(project.FilePath, () => saved = ProjectFileAccess.Save(project, _fileSystem));
        return saved;
    }

    private void WriteProjectFile(string path, Action write)
    {
        using (WriteGuard?.Invoke(path))
        {
            write();
        }

        _projectStamp = ReadStamp(path);
    }

    /// <summary>The project file's stamp now; null when it cannot be read.</summary>
    private DiskStamp? ReadStamp(string path)
    {
        try
        {
            var lastWrite = _fileSystem.GetLastWriteTimeUtc(path);
            return DiskStamp.Of(lastWrite, _fileSystem.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// IDE-P1-117 (run by the file-change service): a project file changed by another program asks
    /// <c>Msg.Project.ChangedOnDisk</c>; Reload re-reads the project (open documents stay open), Keep records the new
    /// stamp. An identical rewrite only records the stamp; a missing or unreadable file is left to the next check.
    /// </summary>
    private async Task CheckProjectFileAsync()
    {
        if (_project is not { } project || _checkingProjectFile)
            return;
        var path = project.FilePath;
        var recorded = _projectStamp;
        _checkingProjectFile = true;
        try
        {
            var current = await Task.Run(() => ChangedStamp(path, recorded));
            if (current is null || _project is null || !string.Equals(_project.FilePath, path, StringComparison.OrdinalIgnoreCase)
                || !ReferenceEquals(_projectStamp, recorded))
                return; // unchanged, closed or written by us meanwhile
            if (recorded is null || recorded.Hash == current.Hash)
            {
                _projectStamp = current; // IDE-P1-112: same bytes, new timestamp (unknown before: start from here)
                return;
            }

            if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.ChangedOnDisk", [])) == "Reload")
                await ReloadProjectAsync(path);
            else
                _projectStamp = current;
        }
        finally
        {
            _checkingProjectFile = false;
        }
    }

    private DiskStamp? ChangedStamp(string path, DiskStamp? recorded)
    {
        try
        {
            if (!_fileSystem.FileExists(path))
                return null;
            if (recorded is not null && recorded.Matches(_fileSystem.GetLastWriteTimeUtc(path), _fileSystem.GetLength(path)))
                return null;
            return ReadStamp(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogCategory, $"Cannot check {path}.", e);
            return null;
        }
    }

    /// <summary>Reload (IDE-P1-117): the project as it is on disk replaces the one in memory; documents stay open.</summary>
    private async Task ReloadProjectAsync(string path)
    {
        var (load, stamp) = await Task.Run(() => (ProjectFileAccess.Load(path, _fileSystem), ReadStamp(path)));
        _projectStamp = stamp; // this version is not reported again, whether it loads or not
        var error = load.Project is { } loaded ? Validate(loaded) : load.Error;
        if (!error.Success)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest(error.MessageId!, error.Args));
            return;
        }

        var project = load.Project!;
        if (!project.Configurations.ContainsKey(project.ActiveConfiguration))
            project = project with { ActiveConfiguration = project.ConfigurationNames[0] };
        SetProject(project);
    }

    /// <summary>
    /// IDE-PM-071 step 5: the heading identifier (first tokens <c>unit</c>/<c>program</c>/<c>library</c>/<c>package</c>
    /// + identifier) and the file name in <c>{$FORM}</c> directive trivia, found with the compiler lexer, replaced in the
    /// open buffer (then saved under the new path) or in the renamed file. True when the heading was replaced.
    /// </summary>
    private async Task<bool> UpdateUnitTextAsync(TextDocumentViewModel? document, string path, string oldBase, string newBase,
        (string Old, string New)? form, bool updateHeading, IReadOnlyList<TextEdit>? serviceEdits = null)
    {
        EncodingDetection? file = null;
        var text = document?.Buffer.Text ?? (file = EncodingDetector.Detect(_fileSystem.ReadAllBytes(path))).Text;
        var edits = new List<TextEdit>();
        var headingUpdated = false;
        var tokens = Lexer.Tokenize(SourceText.From(path, text));
        if (serviceEdits is not null)
        {
            edits.AddRange(serviceEdits); // IDE-PM-131: heading, uses entries and qualified names from the language service
            headingUpdated = true;
        }
        else if (updateHeading && tokens.Length > 1
            && tokens[0].Kind is SyntaxKind.UnitKeyword or SyntaxKind.ProgramKeyword or SyntaxKind.LibraryKeyword or SyntaxKind.PackageKeyword
            && tokens[1].Kind == SyntaxKind.IdentifierToken && string.Equals(tokens[1].Text, oldBase, StringComparison.OrdinalIgnoreCase))
        {
            edits.Add(new TextEdit(tokens[1].Span.Start, tokens[1].Span.Length, newBase));
            headingUpdated = true;
        }

        if (form is { } names)
        {
            foreach (var trivia in tokens.SelectMany(t => t.LeadingTrivia.Concat(t.TrailingTrivia)))
            {
                if (trivia.Kind == SyntaxKind.DirectiveTrivia && trivia.Directive is { Name: "FORM" } directive
                    && string.Equals(directive.Argument.Trim().Trim('\''), names.Old, StringComparison.OrdinalIgnoreCase))
                {
                    var at = trivia.Text.IndexOf(names.Old, 2 + directive.Name.Length, StringComparison.OrdinalIgnoreCase);
                    if (at >= 0)
                        edits.Add(new TextEdit(trivia.Span.Start + at, names.Old.Length, names.New));
                }
            }
        }

        if (document is not null)
        {
            await SaveDocumentAsAsync(document, path, text, edits);
        }
        else if (edits.Count > 0)
        {
            foreach (var edit in edits.OrderByDescending(e => e.Offset))
                text = string.Concat(text.AsSpan(0, edit.Offset), edit.NewText, text.AsSpan(edit.Offset + edit.Length));
            AtomicFileWriter.Write(_fileSystem, path, FileEncodings.Encode(text, file!.Encoding));
        }

        return headingUpdated;
    }

    /// <summary>
    /// Applies <paramref name="edits"/> to the open document and saves it under <paramref name="path"/> (it becomes the
    /// document's file); on failure the buffer gets its text back and an <see cref="IOException"/> is thrown.
    /// </summary>
    private async Task SaveDocumentAsAsync(TextDocumentViewModel document, string path, string originalText, IReadOnlyList<TextEdit>? edits = null)
    {
        if (edits is { Count: > 0 })
            document.Buffer.ApplyEdits(edits);
        var saved = await _documents.SaveAsAsync(document, path);
        if (saved.Success)
            return;
        while (document.Buffer.Text != originalText && document.Buffer.CanUndo)
            document.Buffer.Undo();
        throw new IOException(saved.Args[^1]);
    }

    /// <summary>IDE-PM-004: a file-list mutation is saved immediately; on failure the in-memory project stays as it was.</summary>
    private async Task<bool> SaveChangedProjectAsync(Project project)
    {
        var saved = SaveProjectFile(project);
        if (!saved.Success)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest(saved.MessageId!, saved.Args));
            return false;
        }

        SetProject(project);
        return true;
    }

    public async Task<bool> ViewSourceAsync() =>
        _project is not null && await OpenDocumentAsync(Resolve(_project.MainSource));

    public async Task<NewProjectOutcome> CreateProjectAsync(NewProjectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var template = Templates.FirstOrDefault(t => t.Id == request.TemplateId)
            ?? throw new ArgumentException($"Unknown template '{request.TemplateId}'.", nameof(request));
        if (!await CloseAsync()) // UI-DLG-031: the unsaved-changes flow of the current project first
            return NewProjectOutcome.Canceled;

        if (await CreateProjectFilesAsync(request, template) is not { } projectFile)
            return NewProjectOutcome.Failed;
        if (!await OpenAsync(projectFile))
            return NewProjectOutcome.Failed; // reported by the open
        if (_documents.ActiveDocument is TextDocumentViewModel main && IsMainSource(main.FilePath))
            main.ViewState.Caret = CaretAfterBegin(main.Buffer);
        return NewProjectOutcome.Created;
    }

    /// <summary>
    /// IDE-PM-023 without opening anything (also <c>project.addNewProject</c>, IDE-PM-165): re-validates, creates
    /// <c>&lt;Location&gt;\&lt;Name&gt;\</c> from the template and returns the new project file; a failure removes everything
    /// this call created, shows <c>Msg.Project.CreateFailed</c> and returns null.
    /// </summary>
    internal async Task<string?> CreateProjectFilesAsync(NewProjectRequest request, ProjectTemplate? template = null)
    {
        template ??= Templates.FirstOrDefault(t => t.Id == request.TemplateId)
            ?? throw new ArgumentException($"Unknown template '{request.TemplateId}'.", nameof(request));
        var target = Path.Combine(NewProjectValidation.NormalizeLocation(request.Location) ?? request.Location.Trim(), request.Name);
        var check = NewProjectValidation.ValidateName(request.Name);
        if (check.Success)
            check = NewProjectValidation.ValidateLocation(request.Location, request.Name, template, _fileSystem);
        string? projectFile = null;
        var reason = check.Success ? CreateFiles(template, request.Name, target, out projectFile) : NewProjectValidation.MessageText(check);
        if (reason is null)
            return projectFile;
        await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.CreateFailed", [target, reason]));
        return null;
    }

    /// <summary>
    /// IDE-PM-023: writes every template file into <paramref name="target"/>; null on success, else the OS reason after
    /// removing every file and folder this call created.
    /// </summary>
    private string? CreateFiles(ProjectTemplate template, string name, string target, out string? projectFile)
    {
        projectFile = null;
        var folders = new List<string>(); // created by this call, outermost first
        for (var d = target; d is not null && !_fileSystem.DirectoryExists(d); d = Path.GetDirectoryName(d))
            folders.Insert(0, d);
        var files = new List<string>();
        try
        {
            _fileSystem.CreateDirectory(target);
            foreach (var file in template.Files)
            {
                var path = Path.Combine(target, template.Expand(file.Target, name));
                var text = template.Expand(template.ReadSource(file.Source), name).Replace("\r", "", StringComparison.Ordinal);
                files.Add(path);
                if (path.EndsWith(".pproj", StringComparison.OrdinalIgnoreCase))
                {
                    var parsed = ProjectSerializer.ParseProject(path, text);
                    if (parsed.Value is not { } project || !parsed.Diagnostics.IsEmpty)
                        throw new InvalidDataException($"Template {template.Id}: {parsed.Diagnostics.FirstOrDefault()?.Message}");
                    var saved = ProjectFileAccess.Save(project, _fileSystem);
                    if (!saved.Success)
                        throw new IOException(saved.Args[^1]);
                    projectFile = path;
                }
                else if (path.EndsWith(".form", StringComparison.OrdinalIgnoreCase))
                {
                    AtomicFileWriter.Write(_fileSystem, path, FileEncodings.Encode(text, FileEncoding.Utf8));
                }
                else
                {
                    AtomicFileWriter.Write(_fileSystem, path, SourceBytes(text));
                }
            }

            return projectFile is null ? throw new InvalidDataException($"Template {template.Id} has no project file.") : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            RollBack(files, folders);
            return e.Message;
        }
    }

    private void RollBack(List<string> files, List<string> folders)
    {
        try
        {
            foreach (var file in files)
                _fileSystem.DeleteFile(file);
            for (var i = folders.Count - 1; i >= 0; i--)
                _fileSystem.DeleteDirectory(folders[i]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogCategory, "new project not fully removed", e);
        }
    }

    /// <summary>The first line after the first <c>begin</c> token, at its first non-blank character (IDE-PM-023).</summary>
    private static TextPosition CaretAfterBegin(ITextBuffer buffer)
    {
        var text = buffer.Text;
        var begins = Lexer.Tokenize(SourceText.From(@"C:\Untitled.pas", text)).Where(t => t.Kind == SyntaxKind.BeginKeyword).Take(1).ToList();
        if (begins.Count == 0)
            return new TextPosition(1, 1);
        var line = buffer.GetPosition(begins[0].Span.Start).Line + 1;
        if (line > buffer.LineCount)
            return new TextPosition(line - 1, 1);
        var start = buffer.GetOffset(line, 1);
        var indent = 0;
        while (start + indent < text.Length && text[start + indent] is ' ' or '\t')
            indent++;
        return new TextPosition(line, indent + 1);
    }

    private bool IsMainSource(string? path) =>
        _project is not null && path is not null && string.Equals(path, Resolve(_project.MainSource), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>file.newProject</c> (dialogs.md §3): the dialog, then <see cref="CreateProjectAsync"/>; after a failure the
    /// dialog opens again with the same values (UI-DLG-031). The location used is remembered
    /// (<c>dialogs.NewProject.lastLocation</c>).
    /// </summary>
    private Task NewProjectAsync() => RunNewProjectDialogAsync(CreateProjectAsync);

    /// <summary>
    /// The New Project dialog (dialogs.md §3) with <paramref name="create"/> run on its result; after a failure the dialog
    /// opens again with the same values. Also behind <c>project.addNewProject</c> (IDE-PM-165).
    /// </summary>
    internal async Task RunNewProjectDialogAsync(Func<NewProjectRequest, Task<NewProjectOutcome>> create)
    {
        NewProjectRequest? previous = null;
        while (true)
        {
            var location = _layout.GetDialogValue(NewProjectViewModel.DialogName, "lastLocation") ?? _prompts.DefaultProjectsDirectory;
            var dialog = new NewProjectViewModel(Templates, _fileSystem, _clock, location, previous,
                current => _prompts.PickFolderAsync("Location", current));
            if (await _dialogs.ShowAsync(dialog) is not { } request)
                return;
            _layout.SetDialogValue(NewProjectViewModel.DialogName, "lastLocation", request.Location);
            if (await create(request) != NewProjectOutcome.Failed)
                return;
            previous = request;
        }
    }

    private async Task PickAndOpenAsync()
    {
        if (await _prompts.OpenProjectAsync() is { } path)
            await OpenAsync(path);
    }

    /// <summary>IDE-PM-012 beyond the serializer's checks.</summary>
    internal static OperationResult Validate(Project project)
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

        if (project is null)
            _projectStamp = null;
        FileChanges?.WatchProjectFile(project?.FilePath, project is null ? null : CheckProjectFileAsync);
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
            if (string.IsNullOrEmpty(file.Path))
            {
                // Lead decision: ignored here, kept in the Project (and so in the file on the next save).
                _log.Warning(LogCategory, $"{project.FilePath}: file entry with an empty path ignored");
                continue;
            }

            if (!seen.Add(Resolve(file.Path)))
            {
                _log.Warning(LogCategory, $"{project.FilePath}: duplicate file entry '{file.Path}' ignored");
                continue;
            }

            files.Add(new ProjectFileItem(file.Path, file.Form, file.EffectiveKind));
        }

        return new ProjectModel(project.FilePath, project.Directory, project.Name, project.Language, project.AppType,
            project.MainSource, files, project.ConfigurationNames, project.ActiveConfiguration, project.Platforms,
            ProjectOptions.GetEffectivePlatform(project), isModified: false);
    }

    /// <summary>A project path ('/', relative to the project directory or absolute) as an absolute path.</summary>
    private string Resolve(string projectPath) =>
        Path.GetFullPath(Path.Combine(_project!.Directory, projectPath.Replace('/', Path.DirectorySeparatorChar)));

    private bool IsProjectFile(DocumentViewModel document) =>
        string.Equals(document.FilePath, _project?.FilePath, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> OpenDocumentAsync(string path)
    {
        var result = await _documents.OpenAsync(path);
        if (result.NeedsMessage)
            await _dialogs.ShowMessageAsync(new MessageRequest(result.MessageId!, result.Args));
        return result.Success;
    }

    private sealed class Handler(Func<string?, bool> canExecute, Func<string?, Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument) => execute(argument);
    }
}
