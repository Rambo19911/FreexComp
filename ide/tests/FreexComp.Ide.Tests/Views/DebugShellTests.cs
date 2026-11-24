using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The debug shell (I-133): the six debug tool windows with their empty states, the <c>Debug</c> layout profile and its
/// automatic activation, the Debug toolbar and the Run menu states — with debug sessions over the scripted adapter.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class DebugShellTests
{
    private static readonly string[] DebugTools = ["Breakpoints", "CallStack", "Locals", "Watches", "Threads", "EventLog"];

    /// <summary>
    /// The lines of the empty-state block <c>Tool.&lt;name&gt;.Empty</c> of a shown tool window, joined with " / "; null when
    /// the block is not shown.
    /// </summary>
    private static string? EmptyState(HeadlessHarness h, string name)
    {
        Assert.True(h.Services.ToolWindows.Show(name, focus: false));
        Dispatcher.UIThread.RunJobs();
        var block = h.Find<Control>(AutomationIds.Tool(name) + ".Empty");
        return block.IsEffectivelyVisible
            ? string.Join(" / ", block.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text ?? ""))
            : null;
    }

    /// <summary>UI-WIN-012 (b): a debug session activates the Debug profile for its lifetime; then rule (a) applies again.</summary>
    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-WIN-012")]
    [Trait("Req", "UI-WIN-013")]
    public async Task DebugProfileActivatesOnSessionStart()
    {
        await using var h = await DebugScenario.StartAsync();
        h.DebugSessions.UseScenario("{}");
        var layout = h.Services.Layout.Layout;
        await DebugScenario.OpenAsync(h, DebugScenario.UnitPath(h));
        Assert.Equal(LayoutProfiles.Edit, layout.ActiveProfile);
        Assert.Null(h.TryFind<Control>(AutomationIds.ToolTab("CallStack")));

        // F5 starts the session: the Debug profile with the arrangement of main-window.md §2.
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        await h.WaitForAsync(() => h.Services.Debug!.Session?.State == FreexComp.Debugger.DebugState.Running, "the program runs");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(LayoutProfiles.Debug, layout.ActiveProfile);
        Assert.Equal(["CallStack", "Threads", "ProjectManager", "Structure"], layout.Left.Tools);
        Assert.Equal(["Locals", "Watches"], layout.RightUpper.Tools);
        Assert.Empty(layout.RightLower.Tools);
        Assert.Equal(["Breakpoints", "EventLog", "Output", "Messages", "SearchResults"], layout.Bottom.Tools);
        Assert.Equal(("CallStack", "Locals", "Breakpoints"), (layout.Left.ActiveTool, layout.RightUpper.ActiveTool, layout.Bottom.ActiveTool));
        Assert.All(DebugTools, name => Assert.True(h.Find<Control>(AutomationIds.ToolTab(name)).IsEffectivelyVisible, name));
        Assert.True(h.Services.Commands.Get("window.layout.debug").IsChecked);
        Assert.Equal("Editor.Unit1.pas", h.FocusedAutomationId); // UI-WIN-014: focus stays in the document

        // For the lifetime of the session the profile stays, whatever document becomes active.
        await DebugScenario.OpenAsync(h, DebugScenario.MainPath(h));
        Assert.Equal(LayoutProfiles.Debug, layout.ActiveProfile);

        // The session ends: the profile that rule (a) selects for the active document.
        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(LayoutProfiles.Edit, layout.ActiveProfile);
        Assert.Null(h.TryFind<Control>(AutomationIds.ToolTab("CallStack")));
        Assert.False(h.Services.Commands.Get("window.layout.debug").IsChecked);

        // layout.autoSwitch = false: only the commands switch.
        h.Services.Settings.Set(SettingsCatalog.LayoutAutoSwitch, false);
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        Assert.Equal(LayoutProfiles.Edit, layout.ActiveProfile);
        await h.ExecuteAsync("window.layout.debug");
        Assert.Equal(LayoutProfiles.Debug, layout.ActiveProfile);

        // UI-WIN-013: the Debug profile is stored with the others.
        h.Clock.Advance(TimeSpan.FromSeconds(2));
        var saved = File.ReadAllText(Path.Combine(h.UserDir, "roaming", "uistate.json"));
        Assert.Contains("\"activeProfile\": \"Debug\"", saved, StringComparison.Ordinal);
        Assert.Contains("\"Debug\": {", saved, StringComparison.Ordinal);
        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Assert.Equal(LayoutProfiles.Debug, layout.ActiveProfile);
        await h.ExecuteAsync("window.layout.edit");
        Assert.Equal(LayoutProfiles.Edit, layout.ActiveProfile);
    }

    /// <summary>UI-TW-013 with <c>Msg.Debug.NoSession</c> / <c>NotPaused</c> / <c>BreakpointsEmpty</c> / <c>WatchesEmpty</c> / <c>EventLogEmpty</c>.</summary>
    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-TW-013")]
    [Trait("Req", "UI-TW-006")]
    public async Task ToolWindowsShowEmptyStatesWithoutSession()
    {
        await using var h = await DebugScenario.StartAsync();
        h.DebugSessions.UseScenario($$"""
            { "steps": [ {{DebugScenarios.StopAtStart(DebugScenario.UnitPath(h), 6)}}, {{DebugScenarios.Continue()}},
                         { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 6 } ] } } ] }
            """);

        // Registered from Phase 6: every view.* command of the debug windows is available and shows its window.
        Assert.All(DebugTools, name => Assert.True(h.Services.ToolWindows.IsRegistered(name), name));
        Assert.True(h.Services.Commands.CanExecute("view.breakpoints"));
        var noSession = Strings.Msg_Debug_NoSession + " / " + Strings.Msg_Debug_NoSession_Secondary;
        Assert.Equal(Strings.Msg_Debug_BreakpointsEmpty + " / " + Strings.Msg_Debug_BreakpointsEmpty_Secondary, EmptyState(h, "Breakpoints"));
        Assert.Equal(noSession, EmptyState(h, "CallStack"));
        Assert.Equal(noSession, EmptyState(h, "Locals"));
        Assert.Equal(Strings.Msg_Debug_WatchesEmpty + " / " + Strings.Msg_Debug_WatchesEmpty_Secondary, EmptyState(h, "Watches"));
        Assert.Equal(noSession, EmptyState(h, "Threads"));
        Assert.Equal(Strings.Msg_Debug_EventLogEmpty, EmptyState(h, "EventLog"));
        Assert.True(h.Services.ToolWindows.Show("Breakpoints", focus: false));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("BREAKPOINTS", h.Find<TextBlock>("Tool.Breakpoints.Caption").Text);

        // Paused: Call Stack and Locals have their content's place; Threads lists while a session exists.
        await h.ExecuteAsync("run.start");
        await DebugScenario.WaitForStateAsync(h, IdeState.Paused);
        Assert.Null(EmptyState(h, "CallStack"));
        Assert.Null(EmptyState(h, "Locals"));
        Assert.Null(EmptyState(h, "Threads"));

        // Running: available only while the program is paused.
        await h.ExecuteAsync("run.start"); // Continue
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        Assert.Equal(Strings.Msg_Debug_NotPaused, EmptyState(h, "CallStack"));
        Assert.Equal(Strings.Msg_Debug_NotPaused, EmptyState(h, "Locals"));
        Assert.Null(EmptyState(h, "Threads"));

        // A breakpoint and a watch take the lists' empty states away; they do not depend on the session.
        h.Services.Breakpoints!.Toggle(DebugScenario.UnitPath(h), 6);
        h.Services.Debug!.Watches.Add("Foo1");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(EmptyState(h, "Breakpoints"));
        Assert.Null(EmptyState(h, "Watches"));

        await h.ExecuteAsync("run.stop");
        Assert.Equal(IdeState.Idle, h.Services.State.Current.State);
        Assert.Equal(noSession, EmptyState(h, "CallStack"));
        Assert.Equal(noSession, EmptyState(h, "Threads"));
        Assert.Null(EmptyState(h, "Breakpoints"));
    }

    /// <summary>
    /// I-224: the empty states of UI-TW-013 follow the debug state itself, not the answers the views wait for. The
    /// adapter holds the views' call stack request here (it answers the session's and then waits for the gate), so
    /// Locals has nothing to show yet — and still must not say "Available while the program is paused" about a paused
    /// program.
    /// </summary>
    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-TW-013")]
    public async Task PausedViewsLeaveTheEmptyStateBeforeTheirDataArrives()
    {
        await using var h = await DebugScenario.StartAsync();
        var unit = DebugScenario.UnitPath(h);
        var gateName = "FreexComp.Tests.I224." + Guid.NewGuid().ToString("N");
        using var gate = new EventWaitHandle(false, EventResetMode.ManualReset, gateName);
        h.DebugSessions.UseScenario($$"""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent()}} ] },
                         {{DebugScenarios.Threads}},
                         { "on": "stackTrace", "thenWaitForEvent": "{{gateName}}", "respond": { "totalFrames": 1, "stackFrames": [
                             { "id": 100, "name": "Run", "line": 6, "column": 1, "source": { "name": "Unit1.pas", "path": {{JsonSerializer.Serialize(unit)}} } } ] } },
                         {{DebugScenarios.Stack(unit, 6)}}, {{DebugScenarios.NoScopes}} ] }
            """);

        await h.ExecuteAsync("run.start");
        await DebugScenario.WaitForStateAsync(h, IdeState.Paused);
        await h.WaitForAsync(() => h.DebugSessions.SentRequests("stackTrace").Count >= 2, "the views' call stack request");
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Services.Debug!.HasViewWork); // held by the adapter: the views have no data yet
        Assert.Null(EmptyState(h, "CallStack"));
        Assert.Null(EmptyState(h, "Locals"));
        Assert.Null(EmptyState(h, "Threads"));

        // The answers arrive: the rows follow, the empty states stay away.
        gate.Set();
        await DebugScenario.SettleAsync(h);
        Assert.Null(EmptyState(h, "CallStack"));
        Assert.Equal("Run", DebugScenario.Cell(h, "CallStack", "0", "Routine"));
        Assert.Null(EmptyState(h, "Locals"));

        await h.ExecuteAsync("run.stop");
        Assert.Equal(IdeState.Idle, h.Services.State.Current.State);
        Assert.Equal(Strings.Msg_Debug_NoSession + " / " + Strings.Msg_Debug_NoSession_Secondary, EmptyState(h, "Locals"));
    }

    /// <summary>menus.md §7 conditions and UI-TB-004 / UI-TB-005 / UI-TB-010 for the Debug toolbar, per IDE state.</summary>
    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-TB-004")]
    [Trait("Req", "UI-TB-005")]
    [Trait("Req", "UI-TB-010")]
    public async Task DebugToolbarAndMenuStatesPerIdeState()
    {
        await using var h = await DebugScenario.StartAsync();
        h.DebugSessions.UseScenario($$"""
            { "steps": [ {{DebugScenarios.StopAtStart(DebugScenario.UnitPath(h), 6)}}, {{DebugScenarios.Continue()}} ] }
            """);
        var commands = h.Services.Commands;
        bool Can(string id) => commands.CanExecute(id);
        bool ToolbarShown() => h.TryFind<Control>("ToolBar.Debug") is { IsEffectivelyVisible: true };
        bool ButtonEnabled(string id) => h.Find<Button>(AutomationIds.ToolBarButton(id)).IsEffectivelyEnabled;
        var runItem = h.Window.GetLogicalDescendants().OfType<MenuItem>().Single(m => AutomationIds.Get(m) == AutomationIds.Menu("run.start"));
        string[] debugButtons = ["run.pause", "run.stepOver", "run.stepInto", "run.stepOut", "run.runToCursor", "run.showExecutionPoint", "run.evaluate", "run.toggleBreakpoint"];

        // Idle: no Debug toolbar; Run and the steps that start the program are available.
        await DebugScenario.OpenAsync(h, DebugScenario.UnitPath(h));
        Assert.False(ToolbarShown());
        Assert.Equal((true, true, false, false), (Can("run.start"), Can("run.startWithoutDebugging"), Can("run.stop"), Can("run.restart")));
        Assert.Equal((false, true, true, false), (Can("run.pause"), Can("run.stepOver"), Can("run.stepInto"), Can("run.stepOut")));
        Assert.Equal((false, false, true), (Can("run.showExecutionPoint"), Can("run.evaluate"), Can("run.toggleBreakpoint")));
        Assert.Equal("_Run", runItem.Header);

        // UI-TB-004: the Debug profile alone shows the toolbar, with the buttons of toolbars.md §2.3 in order.
        await h.ExecuteAsync("window.layout.debug");
        Assert.True(ToolbarShown());
        Assert.Equal(debugButtons.Select(id => AutomationIds.ToolBarButton(id)),
            h.Find<Control>("ToolBar.Debug").GetVisualDescendants().OfType<Button>().Select(b => AutomationIds.Get(b)));
        Assert.False(ButtonEnabled("run.pause"));
        Assert.True(ButtonEnabled("run.stepOver"));
        await h.ExecuteAsync("window.layout.edit");
        Assert.False(ToolbarShown());

        // Paused: Continue, the steps, Show Execution Point and Evaluate; no Pause, no build.
        h.Services.Settings.Set(SettingsCatalog.LayoutAutoSwitch, false); // the session, not the profile, shows the toolbar
        await h.ExecuteAsync("run.start");
        await DebugScenario.WaitForStateAsync(h, IdeState.Paused);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ToolbarShown());
        Assert.Equal((true, false, true, true), (Can("run.start"), Can("run.startWithoutDebugging"), Can("run.stop"), Can("run.restart")));
        Assert.Equal((false, true, true, true), (Can("run.pause"), Can("run.stepOver"), Can("run.stepInto"), Can("run.stepOut")));
        Assert.Equal((true, true, true, false), (Can("run.showExecutionPoint"), Can("run.evaluate"), Can("run.runToCursor"), Can("project.build")));
        Assert.Equal("_Continue", runItem.Header);
        var runButton = h.Find<Button>(AutomationIds.ToolBarButton("run.start"));
        Assert.Equal("Continue (F5)", ToolTip.GetTip(runButton));
        var runBounds = runButton.Bounds;
        Assert.Equal((false, true, true, true), (ButtonEnabled("run.pause"), ButtonEnabled("run.stepOver"), ButtonEnabled("run.stepOut"), ButtonEnabled("run.evaluate")));

        // Debugging, program running: Pause and Stop only.
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ToolbarShown());
        Assert.Equal((false, false, true, true), (Can("run.start"), Can("run.startWithoutDebugging"), Can("run.stop"), Can("run.restart")));
        Assert.Equal((true, false, false, false), (Can("run.pause"), Can("run.stepOver"), Can("run.stepInto"), Can("run.stepOut")));
        Assert.Equal((false, false, false), (Can("run.showExecutionPoint"), Can("run.evaluate"), Can("run.runToCursor")));
        Assert.Equal("_Run", runItem.Header);
        Assert.Equal((true, false, false), (ButtonEnabled("run.pause"), ButtonEnabled("run.stepOver"), ButtonEnabled("run.evaluate")));
        Assert.Equal(runBounds, runButton.Bounds); // UI-TB-010: the tooltip changes, not the size or position

        // A blocked shortcut says so (UI-CMD-012).
        h.Press("F10");
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Status_CommandUnavailable, "Step Over"), h.Status.Message);

        // UI-TB-004: the toolbar's own setting hides it even during a session.
        await h.ExecuteAsync("view.toolbar.debug");
        Assert.False(ToolbarShown());
        await h.ExecuteAsync("view.toolbar.debug");
        Assert.True(ToolbarShown());

        // Idle again: the toolbar goes.
        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Dispatcher.UIThread.RunJobs();
        Assert.False(ToolbarShown());
        Assert.Equal((true, false, false), (Can("run.start"), Can("run.stop"), Can("run.pause")));
    }
}
