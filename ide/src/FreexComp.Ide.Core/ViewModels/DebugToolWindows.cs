using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Models;
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
}

/// <summary>Breakpoints (tool-windows.md §10): bottom dock; <c>Msg.Debug.BreakpointsEmpty</c> while there is none.</summary>
public sealed class BreakpointsViewModel : DebugToolWindowViewModel
{
    public const string ToolName = "Breakpoints";

    private readonly IBreakpointService _breakpoints;

    public BreakpointsViewModel(IBreakpointService breakpoints)
    {
        ArgumentNullException.ThrowIfNull(breakpoints);
        _breakpoints = breakpoints;
        breakpoints.Changed += (_, _) => Update();
        Update();
    }

    public ToolWindowDefinition Definition => Define(ToolName, "Cmd_view_breakpoints", ToolGroupId.Bottom, this);

    private void Update()
    {
        if (_breakpoints.All.Count == 0)
            SetEmpty(Strings.Msg_Debug_BreakpointsEmpty, Strings.Msg_Debug_BreakpointsEmpty_Secondary);
        else
            SetEmpty(null);
    }
}

/// <summary>Call Stack (tool-windows.md §11): left dock; only while paused.</summary>
public sealed class CallStackViewModel : DebugToolWindowViewModel
{
    public const string ToolName = "CallStack";

    public CallStackViewModel(IDebugService debug)
    {
        ArgumentNullException.ThrowIfNull(debug);
        debug.StateChanged += (_, _) => SetEmptyUnlessPaused(debug);
        debug.SessionChanged += (_, _) => SetEmptyUnlessPaused(debug);
        SetEmptyUnlessPaused(debug);
    }

    public ToolWindowDefinition Definition => Define(ToolName, "Cmd_view_callStack", ToolGroupId.Left, this);
}

/// <summary>Locals (tool-windows.md §12): upper right dock; only while paused.</summary>
public sealed class LocalsViewModel : DebugToolWindowViewModel
{
    public const string ToolName = "Locals";

    public LocalsViewModel(IDebugService debug)
    {
        ArgumentNullException.ThrowIfNull(debug);
        debug.StateChanged += (_, _) => SetEmptyUnlessPaused(debug);
        debug.SessionChanged += (_, _) => SetEmptyUnlessPaused(debug);
        SetEmptyUnlessPaused(debug);
    }

    public ToolWindowDefinition Definition => Define(ToolName, "Cmd_view_locals", ToolGroupId.RightUpper, this);
}

/// <summary>Watches (tool-windows.md §13): upper right dock; <c>Msg.Debug.WatchesEmpty</c> while the list is empty.</summary>
public sealed class WatchesViewModel : DebugToolWindowViewModel
{
    public const string ToolName = "Watches";

    private readonly WatchList _watches;

    public WatchesViewModel(IDebugService debug)
    {
        ArgumentNullException.ThrowIfNull(debug);
        _watches = debug.Watches;
        _watches.Changed += (_, _) => Update();
        Update();
    }

    public ToolWindowDefinition Definition => Define(ToolName, "Cmd_view_watches", ToolGroupId.RightUpper, this);

    private void Update()
    {
        if (_watches.Expressions.Count == 0)
            SetEmpty(Strings.Msg_Debug_WatchesEmpty, Strings.Msg_Debug_WatchesEmpty_Secondary);
        else
            SetEmpty(null);
    }
}

/// <summary>Threads (tool-windows.md §14): left dock; while a session exists.</summary>
public sealed class ThreadsViewModel : DebugToolWindowViewModel
{
    public const string ToolName = "Threads";

    private readonly IDebugService _debug;

    public ThreadsViewModel(IDebugService debug)
    {
        ArgumentNullException.ThrowIfNull(debug);
        _debug = debug;
        debug.SessionChanged += (_, _) => Update();
        Update();
    }

    public ToolWindowDefinition Definition => Define(ToolName, "Cmd_view_threads", ToolGroupId.Left, this);

    private void Update()
    {
        if (_debug.Session is null)
            SetEmpty(Strings.Msg_Debug_NoSession, Strings.Msg_Debug_NoSession_Secondary);
        else
            SetEmpty(null);
    }
}

/// <summary>Event Log (tool-windows.md §15): bottom dock; <c>Msg.Debug.EventLogEmpty</c> while it has no rows.</summary>
public sealed class EventLogViewModel : DebugToolWindowViewModel
{
    public const string ToolName = "EventLog";

    public EventLogViewModel() => SetEmpty(Strings.Msg_Debug_EventLogEmpty);

    public ToolWindowDefinition Definition => Define(ToolName, "Cmd_view_eventLog", ToolGroupId.Bottom, this);
}
