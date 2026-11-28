using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// Terminal mode of the session, lifetime part (G-106): the console is closed exactly once before the end of the
/// session is reported; what the session does when the console or its command ends first; and the entry stop, which
/// the adapter reports on the thread the system injects when it attaches to a program started in a console.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Phase", "8")]
public sealed class SessionTerminalLifetimeTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Threads = """{ "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } }""";

    /// <summary>A terminal whose run notes, with every close, the state of the session and whether its end was already reported.</summary>
    private static FakeTerminal TerminalOf(SessionHarness harness)
    {
        var terminal = new FakeTerminal();
        terminal.Run.Note = () => harness.Session.State
            + (harness.Events.Snapshot().Any(e => e.StartsWith("terminated", StringComparison.Ordinal)) ? " after the end was reported" : "");
        return terminal;
    }

    [Theory]
    [InlineData("the program exits by itself", "keepWindow=True Terminating", "terminated Exited 0 null")]
    [InlineData("the user terminates", "keepWindow=False Terminating", "terminated TerminatedByUser null null")]
    [InlineData("the adapter crashes", "keepWindow=False Running", "terminated AdapterCrashed null Debug adapter exited unexpectedly (exit code 7).")]
    [InlineData("the launch fails after the start", "keepWindow=False Starting", "terminated LaunchFailed null The program could not be started in its console window (exit code 5).")]
    [Trait("Req", "DBG-551")]
    [Trait("Req", "DBG-552")]
    [Trait("Req", "DBG-432")]
    public async Task End_ClosesTheTerminalOnce_KeepWindowOnlyAfterExit(string how, string close, string end)
    {
        var scenario = how switch
        {
            "the program exits by itself" => SessionTerminalTests.Scenario(more: $$"""
                , {{Threads}}, { "on": "pause", "respond": {}, "thenEvents": [ { "event": "exited", "body": { "exitCode": 0 } }, { "event": "terminated" } ] }
                """),
            "the adapter crashes" => SessionTerminalTests.Scenario(more: """, { "on": "threads", "thenExit": 7 }"""),
            "the launch fails after the start" => """{ "initializedAfter": "never", "steps": [ { "on": "launch", "respond": {}, "thenReverseRequest": """ + SessionTerminalTests.RunInTerminal + " } ] }",
            _ => SessionTerminalTests.Scenario(),
        };
        await using var harness = SessionHarness.Create(scenario);
        var terminal = TerminalOf(harness);

        var launch = harness.LaunchAsync(SessionTerminalTests.Request(terminal));
        switch (how)
        {
            case "the program exits by itself":
                await launch;
                await harness.Session.PauseAsync(Ct).WaitAsync(Safety, Ct);
                break;
            case "the user terminates":
                await launch;
                await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
                break;
            case "the adapter crashes":
                await launch;
                await Assert.ThrowsAsync<DebuggerException>(() => harness.Session.PauseAsync(Ct).WaitAsync(Safety, Ct));
                break;
            default:
                await SessionTerminalTests.AnswerAsync(harness, "runInTerminal");
                terminal.Run.End(5);
                await Assert.ThrowsAsync<DebuggerException>(() => launch);
                break;
        }

        // Closed before the end was reported: the window stays only when the program ended by itself.
        Assert.StartsWith(end, await harness.WaitForEventAsync("terminated"), StringComparison.Ordinal);
        Assert.Equal([close], terminal.Run.Closes);
        Assert.Equal("state " + close[(close.LastIndexOf(' ') + 1)..] + "->Terminated", harness.Events.Snapshot()[^2]);

        // Once: nothing that follows closes it again.
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await harness.Session.DisposeAsync().AsTask().WaitAsync(Safety, Ct);
        Assert.Equal([close], terminal.Run.Closes);
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
    }

    [Theory]
    [InlineData("throws", "The console host does not answer.")]
    [InlineData("faults", "failed")]
    [InlineData("hangs", "timed out")]
    [Trait("Req", "DBG-551")]
    [Trait("Req", "DBG-558")]
    [Trait("Req", "DBG-432")]
    public async Task CloseFaultsOrHangs_SessionStillEnds_OneDebuggerLine(string how, string reason)
    {
        await using var harness = SessionHarness.Create(SessionTerminalTests.Scenario());
        var terminal = TerminalOf(harness);
        var hang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeToken = CancellationToken.None;
        terminal.Run.OnClose = (_, ct) =>
        {
            closeToken = ct;
            switch (how)
            {
                case "throws":
                    throw new InvalidOperationException("The console host does not answer.\r\nIts pipe is broken.");
                case "faults":
                    return Task.FromException(new IOException(""));
                default:
                    // The session waits for this close on its clock: the test moves the clock once that wait exists.
                    waiting.TrySetResult(harness.Clock.WhenTimerCreated(new ShutdownWaits().TerminalClose));
                    return hang.Task;
            }
        };
        await harness.LaunchAsync(SessionTerminalTests.Request(terminal));

        var end = harness.Session.TerminateAsync(Ct);
        if (how == "hangs")
        {
            await (await waiting.Task.WaitAsync(Safety, Ct)).WaitAsync(Safety, Ct);
            Assert.Equal(DebugState.Terminating, harness.Session.State);
            harness.Clock.Advance(TimeSpan.FromSeconds(3));
        }

        await end.WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");

        // The session ended all the same; the failure is one line of the debugger, before the end.
        var line = "output Debugger " + System.Text.Json.JsonSerializer.Serialize("The program's console window could not be closed: " + reason);
        Assert.Equal(
            ["state Running->Terminating", line, "state Terminating->Terminated", "terminated TerminatedByUser null null"],
            harness.Events.Snapshot().TakeLast(4));
        Assert.Equal(["keepWindow=False Terminating"], terminal.Run.Closes);
        // A close that was given up is told so.
        Assert.Equal(how == "hangs", closeToken.IsCancellationRequested);
        hang.TrySetResult();
        harness.AssertScenarioComplete();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "DBG-551")]
    public async Task StartCompletesAfterTheEndBegan_IsClosed(bool disposed)
    {
        // The console takes its time to open and does not react to the cancellation; meanwhile the session ends.
        // When the console is there after all, nobody uses it: it is closed, without keeping the window.
        await using var harness = SessionHarness.Create(SessionTerminalTests.Scenario());
        var terminal = new FakeTerminal();
        var pending = new TaskCompletionSource<IDebuggeeTerminalRun>(TaskCreationOptions.RunContinuationsAsynchronously);
        terminal.OnStart = (_, _) => pending.Task;

        var launch = harness.LaunchAsync(SessionTerminalTests.Request(terminal));
        var token = await terminal.Started.WaitAsync(Safety, Ct);
        // The scripted adapter waits for the answer to its request and answers nothing else.
        var end = disposed ? harness.Session.DisposeAsync().AsTask() : harness.Session.TerminateAsync(Ct);
        await harness.WaitForRequestAsync("disconnect");
        harness.Clock.Advance(new ShutdownWaits().Disconnect);
        await end.WaitAsync(Safety, Ct);
        await Assert.ThrowsAsync<DebuggerException>(() => launch);
        var events = harness.Events.Snapshot();
        Assert.Equal(disposed ? "terminated Disposed null null" : "terminated TerminatedByUser null null", events[^1]);
        Assert.True(token.IsCancellationRequested);
        Assert.Empty(terminal.Run.Closes);

        pending.SetResult(terminal.Run);

        await terminal.Run.CloseCalled.WaitAsync(Safety, Ct);
        Assert.Equal(["keepWindow=False"], terminal.Run.Closes);
        // The adapter was never answered, and the end of the session stays the last thing reported.
        Assert.DoesNotContain(harness.Trace.Snapshot(), e => e.Direction == DapDirection.ToAdapter && e.Json.Contains("\"type\":\"response\"", StringComparison.Ordinal));
        if (!disposed)
            await harness.Session.DisposeAsync().AsTask().WaitAsync(Safety, Ct);
        Assert.Equal(events, harness.Events.Snapshot());
        Assert.Equal(["keepWindow=False"], terminal.Run.Closes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "DBG-550")]
    [Trait("Req", "DBG-433")]
    public async Task ConsoleGoneWhileRunningOrStopped_EndsTheSession(bool stopped)
    {
        // The user closed the console window (or its process ended): that is the user's stop.
        var more = stopped
            ? $$"""
              , { "on": "configurationDone", "respond": {}, "thenEvents": [ { "event": "stopped", "body": { "reason": "breakpoint", "threadId": 1 } } ] },
              {{Threads}},
              { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 100, "name": "Run", "line": 69, "column": 3,
                  "source": { "path": "C:\\t\\DbgDemo.pas" } } ], "totalFrames": 1 } }
              """
            : "";
        await using var harness = SessionHarness.Create(SessionTerminalTests.Scenario(more: more));
        var terminal = TerminalOf(harness);
        await harness.LaunchAsync(SessionTerminalTests.Request(terminal));
        if (stopped)
            await harness.WaitForEventAsync("stopped");
        Assert.Equal(stopped ? DebugState.Stopped : DebugState.Running, harness.Session.State);

        terminal.Run.End(null);

        Assert.Equal("terminated TerminatedByUser null null", await harness.WaitForEventAsync("terminated"));
        Assert.Equal(
            [$"state {(stopped ? "Stopped" : "Running")}->Terminating", "state Terminating->Terminated", "terminated TerminatedByUser null null"],
            harness.Events.Snapshot().TakeLast(3));
        // The shutdown sequence ran: the adapter was asked to end, and the console's processes are the terminal's to end.
        Assert.Equal("disconnect", harness.SentCommands()[^1]);
        Assert.Equal(["keepWindow=False Terminating"], terminal.Run.Closes);
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
        // A terminate of the user that follows finds the session ended.
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        Assert.Single(harness.Events.Snapshot(), e => e.StartsWith("terminated", StringComparison.Ordinal));
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-550")]
    [Trait("Req", "DBG-433")]
    public async Task CommandEnded_AdapterSilent_EndsAsExitedAfterTheWait()
    {
        // The command ended with the program's exit code, yet the adapter reports nothing: after the wait for its
        // report the session ends by itself, with that code.
        await using var harness = SessionHarness.Create(SessionTerminalTests.Scenario());
        var terminal = TerminalOf(harness);
        await harness.LaunchAsync(SessionTerminalTests.Request(terminal));
        var waiting = harness.Clock.WhenTimerCreated(new ShutdownWaits().AdapterExit);

        terminal.Run.End(3);
        await waiting.WaitAsync(Safety, Ct);
        Assert.Equal(DebugState.Running, harness.Session.State);
        Assert.Null(harness.Session.ExitCode);
        harness.Clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal("terminated Exited 3 null", await harness.WaitForEventAsync("terminated"));
        Assert.Equal(3, harness.Session.ExitCode);
        Assert.Equal(
            ["state Running->Terminating", "state Terminating->Terminated", "terminated Exited 3 null"],
            harness.Events.Snapshot().TakeLast(3));
        // The program ended by itself: the window may stay.
        Assert.Equal(["keepWindow=True Terminating"], terminal.Run.Closes);
        harness.AssertScenarioComplete();
    }

    [Theory]
    [InlineData("command first")]
    [InlineData("adapter first")]
    [Trait("Req", "DBG-550")]
    [Trait("Req", "DBG-433")]
    public async Task CommandEnded_AdapterReports_NoSecondEnd(string order)
    {
        // The usual end: the adapter reports the exit - before or after the command's end reaches the session. One end.
        await using var harness = SessionHarness.Create(SessionTerminalTests.Scenario(more: order == "command first"
            ? $$"""
              , {{Threads}}, { "on": "pause", "respond": {}, "thenEvents": [ { "event": "exited", "body": { "exitCode": 3 } }, { "event": "terminated" } ] }
              """
            // The exit is reported, the end of the debugging not yet: the session lives on until that comes.
            : $$"""
              , {{Threads}}, { "on": "pause", "respond": {}, "thenEvents": [ { "event": "exited", "body": { "exitCode": 3 } },
                  { "event": "output", "body": { "category": "console", "output": "exited" } } ] }
              """));
        var terminal = TerminalOf(harness);
        await harness.LaunchAsync(SessionTerminalTests.Request(terminal));

        if (order == "command first")
        {
            var waiting = harness.Clock.WhenTimerCreated(new ShutdownWaits().AdapterExit);
            terminal.Run.End(3);
            await waiting.WaitAsync(Safety, Ct);
            await harness.Session.PauseAsync(Ct).WaitAsync(Safety, Ct);
            Assert.Equal("terminated Exited 3 null", await harness.WaitForEventAsync("terminated"));
            // The wait for the adapter's report is over with the session: its time changes nothing.
            harness.Clock.Advance(TimeSpan.FromSeconds(2));
        }
        else
        {
            await harness.Session.PauseAsync(Ct).WaitAsync(Safety, Ct);
            await harness.WaitForEventAsync("output Debugger \"exited\"");
            terminal.Run.End(3);
            // No wait is started for an exit the adapter has reported: the clock may run, the session stays. (A call
            // the session refuses in this state has gone through its loop: what was posted before it is done.)
            await Assert.ThrowsAsync<DebuggerException>(() => harness.Session.GetThreadsAsync(Ct).WaitAsync(Safety, Ct));
            harness.Clock.Advance(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAsync<DebuggerException>(() => harness.Session.GetThreadsAsync(Ct).WaitAsync(Safety, Ct));
            Assert.Equal(DebugState.Running, harness.Session.State);
            Assert.Empty(terminal.Run.Closes);
            await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        }

        await harness.Session.DisposeAsync().AsTask().WaitAsync(Safety, Ct);
        Assert.Single(harness.Events.Snapshot(), e => e.StartsWith("terminated", StringComparison.Ordinal));
        Assert.Single(harness.Events.Snapshot(), e => e.EndsWith("->Terminating", StringComparison.Ordinal));
        Assert.Equal([order == "command first" ? "keepWindow=True Terminating" : "keepWindow=False Terminating"], terminal.Run.Closes);
    }

    // ---- the entry stop (DBG-554) ----

    private const string EntryStop = """{ "on": "configurationDone", "respond": {}, "thenEvents": [ { "event": "stopped", "body": { "reason": "entry", "threadId": 2, "allThreadsStopped": true } } ] }""";

    /// <summary>The stack of the thread the system injects: its routines only, none with source.</summary>
    private const string BreakInStack = """
        { "on": "stackTrace", "expectArguments": { "threadId": 2, "startFrame": 0, "levels": 20 },
          "respond": { "totalFrames": 3, "stackFrames": [
            { "id": 1048576, "name": "DbgBreakPoint", "line": 2, "column": 1, "instructionPointerReference": "0x7FFBEC8F6AE1",
              "source": { "name": "DbgBreakPoint", "path": "C:\\Windows\\System32\\ntdll.dll`DbgBreakPoint", "presentationHint": "deemphasize", "sourceReference": 6 } },
            { "id": 1048577, "name": "DbgUiRemoteBreakin", "line": 18, "column": 1, "instructionPointerReference": "0x7FFBEC90902E",
              "source": { "name": "DbgUiRemoteBreakin", "path": "C:\\Windows\\System32\\ntdll.dll`DbgUiRemoteBreakin", "presentationHint": "deemphasize", "sourceReference": 7 } },
            { "id": 1048578, "name": "RtlUserThreadStart", "line": 20, "column": 1, "instructionPointerReference": "0x7FFBEC899C44",
              "source": { "name": "RtlUserThreadStart", "path": "C:\\Windows\\System32\\ntdll.dll`RtlUserThreadStart", "presentationHint": "deemphasize", "sourceReference": 8 } } ] } }
        """;

    /// <summary>The program's thread, which has not run yet.</summary>
    private static string ProgramStack(int threadId) => $$"""
        { "on": "stackTrace", "expectArguments": { "threadId": {{threadId}}, "startFrame": 0, "levels": 20 },
          "respond": { "totalFrames": 1, "stackFrames": [
            { "id": 524288, "name": "RtlUserThreadStart", "line": 1, "column": 1, "instructionPointerReference": "0x7FFBEC899C30",
              "source": { "name": "RtlUserThreadStart", "path": "C:\\Windows\\System32\\ntdll.dll`RtlUserThreadStart", "presentationHint": "deemphasize", "sourceReference": 9 } } ] } }
        """;

    [Theory]
    [InlineData("2, 1", false)]
    [InlineData("1, 2", false)]
    [InlineData("2, 1", true)]
    [Trait("Req", "DBG-554")]
    public async Task EntryStopOnTheBreakInThread_IsPublishedOnTheProgramThread(string threadOrder, bool atMainBodyWithoutTypeInformation)
    {
        // Attached to a program started in its console, the adapter reports the entry stop on the injected thread.
        var threads = string.Join(", ", threadOrder.Split(", ").Select(id => $$"""{ "id": {{id}}, "name": "Thread {{id}}" }"""));
        await using var harness = SessionHarness.Create(SessionTerminalTests.Scenario(more: $$"""
            , {{EntryStop}},
            { "on": "threads", "respond": { "threads": [ {{threads}} ] } },
            {{BreakInStack}},
            {{ProgramStack(1)}}
            """));
        var terminal = new FakeTerminal();
        var request = SessionTerminalTests.Request(terminal);
        request = atMainBodyWithoutTypeInformation
            // The first step of a program whose main block is not known: the entry stop is the public stop.
            ? request with { StopAtMainBody = true, TypeInfoPath = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "missing-" + Guid.NewGuid().ToString("N") + ".pdi") }
            : request with { StopOnEntry = true };

        await harness.LaunchAsync(request);

        // The stop is the program's thread's: the first thread of the list that is not the stopping one, with its stack.
        Assert.Equal("stopped Entry epoch=1 thread=1 loc=null hits=[] desc=Stopped at program entry", await harness.WaitForEventAsync("stopped"));
        Assert.Equal(DebugState.Stopped, harness.Session.State);
        Assert.Equal(1, harness.Session.CurrentStop!.ThreadId);
        Assert.Equal(2, harness.SentRequests("stackTrace").Count);
        var listed = await harness.Session.GetThreadsAsync(Ct).WaitAsync(Safety, Ct);
        Assert.Equal(1, Assert.Single(listed, t => t.IsCurrent).Id);
        Assert.Equal(threadOrder.Split(", ").Select(int.Parse), listed.Select(t => t.Id));

        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        harness.AssertScenarioComplete();
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "1")]
    [InlineData(false, "1, 2")]
    [Trait("Req", "DBG-554")]
    public async Task EntryStop_OneThread_Unchanged(bool terminalMode, string threadIds)
    {
        // One thread only: the stop stays where the adapter reported it, and its stack is asked for once. So it does
        // for a program the adapter started itself (captured mode), whatever threads there are: it stops at its entry
        // on its own thread, and that stack has no code of the user either.
        var entry = EntryStop.Replace("\"threadId\": 2", "\"threadId\": 1", StringComparison.Ordinal);
        var threads = string.Join(", ", threadIds.Split(", ").Select(id => $$"""{ "id": {{id}}, "name": "Thread {{id}}" }"""));
        var steps = $$"""{{entry}}, { "on": "threads", "respond": { "threads": [ {{threads}} ] } }, {{ProgramStack(1)}}""";
        await using var harness = SessionHarness.Create(terminalMode
            ? SessionTerminalTests.Scenario(more: ", " + steps)
            : "{ \"steps\": [ " + steps + " ] }");
        var request = SessionTerminalTests.Request(terminalMode ? new FakeTerminal() : null) with { StopOnEntry = true };

        await harness.LaunchAsync(request);

        Assert.Equal("stopped Entry epoch=1 thread=1 loc=null hits=[] desc=Stopped at program entry", await harness.WaitForEventAsync("stopped"));
        Assert.Single(harness.SentRequests("stackTrace"));
        Assert.Equal(1, Assert.Single(await harness.Session.GetThreadsAsync(Ct).WaitAsync(Safety, Ct), t => t.IsCurrent).Id);

        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        harness.AssertScenarioComplete();
    }
}
