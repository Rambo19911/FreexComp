using System.Globalization;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Editing;

/// <summary>A bookmark as stored in the session (IDE-ED-114): absolute path, 1-based line, number 1–9 or null.</summary>
public sealed record BookmarkInfo(string Path, int Line, int? Number);

/// <summary>
/// Bookmarks (editor-features.md §11, IDE-ED-110…114): at most one per line, each number once; anchored to the start of
/// their line in open documents (a closed document keeps the line), drawn as glyphs, stored in the session. Owns the
/// <c>search.bookmark.*</c> commands. UI thread only.
/// </summary>
public sealed class BookmarkService
{
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5);

    private readonly IDocumentService _documents;
    private readonly INavigationService _navigation;
    private readonly IStatusService _status;
    private readonly List<Bookmark> _bookmarks = [];
    private readonly Dictionary<TextDocumentViewModel, Glyphs> _attached = [];

    public BookmarkService(IDocumentService documents, INavigationService navigation, IStatusService status)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
        _navigation = navigation;
        _status = status;
        documents.DocumentOpened += (_, e) =>
        {
            if (e.Document is TextDocumentViewModel { FilePath: not null } text)
                Attach(text);
        };
        documents.DocumentClosed += (_, e) =>
        {
            if (e.Document is TextDocumentViewModel text)
                Detach(text);
        };
    }

    /// <summary>Any bookmark was added, removed or moved (the session schedules a write).</summary>
    public event EventHandler? Changed;

    /// <summary>Every bookmark, by path and line.</summary>
    public IReadOnlyList<BookmarkInfo> All =>
        [.. _bookmarks.Select(b => new BookmarkInfo(b.Path, CurrentLine(b), b.Number)).OrderBy(b => b.Path, StringComparer.OrdinalIgnoreCase).ThenBy(b => b.Line)];

    /// <summary>IDE-ED-114: replaces all bookmarks by the session's (open documents re-anchor them, IDE-ARCH-073 clamping).</summary>
    public void Load(IEnumerable<BookmarkInfo> bookmarks)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        foreach (var document in _attached.Keys.ToList())
            Detach(document);
        _bookmarks.Clear();
        var numbers = new HashSet<int>();
        foreach (var info in bookmarks)
        {
            if (info.Line < 1 || (info.Number is { } n && (n is < 1 or > 9 || !numbers.Add(n))))
                continue;
            if (!_bookmarks.Any(b => SamePath(b.Path, info.Path) && b.Line == info.Line))
                _bookmarks.Add(new Bookmark(info.Path, info.Number) { Line = info.Line });
        }

        foreach (var document in _documents.Documents.OfType<TextDocumentViewModel>().Where(d => d.FilePath is not null))
            Attach(document);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler("search.bookmark.toggle", new Handler(HasEditor, () => Toggle(null)));
        commands.RegisterHandler("search.bookmark.next", new Handler(HasEditor, () => Step(forward: true)));
        commands.RegisterHandler("search.bookmark.previous", new Handler(HasEditor, () => Step(forward: false)));
        commands.RegisterHandler("search.bookmark.clearAll", new Handler(() => _bookmarks.Count > 0, ClearAll));
        for (var n = 1; n <= 9; n++)
        {
            var number = n;
            var suffix = n.ToString(CultureInfo.InvariantCulture);
            commands.RegisterHandler("search.bookmark.toggle" + suffix, new Handler(HasEditor, () => Toggle(number)));
            commands.RegisterHandler("search.bookmark.goTo" + suffix, new Handler(() => true, () => GoToAsync(number)));
        }
    }

    /// <summary>The editor of the active document's visible view (I-302) when it holds bookmarks (a form text does not).</summary>
    private TextDocumentViewModel? Editor => _documents.ActiveDocument?.VisibleText is { FilePath: not null } text && _attached.ContainsKey(text)
        ? text
        : null;

    private bool HasEditor() => Editor is not null;

    /// <summary>IDE-ED-111.</summary>
    private Task Toggle(int? number)
    {
        if (Editor is not { } document)
            return Task.CompletedTask;
        var line = document.ViewState.Caret.Line;
        var onLine = _bookmarks.FirstOrDefault(b => b.Document == document && CurrentLine(b) == line);
        if (number is null)
        {
            if (onLine is not null)
                Remove(onLine);
            else
                Add(document, line, null);
        }
        else if (onLine?.Number == number)
        {
            Remove(onLine);
        }
        else
        {
            if (onLine is not null)
                Remove(onLine);
            if (_bookmarks.FirstOrDefault(b => b.Number == number) is { } existing)
                Remove(existing);
            Add(document, line, number);
            _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Editor_BookmarkSet, number, line), StatusDuration);
        }

        Refresh(document);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>IDE-ED-112: opens the document when needed, caret at column 1 of the line (navigation-history entry).</summary>
    private async Task GoToAsync(int number)
    {
        if (_bookmarks.FirstOrDefault(b => b.Number == number) is not { } bookmark)
        {
            _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Editor_BookmarkMissing, number), StatusDuration);
            return;
        }

        if ((await _documents.OpenAsync(bookmark.Path)).Value is not TextDocumentViewModel document)
            return;
        _navigation.GoToLine(document, CurrentLine(bookmark), 1);
    }

    /// <summary>IDE-ED-112: the active document's bookmarks in line order, wrapping.</summary>
    private Task Step(bool forward)
    {
        if (Editor is not { } document)
            return Task.CompletedTask;
        var lines = _bookmarks.Where(b => b.Document == document).Select(CurrentLine).Order().ToList();
        if (lines.Count == 0)
            return Task.CompletedTask;
        var caret = document.ViewState.Caret.Line;
        var target = forward
            ? lines.FirstOrDefault(l => l > caret, lines[0])
            : lines.LastOrDefault(l => l < caret, lines[^1]);
        _navigation.GoToLine(document, target, 1);
        return Task.CompletedTask;
    }

    private Task ClearAll()
    {
        foreach (var bookmark in _bookmarks.ToList())
            Remove(bookmark);
        foreach (var glyphs in _attached.Values)
            glyphs.Raise();
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private void Add(TextDocumentViewModel document, int line, int? number)
    {
        var bookmark = new Bookmark(document.FilePath!, number) { Line = line };
        _bookmarks.Add(bookmark);
        Anchor(bookmark, document);
    }

    private void Remove(Bookmark bookmark)
    {
        _bookmarks.Remove(bookmark);
        bookmark.Document = null;
        bookmark.Anchor = null;
    }

    private void Attach(TextDocumentViewModel document)
    {
        if (_attached.ContainsKey(document))
            return;
        var glyphs = new Glyphs(this, document);
        _attached[document] = glyphs;
        document.Buffer.Changed += glyphs.OnBufferChanged;
        document.Decorations.Add(glyphs);
        foreach (var bookmark in _bookmarks.Where(b => b.Document is null && SamePath(b.Path, document.FilePath)))
        {
            bookmark.Line = Math.Min(bookmark.Line, document.Buffer.LineCount); // IDE-ARCH-073: clamped to the last line
            Anchor(bookmark, document);
        }

        Refresh(document);
    }

    private void Detach(TextDocumentViewModel document)
    {
        if (!_attached.Remove(document, out var glyphs))
            return;
        foreach (var bookmark in _bookmarks.Where(b => b.Document == document))
        {
            bookmark.Line = CurrentLine(bookmark);
            bookmark.Document = null;
            bookmark.Anchor = null;
        }

        document.Buffer.Changed -= glyphs.OnBufferChanged;
        document.Decorations.Remove(glyphs);
    }

    private static void Anchor(Bookmark bookmark, TextDocumentViewModel document)
    {
        var buffer = document.Buffer;
        var (start, length, delimiter) = buffer.GetLine(Math.Clamp(bookmark.Line, 1, buffer.LineCount));
        bookmark.Document = document;
        bookmark.Anchor = buffer.CreateAnchor(start, movesWithInsertionBefore: true); // a line inserted above moves it down
        (bookmark.Start, bookmark.Length, bookmark.Delimiter) = (start, length, delimiter);
    }

    private static int CurrentLine(Bookmark bookmark) =>
        bookmark.Document is { } document && bookmark.Anchor is { IsDeleted: false } anchor
            ? document.Buffer.GetPosition(anchor.Offset).Line
            : bookmark.Line;

    /// <summary>
    /// IDE-ED-113 after an edit: a bookmark whose whole line (with its break) was removed goes; when lines were joined the
    /// upper line's bookmark stays (one per line); the rest are re-anchored at their line's start.
    /// </summary>
    private void OnEdited(TextDocumentViewModel document, TextChangedEventArgs change)
    {
        var mine = _bookmarks.Where(b => b.Document == document).ToList();
        foreach (var bookmark in mine)
        {
            var wholeLine = change.RemovedLength > 0 && bookmark.Delimiter > 0 && change.Offset <= bookmark.Start
                && change.Offset + change.RemovedLength >= bookmark.Start + bookmark.Length + bookmark.Delimiter;
            if (bookmark.Anchor is not { IsDeleted: false } || wholeLine)
                Remove(bookmark);
        }

        foreach (var group in _bookmarks.Where(b => b.Document == document).GroupBy(CurrentLine).Where(g => g.Count() > 1))
        {
            foreach (var lower in group.OrderBy(b => b.Anchor!.Offset).Skip(1).ToList())
                Remove(lower);
        }

        Refresh(document);
        if (mine.Count > 0)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Every bookmark of the document anchored at its line's start, with the line's extent cached; glyphs redrawn.</summary>
    private void Refresh(TextDocumentViewModel document)
    {
        foreach (var bookmark in _bookmarks.Where(b => b.Document == document))
        {
            bookmark.Line = CurrentLine(bookmark);
            var (start, _, _) = document.Buffer.GetLine(bookmark.Line);
            if (bookmark.Anchor!.Offset != start)
                Anchor(bookmark, document);
            else
                (bookmark.Start, bookmark.Length, bookmark.Delimiter) = document.Buffer.GetLine(bookmark.Line);
        }

        if (_attached.TryGetValue(document, out var glyphs))
            glyphs.Raise();
    }

    private static bool SamePath(string? a, string? b) => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private sealed class Bookmark(string path, int? number)
    {
        public string Path { get; } = path;

        public int? Number { get; } = number;

        /// <summary>The line while detached; refreshed from the anchor while attached.</summary>
        public int Line { get; set; }

        public TextDocumentViewModel? Document { get; set; }

        public ITextAnchor? Anchor { get; set; }

        /// <summary>The line's start, content length and break length as of the last edit (IDE-ED-113 whole-line test).</summary>
        public int Start { get; set; }

        public int Length { get; set; }

        public int Delimiter { get; set; }
    }

    /// <summary>The glyph provider of one open document.</summary>
    private sealed class Glyphs(BookmarkService owner, TextDocumentViewModel document) : IEditorDecorationProvider
    {
        public event EventHandler? Changed;

        public IReadOnlyList<LineGlyph> GetLineGlyphs(int line) =>
            [.. owner._bookmarks.Where(b => b.Document == document && CurrentLine(b) == line).Select(b => new LineGlyph(LineGlyphKind.Bookmark, b.Number ?? 0))];

        public void OnBufferChanged(object? sender, TextChangedEventArgs e) => owner.OnEdited(document, e);

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Handler(Func<bool> canExecute, Func<Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute();
    }
}
