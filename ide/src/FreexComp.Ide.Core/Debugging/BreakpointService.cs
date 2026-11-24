using System.Globalization;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Debugging;

/// <summary>
/// The source breakpoints of the open project (tool-windows.md §10, the debug-session interface §6.1): the list, the
/// gutter glyphs, the session persistence data and — while a debug session exists — the debugger's copy. UI thread only.
/// </summary>
public interface IBreakpointService
{
    /// <summary>Every breakpoint in creation order, lines as they are now.</summary>
    IReadOnlyList<BreakpointModel> All { get; }

    /// <summary>A breakpoint was added, removed, moved or changed, or its session state changed.</summary>
    event EventHandler? Changed;

    /// <summary>The breakpoint on the 1-based <paramref name="line"/> of the file, or null.</summary>
    BreakpointModel? Find(string path, int line);

    /// <summary>Removes the breakpoint on the line, or adds an enabled unconditional one.</summary>
    void Toggle(string path, int line);

    /// <summary>UI-DLG-190: adds the breakpoint, or updates the one already on that line.</summary>
    BreakpointModel Set(BreakpointInfo info);

    void Update(BreakpointModel breakpoint, bool enabled, string? condition, HitCountSpec? hitCount);

    void Remove(BreakpointModel breakpoint);

    void RemoveAll();

    /// <summary>Replaces all breakpoints by the session's (IDE-ARCH-073: clamped to the last line of an open document).</summary>
    void Load(IEnumerable<BreakpointInfo> breakpoints);

    /// <summary>
    /// A debug session starts (IDE-ARCH-077): the breakpoints get the ids 1, 2, … in list order; the result is the launch
    /// set per file. Until <see cref="DetachSession"/> every change is sent with <c>SetBreakpointsAsync</c> and
    /// <c>BreakpointChanged</c> updates state, line and hits.
    /// </summary>
    /// <param name="runTo">
    /// "Run to Cursor" without a session (DBG-232): a temporary breakpoint on that line joins the launch set, unless a
    /// breakpoint is there already; it is not listed and goes with <see cref="ClearTemporary"/>.
    /// </param>
    IReadOnlyList<SourceBreakpoints> AttachSession(IDebugSession session, (string Path, int Line)? runTo = null);

    /// <summary>The program stopped: the temporary breakpoint of a "Run to Cursor" start, if any, is taken from the session.</summary>
    void ClearTemporary();

    /// <summary>The session ended: ids, states and hits are cleared.</summary>
    void DetachSession();

    /// <summary>Completes once every change made so far has been answered by the session.</summary>
    Task WhenSyncedAsync();
}

/// <inheritdoc cref="IBreakpointService"/>
public sealed class BreakpointService : IBreakpointService
{
    private const string Category = "breakpoints";
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5);

    private readonly IDocumentService _documents;
    private readonly IProjectService _projects;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _pickers;
    private readonly IFileSystem _fileSystem;
    private readonly IStatusService _status;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILog _log;
    private readonly List<Entry> _entries = [];
    private readonly Dictionary<TextDocumentViewModel, Glyphs> _attached = [];
    private IDebugSession? _session;
    private EventHandler<BreakpointChangedEventArgs>? _sessionHandler;
    private int _nextId = 1;
    private (string Path, BreakpointSpec Spec)? _temporary;
    private Task _sync = Task.CompletedTask;

    public BreakpointService(IDocumentService documents, IProjectService projects, IDialogService dialogs, IFileDialogService pickers,
        IFileSystem fileSystem, IStatusService status, IUiDispatcher dispatcher, ILog log)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
        _projects = projects;
        _dialogs = dialogs;
        _pickers = pickers;
        _fileSystem = fileSystem;
        _status = status;
        _dispatcher = dispatcher;
        _log = log;
        foreach (var document in documents.Documents.OfType<TextDocumentViewModel>())
            Attach(document);
        documents.DocumentOpened += (_, e) =>
        {
            if (e.Document is TextDocumentViewModel text)
                Attach(text);
        };
        documents.DocumentClosed += (_, e) =>
        {
            if (e.Document is TextDocumentViewModel text)
                Detach(text);
        };
    }

    public event EventHandler? Changed;

    /// <summary>
    /// dialogs.md §19 <c>Condition</c>: evaluates the expression once while a session is paused; the result is the
    /// evaluator's message, or null when it is valid or cannot be checked now (no paused session).
    /// </summary>
    public Func<string, Task<string?>>? ValidateCondition { get; set; }

    public IReadOnlyList<BreakpointModel> All
    {
        get
        {
            foreach (var entry in _entries)
                entry.Model.Line = CurrentLine(entry);
            return [.. _entries.Select(e => e.Model)];
        }
    }

    public BreakpointModel? Find(string path, int line) => FindEntry(path, line)?.Model;

    public void Toggle(string path, int line)
    {
        if (FindEntry(path, line) is { } existing)
            RemoveEntry(existing);
        else
            Add(new BreakpointInfo(Path.GetFullPath(path), line));
        OnChanged(path);
    }

    public BreakpointModel Set(BreakpointInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (FindEntry(info.Path, info.Line) is not { } entry)
            entry = Add(info with { Path = Path.GetFullPath(info.Path) });
        Apply(entry.Model, info.Enabled, info.Condition, info.HitCount);
        OnChanged(info.Path);
        return entry.Model;
    }

    public void Update(BreakpointModel breakpoint, bool enabled, string? condition, HitCountSpec? hitCount)
    {
        ArgumentNullException.ThrowIfNull(breakpoint);
        Apply(breakpoint, enabled, condition, hitCount);
        OnChanged(breakpoint.Path);
    }

    public void Remove(BreakpointModel breakpoint)
    {
        if (_entries.FirstOrDefault(e => e.Model == breakpoint) is not { } entry)
            return;
        RemoveEntry(entry);
        OnChanged(breakpoint.Path);
    }

    public void RemoveAll()
    {
        var paths = _entries.Select(e => e.Model.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var entry in _entries.ToList())
            RemoveEntry(entry);
        foreach (var path in paths)
            Sync(path);
        RaiseChanged();
    }

    public void Load(IEnumerable<BreakpointInfo> breakpoints)
    {
        ArgumentNullException.ThrowIfNull(breakpoints);
        var paths = _entries.Select(e => e.Model.Path).ToList();
        foreach (var entry in _entries.ToList())
            RemoveEntry(entry);
        foreach (var info in breakpoints)
        {
            if (info.Line < 1 || FindEntry(info.Path, info.Line) is not null)
                continue;
            var entry = Add(info);
            Apply(entry.Model, info.Enabled, info.Condition, info.HitCount);
            entry.Model.LogMessage = info.LogMessage;
        }

        foreach (var path in paths.Concat(_entries.Select(e => e.Model.Path)).Distinct(StringComparer.OrdinalIgnoreCase))
            Sync(path);
        RaiseChanged();
    }

    public IReadOnlyList<SourceBreakpoints> AttachSession(IDebugSession session, (string Path, int Line)? runTo = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        DetachSession();
        _session = session;
        _nextId = 1;
        foreach (var entry in _entries)
        {
            entry.Model.Line = CurrentLine(entry);
            entry.Model.Id = _nextId++;
            entry.Model.State = entry.Model.Enabled ? BreakpointState.Pending : BreakpointState.Disabled; // DBG-409, until the session reports
        }

        // DBG-420: raised on the session's thread; the status is applied on the UI thread, in event order.
        _sessionHandler = (_, e) => _dispatcher.Post(() =>
        {
            if (_session == session)
                ApplyStatus(e.Status);
        });
        session.BreakpointChanged += _sessionHandler;
        if (runTo is { } target && FindEntry(target.Path, target.Line) is null)
            _temporary = (Path.GetFullPath(target.Path), new BreakpointSpec { Id = _nextId++, Line = target.Line });
        return [.. _entries.Select(e => e.Model.Path).Concat(_temporary is { } t ? [t.Path] : []).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new SourceBreakpoints(path, SpecsOf(path)))];
    }

    public void ClearTemporary()
    {
        if (_temporary is not { } temporary)
            return;
        _temporary = null;
        Sync(temporary.Path);
    }

    /// <summary>The complete set of one file as the session takes it: its breakpoints at their current lines, then the temporary one.</summary>
    private List<BreakpointSpec> SpecsOf(string path)
    {
        var specs = new List<BreakpointSpec>();
        foreach (var entry in _entries.Where(e => SamePath(e.Model.Path, path)))
        {
            entry.Model.Line = CurrentLine(entry);
            specs.Add(entry.Model.ToSpec());
        }

        if (_temporary is { } temporary && SamePath(temporary.Path, path))
            specs.Add(temporary.Spec);
        return specs;
    }

    public void DetachSession()
    {
        if (_session is not { } session)
            return;
        session.BreakpointChanged -= _sessionHandler;
        _session = null;
        _sessionHandler = null;
        _temporary = null;
        foreach (var entry in _entries)
            (entry.Model.Id, entry.Model.State, entry.Model.Hits, entry.Model.Message) = (0, null, 0, null);
        RaiseChanged();
    }

    public Task WhenSyncedAsync() => _sync;

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler("run.toggleBreakpoint", new Handler(() => Editor is not null, () =>
        {
            if (Editor is { } editor)
                Toggle(editor.FilePath!, editor.ViewState.Caret.Line);
            return Task.CompletedTask;
        }));
        commands.RegisterHandler("run.toggleBreakpointEnabled", new Handler(() => AtCaret is not null, () =>
        {
            if (AtCaret is { } breakpoint)
                Update(breakpoint, !breakpoint.Enabled, breakpoint.Condition, breakpoint.HitCount);
            return Task.CompletedTask;
        }));
        commands.RegisterHandler("run.addBreakpoint", new Handler(() => _projects.Project is not null, () => ShowDialogAsync(null)));
        commands.RegisterHandler("run.breakpointProperties", new Handler(() => AtCaret is not null, () => ShowDialogAsync(AtCaret)));
        commands.RegisterHandler("run.deleteAllBreakpoints", new Handler(() => _entries.Count > 0, DeleteAllAsync));
        Changed += (_, _) => commands.InvalidateCanExecute();
        _documents.ActiveDocumentChanged += (_, _) => commands.InvalidateCanExecute();
    }

    /// <summary>The glyph of a breakpoint (the debug-session interface §6.1, editor.md §3).</summary>
    public static LineGlyph GlyphOf(BreakpointModel breakpoint)
    {
        ArgumentNullException.ThrowIfNull(breakpoint);
        var kind = breakpoint.State == BreakpointState.Invalid ? LineGlyphKind.BreakpointUnverified
            : !breakpoint.Enabled || breakpoint.State == BreakpointState.Disabled
                ? (breakpoint.IsConditional ? LineGlyphKind.BreakpointConditionalDisabled : LineGlyphKind.BreakpointDisabled)
                : breakpoint.IsConditional ? LineGlyphKind.BreakpointConditional : LineGlyphKind.Breakpoint;
        return new LineGlyph(kind, ToolTip: kind == LineGlyphKind.BreakpointUnverified ? breakpoint.Message : null,
            Condition: breakpoint.Condition, PassCount: breakpoint.PassCount);
    }

    /// <summary>The active document when it can hold breakpoints: a saved Object Pascal text (menus.md §7).</summary>
    private TextDocumentViewModel? Editor =>
        _documents.ActiveDocument is TextDocumentViewModel { FilePath: not null } text && CanHold(text) ? text : null;

    private BreakpointModel? AtCaret =>
        ToolSelection?.Invoke() is { Count: > 0 } rows ? (rows.Count == 1 ? rows[0] : null)
        : Editor is { } editor ? Find(editor.FilePath!, editor.ViewState.Caret.Line) : null;

    /// <summary>
    /// The selected rows of the focused Breakpoints tool window (tool-windows.md §10 context menu): the row commands
    /// (<c>run.toggleBreakpointEnabled</c>, <c>run.breakpointProperties</c>) act on a single selected row; null when the
    /// window does not have focus.
    /// </summary>
    public Func<IReadOnlyList<BreakpointModel>?>? ToolSelection { get; set; }

    private static bool CanHold(TextDocumentViewModel document) => document.FilePath is not null && document.Language.Id == "pascal";

    private async Task ShowDialogAsync(BreakpointModel? breakpoint)
    {
        var active = _documents.ActiveDocument as TextDocumentViewModel;
        var files = _projects.Project is { } project
            ? project.Files.Select(f => Path.GetFullPath(Path.Combine(project.Directory, f.Path.Replace('/', '\\')))).ToList()
            : [];
        var model = breakpoint is null
            ? new BreakpointViewModel(_fileSystem, _pickers, _documents, files, active?.FilePath ?? "", active?.ViewState.Caret.Line ?? 1)
            : new BreakpointViewModel(_fileSystem, _pickers, _documents, files, breakpoint.ToInfo() with { Line = CurrentLineOf(breakpoint) });
        model.ValidateCondition = ValidateCondition;
        if (await _dialogs.ShowAsync(model) is not { } result)
            return;
        if (breakpoint is not null && _entries.Any(e => e.Model == breakpoint))
            Update(breakpoint, result.Enabled, result.Condition, result.HitCount);
        else
            Set(result);
    }

    /// <summary>UI-TW-802: asks <c>Msg.Debug.DeleteAllBreakpoints</c> when more than one exists.</summary>
    private async Task DeleteAllAsync()
    {
        if (_entries.Count > 1)
        {
            var count = _entries.Count.ToString(CultureInfo.CurrentCulture);
            if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Debug.DeleteAllBreakpoints", [count])) != "Delete")
                return;
        }

        RemoveAll();
    }

    private static void Apply(BreakpointModel model, bool enabled, string? condition, HitCountSpec? hitCount)
    {
        model.Enabled = enabled;
        model.Condition = string.IsNullOrWhiteSpace(condition) ? null : condition.Trim();
        model.HitCount = hitCount;
    }

    private Entry Add(BreakpointInfo info)
    {
        var entry = new Entry(new BreakpointModel(info.Path, info.Line));
        if (_session is not null)
            (entry.Model.Id, entry.Model.State) = (_nextId++, BreakpointState.Pending);
        _entries.Add(entry);
        if (_attached.Keys.FirstOrDefault(d => SamePath(d.FilePath, info.Path)) is { } document)
        {
            entry.Model.Line = Math.Clamp(info.Line, 1, document.Buffer.LineCount);
            Anchor(entry, document);
        }

        return entry;
    }

    private void RemoveEntry(Entry entry)
    {
        _entries.Remove(entry);
        entry.Document = null;
        entry.Anchor = null;
    }

    private Entry? FindEntry(string path, int line) => _entries.FirstOrDefault(e => SamePath(e.Model.Path, path) && CurrentLine(e) == line);

    private int CurrentLineOf(BreakpointModel breakpoint) => _entries.FirstOrDefault(e => e.Model == breakpoint) is { } entry ? CurrentLine(entry) : breakpoint.Line;

    /// <summary>A change by the user: the file's set goes to the session, the glyphs are redrawn, listeners are told.</summary>
    private void OnChanged(string path)
    {
        Sync(path);
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        foreach (var glyphs in _attached.Values)
            glyphs.Raise();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>I-140: the complete set of one file, sent in the order of the changes.</summary>
    private void Sync(string path)
    {
        if (_session is not { } session)
            return;
        _sync = SendAsync(_sync, session, Path.GetFullPath(path), SpecsOf(path));
    }

    private async Task SendAsync(Task previous, IDebugSession session, string path, IReadOnlyList<BreakpointSpec> specs)
    {
        await previous;
        try
        {
            var statuses = await session.SetBreakpointsAsync(path, specs);
            if (_session != session)
                return;
            foreach (var status in statuses)
                ApplyStatus(status);
        }
        catch (Exception e) when (e is DebuggerException or ObjectDisposedException or OperationCanceledException)
        {
            _log.Info(Category, $"Breakpoints of {path} were not sent: {e.Message}"); // the session is ending
        }
    }

    /// <summary>The debug-session interface §6.1: state → glyph, <c>ActualLine</c> → the breakpoint moves, hits.</summary>
    private void ApplyStatus(BreakpointStatus status)
    {
        if (_entries.FirstOrDefault(e => e.Model.Id == status.Id) is not { } entry)
            return;
        var model = entry.Model;
        (model.State, model.Hits, model.Message) = (status.State, status.HitCount, status.Message);
        if (status.ActualLine >= 1 && status.ActualLine != CurrentLine(entry))
        {
            model.Line = entry.Document is { } open ? Math.Clamp(status.ActualLine, 1, open.Buffer.LineCount) : status.ActualLine;
            if (entry.Document is { } document)
                Anchor(entry, document);
            _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_BreakpointMoved, model.Line), StatusDuration);
        }

        RaiseChanged();
    }

    private void Attach(TextDocumentViewModel document)
    {
        if (!CanHold(document) || _attached.ContainsKey(document))
            return;
        var glyphs = new Glyphs(this, document);
        _attached[document] = glyphs;
        document.Buffer.Changed += glyphs.OnBufferChanged;
        document.Decorations.Add(glyphs);
        document.GlyphMarginClick = line => Toggle(document.FilePath!, line); // UI-ED-012
        foreach (var entry in _entries.Where(e => e.Document is null && SamePath(e.Model.Path, document.FilePath)))
        {
            entry.Model.Line = Math.Min(entry.Model.Line, document.Buffer.LineCount); // IDE-ARCH-073
            Anchor(entry, document);
        }

        glyphs.Raise();
    }

    private void Detach(TextDocumentViewModel document)
    {
        if (!_attached.Remove(document, out var glyphs))
            return;
        foreach (var entry in _entries.Where(e => e.Document == document))
        {
            entry.Model.Line = CurrentLine(entry);
            entry.Document = null;
            entry.Anchor = null;
        }

        document.Buffer.Changed -= glyphs.OnBufferChanged;
        document.Decorations.Remove(glyphs);
        document.GlyphMarginClick = null;
    }

    private static void Anchor(Entry entry, TextDocumentViewModel document)
    {
        var buffer = document.Buffer;
        var (start, length, delimiter) = buffer.GetLine(Math.Clamp(entry.Model.Line, 1, buffer.LineCount));
        entry.Document = document;
        entry.Anchor = buffer.CreateAnchor(start, movesWithInsertionBefore: true); // a line inserted above moves it down
        (entry.Start, entry.Length, entry.Delimiter) = (start, length, delimiter);
    }

    private static int CurrentLine(Entry entry) =>
        entry.Document is { } document && entry.Anchor is { IsDeleted: false } anchor
            ? document.Buffer.GetPosition(anchor.Offset).Line
            : entry.Model.Line;

    /// <summary>
    /// After an edit (as bookmarks, IDE-ED-113): a breakpoint whose whole line was removed goes; when lines were joined the
    /// upper line's breakpoint stays; the rest are re-anchored at their line's start. The session is not told: its lines
    /// are those of the program that runs.
    /// </summary>
    private void OnEdited(TextDocumentViewModel document, TextChangedEventArgs change)
    {
        var mine = _entries.Where(e => e.Document == document).ToList();
        if (mine.Count == 0)
            return;
        foreach (var entry in mine)
        {
            var wholeLine = change.RemovedLength > 0 && entry.Delimiter > 0 && change.Offset <= entry.Start
                && change.Offset + change.RemovedLength >= entry.Start + entry.Length + entry.Delimiter;
            if (entry.Anchor is not { IsDeleted: false } || wholeLine)
                RemoveEntry(entry);
        }

        foreach (var group in _entries.Where(e => e.Document == document).GroupBy(CurrentLine).Where(g => g.Count() > 1))
        {
            foreach (var lower in group.OrderBy(e => e.Anchor!.Offset).Skip(1).ToList())
                RemoveEntry(lower);
        }

        foreach (var entry in _entries.Where(e => e.Document == document))
        {
            entry.Model.Line = CurrentLine(entry);
            var (start, length, delimiter) = document.Buffer.GetLine(entry.Model.Line);
            if (entry.Anchor!.Offset != start)
                Anchor(entry, document);
            else
                (entry.Start, entry.Length, entry.Delimiter) = (start, length, delimiter);
        }

        RaiseChanged();
    }

    private static bool SamePath(string? a, string? b) => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private sealed class Entry(BreakpointModel model)
    {
        public BreakpointModel Model { get; } = model;

        public TextDocumentViewModel? Document { get; set; }

        public ITextAnchor? Anchor { get; set; }

        /// <summary>The line's start, content length and break length as of the last edit (whole-line test).</summary>
        public int Start { get; set; }

        public int Length { get; set; }

        public int Delimiter { get; set; }
    }

    /// <summary>The glyph provider of one open document.</summary>
    private sealed class Glyphs(BreakpointService owner, TextDocumentViewModel document) : IEditorDecorationProvider
    {
        public event EventHandler? Changed;

        public IReadOnlyList<LineGlyph> GetLineGlyphs(int line) =>
            [.. owner._entries.Where(e => e.Document == document && CurrentLine(e) == line).Select(e => GlyphOf(e.Model))];

        public void OnBufferChanged(object? sender, TextChangedEventArgs e) => owner.OnEdited(document, e);

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Handler(Func<bool> canExecute, Func<Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute();
    }
}
