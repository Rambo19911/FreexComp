using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Services;

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
        documents.DocumentClosed += (_, e) =>
        {
            if (e.Document is TextDocumentViewModel text && _sessions.Remove(text, out var session))
                session.Detach();
        };
    }

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
        // menus.md: "text and a search text exists"; Find Next Occurrence of Selection: "text".
        commands.RegisterHandler("search.findNext", new Handler(
            () => Active is not null && _searchText.Length > 0, () => For(Active!).FindNextAsync(forward: true)));
        commands.RegisterHandler("search.findPrevious", new Handler(
            () => Active is not null && _searchText.Length > 0, () => For(Active!).FindNextAsync(forward: false)));
        commands.RegisterHandler("search.findSelectionNext", new Handler(
            () => Active is not null, () => For(Active!).FindSelectionNextAsync()));
    }

    private TextDocumentViewModel? Active => _documents.ActiveDocument as TextDocumentViewModel;

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
