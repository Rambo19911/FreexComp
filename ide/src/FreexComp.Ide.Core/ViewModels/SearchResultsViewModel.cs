using System.Globalization;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ToolWindows;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Models;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// A visible row of Search Results (UI-TW-601/608): a file row (<see cref="Match"/> null) or a match row. The key is the
/// path relative to the search root with <c>/</c> (absolute outside it), match rows <c>&lt;fileKey&gt;:&lt;line&gt;:&lt;col&gt;</c>.
/// <see cref="Text"/> is the displayed line (leading whitespace removed, cut to 240 characters around the match) and
/// <see cref="HighlightStart"/>/<see cref="HighlightLength"/> the match span in it.
/// </summary>
public sealed class SearchResultRow
{
    public required string Key { get; init; }

    public required string Path { get; init; }

    public FileMatch? Match { get; init; }

    public bool IsFile => Match is null;

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";

    public int Count { get; init; }

    public bool IsExpanded { get; init; }

    public string Text { get; init; } = "";

    public int HighlightStart { get; init; }

    public int HighlightLength { get; init; }

    public string LineText => Match?.Line.ToString(CultureInfo.InvariantCulture) ?? "";
}

/// <summary>
/// The Search Results tool window (tool-windows.md §8, editor-features.md §12): runs Find in Files
/// (<c>search.findInFiles</c>, the dialog of dialogs.md §11) and shows the last result as a two-level tree; owns the
/// <c>searchResults.*</c> commands. Positions of documents open at search time (or opened later unmodified) are anchored
/// (UI-TW-604). UI thread only.
/// </summary>
public sealed class SearchResultsViewModel : ToolWindowViewModel, ICommandTarget
{
    public const string ToolName = "SearchResults";

    /// <summary>UI-TW-601: longer lines are cut to a window of this many characters around the first match.</summary>
    public const int MaxLineLength = 240;

    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5);

    private readonly FindInFilesService _service;
    private readonly IDocumentService _documents;
    private readonly IProjectService _projects;
    private readonly NavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _pickers;
    private readonly IFileSystem _fileSystem;
    private readonly ILayoutService _layout;
    private readonly IStatusService _status;
    private readonly IClipboardService _clipboard;
    private readonly IUiDispatcher _dispatcher;
    private readonly List<FileMatch> _matches = [];
    private readonly Dictionary<FileMatch, ITextAnchor> _anchors = [];
    private readonly HashSet<string> _collapsed = new(StringComparer.OrdinalIgnoreCase);
    private IToolWindowService? _toolWindows;
    private ICommandService? _commands;
    private FindInFilesRequest? _request;
    private FindInFilesResult? _result;
    private CancellationTokenSource? _cancel;
    private IDisposable? _sticky;
    private string _root = "";
    private int _generation;
    private bool _focusPending;
    private IReadOnlyList<SearchResultRow> _rows = [];
    private FileMatch? _current;

    public SearchResultsViewModel(FindInFilesService service, IDocumentService documents, IProjectService projects, NavigationService navigation,
        IDialogService dialogs, IFileDialogService pickers, IFileSystem fileSystem, ILayoutService layout, IStatusService status,
        IClipboardService clipboard, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _service = service;
        _documents = documents;
        _projects = projects;
        _navigation = navigation;
        _dialogs = dialogs;
        _pickers = pickers;
        _fileSystem = fileSystem;
        _layout = layout;
        _status = status;
        _clipboard = clipboard;
        _dispatcher = dispatcher;
        documents.DocumentOpened += (_, e) => OnDocumentOpened(e.Document);
    }

    public ToolWindowDefinition Definition => new(ToolName, "Cmd_view_searchResults", ToolGroupId.Bottom, 3, () => this);

    public override IReadOnlyList<ToolWindowToolBarItem> ToolBarCommands { get; } =
    [
        new("searchResults.repeat", "sync"), new("searchResults.cancel", "stop"), new("searchResults.expandAll", "chevron-down"),
        new("searchResults.collapseAll", "collapse-all"), new("searchResults.clear", "clear"),
    ];

    public override bool HasFilter => true;

    /// <summary>tool-windows.md §8 context menu (null = separator).</summary>
    public static readonly IReadOnlyList<string?> ContextCommands =
    [
        "searchResults.goToSource", null, "edit.copy", null, "searchResults.expandAll", "searchResults.collapseAll", null,
        "searchResults.repeat", "search.findInFiles", null, "searchResults.clear",
    ];

    public IReadOnlyList<SearchResultRow> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    public IReadOnlyList<SearchResultRow> SelectedRows { get; set; } = [];

    public bool IsSearching => _cancel is not null;

    /// <summary>Never searched or cleared: <c>Msg.SearchResults.Empty</c>.</summary>
    public bool IsEmpty => _request is null;

    /// <summary>The search finished without a match: <c>Msg.FindInFiles.None</c> under the header.</summary>
    public bool IsNoMatch => _request is not null && !IsSearching && _matches.Count == 0;

    public string NoMatchText => _request is null ? "" : Format(Strings.Msg_FindInFiles_None, _request.Query.Text);

    /// <summary>UI-TW-606: the info bar <c>Msg.FindInFiles.Capped</c>.</summary>
    public bool IsCapped => _result?.IsCapped == true;

    public string CappedText => Format(Strings.Msg_FindInFiles_Capped, FindInFilesService.MatchCap.ToString("N0", CultureInfo.CurrentCulture));

    /// <summary>UI-TW-600: <c>Find "&lt;text&gt;" in &lt;scope&gt; — &lt;counts&gt; (&lt;options&gt;)</c>.</summary>
    public string Header
    {
        get
        {
            if (_request is not { } request)
                return "";
            var scope = request.Scope switch
            {
                FindInFilesScope.Project => Strings.SearchResults_ScopeProject,
                FindInFilesScope.OpenFiles => Strings.SearchResults_ScopeOpenFiles,
                _ => request.Folder ?? "",
            };
            var files = _matches.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var counts = IsSearching ? Strings.SearchResults_Searching
                : _matches.Count == 1 ? Strings.Msg_FindInFiles_DoneOne
                : Format(Strings.Msg_FindInFiles_DoneMany, _matches.Count, files);
            var text = Format(Strings.SearchResults_Header, request.Query.Text, scope, counts);
            List<string> options = [];
            if (request.Query.MatchCase)
                options.Add(Strings.SearchResults_OptionMatchCase);
            if (request.Query.WholeWord)
                options.Add(Strings.SearchResults_OptionWholeWord);
            if (request.Query.Regex)
                options.Add(Strings.SearchResults_OptionRegex);
            return options.Count == 0 ? text : text + " (" + string.Join(", ", options) + ")";
        }
    }

    /// <summary>The match row navigated to last (F8 continues from it).</summary>
    public SearchResultRow? CurrentRow => _current is null ? null : Rows.FirstOrDefault(r => r.Match == _current);

    public bool HasMatches => _matches.Count > 0;

    public void RegisterCommands(ICommandService commands, IToolWindowService toolWindows)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        _toolWindows = toolWindows;
        commands.RegisterHandler("search.findInFiles", new Handler(() => toolWindows.IsRegistered(ToolName), OpenDialogAsync));
        commands.RegisterHandler("searchResults.repeat", new Handler(() => _request is not null, () => Start(_request!)));
        commands.RegisterHandler("searchResults.cancel", new Handler(() => IsSearching, () =>
        {
            _cancel?.Cancel();
            return Task.CompletedTask;
        }));
        commands.RegisterHandler("searchResults.clear", new Handler(() => _request is not null, () =>
        {
            Clear();
            return Task.CompletedTask;
        }));
        commands.RegisterHandler("searchResults.expandAll", new Handler(() => _matches.Count > 0, () => SetAllExpanded(true)));
        commands.RegisterHandler("searchResults.collapseAll", new Handler(() => _matches.Count > 0, () => SetAllExpanded(false)));
        commands.RegisterHandler("searchResults.goToSource", new Handler(() => SelectedRows.Count > 0, () => GoToAsync(SelectedRows[0])));
    }

    public IReadOnlyList<Menus.MenuItemModel> BuildContextMenu()
    {
        var commands = _commands ?? throw new InvalidOperationException("Commands are not registered.");
        return [.. ContextCommands.Select(id => id is null
            ? Menus.MenuItemModel.Separator()
            : Menus.MenuItemModel.ForCommand(commands.Get(id).Title, commands.Get(id), null, Menus.MenuToggleKind.None))];
    }

    /// <summary>UI-TW-607: Ctrl+C copies the selected rows.</summary>
    public bool CanExecute(string commandId, string? argument) => commandId == "edit.copy" && SelectedRows.Count > 0;

    public async Task<bool> ExecuteAsync(string commandId, string? argument)
    {
        if (!CanExecute(commandId, argument))
            return false;
        await _clipboard.SetTextAsync(string.Join("\r\n", SelectedRows.Select(r => r.Match is { } m
            ? $"{m.Path}({m.Line},{m.Column}): {m.LineText.TrimStart()}"
            : r.Path)));
        return true;
    }

    /// <summary><c>search.findInFiles</c>: the dialog, then the search (UI-DLG-110).</summary>
    public async Task OpenDialogAsync()
    {
        var model = new FindInFilesViewModel(_layout, _fileSystem, _pickers, InitialText(), _projects.Project?.Directory);
        if (await _dialogs.ShowAsync(model) is { } request)
            await Start(request);
    }

    /// <summary>Starts the search without waiting for it: the command finishes, a new search may cancel this one.</summary>
    private Task Start(FindInFilesRequest request)
    {
        Running = RunAsync(request);
        return Task.CompletedTask;
    }

    /// <summary>The search started last (tests await it).</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    /// <summary>Starts a search (a running one is canceled first), shows the window and streams the results (IDE-ED-123).</summary>
    public async Task RunAsync(FindInFilesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _cancel?.Cancel();
        var generation = ++_generation;
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        _request = request;
        _result = null;
        _current = null;
        _matches.Clear();
        _anchors.Clear();
        _collapsed.Clear();
        _root = request.Scope switch
        {
            FindInFilesScope.Project => _projects.Project?.Directory ?? "",
            FindInFilesScope.Folder => request.Folder ?? "",
            _ => "",
        };
        _focusPending = true;
        _toolWindows?.Show(ToolName, focus: false);
        UpdateSticky();
        Rebuild();
        var progress = new BatchProgress(batch => _dispatcher.Post(() =>
        {
            if (generation != _generation || _cancel is null)
                return;
            Add(batch);
            UpdateSticky();
            Rebuild();
            FocusOnce();
        }));
        FindInFilesResult result;
        try
        {
            result = await _service.SearchAsync(request, progress, cancel.Token);
        }
        finally
        {
            if (generation == _generation)
                _cancel = null;
        }

        if (generation != _generation)
            return; // replaced by a newer search
        _result = result;
        _matches.Clear();
        _anchors.Clear();
        Add(result.Matches);
        _sticky?.Dispose();
        _sticky = null;
        var files = result.FileCount;
        _status.ShowMessage(result.IsCanceled ? Format(Strings.Msg_FindInFiles_Canceled, result.Matches.Count)
            : result.Matches.Count == 0 ? Format(Strings.Msg_FindInFiles_None, request.Query.Text)
            : result.Matches.Count == 1 ? Strings.Msg_FindInFiles_DoneOne
            : Format(Strings.Msg_FindInFiles_DoneMany, result.Matches.Count, files), StatusDuration);
        if (_matches.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 50)
        {
            foreach (var path in _matches.Select(m => m.Path))
                _collapsed.Add(path); // UI-TW-602
        }

        Rebuild();
        FocusOnce();
        _commands?.InvalidateCanExecute();
    }

    /// <summary>Clears the results (<c>searchResults.clear</c>): the empty state.</summary>
    public void Clear()
    {
        _cancel?.Cancel();
        _generation++;
        _cancel = null;
        _sticky?.Dispose();
        _sticky = null;
        _request = null;
        _result = null;
        _current = null;
        _matches.Clear();
        _anchors.Clear();
        Rebuild();
    }

    /// <summary>Expands or collapses a file row.</summary>
    public void SetExpanded(SearchResultRow row, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.IsFile)
            return;
        if (expanded)
            _collapsed.Remove(row.Path);
        else
            _collapsed.Add(row.Path);
        Rebuild();
    }

    /// <summary>UI-TW-603: the default action — a match opens its file with the span selected; a file row toggles.</summary>
    public async Task GoToAsync(SearchResultRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Match is not { } match)
        {
            SetExpanded(row, !row.IsExpanded);
            return;
        }

        _current = match;
        OnPropertyChanged(nameof(CurrentRow));
        var opened = await _documents.OpenAsync(match.Path);
        if (opened.Value is not TextDocumentViewModel document)
        {
            if (opened.NeedsMessage)
                await _dialogs.ShowMessageAsync(new MessageRequest(opened.MessageId!, opened.Args));
            return;
        }

        var buffer = document.Buffer;
        int start;
        if (_anchors.TryGetValue(match, out var anchor) && !anchor.IsDeleted)
        {
            start = Math.Min(anchor.Offset, buffer.Length);
        }
        else
        {
            var line = Math.Clamp(match.Line, 1, buffer.LineCount);
            var (lineStart, lineLength, _) = buffer.GetLine(line);
            start = lineStart + Math.Clamp(match.Column, 1, lineLength + 1) - 1;
        }

        var position = buffer.GetPosition(start);
        _navigation.GoToLine(document, position.Line, position.Column);
        var (spanLineStart, spanLineLength, _) = buffer.GetLine(position.Line);
        var end = Math.Min(start + match.Length, spanLineStart + spanLineLength);
        if (end > start)
            document.Selection = new TextSelection(start, end);
        ReturnFocusToDocument();
    }

    /// <summary>F8 / Shift+F8 over the match rows (UI-TW-603), wrapping.</summary>
    public Task NavigateNextAsync(bool forward)
    {
        if (_matches.Count == 0)
            return Task.CompletedTask;
        var index = _current is null ? -1 : _matches.IndexOf(_current);
        var next = index < 0 ? (forward ? 0 : _matches.Count - 1) : (index + (forward ? 1 : -1) + _matches.Count) % _matches.Count;
        _current = _matches[next];
        var row = Rows.FirstOrDefault(r => r.Match == _current) ?? MatchRow(_current, KeyOf(_current.Path));
        return GoToAsync(row);
    }

    protected override void OnFilterApplied(string filter) => Rebuild();

    private Task SetAllExpanded(bool expanded)
    {
        _collapsed.Clear();
        if (!expanded)
        {
            foreach (var match in _matches)
                _collapsed.Add(match.Path);
        }

        Rebuild();
        return Task.CompletedTask;
    }

    private void FocusOnce()
    {
        if (!_focusPending || (_matches.Count == 0 && IsSearching))
            return;
        _focusPending = false;
        _toolWindows?.Show(ToolName, focus: true);
    }

    private void UpdateSticky()
    {
        _sticky?.Dispose();
        var files = _matches.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        _sticky = _status.ShowStickyMessage(Format(Strings.Msg_FindInFiles_Searching, files, _matches.Count));
    }

    private void Add(IEnumerable<FileMatch> matches)
    {
        var open = _documents.Documents.OfType<TextDocumentViewModel>().Where(d => d.FilePath is not null)
            .GroupBy(d => d.FilePath!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var match in matches)
        {
            _matches.Add(match);
            if (open.TryGetValue(match.Path, out var document))
                Anchor(match, document);
        }
    }

    private void OnDocumentOpened(DocumentViewModel opened)
    {
        if (opened is not TextDocumentViewModel { FilePath: { } path, IsModified: false } document)
            return;
        foreach (var match in _matches.Where(m => !_anchors.ContainsKey(m) && string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase)))
            Anchor(match, document);
    }

    private void Anchor(FileMatch match, TextDocumentViewModel document)
    {
        var buffer = document.Buffer;
        if (match.Line > buffer.LineCount)
            return;
        var (lineStart, lineLength, _) = buffer.GetLine(match.Line);
        _anchors[match] = buffer.CreateAnchor(lineStart + Math.Clamp(match.Column, 1, lineLength + 1) - 1, movesWithInsertionBefore: true);
    }

    private string KeyOf(string path)
    {
        if (_root.Length > 0)
        {
            var relative = System.IO.Path.GetRelativePath(_root, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !System.IO.Path.IsPathFullyQualified(relative))
                return relative.Replace('\\', '/');
        }

        return path;
    }

    private void Rebuild()
    {
        var filter = FilterText.Trim();
        List<SearchResultRow> rows = [];
        foreach (var group in _matches.GroupBy(m => m.Path, StringComparer.OrdinalIgnoreCase))
        {
            var key = KeyOf(group.Key);
            var fileMatches = filter.Length == 0 || System.IO.Path.GetFileName(group.Key).Contains(filter, StringComparison.OrdinalIgnoreCase);
            var matches = group.Where(m => fileMatches || m.LineText.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
                continue;
            var expanded = filter.Length > 0 || !_collapsed.Contains(group.Key);
            rows.Add(new SearchResultRow { Key = key, Path = group.Key, Count = group.Count(), IsExpanded = expanded });
            if (expanded)
                rows.AddRange(matches.Select(m => MatchRow(m, key)));
        }

        var selected = SelectedRows.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        Rows = rows;
        SelectedRows = [.. rows.Where(r => selected.Contains(r.Key))];
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsNoMatch));
        OnPropertyChanged(nameof(NoMatchText));
        OnPropertyChanged(nameof(IsCapped));
        OnPropertyChanged(nameof(IsSearching));
        _commands?.InvalidateCanExecute();
    }

    /// <summary>UI-TW-601: leading whitespace removed; a long line cut to 240 characters around the match with <c>…</c>.</summary>
    private static SearchResultRow MatchRow(FileMatch match, string fileKey)
    {
        var line = match.LineText;
        var trimmed = line.Length - line.TrimStart().Length;
        var text = line[trimmed..];
        var start = Math.Max(0, match.Column - 1 - trimmed);
        var length = Math.Min(match.Length, Math.Max(0, text.Length - start));
        if (text.Length > MaxLineLength)
        {
            var from = Math.Clamp(start - (MaxLineLength - length) / 2, 0, text.Length - MaxLineLength);
            var window = text.Substring(from, MaxLineLength);
            var prefix = from > 0 ? "…" : "";
            text = prefix + window + (from + MaxLineLength < text.Length ? "…" : "");
            start = start - from + prefix.Length;
            length = Math.Min(length, MaxLineLength - (start - prefix.Length));
        }

        return new SearchResultRow
        {
            Key = $"{fileKey}:{match.Line}:{match.Column}",
            Path = match.Path,
            Match = match,
            Text = text,
            HighlightStart = start,
            HighlightLength = length,
        };
    }

    /// <summary>IDE-P1-105-like default: the single-line selection, else the word at the caret.</summary>
    private string? InitialText()
    {
        if (_documents.ActiveDocument is not TextDocumentViewModel document)
            return null;
        var selection = document.Selection;
        var buffer = document.Buffer;
        var (start, end) = selection.IsEmpty ? EditorOperations.WordAt(buffer, selection.Caret) : (selection.Start, selection.End);
        var text = end > start ? buffer.GetText(start, end - start) : "";
        return text.Length == 0 || text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal) ? null : text;
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>Reports batches synchronously (no captured context); the owner marshals them to the UI thread.</summary>
    private sealed class BatchProgress(Action<IReadOnlyList<FileMatch>> report) : IProgress<IReadOnlyList<FileMatch>>
    {
        public void Report(IReadOnlyList<FileMatch> value) => report(value);
    }

    private sealed class Handler(Func<bool> canExecute, Func<Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute();
    }
}
