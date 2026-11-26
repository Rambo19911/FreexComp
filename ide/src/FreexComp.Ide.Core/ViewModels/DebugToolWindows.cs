using System.Globalization;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ToolWindows;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// Base of the debug tool windows (tool-windows.md §10–§15): the empty-state block shown instead of the content
/// (UI-TW-013) — <c>Msg.Debug.NoSession</c>, <c>NotPaused</c>, <c>BreakpointsEmpty</c>, <c>WatchesEmpty</c>,
/// <c>EventLogEmpty</c> — which follows the debug session.
/// </summary>
public abstract class DebugToolWindowViewModel : ToolWindowViewModel
{
    private string? _emptyHeading;
    private string? _emptySecondary;

    /// <summary>The heading of the empty state; null while the window has content to show.</summary>
    public string? EmptyHeading => _emptyHeading;

    /// <summary>The secondary line of the empty state, if it has one.</summary>
    public string? EmptySecondary => _emptySecondary;

    public bool IsEmpty => _emptyHeading is not null;

    /// <summary>The content stays under the empty state (Watches: its placeholder row is always present, UI-TW-1104).</summary>
    public virtual bool ShowsContentWhenEmpty => false;

    protected void SetEmpty(string? heading, string? secondary = null)
    {
        if (_emptyHeading == heading && _emptySecondary == secondary)
            return;
        (_emptyHeading, _emptySecondary) = (heading, secondary);
        OnPropertyChanged(nameof(EmptyHeading));
        OnPropertyChanged(nameof(EmptySecondary));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>"No debug session" / "Available while the program is paused" / nothing, for windows that show a paused program.</summary>
    protected void SetEmptyUnlessPaused(IDebugService debug)
    {
        ArgumentNullException.ThrowIfNull(debug);
        if (debug.Session is null)
            SetEmpty(Strings.Msg_Debug_NoSession, Strings.Msg_Debug_NoSession_Secondary);
        else if (!debug.IsPaused)
            SetEmpty(Strings.Msg_Debug_NotPaused);
        else
            SetEmpty(null);
    }

    protected static ToolWindowDefinition Define(string name, string titleKey, ToolGroupId home, DebugToolWindowViewModel viewModel) =>
        new(name, titleKey, home, 6, () => viewModel);

    /// <summary><c>&lt;file name&gt;:&lt;line&gt;</c> of a source location.</summary>
    protected static string FileAndLine(SourceLocation location) =>
        Path.GetFileName(location.Path) + ":" + location.Line.ToString(CultureInfo.CurrentCulture);
}

/// <summary>
/// Breakpoints (tool-windows.md §10): every source breakpoint of the project with its state, condition, pass count and
/// hits (the debug-session interface §6.1), with or without a session; <c>Msg.Debug.BreakpointsEmpty</c> while there is none.
/// </summary>
public sealed class BreakpointsViewModel : DebugGridViewModel
{
    public const string ToolName = "Breakpoints";

    private readonly IBreakpointService _breakpoints;
    private readonly IDebugService _debug;
    private readonly IDocumentService _documents;
    private readonly INavigationService _navigation;

    public BreakpointsViewModel(ICommandService commands, IBreakpointService breakpoints, IDebugService debug, IDocumentService documents,
        INavigationService navigation)
        : base(commands)
    {
        ArgumentNullException.ThrowIfNull(breakpoints);
        ArgumentNullException.ThrowIfNull(debug);
        _breakpoints = breakpoints;
        _debug = debug;
        _documents = documents;
        _navigation = navigation;
        breakpoints.Changed += (_, _) => Update();
        debug.ContextChanged += (_, _) => Update(); // UI-TW-803: the row the program is paused at
        Update();
    }

    public ToolWindowDefinition Definition => Define(ToolName, "Cmd_view_breakpoints", ToolGroupId.Bottom, this);

    public override IReadOnlyList<DebugGridColumn> Columns { get; } =
    [
        new("Enabled", "", 28), new("State", "", 24), new("Location", Strings.DebugView_Column_Location, 220, Sortable: true),
        new("Condition", Strings.DebugView_Column_Condition, 0, Sortable: true),
        new("PassCount", Strings.DebugView_Column_PassCount, 80, AlignRight: true, Sortable: true),
        new("Hits", Strings.DebugView_Column_Hits, 64, AlignRight: true, Sortable: true),
    ];

    public override bool MultiSelect => true;

    // tool-windows.md §10 (I-223: own icons of the toolbars.md §4 catalogue).
    public override IReadOnlyList<ToolWindowToolBarItem> ToolBarCommands { get; } =
    [
        new("run.addBreakpoint", "plus"), new("breakpoints.delete", "x"), new("run.deleteAllBreakpoints", "x-all"),
        new("breakpoints.enableAll", "check-all"), new("breakpoints.disableAll", "uncheck-all"),
    ];

    public override IReadOnlyList<string?> ContextCommands { get; } =
    [
        "breakpoints.goToSource", null, "run.toggleBreakpointEnabled", "run.breakpointProperties", null, "breakpoints.delete",
        "run.deleteAllBreakpoints", null, "breakpoints.enableAll", "breakpoints.disableAll", null, "run.addBreakpoint",
    ];

    /// <summary>The breakpoints of the selected rows, in row order.</summary>
    public IReadOnlyList<BreakpointModel> SelectedBreakpoints => [.. SelectedRows.Select(r => r.Tag).OfType<BreakpointModel>()];

    public void RegisterCommands()
    {
        Commands.RegisterHandler("breakpoints.delete", new Handler(() => SelectedBreakpoints.Count > 0, () =>
        {
            DeleteSelected();
            return Task.CompletedTask;
        }));
        Commands.RegisterHandler("breakpoints.enableAll", new Handler(() => _breakpoints.All.Any(b => !b.Enabled), () => SetAllEnabled(true)));
        Commands.RegisterHandler("breakpoints.disableAll", new Handler(() => _breakpoints.All.Any(b => b.Enabled), () => SetAllEnabled(false)));
        Commands.RegisterHandler("breakpoints.goToSource", new Handler(() => SelectedBreakpoints.Count > 0, () => GoToSourceAsync(SelectedBreakpoints[0])));
    }

    /// <summary>UI-TW-801: opens the file at the breakpoint's line, the caret on its first non-blank character; the editor gets focus.</summary>
    public async Task GoToSourceAsync(BreakpointModel breakpoint)
    {
        ArgumentNullException.ThrowIfNull(breakpoint);
        if ((await _documents.OpenAsync(breakpoint.Path)).Value is not TextDocumentViewModel document)
            return;
        _navigation.GoToLine(document, breakpoint.Line, null);
        ReturnFocusToDocument();
    }

    public override Task ActivateAsync(DebugGridRow row, string? column) =>
        row?.Tag is BreakpointModel breakpoint ? GoToSourceAsync(breakpoint) : Task.CompletedTask;

    /// <summary>The check box of the row, or Space: <c>run.toggleBreakpointEnabled</c> for the row.</summary>
    public override void Toggle(DebugGridRow row)
    {
        if (row?.Tag is BreakpointModel breakpoint)
            _breakpoints.Update(breakpoint, !breakpoint.Enabled, breakpoint.Condition, breakpoint.HitCount);
    }

    protected override void OnSortChanged() => Update();

    /// <summary>UI-TW-802: deletes the selected rows without confirmation; the next row (else the previous) is selected.</summary>
    private void DeleteSelected()
    {
        var selected = SelectedBreakpoints;
        var first = Rows.ToList().FindIndex(r => r.Tag is BreakpointModel b && selected.Contains(b));
        foreach (var breakpoint in selected)
            _breakpoints.Remove(breakpoint);
        if (Rows.Count > 0)
            Select(Rows[Math.Clamp(first, 0, Rows.Count - 1)].Key);
    }

    private Task SetAllEnabled(bool enabled)
    {
        foreach (var breakpoint in _breakpoints.All.Where(b => b.Enabled != enabled))
            _breakpoints.Update(breakpoint, enabled, breakpoint.Condition, breakpoint.HitCount);
        return Task.CompletedTask;
    }

    private void Update()
    {
        var all = _breakpoints.All;
        if (all.Count == 0)
            SetEmpty(Strings.Msg_Debug_BreakpointsEmpty, Strings.Msg_Debug_BreakpointsEmpty_Secondary);
        else
            SetEmpty(null);

        // UI-TW-804: <file name>:<line>; equal names in different folders get @<n> in creation order from the second on.
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hit = _debug.CurrentStop?.HitBreakpointIds ?? [];
        var rows = new List<(BreakpointModel Model, DebugGridRow Row)>();
        foreach (var breakpoint in all)
        {
            var location = Path.GetFileName(breakpoint.Path) + ":" + breakpoint.Line.ToString(CultureInfo.InvariantCulture);
            var count = seen[location] = seen.GetValueOrDefault(location) + 1;
            var key = count == 1 ? location : location + "@" + count.ToString(CultureInfo.InvariantCulture);
            var current = breakpoint.Id > 0 && hit.Contains(breakpoint.Id);
            var glyph = BreakpointService.GlyphOf(breakpoint);
            var conditionFailed = breakpoint is { Condition.Length: > 0, Message: not null } && breakpoint.State != BreakpointState.Invalid;
            DebugGridCell[] cells =
            [
                new("", Checked: breakpoint.Enabled),
                new("", Glyph: glyph.Kind, ToolTip: glyph.FormatToolTip(breakpoint.Line)),
                new(Path.GetFileName(breakpoint.Path) + ":" + breakpoint.Line.ToString(CultureInfo.CurrentCulture), ToolTip: breakpoint.Path),
                conditionFailed
                    ? new(breakpoint.Condition!, DebugCellStyle.Error, string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_ConditionInvalid, breakpoint.Message))
                    : new(breakpoint.Condition ?? "", DebugCellStyle.Code),
                new(PassCountText(breakpoint.HitCount)),
                new(breakpoint.Hits.ToString(CultureInfo.CurrentCulture)),
            ];
            rows.Add((breakpoint, new DebugGridRow(key, cells)
            {
                Tag = breakpoint,
                IsHighlighted = current,
                AutomationName = cells[2].Text + (current ? Strings.DebugRow_Current : ""),
            }));
        }

        SetRows([.. Sorted(rows).Select(r => r.Row)]);
    }

    private IEnumerable<(BreakpointModel Model, DebugGridRow Row)> Sorted(List<(BreakpointModel Model, DebugGridRow Row)> rows)
    {
        IOrderedEnumerable<(BreakpointModel Model, DebugGridRow Row)>? sorted = SortColumn switch
        {
            // File name, then line numerically.
            "Location" => rows.OrderBy(r => Path.GetFileName(r.Model.Path), StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Model.Line),
            "Condition" => rows.OrderBy(r => r.Model.Condition ?? "", StringComparer.CurrentCultureIgnoreCase),
            "PassCount" => rows.OrderBy(r => r.Model.HitCount?.Value ?? 0),
            "Hits" => rows.OrderBy(r => r.Model.Hits),
            _ => null,
        };
        return sorted is null ? rows : SortDescending ? sorted.Reverse() : sorted;
    }

    /// <summary>The debug-session interface §6.1: n hits to skip; the other modes read-only as <c>= v</c> / <c>every v</c>; empty = 0.</summary>
    public static string PassCountText(HitCountSpec? hitCount) => hitCount switch
    {
        null => "",
        { Mode: HitCountMode.Equal } => string.Format(CultureInfo.CurrentCulture, Strings.Breakpoint_PassCountEqual, hitCount.Value),
        { Mode: HitCountMode.Multiple } => string.Format(CultureInfo.CurrentCulture, Strings.Breakpoint_PassCountEvery, hitCount.Value),
        _ => BreakpointModel.PassCountOf(hitCount) is > 0 and var n ? n.ToString(CultureInfo.CurrentCulture) : "",
    };
}

/// <summary>
/// Call Stack (tool-windows.md §11): the frames of the current thread while paused, innermost first, in pages of 200
/// (<c>GetStackTraceAsync</c>, <c>StackTraceResult.TotalFrames</c>); the default action selects a frame.
/// </summary>
public sealed class CallStackViewModel : DebugGridViewModel
{
    public const string ToolName = "CallStack";

    /// <summary>The key of the row <c>Msg.Debug.MoreFrames</c>.</summary>
    public const string MoreKey = "$more";

    private readonly IDebugService _debug;
    private readonly IClipboardService _clipboard;
    private readonly List<StackFrameInfo> _frames = [];
    private int _total;
    private (int Epoch, int ThreadId)? _shown;
    private int _generation;

    public CallStackViewModel(ICommandService commands, IDebugService debug, IClipboardService clipboard)
        : base(commands)
    {
        ArgumentNullException.ThrowIfNull(debug);
        _debug = debug;
        _clipboard = clipboard;
        debug.ContextChanged += (_, _) => OnContextChanged();
        debug.SessionChanged += (_, _) => OnContextChanged();
        OnContextChanged();
    }

    public ToolWindowDefinition Definition => Define(ToolName, "Cmd_view_callStack", ToolGroupId.Left, this);

    public override IReadOnlyList<DebugGridColumn> Columns { get; } =
    [
        new("Marker", "", 24), new("Routine", Strings.DebugView_Column_Routine, 0), new("File", Strings.DebugView_Column_File, 160),
        new("Line", Strings.DebugView_Column_Line, 56, AlignRight: true),
    ];

    public override IReadOnlyList<string?> ContextCommands { get; } = ["callStack.goToFrame", "run.showExecutionPoint", null, "callStack.copy"];

    /// <summary>The frames loaded so far, innermost first.</summary>
    public IReadOnlyList<StackFrameInfo> Frames => _frames;

    public void RegisterCommands()
    {
        Commands.RegisterHandler("callStack.goToFrame", new Handler(
            () => SelectedRows is [{ } row] && (row.Key == MoreKey || row.Tag is StackFrameInfo { Location: not null }),
            () => SelectedRows is [{ } row] ? ActivateAsync(row, null) : Task.CompletedTask));
        Commands.RegisterHandler("callStack.copy", new Handler(() => _frames.Count > 0, () => _clipboard.SetTextAsync(CopyText())));
    }

    /// <summary>UI-TW-903: all frames, one per line: <c>&lt;Routine&gt; (&lt;file name&gt;:&lt;line&gt;)</c> or the address.</summary>
    public string CopyText() =>
        string.Join("\r\n", _frames.Select(f => f.Location is { } location ? $"{f.Name} ({FileAndLine(location)})" : f.Name));

    /// <summary>UI-TW-901 / UI-TW-902: a frame with source becomes the selected frame; the last row loads the next 200 frames.</summary>
    public override Task ActivateAsync(DebugGridRow row, string? column)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Key == MoreKey)
            return Pending = _debug.Track(LoadMoreAsync());
        return row.Tag is StackFrameInfo { Location: not null } frame ? _debug.GoToFrameAsync(frame) : Task.CompletedTask;
    }

    /// <summary>Ctrl+C (<c>edit.copy</c>) copies the call stack as <c>callStack.copy</c> does.</summary>
    public override bool CanExecute(string commandId, string? argument) => commandId == "edit.copy" && _frames.Count > 0;

    public override async Task<bool> ExecuteAsync(string commandId, string? argument)
    {
        if (!CanExecute(commandId, argument))
            return false;
        await _clipboard.SetTextAsync(CopyText());
        return true;
    }

    private void OnContextChanged()
    {
        SetEmptyUnlessPaused(_debug);
        if (_debug.Context is not { } context)
        {
            _generation++;
            _shown = null;
            _frames.Clear();
            SetRows([]);
            return;
        }

        if (_shown == (context.Epoch, context.ThreadId))
        {
            BuildRows(); // the selected frame changed: the marker moves
            return;
        }

        _shown = (context.Epoch, context.ThreadId);
        Pending = _debug.Track(LoadAsync());
    }

    /// <summary>UI-TW-902: on each stop the list is rebuilt, frame 0 is selected and scrolled into view.</summary>
    private async Task LoadAsync()
    {
        var generation = ++_generation;
        var result = await _debug.GetFramesAsync();
        if (generation != _generation)
            return;
        _frames.Clear();
        _frames.AddRange(result?.Frames ?? []);
        _total = result?.TotalFrames ?? 0;
        BuildRows();
        if (_frames.Count > 0)
            Select("0");
    }

    private async Task LoadMoreAsync()
    {
        var generation = _generation;
        var result = await _debug.GetMoreFramesAsync(_frames.Count);
        if (generation != _generation || result is null)
            return;
        _frames.AddRange(result.Frames);
        _total = Math.Max(result.TotalFrames, _frames.Count);
        BuildRows();
    }

    private void BuildRows()
    {
        var selected = _debug.SelectedFrame?.Index ?? 0;
        var rows = new List<DebugGridRow>(_frames.Count + 1);
        foreach (var frame in _frames)
        {
            var style = frame.Location is null ? DebugCellStyle.Tertiary : DebugCellStyle.Normal; // UI-TW-900: no source
            LineGlyphKind? marker = frame.Index == 0 ? LineGlyphKind.Execution : frame.Index == selected ? LineGlyphKind.Frame : null;
            rows.Add(new DebugGridRow(frame.Index.ToString(CultureInfo.InvariantCulture),
            [
                new("", Glyph: marker),
                new(frame.Name, style),
                frame.Location is { } location ? new(Path.GetFileName(location.Path), ToolTip: location.Path) : DebugGridCell.Empty,
                frame.Location is { } at ? new(at.Line.ToString(CultureInfo.CurrentCulture)) : DebugGridCell.Empty,
            ])
            {
                Tag = frame,
                AutomationName = frame.Name,
            });
        }

        if (_total > _frames.Count)
        {
            var text = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_MoreFrames, _total - _frames.Count);
            rows.Add(new DebugGridRow(MoreKey, [DebugGridCell.Empty, new(text, DebugCellStyle.Tertiary), DebugGridCell.Empty, DebugGridCell.Empty]) { AutomationName = text });
        }

        SetRows(rows);
    }
}

/// <summary>
/// Threads (tool-windows.md §14): the threads of the debuggee while a session exists (<c>GetThreadsAsync</c> at every
/// stop, <c>ThreadChanged</c> in between); the state is derived from the session state and the stop (the debug-session
/// interface §6.1); the default action switches the current thread.
/// </summary>
public sealed class ThreadsViewModel : DebugGridViewModel
{
    public const string ToolName = "Threads";

    private readonly IDebugService _debug;
    private readonly IClipboardService _clipboard;
    private readonly List<Entry> _threads = [];
    private int? _shownEpoch;
    private int _generation;

    public ThreadsViewModel(ICommandService commands, IDebugService debug, IClipboardService clipboard)
        : base(commands)
    {
        ArgumentNullException.ThrowIfNull(debug);
        _debug = debug;
        _clipboard = clipboard;
        debug.SessionChanged += (_, _) => OnSessionChanged();
        debug.ContextChanged += (_, _) => OnContextChanged();
        debug.ThreadChanged += (_, e) => OnThreadChanged(e);
        OnSessionChanged();
    }

    public ToolWindowDefinition Definition => Define(ToolName, "Cmd_view_threads", ToolGroupId.Left, this);

    public override IReadOnlyList<DebugGridColumn> Columns { get; } =
    [
        new("Marker", "", 24), new("Id", Strings.DebugView_Column_Id, 64, AlignRight: true, Sortable: true),
        new("Name", Strings.DebugView_Column_Name, 160, Sortable: true), new("State", Strings.DebugView_Column_State, 80, Sortable: true),
        new("Location", Strings.DebugView_Column_Location, 0),
    ];

    public override IReadOnlyList<string?> ContextCommands { get; } = ["threads.switchTo", null, "edit.copy"];

    public void RegisterCommands() =>
        Commands.RegisterHandler("threads.switchTo", new Handler(
            () => _debug.IsPaused && SelectedRows is [{ Tag: Entry }],
            () => SelectedRows is [{ } row] ? ActivateAsync(row, null) : Task.CompletedTask));

    /// <summary>UI-TW-1201: makes the thread the current thread; only while paused.</summary>
    public override Task ActivateAsync(DebugGridRow row, string? column) =>
        row?.Tag is Entry thread && _debug.IsPaused ? _debug.Track(_debug.SwitchThreadAsync(thread.Id)) : Task.CompletedTask;

    /// <summary>Ctrl+C (<c>edit.copy</c>): the selected row as tab-separated text.</summary>
    public override bool CanExecute(string commandId, string? argument) => commandId == "edit.copy" && SelectedRows.Count > 0;

    public override async Task<bool> ExecuteAsync(string commandId, string? argument)
    {
        if (!CanExecute(commandId, argument))
            return false;
        await _clipboard.SetTextAsync(string.Join("\r\n", SelectedRows.Select(r => string.Join("\t", r.Cells.Skip(1).Select(c => c.Text)))));
        return true;
    }

    protected override void OnSortChanged() => BuildRows();

    private void OnSessionChanged()
    {
        if (_debug.Session is null)
        {
            _generation++;
            _threads.Clear();
            _shownEpoch = null;
            SetEmpty(Strings.Msg_Debug_NoSession, Strings.Msg_Debug_NoSession_Secondary);
        }
        else
        {
            SetEmpty(null);
        }

        BuildRows();
    }

    private void OnContextChanged()
    {
        if (_debug.Context is not { } context)
        {
            _shownEpoch = null;
            BuildRows(); // running: no locations
            return;
        }

        if (_shownEpoch == context.Epoch)
        {
            BuildRows(); // another current thread: the marker moves
            return;
        }

        _shownEpoch = context.Epoch;
        Pending = _debug.Track(LoadAsync());
    }

    /// <summary>UI-TW-1200: threads that start or exit while the program runs join or leave the list.</summary>
    private void OnThreadChanged(ThreadChangedEventArgs e)
    {
        if (e.Change == ThreadChange.Exited)
            _threads.RemoveAll(t => t.Id == e.ThreadId);
        else if (_threads.All(t => t.Id != e.ThreadId))
            _threads.Add(new Entry(e.ThreadId, string.Format(CultureInfo.CurrentCulture, Strings.DebugThread_Unnamed, e.ThreadId), ""));
        BuildRows();
    }

    private async Task LoadAsync()
    {
        var generation = ++_generation;
        if (_debug.Session is not { } session)
            return;
        var loaded = new List<Entry>();
        try
        {
            foreach (var thread in await session.GetThreadsAsync())
            {
                // The debug-session interface §6.1: the top frame with source as <file name>:<line>, else that frame's name.
                var frames = thread.Id == _debug.CurrentThreadId ? await _debug.GetFramesAsync() : await session.GetStackTraceAsync(thread.Id, 0, 1);
                if (generation != _generation)
                    return;
                var top = frames is { Frames.Count: > 0 } ? frames.Frames[0] : null;
                var name = thread.Name.Length > 0 ? thread.Name : string.Format(CultureInfo.CurrentCulture, Strings.DebugThread_Unnamed, thread.Id);
                loaded.Add(new Entry(thread.Id, name, top is null ? "" : top.Location is { } location ? FileAndLine(location) : top.Name));
            }
        }
        catch (Exception e) when (e is DebuggerException or ObjectDisposedException or OperationCanceledException)
        {
            return; // the program runs again, or the session ends: the list stays as it was
        }

        if (generation != _generation)
            return;
        _threads.Clear();
        _threads.AddRange(loaded);
        BuildRows();
    }

    private void BuildRows()
    {
        var stop = _debug.CurrentStop;
        var current = _debug.CurrentThreadId;
        string State(Entry thread) => stop is null ? Strings.DebugThread_Running
            : thread.Id != stop.ThreadId ? Strings.DebugThread_Paused
            : stop.Reason is StopReason.Breakpoint or StopReason.RunToLine ? Strings.DebugThread_StoppedAtBreakpoint
            : stop.Reason == StopReason.Step ? Strings.DebugThread_Stepping
            : Strings.DebugThread_Paused;

        IEnumerable<Entry> ordered = SortColumn switch
        {
            "Id" => _threads.OrderBy(t => t.Id),
            "Name" => _threads.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase),
            "State" => _threads.OrderBy(State, StringComparer.CurrentCultureIgnoreCase),
            _ => _threads,
        };
        if (SortColumn is not null && SortDescending)
            ordered = ordered.Reverse();
        SetRows([.. ordered.Select(thread => new DebugGridRow(thread.Id.ToString(CultureInfo.InvariantCulture),
        [
            new("", Glyph: thread.Id == current ? LineGlyphKind.Execution : null),
            new(thread.Id.ToString(CultureInfo.CurrentCulture)),
            new(thread.Name),
            new(State(thread)),
            new(stop is null ? "" : thread.Location),
        ])
        {
            Tag = thread,
            AutomationName = thread.Name,
        })]);
    }

    private sealed record Entry(int Id, string Name, string Location);
}

/// <summary>
/// Event Log (tool-windows.md §15): the rows the debug service made of the session's events (kinds per the
/// debug-session interface §6.1), newest last, with a filter over kind and message; <c>Msg.Debug.EventLogEmpty</c> while
/// it has no rows.
/// </summary>
public sealed class EventLogViewModel : DebugGridViewModel
{
    public const string ToolName = "EventLog";

    private readonly IDebugService _debug;
    private readonly IClipboardService _clipboard;
    private readonly IDocumentService _documents;
    private readonly INavigationService _navigation;
    private string _filter = "";

    public EventLogViewModel(ICommandService commands, IDebugService debug, IClipboardService clipboard, IDocumentService documents, INavigationService navigation)
        : base(commands)
    {
        ArgumentNullException.ThrowIfNull(debug);
        _debug = debug;
        _clipboard = clipboard;
        _documents = documents;
        _navigation = navigation;
        debug.LogChanged += (_, entry) =>
        {
            if (entry is null)
            {
                Update();
                return;
            }

            Append(entry);
            RequestScrollToEnd(); // UI-TW-1300: auto-scrolling as Output
        };
        Update();
    }

    public ToolWindowDefinition Definition => Define(ToolName, "Cmd_view_eventLog", ToolGroupId.Bottom, this);

    public override IReadOnlyList<DebugGridColumn> Columns { get; } =
    [
        new("Time", Strings.DebugView_Column_Time, 96), new("Kind", Strings.DebugView_Column_Kind, 88), new("Message", Strings.DebugView_Column_Message, 0),
    ];

    public override bool MultiSelect => true;

    public override bool HasFilter => true;

    public override IReadOnlyList<ToolWindowToolBarItem> ToolBarCommands { get; } = [new("eventLog.clear", "clear")];

    public override IReadOnlyList<string?> ContextCommands { get; } = ["edit.copy", "edit.selectAll", null, "eventLog.clear"];

    public void RegisterCommands() =>
        Commands.RegisterHandler("eventLog.clear", new Handler(() => _debug.Log.Count > 0, () =>
        {
            _debug.ClearLog();
            return Task.CompletedTask;
        }));

    /// <summary>The name of a kind as its column shows it.</summary>
    public static string KindName(DebugLogKind kind) => kind switch
    {
        DebugLogKind.Process => Strings.DebugLog_Kind_Process,
        DebugLogKind.Module => Strings.DebugLog_Kind_Module,
        DebugLogKind.Thread => Strings.DebugLog_Kind_Thread,
        DebugLogKind.Breakpoint => Strings.DebugLog_Kind_Breakpoint,
        DebugLogKind.Exception => Strings.DebugLog_Kind_Exception,
        DebugLogKind.Output => Strings.DebugLog_Kind_Output,
        _ => Strings.DebugLog_Kind_Debugger,
    };

    /// <summary>UI-TW-1302: a Breakpoint or Exception row with a source location navigates to it; other rows do nothing.</summary>
    public override async Task ActivateAsync(DebugGridRow row, string? column)
    {
        if (row?.Tag is not DebugLogEntry { Kind: DebugLogKind.Breakpoint or DebugLogKind.Exception, Location: { } location })
            return;
        if ((await _documents.OpenAsync(location.Path)).Value is not TextDocumentViewModel document)
            return;
        _navigation.GoToLine(document, location.Line, Math.Max(1, location.Column));
        ReturnFocusToDocument();
    }

    /// <summary>UI-TW-1300: <c>edit.copy</c> copies the selected rows as <c>&lt;Time&gt;\t&lt;Kind&gt;\t&lt;Message&gt;</c>; <c>edit.selectAll</c> selects all.</summary>
    public override bool CanExecute(string commandId, string? argument) =>
        (commandId == "edit.copy" && SelectedRows.Count > 0) || (commandId == "edit.selectAll" && Rows.Count > 0);

    public override async Task<bool> ExecuteAsync(string commandId, string? argument)
    {
        if (!CanExecute(commandId, argument))
            return false;
        if (commandId == "edit.selectAll")
            Select([.. Rows.Select(r => r.Key)]);
        else
            await _clipboard.SetTextAsync(string.Join("\r\n", SelectedRows.Select(r => string.Join("\t", r.Cells.Select(c => c.Text)))));
        return true;
    }

    /// <summary>UI-TW-1301: the filter matches kind and message.</summary>
    protected override void OnFilterApplied(string filter)
    {
        _filter = filter.Trim();
        Update();
    }

    private bool Matches(DebugLogEntry entry) =>
        _filter.Length == 0 || KindName(entry.Kind).Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
        || entry.Message.Contains(_filter, StringComparison.CurrentCultureIgnoreCase);

    private static DebugGridRow RowOf(DebugLogEntry entry)
    {
        var style = entry.Kind switch
        {
            DebugLogKind.Exception => DebugCellStyle.Error,
            DebugLogKind.Debugger => DebugCellStyle.Warning,
            _ => DebugCellStyle.Normal,
        };
        // UI-TW-1303: the zero-based index since the last clear.
        return new DebugGridRow(entry.Index.ToString(CultureInfo.InvariantCulture),
        [
            new(entry.Time.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture), DebugCellStyle.Tertiary),
            new(KindName(entry.Kind), style),
            new(entry.Message, style),
        ])
        {
            Tag = entry,
            AutomationName = entry.Message,
        };
    }

    /// <summary>One more row: the rows shown are kept, the new one joins them, rows the log dropped go.</summary>
    private void Append(DebugLogEntry entry)
    {
        SetEmpty(null);
        var oldest = _debug.Log.Count > 0 ? _debug.Log[0].Index : entry.Index;
        var dropped = 0;
        while (dropped < _shown.Count && ((DebugLogEntry)_shown[dropped].Tag!).Index < oldest)
            dropped++;
        _shown.RemoveRange(0, dropped);
        if (Matches(entry))
            _shown.Add(RowOf(entry));
        else if (dropped == 0)
            return;
        SetRows([.. _shown]);
    }

    private void Update()
    {
        var log = _debug.Log;
        if (log.Count == 0)
            SetEmpty(Strings.Msg_Debug_EventLogEmpty);
        else
            SetEmpty(null);
        _shown.Clear();
        _shown.AddRange(log.Where(Matches).Select(RowOf));
        SetRows([.. _shown]);
    }

    private readonly List<DebugGridRow> _shown = [];
}
