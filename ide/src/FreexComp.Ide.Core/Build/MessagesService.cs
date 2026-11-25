using FreexComp.Compiler;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Build;

/// <summary>Severity of a Messages row (IDE-P3-060): related locations are <see cref="Info"/>.</summary>
public enum MessageSeverity
{
    Error,
    Warning,
    Hint,
    Info,
}

/// <summary>A 1-based position; 0 = unknown (phase3-build-run.md §7.1).</summary>
public sealed record MessageLocation(string? FilePath, int Line, int Column, int EndLine, int EndColumn);

/// <summary>A list with <see cref="IReadOnlyCollection{T}.Count"/> and an indexer for virtualizing views (IDE-ARCH-053).</summary>
public interface IVirtualList<out T> : IReadOnlyList<T>;

/// <summary>One row of the Messages tool window (phase3-build-run.md §7.1).</summary>
public sealed class MessageItem
{
    internal MessageItem(int index, MessageSeverity severity, string code, string text, MessageLocation? location,
        IReadOnlyList<MessageItem> related, bool isSummary)
    {
        Index = index;
        Severity = severity;
        Code = code;
        Text = text;
        Location = location;
        Related = related;
        IsSummary = isSummary;
    }

    /// <summary>Arrival index within the current build.</summary>
    public int Index { get; }

    public MessageSeverity Severity { get; }

    /// <summary>"E2003"; "" for tool and summary rows.</summary>
    public string Code { get; }

    public string Text { get; }

    public MessageLocation? Location { get; }

    /// <summary>From the diagnostic's related locations, severity <see cref="MessageSeverity.Info"/>.</summary>
    public IReadOnlyList<MessageItem> Related { get; }

    public bool IsSummary { get; }

    /// <summary>Follows edits of the open file (IDE-P3-064).</summary>
    public ITextAnchor? Anchor { get; internal set; }

    /// <summary>Version of the open buffer when the build started; null when the file was not open.</summary>
    public long? BufferVersionAtBuild { get; internal set; }

    /// <summary>Replaces the file navigation of <see cref="MessagesService.NavigateToAsync"/> (form problems, DSG-FD-071).</summary>
    internal Action? Navigate { get; set; }

    /// <summary>A row with a file and a line can be navigated to (IDE-P3-062).</summary>
    public bool IsNavigable => Location is { FilePath: not null, Line: > 0 };
}

/// <summary>The Messages model (phase3-build-run.md §7.1). UI thread only.</summary>
public interface IMessagesService
{
    /// <summary>All items in arrival order, the summary last.</summary>
    IVirtualList<MessageItem> Items { get; }

    int ErrorCount { get; }

    int WarningCount { get; }

    int HintCount { get; }

    void Clear();

    Task NavigateToAsync(MessageItem item);

    Task NavigateNextAsync(bool forward);

    event EventHandler? Changed;
}

/// <inheritdoc cref="IMessagesService"/>
public sealed partial class MessagesService : IMessagesService
{
    private readonly ItemList _items = new();
    private readonly HashSet<(DiagnosticSeverity, string, string, string?, int, int)> _seen = [];

    public IVirtualList<MessageItem> Items => _items;

    public int ErrorCount { get; private set; }

    public int WarningCount { get; private set; }

    public int HintCount { get; private set; }

    public event EventHandler? Changed;

    /// <summary>IDE-P3-067: empties the list and resets the counts.</summary>
    public void Clear()
    {
        var had = _items.Count > 0;
        _items.List.Clear();
        _seen.Clear();
        ErrorCount = WarningCount = HintCount = 0;
        ClearNavigation();
        if (had)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds the rows of <paramref name="diagnostics"/> (IDE-P3-060); one identical in severity, code, message, file, line
    /// and column to one already listed is dropped (IDE-P3-061). One <see cref="Changed"/> for the batch.
    /// </summary>
    public void AddDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var added = false;
        foreach (var d in diagnostics)
        {
            var location = Map(d.Location);
            if (!_seen.Add((d.Severity, d.Code, d.Message, location?.FilePath, location?.Line ?? 0, location?.Column ?? 0)))
                continue;
            var related = d.Related.IsDefaultOrEmpty
                ? []
                : d.Related.Select(r => new MessageItem(-1, MessageSeverity.Info, "", r.Message, Map(r.Location), [], false)).ToList();
            var severity = d.Severity switch
            {
                DiagnosticSeverity.Error => MessageSeverity.Error,
                DiagnosticSeverity.Warning => MessageSeverity.Warning,
                _ => MessageSeverity.Hint,
            };
            var item = new MessageItem(_items.Count, severity, d.Code, d.Message, location, related, false);
            _items.List.Add(item);
            Count(severity);
            OnAdded(item);
            added = true;
        }

        if (added)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// DSG-FD-071 <c>Show Problems</c>: the list is replaced by the problems of one form (1-based positions already);
    /// one <see cref="Changed"/>.
    /// </summary>
    /// <param name="navigate">DSG-FD-071: what a row's navigation does instead of opening the file (the form's views).</param>
    public void ShowFormDiagnostics(IEnumerable<FreexComp.Designer.Text.FormDiagnostic> diagnostics,
        Action<FreexComp.Designer.Text.FormDiagnostic>? navigate = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Clear();
        foreach (var d in diagnostics)
        {
            var severity = d.Severity switch
            {
                DiagnosticSeverity.Error => MessageSeverity.Error,
                DiagnosticSeverity.Warning => MessageSeverity.Warning,
                _ => MessageSeverity.Hint,
            };
            var location = d.Line > 0 ? new MessageLocation(d.Path, d.Line, d.Col, Math.Max(d.EndLine, d.Line), Math.Max(d.EndCol, d.Col)) : null;
            var item = new MessageItem(_items.Count, severity, d.Code, d.Message, location, [], false);
            if (navigate is not null)
            {
                var diagnostic = d;
                item.Navigate = () => navigate(diagnostic);
            }

            _items.List.Add(item);
            Count(severity);
            OnAdded(item);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>An error row without position and code (internal error, IDE-P3-034).</summary>
    public void AddError(string text)
    {
        _items.List.Add(new MessageItem(_items.Count, MessageSeverity.Error, "", text, null, [], false));
        ErrorCount++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A warning row without position and code (I-225: the database engine library was not deployed).</summary>
    public void AddWarning(string text)
    {
        _items.List.Add(new MessageItem(_items.Count, MessageSeverity.Warning, "", text, null, [], false));
        WarningCount++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The summary row, always last (IDE-P3-034); not counted (IDE-P3-063).</summary>
    public void AddSummary(string text, MessageSeverity severity)
    {
        _items.List.Add(new MessageItem(_items.Count, severity, "", text, null, [], true));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// IDE-P3-060: 0-based API position → 1-based; an empty span ends where it starts; no position → null.
    /// </summary>
    public static MessageLocation? Map(Location location)
    {
        if (!location.HasPosition || string.IsNullOrEmpty(location.Path))
            return null;
        var (start, end) = (location.LineSpan.Start, location.LineSpan.End);
        if (end.Line < start.Line || (end.Line == start.Line && end.Column <= start.Column))
            end = start;
        return new MessageLocation(location.Path, start.Line + 1, start.Column + 1, end.Line + 1, end.Column + 1);
    }

    partial void OnAdded(MessageItem item);

    partial void ClearNavigation();

    private void Count(MessageSeverity severity)
    {
        switch (severity)
        {
            case MessageSeverity.Error:
                ErrorCount++;
                break;
            case MessageSeverity.Warning:
                WarningCount++;
                break;
            case MessageSeverity.Hint:
                HintCount++;
                break;
        }
    }

    private sealed class ItemList : IVirtualList<MessageItem>
    {
        public List<MessageItem> List { get; } = [];

        public int Count => List.Count;

        public MessageItem this[int index] => List[index];

        public IEnumerator<MessageItem> GetEnumerator() => List.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
