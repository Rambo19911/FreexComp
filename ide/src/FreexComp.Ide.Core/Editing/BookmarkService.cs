using System.Globalization;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// A bookmark as stored in the session (IDE-ED-114, 118): absolute path, 1-based line, number 1–9 or null, name or null.
/// <see cref="Text"/> is the preview of its line (IDE-ED-119) and <see cref="Id"/> tells the bookmarks of the service
/// apart while their lines move (0 = not from the service); neither is stored.
/// </summary>
public sealed record BookmarkInfo(string Path, int Line, int? Number, string? Name = null)
{
    public string Text { get; init; } = "";

    public int Id { get; init; }
}

/// <summary>
/// Bookmarks (editor-features.md §11, IDE-ED-110…119): at most one per line, each number once, an optional name; anchored
/// to the start of their line in open documents (a closed document keeps the line), drawn as glyphs, stored in the
/// session, each with the preview text of its line. Owns the <c>search.bookmark.*</c> commands. UI thread only.
/// </summary>
public sealed class BookmarkService
{
    /// <summary>IDE-ED-116.</summary>
    public const int MaxNameLength = 80;

    /// <summary>IDE-ED-119.</summary>
    public const int MaxPreviewLength = 200;

    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5);

    private readonly IDocumentService _documents;
    private readonly INavigationService _navigation;
    private readonly IStatusService _status;
    private readonly IFileSystem _fileSystem;
    private readonly List<Bookmark> _bookmarks = [];
    private readonly Dictionary<TextDocumentViewModel, Glyphs> _attached = [];
    private int _lastId;

    public BookmarkService(IDocumentService documents, INavigationService navigation, IStatusService status, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _documents = documents;
        _navigation = navigation;
        _status = status;
        _fileSystem = fileSystem;
        documents.DocumentOpened += (_, e) =>
        {
            if (e.Document is TextDocumentViewModel { FilePath: not null } text)
                Attach(text);
        };
        documents.DocumentClosed += (_, e) =>
        {
            if (e.Document is TextDocumentViewModel text && Detach(text) is { Count: > 0 } detached)
                ReadPreviews(detached); // IDE-ED-119: the closed document's lines come from disk again
        };
    }

    /// <summary>
    /// Any bookmark was added, removed, renamed, renumbered or moved, or a preview changed (IDE-ED-119); the session
    /// schedules a write, the Bookmarks window redraws.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>The background reads of previews from disk started so far (completed when none is under way).</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    /// <summary>Every bookmark, by path and line.</summary>
    public IReadOnlyList<BookmarkInfo> All =>
        [.. _bookmarks.Select(Info).OrderBy(b => b.Path, StringComparer.OrdinalIgnoreCase).ThenBy(b => b.Line)];

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
                _bookmarks.Add(new Bookmark(info.Path, ++_lastId) { Number = info.Number, Name = NormalizeName(info.Name), Line = info.Line });
        }

        foreach (var document in _documents.Documents.OfType<TextDocumentViewModel>().Where(d => d.FilePath is not null))
            Attach(document);
        Changed?.Invoke(this, EventArgs.Empty);
        ReadPreviews([.. _bookmarks.Where(b => b.Document is null)]);
    }

    /// <summary>
    /// IDE-ED-116: the text of a name — its first line, trimmed, cut at <see cref="MaxNameLength"/> characters; null when
    /// nothing is left (no name).
    /// </summary>
    public static string? NormalizeName(string? name)
    {
        if (name is null)
            return null;
        var end = name.AsSpan().IndexOfAny('\r', '\n');
        var text = (end >= 0 ? name[..end] : name).Trim();
        if (text.Length > MaxNameLength)
            text = text[..MaxNameLength].TrimEnd();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// IDE-ED-117: the bookmark on <paramref name="line"/> of the open document, added unnumbered when the line has none
    /// (an existing one is kept as it is).
    /// </summary>
    public BookmarkInfo Ensure(TextDocumentViewModel document, int line)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_attached.ContainsKey(document))
            throw new ArgumentException("The document holds no bookmarks.", nameof(document));
        line = Math.Clamp(line, 1, document.Buffer.LineCount);
        if (_bookmarks.FirstOrDefault(b => b.Document == document && CurrentLine(b) == line) is { } existing)
            return Info(existing);
        var added = Add(document, line, null);
        Refresh(document);
        Changed?.Invoke(this, EventArgs.Empty);
        return Info(added);
    }

    /// <summary>IDE-ED-116: gives the bookmark the name (<see cref="NormalizeName"/>; empty = no name). False when it no longer exists.</summary>
    public bool Rename(BookmarkInfo bookmark, string? name)
    {
        if (Find(bookmark) is not { } found)
            return false;
        var normalized = NormalizeName(name);
        if (found.Name != normalized)
        {
            found.Name = normalized;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>Removes the bookmarks (UI-TW-1502); those that no longer exist are skipped.</summary>
    public void Remove(IEnumerable<BookmarkInfo> bookmarks)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        var found = bookmarks.Select(Find).OfType<Bookmark>().Distinct().ToList();
        if (found.Count == 0)
            return;
        var documents = found.Select(b => b.Document).OfType<TextDocumentViewModel>().Distinct().ToList();
        foreach (var bookmark in found)
            Remove(bookmark);
        foreach (var document in documents)
            Refresh(document);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>IDE-ED-112 for one bookmark: opens the document when needed, caret at column 1 of the line (navigation-history entry).</summary>
    public Task GoToAsync(BookmarkInfo bookmark) => Find(bookmark) is { } found ? GoToAsync(found) : Task.CompletedTask;

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
    public TextDocumentViewModel? Editor => _documents.ActiveDocument?.VisibleText is { FilePath: not null } text && _attached.ContainsKey(text)
        ? text
        : null;

    private bool HasEditor() => Editor is not null;

    private Bookmark? Find(BookmarkInfo? info) =>
        info is null ? null
        : info.Id != 0 ? _bookmarks.FirstOrDefault(b => b.Id == info.Id)
        : _bookmarks.FirstOrDefault(b => SamePath(b.Path, info.Path) && CurrentLine(b) == info.Line);

    /// <summary>IDE-ED-111; IDE-ED-116: <c>toggle&lt;N&gt;</c> on a line with a bookmark keeps its name and sets the number.</summary>
    private Task Toggle(int? number)
    {
        if (Editor is not { } document)
            return Task.CompletedTask;
        var line = document.ViewState.Caret.Line;
        var onLine = _bookmarks.FirstOrDefault(b => b.Document == document && CurrentLine(b) == line);
        var others = new List<TextDocumentViewModel>();
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
            // The number moves (it exists once); a named bookmark that had it stays without it, an unnamed one goes.
            if (_bookmarks.FirstOrDefault(b => b.Number == number) is { } existing)
            {
                if (existing.Document is { } other)
                    others.Add(other);
                if (existing.Name is null)
                    Remove(existing);
                else
                    existing.Number = null;
            }

            if (onLine is not null)
                onLine.Number = number;
            else
                Add(document, line, number);
            _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Editor_BookmarkSet, number, line), StatusDuration);
        }

        foreach (var other in others.Where(o => o != document))
            Refresh(other);
        Refresh(document);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>IDE-ED-112: opens the document when needed, caret at column 1 of the line (navigation-history entry).</summary>
    private Task GoToAsync(int number)
    {
        if (_bookmarks.FirstOrDefault(b => b.Number == number) is not { } bookmark)
        {
            _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Editor_BookmarkMissing, number), StatusDuration);
            return Task.CompletedTask;
        }

        return GoToAsync(bookmark);
    }

    private async Task GoToAsync(Bookmark bookmark)
    {
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

    private Bookmark Add(TextDocumentViewModel document, int line, int? number)
    {
        var bookmark = new Bookmark(document.FilePath!, ++_lastId) { Number = number, Line = line };
        _bookmarks.Add(bookmark);
        Anchor(bookmark, document);
        return bookmark;
    }

    private void Remove(Bookmark bookmark)
    {
        _bookmarks.Remove(bookmark);
        bookmark.Document = null;
        bookmark.Anchor = null;
    }

    private BookmarkInfo Info(Bookmark bookmark)
    {
        var line = CurrentLine(bookmark);
        return new BookmarkInfo(bookmark.Path, line, bookmark.Number, bookmark.Name)
        {
            Text = bookmark.Document is { } document ? Preview(document.Buffer, line) : bookmark.Preview,
            Id = bookmark.Id,
        };
    }

    /// <summary>IDE-ED-119: the line's text without leading and trailing whitespace, at most <see cref="MaxPreviewLength"/> characters.</summary>
    private static string Preview(ITextBuffer buffer, int line)
    {
        if (line < 1 || line > buffer.LineCount)
            return "";
        var (start, length, _) = buffer.GetLine(line);
        return Preview(buffer.GetText(start, length));
    }

    private static string Preview(string line)
    {
        var text = line.Trim();
        return text.Length > MaxPreviewLength ? text[..MaxPreviewLength] : text;
    }

    /// <summary>
    /// IDE-ED-119: reads the lines of the bookmarks from their files on a pool thread (IDE-ARCH-081); a bookmark whose
    /// document opened or whose line changed meanwhile keeps what it shows. Raises <see cref="Changed"/> when a preview changed.
    /// </summary>
    private void ReadPreviews(IReadOnlyList<Bookmark> bookmarks)
    {
        if (bookmarks.Count == 0)
            return;
        var previous = Pending;
        Pending = Task.WhenAll(previous, ReadPreviewsAsync(bookmarks));
    }

    private async Task ReadPreviewsAsync(IReadOnlyList<Bookmark> bookmarks)
    {
        var requests = bookmarks.Select(b => (Bookmark: b, b.Path, b.Line)).ToList();
        var fileSystem = _fileSystem;
        var texts = await Task.Run(() => ReadLines(fileSystem, [.. requests.Select(r => (r.Path, r.Line))]));
        var changed = false;
        for (var i = 0; i < requests.Count; i++)
        {
            var (bookmark, _, line) = requests[i];
            if (bookmark.Document is null && bookmark.Line == line && _bookmarks.Contains(bookmark) && bookmark.Preview != texts[i])
            {
                bookmark.Preview = texts[i];
                changed = true;
            }
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The preview of each (path, line), each file read once; a file that cannot be read or is binary gives "".</summary>
    private static string[] ReadLines(IFileSystem fileSystem, IReadOnlyList<(string Path, int Line)> requests)
    {
        var lines = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var result = new string[requests.Count];
        for (var i = 0; i < requests.Count; i++)
        {
            var (path, line) = requests[i];
            if (!lines.TryGetValue(path, out var text))
                lines[path] = text = ReadFileLines(fileSystem, path);
            result[i] = line >= 1 && line <= text.Length ? Preview(text[line - 1]) : "";
        }

        return result;
    }

    private static string[] ReadFileLines(IFileSystem fileSystem, string path)
    {
        try
        {
            var detection = EncodingDetector.Detect(fileSystem.ReadAllBytes(path));
            return detection.IsBinary ? [] : detection.Text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
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

    /// <summary>Detaches the document; returns its bookmarks, which keep the line and the preview they had.</summary>
    private List<Bookmark> Detach(TextDocumentViewModel document)
    {
        if (!_attached.Remove(document, out var glyphs))
            return [];
        var detached = _bookmarks.Where(b => b.Document == document).ToList();
        foreach (var bookmark in detached)
        {
            bookmark.Line = CurrentLine(bookmark);
            bookmark.Preview = Preview(document.Buffer, bookmark.Line);
            bookmark.Document = null;
            bookmark.Anchor = null;
        }

        document.Buffer.Changed -= glyphs.OnBufferChanged;
        document.Decorations.Remove(glyphs);
        return detached;
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

    private sealed class Bookmark(string path, int id)
    {
        public string Path { get; } = path;

        public int Id { get; } = id;

        public int? Number { get; set; }

        /// <summary>IDE-ED-116: null = no name.</summary>
        public string? Name { get; set; }

        /// <summary>The line while detached; refreshed from the anchor while attached.</summary>
        public int Line { get; set; }

        /// <summary>IDE-ED-119: the preview while detached (from disk, or the buffer's at the close).</summary>
        public string Preview { get; set; } = "";

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
