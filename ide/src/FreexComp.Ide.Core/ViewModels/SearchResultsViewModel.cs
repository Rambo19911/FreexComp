using System.Globalization;
using FreexComp.Ide.Core.CodeModel;
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
using ReferenceKind = FreexComp.LanguageService.ReferenceKind;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// A visible row of Search Results (UI-TW-601/608): a file row, a match row (<see cref="Match"/> set) or, in a
/// References result set (UI-TW-609/614), a container row (<see cref="Container"/> set) between them. The key is the
/// path relative to the search root with <c>/</c> (absolute outside it), match rows <c>&lt;fileKey&gt;:&lt;line&gt;:&lt;col&gt;</c>,
/// container rows <c>&lt;fileKey&gt;#&lt;container&gt;</c> (<c>@2</c> … for a further container of that name in the file).
/// <see cref="Text"/> is the displayed line (leading whitespace removed, cut to 240 characters around the match) and
/// <see cref="HighlightStart"/>/<see cref="HighlightLength"/> the match span in it.
/// </summary>
public sealed class SearchResultRow
{
    public required string Key { get; init; }

    public required string Path { get; init; }

    public FileMatch? Match { get; init; }

    /// <summary>The container's name on a container row (UI-TW-609); null on file and match rows.</summary>
    public string? Container { get; init; }

    public bool IsFile => Match is null && Container is null;

    public bool IsContainer => Match is null && Container is not null;

    /// <summary>The kind of a reference row (UI-TW-610); null on every other row.</summary>
    public ReferenceKind? ReferenceKind => (Match as ReferenceMatch)?.Kind;

    /// <summary>UI-TW-609: the file row of a library source (the suffix <c>Msg.References.Library</c>).</summary>
    public bool IsLibrary { get; init; }

    /// <summary>What the expansion state of a file or container row is kept under.</summary>
    internal string GroupId { get; init; } = "";

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
/// One result set of the Search Results window with the state that belongs to it (UI-TW-618): the query — a Find in
/// Files request or the origin of a reference query —, the matches in display order, their anchors, the collapsed
/// rows, the current row and the running search. The window shows one set; keeping several is I-354.
/// </summary>
internal sealed class SearchResultSet
{
    /// <summary>Kind Find in Files: the request.</summary>
    public FindInFilesRequest? Request { get; init; }

    /// <summary>Kind References: where the query was started (IDE-FREF-008).</summary>
    public ReferenceOrigin? Origin { get; init; }

    /// <summary>Kind References: the identifier as written where the query started (UI-TW-611).</summary>
    public string Identifier { get; init; } = "";

    /// <summary>UI-TW-608: row keys are relative to this folder.</summary>
    public string Root { get; init; } = "";

    public List<FileMatch> Matches { get; } = [];

    public Dictionary<FileMatch, ITextAnchor> Anchors { get; } = [];

    /// <summary>The <see cref="SearchResultRow.GroupId"/> of the collapsed file and container rows.</summary>
    public HashSet<string> Collapsed { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The match navigated to last (F8 continues from it).</summary>
    public FileMatch? Current { get; set; }

    /// <summary>The running Find in Files search of this set; null when it has ended.</summary>
    public CancellationTokenSource? Cancel { get; set; }

    public bool IsCapped { get; set; }

    /// <summary>Kind References: the tracked start of the origin identifier while <see cref="OriginDocument"/> is open.</summary>
    public ITextAnchor? OriginAnchor { get; set; }

    public TextDocumentViewModel? OriginDocument { get; set; }
}

/// <summary>
/// The Search Results tool window (tool-windows.md §8, editor-features.md §12): runs Find in Files
/// (<c>search.findInFiles</c>, the dialog of dialogs.md §11) and shows one result set — of kind Find in Files (a
/// two-level tree) or References (<see cref="ShowReferences"/>: file, container, reference; UI-TW-609…614); owns the
/// <c>searchResults.*</c> commands. Positions of documents open at search time (or opened later unmodified) are anchored
/// (UI-TW-604). UI thread only.
/// </summary>
public sealed class SearchResultsViewModel : ToolWindowViewModel, ICommandTarget
{
    public const string ToolName = "SearchResults";

    /// <summary>UI-TW-601: longer lines are cut to a window of this many characters around the first match.</summary>
    public const int MaxLineLength = 240;

    /// <summary>UI-TW-602 / UI-TW-612: with more files than this the rows start collapsed.</summary>
    public const int ExpandedFileLimit = 50;

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
    private IToolWindowService? _toolWindows;
    private ICommandService? _commands;
    private SearchResultSet? _set; // the shown result set; null = the empty state
    private IDisposable? _sticky;
    private bool _focusPending;
    private IReadOnlyList<SearchResultRow> _rows = [];

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
        new("searchResults.repeat", "refresh"), new("searchResults.cancel", "stop"), new("searchResults.expandAll", "chevron-down"),
        new("searchResults.collapseAll", "collapse-all"), new("searchResults.clear", "trash"),
    ];

    public override bool HasFilter => true;

    /// <summary>tool-windows.md §8 context menu (null = separator).</summary>
    public static readonly IReadOnlyList<string?> ContextCommands =
    [
        "searchResults.goToSource", null, "edit.copy", null, "searchResults.expandAll", "searchResults.collapseAll", null,
        "searchResults.repeat", "search.findInFiles", null, "searchResults.clear",
    ];

    /// <summary>The reference queries (<c>search.findReferences</c>); set by <see cref="FindReferencesService"/>.</summary>
    internal FindReferencesService? References { get; set; }

    /// <summary>
    /// A result set was shown without moving the keyboard focus (IDE-FREF-003): Search Results is then the window
    /// <c>search.nextMessage</c> / <c>search.previousMessage</c> step through (UI-TW-603).
    /// </summary>
    public event EventHandler? ResultsShown;

    public IReadOnlyList<SearchResultRow> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    public IReadOnlyList<SearchResultRow> SelectedRows { get; set; } = [];

    /// <summary>The shown set's search runs: Find in Files, or the repeat of a References set (IDE-FREF-008).</summary>
    public bool IsSearching => _set is { } set && (set.Cancel is not null || ReferenceEquals(References?.RepeatTarget, set));

    /// <summary>Never searched or cleared: <c>Msg.SearchResults.Empty</c>.</summary>
    public bool IsEmpty => _set is null;

    /// <summary>The shown set is of kind References (UI-TW-609).</summary>
    public bool IsReferences => _set?.Origin is not null;

    /// <summary>The search finished without a match: <c>Msg.FindInFiles.None</c> under the header.</summary>
    public bool IsNoMatch => _set is { Request: not null } set && !IsSearching && set.Matches.Count == 0;

    public string NoMatchText => _set?.Request is { } request ? Format(Strings.Msg_FindInFiles_None, request.Query.Text) : "";

    /// <summary>UI-TW-606 / IDE-FREF-006: the info bar <c>Msg.FindInFiles.Capped</c> or <c>Msg.References.Capped</c>.</summary>
    public bool IsCapped => _set?.IsCapped == true;

    public string CappedText => Format(IsReferences ? Strings.Msg_References_Capped : Strings.Msg_FindInFiles_Capped,
        FindInFilesService.MatchCap.ToString("N0", CultureInfo.CurrentCulture));

    /// <summary>
    /// UI-TW-600: <c>Find "&lt;text&gt;" in &lt;scope&gt; — &lt;counts&gt; (&lt;options&gt;)</c>; UI-TW-611:
    /// <c>References to &lt;name&gt; — &lt;counts&gt;</c> with the library part.
    /// </summary>
    public string Header
    {
        get
        {
            if (_set is not { } set)
                return "";
            var matches = set.Matches;
            var files = matches.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (set.Origin is not null)
            {
                if (IsSearching)
                    return Format(Strings.SearchResults_Header_References, set.Identifier, Strings.SearchResults_Searching);
                var references = Format(Strings.SearchResults_Header_References, set.Identifier, FindReferencesService.CountText(matches.Count, files));
                var library = matches.Count(m => m is ReferenceMatch { IsLibrary: true });
                return library == 0 ? references : references + " " + Format(Strings.SearchResults_Header_Library, library);
            }

            var request = set.Request!;
            var scope = request.Scope switch
            {
                FindInFilesScope.Project => Strings.SearchResults_ScopeProject,
                FindInFilesScope.OpenFiles => Strings.SearchResults_ScopeOpenFiles,
                _ => request.Folder ?? "",
            };
            var counts = IsSearching ? Strings.SearchResults_Searching
                : matches.Count == 1 ? Strings.Msg_FindInFiles_DoneOne
                : Format(Strings.Msg_FindInFiles_DoneMany, matches.Count, files);
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
    public SearchResultRow? CurrentRow => _set?.Current is { } current ? Rows.FirstOrDefault(r => r.Match == current) : null;

    public bool HasMatches => _set is { Matches.Count: > 0 };

    public void RegisterCommands(ICommandService commands, IToolWindowService toolWindows)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        _toolWindows = toolWindows;
        commands.RegisterHandler("search.findInFiles", new Handler(() => toolWindows.IsRegistered(ToolName), OpenDialogAsync));
        commands.RegisterHandler("searchResults.repeat", new Handler(() => _set is not null, Repeat));
        commands.RegisterHandler("searchResults.cancel", new Handler(() => IsSearching || References?.IsSearching == true, () =>
        {
            _set?.Cancel?.Cancel();
            References?.Cancel(); // IDE-FREF-002
            return Task.CompletedTask;
        }));
        commands.RegisterHandler("searchResults.clear", new Handler(() => _set is not null, () =>
        {
            Clear();
            return Task.CompletedTask;
        }));
        commands.RegisterHandler("searchResults.expandAll", new Handler(() => HasMatches, () => SetAllExpanded(true)));
        commands.RegisterHandler("searchResults.collapseAll", new Handler(() => HasMatches, () => SetAllExpanded(false)));
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
        await _clipboard.SetTextAsync(string.Join("\r\n", SelectedRows.Select(CopyText)));
        return true;
    }

    /// <summary>UI-TW-607; UI-TW-613: a reference row names its kind, a container row its file and its name.</summary>
    private static string CopyText(SearchResultRow row) => row.Match switch
    {
        ReferenceMatch m => $"{m.Path}({m.Line},{m.Column}): {KindName(m.Kind)}: {m.LineText.TrimStart()}",
        { } m => $"{m.Path}({m.Line},{m.Column}): {m.LineText.TrimStart()}",
        null => row.Container is { } container ? $"{row.Path}: {container}" : row.Path,
    };

    /// <summary>UI-TW-610: the name of a reference kind (<c>Msg.References.Kind…</c>): tooltip and automation name of its badge.</summary>
    public static string KindName(ReferenceKind kind) => kind switch
    {
        ReferenceKind.Declaration => Strings.Msg_References_KindDeclaration,
        ReferenceKind.Write => Strings.Msg_References_KindWrite,
        ReferenceKind.Call => Strings.Msg_References_KindCall,
        ReferenceKind.TypeReference => Strings.Msg_References_KindTypeReference,
        ReferenceKind.Implementation => Strings.Msg_References_KindImplementation,
        _ => Strings.Msg_References_KindRead,
    };

    /// <summary>UI-TW-610: the colour token of a kind's badge letter.</summary>
    public static string KindToken(ReferenceKind kind) => kind switch
    {
        ReferenceKind.Declaration or ReferenceKind.Implementation => "accent.default",
        ReferenceKind.Write => "status.warning",
        _ => "text.secondary",
    };

    /// <summary>UI-TW-610: the badge letter of a reference kind — D, R, W, C, T, I.</summary>
    public static string KindLetter(ReferenceKind kind) => kind switch
    {
        ReferenceKind.Declaration => "D",
        ReferenceKind.Write => "W",
        ReferenceKind.Call => "C",
        ReferenceKind.TypeReference => "T",
        ReferenceKind.Implementation => "I",
        _ => "R",
    };

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

    /// <summary><c>searchResults.repeat</c>: the shown set's query again (IDE-FREF-008 for a References set).</summary>
    private Task Repeat()
    {
        if (_set is not { } set)
            return Task.CompletedTask;
        if (set.Request is { } request)
            return Start(request);
        if (References is not { } references)
            return Task.CompletedTask;
        int? offset = null;
        var gone = false;
        if (set.OriginAnchor is { } anchor && set.OriginDocument is { } document && _documents.Documents.Contains(document))
        {
            gone = anchor.IsDeleted;
            offset = gone ? null : anchor.Offset;
        }

        _ = references.RepeatAsync(set.Origin!, offset, gone, set);
        return Task.CompletedTask;
    }

    /// <summary>The search started last (tests await it).</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    /// <summary>Starts a search (a running one is canceled first), shows the window and streams the results (IDE-ED-123).</summary>
    public async Task RunAsync(FindInFilesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _set?.Cancel?.Cancel();
        References?.Abandon(); // a reference query would replace this search when its result arrives
        using var cancel = new CancellationTokenSource();
        var set = new SearchResultSet
        {
            Request = request,
            Cancel = cancel,
            Root = request.Scope switch
            {
                FindInFilesScope.Project => _projects.Project?.Directory ?? "",
                FindInFilesScope.Folder => request.Folder ?? "",
                _ => "",
            },
        };
        _set = set;
        _focusPending = true;
        _toolWindows?.Show(ToolName, focus: false);
        UpdateSticky();
        Rebuild();
        var progress = new BatchProgress(batch => _dispatcher.Post(() =>
        {
            if (!ReferenceEquals(_set, set) || set.Cancel is null)
                return;
            Add(set, batch);
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
            set.Cancel = null;
        }

        if (!ReferenceEquals(_set, set))
            return; // replaced by a newer search
        set.IsCapped = result.IsCapped;
        set.Matches.Clear();
        set.Anchors.Clear();
        Add(set, result.Matches);
        _sticky?.Dispose();
        _sticky = null;
        var files = result.FileCount;
        _status.ShowMessage(result.IsCanceled ? Format(Strings.Msg_FindInFiles_Canceled, result.Matches.Count)
            : result.Matches.Count == 0 ? Format(Strings.Msg_FindInFiles_None, request.Query.Text)
            : result.Matches.Count == 1 ? Strings.Msg_FindInFiles_DoneOne
            : Format(Strings.Msg_FindInFiles_DoneMany, result.Matches.Count, files), StatusDuration);
        if (set.Matches.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() > ExpandedFileLimit)
        {
            foreach (var path in set.Matches.Select(m => m.Path))
                set.Collapsed.Add(path); // UI-TW-602
        }

        Rebuild();
        FocusOnce();
        _commands?.InvalidateCanExecute();
    }

    /// <summary>
    /// IDE-FREF-003: <paramref name="result"/> becomes the shown result set (kind References) — the window is shown with
    /// its tab selected and the keyboard focus stays where it is; the reference at <paramref name="originOffset"/> of the
    /// origin's file is the current row (else the first reference). <paramref name="repeatOf"/>: the set whose query
    /// was run again (IDE-FREF-008) — it is replaced only while it is still the shown one (false: nothing was shown).
    /// </summary>
    internal bool ShowReferences(ReferencesResult result, ReferenceOrigin origin, int originOffset, object? repeatOf)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(origin);
        if (repeatOf is not null && !ReferenceEquals(repeatOf, _set))
            return false; // the repeated set was cleared or replaced meanwhile
        _set?.Cancel?.Cancel();
        _sticky?.Dispose();
        _sticky = null;
        _focusPending = false;
        var set = new SearchResultSet
        {
            Origin = origin,
            Identifier = result.Identifier,
            Root = _projects.Project?.Directory ?? "",
            IsCapped = result.IsCapped,
        };
        _set = set;
        Add(set, result.Matches);
        if (OpenDocuments().TryGetValue(origin.Path, out var document) && originOffset <= document.Buffer.Length)
        {
            set.OriginDocument = document;
            set.OriginAnchor = document.Buffer.CreateAnchor(originOffset, movesWithInsertionBefore: true);
        }

        // UI-TW-612: everything expanded up to 50 files, else all collapsed; library file rows always start collapsed.
        var many = result.Matches.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() > ExpandedFileLimit;
        foreach (var match in result.Matches)
        {
            if (many || match.IsLibrary)
                set.Collapsed.Add(match.Path);
            if (many)
                set.Collapsed.Add(ContainerId(match));
        }

        set.Current = result.Matches.FirstOrDefault(m => m.Line == origin.Line && m.Column == origin.Column
            && string.Equals(m.Path, origin.Path, StringComparison.OrdinalIgnoreCase)) ?? result.Matches.FirstOrDefault();
        _toolWindows?.Show(ToolName, focus: false);
        ResultsShown?.Invoke(this, EventArgs.Empty);
        Rebuild();
        OnPropertyChanged(nameof(CurrentRow));
        return true;
    }

    /// <summary>A reference query started or ended: the header of a repeated set and <c>searchResults.cancel</c> follow it.</summary>
    internal void OnReferenceSearchChanged()
    {
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(IsSearching));
        _commands?.InvalidateCanExecute();
    }

    /// <summary>Clears the results (<c>searchResults.clear</c>): the empty state.</summary>
    public void Clear()
    {
        _set?.Cancel?.Cancel();
        if (_set is not null && ReferenceEquals(References?.RepeatTarget, _set))
            References!.Abandon();
        _sticky?.Dispose();
        _sticky = null;
        _set = null;
        Rebuild();
    }

    /// <summary>Expands or collapses a file row or a container row.</summary>
    public void SetExpanded(SearchResultRow row, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Match is not null || _set is not { } set)
            return;
        if (expanded)
            set.Collapsed.Remove(row.GroupId);
        else
            set.Collapsed.Add(row.GroupId);
        Rebuild();
    }

    /// <summary>
    /// UI-TW-603 / IDE-FREF-007: the default action — a match opens its file with the span selected; a file row or a
    /// container row toggles.
    /// </summary>
    public async Task GoToAsync(SearchResultRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Match is not { } match)
        {
            SetExpanded(row, !row.IsExpanded);
            return;
        }

        var set = _set;
        if (set is not null)
        {
            set.Current = match;
            OnPropertyChanged(nameof(CurrentRow));
        }

        var opened = await _documents.OpenAsync(match.Path);
        if (opened.Value is not TextDocumentViewModel document)
        {
            if (opened.NeedsMessage)
                await _dialogs.ShowMessageAsync(new MessageRequest(opened.MessageId!, opened.Args));
            return;
        }

        var buffer = document.Buffer;
        int start;
        if (set is not null && set.Anchors.TryGetValue(match, out var anchor) && !anchor.IsDeleted)
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
        if (_set is not { Matches.Count: > 0 } set)
            return Task.CompletedTask;
        var matches = set.Matches;
        var index = set.Current is null ? -1 : matches.IndexOf(set.Current);
        var next = index < 0 ? (forward ? 0 : matches.Count - 1) : (index + (forward ? 1 : -1) + matches.Count) % matches.Count;
        set.Current = matches[next];
        var row = Rows.FirstOrDefault(r => r.Match == set.Current) ?? MatchRow(set.Current, KeyOf(set, set.Current.Path));
        return GoToAsync(row);
    }

    protected override void OnFilterApplied(string filter) => Rebuild();

    private Task SetAllExpanded(bool expanded)
    {
        if (_set is not { } set)
            return Task.CompletedTask;
        set.Collapsed.Clear();
        if (!expanded)
        {
            foreach (var match in set.Matches)
            {
                set.Collapsed.Add(match.Path);
                if (match is ReferenceMatch reference)
                    set.Collapsed.Add(ContainerId(reference));
            }
        }

        Rebuild();
        return Task.CompletedTask;
    }

    private void FocusOnce()
    {
        if (!_focusPending || (!HasMatches && IsSearching))
            return;
        _focusPending = false;
        _toolWindows?.Show(ToolName, focus: true);
    }

    private void UpdateSticky()
    {
        _sticky?.Dispose();
        var matches = _set?.Matches ?? [];
        var files = matches.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        _sticky = _status.ShowStickyMessage(Format(Strings.Msg_FindInFiles_Searching, files, matches.Count));
    }

    private Dictionary<string, TextDocumentViewModel> OpenDocuments() =>
        _documents.Documents.OfType<TextDocumentViewModel>().Where(d => d.FilePath is not null)
            .GroupBy(d => d.FilePath!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private void Add(SearchResultSet set, IEnumerable<FileMatch> matches)
    {
        var open = OpenDocuments();
        foreach (var match in matches)
        {
            set.Matches.Add(match);
            if (open.TryGetValue(match.Path, out var document))
                Anchor(set, match, document);
        }
    }

    private void OnDocumentOpened(DocumentViewModel opened)
    {
        if (_set is not { } set || opened is not TextDocumentViewModel { FilePath: { } path, IsModified: false } document)
            return;
        foreach (var match in set.Matches.Where(m => !set.Anchors.ContainsKey(m) && string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase)))
            Anchor(set, match, document);

        // IDE-FREF-008: the origin of a References set is tracked again in its reopened document.
        if (set.Origin is { } origin && string.Equals(origin.Path, path, StringComparison.OrdinalIgnoreCase)
            && (set.OriginDocument is null || !_documents.Documents.Contains(set.OriginDocument)) && origin.Line <= document.Buffer.LineCount)
        {
            var (lineStart, lineLength, _) = document.Buffer.GetLine(origin.Line);
            set.OriginDocument = document;
            set.OriginAnchor = document.Buffer.CreateAnchor(lineStart + Math.Clamp(origin.Column, 1, lineLength + 1) - 1, movesWithInsertionBefore: true);
        }
    }

    private static void Anchor(SearchResultSet set, FileMatch match, TextDocumentViewModel document)
    {
        var buffer = document.Buffer;
        if (match.Line > buffer.LineCount)
            return;
        var (lineStart, lineLength, _) = buffer.GetLine(match.Line);
        set.Anchors[match] = buffer.CreateAnchor(lineStart + Math.Clamp(match.Column, 1, lineLength + 1) - 1, movesWithInsertionBefore: true);
    }

    private static string KeyOf(SearchResultSet set, string path)
    {
        if (set.Root.Length > 0)
        {
            var relative = System.IO.Path.GetRelativePath(set.Root, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !System.IO.Path.IsPathFullyQualified(relative))
                return relative.Replace('\\', '/');
        }

        return path;
    }

    /// <summary>The <see cref="SearchResultRow.GroupId"/> of the container row of <paramref name="match"/> (a file row: its path).</summary>
    private static string ContainerId(ReferenceMatch match) => match.Path + "\0" + match.ContainerIndex.ToString(CultureInfo.InvariantCulture);

    private void Rebuild()
    {
        var filter = FilterText.Trim();
        List<SearchResultRow> rows = [];
        if (_set is { } set)
        {
            foreach (var group in set.Matches.GroupBy(m => m.Path, StringComparer.OrdinalIgnoreCase))
            {
                var key = KeyOf(set, group.Key);
                var fileMatches = filter.Length == 0 || System.IO.Path.GetFileName(group.Key).Contains(filter, StringComparison.OrdinalIgnoreCase);
                if (set.Origin is not null)
                    AddReferenceRows(rows, set, group, key, filter, fileMatches);
                else
                    AddMatchRows(rows, set, group, key, filter, fileMatches);
            }
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

    /// <summary>UI-TW-601 / UI-TW-605: the file row and, expanded, its match rows.</summary>
    private static void AddMatchRows(List<SearchResultRow> rows, SearchResultSet set, IGrouping<string, FileMatch> group, string key, string filter,
        bool fileMatches)
    {
        var matches = group.Where(m => fileMatches || m.LineText.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
            return;
        var expanded = filter.Length > 0 || !set.Collapsed.Contains(group.Key);
        rows.Add(new SearchResultRow { Key = key, Path = group.Key, GroupId = group.Key, Count = group.Count(), IsExpanded = expanded });
        if (expanded)
            rows.AddRange(matches.Select(m => MatchRow(m, key)));
    }

    /// <summary>
    /// UI-TW-609 / UI-TW-613: the file row, its container rows and their reference rows; the filter keeps a container
    /// whose name matches (with all its references) or that has a reference whose line matches.
    /// </summary>
    private static void AddReferenceRows(List<SearchResultRow> rows, SearchResultSet set, IGrouping<string, FileMatch> group, string key, string filter,
        bool fileMatches)
    {
        var fileExpanded = filter.Length > 0 || !set.Collapsed.Contains(group.Key);
        List<SearchResultRow> below = [];
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var container in group.Cast<ReferenceMatch>().GroupBy(m => m.ContainerIndex))
        {
            var first = container.First();
            var ordinal = names[first.Container] = names.GetValueOrDefault(first.Container) + 1; // UI-TW-614: a second container of the name is @2
            var containerMatches = fileMatches || first.Container.Contains(filter, StringComparison.OrdinalIgnoreCase);
            var references = container.Where(m => containerMatches || m.LineText.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (references.Count == 0)
                continue;
            var id = ContainerId(first);
            var expanded = filter.Length > 0 || !set.Collapsed.Contains(id);
            below.Add(new SearchResultRow
            {
                Key = $"{key}#{first.Container}" + (ordinal > 1 ? "@" + ordinal.ToString(CultureInfo.InvariantCulture) : ""),
                Path = group.Key,
                Container = first.Container,
                GroupId = id,
                Count = container.Count(),
                IsExpanded = expanded,
            });
            if (expanded)
                below.AddRange(references.Select(m => MatchRow(m, key)));
        }

        if (below.Count == 0)
            return;
        rows.Add(new SearchResultRow
        {
            Key = key,
            Path = group.Key,
            GroupId = group.Key,
            Count = group.Count(),
            IsExpanded = fileExpanded,
            IsLibrary = group.First() is ReferenceMatch { IsLibrary: true },
        });
        if (fileExpanded)
            rows.AddRange(below);
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
        if (_documents.ActiveDocument?.VisibleText is not { } document) // I-302: the editor of the visible view
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
