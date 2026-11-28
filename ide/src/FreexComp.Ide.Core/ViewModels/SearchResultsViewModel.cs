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
/// rows, the current row and the running search — and, while another set is shown, the selection and the scroll
/// position. The window keeps several sets (UI-TW-615) and shows one of them.
/// </summary>
internal sealed class SearchResultSet
{
    /// <summary>UI-TW-616: kept (<c>searchResults.pin</c>) — no other search replaces, cancels or changes it.</summary>
    public bool IsPinned { get; set; }

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

    /// <summary>The sticky status text of the running Find in Files search of this set.</summary>
    public IDisposable? Sticky { get; set; }

    /// <summary>UI-TW-618: the keys of the rows that were selected when another set was shown instead.</summary>
    public IReadOnlyList<string> SelectedKeys { get; set; } = [];

    /// <summary>UI-TW-618: the vertical scroll position of the list (the view writes it before another set is shown).</summary>
    public double ScrollOffset { get; set; }
}

/// <summary>
/// A tab of the set strip of Search Results (UI-TW-615): <see cref="Number"/> is its 1-based position, <see cref="Title"/>
/// the search text in double quotes or the identifier (cut to 24 characters), <see cref="IconName"/> the icon of its
/// kind and <see cref="Tooltip"/> the set's header text.
/// </summary>
public sealed record SearchResultSetTab(int Number, string Title, string IconName, bool IsPinned, bool IsShown, string Tooltip);

/// <summary>
/// The Search Results tool window (tool-windows.md §8, editor-features.md §12): runs Find in Files
/// (<c>search.findInFiles</c>, the dialog of dialogs.md §11) and shows one result set — of kind Find in Files (a
/// two-level tree) or References (<see cref="ShowReferences"/>: file, container, reference; UI-TW-609…614); owns the
/// <c>searchResults.*</c> commands. Positions of documents open at search time (or opened later unmodified) are anchored
/// (UI-TW-604). It holds the result sets (UI-TW-615…618): at most one current set, which a new search replaces, and up
/// to <see cref="MaxPinnedSets"/> pinned ones (<c>searchResults.pin</c>), in the order of the set strip — the pinned
/// sets in creation order, the current one last; exactly one of them is shown. UI thread only.
/// </summary>
public sealed class SearchResultsViewModel : ToolWindowViewModel, ICommandTarget
{
    public const string ToolName = "SearchResults";

    /// <summary>UI-TW-616: <c>Keep Results</c>.</summary>
    public const string PinCommandId = "searchResults.pin";

    /// <summary>UI-TW-615: the window keeps at most this many pinned sets.</summary>
    public const int MaxPinnedSets = 10;

    /// <summary>UI-TW-615: a longer tab title is cut to this many characters, the last one an ellipsis.</summary>
    public const int MaxSetTitleLength = 24;

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
    private readonly List<SearchResultSet> _sets = []; // strip order: the pinned sets in creation order, the current one last
    private SearchResultSet? _shown; // the shown result set; null = the empty state (there is no set)
    private SearchResultSet? _focusFor; // the Find in Files search that takes the keyboard focus with its first rows
    private IReadOnlyList<string>? _restoreSelection; // the selection of the set that Rebuild shows next
    private string _builtFilter = "";
    private bool _pinAvailable;
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

    /// <summary>tool-windows.md §8; <c>searchResults.pin</c> where it is delivered (UI-TW-616).</summary>
    public override IReadOnlyList<ToolWindowToolBarItem> ToolBarCommands =>
    [
        new("searchResults.repeat", "refresh"), new("searchResults.cancel", "stop"), new("searchResults.expandAll", "chevron-down"),
        new("searchResults.collapseAll", "collapse-all"), .. _pinAvailable ? [new ToolWindowToolBarItem(PinCommandId, "pin")] : Array.Empty<ToolWindowToolBarItem>(),
        new("searchResults.clear", "trash"),
    ];

    public override bool HasFilter => true;

    /// <summary>tool-windows.md §8 context menu (null = separator).</summary>
    public static readonly IReadOnlyList<string?> ContextCommands =
    [
        "searchResults.goToSource", null, "edit.copy", null, "searchResults.expandAll", "searchResults.collapseAll", null,
        PinCommandId, "searchResults.repeat", "search.findInFiles", null, "searchResults.clear",
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
    public bool IsSearching => _shown is { } set && Searching(set);

    private bool Searching(SearchResultSet set) => set.Cancel is not null || References?.IsRepeating(set) == true;

    /// <summary>Never searched, or every set was removed: <c>Msg.SearchResults.Empty</c>.</summary>
    public bool IsEmpty => _shown is null;

    /// <summary>The shown set is of kind References (UI-TW-609).</summary>
    public bool IsReferences => _shown?.Origin is not null;

    /// <summary>The search finished without a match: <c>Msg.FindInFiles.None</c> under the header.</summary>
    public bool IsNoMatch => _shown is { Request: not null } set && !IsSearching && set.Matches.Count == 0;

    public string NoMatchText => _shown?.Request is { } request ? Format(Strings.Msg_FindInFiles_None, request.Query.Text) : "";

    /// <summary>UI-TW-606 / IDE-FREF-006: the info bar <c>Msg.FindInFiles.Capped</c> or <c>Msg.References.Capped</c>.</summary>
    public bool IsCapped => _shown?.IsCapped == true;

    /// <summary>UI-TW-615: the tabs of the set strip, in its order.</summary>
    public IReadOnlyList<SearchResultSetTab> Sets =>
    [
        .. _sets.Select((set, index) => new SearchResultSetTab(index + 1,
            set.Origin is null ? SetTitle(set.Request!.Query.Text, quoted: true) : SetTitle(set.Identifier, quoted: false),
            set.Origin is null ? "find-in-files" : "find", set.IsPinned, ReferenceEquals(set, _shown), HeaderOf(set))),
    ];

    /// <summary>UI-TW-615: the set strip is shown while at least one set is pinned.</summary>
    public bool HasSetStrip => _sets.Exists(s => s.IsPinned);

    /// <summary>UI-TW-618: the scroll position of the shown set's list; the view keeps it here while another set is shown.</summary>
    public double ScrollOffset
    {
        get => _shown?.ScrollOffset ?? 0;
        set
        {
            if (_shown is { } set)
                set.ScrollOffset = value;
        }
    }

    /// <summary>Another set is about to be shown: the view stores the scroll position of the one that leaves (<see cref="ScrollOffset"/>).</summary>
    public event EventHandler? ShownSetChanging;

    /// <summary>Another set is shown, with its rows and its selection: the view restores its scroll position.</summary>
    public event EventHandler? ShownSetChanged;

    /// <summary>
    /// UI-TW-616: the shown set is the current one, its search has ended, it has rows, and fewer than
    /// <see cref="MaxPinnedSets"/> sets are pinned.
    /// </summary>
    public bool CanPin => _shown is { IsPinned: false, Matches.Count: > 0 } set && !Searching(set) && _sets.Count(s => s.IsPinned) < MaxPinnedSets;

    /// <summary>
    /// UI-TW-615: the title of a set's tab — the search text in double quotes, or the identifier; longer than
    /// <see cref="MaxSetTitleLength"/> characters (the quotes included) it is cut, with an ellipsis before the closing quote.
    /// </summary>
    internal static string SetTitle(string text, bool quoted)
    {
        var room = MaxSetTitleLength - (quoted ? 2 : 0);
        if (text.Length > room)
            text = string.Concat(text.AsSpan(0, room - 1), "…");
        return quoted ? "\"" + text + "\"" : text;
    }

    public string CappedText => Format(IsReferences ? Strings.Msg_References_Capped : Strings.Msg_FindInFiles_Capped,
        FindInFilesService.MatchCap.ToString("N0", CultureInfo.CurrentCulture));

    /// <summary>
    /// UI-TW-600: <c>Find "&lt;text&gt;" in &lt;scope&gt; — &lt;counts&gt; (&lt;options&gt;)</c>; UI-TW-611:
    /// <c>References to &lt;name&gt; — &lt;counts&gt;</c> with the library part.
    /// </summary>
    public string Header => _shown is { } set ? HeaderOf(set) : "";

    /// <summary>The header text of a set: what its header strip shows, and the tooltip of its tab (UI-TW-615).</summary>
    private string HeaderOf(SearchResultSet set)
    {
        var searching = Searching(set);
        var matches = set.Matches;
        var files = matches.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (set.Origin is not null)
        {
            if (searching)
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
        var counts = searching ? Strings.SearchResults_Searching
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

    /// <summary>The match row navigated to last (F8 continues from it).</summary>
    public SearchResultRow? CurrentRow => _shown?.Current is { } current ? Rows.FirstOrDefault(r => r.Match == current) : null;

    public bool HasMatches => _shown is { Matches.Count: > 0 };

    /// <param name="implementedPhase">Decides whether <c>searchResults.pin</c> — a Phase 8 command — is offered (IDE-P8-003).</param>
    public void RegisterCommands(ICommandService commands, IToolWindowService toolWindows, int implementedPhase)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        _toolWindows = toolWindows;
        commands.RegisterHandler("search.findInFiles", new Handler(() => toolWindows.IsRegistered(ToolName), OpenDialogAsync));
        commands.RegisterHandler("searchResults.repeat", new Handler(() => _shown is not null, Repeat));

        // UI-TW-617: the running search of the shown set; IDE-FREF-002: and a new reference query, which is no set yet.
        commands.RegisterHandler("searchResults.cancel", new Handler(() => IsSearching || References?.IsFinding == true, () =>
        {
            _shown?.Cancel?.Cancel();
            References?.Cancel(_shown);
            return Task.CompletedTask;
        }));
        commands.RegisterHandler("searchResults.clear", new Handler(() => _shown is not null, () =>
        {
            Clear();
            return Task.CompletedTask;
        }));
        _pinAvailable = Composition.Phase8Features.IsAvailable(PinCommandId, 8, implementedPhase);
        if (_pinAvailable)
        {
            commands.RegisterHandler(PinCommandId, new Handler(() => CanPin, () =>
            {
                Pin();
                return Task.CompletedTask;
            }));
        }

        commands.RegisterHandler("searchResults.expandAll", new Handler(() => HasMatches, () => SetAllExpanded(true)));
        commands.RegisterHandler("searchResults.collapseAll", new Handler(() => HasMatches, () => SetAllExpanded(false)));
        commands.RegisterHandler("searchResults.goToSource", new Handler(() => SelectedRows.Count > 0, () => GoToAsync(SelectedRows[0])));
    }

    public IReadOnlyList<Menus.MenuItemModel> BuildContextMenu()
    {
        var commands = _commands ?? throw new InvalidOperationException("Commands are not registered.");
        return [.. ContextCommands.Where(id => id != PinCommandId || _pinAvailable).Select(id => id is null
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
    private Task Start(FindInFilesRequest request, SearchResultSet? repeatOf = null)
    {
        Running = RunAsync(request, repeatOf);
        return Task.CompletedTask;
    }

    /// <summary>
    /// <c>searchResults.repeat</c>: the shown set's query again, in place (UI-TW-617: a pinned set stays pinned and keeps
    /// its position; IDE-FREF-008 for a References set).
    /// </summary>
    private Task Repeat()
    {
        if (_shown is not { } set)
            return Task.CompletedTask;
        if (set.Request is { } request)
            return Start(request, set);
        if (References is not { } references)
            return Task.CompletedTask;
        int? offset = null;
        var gone = false;
        if (set.OriginAnchor is { } anchor && set.OriginDocument is { } document && _documents.Documents.Contains(document))
        {
            gone = anchor.IsDeleted;
            offset = gone ? null : anchor.Offset;
        }

        _ = references.RepeatAsync(set.Origin!, offset, gone, set, set.IsPinned);
        return Task.CompletedTask;
    }

    /// <summary>The search started last (tests await it).</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Starts a search, shows the window and streams the results (IDE-ED-123). UI-TW-617: the search becomes the current
    /// set — the previous current set is replaced and its running search canceled — and is shown; pinned sets are not touched.
    /// </summary>
    public Task RunAsync(FindInFilesRequest request) => RunAsync(request, repeatOf: null);

    /// <param name="repeatOf">The set whose query is run again (<c>searchResults.repeat</c>): the new set takes its place in the strip.</param>
    private async Task RunAsync(FindInFilesRequest request, SearchResultSet? repeatOf)
    {
        ArgumentNullException.ThrowIfNull(request);
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
        var place = repeatOf is null ? -1 : _sets.IndexOf(repeatOf);
        if (place >= 0)
        {
            Release(repeatOf!);
            set.IsPinned = repeatOf!.IsPinned;
            _sets[place] = set;
        }
        else
        {
            ReplaceCurrent(set);
        }

        if (!set.IsPinned)
            References?.AbandonCurrent(); // a reference query would replace this search when its result arrives
        var shown = place < 0 || ReferenceEquals(_shown, repeatOf);
        if (shown)
        {
            _focusFor = set;
            _toolWindows?.Show(ToolName, focus: false);
        }

        UpdateSticky(set);
        if (shown)
            Show(set, sameTab: place >= 0);
        var progress = new BatchProgress(batch => _dispatcher.Post(() =>
        {
            if (set.Cancel is null || !_sets.Contains(set))
                return;
            Add(set, batch);
            UpdateSticky(set);
            if (!ReferenceEquals(_shown, set))
                return;
            Rebuild();
            FocusOnce(set);
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

        if (!_sets.Contains(set))
            return; // replaced by a newer search, or closed
        set.IsCapped = result.IsCapped;
        set.Matches.Clear();
        set.Anchors.Clear();
        Add(set, result.Matches);
        set.Sticky?.Dispose();
        set.Sticky = null;
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

        if (ReferenceEquals(_shown, set))
        {
            Rebuild();
            FocusOnce(set);
        }
        else
        {
            if (ReferenceEquals(_focusFor, set))
                _focusFor = null; // another set is looked at: this search does not take the focus any more
            NotifySets(); // the tooltip of its tab
        }

        _commands?.InvalidateCanExecute();
    }

    /// <summary>
    /// IDE-FREF-003: <paramref name="result"/> becomes the current result set (kind References) and is shown — the window
    /// is shown with its tab selected and the keyboard focus stays where it is; the reference at
    /// <paramref name="originOffset"/> of the origin's file is the current row (else the first reference).
    /// <paramref name="repeatOf"/>: the set whose query was run again (IDE-FREF-008, UI-TW-617) — the result takes its
    /// place in the strip, pinned as it was, and is shown when that set was the shown one (false: the set was removed or
    /// replaced meanwhile, nothing was changed).
    /// </summary>
    internal bool ShowReferences(ReferencesResult result, ReferenceOrigin origin, int originOffset, object? repeatOf)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(origin);
        var repeated = repeatOf as SearchResultSet;
        var place = repeated is null ? -1 : _sets.IndexOf(repeated);
        if (repeatOf is not null && place < 0)
            return false; // the repeated set was removed or replaced meanwhile
        var set = new SearchResultSet
        {
            Origin = origin,
            Identifier = result.Identifier,
            Root = _projects.Project?.Directory ?? "",
            IsCapped = result.IsCapped,
            IsPinned = repeated?.IsPinned == true,
        };
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
        if (place >= 0)
        {
            _sets[place] = set; // in place
            if (!ReferenceEquals(_shown, repeated))
            {
                NotifySets(); // another set is shown and stays shown: the tooltip of this one's tab
                return true;
            }
        }
        else
        {
            ReplaceCurrent(set);
            _focusFor = null;
        }

        _toolWindows?.Show(ToolName, focus: false);
        ResultsShown?.Invoke(this, EventArgs.Empty);
        Show(set, sameTab: place >= 0);
        OnPropertyChanged(nameof(CurrentRow));
        return true;
    }

    /// <summary>A reference query started or ended: the header of a repeated set and <c>searchResults.cancel</c> follow it.</summary>
    internal void OnReferenceSearchChanged()
    {
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(IsSearching));
        NotifySets();
    }

    /// <summary>
    /// <c>searchResults.clear</c> (UI-TW-618): removes the shown set; the next one in the strip is shown, else the
    /// previous one, else the empty state.
    /// </summary>
    public void Clear()
    {
        if (_shown is { } set)
            Remove(set);
    }

    /// <summary>UI-TW-616 <c>searchResults.pin</c>: the shown set is kept. One way: a pinned set is removed, never unpinned.</summary>
    public void Pin()
    {
        if (!CanPin)
            return;
        _shown!.IsPinned = true;
        NotifySets();
    }

    /// <summary>UI-TW-615: shows the set of the tab <paramref name="number"/> (1-based position in the strip).</summary>
    public void ShowSet(int number)
    {
        if (number >= 1 && number <= _sets.Count)
            Show(_sets[number - 1]);
    }

    /// <summary>UI-TW-615 <c>Left</c> / <c>Right</c> on the strip: shows the set before or after the shown one (no wrapping).</summary>
    public void ShowNeighbourSet(bool forward)
    {
        if (_shown is { } set)
            ShowSet(_sets.IndexOf(set) + 1 + (forward ? 1 : -1));
    }

    /// <summary>UI-TW-618: a tab's close button — removes the set of the tab <paramref name="number"/> (as <see cref="Clear"/> when it is the shown one).</summary>
    public void CloseSet(int number)
    {
        if (number >= 1 && number <= _sets.Count)
            Remove(_sets[number - 1]);
    }

    /// <summary>The search of <paramref name="set"/> — a set that is replaced or removed — ends without a result and without a message.</summary>
    private void Release(SearchResultSet set)
    {
        set.Cancel?.Cancel();
        References?.Abandon(set);
        set.Sticky?.Dispose();
        set.Sticky = null;
    }

    /// <summary>UI-TW-617: <paramref name="set"/> becomes the current set; the previous current set is gone, its running search canceled.</summary>
    private void ReplaceCurrent(SearchResultSet set)
    {
        if (_sets.Find(s => !s.IsPinned) is { } previous)
        {
            Release(previous);
            _sets.Remove(previous);
        }

        _sets.Add(set);
    }

    private void Remove(SearchResultSet set)
    {
        var place = _sets.IndexOf(set);
        if (place < 0)
            return;
        Release(set);
        _sets.RemoveAt(place);
        if (ReferenceEquals(_focusFor, set))
            _focusFor = null;
        if (ReferenceEquals(_shown, set))
            Show(place < _sets.Count ? _sets[place] : place > 0 ? _sets[place - 1] : null);
        else
            NotifySets();
    }

    /// <summary>
    /// Makes <paramref name="set"/> the shown one (null: the empty state) with the state that belongs to it (UI-TW-618):
    /// its rows, expansion, selection, current row and — through the view — scroll position. The filter text is cleared,
    /// unless the set takes the place of the shown one (<paramref name="sameTab"/>: a repeat).
    /// </summary>
    private void Show(SearchResultSet? set, bool sameTab = false)
    {
        if (ReferenceEquals(_shown, set))
            return;
        ShownSetChanging?.Invoke(this, EventArgs.Empty);
        if (_shown is { } previous)
            previous.SelectedKeys = [.. SelectedRows.Select(r => r.Key)];
        _shown = set;
        _restoreSelection = set?.SelectedKeys ?? [];
        if (!sameTab && FilterText.Length > 0)
            FilterText = "";
        Rebuild();
        ShownSetChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The strip changed (a set was pinned, replaced or removed, or the header of a set that is not shown changed).</summary>
    private void NotifySets()
    {
        OnPropertyChanged(nameof(Sets));
        OnPropertyChanged(nameof(HasSetStrip));
        _commands?.InvalidateCanExecute();
    }

    /// <summary>Expands or collapses a file row or a container row.</summary>
    public void SetExpanded(SearchResultRow row, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Match is not null || _shown is not { } set)
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

        var set = _shown;
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

    /// <summary>F8 / Shift+F8 over the match rows of the shown set (UI-TW-603, UI-TW-618), wrapping.</summary>
    public Task NavigateNextAsync(bool forward)
    {
        if (_shown is not { Matches.Count: > 0 } set)
            return Task.CompletedTask;
        var matches = set.Matches;
        var index = set.Current is null ? -1 : matches.IndexOf(set.Current);
        var next = index < 0 ? (forward ? 0 : matches.Count - 1) : (index + (forward ? 1 : -1) + matches.Count) % matches.Count;
        set.Current = matches[next];
        var row = Rows.FirstOrDefault(r => r.Match == set.Current) ?? MatchRow(set.Current, KeyOf(set, set.Current.Path));
        return GoToAsync(row);
    }

    protected override void OnFilterApplied(string filter)
    {
        // Nothing to do when the rows were built with this text already (a batch arrived, or the text was cleared for
        // another set): building them again would only reset the list.
        if (filter.Trim() != _builtFilter)
            Rebuild();
    }

    private Task SetAllExpanded(bool expanded)
    {
        if (_shown is not { } set)
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

    /// <summary>The shown Find in Files search takes the keyboard focus once: with its first rows, or at its end.</summary>
    private void FocusOnce(SearchResultSet set)
    {
        if (!ReferenceEquals(_focusFor, set) || (set.Matches.Count == 0 && set.Cancel is not null))
            return;
        _focusFor = null;
        _toolWindows?.Show(ToolName, focus: true);
    }

    private void UpdateSticky(SearchResultSet set)
    {
        set.Sticky?.Dispose();
        var matches = set.Matches;
        var files = matches.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        set.Sticky = _status.ShowStickyMessage(Format(Strings.Msg_FindInFiles_Searching, files, matches.Count));
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

    /// <summary>UI-TW-604 / UI-TW-618: the rows of every set — shown or not — are tracked in a document that is opened unmodified.</summary>
    private void OnDocumentOpened(DocumentViewModel opened)
    {
        if (opened is not TextDocumentViewModel { FilePath: { } path, IsModified: false } document)
            return;
        foreach (var set in _sets)
            TrackIn(set, path, document);
    }

    private void TrackIn(SearchResultSet set, string path, TextDocumentViewModel document)
    {
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
        var filter = _builtFilter = FilterText.Trim();
        List<SearchResultRow> rows = [];
        if (_shown is { } set)
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

        // The selection first: the view takes it over when the rows change (the rows of a set that is shown again get
        // the selection that set had, UI-TW-618).
        var selected = (_restoreSelection ?? SelectedRows.Select(r => r.Key)).ToHashSet(StringComparer.Ordinal);
        _restoreSelection = null;
        SelectedRows = [.. rows.Where(r => selected.Contains(r.Key))];
        Rows = rows;
        OnPropertyChanged(nameof(Sets));
        OnPropertyChanged(nameof(HasSetStrip));
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
