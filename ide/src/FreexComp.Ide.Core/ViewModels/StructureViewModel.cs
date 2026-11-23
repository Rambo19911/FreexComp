using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.CodeModel;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ToolWindows;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>A visible row of the Structure tree: the node, its depth and whether it is expanded.</summary>
public sealed class StructureRow(StructureNode node, int depth, bool isExpanded) : ObservableObject
{
    private bool _isTracked;

    public StructureNode Node { get; } = node;

    public int Depth { get; } = depth;

    public bool IsExpanded { get; } = isExpanded;

    public bool HasChildren => Node.Children.Count > 0;

    /// <summary>UI-TW-205: the row of the declaration containing the caret.</summary>
    public bool IsTracked
    {
        get => _isTracked;
        set => SetProperty(ref _isTracked, value);
    }
}

/// <summary>
/// The Structure tool window (tool-windows.md §3, IDE-ED-130/131): the tree of the active Pascal document's buffer,
/// parsed on the <see cref="CodeModelWorker"/> 400 ms after the last edit and immediately on document switch; expansion
/// kept by node key; <c>structure.sortAlphabetically</c> persisted in <c>toolWindows.Structure.sorted</c>;
/// <c>structure.goToSource</c> moves the caret to the name and focuses the editor; the caret's declaration is tracked
/// 300 ms after the caret rests. A form document in the Design view (UI-TW-207, D-086) shows its component tree instead:
/// the selection follows the design surface both ways, the default action focuses the surface and <c>edit.delete</c>
/// deletes the selected components. Other documents show <c>Msg.Structure.Empty</c>.
/// </summary>
public sealed class StructureViewModel : ToolWindowViewModel, ICommandTarget
{
    /// <summary>UI-TW-209: the key prefix of the Design view's rows (<c>Form/&lt;ComponentName&gt;</c>).</summary>
    public const string DesignKeyPrefix = "Form/";

    public const string ToolName = "Structure";

    /// <summary>UI-TW-200: refresh delay after the last edit.</summary>
    public static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>UI-TW-205: caret-tracking delay.</summary>
    public static readonly TimeSpan TrackDelay = TimeSpan.FromMilliseconds(300);

    private readonly IDocumentService _documents;
    private readonly IClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly CodeModelWorker _worker;
    private readonly ILayoutService _layout;
    private readonly NavigationService _navigation;
    /// <summary>IDE-ED-133: the expansion state of each open document, by node key (dropped when the document closes).</summary>
    private readonly Dictionary<DocumentViewModel, Dictionary<string, bool>> _expansions = new(ReferenceEqualityComparer.Instance);
    private Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);
    private IReadOnlyList<StructureNode> _nodes = [];
    private TextDocumentViewModel? _document;
    private IDisposable? _refreshTimer;
    private IDisposable? _trackTimer;
    private SyntaxTree? _tree;
    private int _generation;
    private bool _isEmpty = true;
    private StructureRow? _selected;
    private IReadOnlyList<StructureRow> _selectedRows = [];
    private FormDocumentViewModel? _form;
    private ICommandService? _commands;

    public StructureViewModel(IDocumentService documents, IClock clock, IUiDispatcher dispatcher, CodeModelWorker worker, ILayoutService layout,
        NavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
        _clock = clock;
        _dispatcher = dispatcher;
        _worker = worker;
        _layout = layout;
        _navigation = navigation;
        documents.ActiveDocumentChanged += (_, _) => OnActiveDocumentChanged();
        documents.DocumentClosed += (_, e) => _expansions.Remove(e.Document);
        OnActiveDocumentChanged();
    }

    public ToolWindowDefinition Definition => new(ToolName, "Cmd_view_structure", ToolGroupId.Left, 3, () => this);

    public override IReadOnlyList<ToolWindowToolBarItem> ToolBarCommands { get; } = [new("structure.sortAlphabetically", "sort-az")];

    public override bool HasFilter => true;

    public ObservableCollection<StructureRow> Rows { get; } = [];

    /// <summary>No supported document (or nothing to show): the empty state <c>Msg.Structure.Empty</c>.</summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    public bool IsSorted => _layout.GetUiFlag("toolWindows.Structure", "sorted") == true;

    public StructureRow? SelectedRow
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                _commands?.InvalidateCanExecute();
        }
    }

    /// <summary>The Design view of a form document is active: the rows are its components (UI-TW-207).</summary>
    public bool IsDesign => _form is { Designer: { IsDesignActive: true, Model: not null } };

    /// <summary>UI-TW-207: the rows of the components selected on the design surface (several: multi-select).</summary>
    public IReadOnlyList<StructureRow> SelectedRows
    {
        get => _selectedRows;
        private set => SetProperty(ref _selectedRows, value);
    }

    /// <summary>The source of the rows (after a refresh); for tests and the view.</summary>
    public IReadOnlyList<StructureNode> Nodes => _nodes;

    public event EventHandler? Refreshed;

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        commands.RegisterHandler("structure.sortAlphabetically", new Handler(() => true, () =>
        {
            _layout.SetUiFlag("toolWindows.Structure", "sorted", !IsSorted);
            Rebuild();
            return Task.CompletedTask;
        }, () => IsSorted));
        commands.RegisterHandler("structure.goToSource", new Handler(
            () => SelectedRow is { } row && (IsDesign || row.Node.NameOffset >= 0 && _document is not null),
            () => GoToSourceAsync(SelectedRow!)));
    }

    /// <summary>
    /// UI-TW-207: the rows selected in the tree select their components on the design surface (the last one primary);
    /// nothing outside the Design view.
    /// </summary>
    public void SelectRows(IReadOnlyList<StructureRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (!IsDesign || rows.Count == 0 || _form!.Designer.Surface is not { } surface)
            return;
        surface.Selection.Set(rows.Select(r => r.Node.Name));
        SyncFromSurface(); // also when the surface kept its selection (the root dropped from a multi-selection)
    }

    /// <summary>UI-TW-207: <c>edit.delete</c> in the Design view's tree deletes the selected components, as on the surface.</summary>
    bool ICommandTarget.CanExecute(string commandId, string? argument) =>
        commandId == FreexComp.Designer.Surface.Commands.Delete && IsDesign && _form!.Designer.Surface is { } surface && surface.CanExecute(commandId);

    Task<bool> ICommandTarget.ExecuteAsync(string commandId, string? argument)
    {
        if (!((ICommandTarget)this).CanExecute(commandId, argument))
            return Task.FromResult(false);
        _form!.Designer.Surface!.Execute(commandId, argument);
        return Task.FromResult(true);
    }

    /// <summary>
    /// tool-windows.md §3: in the Design view a row's context menu is the designer context menu of the selection (its
    /// verb group first, DSG-SUR-191, UI-DSG-082); null outside the Design view.
    /// </summary>
    public IReadOnlyList<Menus.MenuItemModel>? BuildDesignContextMenu() => IsDesign ? _form!.Designer.ContextMenu?.Invoke() : null;

    /// <summary>Expands or collapses a row (keyed, kept across refreshes).</summary>
    public void SetExpanded(StructureRow row, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.HasChildren || row.IsExpanded == expanded)
            return;
        _expanded[row.Node.Key] = expanded;
        Flatten(row.Node.Key);
        if (IsDesign)
            SyncFromSurface();
    }

    /// <summary>UI-TW-204: caret to the name (or error position), revealed at one third, editor focused.</summary>
    public Task GoToSourceAsync(StructureRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (IsDesign)
        {
            // DSG-SUR-196: a component whose class has a component editor opens it (Enter, double-click on its node).
            const string editComponent = FreexComp.Designer.Surface.Commands.EditComponent;
            if (_commands is { } commands && _form!.Designer.Model?.FindByName(row.Node.Name) is { Info.ComponentEditor: not null } component
                && _form.Designer.Surface is { } surface && surface.Selection.Items is [var only] && ReferenceEquals(only, component)
                && commands.CanExecute(editComponent))
            {
                return commands.ExecuteAsync(editComponent);
            }

            ReturnFocusToDocument(); // UI-TW-207: the default action focuses the design surface
            return Task.CompletedTask;
        }

        if (_document is not { } document || row.Node.NameOffset < 0)
            return Task.CompletedTask;
        var from = document.ViewState.Caret;
        var offset = Math.Min(row.Node.NameOffset, document.Buffer.Length);
        document.Folding.Reveal(offset);
        document.Selection = TextSelection.At(offset);
        document.RevealCaret(third: true);
        _navigation.RecordDeparture(document, from);
        ReturnFocusToDocument();
        return Task.CompletedTask;
    }

    protected override void OnFilterApplied(string filter)
    {
        Flatten(null);
        if (IsDesign)
            SyncFromSurface();
    }

    private void OnActiveDocumentChanged()
    {
        if (_document is { } old)
        {
            old.Buffer.Changed -= OnBufferChanged;
            old.ViewState.PropertyChanged -= OnViewStateChanged;
        }

        if (_form is { } oldForm)
        {
            oldForm.Designer.ActiveViewChanged -= OnDesignViewChanged;
            oldForm.Designer.ModelChanged -= OnDesignModelChanged;
            oldForm.Designer.ModelReplaced -= OnDesignModelChanged;
            oldForm.Designer.SelectionChanged -= OnDesignSelectionChanged;
        }

        _expanded = new(StringComparer.Ordinal);
        if (_documents.ActiveDocument is { } active)
        {
            if (_expansions.TryGetValue(active, out var kept))
                _expanded = kept;
            else
                _expansions[active] = _expanded;
        }

        _form = _documents.ActiveDocument as FormDocumentViewModel;
        if (_form is { } form)
        {
            form.Designer.ActiveViewChanged += OnDesignViewChanged;
            form.Designer.ModelChanged += OnDesignModelChanged;
            form.Designer.ModelReplaced += OnDesignModelChanged;
            form.Designer.SelectionChanged += OnDesignSelectionChanged;
            if (IsDesign)
            {
                _document = null;
                _generation++;
                Rebuild(); // immediately on document switch
                return;
            }
        }

        _document = _documents.ActiveDocument as TextDocumentViewModel;
        if (_document is { } document && document.Language.Id == "pascal")
        {
            document.Buffer.Changed += OnBufferChanged;
            document.ViewState.PropertyChanged += OnViewStateChanged;
            Refresh(); // immediately on document switch
        }
        else
        {
            _document = null;
            _generation++;
            _tree = null;
            _nodes = [];
            Flatten(null);
        }
    }

    /// <summary>Code and Design switched: the tree of the view now shown (the unit is parsed again, code sync may have changed it).</summary>
    private void OnDesignViewChanged(object? sender, EventArgs e) => OnActiveDocumentChanged();

    private void OnDesignModelChanged(object? sender, EventArgs e)
    {
        if (IsDesign)
            Rebuild();
    }

    private void OnDesignSelectionChanged(object? sender, EventArgs e)
    {
        if (IsDesign)
            SyncFromSurface();
    }

    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        _refreshTimer?.Dispose();
        _refreshTimer = _clock.Schedule(RefreshDelay, Refresh);
    }

    private void OnViewStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditorViewState.Caret))
            return;
        _trackTimer?.Dispose();
        _trackTimer = _clock.Schedule(TrackDelay, Track);
    }

    private void Refresh()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        if (_document is not { } document)
            return;
        var generation = ++_generation;
        var path = document.FilePath ?? Path.Combine(Path.GetTempPath(), document.Title);
        var text = document.Buffer.Text; // the buffer, never the disk (UI-TW-200)
        _ = ParseAsync(generation, path, text);
    }

    private async Task ParseAsync(int generation, string path, string text)
    {
        SyntaxTree tree;
        try
        {
            tree = await _worker.RunAsync("structure", _ => SyntaxTree.Parse(SourceText.From(PathUtilNormalize(path), text)));
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            if (generation != _generation)
                return; // a newer request or another document
            _tree = tree;
            Rebuild();
        });
    }

    private static string PathUtilNormalize(string path) => Compiler.Text.PathUtil.Normalize(path);

    private void Rebuild()
    {
        if (IsDesign)
        {
            _nodes = BuildDesign(_form!.Designer.Model!);
            Flatten(null);
            SyncFromSurface();
        }
        else
        {
            _nodes = _tree is null ? [] : StructureBuilder.Build(_tree, IsSorted);
            Flatten(null);
            SelectedRows = [];
            Track();
        }

        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// UI-TW-207, 209: the form, then its components in creation order (non-visual ones after their visual siblings), each
    /// <c>Name: TClass</c> with the key <c>Form/&lt;Name&gt;</c> (a duplicate name, preserved F0106, appends <c>#&lt;n&gt;</c>).
    /// </summary>
    private static IReadOnlyList<StructureNode> BuildDesign(FormDocument model)
    {
        var keys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static bool IsNonVisual(FormObject obj) => obj.Info?.Kind == ComponentKind.NonVisual;
        StructureNode Node(FormObject obj)
        {
            var key = DesignKeyPrefix + obj.Name;
            var seen = keys.GetValueOrDefault(key);
            keys[key] = seen + 1;
            if (seen > 0)
                key += "#" + seen.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var children = obj.Children.Where(c => !IsNonVisual(c)).Concat(obj.Children.Where(IsNonVisual)).Select(Node).ToList();
            return new StructureNode(key, obj.Name, $"{obj.Name}: {obj.TypeName}", StructureKind.Component, -1, 0, 0, children,
                IsNonVisual: IsNonVisual(obj), IsUnavailable: obj.IsPreserved);
        }

        return [Node(model.Root)];
    }

    /// <summary>UI-TW-207: the tree selection follows the surface's (the primary is the selected row).</summary>
    private void SyncFromSurface()
    {
        if (_form?.Designer.Surface is not { } surface)
            return;
        var selected = surface.Selection.Items;
        SelectedRows = [.. Rows.Where(r => selected.Any(o => o.Name.Equals(r.Node.Name, StringComparison.OrdinalIgnoreCase)))];
        SelectedRow = Rows.FirstOrDefault(r => r.Node.Name.Equals(surface.Selection.Primary.Name, StringComparison.OrdinalIgnoreCase));
    }

    private void Flatten(string? keepKey)
    {
        var selectedKey = keepKey ?? SelectedRow?.Node.Key;
        Rows.Clear();
        var filter = FilterText.Trim();
        void Add(StructureNode node, int depth)
        {
            var matches = filter.Length == 0 || Matches(node, filter);
            if (!matches)
                return;
            // UI-TW-206: new nodes start expanded, except types with more than 30 members.
            var expanded = node.Children.Count > 0 && (filter.Length > 0
                || _expanded.GetValueOrDefault(node.Key, node.Kind == StructureKind.Group || node.Children.Count <= 30));
            var row = new StructureRow(node, depth, expanded);
            Rows.Add(row);
            if (expanded)
            {
                foreach (var child in node.Children)
                    Add(child, depth + 1);
            }
        }

        foreach (var node in _nodes)
            Add(node, 0);
        IsEmpty = _document is null && !IsDesign || Rows.Count == 0 && filter.Length == 0;
        OnPropertyChanged(nameof(IsDesign));
        SelectedRow = Rows.FirstOrDefault(r => r.Node.Key == selectedKey);
    }

    /// <summary>UI-TW-208: a row is shown when its text or a descendant's text matches.</summary>
    private static bool Matches(StructureNode node, string filter) =>
        node.Text.Contains(filter, StringComparison.OrdinalIgnoreCase) || node.Children.Any(c => Matches(c, filter));

    /// <summary>UI-TW-205: the innermost declaration containing the caret; inside a collapsed node its nearest visible ancestor.</summary>
    private void Track()
    {
        _trackTimer?.Dispose();
        _trackTimer = null;
        foreach (var row in Rows)
            row.IsTracked = false;
        if (IsDesign || _document is not { } document || _nodes.Count == 0)
            return;
        var offset = document.CaretOffset; // clamped: the caret may be stale against the buffer (I-210)
        if (StructureBuilder.FindInnermost(_nodes, offset) is not { } target)
            return;
        var key = target.Key;
        while (key.Length > 0)
        {
            if (Rows.FirstOrDefault(r => r.Node.Key == key) is { } row)
            {
                row.IsTracked = true;
                SelectedRow = row;
                return;
            }

            var slash = key.LastIndexOf('/');
            key = slash < 0 ? "" : key[..slash];
        }
    }

    private sealed class Handler(Func<bool> canExecute, Func<Task> execute, Func<bool>? isChecked = null) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute();

        public bool IsChecked(string? argument) => isChecked?.Invoke() == true;
    }
}
