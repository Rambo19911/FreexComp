using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Build;

/// <summary>Position tracking and navigation of Messages rows (IDE-P3-064…066).</summary>
public sealed partial class MessagesService
{
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5);

    private readonly IDocumentService? _documents;
    private readonly INavigationService? _navigation;
    private readonly IFileSystem? _fileSystem;
    private readonly IStatusService? _status;
    private readonly ILayoutService? _layout;
    private readonly Dictionary<string, long> _versionsAtBuild = new(StringComparer.OrdinalIgnoreCase);
    private MessageItem? _current;

    /// <summary>A model without navigation (tests of the mapping).</summary>
    public MessagesService()
    {
    }

    public MessagesService(IDocumentService documents, INavigationService navigation, IFileSystem fileSystem, IStatusService status,
        ILayoutService? layout = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
        _navigation = navigation;
        _fileSystem = fileSystem;
        _status = status;
        _layout = layout;
        documents.DocumentOpened += (_, e) => OnDocumentOpened(e.Document);
    }

    /// <summary>UI-TW-405: rows hidden by the Messages toggles and filter are skipped by Next/Previous (set by the view-model).</summary>
    public Func<MessageItem, bool>? IsVisible { get; set; }

    /// <summary>The row navigated to last (IDE-P3-066); null after a new build or <see cref="Clear"/>.</summary>
    public MessageItem? Current => _current;

    /// <summary>
    /// A build or check starts (IDE-P3-064, -068): clears the list and records the version of every open buffer, so
    /// rows can be anchored where the build saw the text.
    /// </summary>
    public void BeginBuild()
    {
        Clear();
        _versionsAtBuild.Clear();
        foreach (var document in OpenTextDocuments())
            _versionsAtBuild[document.FilePath!] = document.Buffer.Version;
    }

    /// <summary>
    /// IDE-P3-065: opens the file (normal open path), puts the caret on the row's position — the anchor when it still
    /// exists, else the original line and column clamped to the document — selects the span and focuses the editor.
    /// </summary>
    public async Task NavigateToAsync(MessageItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsNavigable || _documents is null || _navigation is null)
            return;
        _current = item;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        var location = item.Location!;
        if (_fileSystem?.FileExists(location.FilePath!) == false)
        {
            _status?.ShowMessage(Strings.Msg_Messages_SourceMissing, StatusDuration);
            return;
        }

        var opened = await _documents.OpenAsync(location.FilePath!);
        if (opened.Value is not TextDocumentViewModel document)
            return;
        if (item.Anchor is null && item.BufferVersionAtBuild is null)
            TryAnchor(item, document);

        var buffer = document.Buffer;
        int start;
        if (item.Anchor is { IsDeleted: false } anchor)
        {
            start = Math.Min(anchor.Offset, buffer.Length);
        }
        else
        {
            var line = Math.Clamp(location.Line, 1, buffer.LineCount);
            var (lineStart, lineLength, _) = buffer.GetLine(line);
            start = lineStart + Math.Clamp(location.Column, 1, lineLength + 1) - 1;
            if (item.BufferVersionAtBuild is { } version && version != buffer.Version)
                _status?.ShowMessage(Strings.Msg_Messages_LineStale, StatusDuration);
        }

        var position = buffer.GetPosition(start);
        _navigation.GoToLine(document, position.Line, position.Column);
        var length = location.EndLine == location.Line ? Math.Max(0, location.EndColumn - location.Column) : 0;
        var (spanLineStart, spanLineLength, _) = buffer.GetLine(position.Line);
        var end = Math.Min(start + length, spanLineStart + spanLineLength);
        if (end > start)
            document.Selection = new TextSelection(start, end);
        _layout?.Layout.FocusPane(Models.Pane.Documents);
    }

    /// <summary>
    /// IDE-P3-066: the next (previous) navigable row after the current one, wrapping; summary and position-less rows are
    /// skipped. Without a current row Next goes to the first navigable row and Previous to the last.
    /// </summary>
    public Task NavigateNextAsync(bool forward)
    {
        var rows = _items.List.Where(i => i.IsNavigable && !i.IsSummary && (IsVisible?.Invoke(i) ?? true)).ToList();
        if (rows.Count == 0)
            return Task.CompletedTask;
        var index = _current is null ? -1 : rows.IndexOf(_current);
        var next = index < 0
            ? (forward ? 0 : rows.Count - 1)
            : (index + (forward ? 1 : -1) + rows.Count) % rows.Count;
        return NavigateToAsync(rows[next]);
    }

    /// <summary><see cref="Current"/> changed (the Messages list selects that row, UI-TW-405).</summary>
    public event EventHandler? CurrentChanged;

    partial void ClearNavigation() => _current = null;

    partial void OnAdded(MessageItem item)
    {
        if (!item.IsNavigable)
            return;
        var document = OpenTextDocuments().FirstOrDefault(d => SamePath(d.FilePath, item.Location!.FilePath));
        if (document is not null)
            TryAnchor(item, document);
    }

    private void OnDocumentOpened(DocumentViewModel opened)
    {
        // Opened later: anchor when the buffer still holds the disk text the build compiled.
        if (opened is not TextDocumentViewModel { FilePath: { } path, IsModified: false } document)
            return;
        foreach (var item in _items.List.Where(i => i.Anchor is null && i.BufferVersionAtBuild is null && i.IsNavigable
                     && SamePath(i.Location!.FilePath, path)))
        {
            Anchor(item, document);
        }
    }

    private void TryAnchor(MessageItem item, TextDocumentViewModel document)
    {
        if (_versionsAtBuild.TryGetValue(document.FilePath!, out var version))
        {
            item.BufferVersionAtBuild = version;
            if (version == document.Buffer.Version)
                Anchor(item, document);
        }
        else if (!document.IsModified)
        {
            Anchor(item, document);
        }
    }

    private static void Anchor(MessageItem item, TextDocumentViewModel document)
    {
        var buffer = document.Buffer;
        var location = item.Location!;
        if (location.Line > buffer.LineCount)
            return;
        var (lineStart, lineLength, _) = buffer.GetLine(location.Line);
        var offset = lineStart + Math.Clamp(location.Column, 1, lineLength + 1) - 1;
        item.Anchor = buffer.CreateAnchor(offset, movesWithInsertionBefore: true);
    }

    private IEnumerable<TextDocumentViewModel> OpenTextDocuments() =>
        _documents?.Documents.OfType<TextDocumentViewModel>().Where(d => d.FilePath is not null) ?? [];

    private static bool SamePath(string? a, string? b) => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
