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
public sealed class ProjectManagerViewModel : ToolWindowViewModel
{
    public const string ToolName = "ProjectManager";
    public const string MissingFilesKind = "MissingFiles";
    public const string RootKey = "$project";
    public const string ExternalKey = "$external/";

    private const string LogCategory = "project";

    private static readonly string?[] RootMenu =
    [
        "project.build", "project.rebuild", "project.clean", null, "run.start", "run.startWithoutDebugging", null,
        "project.addNewUnit", "project.addNewForm", "project.addExistingFile", "project.newFolder", null,
        "project.viewSource", "file.showInExplorer", "project.openOutputFolder", "file.copyPath", null,
        "file.closeProject", null, "project.options",
    ];

    private static readonly string?[] FolderMenu =
    [
        "project.addNewUnit", "project.addNewForm", "project.addExistingFile", "project.newFolder", null,
        "file.showInExplorer", "file.copyPath",
    ];

    private static readonly string?[] FileMenu =
    [
        "projectManager.open", "projectManager.openForm", null, "file.save", null, "project.renameFile", "project.removeFile",
        "project.deleteFile", null, "file.showInExplorer", "file.copyPath",
    ];

    private static readonly string?[] FilesMenu = ["projectManager.open", null, "project.removeFile", "project.deleteFile"];

    private static readonly string?[] EmptyAreaMenu =
        ["project.addNewUnit", "project.addNewForm", "project.addExistingFile", null, "projectManager.collapseAll"];

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

    /// <summary>The project roots (one in P1–P7, UI-TW-100); empty without a project.</summary>
    public ObservableCollection<ProjectNodeViewModel> Roots { get; } = [];

    public bool HasProject => Roots.Count > 0;

    /// <summary>The empty state's links (UI-TW-013): New Project, Open Project.</summary>
    public IReadOnlyList<IdeCommand> EmptyLinks => [_commands.Get("file.newProject"), _commands.Get("file.openProject")];

    /// <summary>Selected rows in selection order (UI-TW-109).</summary>
    public IReadOnlyList<ProjectNodeViewModel> Selection => _selection;

    /// <summary>The <c>Msg.Project.MissingFiles*</c> info bar (UI-TW-107); null when nothing is missing.</summary>
    public InfoBarViewModel? MissingFilesBar
    {
        get => _missingFilesBar;
        private set => SetProperty(ref _missingFilesBar, value);
    }

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
        _commands.RegisterHandler("projectManager.collapseAll", new Handler(_ => HasProject, _ =>
        {
            CollapseAll();
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

    /// <summary>The folder of the selected row when exactly one folder inside the project is selected (IDE-PM-040).</summary>
    public string? SelectedFolderPath => _selection is [{ Kind: ProjectNodeKind.Folder } folder] && folder.Key != ExternalKey ? folder.FullPath : null;

    /// <summary>The in-place rename of a file row ended (committed or canceled); the view puts focus back on the tree.</summary>
    public event EventHandler? RenameEnded;

    /// <summary>
    /// UI-TW-112: the file row <paramref name="projectPath"/> gets an in-place editor (<see cref="ProjectNodeViewModel.Rename"/>)
    /// with the validation and rename of the service. False when there is no such row.
    /// </summary>
    public bool BeginRename(string projectPath)
    {
        if (Find(projectPath) is not { Kind: ProjectNodeKind.File } node)
            return false;
        var rename = new RenameFileViewModel(node.Name, name => _projects.ValidateRename(projectPath, name),
            (name, update) => _projects.RenameFileAsync(projectPath, name, update));
        rename.CloseRequested += (_, _) =>
        {
            node.Rename = null;
            RenameEnded?.Invoke(this, EventArgs.Empty);
        };
        node.Rename = rename;
        return true;
    }

    /// <summary>The project paths (row keys) of the selected file and form rows (IDE-PM-060).</summary>
    public IReadOnlyList<string> SelectedFilePaths => [.. _selection.Where(n => n.IsFile).Select(n => n.Key)];

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
            if (command.Definition.Phase > _implementedPhase)
                continue;
            if (pendingSeparator)
                items.Add(MenuItemModel.Separator());
            pendingSeparator = false;
            items.Add(MenuItemModel.ForCommand(MenuLabels.For(new CommandMenuNode(id)), command, null, MenuToggleKind.None));
        }

        return items;
    }

    /// <summary>
    /// UI-TW-108 default action of the selection: files open (the last one active, focus to the document area); a
    /// single folder or root row toggles its expansion (the root's Project Options action arrives in P3).
    /// </summary>
    public async Task OpenSelectionAsync()
    {
        if (_selection is [{ IsFile: false } container])
        {
            container.IsExpanded = !container.IsExpanded;
            return;
        }

        var opened = false;
        foreach (var node in _selection.Where(n => n.IsFile).ToList())
        {
            var result = await _documents.OpenAsync(node.FullPath);
            if (result.Success)
                opened = true;
            else
                await _dialogs.ShowMessageAsync(new MessageRequest(result.MessageId!, result.Args));
        }

        if (opened)
            ReturnFocusToDocument();
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
        if (HasProject)
            _ = CheckMissingFilesAsync();
    }

    /// <summary>
    /// IDE-PM-103: <c>File.Exists</c> per file row on a background thread; the result is applied only when no newer check
    /// or rebuild started meanwhile.
    /// </summary>
    public async Task CheckMissingFilesAsync()
    {
        var check = ++_missingCheck;
        var files = Roots.SelectMany(r => r.SelfAndDescendants()).Where(n => n.IsFile).ToList();
        IsCheckingMissingFiles = true;
        bool[] exists;
        try
        {
            exists = await Task.Run(() => files.Select(f => _fileSystem.FileExists(f.FullPath)).ToArray());
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
        IsCheckingMissingFiles = false;
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
        if (_projects.Project is { } project)
        {
            var root = BuildTree(project);
            RestoreExpansionInto(root, expansion);
            Roots.Add(root);
            ApplyFilter();
            _selection.AddRange(selected.Select(Find).OfType<ProjectNodeViewModel>());
        }
        else
        {
            _expansionBeforeFilter = null;
        }

        OnPropertyChanged(nameof(HasProject));
        UpdateDocumentMarkers();
        OnSelectionChanged();
        if (HasProject)
            _ = CheckMissingFilesAsync();
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
    private ProjectNodeViewModel BuildTree(ProjectModel project)
    {
        var badge = $"{project.ActiveConfiguration} · {project.Platforms.FirstOrDefault() ?? "win64"}";
        var root = new ProjectNodeViewModel(ProjectNodeKind.Project, RootKey, project.Name, project.FilePath, null)
        {
            Badge = badge,
            IsExpanded = true,
        };
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
            var folder = new ProjectNodeViewModel(ProjectNodeKind.Folder, relativeDirectory, name,
                Path.Combine(project.Directory, relativeDirectory.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar)), parent)
            {
                IsExpanded = true,
            };
            folders[relativeDirectory] = folder;
            parent.Children.Add(folder);
            return folder;
        }

        foreach (var file in project.Files)
        {
            var path = file.Path.Replace('\\', '/');
            var fullPath = _projects.ResolvePath(file.Path)!;
            ProjectNodeViewModel parent;
            if (path.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(file.Path))
            {
                external ??= new ProjectNodeViewModel(ProjectNodeKind.Folder, ExternalKey, Strings.ProjectManager_External,
                    project.Directory, root) { IsExpanded = true };
                parent = external;
            }
            else
            {
                var slash = path.LastIndexOf('/');
                parent = FolderFor(slash < 0 ? "" : path[..(slash + 1)]);
            }

            var node = new ProjectNodeViewModel(ProjectNodeKind.File, file.Path, Path.GetFileName(fullPath), fullPath, parent)
            {
                IsMainSource = string.Equals(file.Path, project.MainSource, StringComparison.OrdinalIgnoreCase),
            };
            if (file.Form is { } form)
            {
                var formPath = _projects.ResolvePath(form)!;
                node.Children.Add(new ProjectNodeViewModel(ProjectNodeKind.Form, form, Path.GetFileName(formPath), formPath, node));
            }

            parent.Children.Add(node);
        }

        Sort(root);
        if (external is not null)
        {
            Sort(external);
            root.Children.Add(external);
        }

        return root;
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
            node.IsModified = modified.Contains(node.FullPath)
                || (node.Kind == ProjectNodeKind.Project && _projects.Project?.IsModified == true);
            node.IsActiveDocument = node.IsFile && string.Equals(node.FullPath, active, StringComparison.OrdinalIgnoreCase);
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
