using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Layout;

namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// In-editor find and replace (phase1-shell.md §10): the search text, replacement and options shared by all editors
/// (UI-DLG-102), and one <see cref="FindSession"/> per text document holding its panel state and matches. Owns
/// <c>search.findNext</c>, <c>search.findPrevious</c> and <c>search.findSelectionNext</c>. UI thread only.
/// </summary>
public interface IFindService
{
    /// <summary>The search text of this session, shared by all editors (initially empty).</summary>
    string SearchText { get; set; }

    string ReplaceText { get; set; }

    /// <summary>Persisted as <c>find.matchCase</c> in <c>uistate.json</c>.</summary>
    bool MatchCase { get; set; }

    /// <summary>Persisted as <c>find.wholeWord</c>.</summary>
    bool WholeWord { get; set; }

    /// <summary>Persisted as <c>find.regex</c>.</summary>
    bool Regex { get; set; }

    /// <summary>The search text and options as one query.</summary>
    FindQuery Query { get; }

    /// <summary>The search text or an option changed (not the replacement).</summary>
    event EventHandler? QueryChanged;

    /// <summary>The find state of <paramref name="document"/> (created on first use, dropped when it closes).</summary>
    FindSession For(TextDocumentViewModel document);

    /// <summary>UI-DLG-009: search texts used for a navigation or replace, most recent first (<c>dialogs.Find.history.SearchText</c>).</summary>
    IReadOnlyList<string> SearchHistory { get; }

    /// <summary>Replacements used, most recent first (<c>dialogs.Find.history.ReplaceText</c>).</summary>
    IReadOnlyList<string> ReplaceHistory { get; }
}

/// <inheritdoc cref="IFindService"/>
public sealed class FindService : IFindService
{
    private const string Section = "find";
    private readonly ILayoutService _layout;
    private readonly IDocumentService _documents;
    private readonly Dictionary<TextDocumentViewModel, FindSession> _sessions = [];
    private string _searchText = "";
    private string _replaceText = "";

    public FindService(ILayoutService layout, IStatusService status, IClock clock, IDocumentService documents)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        Status = status ?? throw new ArgumentNullException(nameof(status));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        documents.DocumentOpened += (_, e) =>
        {
            if (TextOf(e.Document) is { } text)
                text.FindSource = For; // the editor view binds its panel through For(doc)
        };
        documents.DocumentClosed += (_, e) =>
        {
            if (TextOf(e.Document) is { } text && _sessions.Remove(text, out var session))
                session.Detach();
        };
    }

    private const string HistoryDialog = "Find";

    public IReadOnlyList<string> SearchHistory => _layout.GetDialogHistory(HistoryDialog, "SearchText");

    public IReadOnlyList<string> ReplaceHistory => _layout.GetDialogHistory(HistoryDialog, "ReplaceText");

    /// <summary>IDE-P1-108: a navigation was performed with the search text.</summary>
    internal void RecordSearch()
    {
        if (_searchText.Length > 0)
            _layout.AddDialogHistory(HistoryDialog, "SearchText", _searchText);
    }

    /// <summary>A replace was performed: the search text and the replacement (an empty replacement is not recorded).</summary>
    internal void RecordReplace()
    {
        RecordSearch();
        if (_replaceText.Length > 0)
            _layout.AddDialogHistory(HistoryDialog, "ReplaceText", _replaceText);
    }

    /// <summary>The text document behind a document tab: a text file, or the content of a generated document.</summary>
    private static TextDocumentViewModel? TextOf(DocumentViewModel? document) => document switch
    {
        TextDocumentViewModel text => text,
        GeneratedTextDocumentViewModel generated => generated.Content,
        _ => null,
    };

    public event EventHandler? QueryChanged;

    internal IStatusService Status { get; }

    internal IClock Clock { get; }

    public string SearchText
    {
        get => _searchText;
        set => SetQueryPart(ref _searchText, value ?? "");
    }

    public string ReplaceText
    {
        get => _replaceText;
        set => _replaceText = value ?? "";
    }

    public bool MatchCase
    {
        get => _layout.GetUiFlag(Section, "matchCase") ?? false;
        set => SetFlag("matchCase", value);
    }

    public bool WholeWord
    {
        get => _layout.GetUiFlag(Section, "wholeWord") ?? false;
        set => SetFlag("wholeWord", value);
    }

    public bool Regex
    {
        get => _layout.GetUiFlag(Section, "regex") ?? false;
        set => SetFlag("regex", value);
    }

    public FindQuery Query => new(_searchText, MatchCase, WholeWord, Regex);

    public FindSession For(TextDocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_sessions.TryGetValue(document, out var session))
            _sessions[document] = session = new FindSession(this, document);
        return session;
    }

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        // menus.md "text" (Replace: "text and writable"). UI-DLG-104 (lead decision for I-058): Find Next / Previous
        // without a search text open the panel, so they are enabled in every text document.
        commands.RegisterHandler("search.find", new Handler(() => Active is not null, () => OpenPanel(replace: false)));
        commands.RegisterHandler("search.replace", new Handler(() => Active is { IsReadOnly: false }, () => OpenPanel(replace: true)));
        commands.RegisterHandler("search.findNext", new Handler(() => Active is not null, () => FindNextAsync(forward: true)));
        commands.RegisterHandler("search.findPrevious", new Handler(() => Active is not null, () => FindNextAsync(forward: false)));
        commands.RegisterHandler("search.findSelectionNext", new Handler(
            () => Active is not null, () => For(Active!).FindSelectionNextAsync()));
    }

    private TextDocumentViewModel? Active => TextOf(_documents.ActiveDocument);

    /// <summary>
    /// UI-DLG-100: opens the panel (seeded from the selection) in find or replace mode; <c>search.replace</c> in an
    /// already open panel toggles the replace row.
    /// </summary>
    private Task<bool> OpenPanel(bool replace)
    {
        var session = For(Active!);
        if (replace && session.IsOpen)
        {
            session.SetReplaceMode(!session.IsReplaceMode);
            session.RequestFocus();
        }
        else
        {
            session.Open(replace);
        }

        return Task.FromResult(true);
    }

    private Task<bool> FindNextAsync(bool forward)
    {
        var session = For(Active!);
        if (_searchText.Length > 0)
            return session.FindNextAsync(forward);
        session.Open(replace: false); // UI-DLG-104: no previous search
        return Task.FromResult(true);
    }

    private void SetQueryPart(ref string field, string value)
    {
        if (field == value)
            return;
        field = value;
        QueryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetFlag(string key, bool value)
    {
        if ((_layout.GetUiFlag(Section, key) ?? false) == value)
            return;
        _layout.SetUiFlag(Section, key, value);
        QueryChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Handler(Func<bool> canExecute, Func<Task<bool>> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute();
    }
}
