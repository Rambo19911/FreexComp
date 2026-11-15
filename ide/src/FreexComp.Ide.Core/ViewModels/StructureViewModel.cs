using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
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
/// 300 ms after the caret rests. Other documents show <c>Msg.Structure.Empty</c>.
/// </summary>
public sealed class StructureViewModel : ToolWindowViewModel
{
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
    private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);
    private IReadOnlyList<StructureNode> _nodes = [];
    private TextDocumentViewModel? _document;
    private IDisposable? _refreshTimer;
    private IDisposable? _trackTimer;
    private SyntaxTree? _tree;
    private int _generation;
    private bool _isEmpty = true;
    private StructureRow? _selected;
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
        commands.RegisterHandler("structure.goToSource", new Handler(() => SelectedRow is { Node.NameOffset: >= 0 } && _document is not null,
            () => GoToSourceAsync(SelectedRow!)));
    }

    /// <summary>Expands or collapses a row (keyed, kept across refreshes).</summary>
    public void SetExpanded(StructureRow row, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.HasChildren || row.IsExpanded == expanded)
            return;
        _expanded[row.Node.Key] = expanded;
        Flatten(row.Node.Key);
    }

    /// <summary>UI-TW-204: caret to the name (or error position), revealed at one third, editor focused.</summary>
    public Task GoToSourceAsync(StructureRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
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

    protected override void OnFilterApplied(string filter) => Flatten(null);

    private void OnActiveDocumentChanged()
    {
        if (_document is { } old)
        {
            old.Buffer.Changed -= OnBufferChanged;
            old.ViewState.PropertyChanged -= OnViewStateChanged;
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
        _nodes = _tree is null ? [] : StructureBuilder.Build(_tree, IsSorted);
        Flatten(null);
        Track();
        Refreshed?.Invoke(this, EventArgs.Empty);
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
        IsEmpty = _document is null || Rows.Count == 0 && filter.Length == 0;
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
        if (_document is not { } document || _nodes.Count == 0)
            return;
        var caret = document.ViewState.Caret;
        var offset = document.Buffer.GetOffset(caret.Line, caret.Column);
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
