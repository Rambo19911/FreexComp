using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.CodeModel;

/// <summary>How an item matches the typed text (IDE-ED-082/083); lower ranks first.</summary>
public enum CompletionMatch
{
    None = -1,
    ExactCasePrefix = 0,
    Prefix = 1,
    WordStarts = 2,
}

/// <summary>What typing a character starts (IDE-ED-080).</summary>
public enum CompletionAutoTrigger
{
    None,

    /// <summary>(b) <c>.</c> after an identifier, <c>)</c> or <c>]</c>: at once.</summary>
    Immediate,

    /// <summary>(c) the first character of an identifier: after <c>editor.completion.delayMs</c>.</summary>
    Delayed,
}

/// <summary>The pure rules of code completion (editor-features.md §8); the session applies them.</summary>
public static class CompletionRules
{
    /// <summary>IDE-ED-084: typing one of these commits a prefix-matching selected item, then the character is inserted.</summary>
    public const string CommitCharacters = ".()[];,:=+-*/<> ";

    /// <summary>Colour tokens inside which nothing triggers automatically (comments, strings, numbers, inactive code).</summary>
    private static readonly HashSet<string> Suppressing = new(StringComparer.Ordinal)
    {
        "syntax.comment", "syntax.string", "syntax.char", "syntax.number", "syntax.inactive", "syntax.directive", "syntax.asm",
    };

    public static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>A "word" start of <paramref name="text"/> (IDE-ED-082): the start, a capital after a lower-case letter, or after <c>_</c>.</summary>
    public static bool IsWordStart(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);
        return index == 0 || text[index - 1] == '_' || char.IsUpper(text[index]) && char.IsLower(text[index - 1]);
    }

    /// <summary>IDE-ED-082: prefix (case-sensitive first, then not), or a subsequence whose characters each start a word.</summary>
    public static CompletionMatch Match(string typed, string label)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(label);
        if (label.StartsWith(typed, StringComparison.Ordinal))
            return CompletionMatch.ExactCasePrefix;
        if (label.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            return CompletionMatch.Prefix;
        return WordStartIndices(typed, label) is null ? CompletionMatch.None : CompletionMatch.WordStarts;
    }

    /// <summary>
    /// UI-ED-115: the indices of the characters of <paramref name="label"/> that <paramref name="typed"/> matched (the
    /// prefix, or the word starts of IDE-ED-082); empty when nothing is typed or nothing matches.
    /// </summary>
    public static IReadOnlyList<int> MatchedIndices(string typed, string label)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(label);
        if (typed.Length == 0)
            return [];
        if (label.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            return [.. Enumerable.Range(0, typed.Length)];
        return WordStartIndices(typed, label) ?? [];
    }

    /// <summary>Each typed character at the next word start with that letter (ignoring case); null when one is missing.</summary>
    private static List<int>? WordStartIndices(string typed, string label)
    {
        var indices = new List<int>(typed.Length);
        var at = 0;
        foreach (var c in typed)
        {
            while (at < label.Length && !(char.ToUpperInvariant(label[at]) == char.ToUpperInvariant(c) && IsWordStart(label, at)))
                at++;
            if (at == label.Length)
                return null;
            indices.Add(at);
            at++;
        }

        return indices;
    }

    /// <summary>
    /// IDE-ED-083: the matching items, ranked by match kind, then by group (the language service's sort-text digit: locals and
    /// parameters, class members, this unit, used units, the System unit, keywords), then by label ignoring case.
    /// </summary>
    public static IReadOnlyList<CompletionItem> FilterAndRank(IEnumerable<CompletionItem> items, string typed)
    {
        ArgumentNullException.ThrowIfNull(items);
        return [.. items
            .Select(item => (Item: item, Match: Match(typed, item.Label)))
            .Where(x => x.Match != CompletionMatch.None)
            .OrderBy(x => x.Match)
            .ThenBy(x => Group(x.Item))
            .ThenBy(x => x.Item.Label, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Item)];
    }

    /// <summary>The ranking group: the leading digit of the sort text; keywords last.</summary>
    private static int Group(CompletionItem item) =>
        item.Kind is CompletionItemKind.Keyword or CompletionItemKind.Directive ? 8
            : item.SortText.Length > 0 && char.IsAsciiDigit(item.SortText[0]) ? item.SortText[0] - '0' : 7;

    /// <summary>
    /// IDE-ED-080 (b)–(d): what typing <paramref name="typed"/> (now just left of <paramref name="caret"/> in
    /// <paramref name="text"/>) triggers; <paramref name="classAt"/> gives the lexical colour token at an offset (null = none).
    /// </summary>
    public static CompletionAutoTrigger AutoTrigger(string text, int caret, char typed, Func<int, string?> classAt)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(classAt);
        int at = caret - 1;
        if (at < 0 || at >= text.Length || text[at] != typed || Suppressed(classAt(at)))
            return CompletionAutoTrigger.None;
        var before = at > 0 ? text[at - 1] : '\n';
        if (typed == '.')
            return (IsIdentifierChar(before) || before is ')' or ']') && !Suppressed(at > 0 ? classAt(at - 1) : null) && before != '.'
                ? CompletionAutoTrigger.Immediate
                : CompletionAutoTrigger.None;
        if ((char.IsLetter(typed) || typed == '_') && !IsIdentifierChar(before))
            return CompletionAutoTrigger.Delayed;
        return CompletionAutoTrigger.None;
    }

    internal static bool Suppressed(string? tokenClass) => tokenClass is not null && Suppressing.Contains(tokenClass);

    /// <summary>The lexical colour token of <paramref name="document"/> at <paramref name="offset"/> (null = none).</summary>
    internal static string? ClassAt(TextDocumentViewModel document, int offset)
    {
        var buffer = document.Buffer;
        if (offset < 0 || offset >= buffer.Length)
            return null;
        var line = buffer.GetPosition(offset).Line;
        foreach (var span in document.Language.CreateClassifier().Classify(buffer, new LineRange(line, line)))
        {
            if (span.Offset <= offset && offset < span.Offset + span.Length)
                return span.TokenClass;
        }

        return null;
    }
}

/// <summary>
/// The completion popup's model for one document (editor-features.md §8, I-114; the view is I-115): open/closed, the
/// filtered and ranked items, the selection, and the commit rules. The view shows <see cref="Items"/> while
/// <see cref="IsOpen"/>; the document passes typed text and keys here first.
/// </summary>
public sealed class CompletionSession
{
    /// <summary>Asks the language service for the whole context set at an offset; null = not available here.</summary>
    public delegate Task<CompletionList?> Fetch(int offset, CompletionTrigger trigger, CancellationToken ct);

    /// <summary>Asks the language service for an item's declaration and doc comment (UI-ED-117); null = none.</summary>
    public delegate Task<CompletionDescription?> Describe(CompletionItem item, CancellationToken ct);

    private static readonly TimeSpan ComputingStatusAfter = TimeSpan.FromMilliseconds(300); // IDE-ED-087

    /// <summary>UI-ED-117: the documentation side panel appears this long after a row is selected.</summary>
    public static readonly TimeSpan DescriptionDelay = TimeSpan.FromMilliseconds(300);

    private readonly TextDocumentViewModel _document;
    private readonly Fetch _fetch;
    private readonly IClock _clock;
    private readonly Func<bool> _autoTrigger;
    private readonly Func<TimeSpan> _delay;
    private readonly Action<string> _status;
    private readonly Describe? _describe;
    private CompletionList? _list;
    private int _start;
    private IDisposable? _timer;
    private CancellationTokenSource? _request;
    private bool _committing;
    private bool _templateMode;
    private IDisposable? _descriptionTimer;
    private CancellationTokenSource? _descriptionRequest;
    private CompletionItem? _describedItem;

    public CompletionSession(TextDocumentViewModel document, Fetch fetch, IClock clock, Func<bool> autoTrigger, Func<TimeSpan> delay, Action<string> status,
        Describe? describe = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        _fetch = fetch;
        _clock = clock;
        _autoTrigger = autoTrigger;
        _delay = delay;
        _status = status;
        _describe = describe;
        document.ViewState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewState.Caret) && IsOpen && !_committing)
                Refilter();
        };
    }

    /// <summary>Opened, closed, re-filtered or the selection moved.</summary>
    public event EventHandler? Changed;

    public bool IsOpen { get; private set; }

    /// <summary>The matching items, best first (IDE-ED-083).</summary>
    public IReadOnlyList<CompletionItem> Items { get; private set; } = [];

    public int SelectedIndex { get; private set; }

    public CompletionItem? SelectedItem => IsOpen && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    /// <summary>The running or last request; completes when its result is applied (tests await it).</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// UI-ED-117: the selected item's declaration and doc comment, set <see cref="DescriptionDelay"/> after the row was
    /// selected; null while waiting, without a declaration, or closed.
    /// </summary>
    public CompletionDescription? Description { get; private set; }

    /// <summary>
    /// UI-ED-113/117: the declaring unit to show for <paramref name="origin"/> (an item's or a description's); null when
    /// it is empty (locals, keywords, templates) or the document's own unit (its file name without extension).
    /// </summary>
    public string? ShownOrigin(string? origin) =>
        string.IsNullOrEmpty(origin)
        || _document.FilePath is { } path && string.Equals(origin, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)
            ? null
            : origin;

    /// <summary><see cref="Description"/> changed.</summary>
    public event EventHandler? DescriptionChanged;

    /// <summary>The running or last description request (tests await it).</summary>
    public Task DescriptionPending { get; private set; } = Task.CompletedTask;

    /// <summary>The text from the completion's start to the caret.</summary>
    public string TypedText
    {
        get
        {
            var caret = Caret;
            return caret >= _start ? _document.Buffer.GetText(_start, caret - _start) : "";
        }
    }

    private int Caret => _document.Buffer.GetOffset(_document.ViewState.Caret.Line, _document.ViewState.Caret.Column);

    /// <summary>IDE-ED-080 (a), IDE-ED-081: Ctrl+Space.</summary>
    public Task InvokeAsync()
    {
        CancelTimer();
        if (InSuppressedText(Caret))
            return Task.CompletedTask;
        Pending = RequestAsync(new CompletionTrigger(CompletionTriggerKind.Invoked, '\0'), explicitly: true);
        return Pending;
    }

    /// <summary>
    /// Before typed text is inserted: a commit character commits a prefix-matching selected item (IDE-ED-084); a character
    /// that is no identifier character ends a pending delayed trigger.
    /// </summary>
    internal void BeforeTyped(string text)
    {
        if (text.Length != 1)
            return;
        var c = text[0];
        if (!CompletionRules.IsIdentifierChar(c))
            CancelTimer();
        if (IsOpen && CompletionRules.CommitCharacters.Contains(c, StringComparison.Ordinal))
        {
            var typed = TypedText;
            if (typed.Length > 0 && SelectedItem is { } item && CompletionRules.Match(typed, item.Label) is CompletionMatch.ExactCasePrefix or CompletionMatch.Prefix)
                Commit(wholeIdentifier: false);
            else
                Close();
        }
    }

    /// <summary>After typed text was inserted: re-filter an open popup or start an automatic trigger (IDE-ED-080).</summary>
    internal void AfterTyped(string text)
    {
        if (IsOpen)
        {
            Refilter();
            return;
        }

        if (text.Length != 1 || !_autoTrigger() || _document.ViewState.Overwrite)
            return;
        var buffer = _document.Buffer;
        var caret = Caret;
        switch (CompletionRules.AutoTrigger(buffer.Text, caret, text[0], ClassAt))
        {
            case CompletionAutoTrigger.Immediate:
                Pending = RequestAsync(new CompletionTrigger(CompletionTriggerKind.Character, text[0]), explicitly: false);
                break;
            case CompletionAutoTrigger.Delayed:
                CancelTimer();
                _timer = _clock.Schedule(_delay(), () =>
                {
                    _timer = null;
                    Pending = RequestAsync(new CompletionTrigger(CompletionTriggerKind.Character, text[0]), explicitly: false);
                });
                break;
        }
    }

    /// <summary>Keys while the popup is open: Up/Down/PageUp/PageDown move, Enter/Tab commit, Ctrl+Enter replaces the whole identifier, Escape closes.</summary>
    public bool HandleKey(string gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        if (!IsOpen)
            return false;
        switch (gesture)
        {
            case "Up":
                Select(SelectedIndex - 1);
                return true;
            case "Down":
                Select(SelectedIndex + 1);
                return true;
            case "PageUp":
                Select(SelectedIndex - 8);
                return true;
            case "PageDown":
                Select(SelectedIndex + 8);
                return true;
            case "Enter" or "Tab":
                Commit(wholeIdentifier: false);
                return true;
            case "Ctrl+Enter":
                Commit(wholeIdentifier: true);
                return true;
            case "Escape":
                Close();
                return true;
            default:
                return false;
        }
    }

    public void Select(int index)
    {
        if (!IsOpen || Items.Count == 0)
            return;
        SelectedIndex = Math.Clamp(index, 0, Items.Count - 1);
        RaiseChanged();
    }

    /// <summary>
    /// IDE-ED-084: inserts the selected item in place of the identifier from its start to the caret (with
    /// <paramref name="wholeIdentifier"/>, also the identifier's rest right of the caret), then closes.
    /// </summary>
    public void Commit(bool wholeIdentifier)
    {
        if (SelectedItem is not { } item)
        {
            Close();
            return;
        }

        if (item.Kind == CompletionItemKind.Snippet && _document.Templates?.Find(item.Label, _document.Language.Id) is { } template)
        {
            // IDE-ED-062/063: a template item expands in place of the typed abbreviation.
            var start = _start;
            var length = Caret - _start;
            Close();
            _committing = true;
            try
            {
                _document.ExpandTemplate(template, start, length);
            }
            finally
            {
                _committing = false;
            }

            return;
        }

        var caret = Caret;
        var end = caret;
        var text = _document.Buffer.Text;
        if (wholeIdentifier)
        {
            while (end < text.Length && CompletionRules.IsIdentifierChar(text[end]))
                end++;
        }

        Close();
        _committing = true;
        try
        {
            if (_document.TryInsertText(_start, end - _start, item.InsertText))
                _document.Selection = TextSelection.At(_start + item.InsertText.Length);
        }
        finally
        {
            _committing = false;
        }
    }

    /// <summary>Closes without inserting; a running request is dropped.</summary>
    public void Close()
    {
        CancelTimer();
        _request?.Cancel();
        _request = null;
        _templateMode = false;
        if (!IsOpen)
            return;
        IsOpen = false;
        Items = [];
        _list = null;
        RaiseChanged();
    }

    private async Task RequestAsync(CompletionTrigger trigger, bool explicitly)
    {
        _request?.Cancel();
        var request = _request = new CancellationTokenSource();
        var version = _document.Buffer.Version;
        var offset = Caret;
        using var computing = explicitly ? _clock.Schedule(ComputingStatusAfter, () => _status(Strings.Status_ComputingCompletions)) : null;
        CompletionList? list;
        try
        {
            list = await _fetch(offset, trigger, request.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (request.IsCancellationRequested || !ReferenceEquals(request, _request) || _document.Buffer.Version != version || list is null)
            return; // superseded, or a stale buffer version (IDE-ED-087)
        _request = null;
        var items = list.Items.Where(i => i.Kind != CompletionItemKind.Snippet).ToList();
        if (list.Items.Any(i => i.Kind == CompletionItemKind.Snippet))
            items.AddRange(TemplateItems()); // IDE-ED-063: at statement positions the code templates take the snippets' place
        if (items.Count == 0)
        {
            if (explicitly)
                _status(Strings.Msg_Editor_NoCompletions);
            return;
        }

        _list = list with { Items = [.. items] };
        _start = Math.Min(list.ReplacementSpan.Start, offset);
        var matches = CompletionRules.FilterAndRank(_list.Items, TypedText);
        if (explicitly && matches.Count == 1)
        {
            // IDE-ED-081: a single candidate is inserted without the popup.
            IsOpen = true;
            Items = matches;
            SelectedIndex = 0;
            Commit(wholeIdentifier: false);
            return;
        }

        if (matches.Count == 0)
        {
            if (explicitly)
                _status(Strings.Msg_Editor_NoCompletions);
            _list = null;
            return;
        }

        IsOpen = true;
        Items = matches;
        SelectedIndex = 0;
        RaiseChanged();
    }

    /// <summary>IDE-ED-082/086: the typed text changed or the caret moved.</summary>
    private void Refilter()
    {
        if (_list is null)
            return;
        var caret = Caret;
        var text = _document.Buffer.Text;
        if (caret < _start || caret > text.Length || !text.AsSpan(_start, caret - _start).ToString().All(CompletionRules.IsIdentifierChar))
        {
            Close(); // deleted past the start or left the identifier
            return;
        }

        var matches = CompletionRules.FilterAndRank(_list.Items, TypedText);
        if (matches.Count == 0)
        {
            var typed = TypedText;
            var templates = _templateMode;
            Close();
            if (templates)
                _status(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Editor_TemplateNotFound, typed)); // IDE-ED-066
            return;
        }

        Items = matches;
        SelectedIndex = 0;
        RaiseChanged();
    }

    /// <summary>
    /// IDE-ED-062 (<c>edit.insertTemplate</c>, Ctrl+J): the popup lists only the code templates, filtered by the identifier
    /// left of the caret; an unknown abbreviation shows <c>Msg.Editor.TemplateNotFound</c> (IDE-ED-066).
    /// </summary>
    public void ShowTemplates()
    {
        Close();
        var caret = Caret;
        var text = _document.Buffer.Text;
        var start = caret;
        while (start > 0 && CompletionRules.IsIdentifierChar(text[start - 1]))
            start--;
        var items = TemplateItems();
        _list = new CompletionList(new FreexComp.Compiler.Text.TextSpan(start, caret - start), [.. items], false);
        _start = start;
        var matches = CompletionRules.FilterAndRank(items, TypedText);
        if (matches.Count == 0)
        {
            _list = null;
            _status(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Editor_TemplateNotFound, TypedText));
            return;
        }

        _templateMode = true;
        IsOpen = true;
        Items = matches;
        SelectedIndex = 0;
        RaiseChanged();
    }

    /// <summary>The document language's code templates as completion items (detail = the description, UI-ED-113).</summary>
    private List<CompletionItem> TemplateItems() =>
        _document.Templates is not { } templates ? []
            : [.. templates.Templates.Where(t => t.Language == _document.Language.Id)
                .Select(t => new CompletionItem(t.Abbreviation, CompletionItemKind.Snippet, t.Abbreviation, "6" + t.Abbreviation, t.Description, false, null))];

    private void CancelTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void RaiseChanged()
    {
        UpdateDescription();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>UI-ED-117: a newly selected item hides the panel and asks for its description after the delay.</summary>
    private void UpdateDescription()
    {
        var item = SelectedItem;
        if (ReferenceEquals(item, _describedItem))
            return;
        _describedItem = item;
        _descriptionTimer?.Dispose();
        _descriptionTimer = null;
        _descriptionRequest?.Cancel();
        _descriptionRequest = null;
        if (Description is not null)
        {
            Description = null;
            DescriptionChanged?.Invoke(this, EventArgs.Empty);
        }

        if (item is not { SymbolId: not null } || _describe is null)
            return; // only symbols have declaration text
        _descriptionTimer = _clock.Schedule(DescriptionDelay, () =>
        {
            _descriptionTimer = null;
            DescriptionPending = DescribeAsync(item);
        });
    }

    private async Task DescribeAsync(CompletionItem item)
    {
        var request = _descriptionRequest = new CancellationTokenSource();
        CompletionDescription? description;
        try
        {
            description = await _describe!(item, request.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (request.IsCancellationRequested || !ReferenceEquals(item, _describedItem) || description is not { Signature.Length: > 0 })
            return;
        _descriptionRequest = null;
        Description = description;
        DescriptionChanged?.Invoke(this, EventArgs.Empty);
    }

    private string? ClassAt(int offset) => CompletionRules.ClassAt(_document, offset);

    /// <summary>IDE-ED-080 (a): Ctrl+Space does nothing inside comments and string literals.</summary>
    private bool InSuppressedText(int caret)
    {
        var inside = ClassAt(caret - 1);
        var right = ClassAt(caret);
        return inside is "syntax.comment" or "syntax.string" && right == inside;
    }
}

/// <summary>
/// I-114: gives every open Pascal document of the project its <see cref="CompletionSession"/> over the project
/// <c>Compilation</c> (requests on the <see cref="CodeModelWorker"/>) and handles <c>edit.completeCode</c> (Ctrl+Space).
/// </summary>
public sealed class CompletionController
{
    public const string CommandId = "edit.completeCode";

    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly IDocumentService _documents;
    private readonly IProjectService _projects;
    private readonly CodeModelService _codeModel;
    private readonly ISettingsService _settings;
    private readonly IClock _clock;
    private readonly IStatusService _status;

    public CompletionController(IDocumentService documents, IProjectService projects, CodeModelService codeModel, ISettingsService settings,
        IClock clock, IStatusService status)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
        _projects = projects;
        _codeModel = codeModel;
        _settings = settings;
        _clock = clock;
        _status = status;
        documents.DocumentOpened += (_, e) => Attach(e.Document);
        foreach (var document in documents.Documents)
            Attach(document);
    }

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler(CommandId, new Handler(this));
    }

    private bool CanComplete => _documents.ActiveDocument is TextDocumentViewModel { Completion: not null } && _projects.Project is not null;

    private void Attach(DocumentViewModel document)
    {
        if (document is not TextDocumentViewModel { FilePath: not null } text || text.Language.Id != "pascal" || text.Completion is not null)
            return;
        LanguageService.Document? source = null; // the snapshot the items came from: their descriptions are looked up there
        text.Completion = new CompletionSession(text, (offset, trigger, ct) => FetchAsync(text, offset, trigger, s => source = s, ct), _clock,
            () => _settings.Get(SettingsCatalog.EditorCompletionAutoTrigger),
            () => TimeSpan.FromMilliseconds(_settings.Get(SettingsCatalog.EditorCompletionDelayMs)),
            message => _status.ShowMessage(message, StatusDuration),
            (item, ct) => source is { } s
                ? _codeModel.Worker.RunAsync<CompletionDescription?>("completionDescription", token => LanguageService.CompletionService.GetDescription(s, item, token), ct)
                : Task.FromResult<CompletionDescription?>(null));
    }

    private Task<CompletionList?> FetchAsync(TextDocumentViewModel document, int offset, CompletionTrigger trigger, Action<LanguageService.Document> fetchedFrom, CancellationToken ct)
    {
        if (document.FilePath is not { } path || _codeModel.Synchronize() is null)
            return Task.FromResult<CompletionList?>(null);
        var source = _codeModel.Workspace.GetDocument(path);
        fetchedFrom(source);
        return _codeModel.Worker.RunAsync<CompletionList?>("completion", token => LanguageService.CompletionService.GetCompletions(source, offset, trigger, token), ct);
    }

    private sealed class Handler(CompletionController owner) : ICommandHandler
    {
        public bool CanExecute(string? argument) => owner.CanComplete;

        public Task ExecuteAsync(string? argument) =>
            owner._documents.ActiveDocument is TextDocumentViewModel { Completion: { } session } ? session.InvokeAsync() : Task.CompletedTask;
    }
}
