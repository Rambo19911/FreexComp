using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ToolWindows;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>How <see cref="ProjectManagerViewModel.Select"/> changes the selection (UI-TW-015).</summary>
public enum ProjectSelectMode
{
    /// <summary>Plain click: the row alone.</summary>
    Replace,

    /// <summary>Ctrl+click: adds or removes the row.</summary>
    Toggle,
}

/// <summary>
/// The Project Manager tool window (tool-windows.md §2, project-manager.md §6): the nested tree built from
/// <see cref="ProjectModel"/>, modified and active-document markers, missing files with their info bar, the filter, the
/// selection and the context menus. Handles <c>projectManager.open</c>, <c>.collapseAll</c> and <c>.syncWithEditor</c>.
/// UI thread only.
/// </summary>
public sealed class ProjectManagerViewModel : ToolWindowViewModel, ICommandTarget
{
    public const string ToolName = "ProjectManager";
    public const string MissingFilesKind = "MissingFiles";
    public const string UnlistedFilesKind = "UnlistedFiles";
    public const string RootKey = "$project";
    public const string ExternalKey = "$external/";

    private const string LogCategory = "project";

    private static readonly string?[] RootMenu =
    [
        "project.build", "project.rebuild", "project.clean", null, "run.start", "run.startWithoutDebugging", null,
        "project.dependencies", null, // UI-TW-129
        "project.addNewUnit", "project.addNewForm", "project.addNewFile", "project.addExistingFile", "project.newFolder", null,
        "project.viewSource", "file.showInExplorer", "file.openInTerminal", "project.openOutputFolder", "file.copyPath", null,
        "project.addExistingProject", "project.addNewProject", "project.removeFromGroup", "project.moveProjectUp", "project.moveProjectDown", null, // UI-TW-129
        "file.closeProject", null, "project.options",
    ];

    private static readonly string?[] FolderMenu =
    [
        "project.addNewUnit", "project.addNewForm", "project.addNewFile", "project.addExistingFile", "project.newFolder", null,
        "project.removeFile", null, "file.showInExplorer", "file.openInTerminal", "file.copyPath",
    ];

    private static readonly string?[] FileMenu =
    [
        "projectManager.open", "projectManager.openForm", null, "file.save", null, "project.renameFile", "project.moveFile",
        "project.removeFile", "project.deleteFile", null, "file.showInExplorer", "file.openInTerminal", "file.copyPath",
    ];

    private static readonly string?[] FilesMenu =
        ["projectManager.open", null, "project.moveFile", "project.removeFile", "project.deleteFile"];

    private static readonly string?[] EmptyAreaMenu =
    [
        "project.addNewUnit", "project.addNewForm", "project.addExistingFile", null, "projectManager.collapseAll", null,
        "project.addExistingProject", "project.addNewProject", // UI-TW-129
    ];

    // UI-TW-129: rows of the other members of a project group.
    private static readonly string?[] ListedRootMenu =
    [
        "project.setActive", null, "project.build", "project.rebuild", "project.clean", null, "project.dependencies", null,
        "project.removeFromGroup", "project.moveProjectUp", "project.moveProjectDown", null,
        "file.showInExplorer", "file.openInTerminal", "file.copyPath",
    ];

    private static readonly string?[] UnavailableRootMenu = ["project.removeFromGroup", null, "file.showInExplorer", "file.copyPath"];

    private static readonly string?[] ListedFileMenu =
        ["projectManager.open", null, "file.showInExplorer", "file.openInTerminal", "file.copyPath"];

    private static readonly string?[] ListedFolderMenu = ["file.showInExplorer", "file.openInTerminal", "file.copyPath"];

    private static readonly string?[] ListedFilesMenu = ["projectManager.open"];

    private readonly IProjectService _projects;
    private readonly IDocumentService _documents;
    private readonly IFileSystem _fileSystem;
    private readonly IDialogService _dialogs;
    private readonly ICommandService _commands;
    private readonly ILog _log;
    private readonly int _implementedPhase;
    private readonly List<ProjectNodeViewModel> _selection = [];
    private readonly HashSet<DocumentViewModel> _watched = [];
    private Dictionary<string, bool>? _expansionBeforeFilter;
    private string _appliedFilter = "";
    private InfoBarViewModel? _missingFilesBar;
    private int _missingCheck;
    private bool _isCheckingMissingFiles;
    private readonly List<string> _emptyFolders = []; // UI-TW-114 folder keys ("lib/"), ordinal order (IDE-PM-113)
    private readonly List<string> _ignoredFiles = []; // project-relative paths with '/', ordinal order (IDE-PM-148)
    private readonly HashSet<string> _acknowledgedUnlisted = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _unlisted = [];
    private InfoBarViewModel? _unlistedFilesBar;
    private string? _stateProject;
    private IProjectGroupService? _group;

    public ProjectManagerViewModel(IProjectService projects, IDocumentService documents, IFileSystem fileSystem, IDialogService dialogs,
        ICommandService commands, ILog log, int implementedPhase)
    {
        _projects = projects;
        _documents = documents;
        _fileSystem = fileSystem;
        _dialogs = dialogs;
        _commands = commands;
        _log = log;
        _implementedPhase = implementedPhase;
        projects.ProjectChanged += (_, _) => Rebuild();
        projects.FilesAdded += (_, paths) => SelectAdded(paths);
        projects.FileRenamed += (_, path) => SelectAdded([path]);
        projects.PendingRenameChanged += (_, _) => UpdateBusyRow();
        ((INotifyCollectionChanged)documents.Documents).CollectionChanged += (_, _) => UpdateDocumentMarkers();
        documents.ActiveDocumentChanged += (_, _) =>
        {
            UpdateDocumentMarkers();
            _commands.InvalidateCanExecute();
        };
        documents.DocumentSaved += (_, _) => UpdateDocumentMarkers();
    }

    /// <summary>The registration of the production tool window (tool-windows.md §1.2).</summary>
    public ToolWindowDefinition Definition =>
        new(ToolName, "Cmd_view_projectManager", ToolGroupId.Left, 1, () => this, CommandScope.ProjectManager);

    public override IReadOnlyList<ToolWindowToolBarItem> ToolBarCommands { get; } =
    [
        new("project.addNewUnit", "new-file"),
        new("project.addExistingFile", "open-folder"),
        new("projectManager.syncWithEditor", "sync"),
        new("projectManager.collapseAll", "collapse-all"),
    ];

    public override bool HasFilter => true;

    /// <summary>
    /// The project group (IDE-PM-151; set by the service graph): one root per member (UI-TW-125). Without it the tree has
    /// the open project's root only.
    /// </summary>
    public IProjectGroupService? Group
    {
        get => _group;
        set
        {
            _group = value;
            if (value is not null)
                value.Changed += (_, _) => Rebuild();
        }
    }

    /// <summary>The project roots, one per member of the group (UI-TW-100, UI-TW-125); empty without a project.</summary>
    public ObservableCollection<ProjectNodeViewModel> Roots { get; } = [];

    /// <summary>A saved group without any member is open: the empty state says <c>Msg.Group.Empty</c> (IDE-PM-154).</summary>
    public bool IsEmptyGroup => _group is { IsOpen: true, Members.Count: 0 };

    /// <summary>The root of the loaded project; null without one.</summary>
    private ProjectNodeViewModel? ActiveRoot => Roots.FirstOrDefault(r => !r.IsListed);

    /// <summary>The rows of the loaded project (rows of listed members are shown only, IDE-PM-163).</summary>
    private IEnumerable<ProjectNodeViewModel> ActiveNodes() => ActiveRoot?.SelfAndDescendants() ?? [];

    /// <summary>IDE-PM-160: the project file of the member whose root row is the single selection; null otherwise.</summary>
    public string? SelectedMemberPath => _selection is [{ Kind: ProjectNodeKind.Project } root] ? root.FullPath : null;

    /// <summary>IDE-PM-163: the selection has a row of a listed or unavailable member.</summary>
    public bool IsListedSelection => _selection.Any(n => n.IsListed);

    public bool HasProject => Roots.Count > 0;

    /// <summary>The empty state's links (UI-TW-013): New Project, Open Project.</summary>
    public IReadOnlyList<IdeCommand> EmptyLinks => IsEmptyGroup
        ? [.. new[] { "project.addExistingProject", "project.addNewProject" }.Where(FileOperationsDelivered).Select(_commands.Get)] // Msg.Group.Empty
        : [_commands.Get("file.newProject"), _commands.Get("file.openProject")];

    /// <summary>Selected rows in selection order (UI-TW-109).</summary>
    public IReadOnlyList<ProjectNodeViewModel> Selection => _selection;

    /// <summary>The <c>Msg.Project.MissingFiles*</c> info bar (UI-TW-107); null when nothing is missing.</summary>
    public InfoBarViewModel? MissingFilesBar
    {
        get => _missingFilesBar;
        private set => SetProperty(ref _missingFilesBar, value);
    }

    /// <summary>The <c>Msg.Project.UnlistedFiles*</c> info bar (UI-TW-123); null when no unit awaits a decision.</summary>
    public InfoBarViewModel? UnlistedFilesBar
    {
        get => _unlistedFilesBar;
        private set => SetProperty(ref _unlistedFilesBar, value);
    }

    /// <summary>The full paths of the unlisted units of the last detection that are not ignored (IDE-PM-145, IDE-PM-148).</summary>
    public IReadOnlyList<string> UnlistedFiles => _unlisted;

    /// <summary>The OS shell of the unlisted-files bar's <c>Show</c> (IDE-PM-147; set by the service graph).</summary>
    public IShellService? Shell { get; set; }

    /// <summary><c>session.json</c> <c>projectManager.folders</c>: the remembered empty folders (IDE-PM-113).</summary>
    public IReadOnlyList<string> EmptyFolders => _emptyFolders;

    /// <summary><c>session.json</c> <c>projectManager.ignoredFiles</c> (IDE-PM-148).</summary>
    public IReadOnlyList<string> IgnoredFiles => _ignoredFiles;

    /// <summary><see cref="EmptyFolders"/> or <see cref="IgnoredFiles"/> changed: the session is written.</summary>
    public event EventHandler? SessionStateChanged;

    /// <summary>A missing-file check runs in the background (IDE-PM-103).</summary>
    public bool IsCheckingMissingFiles
    {
        get => _isCheckingMissingFiles;
        private set => SetProperty(ref _isCheckingMissingFiles, value);
    }

    /// <summary>The view selects, expands ancestors of and scrolls to this row (sync, Show, rebuild).</summary>
    public event EventHandler? SelectionChanged;

    public void RegisterCommands()
    {
        _commands.RegisterHandler("projectManager.open", new Handler(_ => _selection.Count > 0, _ => OpenSelectionAsync()));
        if (_implementedPhase >= 5)
            _commands.RegisterHandler("projectManager.openForm", new Handler(_ => SelectedFormPath() is not null, _ => OpenFormAsync())); // UI-TW-108
        _commands.RegisterHandler("projectManager.collapseAll", new Handler(_ => HasProject, _ =>
        {
            CollapseAll();
            return Task.CompletedTask;
        }));
        // IDE-PM-110: the root or a folder row inside the project; the Idle rule is the command service's (IDE-PM-032).
        _commands.RegisterHandler("project.newFolder", new Handler(_ => NewFolderTarget() is not null, _ => NewFolderAsync()));
        // IDE-PM-127: enabled as a drag could start.
        _commands.RegisterHandler("project.moveFile", new Handler(_ => MoveCandidates().Count > 0, _ =>
        {
            ShowMoveToFolder();
            return Task.CompletedTask;
        }));
        _commands.RegisterHandler("projectManager.syncWithEditor", new Handler(_ => ActiveDocumentNode() is not null, _ =>
        {
            SyncWithEditor();
            return Task.CompletedTask;
        }));
    }

    /// <summary>The row with the UI-TW-114 key, else null.</summary>
    public ProjectNodeViewModel? Find(string key) =>
        Roots.SelectMany(r => r.SelfAndDescendants()).FirstOrDefault(n => n.Key == key);

    /// <summary>The rows a user sees, top to bottom: visible under the filter and inside expanded parents.</summary>
    public IReadOnlyList<ProjectNodeViewModel> VisibleRows()
    {
        var rows = new List<ProjectNodeViewModel>();
        void Add(ProjectNodeViewModel node)
        {
            if (!node.IsVisibleInTree)
                return;
            rows.Add(node);
            if (node.IsExpanded)
            {
                foreach (var child in node.Children)
                    Add(child);
            }
        }

        foreach (var root in Roots)
            Add(root);
        return rows;
    }

    /// <summary>Selects <paramref name="node"/>; folder and root rows never join a multi-selection (UI-TW-109).</summary>
    public void Select(ProjectNodeViewModel node, ProjectSelectMode mode = ProjectSelectMode.Replace)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (mode == ProjectSelectMode.Toggle && node.IsFile && _selection.All(n => n.IsFile))
        {
            if (!_selection.Remove(node))
                _selection.Add(node);
        }
        else
        {
            _selection.Clear();
            _selection.Add(node);
        }

        OnSelectionChanged();
    }

    /// <summary>The view reports the tree's selection (mouse, keyboard); the UI-TW-109 rule is applied.</summary>
    public void SetSelection(IReadOnlyList<ProjectNodeViewModel> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var result = nodes.Count > 1 && nodes.Any(n => !n.IsFile) ? [nodes[^1]] : nodes;
        if (result.SequenceEqual(_selection))
            return;
        _selection.Clear();
        _selection.AddRange(result);
        OnSelectionChanged();
    }

    /// <summary>The focus target of the tree: <c>edit.selectAll</c> (Ctrl+A) selects every visible file row (UI-TW-015).</summary>
    bool ICommandTarget.CanExecute(string commandId, string? argument) => commandId == SelectAllCommandId && HasProject;

    Task<bool> ICommandTarget.ExecuteAsync(string commandId, string? argument)
    {
        if (commandId != SelectAllCommandId || !HasProject)
            return Task.FromResult(false);
        SelectAllVisible();
        return Task.FromResult(true);
    }

    private const string SelectAllCommandId = "edit.selectAll";

    /// <summary>UI-TW-015 Ctrl+A: every visible row that may join a multi-selection, i.e. the file rows (UI-TW-109).</summary>
    public void SelectAllVisible()
    {
        var files = VisibleRows().Where(n => n.IsFile).ToList();
        if (files.Count == 0 || files.SequenceEqual(_selection))
            return;
        _selection.Clear();
        _selection.AddRange(files);
        OnSelectionChanged();
    }

    /// <summary>The folder of the selected row when exactly one folder inside the project is selected (IDE-PM-040).</summary>
    public string? SelectedFolderPath =>
        _selection is [{ Kind: ProjectNodeKind.Folder, IsListed: false } folder] && folder.Key != ExternalKey ? folder.FullPath : null;

    /// <summary>The in-place rename of a file row ended (committed or canceled); the view puts focus back on the tree.</summary>
    public event EventHandler? RenameEnded;

    private ProjectNodeViewModel? _busyRow;

    /// <summary>UI-TW-118: the row of the unit rename that waits for the language service shows the typed name and the busy ring.</summary>
    private void UpdateBusyRow()
    {
        _busyRow?.BusyName = null;
        _busyRow = _projects.PendingRename is { } pending ? Find(pending.ProjectPath) : null;
        _busyRow?.BusyName = _projects.PendingRename?.NewName;
    }

    /// <summary>
    /// UI-TW-112: the file row <paramref name="projectPath"/> gets an in-place editor (<see cref="ProjectNodeViewModel.Rename"/>)
    /// with the validation and rename of the service. False when there is no such row.
    /// </summary>
    public bool BeginRename(string projectPath, bool newFile = false)
    {
        if (_projects.PendingRename is not null || Find(projectPath) is not { Kind: ProjectNodeKind.File } node)
            return false;
        var rename = new RenameFileViewModel(node.Name, name => _projects.ValidateRename(projectPath, name, newFile),
            (name, update) => _projects.RenameFileAsync(projectPath, name, update));
        rename.CloseRequested += (_, _) =>
        {
            node.Rename = null;
            RenameEnded?.Invoke(this, EventArgs.Empty);
        };
        node.Rename = rename;
        return true;
    }

    /// <summary>IDE-PM-110: the directory and key prefix a new folder goes into; null when the selection allows none.</summary>
    private (string Directory, string KeyPrefix)? NewFolderTarget() => _selection switch
    {
        [{ Kind: ProjectNodeKind.Project, IsListed: false }] when _projects.Project is { } project => (project.Directory, ""),
        [{ Kind: ProjectNodeKind.Folder, IsListed: false } folder] when folder.Key != ExternalKey => (folder.FullPath, folder.Key),
        _ => null,
    };

    /// <summary>
    /// <c>project.newFolder</c> (IDE-PM-111): creates <c>NewFolder&lt;N&gt;</c> in the selected root or folder, remembers
    /// it as an empty folder, selects its row and starts the in-place naming (UI-TW-116). A failure shows
    /// <c>Msg.Project.NewFolderFailed</c> and changes nothing. True when the folder exists afterwards.
    /// </summary>
    public async Task<bool> NewFolderAsync()
    {
        if (NewFolderTarget() is not var (directory, prefix))
            return false;
        var name = FolderNames.FirstFree(_fileSystem, directory, "NewFolder");
        var path = Path.Combine(directory, name);
        try
        {
            _fileSystem.CreateDirectory(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.NewFolderFailed", [path, e.Message]));
            return false;
        }

        var key = prefix + name + "/";
        RememberFolder(key);
        Rebuild();
        if (Find(key) is not { } node)
            return true;
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            parent.IsExpanded = true;
        Select(node);
        BeginFolderNaming(node);
        return true;
    }

    /// <summary>UI-TW-116 / IDE-PM-112: the in-place editor of a just-created folder row, pre-filled and fully selected.</summary>
    private void BeginFolderNaming(ProjectNodeViewModel node)
    {
        var parentDirectory = Path.GetDirectoryName(node.FullPath)!;
        var key = node.Key;
        var rename = new RenameFileViewModel(node.Name,
            name => FolderNames.Validate(_fileSystem, parentDirectory, name, node.Name),
            (name, _) => RenameFolderAsync(key, FolderNames.Normalize(name)))
        {
            CancelInvalidOnFocusLoss = true,
        };
        rename.CloseRequested += (_, _) =>
        {
            node.Rename = null;
            RenameEnded?.Invoke(this, EventArgs.Empty);
        };
        node.Rename = rename;
    }

    /// <summary>IDE-PM-112: the committed name renames the directory; the remembered key follows.</summary>
    private async Task<bool> RenameFolderAsync(string key, string newName)
    {
        if (Find(key) is not { } node)
            return false;
        if (newName == node.Name)
            return true;
        var newPath = Path.Combine(Path.GetDirectoryName(node.FullPath)!, newName);
        try
        {
            if (string.Equals(newName, node.Name, StringComparison.OrdinalIgnoreCase))
            {
                // A change of letter case only goes through a temporary name.
                var temp = node.FullPath + ".~rename";
                _fileSystem.MoveDirectory(node.FullPath, temp);
                _fileSystem.MoveDirectory(temp, newPath);
            }
            else
            {
                _fileSystem.MoveDirectory(node.FullPath, newPath);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.NewFolderFailed", [newPath, e.Message]));
            return false;
        }

        var newKey = key[..(key.Length - node.Name.Length - 1)] + newName + "/";
        _emptyFolders.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        RememberFolder(newKey);
        Rebuild();
        if (Find(newKey) is { } renamed)
            Select(renamed);
        return true;
    }

    /// <summary>Adds an entry to <see cref="EmptyFolders"/> (no duplicates ignoring case, ordinal order).</summary>
    private void RememberFolder(string key)
    {
        if (_emptyFolders.Contains(key, StringComparer.OrdinalIgnoreCase))
            return;
        _emptyFolders.Add(key);
        _emptyFolders.Sort(StringComparer.Ordinal);
        SessionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>IDE-PM-115: one folder row inside the project with no project file in it or below it is selected.</summary>
    public bool CanRemoveSelectedFolder =>
        _selection is [{ Kind: ProjectNodeKind.Folder, IsListed: false } folder] && folder.Key != ExternalKey
        && !folder.SelfAndDescendants().Any(n => n.IsFile);

    /// <summary>
    /// IDE-PM-115 (<c>project.removeFile</c> on a folder row): forgets the folder and its sub-folders and deletes each of
    /// those directories, deepest first, only if it is empty; no confirmation. The parent row is selected.
    /// </summary>
    public Task RemoveSelectedFolderAsync()
    {
        if (!CanRemoveSelectedFolder)
            return Task.CompletedTask;
        var folder = _selection[0];
        var parent = folder.Parent;
        foreach (var node in folder.SelfAndDescendants().Reverse().ToList())
        {
            try
            {
                if (_fileSystem.DirectoryExists(node.FullPath) && _fileSystem.EnumerateFiles(node.FullPath, "*").Count == 0
                    && _fileSystem.EnumerateDirectories(node.FullPath).Count == 0)
                    _fileSystem.DeleteDirectory(node.FullPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log.Warning(LogCategory, $"{node.FullPath}: folder not deleted", e);
            }
        }

        _emptyFolders.RemoveAll(k => k.StartsWith(folder.Key, StringComparison.OrdinalIgnoreCase));
        SessionStateChanged?.Invoke(this, EventArgs.Empty);
        Rebuild();
        if (parent is not null && Find(parent.Key) is { } row)
            Select(row);
        return Task.CompletedTask;
    }

    /// <summary>
    /// IDE-PM-140: the directory <c>file.openInTerminal</c> acts on for the one selected node (root → project directory,
    /// folder → itself, file → its directory); null for the External group, a multi-selection or no selection.
    /// </summary>
    public string? SelectedNodeDirectory => _selection switch
    {
        [{ Key: ExternalKey }] => null,
        [{ Kind: ProjectNodeKind.Folder } folder] => folder.FullPath,
        [var node] => Path.GetDirectoryName(node.FullPath),
        _ => null,
    };

    /// <summary>phase8-delivery.md: the parts of card F09-054 that are no command follow the command they belong to.</summary>
    private bool FileOperationsDelivered(string commandId) =>
        Composition.Phase8Features.IsAvailable(commandId, _commands.Get(commandId).Definition.Phase, _implementedPhase);

    /// <summary>Whether the IDE state is Idle (IDE-PM-120; set by the service graph, true without one).</summary>
    public Func<bool>? IsIdle { get; set; }

    /// <summary>Shows a quick-pick popup (UI-TW-124; set by the service graph).</summary>
    public Action<Popups.QuickPickViewModel>? ShowQuickPick { get; set; }

    /// <summary>IDE-PM-120: a file row inside the project directory that exists on disk (not a form child, not under External).</summary>
    private static bool IsMovable(ProjectNodeViewModel node) =>
        node is { Kind: ProjectNodeKind.File, IsMissing: false, IsListed: false } && node.Parent?.Key != ExternalKey;

    /// <summary>
    /// IDE-PM-120 / UI-TW-119: a drag can start on <paramref name="node"/> — a movable file row, while the IDE is Idle,
    /// the filter box is empty and no row is being named.
    /// </summary>
    public bool CanStartDrag(ProjectNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        // UI-TW-127: a root row of a group with several members is dragged to change the order.
        var movable = node.Kind == ProjectNodeKind.Project
            ? _group is { Members.Count: > 1 } && FileOperationsDelivered("project.moveProjectUp")
            : IsMovable(node) && FileOperationsDelivered("project.moveFile");
        return movable && FilterText.Length == 0 && _appliedFilter.Length == 0
            && IsIdle?.Invoke() != false && _projects.PendingRename is null
            && !Roots.SelectMany(r => r.SelfAndDescendants()).Any(n => n.Rename is not null);
    }

    /// <summary>UI-TW-119: the rows a drag started on <paramref name="pressed"/> moves — the movable rows of the selection it belongs to, else itself.</summary>
    public IReadOnlyList<ProjectNodeViewModel> DragRows(ProjectNodeViewModel pressed)
    {
        ArgumentNullException.ThrowIfNull(pressed);
        return pressed.Kind != ProjectNodeKind.Project && _selection.Contains(pressed) ? [.. _selection.Where(IsMovable)] : [pressed];
    }

    /// <summary>
    /// IDE-PM-121 / UI-TW-121: the row that takes a drop over <paramref name="over"/> — the root or a folder row inside
    /// the project itself, the folder (or root) of a file row; null for the External group and outside the rows.
    /// </summary>
    public ProjectNodeViewModel? DropTargetFor(ProjectNodeViewModel? over) => over switch
    {
        null or { Key: ExternalKey } or { IsListed: true } => null, // UI-TW-127: nothing is dropped on another project
        { Kind: ProjectNodeKind.Project or ProjectNodeKind.Folder } => over,
        _ => DropTargetFor(over.Parent),
    };

    /// <summary>The row marked as the drop target while a drag lasts (UI-TW-121): exactly one at a time.</summary>
    public ProjectNodeViewModel? DropTarget
    {
        get => _dropTarget;
        set
        {
            if (_dropTarget == value)
                return;
            _dropTarget?.IsDropTarget = false;
            _dropTarget = value;
            _dropTarget?.IsDropTarget = true;
        }
    }

    private ProjectNodeViewModel? _dropTarget;
    private IReadOnlyList<ProjectNodeViewModel> _dragged = [];
    private ProjectNodeViewModel? _hovered;
    private IDisposable? _hoverTimer;

    /// <summary>UI-TW-121: a collapsed folder under the pointer expands after this long.</summary>
    public static readonly TimeSpan DragExpandDelay = TimeSpan.FromMilliseconds(700);

    /// <summary>The rows of the drag in progress (UI-TW-120: drawn at 50 % opacity); empty without one.</summary>
    public IReadOnlyList<ProjectNodeViewModel> DraggedRows => _dragged;

    /// <summary>UI-TW-119: the drag of <see cref="DragRows"/>(<paramref name="pressed"/>) starts; false when it cannot (IDE-PM-120).</summary>
    public bool BeginDrag(ProjectNodeViewModel pressed)
    {
        ArgumentNullException.ThrowIfNull(pressed);
        EndDrag();
        if (!CanStartDrag(pressed))
            return false;
        _dragged = DragRows(pressed);
        foreach (var row in _dragged)
            row.IsDragged = true;
        return true;
    }

    /// <summary>
    /// UI-TW-121: the pointer of the drag is over the row <paramref name="over"/> (null: no row). Marks and returns the
    /// drop target; a collapsed folder that stays under the pointer for <see cref="DragExpandDelay"/> expands.
    /// </summary>
    public ProjectNodeViewModel? DragOver(ProjectNodeViewModel? over)
    {
        if (_dragged.Count == 0 || IsRootDrag)
            return null; // UI-TW-127: rows are no target while a root is dragged
        DropTarget = DropTargetFor(over);
        var hovered = over is { Kind: ProjectNodeKind.Folder, IsExpanded: false, Children.Count: > 0 } && over.Key != ExternalKey ? over : null;
        if (hovered != _hovered)
        {
            _hoverTimer?.Dispose();
            _hovered = hovered;
            _hoverTimer = hovered is null ? null : Clock?.Schedule(DragExpandDelay, () =>
            {
                if (_hovered == hovered)
                    hovered.IsExpanded = true;
                _hovered = null;
            });
        }

        return DropTarget;
    }

    /// <summary>The drag ends without a drop (Escape, release outside a target, UI-TW-119) or after <see cref="DropAsync"/>.</summary>
    public void EndDrag()
    {
        _hoverTimer?.Dispose();
        _hoverTimer = null;
        _hovered = null;
        DropTarget = null;
        InsertionGap = null;
        foreach (var row in _dragged)
            row.IsDragged = false;
        _dragged = [];
    }

    /// <summary>UI-TW-127: the drag in progress moves a root row (the order of the group).</summary>
    public bool IsRootDrag => _dragged is [{ Kind: ProjectNodeKind.Project }];

    /// <summary>
    /// UI-TW-127: the gap between root rows that takes the dragged root — 0 before the first root … <c>Roots.Count</c>
    /// after the last one; null while no root is dragged or the pointer is outside the tree.
    /// </summary>
    public int? InsertionGap { get; private set; }

    /// <summary>The automation name of the insertion line: "before &lt;project&gt;" / "after &lt;project&gt;"; null without a gap.</summary>
    public string? InsertionName => InsertionGap is not { } gap || Roots.Count == 0 ? null
        : gap < Roots.Count ? string.Format(CultureInfo.CurrentCulture, Strings.ProjectManager_InsertBefore, Roots[gap].Name)
        : string.Format(CultureInfo.CurrentCulture, Strings.ProjectManager_InsertAfter, Roots[^1].Name);

    /// <summary>UI-TW-127: the pointer of a root drag is nearest to <paramref name="gap"/> (null: outside the tree).</summary>
    public void DragOverGap(int? gap) => InsertionGap = IsRootDrag && gap is >= 0 && gap <= Roots.Count ? gap : null;

    /// <summary>The pointer was released: the dragged rows move into the marked target, if any (IDE-PM-121). True when moved.</summary>
    public async Task<bool> DropAsync()
    {
        var rows = _dragged;
        var target = DropTarget;
        var gap = InsertionGap;
        var rootDrag = IsRootDrag;
        EndDrag();
        if (rootDrag)
        {
            // IDE-PM-168: the root takes the place at the gap; its own position (the gap above or below it) does nothing.
            var from = Roots.IndexOf(rows[0]);
            return _group is not null && gap is { } at && from >= 0 && at != from && at != from + 1
                && await _group.MoveToAsync(rows[0].FullPath, at > from ? at - 1 : at);
        }

        return rows.Count > 0 && target is not null && await MoveAsync(rows, target);
    }

    /// <summary>
    /// IDE-PM-121…126: moves <paramref name="rows"/> into the directory of the root or folder row
    /// <paramref name="target"/>. A source folder left without project files stays as an empty folder; the target is
    /// expanded and the moved rows are selected. False when nothing was moved (no-op, refusal or failure).
    /// </summary>
    public async Task<bool> MoveAsync(IReadOnlyList<ProjectNodeViewModel> rows, ProjectNodeViewModel target)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(target);
        if (_projects.Project is not { } project || DropTargetFor(target) != target)
            return false;
        var directory = target.Kind == ProjectNodeKind.Project ? project.Directory : target.FullPath;
        var targetKey = target.Key;
        var sources = rows.Select(r => r.Parent).OfType<ProjectNodeViewModel>().Where(p => p.Kind == ProjectNodeKind.Folder)
            .Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var moved = await _projects.MoveFilesAsync([.. rows.Where(IsMovable).Select(r => r.Key)], directory);
        if (moved.Count == 0)
            return false;

        // IDE-PM-126: an emptied source folder does not vanish under the pointer.
        var emptied = sources.Where(key => Find(key) is null).ToList();
        foreach (var key in emptied)
            RememberFolder(key);
        if (emptied.Count > 0)
            Rebuild();
        for (var node = Find(targetKey); node is not null; node = node.Parent)
            node.IsExpanded = true;
        SelectAdded(moved);
        return true;
    }

    /// <summary>The rows <c>project.moveFile</c> moves: the movable rows of a selection of file rows; none while a drag could not start.</summary>
    private IReadOnlyList<ProjectNodeViewModel> MoveCandidates() =>
        _selection.Count > 0 && _selection.All(n => n.IsFile) && _selection.FirstOrDefault(IsMovable) is { } first && CanStartDrag(first)
            ? [.. _selection.Where(IsMovable)]
            : [];

    /// <summary>
    /// <c>project.moveFile</c> (IDE-PM-127, UI-TW-124): the quick pick of the project root and every folder in tree order
    /// (without the folder all selected files are in); the chosen one receives the selection.
    /// </summary>
    public void ShowMoveToFolder()
    {
        var rows = MoveCandidates();
        if (rows.Count == 0 || ActiveRoot is not { } activeRoot)
            return;
        var common = rows.Select(r => r.Parent).Distinct().ToList() is [var only] ? only : null;
        var targets = activeRoot.SelfAndDescendants()
            .Where(n => n.Kind is ProjectNodeKind.Project or ProjectNodeKind.Folder && n.Key != ExternalKey && n != common)
            .Select(n => (n.Key, Label: n.Kind == ProjectNodeKind.Project ? n.Name : n.Key.TrimEnd('/')))
            .ToList();
        ShowQuickPick?.Invoke(new Popups.MoveToFolderViewModel(targets, key => Find(key) is { } target ? MoveAsync(rows, target) : Task.FromResult(false)));
    }

    /// <summary>The project paths (row keys) of the selected file and form rows (IDE-PM-060).</summary>
    public IReadOnlyList<string> SelectedFilePaths => [.. _selection.Where(n => n.IsFile && !n.IsListed).Select(n => n.Key)];

    /// <summary>
    /// tool-windows.md §2: the full paths of the selected rows (root = project file, folder = directory, file or form =
    /// the file) for <c>file.save</c>, <c>file.copyPath</c>, <c>file.showInExplorer</c>; the External group has no path.
    /// </summary>
    public IReadOnlyList<string> SelectedNodePaths => [.. _selection.Where(n => n.Key != ExternalKey).Select(n => n.FullPath)];

    /// <summary>IDE-PM-033: the rows of added files are selected, their ancestors expanded.</summary>
    private void SelectAdded(IReadOnlyList<string> paths)
    {
        var nodes = paths.Select(Find).OfType<ProjectNodeViewModel>().ToList();
        if (nodes.Count == 0)
            return;
        foreach (var node in nodes)
        {
            for (var parent = node.Parent; parent is not null; parent = parent.Parent)
                parent.IsExpanded = true;
        }

        _selection.Clear();
        _selection.AddRange(nodes);
        OnSelectionChanged();
    }

    public void ClearSelection()
    {
        _selection.Clear();
        OnSelectionChanged();
    }

    /// <summary>The context menu for the current selection (UI-TW-014); later-phase items are omitted (UI-MENU-042).</summary>
    public IReadOnlyList<MenuItemModel> ContextMenu()
    {
        var ids = _selection switch
        {
            [] => EmptyAreaMenu,
            [{ Kind: ProjectNodeKind.Project, IsUnavailable: true }] => UnavailableRootMenu,
            [{ Kind: ProjectNodeKind.Project, IsListed: true }] => ListedRootMenu,
            [{ Kind: ProjectNodeKind.Folder, IsListed: true }] => ListedFolderMenu,
            [{ IsListed: true }] => ListedFileMenu,
            _ when IsListedSelection => ListedFilesMenu,
            [{ Kind: ProjectNodeKind.Project }] => RootMenu,
            [{ Kind: ProjectNodeKind.Folder }] => FolderMenu,
            [_] => FileMenu,
            _ => FilesMenu,
        };
        var items = new List<MenuItemModel>();
        var pendingSeparator = false;
        foreach (var id in ids)
        {
            if (id is null)
            {
                pendingSeparator = items.Count > 0;
                continue;
            }

            var command = _commands.Get(id);
            if (!Composition.Phase8Features.IsAvailable(id, command.Definition.Phase, _implementedPhase))
                continue;
            if (id == "projectManager.openForm" && SelectedFormPath() is null)
                continue; // only for units with a form and .form rows (tool-windows.md §2)
            if (id == "project.removeFile" && ids == FolderMenu && !FileOperationsDelivered("project.newFolder"))
                continue; // removing an empty folder comes with New Folder (IDE-PM-115)
            if (pendingSeparator)
                items.Add(MenuItemModel.Separator());
            pendingSeparator = false;
            // IDE-PM-170: Build, Rebuild and Clean of a listed root act on that member, not on the loaded project.
            items.Add(ids == ListedRootMenu && id is "project.build" or "project.rebuild" or "project.clean"
                ? MenuItemModel.ForCommandOn(MenuLabels.For(new CommandMenuNode(id)), command, _selection[0].FullPath!)
                : MenuItemModel.ForCommand(MenuLabels.For(new CommandMenuNode(id)), command, null, MenuToggleKind.None));
        }

        return items;
    }

    /// <summary>
    /// UI-TW-108 default action of the selection: files open (the last one active, focus to the document area); a
    /// single folder or root row toggles its expansion (the root's Project Options action arrives in P3).
    /// </summary>
    public async Task OpenSelectionAsync()
    {
        // IDE-PM-160: the default action of a listed root activates that project (an unavailable one does nothing).
        if (_selection is [{ Kind: ProjectNodeKind.Project, IsListed: true } listed])
        {
            await _commands.ExecuteAsync("project.setActive", listed.FullPath);
            return;
        }

        // IDE-PM-102 / I-109: from P3 Enter on the root row opens Project Options.
        if (_selection is [{ Kind: ProjectNodeKind.Project }] && _implementedPhase >= 3)
        {
            await _commands.ExecuteAsync("project.options");
            return;
        }

        if (_selection is [{ IsFile: false } container])
        {
            container.IsExpanded = !container.IsExpanded;
            return;
        }

        var opened = false;
        foreach (var node in _selection.Where(n => n.IsFile).ToList())
        {
            // UI-TW-108: from P5 a newly opened unit with a form shows the view of designer.defaultView. The file of a
            // listed member opens as a document outside the project (IDE-PM-163: a unit with a form opens as code).
            var result = node.IsListed ? await _documents.OpenAsync(node.FullPath) : await _documents.OpenInDefaultViewAsync(node.FullPath);
            if (result.Success)
                opened = true;
            else if (result.NeedsMessage)
                await _dialogs.ShowMessageAsync(new MessageRequest(result.MessageId!, result.Args));
        }

        if (opened)
            ReturnFocusToDocument();
    }

    /// <summary>The form of the single selected row: a <c>.form</c> row, or the form child of a unit row; else null.</summary>
    private string? SelectedFormPath() => _selection switch
    {
        [{ Kind: ProjectNodeKind.Form, IsListed: false } form] => form.FullPath,
        [{ Kind: ProjectNodeKind.File, IsListed: false } file] => file.Children.FirstOrDefault(c => c.Kind == ProjectNodeKind.Form)?.FullPath,
        _ => null,
    };

    /// <summary><c>projectManager.openForm</c> (UI-TW-108, P5): the form document in its Design view (DSG-CS-080).</summary>
    private async Task OpenFormAsync()
    {
        if (SelectedFormPath() is not { } path)
            return;
        var result = await _documents.OpenAsync(path);
        if (result.Success)
            ReturnFocusToDocument();
        else if (result.NeedsMessage)
            await _dialogs.ShowMessageAsync(new MessageRequest(result.MessageId!, result.Args));
    }

    public void CollapseAll()
    {
        foreach (var node in Roots.SelectMany(r => r.SelfAndDescendants()))
            node.IsExpanded = false;
    }

    /// <summary>IDE-PM-101: expands the ancestors of the active document's row and selects it.</summary>
    public void SyncWithEditor()
    {
        if (ActiveDocumentNode() is not { } node)
            return;
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            parent.IsExpanded = true;
        Select(node);
    }

    /// <summary>Main-window activation re-checks the files on disk (UI-TW-107).</summary>
    public void OnWindowActivated()
    {
        _group?.Refresh(); // IDE-PM-152: the listed members are read again
        if (HasProject)
            _ = CheckMissingFilesAsync();
    }

    /// <summary>
    /// IDE-PM-103: <c>File.Exists</c> per file row on a background thread; the result is applied only when no newer check
    /// or rebuild started meanwhile.
    /// </summary>
    public Task CheckMissingFilesAsync()
    {
        var check = CheckMissingFilesCoreAsync();
        MissingFileChecks = MissingFileChecks.IsCompleted ? check : Task.WhenAll(MissingFileChecks, check);
        return check;
    }

    /// <summary>Completes once every missing-file check started so far has applied (or dropped) its result (I-210).</summary>
    public Task MissingFileChecks { get; private set; } = Task.CompletedTask;

    private async Task CheckMissingFilesCoreAsync()
    {
        var check = ++_missingCheck;
        var files = ActiveNodes().Where(n => n.IsFile).ToList();
        var scan = FileOperationsDelivered("project.newFolder") ? UnlistedScan.For(_projects, files, _emptyFolders, _ignoredFiles) : null;
        IsCheckingMissingFiles = true;
        bool[] exists;
        UnlistedResult? unlisted = null;
        try
        {
            (exists, unlisted) = await Task.Run(() => (files.Select(f => _fileSystem.FileExists(f.FullPath)).ToArray(), scan?.Run(_fileSystem)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogCategory, "missing-file check failed", e);
            exists = [.. files.Select(_ => true)];
        }

        if (check != _missingCheck)
            return; // stale
        for (var i = 0; i < files.Count; i++)
            files[i].IsMissing = !exists[i];
        UpdateMissingFilesBar();
        if (unlisted is not null)
            ApplyUnlisted(unlisted);
        IsCheckingMissingFiles = false;
    }

    /// <summary>The unlisted units found and the ignored entries that are still valid (IDE-PM-146, IDE-PM-148).</summary>
    private sealed record UnlistedResult(IReadOnlyList<string> Files, IReadOnlyList<string> Ignored);

    /// <summary>
    /// IDE-PM-145: the input of one detection, captured on the UI thread: the project directory, the directories to look
    /// in (the project directory, every directory inside it that holds a project file, the remembered empty folders —
    /// none below a directory whose name starts with <c>.</c>, is <c>obj</c> or the first segment of an output
    /// directory), the files of the project and the ignored list.
    /// </summary>
    private sealed record UnlistedScan(string ProjectDirectory, IReadOnlyList<string> Directories, HashSet<string> Members, IReadOnlyList<string> Ignored)
    {
        public static UnlistedScan? For(IProjectService projects, IReadOnlyList<ProjectNodeViewModel> files, IReadOnlyList<string> emptyFolders,
            IReadOnlyList<string> ignored)
        {
            if (projects.Project is not { } project)
                return null;
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "obj" };
            foreach (var configuration in projects.Definition is { } definition ? definition.ConfigurationNames.Select(n => ConfigurationEdits.Effective(definition, n)!) : [])
            {
                if (configuration.OutputDir.Split('/', '\\').FirstOrDefault(s => s.Length > 0) is { } first)
                    excluded.Add(first);
            }

            var directories = new List<string> { project.Directory };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { project.Directory };
            void Add(string directory)
            {
                var relative = Path.GetRelativePath(project.Directory, directory);
                if (relative == "." || Path.IsPathRooted(relative))
                    return;
                var segments = relative.Split(Path.DirectorySeparatorChar);
                if (segments.Any(s => s == ".." || s.StartsWith('.') || excluded.Contains(s)))
                    return;
                if (seen.Add(directory))
                    directories.Add(directory);
            }

            foreach (var file in files.Where(f => f.Kind == ProjectNodeKind.File))
                Add(Path.GetDirectoryName(file.FullPath)!);
            foreach (var key in emptyFolders)
                Add(Path.GetFullPath(Path.Combine(project.Directory, key.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar))));
            return new UnlistedScan(project.Directory, directories, files.Select(f => f.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase), [.. ignored]);
        }

        /// <summary>Background thread: <c>*.pas</c> directly in each directory that is not a project file.</summary>
        public UnlistedResult Run(IFileSystem fileSystem)
        {
            var found = new List<string>();
            foreach (var directory in Directories)
            {
                if (!fileSystem.DirectoryExists(directory))
                    continue;
                found.AddRange(fileSystem.EnumerateFiles(directory, "*.pas")
                    .Where(f => Path.GetExtension(f).Equals(".pas", StringComparison.OrdinalIgnoreCase) && !Members.Contains(f)));
            }

            // IDE-PM-148: an ignored entry stays while its file exists and is not a project member.
            var ignored = Ignored.Where(relative =>
            {
                var full = Path.GetFullPath(Path.Combine(ProjectDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
                return !Members.Contains(full) && fileSystem.FileExists(full);
            }).ToList();
            return new UnlistedResult(found, ignored);
        }
    }

    private string ToRelative(string fullPath) =>
        Path.GetRelativePath(_projects.Project!.Directory, fullPath).Replace('\\', '/');

    /// <summary>IDE-PM-146: the bar shows while a unit is unlisted that is neither ignored nor already answered.</summary>
    private void ApplyUnlisted(UnlistedResult result)
    {
        if (!result.Ignored.SequenceEqual(_ignoredFiles, StringComparer.Ordinal))
        {
            _ignoredFiles.Clear();
            _ignoredFiles.AddRange(result.Ignored);
            SessionStateChanged?.Invoke(this, EventArgs.Empty);
        }

        var ignored = _ignoredFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = result.Files.Where(f => !ignored.Contains(ToRelative(f))).ToList();
        _acknowledgedUnlisted.IntersectWith(files);
        var changed = !files.SequenceEqual(_unlisted, StringComparer.OrdinalIgnoreCase);
        _unlisted = files;
        if (!files.Any(f => !_acknowledgedUnlisted.Contains(f)))
        {
            UnlistedFilesBar = null;
            return;
        }

        if (UnlistedFilesBar is not null && !changed)
            return;
        UnlistedFilesBar = new InfoBarViewModel(
            UnlistedFilesKind,
            InfoBarSeverity.Info,
            files.Count == 1
                ? Strings.Msg_Project_UnlistedFilesOne
                : string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_UnlistedFilesMany, files.Count),
            [
                new InfoBarAction("AddToProject", Strings.InfoBar_Action_AddToProject, AddUnlistedAsync),
                new InfoBarAction("Show", Strings.Button_Show, () =>
                {
                    // IDE-PM-147: the OS file manager with the first unlisted file selected.
                    if (_unlisted.Count > 0)
                        Shell?.RevealFile(_unlisted[0]);
                    AnswerUnlisted();
                    return Task.CompletedTask;
                }),
                new InfoBarAction("Ignore", Strings.InfoBar_Action_Ignore, () =>
                {
                    IgnoreUnlisted();
                    return Task.CompletedTask;
                }),
            ],
            dismiss: null);
    }

    /// <summary>IDE-PM-147: each action removes the bar until a detection finds a file not listed in it.</summary>
    private void AnswerUnlisted()
    {
        _acknowledgedUnlisted.UnionWith(_unlisted);
        UnlistedFilesBar = null;
    }

    /// <summary>IDE-PM-147 <c>Add to Project</c>: every listed file as <c>project.addExistingFile</c> would add it.</summary>
    private async Task AddUnlistedAsync()
    {
        var files = _unlisted.ToList();
        AnswerUnlisted();
        await _projects.AddExistingFilesAsync(files, reportFirstDuplicateOnly: true);
    }

    /// <summary>IDE-PM-147 <c>Ignore</c>: every currently unlisted file goes to <see cref="IgnoredFiles"/>.</summary>
    private void IgnoreUnlisted()
    {
        _ignoredFiles.AddRange(_unlisted.Select(ToRelative).Where(r => !_ignoredFiles.Contains(r, StringComparer.OrdinalIgnoreCase)));
        _ignoredFiles.Sort(StringComparer.Ordinal);
        _unlisted = [];
        UnlistedFilesBar = null;
        SessionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnFilterApplied(string filter)
    {
        _appliedFilter = filter;
        if (filter.Length > 0 && _expansionBeforeFilter is null)
            _expansionBeforeFilter = ExpansionState();
        ApplyFilter();
        if (filter.Length == 0 && _expansionBeforeFilter is { } saved)
        {
            _expansionBeforeFilter = null;
            RestoreExpansion(saved);
        }
    }

    private void ApplyFilter()
    {
        foreach (var root in Roots)
            Filter(root);
    }

    /// <summary>UI-TW-009 / UI-TW-110: a row is shown when its name or a descendant matches; ancestors of matches expand.</summary>
    private bool Filter(ProjectNodeViewModel node)
    {
        var anyChild = false;
        foreach (var child in node.Children)
            anyChild |= Filter(child);

        var index = _appliedFilter.Length == 0 || node.Kind == ProjectNodeKind.Project
            ? -1
            : node.Name.IndexOf(_appliedFilter, StringComparison.OrdinalIgnoreCase);
        node.SetMatch(index, index < 0 ? 0 : _appliedFilter.Length);
        var visible = _appliedFilter.Length == 0 || index >= 0 || anyChild;
        node.IsVisibleInTree = visible;
        if (_appliedFilter.Length > 0 && anyChild)
            node.IsExpanded = true;
        return visible;
    }

    /// <summary>
    /// <c>session.json</c> <c>projectManager.expanded</c>: the keys of the expanded rows that have children (the state
    /// before a filter was typed while one is active).
    /// </summary>
    public IReadOnlyList<string> ExpandedKeys()
    {
        var state = _expansionBeforeFilter ?? ExpansionState();
        return [.. ActiveNodes()
            .Where(n => n.Children.Count > 0 && state.TryGetValue(n.Key, out var expanded) && expanded)
            .Select(n => n.Key)];
    }

    /// <summary>
    /// Session restore (UI-TW-012): rows with children are expanded exactly when listed; the row
    /// <paramref name="selectedKey"/> is selected when it exists. Unknown keys are ignored.
    /// </summary>
    public void RestoreSessionState(IReadOnlyCollection<string> expandedKeys, string? selectedKey)
    {
        ArgumentNullException.ThrowIfNull(expandedKeys);
        var expanded = expandedKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var node in ActiveNodes().Where(n => n.Children.Count > 0))
            node.IsExpanded = expanded.Contains(node.Key);
        if (selectedKey is not null && Find(selectedKey) is { } selected)
            Select(selected);
    }

    /// <summary>
    /// Session restore of <c>projectManager.folders</c> and <c>.ignoredFiles</c> (IDE-PM-113, IDE-PM-148), before
    /// <see cref="RestoreSessionState"/>: the tree is rebuilt with the folders that still exist.
    /// </summary>
    public void RestoreFolders(IReadOnlyCollection<string> folders, IReadOnlyCollection<string> ignoredFiles)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(ignoredFiles);
        _emptyFolders.Clear();
        foreach (var key in folders.Where(k => k.EndsWith('/')).Order(StringComparer.Ordinal))
        {
            if (!_emptyFolders.Contains(key, StringComparer.OrdinalIgnoreCase))
                _emptyFolders.Add(key);
        }

        _ignoredFiles.Clear();
        _ignoredFiles.AddRange(ignoredFiles.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal));
        if (_emptyFolders.Count > 0 || _ignoredFiles.Count > 0)
            Rebuild();
    }

    /// <summary>IDE-PM-113: entries whose directory is gone or outside the project directory are dropped.</summary>
    private void PruneEmptyFolders(ProjectModel project)
    {
        var removed = _emptyFolders.RemoveAll(key =>
        {
            var relative = key.TrimEnd('/');
            if (relative.Length == 0 || Path.IsPathRooted(relative) || relative.Split('/').Any(s => s is "" or "." or ".."))
                return true;
            return !_fileSystem.DirectoryExists(Path.Combine(project.Directory, relative.Replace('/', Path.DirectorySeparatorChar)));
        });
        if (removed > 0)
            SessionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private Dictionary<string, bool> ExpansionState() =>
        Roots.SelectMany(r => r.SelfAndDescendants()).ToDictionary(n => n.Key, n => n.IsExpanded, StringComparer.Ordinal);

    private void RestoreExpansion(Dictionary<string, bool> state)
    {
        foreach (var node in Roots.SelectMany(r => r.SelfAndDescendants()))
        {
            if (state.TryGetValue(node.Key, out var expanded))
                node.IsExpanded = expanded;
        }
    }

    /// <summary>IDE-PM-100: rebuilt from the model after each change, keeping expansion and selection by key.</summary>
    private void Rebuild()
    {
        var expansion = _expansionBeforeFilter ?? ExpansionState();
        var selected = _selection.Select(n => n.Key).ToList();
        Roots.Clear();
        _selection.Clear();
        _missingCheck++;
        IsCheckingMissingFiles = false;
        MissingFilesBar = null;
        if (!string.Equals(_projects.Project?.FilePath, _stateProject, StringComparison.OrdinalIgnoreCase))
        {
            // Another project (or none): its folders and ignored files come from its own session.
            _stateProject = _projects.Project?.FilePath;
            _emptyFolders.Clear();
            _ignoredFiles.Clear();
            _acknowledgedUnlisted.Clear();
            _unlisted = [];
            UnlistedFilesBar = null;
        }

        var members = _group?.Members ?? [];
        if (_projects.Project is { } project)
            PruneEmptyFolders(project);
        if (_projects.Project is { } open && !members.Any(m => IsLoaded(m, open)))
            AddRoot(BuildTree(open, announceActive: false), expansion); // no group, or one that does not list the project yet

        // UI-TW-125: one root per member, in group order.
        var keyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
        {
            if (_projects.Project is { } loaded && IsLoaded(member, loaded))
            {
                AddRoot(BuildTree(loaded, announceActive: members.Count > 1), expansion);
                continue;
            }

            // UI-TW-128: the key name is the project name; a later member of the same name gets its position appended.
            var keyName = keyNames.Add(member.Name) ? member.Name : $"{member.Name}#{Roots.Count + 1}";
            keyNames.Add(keyName);
            AddRoot(BuildListedTree(member, keyName), expansion);
        }

        if (Roots.Count > 0)
        {
            ApplyFilter();
            _selection.AddRange(selected.Select(Find).OfType<ProjectNodeViewModel>());
        }
        else
        {
            _expansionBeforeFilter = null;
        }

        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(IsEmptyGroup));
        UpdateDocumentMarkers();
        OnSelectionChanged();
        if (HasProject)
            _ = CheckMissingFilesAsync();
    }

    private static bool IsLoaded(ProjectGroupMember member, ProjectModel project) =>
        string.Equals(member.FilePath, project.FilePath, StringComparison.OrdinalIgnoreCase);

    private void AddRoot(ProjectNodeViewModel root, Dictionary<string, bool> expansion)
    {
        RestoreExpansionInto(root, expansion);
        Roots.Add(root);
    }

    private static void RestoreExpansionInto(ProjectNodeViewModel root, Dictionary<string, bool> state)
    {
        foreach (var node in root.SelfAndDescendants())
        {
            if (state.TryGetValue(node.Key, out var expanded))
                node.IsExpanded = expanded;
        }
    }

    /// <summary>UI-TW-100…103: folders from path segments, files outside the project under External (last).</summary>
    private ProjectNodeViewModel BuildTree(ProjectModel project, bool announceActive)
    {
        var badge = $"{project.ActiveConfiguration} · {project.Platforms.FirstOrDefault() ?? "win64"}";
        var library = project.AppType == "library";
        var root = new ProjectNodeViewModel(ProjectNodeKind.Project, RootKey, project.Name, project.FilePath, null)
        {
            IsLibrary = library,
            ToolTipText = library && _projects.Definition is { } definition && definition.Configurations.ContainsKey(project.ActiveConfiguration)
                ? FreexComp.Build.ProjectOptions.GetOutputPath(definition, project.ActiveConfiguration) // IDE-TGT-011, 017
                : null!,
            Badge = badge,
            IsExpanded = true,
            AnnounceActive = announceActive,
        };
        AddFileRows(root, project.Directory, [.. project.Files.Select(f => (f.Path, f.Form))], project.MainSource, "", _emptyFolders);
        return root;
    }

    /// <summary>
    /// UI-TW-125, 126, 128: the root of a listed member (collapsed, its own badge, its files below it with the key prefix
    /// <c>&lt;name&gt;:</c>) or of an unavailable one (no badge, no rows, the reason in the tooltip).
    /// </summary>
    private static ProjectNodeViewModel BuildListedTree(ProjectGroupMember member, string keyName)
    {
        var root = new ProjectNodeViewModel(ProjectNodeKind.Project, RootKey + ":" + keyName, member.Name, member.FilePath, null)
        {
            IsListed = true,
            IsUnavailable = !member.IsAvailable,
            IsLibrary = member.Definition?.AppType == "library",
            ToolTipText = member.IsAvailable ? null! : member.FilePath + "\n" + member.Reason,
            Badge = member.IsAvailable ? $"{member.Configuration} · {member.Platform}" : null,
        };
        if (member.Definition is not { } definition)
            return root;

        // As the loaded project's model (IDE-PM-012): the main source is listed, empty and repeated entries are not.
        var directory = Path.GetDirectoryName(member.FilePath)!;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(string Path, string? Form)>();
        foreach (var (path, form) in member.Files.Select(f => (f.Path, f.Form)).Prepend((definition.MainSource, null)))
        {
            if (string.IsNullOrEmpty(path))
                continue;
            var index = files.FindIndex(f => string.Equals(ResolveIn(directory, f.Path), ResolveIn(directory, path), StringComparison.OrdinalIgnoreCase));
            if (index < 0 && seen.Add(ResolveIn(directory, path)))
                files.Add((path, form));
            else if (index >= 0 && form is not null)
                files[index] = (files[index].Path, form); // the files entry of the main source carries its form
        }

        AddFileRows(root, directory, files, definition.MainSource, keyName + ":", []);
        return root;
    }

    private static string ResolveIn(string directory, string projectPath) =>
        Path.GetFullPath(Path.Combine(directory, projectPath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The folder, file and form rows below <paramref name="root"/>; every key starts with <paramref name="keyPrefix"/>.</summary>
    private static void AddFileRows(ProjectNodeViewModel root, string directory, IReadOnlyList<(string Path, string? Form)> files,
        string mainSource, string keyPrefix, IReadOnlyList<string> emptyFolders)
    {
        var listed = root.IsListed;
        var folders = new Dictionary<string, ProjectNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        ProjectNodeViewModel? external = null;

        ProjectNodeViewModel FolderFor(string relativeDirectory)
        {
            if (relativeDirectory.Length == 0)
                return root;
            if (folders.TryGetValue(relativeDirectory, out var existing))
                return existing;
            var cut = relativeDirectory.LastIndexOf('/', relativeDirectory.Length - 2);
            var parent = FolderFor(cut < 0 ? "" : relativeDirectory[..(cut + 1)]);
            var name = relativeDirectory[(cut + 1)..^1];
            var folder = new ProjectNodeViewModel(ProjectNodeKind.Folder, keyPrefix + relativeDirectory, name,
                Path.Combine(directory, relativeDirectory.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar)), parent)
            {
                IsExpanded = true,
                IsListed = listed,
            };
            folders[relativeDirectory] = folder;
            parent.Children.Add(folder);
            return folder;
        }

        foreach (var file in files)
        {
            var path = file.Path.Replace('\\', '/');
            var fullPath = ResolveIn(directory, file.Path);
            ProjectNodeViewModel parent;
            if (path.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(file.Path))
            {
                external ??= new ProjectNodeViewModel(ProjectNodeKind.Folder, keyPrefix + ExternalKey, Strings.ProjectManager_External,
                    directory, root) { IsExpanded = true, IsListed = listed };
                parent = external;
            }
            else
            {
                var slash = path.LastIndexOf('/');
                parent = FolderFor(slash < 0 ? "" : path[..(slash + 1)]);
            }

            var node = new ProjectNodeViewModel(ProjectNodeKind.File, keyPrefix + file.Path, Path.GetFileName(fullPath), fullPath, parent)
            {
                IsMainSource = string.Equals(file.Path, mainSource, StringComparison.OrdinalIgnoreCase),
                IsListed = listed,
            };
            if (file.Form is { } form)
            {
                var formPath = ResolveIn(directory, form);
                node.Children.Add(new ProjectNodeViewModel(ProjectNodeKind.Form, keyPrefix + form, Path.GetFileName(formPath), formPath, node) { IsListed = listed });
            }

            parent.Children.Add(node);
        }

        // IDE-PM-114: the remembered empty folders with every ancestor.
        foreach (var key in emptyFolders)
            FolderFor(key);

        Sort(root);
        if (external is not null)
        {
            Sort(external);
            root.Children.Add(external);
        }
    }

    /// <summary>UI-TW-102: folders first, then files, by name ignoring case; the main source first under the root.</summary>
    private static void Sort(ProjectNodeViewModel node)
    {
        var sorted = node.Children
            .OrderBy(c => c.Kind == ProjectNodeKind.Folder ? 0 : c.IsMainSource && node.Kind == ProjectNodeKind.Project ? 1 : 2)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        node.Children.Clear();
        foreach (var child in sorted)
        {
            node.Children.Add(child);
            if (child.Kind == ProjectNodeKind.Folder)
                Sort(child);
        }
    }

    /// <summary>UI-TW-105 / UI-TW-106: the dot for modified documents (the root for the project file), bold for the active one.</summary>
    private void UpdateDocumentMarkers()
    {
        foreach (var document in _documents.Documents.Where(d => _watched.Add(d)))
            document.PropertyChanged += OnDocumentPropertyChanged;
        _watched.RemoveWhere(d =>
        {
            if (_documents.Documents.Contains(d))
                return false;
            d.PropertyChanged -= OnDocumentPropertyChanged;
            return true;
        });

        var modified = _documents.Documents.Where(d => d.IsModified && d.FilePath is not null)
            .Select(d => Path.GetFullPath(d.FilePath!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var active = _documents.ActiveDocument?.FilePath is { } activePath ? Path.GetFullPath(activePath) : null;
        foreach (var node in Roots.SelectMany(r => r.SelfAndDescendants()))
        {
            // Rows of listed members carry no document marker (project-groups.md §8.1).
            node.IsModified = !node.IsListed && (modified.Contains(node.FullPath)
                || (node.Kind == ProjectNodeKind.Project && _projects.Project?.IsModified == true));
            node.IsActiveDocument = node.IsFile && !node.IsListed && string.Equals(node.FullPath, active, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentViewModel.IsModified) or nameof(DocumentViewModel.FilePath))
            UpdateDocumentMarkers();
    }

    private ProjectNodeViewModel? ActiveDocumentNode() =>
        Roots.SelectMany(r => r.SelfAndDescendants()).FirstOrDefault(n => n.IsActiveDocument);

    private void UpdateMissingFilesBar()
    {
        var count = Roots.SelectMany(r => r.SelfAndDescendants()).Count(n => n.IsMissing);
        MissingFilesBar = count == 0
            ? null
            : new InfoBarViewModel(
                MissingFilesKind,
                InfoBarSeverity.Warning,
                count == 1
                    ? Strings.Msg_Project_MissingFilesOne
                    : string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_MissingFilesMany, count),
                [
                    new InfoBarAction("Show", Strings.Button_Show, () =>
                    {
                        ShowFirstMissing();
                        return Task.CompletedTask;
                    }),
                    new InfoBarAction("RemoveFromProject", Strings.Button_RemoveFromProject, RemoveMissingAsync),
                ],
                dismiss: null);
    }

    /// <summary>UI-TW-107 <c>Show</c>: selects the first missing file in tree order.</summary>
    private void ShowFirstMissing()
    {
        if (Roots.SelectMany(r => r.SelfAndDescendants()).FirstOrDefault(n => n.IsMissing) is not { } node)
            return;
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            parent.IsExpanded = true;
        Select(node);
    }

    /// <summary>UI-TW-107 <c>Remove from Project</c>: all missing entries after the confirmation.</summary>
    private async Task RemoveMissingAsync()
    {
        var missing = Roots.SelectMany(r => r.SelfAndDescendants()).Where(n => n.IsMissing).ToList();
        if (missing.Count == 0)
            return;
        var request = missing.Count == 1
            ? new MessageRequest("Msg.Project.RemoveFileConfirm", [missing[0].Name])
            : new MessageRequest("Msg.Project.RemoveFilesConfirm", [missing.Count.ToString(CultureInfo.CurrentCulture)]);
        if (await _dialogs.ShowMessageAsync(request) != "Remove")
            return;
        await _projects.RemoveFromProjectAsync([.. missing.Select(n => n.Key)]);
    }

    private void OnSelectionChanged()
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        _commands.InvalidateCanExecute();
    }

    private sealed class Handler(Func<string?, bool> canExecute, Func<string?, Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument) => execute(argument);
    }
}
