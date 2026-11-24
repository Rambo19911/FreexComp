using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Threads tool window (I-142; tool-windows.md §14, the debug-session interface §6.1): the list from
/// <c>GetThreadsAsync</c> and <c>ThreadChanged</c>, the derived State column, the location of each thread's top frame and
/// switching the current thread — with a session over the scripted adapter.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class ThreadsViewTests
{
    private static string ThreadEvent(string reason, int id) => $$"""{ "event": "thread", "body": { "reason": "{{reason}}", "threadId": {{id}} } }""";

    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-TW-1200")]
    [Trait("Req", "UI-TW-1201")]
    [Trait("Req", "UI-TW-1202")]
    public async Task ListStateAndSwitch()
    {
        await using var h = await DebugScenario.StartAsync();
        var unitPath = DebugScenario.UnitPath(h);
        var mainPath = DebugScenario.MainPath(h);
        var debug = h.Services.Debug!;
        var threads = h.Services.DebugShell!.Threads;
        var stack = h.Services.DebugShell.CallStack;
        var clipboard = (FakeClipboard)h.Services.Clipboard;
        var commands = h.Services.Commands;
        var list = DebugScenarios.ThreadList((1, "main"), (9, ""), (12, "Timer"));
        (string, string, int)[] mainFrames = [("Run", unitPath, 5), ("Hello", mainPath, 3)];
        (string, string, int)[] workerFrames = [("Worker", unitPath, 6)];
        const string NoSource = """{ "on": "stackTrace", "respond": { "totalFrames": 1, "stackFrames": [ { "id": 300, "name": "", "line": 0, "column": 0, "instructionPointerReference": "0x7FFB2C1D0F14" } ] } }""";
        h.DebugSessions.UseScenario($$"""
            { "steps": [
              { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 5 } ] } },
              { "on": "configurationDone", "respond": {}, "thenEvents": [ {{ThreadEvent("started", 9)}}, {{ThreadEvent("started", 12)}},
                  {{DebugScenarios.StoppedEvent(more: """, "hitBreakpointIds": [1]""")}} ] },
              {{DebugScenarios.AtStop(mainFrames, threads: list)}},
              {{DebugScenarios.Stack(200, workerFrames)}}, {{NoSource}},
              {{DebugScenarios.Stack(200, workerFrames)}}, {{DebugScenarios.NoScopes}},
              {{DebugScenarios.Stack(mainFrames)}},
              { "on": "continue", "respond": { "allThreadsContinued": true }, "thenEvents": [ {{ThreadEvent("exited", 9)}}, {{ThreadEvent("started", 15)}} ] },
              { "on": "pause", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent("pause")}} ] },
              {{DebugScenarios.AtStop(mainFrames, threads: DebugScenarios.ThreadList((1, "main"), (12, "Timer"), (15, "")))}},
              {{NoSource}}, {{NoSource}} ] }
            """);
        h.Services.Breakpoints!.Toggle(unitPath, 5);

        // No session: the empty state.
        Assert.True(h.Services.ToolWindows.Show(ThreadsViewModel.ToolName, focus: false));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Strings.Msg_Debug_NoSession, threads.EmptyHeading);
        Assert.True(h.Find<Control>("Tool.Threads.Empty").IsEffectivelyVisible);

        h.Press("F5");
        await DebugScenario.WaitForStopAsync(h, 5);
        Assert.True(h.Services.ToolWindows.Show(ThreadsViewModel.ToolName, focus: false)); // the Debug profile shows Call Stack first
        Dispatcher.UIThread.RunJobs();

        // UI-TW-1200 / UI-TW-1202: one row per thread, keyed by its id, in creation order; the state is derived from the stop
        // (the thread that hit the breakpoint / the others); the location is the top frame's <file name>:<line>, else its name.
        Assert.Null(threads.EmptyHeading);
        Assert.Equal(["Marker", "Id", "Name", "State", "Location"], threads.Columns.Select(c => c.Id));
        Assert.Equal(["1", "9", "12"], threads.Rows.Select(r => r.Key));
        string Row(string key) => string.Join(" | ", new[] { "Id", "Name", "State", "Location" }.Select(c => DebugScenario.Cell(h, "Threads", key, c)));
        Assert.Equal("1 | main | Stopped at breakpoint | Unit1.pas:5", Row("1"));
        Assert.Equal("9 | Thread 9 | Paused | Unit1.pas:6", Row("9"));
        Assert.Equal("12 | Timer | Paused | $00007FFB2C1D0F14", Row("12"));
        Assert.Equal(LineGlyphKind.Execution, h.Find<DebugGlyph>("Tool.Threads.Row.1.Marker").Kind); // the arrow on the current thread
        Assert.Null(threads.Rows[1].Cells[0].Glyph);
        Assert.Equal(1, debug.CurrentThreadId);
        Assert.Equal(SelectionMode.Single, h.Find<ListBox>("Tool.Threads.List").SelectionMode);

        // UI-TW-1201: the default action makes the thread the current thread — Call Stack, Locals and the editor follow.
        h.Click("Tool.Threads.Row.9.Name");
        Assert.Equal(["9"], threads.SelectedKeys);
        Assert.Equal(1, debug.CurrentThreadId); // a single click only selects
        Assert.True(commands.CanExecute("threads.switchTo"));
        h.Press("Enter");
        await h.WaitForAsync(() => debug.CurrentThreadId == 9, "thread 9 is the current thread");
        await DebugScenario.SettleAsync(h);
        Assert.Null(threads.Rows[0].Cells[0].Glyph);
        Assert.Equal(LineGlyphKind.Execution, threads.Rows[1].Cells[0].Glyph);
        Assert.Equal(["Worker"], stack.Rows.Select(r => r.Cells[1].Text));
        Assert.Equal("Worker", (await debug.GetEvaluationFrameAsync())?.Name);
        Assert.Equal(6, ((TextDocumentViewModel)h.Services.Documents.ActiveDocument!).ViewState.Caret.Line);
        Assert.Equal("Frame", DebugScenario.ProbeOf(h, unitPath).GlyphAt(6)); // not the execution point: the frame marker
        Assert.Equal("Execution", DebugScenario.ProbeOf(h, unitPath).GlyphAt(5));
        Assert.Contains("\"threadId\":9", h.DebugSessions.SentArguments("stackTrace")[^1], StringComparison.Ordinal);
        Assert.Contains("\"frameId\":200", h.DebugSessions.SentArguments("scopes")[^1], StringComparison.Ordinal); // Locals follows

        // Back to the thread that stopped (double-click): the execution point again.
        h.DoubleClick("Tool.Threads.Row.1.Name");
        await h.WaitForAsync(() => debug.CurrentThreadId == 1, "thread 1 is the current thread");
        await DebugScenario.SettleAsync(h);
        Assert.Equal(["Run", "Hello"], stack.Rows.Select(r => r.Cells[1].Text));
        Assert.Equal(5, ((TextDocumentViewModel)h.Services.Documents.ActiveDocument!).ViewState.Caret.Line);
        Assert.NotEqual("Frame", DebugScenario.ProbeOf(h, unitPath).GlyphAt(6));

        // Sorting by a sortable column (Name), ascending then descending then natural order again.
        h.Click("Tool.Threads.Column.Name");
        Assert.Equal(["main", "Thread 9", "Timer"], threads.Rows.Select(r => r.Cells[2].Text));
        h.Click("Tool.Threads.Column.Name");
        Assert.Equal(["Timer", "Thread 9", "main"], threads.Rows.Select(r => r.Cells[2].Text));
        h.Click("Tool.Threads.Column.Name");
        Assert.Equal(["1", "9", "12"], threads.Rows.Select(r => r.Key));
        h.Click("Tool.Threads.Column.Location"); // not sortable
        Assert.Null(threads.SortColumn);

        // edit.copy: the selected row as tab-separated text. The context menu of tool-windows.md §14.
        h.Click("Tool.Threads.Row.12.Name");
        Assert.True(await threads.ExecuteAsync("edit.copy", null));
        Assert.Equal("12\tTimer\tPaused\t$00007FFB2C1D0F14", clipboard.Text);
        Assert.Equal(["threads.switchTo", null, "edit.copy"], threads.ContextCommands);

        // Running: every thread runs, no locations; threads that exit or start leave or join the list; no switching.
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        await h.WaitForAsync(() => threads.Rows.Any(r => r.Key == "15"), "the new thread listed");
        Assert.Equal(["1", "12", "15"], threads.Rows.Select(r => r.Key));
        Assert.Equal(["main | Running | ", "Timer | Running | ", "Thread 15 | Running | "], threads.Rows.Select(r => $"{r.Cells[2].Text} | {r.Cells[3].Text} | {r.Cells[4].Text}"));
        Assert.All(threads.Rows, r => Assert.Null(r.Cells[0].Glyph));
        threads.SelectedKeys = ["12"];
        Assert.False(commands.CanExecute("threads.switchTo"));

        // The next stop refreshes the list; a pause is "Paused" for the thread that stopped too.
        h.Press("Ctrl+Alt+Break");
        await DebugScenario.WaitForStopAsync(h, 5);
        Assert.Equal(["main | Paused | Unit1.pas:5", "Timer | Paused | $00007FFB2C1D0F14", "Thread 15 | Paused | $00007FFB2C1D0F14"],
            threads.Rows.Select(r => $"{r.Cells[2].Text} | {r.Cells[3].Text} | {r.Cells[4].Text}"));

        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(threads.Rows);
        Assert.Equal(Strings.Msg_Debug_NoSession, threads.EmptyHeading);
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());
    }
}
