using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// I-307 (UI-TW-007): showing a debug tool window with focus puts keyboard focus on its list — the primary control —
/// whenever it has rows, also after it was shown empty (the empty state's panel took the focus then).
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class ToolWindowFocusTests
{
    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-TW-007")]
    public async Task ShowFocusesTheDebugGridList()
    {
        await using var h = await DebugScenario.StartDemoAsync();
        var demo = DebugScenario.DemoPath(h);
        var shell = h.Services.DebugShell!;
        var (_, editor, _) = await DebugScenario.OpenAsync(h, demo);
        void Check(string name, DebugGridViewModel grid)
        {
            editor.TextArea.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.TextArea.IsKeyboardFocusWithin);
            h.Services.Layout.Layout.BottomSize = 300;
            Assert.True(h.Services.ToolWindows.Show(name, focus: true));
            Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(grid.Rows);
            Assert.True(h.Find<ListBox>($"Tool.{name}.List").IsKeyboardFocusWithin, $"{name}: focus on {h.FocusedAutomationId} ({h.Window.FocusManager?.GetFocusedElement()?.GetType().Name})");
            Assert.Equal(name, h.Services.FocusTracker.Current.ToolWindowName);
        }

        void ShowEmpty(string name)
        {
            Assert.True(h.Services.ToolWindows.Show(name, focus: true));
            Dispatcher.UIThread.RunJobs();
            Assert.True(h.Find<Control>($"Tool.{name}.Empty").IsEffectivelyVisible);
        }

        // Every window shown once while empty (the panel takes the focus), Watches with its placeholder row only.
        ShowEmpty(BreakpointsViewModel.ToolName);
        ShowEmpty(LocalsViewModel.ToolName);
        ShowEmpty(EventLogViewModel.ToolName);
        Check(WatchesViewModel.ToolName, shell.Watches);

        // Breakpoints without a session (removed again before the session starts).
        h.Services.Breakpoints!.Toggle(demo, DebugDemo.StopLine);
        Dispatcher.UIThread.RunJobs();
        Check(BreakpointsViewModel.ToolName, shell.Breakpoints);
        h.Services.Breakpoints.Toggle(demo, DebugDemo.StopLine);
        Dispatcher.UIThread.RunJobs();

        // Paused: the variables of the frame and the session's events.
        h.DebugSessions.UseScenario($$"""
            { {{DebugDemo.Data()}}, "steps": [
              {{DebugDemo.FirstStep(demo)}},
              {{DebugDemo.AtStop(demo)}},
              {{DebugScenarios.Continue(DebugScenarios.Exit())}} ] }
            """);
        h.Press("F11");
        await DebugScenario.WaitForStopAsync(h, DebugDemo.StopLine);
        Check(LocalsViewModel.ToolName, shell.Locals);
        Check(WatchesViewModel.ToolName, shell.Watches);
        Check(EventLogViewModel.ToolName, shell.EventLog);
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        await DebugScenario.WaitForScenarioCompleteAsync(h);
    }
}
