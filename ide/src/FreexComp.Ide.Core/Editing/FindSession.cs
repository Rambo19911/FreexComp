using System.ComponentModel;
using System.Globalization;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// The find state of one text document (phase1-shell.md §10): whether its panel is open, the search origin, the
/// In-selection range (anchors, so it follows edits), the current matches and the counter. While the panel is open
/// every query or text change searches again <see cref="Delay"/> later (IDE-P1-101); a query change also selects
/// the first match at or after the origin and, when that is more than 10 lines from the origin, records the origin in
/// the navigation history (IDE-P1-134: Navigate Back returns to where the search started). Buffers above <see cref="WholeTextClassifier.BackgroundThreshold"/> are
/// searched on the thread pool; results come back on the starting synchronization context and outdated ones are
/// dropped. UI thread only.
/// </summary>
public sealed class FindSession
{
    /// <summary>The incremental-search debounce (IDE-P1-101).</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5);

    private readonly FindService _service;
    private readonly TextDocumentViewModel _document;
    private ITextAnchor? _origin;
    private ITextAnchor? _rangeStart;
    private ITextAnchor? _rangeEnd;
    private IDisposable? _timer;
    private CancellationTokenSource? _search;
    private bool _reselect;
    private bool _inSelection;
    private long _generation;
    private string _resultText = "";

    internal FindSession(FindService service, TextDocumentViewModel document)
    {
        _service = service;
        _document = document;
        _service.QueryChanged += OnQueryChanged;
        _document.Buffer.Changed += OnBufferChanged;
        _document.ViewState.PropertyChanged += OnViewStateChanged;
    }

    /// <summary>Matches, counter, error or panel state changed.</summary>
    public event EventHandler? Changed;

    /// <summary><c>search.find</c> / <c>search.replace</c>: the panel's search field takes the focus and selects its text.</summary>
    public event EventHandler? FocusRequested;

    /// <summary>The search text, replacement and options shared by all editors.</summary>
    public IFindService Service => _service;

    public bool IsOpen { get; private set; }

    public bool IsReplaceMode { get; private set; }

    /// <summary>Shows or hides the replace row of an open panel (the chevron, Ctrl+H in an open panel).</summary>
    public void SetReplaceMode(bool replace)
    {
        if (IsReplaceMode == replace)
            return;
        IsReplaceMode = replace;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The panel's search field takes the focus (<see cref="FocusRequested"/>).</summary>
    public void RequestFocus() => FocusRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The panel was opened with a selection spanning more than one line (IDE-P1-102).</summary>
    public bool CanUseInSelection { get; private set; }

    /// <summary>Restricts matches to <see cref="SelectionRange"/>; not remembered between openings.</summary>
    public bool InSelection
    {
        get => _inSelection && SelectionRange is not null;
        set
        {
            value &= CanUseInSelection && SelectionRange is not null;
            if (_inSelection == value)
                return;
            _inSelection = value;
            Schedule(reselect: true);
        }
    }

    /// <summary>The In-selection range as it follows edits; null when there is none (or an edit removed its edge).</summary>
    public TextMatch? SelectionRange =>
        _rangeStart is { IsDeleted: false } start && _rangeEnd is { IsDeleted: false } end
            ? new TextMatch(start.Offset, Math.Max(0, end.Offset - start.Offset))
            : null;

    /// <summary>The matches of the last search (at most <see cref="TextFinder.MatchCap"/>); empty while the panel is closed.</summary>
    public IReadOnlyList<TextMatch> Matches { get; private set; } = [];

    public bool IsCapped { get; private set; }

    /// <summary><c>Msg.Find.RegexInvalid</c> of the last search, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>1-based index of the selected match in <see cref="Matches"/>; 0 when the selection is no listed match.</summary>
    public int CurrentIndex
    {
        get
        {
            var selection = _document.Selection;
            var index = BinarySearch(selection.Start);
            return index >= 0 && Matches[index].End == selection.End ? index + 1 : 0;
        }
    }

    /// <summary>The panel's counter (IDE-P1-106): empty for an empty search text or an error.</summary>
    public string CounterText
    {
        get
        {
            if (_resultText.Length == 0 || Error is not null)
                return "";
            if (Matches.Count == 0)
                return Strings.Msg_Find_NoMatches;
            return IsCapped
                ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_Find_MatchCountCapped, CurrentIndex, TextFinder.MatchCap)
                : string.Format(CultureInfo.CurrentCulture, Strings.Msg_Find_MatchCount, CurrentIndex, Matches.Count);
        }
    }

    /// <summary>Replace and Replace All are disabled in read-only documents (IDE-P1-109).</summary>
    public bool CanReplace => !_document.Buffer.IsReadOnly;

    /// <summary>The token of the running background search (IDE-ARCH-084), or <see cref="CancellationToken.None"/>.</summary>
    internal CancellationToken SearchCancellation => _search?.Token ?? CancellationToken.None;

    /// <summary>
    /// Opens (or re-focuses) the panel (IDE-P1-100): a non-empty single-line selection becomes the search text
    /// (regex-escaped with Regex on); a multi-line selection becomes the In-selection range and turns it on; the caret
    /// is the search origin.
    /// </summary>
    public void Open(bool replace)
    {
        var selection = _document.Selection;
        var buffer = _document.Buffer;
        var multiLine = !selection.IsEmpty && buffer.GetPosition(selection.Start).Line != buffer.GetPosition(selection.End).Line;
        IsOpen = true;
        IsReplaceMode = replace;
        CanUseInSelection = multiLine;
        _rangeStart = multiLine ? buffer.CreateAnchor(selection.Start, movesWithInsertionBefore: false) : null;
        _rangeEnd = multiLine ? buffer.CreateAnchor(selection.End, movesWithInsertionBefore: true) : null;
        _inSelection = multiLine;
        _origin = buffer.CreateAnchor(selection.Caret, movesWithInsertionBefore: false);
        if (!selection.IsEmpty && !multiLine)
        {
            var text = buffer.GetText(selection.Start, selection.End - selection.Start);
            _service.SearchText = _service.Regex ? System.Text.RegularExpressions.Regex.Escape(text) : text;
        }

        Schedule(reselect: true);
        Changed?.Invoke(this, EventArgs.Empty);
        RequestFocus();
    }

    /// <summary>Closes the panel (IDE-P1-108): the matches go, the selection stays.</summary>
    public void Close()
    {
        IsOpen = false;
        _timer?.Dispose();
        _timer = null;
        CancelSearch();
        _generation++;
        _origin = _rangeStart = _rangeEnd = null;
        _inSelection = false;
        CanUseInSelection = false;
        Matches = [];
        IsCapped = false;
        Error = null;
        _resultText = "";
        _document.ShowsFindMatches = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// <c>search.findNext</c> / <c>findPrevious</c> (IDE-P1-104): the next match after the selection (or the last one
    /// before it), wrapping with <c>Msg.Find.WrappedForward</c> / <c>WrappedBackward</c>; with no match
    /// <c>Msg.Find.NoMatches</c> on the status bar while the panel is closed. True when a match was selected.
    /// </summary>
    public Task<bool> FindNextAsync(bool forward) => FindNextAsync(forward, from: null);

    /// <summary>
    /// <c>search.findSelectionNext</c> (IDE-P1-105): the selection, else the word at the caret, becomes the search text
    /// (searched literally: regex-escaped while Regex is on) and the next occurrence after it is selected.
    /// </summary>
    public Task<bool> FindSelectionNextAsync()
    {
        var selection = _document.Selection;
        var buffer = _document.Buffer;
        var (start, end) = selection.IsEmpty ? EditorOperations.WordAt(buffer, selection.Caret) : (selection.Start, selection.End);
        if (start == end)
            return Task.FromResult(false);
        var text = buffer.GetText(start, end - start);
        _service.SearchText = _service.Regex ? System.Text.RegularExpressions.Regex.Escape(text) : text;
        return FindNextAsync(forward: true, from: end);
    }

    /// <summary>Replace (IDE-P1-107): a selection that is exactly a match is replaced; then the next match is selected.</summary>
    public async Task<bool> ReplaceAsync()
    {
        var query = _service.Query;
        if (!CanReplace || query.Text.Length == 0)
            return false;
        _service.RecordReplace();
        var selection = _document.Selection;
        var buffer = _document.Buffer;
        var (rangeStart, rangeEnd) = SearchRange();
        if (selection.Start >= rangeStart && selection.End <= rangeEnd
            && TextFinder.IsMatchAt(buffer.Text, query, selection.Start, selection.End - selection.Start, out var match))
        {
            var replacement = TextFinder.Expand(_service.ReplaceText, query, match, _document.LineEnding.NewLine);
            _document.ApplyCommandEdit(() =>
            {
                if (!_document.TryEdit([new TextEdit(selection.Start, selection.End - selection.Start, replacement)]))
                    return false;
                _document.Selection = TextSelection.At(selection.Start + replacement.Length);
                return true;
            });
        }

        return await FindNextAsync(forward: true).ConfigureAwait(true);
    }

    /// <summary>
    /// Replace All (IDE-P1-107): every match (inside the range while In selection is on) as one undo step; the caret
    /// keeps its line and column (clamped); status <c>Msg.Find.ReplacedOne</c> / <c>…Many</c> / <c>…ReplaceNone</c>.
    /// Returns the number of replacements.
    /// </summary>
    public async Task<int> ReplaceAllAsync()
    {
        var query = _service.Query;
        if (!CanReplace || query.Text.Length == 0)
            return 0;
        _service.RecordReplace();
        var buffer = _document.Buffer;
        var (start, end) = SearchRange();
        var text = buffer.Text;
        var version = buffer.Version;
        var replacement = _service.ReplaceText;
        var newLine = _document.LineEnding.NewLine;
        var (edits, error) = text.Length > WholeTextClassifier.BackgroundThreshold
            ? await Task.Run(() => TextFinder.Replacements(text, query, start, end, replacement, newLine)).ConfigureAwait(true)
            : TextFinder.Replacements(text, query, start, end, replacement, newLine);
        if (error is not null || buffer.Version != version)
        {
            if (error is not null)
                SetResult([], false, error, query.Text);
            return 0;
        }

        if (edits.Count == 0)
        {
            _service.Status.ShowMessage(Strings.Msg_Find_ReplaceNone, StatusDuration);
            return 0;
        }

        var caret = _document.ViewState.Caret;
        if (!_document.ApplyCommandEdit(() =>
            {
                if (!_document.TryEdit(edits))
                    return false;
                _document.Selection = TextSelection.At(buffer.GetOffset(Math.Clamp(caret.Line, 1, buffer.LineCount), caret.Column));
                return true;
            }))
            return 0;
        _service.Status.ShowMessage(edits.Count == 1
            ? Strings.Msg_Find_ReplacedOne
            : string.Format(CultureInfo.CurrentCulture, Strings.Msg_Find_ReplacedMany, edits.Count), StatusDuration);
        return edits.Count;
    }

    /// <summary>The document closed: stop listening.</summary>
    internal void Detach()
    {
        _timer?.Dispose();
        CancelSearch();
        _generation++;
        _service.QueryChanged -= OnQueryChanged;
        _document.Buffer.Changed -= OnBufferChanged;
        _document.ViewState.PropertyChanged -= OnViewStateChanged;
    }

    private async Task<bool> FindNextAsync(bool forward, int? from)
    {
        var query = _service.Query;
        if (query.Text.Length == 0)
            return false;
        _service.RecordSearch(); // IDE-P1-108: a navigation records the text
        var buffer = _document.Buffer;
        var selection = _document.Selection;
        var position = from ?? (forward ? selection.End : selection.Start);
        var (start, end) = SearchRange();
        var text = buffer.Text;
        var version = buffer.Version;
        var found = text.Length > WholeTextClassifier.BackgroundThreshold
            ? await Task.Run(() => TextFinder.FindNext(text, query, start, end, position, forward)).ConfigureAwait(true)
            : TextFinder.FindNext(text, query, start, end, position, forward);
        if (buffer.Version != version)
            return false; // the text changed meanwhile: the user goes on from the new state
        if (found is not { } result)
        {
            if (!IsOpen)
                _service.Status.ShowMessage(Strings.Msg_Find_NoMatches, StatusDuration);
            return false;
        }

        var departure = _document.ViewState.Caret;
        _document.Selection = new TextSelection(result.Match.Offset, result.Match.End);
        _document.RevealCaret();
        _document.OnJumped(departure, always: result.Wrapped); // IDE-ED-100: wrap-around or more than 10 lines
        if (result.Wrapped)
            _service.Status.ShowMessage(forward ? Strings.Msg_Find_WrappedForward : Strings.Msg_Find_WrappedBackward, StatusDuration);
        if (IsOpen)
            _origin = buffer.CreateAnchor(result.Match.Offset, movesWithInsertionBefore: false);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private (int Start, int End) SearchRange() =>
        IsOpen && InSelection && SelectionRange is { } range ? (range.Offset, range.End) : (0, _document.Buffer.Length);

    private void OnQueryChanged(object? sender, EventArgs e) => Schedule(reselect: true);

    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        if (_inSelection && SelectionRange is null)
            _inSelection = false; // an edit removed an edge of the range
        Schedule(reselect: false);
    }

    private void OnViewStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsOpen && e.PropertyName is nameof(EditorViewState.Caret) or nameof(EditorViewState.SelectionAnchor))
            Changed?.Invoke(this, EventArgs.Empty); // the counter's current index
    }

    private void Schedule(bool reselect)
    {
        if (!IsOpen)
            return;
        _reselect |= reselect;
        _timer?.Dispose();
        _timer = _service.Clock.Schedule(Delay, Search);
    }

    /// <summary>IDE-ARCH-084: a newer search, closing the panel and closing the document cancel the running one.</summary>
    private void CancelSearch()
    {
        _search?.Cancel();
        _search?.Dispose();
        _search = null;
    }

    private void Search()
    {
        _timer = null;
        CancelSearch();
        var generation = ++_generation;
        var reselect = _reselect;
        _reselect = false;
        var query = _service.Query;
        var buffer = _document.Buffer;
        var text = buffer.Text;
        var version = buffer.Version;
        var (start, end) = SearchRange();
        if (text.Length <= WholeTextClassifier.BackgroundThreshold)
        {
            Apply(TextFinder.FindAll(text, query, start, end));
            return;
        }

        var context = SynchronizationContext.Current;
        var cancellation = (_search = new CancellationTokenSource()).Token;
        Task.Run(() => TextFinder.FindAll(text, query, start, end, cancellation), cancellation).ContinueWith(
            t => Apply(t.Result),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            context is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext());

        void Apply(FindResult result)
        {
            if (generation != _generation || !IsOpen)
                return; // closed, or a newer search started
            if (buffer.Version != version)
            {
                Schedule(reselect); // the text changed while searching in the background
                return;
            }

            if (reselect && result.Matches.Count > 0)
            {
                var origin = _origin is { IsDeleted: false } anchor ? anchor.Offset : _document.Selection.Caret;
                var match = result.Matches.FirstOrDefault(m => m.Offset >= origin, result.Matches[0]);
                var departure = buffer.GetPosition(origin);
                _document.Selection = new TextSelection(match.Offset, match.End);
                _document.RevealCaret();
                _document.OnJumped(departure); // IDE-P1-134: more than 10 lines from the search origin → navigation history
            }

            SetResult(result.Matches, result.IsCapped, result.Error, query.Text);
        }
    }

    private void SetResult(IReadOnlyList<TextMatch> matches, bool capped, string? error, string text)
    {
        Matches = matches;
        IsCapped = capped;
        Error = error;
        _resultText = text;
        _document.ShowsFindMatches = IsOpen && matches.Count > 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Index of the match starting at <paramref name="offset"/>, or a negative number.</summary>
    private int BinarySearch(int offset)
    {
        int low = 0, high = Matches.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            var value = Matches[mid].Offset;
            if (value == offset)
                return mid;
            if (value < offset)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return -1;
    }
}
