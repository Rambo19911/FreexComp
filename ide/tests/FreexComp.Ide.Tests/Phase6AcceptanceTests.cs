using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests;

/// <summary>
/// Phase 6 IDE acceptance (I-146, testing.md §5.4): start under the debugger → stop at a breakpoint → inspect the locals
/// → step over → continue → the program exits — through the real main window, with real debug sessions over the
/// scripted adapter running the scenario file <c>TestData/debug-scenarios/phase6-acceptance.json</c>.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class Phase6AcceptanceTests
{
    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-P3-001")]
    [Trait("Req", "UI-WIN-012")]
    public async Task BreakStepInspectContinue()
    {
        await using var h = await DebugScenario.StartDemoAsync();
        var demo = DebugScenario.DemoPath(h);
        var debug = h.Services.Debug!;
        var shell = h.Services.DebugShell!;
        var layout = h.Services.Layout.Layout;
        h.DebugSessions.UseScenarioFile("phase6-acceptance.json", ("source", demo));
        Assert.True(Core.Composition.ServiceGraph.ImplementedPhase >= 6); // the product has the debug windows (Phase 7 since I-226)
        Assert.Equal(6, h.Services.ImplementedPhase); // the scenario is pinned to its own phase (DebugScenario)

        // The user opens the program's source and sets a breakpoint with F9 on line 71 of Run.
        var (document, editor, probe) = await DebugScenario.OpenAsync(h, demo);
        editor.TextArea.Caret.Offset = editor.Document.GetLineByNumber(DebugDemo.StopLine).Offset;
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        h.Press("F9");
        Assert.Equal("Breakpoint", probe.GlyphAt(DebugDemo.StopLine));
        Assert.Equal("Hello – FreexComp", h.Window.Title);
        Assert.Equal(LayoutProfiles.Edit, layout.ActiveProfile);

        // F5: the project is built, the program starts under the debugger and stops at the breakpoint.
        h.Press("F5");
        await DebugScenario.WaitForStopAsync(h, DebugDemo.StopLine);
        Assert.Single(h.Build.Requests);
        Assert.Empty(h.Processes.Started); // under the debugger, not through the process launcher
        var stop = debug.CurrentStop!;
        Assert.Equal((StopReason.Breakpoint, new SourceLocation(demo, DebugDemo.StopLine, 3)), (stop.Reason, stop.Location));
        Assert.Equal([1], stop.HitBreakpointIds);
        Assert.Equal("Hello (Paused) – FreexComp", h.Window.Title);
        Assert.Equal("Paused at DbgDemo.pas:71", h.Status.State);

        // The editor shows the stop: the execution arrow over the breakpoint, the caret on the line.
        Assert.Same(document, h.Services.Documents.ActiveDocument);
        Assert.Equal("Execution", probe.GlyphAt(DebugDemo.StopLine));
        Assert.Equal("editor.execution.line.bg", probe.LineBackgroundAt(DebugDemo.StopLine));
        Assert.Equal(DebugDemo.StopLine, document.ViewState.Caret.Line);

        // The Debug profile: Call Stack, Locals and Breakpoints show the stop.
        Assert.Equal(LayoutProfiles.Debug, layout.ActiveProfile);
        Assert.True(h.Find<Control>("ToolBar.Debug").IsEffectivelyVisible);
        Assert.Equal(("Run", "DbgDemo.pas", "71"), (DebugScenario.Cell(h, "CallStack", "0", "Routine"), DebugScenario.Cell(h, "CallStack", "0", "File"), DebugScenario.Cell(h, "CallStack", "0", "Line")));
        Assert.Equal(["Run", "DbgDemo"], shell.CallStack.Rows.Select(r => r.Cells[1].Text));
        Assert.Equal(("24", "Integer"), (DebugScenario.Cell(h, "Locals", "Total", "Value"), DebugScenario.Cell(h, "Locals", "Total", "Type")));
        Assert.Equal(
            ["I=3", "Total=24", "Ch='A'", "Ok=True", "Color=clGreen", "Colors=[clRed, clBlue]", "S='Héllo'", "P=(X: 3; Y: 4)", "Nums=(10, 20, 30)",
                "Pet=TDog($000001F3A2B41C60)", "PTotal=$0000007F3A6FFB04"],
            shell.Locals.Rows.Select(r => $"{r.Cells[0].Text}={r.Cells[1].Text}"));
        var breakpointRow = Assert.Single(shell.Breakpoints.Rows);
        Assert.Equal(("DbgDemo.pas:71", true, "1"), (breakpointRow.Key, breakpointRow.IsHighlighted, breakpointRow.Cells[5].Text));
        Assert.Equal("1", DebugScenario.Cell(h, "Breakpoints", "DbgDemo.pas:71", "Hits"));

        // Inspect: the object's fields are fetched when its row is expanded.
        h.Click("Tool.Locals.Row.P.Expander");
        await DebugScenario.SettleAsync(h);
        Assert.Equal(("3", "4"), (DebugScenario.Cell(h, "Locals", "P.X", "Value"), DebugScenario.Cell(h, "Locals", "P.Y", "Value")));

        // What the program wrote before the stop is in the Output window's program channel.
        h.Clock.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Contains(new OutputLine("Dog Rex", OutputKind.Normal), h.Services.Output.GetLines(OutputChannels.Program));

        // F10 steps over: the next line; the breakpoint's own glyph is back on line 71; the views follow.
        h.Press("F10");
        await DebugScenario.WaitForStopAsync(h, 72);
        Assert.Equal((StopReason.Step, 72), (debug.CurrentStop!.Reason, debug.CurrentStop.Location!.Line));
        Assert.Equal("Execution", probe.GlyphAt(72));
        Assert.Equal("Breakpoint", probe.GlyphAt(DebugDemo.StopLine));
        Assert.Equal("Paused at DbgDemo.pas:72", h.Status.State);
        Assert.Equal("72", DebugScenario.Cell(h, "CallStack", "0", "Line"));
        Assert.Equal("24", DebugScenario.Cell(h, "Locals", "Total", "Value"));
        Assert.True(shell.Locals.Rows.Single(r => r.Key == "P").IsExpanded); // kept across the stops
        Assert.False(Assert.Single(shell.Breakpoints.Rows).IsHighlighted);
        Assert.Contains("\"threadId\":1", Assert.Single(h.DebugSessions.SentArguments("next")), StringComparison.Ordinal);

        // F5 continues: the program runs to its end; the IDE is idle again, in the Edit profile.
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(debug.Session);
        Assert.Equal("Hello – FreexComp", h.Window.Title);
        Assert.Equal(LayoutProfiles.Edit, layout.ActiveProfile);
        Assert.NotEqual("Execution", probe.GlyphAt(72));
        Assert.Equal("Breakpoint", probe.GlyphAt(DebugDemo.StopLine));
        h.Clock.Advance(TimeSpan.FromMilliseconds(50));
        var output = h.Services.Output.GetLines(OutputChannels.Program);
        Assert.Equal(["Dog Rex", "24", "done"], output.Where(l => l.Kind == OutputKind.Normal).Select(l => l.Text));
        Assert.Contains(output, l => l.Kind == OutputKind.Ide && l.Text.StartsWith("Program exited with code 0", StringComparison.Ordinal));

        // The Event Log kept the session's story; the breakpoint is the project's (session.json).
        Assert.Equal(
            [DebugLogKind.Process, DebugLogKind.Breakpoint, DebugLogKind.Breakpoint, DebugLogKind.Process],
            debug.Log.Select(e => e.Kind));
        Assert.Equal(["Breakpoint at DbgDemo.pas:71", "Step completed", string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Debug_ProcessExited, 0, "0")],
            debug.Log.Skip(1).Select(e => e.Message));
        h.Services.Session.Save();
        using var session = JsonDocument.Parse(File.ReadAllText(h.Services.Session.SessionPath));
        Assert.Equal(DebugDemo.StopLine, Assert.Single(session.RootElement.GetProperty("breakpoints").EnumerateArray()).GetProperty("line").GetInt32());

        // Every step of the scenario file was asked for, in its order.
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());
        Assert.Empty(h.Messages.Requests);
    }
}
