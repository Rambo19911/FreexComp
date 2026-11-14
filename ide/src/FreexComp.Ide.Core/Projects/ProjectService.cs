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

    public IReadOnlyList<ProjectTemplate> Templates { get; }

    public event EventHandler? ProjectChanged;

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        commands.RegisterHandler("file.newProject", new Handler(_ => Templates.Count > 0, _ => NewProjectAsync()));
        commands.RegisterHandler("file.openProject", new Handler(_ => true, _ => PickAndOpenAsync()));
        commands.RegisterHandler("file.closeProject", new Handler(_ => Project is not null, _ => CloseAsync()));
        commands.RegisterHandler("project.viewSource", new Handler(_ => Project is not null, _ => ViewSourceAsync()));
        commands.RegisterHandler("project.addNewUnit", new Handler(_ => Project is not null, _ => AddNewUnitAsync()));
        commands.RegisterHandler("project.addExistingFile", new Handler(_ => Project is not null, _ => PickAndAddExistingAsync()));
        commands.RegisterHandler("project.removeFile", new Handler(_ => CanOperateOnFiles(), _ => RemoveFilesAsync(FileTargets())));
        commands.RegisterHandler("project.deleteFile", new Handler(_ => CanOperateOnFiles(), _ => DeleteFilesAsync(FileTargets())));
        commands.RegisterHandler("project.renameFile", new Handler(_ => RenameTarget() is not null, _ => RenameCommandAsync()));
    }

    /// <summary><c>project.addExistingFile</c> (IDE-PM-050): the multi-select picker, then <see cref="AddExistingFilesAsync"/>.</summary>
    private async Task PickAndAddExistingAsync()
    {
        var picked = await _prompts.AddExistingAsync();
        if (picked.Count > 0)
            await AddExistingFilesAsync(picked);
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
        try
        {
            AtomicFileWriter.Write(_fileSystem, path, SourceBytes($"unit {name};\n\ninterface\n\nimplementation\n\nend.\n"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.SaveFailed", [path, e.Message]));
            return false;
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
        else if (!opened.Success)
            await _dialogs.ShowMessageAsync(new MessageRequest(opened.MessageId!, opened.Args));
        return true;
    }

    public async Task<int> AddExistingFilesAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (_project is null)
            return 0;
        var units = UnitNames(_project);
        var files = FilesForMutation(_project).ToBuilder();
        var added = new List<string>();
        var addedFull = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.DuplicateUnitName", [unitName])); // IDE-PM-031
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
    public Func<string, bool>? RenameInPlace { get; set; }

    public event EventHandler<string>? FileRenamed;

    /// <summary>The one <c>files</c> entry (not a form) <c>project.renameFile</c> acts on; null when there is none.</summary>
    private string? RenameTarget() =>
        FileTargets() is [var target] && FindEntry(target) is not null ? target : null;

    /// <summary>The <c>files</c> entry whose path resolves like <paramref name="projectPath"/>.</summary>
    private ProjectFile? FindEntry(string projectPath) =>
        FilesForMutation(_project!).FirstOrDefault(f => !string.IsNullOrEmpty(f.Path)
            && string.Equals(Resolve(f.Path), Resolve(projectPath), StringComparison.OrdinalIgnoreCase));

    /// <summary><c>project.renameFile</c>: in place inside the tree, else the <c>RenameFile</c> dialog (UI-DLG-070).</summary>
    private async Task RenameCommandAsync()
    {
        if (RenameTarget() is not { } target)
            return;
        if (SelectedFiles?.Invoke() is not null && RenameInPlace?.Invoke(target) == true)
            return;
        var dialog = new RenameFileViewModel(Path.GetFileName(Resolve(target)), name => ValidateRename(target, name),
            (name, update) => RenameFileAsync(target, name, update));
        await _dialogs.ShowAsync(dialog);
    }

    public OperationResult ValidateRename(string projectPath, string newName)
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
        if (_project is null || FindEntry(projectPath) is not { } entry || !ValidateRename(projectPath, newName).Success)
            return false;
        var oldPath = Resolve(projectPath);
        var oldName = Path.GetFileName(oldPath);
        if (newName == oldName)
            return true;
        var newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, newName);
        var newBase = Path.GetFileNameWithoutExtension(newName);
        var document = _documents.Documents.OfType<TextDocumentViewModel>()
            .FirstOrDefault(d => string.Equals(d.FilePath, oldPath, StringComparison.OrdinalIgnoreCase));

        // (1) A modified open document is saved first; a failure (reported by the save) aborts.
        if (document is { IsModified: true } && !await _documents.SaveAsync(document))
            return false;

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
        var headingUpdated = false;
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
            undo.Push(() => AtomicFileWriter.Write(_fileSystem, original.FilePath, projectBytes)); // the exact bytes back

            // (5)-(6) heading and {$FORM} in the buffer or the file; the open document follows the new path.
            if (IsUnitFile(oldName))
                headingUpdated = await UpdateUnitTextAsync(document, newPath, Path.GetFileNameWithoutExtension(oldName), newBase,
                    oldForm is null ? null : (Path.GetFileName(oldForm), Path.GetFileName(newForm!)), updateHeading);
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

            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.RenameFailed", [oldPath, e.Message]));
            return false;
        }

        SetProject(renamed);
        FileRenamed?.Invoke(this, Rename(projectPath, newName));
        if (headingUpdated)
            _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_RenameUnitNote, oldName, newName), StatusDuration);
        return true;
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
        var saved = ProjectFileAccess.Save(project, _fileSystem);
        if (!saved.Success)
            throw new IOException(saved.Args[^1]);
    }

    /// <summary>
    /// IDE-PM-071 step 5: the heading identifier (first tokens <c>unit</c>/<c>program</c>/<c>library</c>/<c>package</c>
    /// + identifier) and the file name in <c>{$FORM}</c> directive trivia, found with the compiler lexer, replaced in the
    /// open buffer (then saved under the new path) or in the renamed file. True when the heading was replaced.
    /// </summary>
    private async Task<bool> UpdateUnitTextAsync(TextDocumentViewModel? document, string path, string oldBase, string newBase,
        (string Old, string New)? form, bool updateHeading)
    {
        EncodingDetection? file = null;
        var text = document?.Buffer.Text ?? (file = EncodingDetector.Detect(_fileSystem.ReadAllBytes(path))).Text;
        var edits = new List<TextEdit>();
        var headingUpdated = false;
        var tokens = Lexer.Tokenize(SourceText.From(path, text));
        if (updateHeading && tokens.Length > 1
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
        var saved = ProjectFileAccess.Save(project, _fileSystem);
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

        var target = Path.Combine(NewProjectValidation.NormalizeLocation(request.Location) ?? request.Location.Trim(), request.Name);
        var check = NewProjectValidation.ValidateName(request.Name);
        if (check.Success)
            check = NewProjectValidation.ValidateLocation(request.Location, request.Name, template, _fileSystem);
        string? projectFile = null;
        var reason = check.Success ? CreateFiles(template, request.Name, target, out projectFile) : NewProjectValidation.MessageText(check);
        if (reason is not null)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.CreateFailed", [target, reason]));
            return NewProjectOutcome.Failed;
        }

        if (!await OpenAsync(projectFile!))
            return NewProjectOutcome.Failed; // reported by the open
        if (_documents.ActiveDocument is TextDocumentViewModel main && IsMainSource(main.FilePath))
            main.ViewState.Caret = CaretAfterBegin(main.Buffer);
        return NewProjectOutcome.Created;
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
                var path = Path.Combine(target, ProjectTemplate.Expand(file.Target, name));
                var text = ProjectTemplate.Expand(template.ReadSource(file.Source), name).Replace("\r", "", StringComparison.Ordinal);
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
    private async Task NewProjectAsync()
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
            if (await CreateProjectAsync(request) != NewProjectOutcome.Failed)
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
