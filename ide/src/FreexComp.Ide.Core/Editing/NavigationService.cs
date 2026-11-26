using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Editing;

/// <summary>A navigation moved the caret of <see cref="Document"/> away from <see cref="From"/> (the history entry, IDE-ED-100).</summary>
public sealed class NavigatedEventArgs(TextDocumentViewModel document, TextPosition from) : EventArgs
{
    public TextDocumentViewModel Document { get; } = document;

    public TextPosition From { get; } = from;
}

/// <summary>One navigation-history location (IDE-ED-100): the file (null for an untitled document), its name, the position now.</summary>
public sealed record NavigationLocation(string? FilePath, string Name, TextPosition Position);

/// <summary>
/// Caret navigation inside documents (commands.md: owner of <c>search.goToLine</c>, <c>search.navigateBack/Forward</c>)
/// and the navigation history (IDE-ED-100). Every <see cref="Navigated"/> pushes its departure onto the history. UI thread only.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Raised after a navigation with the position it left: Go to Line, a far bracket/find jump of a document
    /// (<see cref="TextDocumentViewModel.Jumped"/>), switching to another document.
    /// </summary>
    event EventHandler<NavigatedEventArgs>? Navigated;

    /// <summary>The history, oldest first (at most <see cref="NavigationService.MaxEntries"/>); positions follow edits.</summary>
    IReadOnlyList<NavigationLocation> History { get; }

    /// <summary>The entry the user is at; <c>History.Count</c> when not inside the history (the live end).</summary>
    int HistoryIndex { get; }

    /// <summary>
    /// UI-DLG-120 / phase1-shell.md §10: caret to <paramref name="line"/> (clamped to the document), column
    /// <paramref name="column"/> (below 1 → 1, beyond the line → its end; null → the first non-blank character), selection
    /// cleared, folds hiding the line opened, the line scrolled to one third of the viewport.
    /// </summary>
    void GoToLine(TextDocumentViewModel document, int line, int? column);

    /// <summary>Records <paramref name="from"/> in <paramref name="document"/> as the departure of a navigation (raises <see cref="Navigated"/>).</summary>
    void RecordDeparture(TextDocumentViewModel document, TextPosition from);
}

/// <inheritdoc cref="INavigationService"/>
public sealed class NavigationService : INavigationService
{
    /// <summary>IDE-ED-100: at most 50 entries.</summary>
    public const int MaxEntries = 50;

    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly IDocumentService _documents;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly IFileSystem _fileSystem;
    private readonly List<Entry> _entries = [];
    private int _index;
    private bool _navigating;
    private DocumentViewModel? _lastActive;
    private ICommandService? _commands;

    public NavigationService(IDocumentService documents, IDialogService dialogs, IStatusService status, IFileSystem fileSystem)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _documents.DocumentOpened += OnDocumentOpened;
        _documents.DocumentClosed += OnDocumentClosed;
        _documents.ActiveDocumentChanged += OnActiveDocumentChanged;
        Navigated += (_, e) => Push(e.Document, e.From); // the history consumes every navigation
    }

    public event EventHandler<NavigatedEventArgs>? Navigated;

    public IReadOnlyList<NavigationLocation> History =>
        [.. _entries.Select(e => new NavigationLocation(e.Path, e.Name, e.Current))];

    public int HistoryIndex => _index;

    /// <summary>menus.md: Navigate Back is enabled when the history has an earlier entry (at the live end: any entry).</summary>
    public bool CanGoBack => _index > 0;

    /// <summary>menus.md: Navigate Forward is enabled when the history has a later entry.</summary>
    public bool CanGoForward => _index < _entries.Count - 1;

    public void GoToLine(TextDocumentViewModel document, int line, int? column)
    {
        ArgumentNullException.ThrowIfNull(document);
        // I-218: a source position is shown in the Code view of a form document (designer.defaultView may open it in Design).
        if (document is FormDocumentViewModel { HasUnit: true, ActiveView: not DocumentView.Code } form)
            form.ShowView(DocumentView.Code);
        var buffer = document.Buffer;
        var from = document.ViewState.Caret;
        line = Math.Clamp(line, 1, buffer.LineCount);
        var (start, length, _) = buffer.GetLine(line);
        var offset = column is { } c ? start + Math.Clamp(c, 1, length + 1) - 1 : start + FirstNonBlank(buffer.GetText(start, length));
        document.Folding.Reveal(start);
        document.Folding.Reveal(offset);
        document.Selection = TextSelection.At(offset);
        document.RevealCaret(third: true);
        RecordDeparture(document, from);
    }

    public void RecordDeparture(TextDocumentViewModel document, TextPosition from)
    {
        ArgumentNullException.ThrowIfNull(document);
        Navigated?.Invoke(this, new NavigatedEventArgs(document, from));
    }

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        commands.RegisterHandler("search.goToLine", new Handler(this)); // menus.md: "text"
        commands.RegisterHandler("search.navigateBack", new HistoryHandler(() => CanGoBack, () => GoAsync(back: true)));
        commands.RegisterHandler("search.navigateForward", new HistoryHandler(() => CanGoForward, () => GoAsync(back: false)));
    }

    /// <summary>Index of the first character that is not a space or tab; 0 for a blank line.</summary>
    private static int FirstNonBlank(string text)
    {
        var index = 0;
        while (index < text.Length && text[index] is ' ' or '\t')
            index++;
        return index == text.Length ? 0 : index;
    }

    /// <summary>
    /// IDE-ED-100: the entries after the current one go; the departure is appended, replacing the last entry when that is
    /// in the same document within 10 lines; the oldest entry goes beyond 50.
    /// </summary>
    private void Push(TextDocumentViewModel document, TextPosition from)
    {
        if (_navigating)
            return; // moving through the history records nothing
        if (_index < _entries.Count)
            _entries.RemoveRange(_index, _entries.Count - _index); // the current entry is replaced by the departure itself
        var entry = new Entry(document, from);
        if (_entries.Count > 0 && _entries[^1].IsNear(entry))
            _entries[^1] = entry;
        else
            _entries.Add(entry);
        if (_entries.Count > MaxEntries)
            _entries.RemoveAt(0);
        _index = _entries.Count;
        _commands?.InvalidateCanExecute();
    }

    /// <summary>
    /// <c>search.navigateBack</c> first records the current location when at the live end; then the previous / next entry
    /// is shown. Entries whose file is gone (or cannot be opened) are skipped and removed; nothing left →
    /// <c>Msg.Nav.NoBack</c> / <c>Msg.Nav.NoForward</c>.
    /// </summary>
    private async Task GoAsync(bool back)
    {
        if (back && _index == _entries.Count && _documents.ActiveDocument?.VisibleText is { } current)
        {
            RecordDeparture(current, current.ViewState.Caret);
            _index = _entries.Count - 1; // the user is at the entry just recorded
        }

        var target = back ? _index - 1 : _index + 1;
        while (target >= 0 && target < _entries.Count)
        {
            var entry = _entries[target];
            if (await ShowAsync(entry) && _entries.IndexOf(entry) is >= 0 and var shown)
            {
                _index = shown;
                _commands?.InvalidateCanExecute();
                return;
            }

            Remove(_entries.IndexOf(entry));
            if (back)
                target--;
        }

        _commands?.InvalidateCanExecute();
        _status.ShowMessage(back ? Strings.Msg_Nav_NoBack : Strings.Msg_Nav_NoForward, StatusDuration);
    }

    /// <summary>Activates (or reopens) the entry's document and puts the caret at its position; false when that is not possible.</summary>
    private async Task<bool> ShowAsync(Entry entry)
    {
        _navigating = true;
        try
        {
            var document = entry.Document;
            if (document is null)
            {
                if (entry.Path is not { } path || !_fileSystem.FileExists(path))
                    return false;
                if ((await _documents.OpenAsync(path)).Value is not TextDocumentViewModel opened)
                    return false;
                document = opened;
            }
            else if (FormOf(document) is { } form)
            {
                // I-302: a location in a form text is shown in the Text view of its form document.
                _documents.Activate(form);
                if (form.ActiveView != DocumentView.Text)
                    form.ShowView(DocumentView.Text);
            }
            else
            {
                _documents.Activate(document);
            }

            // A location in a unit is shown in the Code view of its form document (I-218, I-302).
            if (document is FormDocumentViewModel { HasUnit: true, ActiveView: not DocumentView.Code } unit)
                unit.ShowView(DocumentView.Code);
            var position = entry.Current;
            var buffer = document.Buffer;
            var offset = buffer.GetOffset(Math.Clamp(position.Line, 1, buffer.LineCount), position.Column);
            document.Folding.Reveal(offset);
            document.Selection = TextSelection.At(offset);
            document.RevealCaret(third: true);
            return true;
        }
        finally
        {
            _navigating = false;
            _lastActive = _documents.ActiveDocument;
        }
    }

    private void Remove(int index)
    {
        if (index < 0)
            return;
        _entries.RemoveAt(index);
        if (index < _index)
            _index--;
    }

    /// <summary>The open form document whose Text view edits <paramref name="formText"/>; null for a listed document.</summary>
    private FormDocumentViewModel? FormOf(TextDocumentViewModel formText) =>
        _documents.Documents.OfType<FormDocumentViewModel>().FirstOrDefault(f => f.FormText == formText);

    private void OnDocumentOpened(object? sender, DocumentEventArgs e)
    {
        if (e.Document is not TextDocumentViewModel document)
            return;
        document.Jumped += OnJumped;
        if (document is FormDocumentViewModel form)
            form.FormText.Jumped += OnJumped; // I-302: far jumps in the Text view are recorded too
        foreach (var entry in _entries.Where(x => x.Document is null && x.Path is not null &&
                     string.Equals(x.Path, document.FilePath, StringComparison.OrdinalIgnoreCase)))
            entry.Bind(document); // an entry in a closed document follows the reopened one
    }

    private void OnDocumentClosed(object? sender, DocumentEventArgs e)
    {
        if (e.Document is not TextDocumentViewModel document)
            return;
        document.Jumped -= OnJumped;
        if (document is FormDocumentViewModel form)
        {
            // I-302: locations in the form text go with their document (reopening its .form opens the form document).
            form.FormText.Jumped -= OnJumped;
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Document == form.FormText)
                    Remove(i);
            }
        }

        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Document != document)
                continue;
            if (!_entries[i].Unbind())
                Remove(i); // an untitled document cannot be reopened
        }

        _commands?.InvalidateCanExecute();
    }

    private void OnJumped(object? sender, TextPosition from) => RecordDeparture((TextDocumentViewModel)sender!, from);

    /// <summary>
    /// IDE-ED-100: switching (or opening) documents records the location left in the previous one: the caret of the
    /// editor its visible view showed (I-302: the form text in the Text view; nothing from a Design view).
    /// </summary>
    private void OnActiveDocumentChanged(object? sender, EventArgs e)
    {
        var active = _documents.ActiveDocument;
        if (!_navigating && _lastActive is { } last && last != active && _documents.Documents.Contains(last) && last.VisibleText is { } left)
            RecordDeparture(left, left.ViewState.Caret);
        _lastActive = active;
    }

    /// <summary>A history location: anchored in its document while that is open, else a path and a position.</summary>
    private sealed class Entry
    {
        private ITextAnchor? _anchor;
        private TextPosition _position;

        public Entry(TextDocumentViewModel document, TextPosition position)
        {
            _position = position;
            Name = document.Title;
            Path = document.FilePath;
            Bind(document);
        }

        public TextDocumentViewModel? Document { get; private set; }

        public string? Path { get; private set; }

        public string Name { get; private set; }

        public TextPosition Current =>
            Document is { } document && _anchor is { IsDeleted: false } anchor ? document.Buffer.GetPosition(anchor.Offset) : _position;

        public void Bind(TextDocumentViewModel document)
        {
            Document = document;
            var buffer = document.Buffer;
            var offset = buffer.GetOffset(Math.Clamp(_position.Line, 1, buffer.LineCount), _position.Column);
            _position = buffer.GetPosition(offset);
            _anchor = buffer.CreateAnchor(offset, movesWithInsertionBefore: false);
        }

        /// <summary>The document closed: keeps its file and the position; false for an untitled document.</summary>
        public bool Unbind()
        {
            _position = Current;
            Path = Document!.FilePath;
            Name = Document.Title;
            Document = null;
            _anchor = null;
            return Path is not null;
        }

        /// <summary>IDE-ED-100 merging: the same document, within 10 lines.</summary>
        public bool IsNear(Entry other)
        {
            var same = Document is not null ? Document == other.Document
                : Path is not null && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);
            return same && Math.Abs(Current.Line - other.Current.Line) <= TextDocumentViewModel.FarJumpLines;
        }
    }

    private sealed class HistoryHandler(Func<bool> canExecute, Func<Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute();
    }

    private sealed class Handler(NavigationService owner) : ICommandHandler
    {
        public bool CanExecute(string? argument) => owner.Active is not null;

        public async Task ExecuteAsync(string? argument)
        {
            if (owner._documents.ActiveDocument is not { VisibleText: { } document } host)
                return;
            var dialog = new GoToLineViewModel(document.Buffer.LineCount, document.ViewState.Caret.Line);
            if (await owner._dialogs.ShowAsync(dialog) is { } target && owner._documents.Documents.Contains(host))
                owner.GoToLine(document, target.Line, target.Column);
        }
    }

    /// <summary>menus.md "text": the editor of the active document's visible view (I-302: the form text in the Text view).</summary>
    private TextDocumentViewModel? Active => _documents.ActiveDocument?.VisibleText;
}
