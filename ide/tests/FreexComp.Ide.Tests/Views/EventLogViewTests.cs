using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Event Log tool window (I-144; tool-windows.md §15): the rows the IDE makes of the session's events, with the
/// kinds of the debug-session interface §6.1 — Process, Module, Thread, Breakpoint, Exception, Debugger — the filter,
/// copy, clear and the default action; with sessions over the scripted adapter.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed partial class EventLogViewTests
{
    [GeneratedRegex(@"^\d\d:\d\d:\d\d\.\d\d\d$")]
    private static partial Regex TimeFormat();

    private static string Row(DebugGridRow row) => $"{row.Cells[1].Text}: {row.Cells[2].Text}";

    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-TW-1300")]
    [Trait("Req", "UI-TW-1301")]
    [Trait("Req", "UI-TW-1302")]
    [Trait("Req", "UI-TW-1303")]
    [Trait("Req", "DBG-263")]
    public async Task KindsFromSessionEvents()
    {
        await using var h = await DebugScenario.StartAsync();
        var unitPath = DebugScenario.UnitPath(h);
        var exe = BuildScenario.ExePath(h);
        var debug = h.Services.Debug!;
        var log = h.Services.DebugShell!.EventLog;
        var commands = h.Services.Commands;
        var clipboard = (FakeClipboard)h.Services.Clipboard;
        void Show()
        {
            h.Services.Layout.Layout.BottomSize = 400;
            Assert.True(h.Services.ToolWindows.Show(EventLogViewModel.ToolName, focus: true));
            Dispatcher.UIThread.RunJobs();
        }

        // No rows: the empty state; nothing to clear.
        Show();
        Assert.Equal(Strings.Msg_Debug_EventLogEmpty, log.EmptyHeading);
        Assert.True(h.Find<Control>("Tool.EventLog.Empty").IsEffectivelyVisible);
        Assert.False(commands.CanExecute("eventLog.clear"));
        Assert.Equal(["Time", "Kind", "Message"], log.Columns.Select(c => c.Id));

        // A session without type information: a module and a thread, a log point, a breakpoint stop, the thread's and the
        // program's end. The log point's breakpoint comes from the session file (it has no dialog in Phase 6).
        h.Services.Breakpoints!.Load([new BreakpointInfo(unitPath, 5), new BreakpointInfo(unitPath, 6, LogMessage: "reached line 6")]);
        h.DebugSessions.UseScenario($$"""
            { "steps": [
              { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 11, "verified": true, "line": 5 }, { "id": 12, "verified": true, "line": 6 } ] } },
              { "on": "configurationDone", "respond": {}, "thenEvents": [
                  { "event": "module", "body": { "reason": "new", "module": { "id": "1", "name": "Hello.exe", "path": {{DebugScenarios.Json(exe)}} } } },
                  { "event": "thread", "body": { "reason": "started", "threadId": 9 } },
                  {{DebugScenarios.StoppedEvent(more: """, "hitBreakpointIds": [12]""")}} ] },
              {{DebugScenarios.Threads}}, {{DebugScenarios.Stack(unitPath, 6)}},
              {{DebugScenarios.Continue(DebugScenarios.StoppedEvent(more: """, "hitBreakpointIds": [11]"""))}},
              {{DebugScenarios.AtStop([("Run", unitPath, 5)])}},
              {{DebugScenarios.Continue($$"""{ "event": "thread", "body": { "reason": "exited", "threadId": 9 } }, {{DebugScenarios.Exit(3)}}""")}} ] }
            """);
        h.Press("F5");
        await DebugScenario.WaitForStopAsync(h, 5);
        Show();

        // The kinds of the debug-session interface §6.1, newest last (UI-TW-1300); keys count from the last clear (UI-TW-1303).
        Assert.Null(log.EmptyHeading);
        Assert.Equal(
        [
            "Debugger: Type information file not found: " + Path.ChangeExtension(exe, ".pdi") + ". Values are shown unformatted.",
            "Process: Program started: Hello.exe (process 0)",
            "Module: Module loaded: " + exe,
            "Thread: Thread 9 started",
            "Breakpoint: reached line 6",
            "Breakpoint: Breakpoint at Unit1.pas:5",
        ], log.Rows.Select(Row));
        Assert.Equal(["0", "1", "2", "3", "4", "5"], log.Rows.Select(r => r.Key));
        Assert.All(log.Rows, r => Assert.Matches(TimeFormat(), r.Cells[0].Text)); // HH:mm:ss.fff
        Assert.Equal(("Process", "Program started: Hello.exe (process 0)"), (DebugScenario.Cell(h, "EventLog", "1", "Kind"), DebugScenario.Cell(h, "EventLog", "1", "Message")));
        Assert.Equal(log.Rows[1].Cells[0].Text, DebugScenario.Cell(h, "EventLog", "1", "Time"));
        Assert.Equal(DebugCellStyle.Warning, log.Rows[0].Cells[1].Style); // Debugger rows: status.warning
        Assert.Equal(DebugCellStyle.Normal, log.Rows[1].Cells[1].Style);
        Assert.True(h.Find<ListBox>("Tool.EventLog.List").SelectionMode.HasFlag(SelectionMode.Multiple));

        // The program runs on and ends: kept after the session ends.
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Thread: Thread 9 exited", "Process: Process 0 exited with code 3"], log.Rows.Skip(6).Select(Row));
        Assert.Equal(8, log.Rows.Count);
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());

        // UI-TW-1301: the filter matches Kind and Message (applied after the filter delay).
        log.FilterText = "thread";
        h.Clock.Advance(ToolWindowViewModel.FilterDelay);
        Assert.Equal(["3", "6"], log.Rows.Select(r => r.Key));
        log.FilterText = "LINE 6";
        h.Clock.Advance(ToolWindowViewModel.FilterDelay);
        Assert.Equal(["Breakpoint: reached line 6"], log.Rows.Select(Row));
        log.FilterText = "";
        h.Clock.Advance(ToolWindowViewModel.FilterDelay);
        Assert.Equal(8, log.Rows.Count);
        Assert.NotNull(h.TryFind<Control>("Tool.EventLog.Filter"));
        Assert.NotNull(h.TryFind<Control>("Tool.EventLog.ToolBar.eventLog.clear"));

        // UI-TW-1302: the default action of a Breakpoint row with a source location navigates to it; other rows do nothing.
        await h.Services.Documents.OpenAsync(DebugScenario.MainPath(h));
        await log.ActivateAsync(log.Rows[2], null); // a Module row
        Assert.Equal(DebugScenario.MainPath(h), h.Services.Documents.ActiveDocument?.FilePath);
        Show();
        h.DoubleClick("Tool.EventLog.Row.5.Message");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == unitPath, "the stop's location shown");
        Assert.Equal(5, ((TextDocumentViewModel)h.Services.Documents.ActiveDocument!).ViewState.Caret.Line);

        // UI-TW-1300: edit.copy copies the selected rows as <Time>\t<Kind>\t<Message>; edit.selectAll selects every row.
        Show();
        h.Click("Tool.EventLog.Row.3.Message");
        log.SelectedKeys = ["3", "6"];
        h.Press("Ctrl+C");
        Assert.Equal($"{log.Rows[3].Cells[0].Text}\tThread\tThread 9 started\r\n{log.Rows[6].Cells[0].Text}\tThread\tThread 9 exited", clipboard.Text);
        Assert.True(await log.ExecuteAsync("edit.selectAll", null));
        Assert.Equal(8, log.SelectedKeys.Count);
        Assert.Equal(["edit.copy", "edit.selectAll", null, "eventLog.clear"], log.ContextCommands);
        Assert.Equal(10_000, DebugService.MaxLogRows);

        // A new session clears the log. Its program raises an exception: an Exception row (status.error) that navigates too.
        File.WriteAllText(Path.ChangeExtension(exe, ".pdi"), DebugRaises.TypeInfo(unitPath));
        h.Services.Breakpoints.Load([]);
        h.Messages.Answer("Msg.Debug.Exception", "Break");
        h.DebugSessions.UseScenario($$"""
            { {{DebugRaises.Capabilities}}, {{DebugRaises.Memory}}, "steps": [
              {{DebugRaises.HookSet}},
              { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DebugRaises.HookHit}} ] },
              {{DebugRaises.AtRaiseStop(unitPath, 6, DebugRaises.SecondSlot)}} ] }
            """);
        h.Press("F5");
        await DebugScenario.WaitForStopAsync(h, 6);
        Assert.Equal(["Process: Program started: Hello.exe (process 0)", "Exception: ESecond: second"], log.Rows.Select(Row));
        Assert.Equal(["0", "1"], log.Rows.Select(r => r.Key));
        Assert.Equal((DebugCellStyle.Error, DebugCellStyle.Error), (log.Rows[1].Cells[1].Style, log.Rows[1].Cells[2].Style));
        await h.Services.Documents.OpenAsync(DebugScenario.MainPath(h));
        await log.ActivateAsync(log.Rows[1], null);
        Assert.Equal(unitPath, h.Services.Documents.ActiveDocument?.FilePath);
        Assert.Equal(6, ((TextDocumentViewModel)h.Services.Documents.ActiveDocument!).ViewState.Caret.Line);

        // eventLog.clear empties it; the session goes on.
        Assert.True(commands.CanExecute("eventLog.clear"));
        await h.ExecuteAsync("eventLog.clear");
        Assert.Empty(log.Rows);
        Assert.Equal(Strings.Msg_Debug_EventLogEmpty, log.EmptyHeading);
        Assert.Equal(IdeState.Paused, h.Services.State.Current.State);
        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Process: " + Strings.Msg_Run_Terminated], log.Rows.Select(Row));
        Assert.Equal("0", log.Rows[0].Key);
        Assert.Same(debug.Log[0], log.Rows[0].Tag);
    }
}
