using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>Stop pipeline and execution control (architecture §7, DBG-015, DBG-017; DAP usage §5.1, §6).</summary>
[Trait("Category", "Integration")]
public sealed class SessionStopTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Demo = @"C:\work\DbgDemo\DbgDemo.pas";

    private const string Threads = """{ "on": "threads", "respond": { "threads": [ { "id": 7412, "name": "main thread" }, { "id": 8120, "name": "Thread 2" } ] } }""";

    private static string Stack(int line, int threadId = 7412) => $$"""
        { "on": "stackTrace", "expectArguments": { "threadId": {{threadId}}, "startFrame": 0, "levels": 20 },
          "respond": { "totalFrames": 2, "stackFrames": [
            { "id": 524288, "name": "Factorial", "line": {{line}}, "column": 5, "source": { "name": "DbgDemo.pas", "path": "c:\\work\\DbgDemo\\DbgDemo.pas" } },
            { "id": 524289, "name": "DbgDemo", "line": 69, "column": 3, "source": { "name": "DbgDemo.pas", "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } } ] } }
        """;

    private static string Stopped(string body) => $$"""{ "event": "stopped", "body": {{body}} }""";

    /// <summary>One breakpoint (IDE id 5, adapter id 2) on line 40; the debuggee stops there right after configuration.</summary>
    private static string BreakpointAtStart(string stoppedBody = """{ "reason": "breakpoint", "description": "breakpoint 2.1", "threadId": 7412, "allThreadsStopped": true, "hitBreakpointIds": [2] }""") => $$"""
        { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 2, "verified": true, "line": 40 } ] } },
        { "on": "configurationDone", "respond": {}, "thenEvents": [ {{Stopped(stoppedBody)}} ] },
        {{Threads}},
        {{Stack(40)}}
        """;

    private static LaunchRequest LaunchWithBreakpoint(bool stopOnEntry = false) => new()
    {
        Program = SessionHarness.Program,
        StopOnEntry = stopOnEntry,
        Breakpoints = [new SourceBreakpoints(Demo, [new BreakpointSpec { Id = 5, Line = 40 }])],
    };

    private static async Task<SessionHarness> StoppedAtBreakpointAsync(string moreSteps)
    {
        var harness = SessionHarness.Create($$"""{ "steps": [ {{BreakpointAtStart()}}{{(moreSteps.Length > 0 ? ", " + moreSteps : "")}} ] }""");
        await harness.LaunchAsync(LaunchWithBreakpoint());
        await harness.WaitForEventAsync("stopped");
        return harness;
    }

    private static async Task EndAsync(SessionHarness harness)
    {
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");   // the last event: everything before it has been delivered
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-015")]
    public async Task Breakpoint_Stop_Published()
    {
        await using var harness = SessionHarness.Create($$"""{ "steps": [ {{BreakpointAtStart()}} ] }""");
        var raised = new TaskCompletionSource<StopInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Session.Stopped += (_, stop) => raised.TrySetResult(stop);

        await harness.LaunchAsync(LaunchWithBreakpoint());
        var line = await harness.WaitForEventAsync("stopped");

        // The adapter's breakpoint id 2 is reported as the IDE's id 5; the path is normalised (drive letter upper-case).
        Assert.Equal($"stopped Breakpoint epoch=1 thread=7412 loc={Demo}:40:5 hits=[5] desc=Breakpoint at DbgDemo.pas:40", line);
        Assert.Equal(DebugState.Stopped, harness.Session.State);
        var stop = harness.Session.CurrentStop;
        Assert.NotNull(stop);
        Assert.Same(await raised.Task.WaitAsync(Safety, Ct), stop);
        Assert.Null(stop.Exception);
        // threads before stackTrace (the adapter needs the thread list first).
        Assert.Equal(
            ["initialize", "launch", "setBreakpoints", "setExceptionBreakpoints", "configurationDone", "threads", "stackTrace"],
            harness.SentCommands());
        Assert.EndsWith("\"command\":\"threads\"}", Assert.Single(harness.SentRequests("threads")));
        Assert.Equal("""{"threadId":7412,"startFrame":0,"levels":20}""", harness.SentArguments("stackTrace"));
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-071")]
    public async Task Breakpoint_Stop_WithoutIdsAndThread_MatchedByLocation_FirstThread()
    {
        await using var harness = SessionHarness.Create($$"""{ "steps": [ {{BreakpointAtStart("""{ "reason": "breakpoint" }""")}} ] }""");

        await harness.LaunchAsync(LaunchWithBreakpoint());

        Assert.Equal(
            $"stopped Breakpoint epoch=1 thread=7412 loc={Demo}:40:5 hits=[5] desc=Breakpoint at DbgDemo.pas:40",
            await harness.WaitForEventAsync("stopped"));
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-071")]
    public async Task Breakpoint_Stop_NoMatchingBreakpoint_IsPlainPause()
    {
        await using var harness = SessionHarness.Create($$"""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [ {{Stopped("""{ "reason": "breakpoint", "threadId": 7412 }""")}} ] },
                         {{Threads}}, {{Stack(40)}} ] }
            """);

        await harness.LaunchAsync();

        Assert.Equal($"stopped Pause epoch=1 thread=7412 loc={Demo}:40:5 hits=[] desc=Paused", await harness.WaitForEventAsync("stopped"));
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-055")]
    public async Task Entry_Stop()
    {
        await using var harness = SessionHarness.Create($$"""
            { "steps": [ { "on": "launch", "respond": {}, "expectArguments": { "stopOnEntry": true } },
                         { "on": "configurationDone", "respond": {}, "thenEvents": [ {{Stopped("""{ "reason": "entry", "threadId": 7412 }""")}} ] },
                         {{Threads}},
                         { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 1, "name": "ntdll.dll`LdrInitializeThunk", "line": 12, "column": 1,
                             "source": { "name": "ntdll.dll`LdrInitializeThunk", "sourceReference": 1001 }, "instructionPointerReference": "0x7FFB12345678" } ] } } ] }
            """);

        // With StopOnEntry the launch completes only when the entry stop has been published.
        await harness.LaunchAsync(new LaunchRequest { Program = SessionHarness.Program, StopOnEntry = true, TypeInfoPath = PdiFixture.DbgDemoPath });

        Assert.Equal(DebugState.Stopped, harness.Session.State);
        var stop = harness.Session.CurrentStop;
        Assert.NotNull(stop);
        Assert.Equal(StopReason.Entry, stop.Reason);
        Assert.Equal(7412, stop.ThreadId);
        Assert.Null(stop.Location);   // no frame has source
        Assert.Equal("Stopped at program entry", stop.Description);
        await harness.WaitForEventAsync("stopped");
        Assert.Equal(
            ["state Idle->Starting", "state Starting->Stopped", "stopped Entry epoch=1 thread=7412 loc=null hits=[] desc=Stopped at program entry"],
            harness.Events.Snapshot());
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-028")]
    public async Task StopDetailsFail_StillPublished()
    {
        await using var harness = SessionHarness.Create($$"""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [ {{Stopped("""{ "reason": "step", "threadId": 7412 }""")}} ] },
                         {{Threads}}, { "on": "stackTrace", "fail": "thread has no frames" } ] }
            """);

        await harness.LaunchAsync();

        // A stop is never lost.
        Assert.Equal(
            "stopped Pause epoch=1 thread=7412 loc=null hits=[] desc=Stopped (details unavailable: thread has no frames)",
            await harness.WaitForEventAsync("stopped"));
        Assert.Equal(DebugState.Stopped, harness.Session.State);
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-421")]
    [Trait("Req", "DBG-422")]
    public async Task Events_OrderAndState()
    {
        await using var harness = SessionHarness.Create($$"""
            { "steps": [ {{BreakpointAtStart()}},
              { "on": "continue", "respond": { "allThreadsContinued": true },
                "thenEvents": [ { "event": "continued", "body": { "threadId": 7412 } }, {{Stopped("""{ "reason": "step", "threadId": 7412 }""")}} ] },
              {{Threads}}, {{Stack(41)}},
              { "on": "continue", "respond": {}, "thenEvents": [ { "event": "exited", "body": { "exitCode": 0 } }, { "event": "terminated" } ] } ] }
            """);
        var session = harness.Session;
        var log = new Watch<string>();
        // The snapshots already hold the new values when a handler runs (DBG-422). They are recorded where the session
        // rests until the test acts (Stopped, Terminated); elsewhere the session may already have moved on.
        // Every handler runs on the dispatcher thread.
        string Snapshot() => $"State={session.State} stop={session.CurrentStop?.Epoch.ToString() ?? "null"}";
        string Where() => "on " + Thread.CurrentThread.Name;
        session.StateChanged += (_, e) => log.Add(
            $"state {e.OldState}->{e.NewState} [{(e.NewState is DebugState.Stopped or DebugState.Terminated ? Snapshot() + " " : "")}{Where()}]");
        session.Stopped += (_, e) => log.Add($"stopped {e.Reason} {e.Epoch} [{Snapshot()} {Where()}]");
        session.Continued += (_, _) => log.Add($"continued [{Where()}]");
        session.Terminated += (_, e) => log.Add($"terminated {e.Cause} [{Snapshot()} {Where()}]");

        await harness.LaunchAsync(LaunchWithBreakpoint());
        await log.WaitAsync(e => e.StartsWith("stopped Breakpoint 1", StringComparison.Ordinal));
        await session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        await log.WaitAsync(e => e.StartsWith("stopped Step 2", StringComparison.Ordinal));
        await session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        await log.WaitAsync(e => e.StartsWith("terminated", StringComparison.Ordinal));

        const string thread = "on FreexComp.Debugger.Events";
        Assert.Equal(
        [
            $"state Idle->Starting [{thread}]",
            $"state Starting->Running [{thread}]",
            $"state Running->Stopped [State=Stopped stop=1 {thread}]",
            $"stopped Breakpoint 1 [State=Stopped stop=1 {thread}]",
            $"state Stopped->Running [{thread}]",
            $"continued [{thread}]",
            // The adapter's own "continued" event is ignored (DBG-018).
            $"state Running->Stopped [State=Stopped stop=2 {thread}]",
            $"stopped Step 2 [State=Stopped stop=2 {thread}]",
            $"state Stopped->Running [{thread}]",
            $"continued [{thread}]",
            $"state Running->Terminating [{thread}]",
            $"state Terminating->Terminated [State=Terminated stop=null {thread}]",
            $"terminated Exited [State=Terminated stop=null {thread}]",
        ], log.Snapshot());
        harness.AssertScenarioComplete();
    }

    // ---- execution control (G-025) ----

    [Fact]
    [Trait("Req", "DBG-404")]
    public async Task Continue_Acknowledged_Running()
    {
        await using var harness = await StoppedAtBreakpointAsync(
            """{ "on": "continue", "respond": { "allThreadsContinued": true }, "expectArguments": { "threadId": 7412 } }""");

        await harness.Session.ContinueAsync(Ct).WaitAsync(Safety, Ct);

        // Completed on acknowledgement: the state is Running already.
        Assert.Equal(DebugState.Running, harness.Session.State);
        Assert.Null(harness.Session.CurrentStop);
        Assert.Equal("""{"threadId":7412}""", harness.SentArguments("continue"));
        await harness.WaitForEventAsync("continued");
        Assert.Equal(["state Stopped->Running", "continued"], harness.Events.Snapshot().TakeLast(2));
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-404")]
    public async Task Continue_Rejected_FaultsAndStaysStopped()
    {
        await using var harness = await StoppedAtBreakpointAsync("""{ "on": "continue", "fail": "process is busy" }""");
        var eventsBefore = harness.Events.Snapshot().Count;

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => harness.Session.ContinueAsync(Ct).WaitAsync(Safety, Ct));

        Assert.Equal(DebugErrorCode.RequestFailed, exception.Code);
        Assert.Equal("process is busy", exception.Message);
        Assert.Equal("continue", exception.DapCommand);
        Assert.Equal(DebugState.Stopped, harness.Session.State);
        Assert.NotNull(harness.Session.CurrentStop);
        await EndAsync(harness);
        Assert.Equal("state Stopped->Terminating", harness.Events.Snapshot()[eventsBefore]);
    }

    [Fact]
    [Trait("Req", "DBG-090")]
    [Trait("Req", "DBG-092")]
    public async Task Step_Requests_ExactJson()
    {
        // Each step is acknowledged (state Running) and ends in a new stop; the thread id is the caller's.
        await using var harness = await StoppedAtBreakpointAsync($$"""
            { "on": "next", "respond": {}, "thenEvents": [ {{Stopped("""{ "reason": "step", "threadId": 7412 }""")}} ] }, {{Threads}}, {{Stack(41)}},
            { "on": "stepIn", "respond": {}, "thenEvents": [ {{Stopped("""{ "reason": "step", "threadId": 8120 }""")}} ] }, {{Threads}}, {{Stack(12, 8120)}},
            { "on": "stepOut", "respond": {} }
            """);
        var session = harness.Session;

        await session.StepOverAsync(7412, Ct).WaitAsync(Safety, Ct);
        Assert.Equal($"stopped Step epoch=2 thread=7412 loc={Demo}:41:5 hits=[] desc=Step completed", await harness.WaitForEventAsync("stopped Step epoch=2"));
        await session.StepIntoAsync(8120, Ct).WaitAsync(Safety, Ct);
        Assert.Equal($"stopped Step epoch=3 thread=8120 loc={Demo}:12:5 hits=[] desc=Step completed", await harness.WaitForEventAsync("stopped Step epoch=3"));
        await session.StepOutAsync(8120, Ct).WaitAsync(Safety, Ct);
        Assert.Equal(DebugState.Running, session.State);

        // No singleThread, no granularity: only the thread id.
        Assert.Equal("""{"seq":8,"type":"request","command":"next","arguments":{"threadId":7412}}""", Assert.Single(harness.SentRequests("next")));
        Assert.Equal("""{"seq":11,"type":"request","command":"stepIn","arguments":{"threadId":8120}}""", Assert.Single(harness.SentRequests("stepIn")));
        Assert.Equal("""{"seq":14,"type":"request","command":"stepOut","arguments":{"threadId":8120}}""", Assert.Single(harness.SentRequests("stepOut")));
        await EndAsync(harness);
        Assert.Equal(3, harness.Events.Snapshot().Count(e => e == "continued"));
    }

    [Fact]
    [Trait("Req", "DBG-405")]
    public async Task Pause_WhileStopped_NoOp()
    {
        await using var harness = await StoppedAtBreakpointAsync("");
        var sent = harness.SentCommands().Count;
        var events = harness.Events.Snapshot().Count;

        await harness.Session.PauseAsync(Ct).WaitAsync(Safety, Ct);

        Assert.Equal(DebugState.Stopped, harness.Session.State);
        Assert.Equal(sent, harness.SentCommands().Count);
        Assert.Equal(events, harness.Events.Snapshot().Count);
        // The scenario has no "pause" step and fails on unexpected requests.
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-405")]
    [Trait("Req", "DBG-091")]
    public async Task Pause_WhileRunning_UsesFirstKnownThread_InterruptStopIsPause()
    {
        // The pinned adapter reports a pause as an "exception"/"interrupt" stop on an injected break-in thread.
        await using var harness = await StoppedAtBreakpointAsync($$"""
            { "on": "continue", "respond": {} },
            { "on": "pause", "respond": {}, "expectArguments": { "threadId": 7412 },
              "thenEvents": [ {{Stopped("""{ "reason": "exception", "description": "Interrupt", "threadId": 9999 }""")}} ] },
            { "on": "threads", "respond": { "threads": [ { "id": 9999, "name": "break-in" }, { "id": 7412, "name": "main thread" } ] } },
            {{Stack(55)}}
            """);
        await harness.Session.ContinueAsync(Ct).WaitAsync(Safety, Ct);

        await harness.Session.PauseAsync(Ct).WaitAsync(Safety, Ct);

        // Acknowledged; the stop arrives as an event, on the first thread that is not the break-in thread.
        Assert.Equal($"stopped Pause epoch=2 thread=7412 loc={Demo}:55:5 hits=[] desc=Paused", await harness.WaitForEventAsync("stopped Pause"));
        Assert.Equal("""{"threadId":7412}""", harness.SentArguments("pause"));
        Assert.Null(harness.Session.CurrentStop!.Exception);
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-091")]
    public async Task Pause_WhileRunning_NoThreadKnown_RequestsThreadsFirst()
    {
        await using var harness = SessionHarness.Create($$"""
            { "steps": [ { "on": "threads", "respond": { "threads": [ { "id": 31, "name": "" } ] } },
                         { "on": "pause", "respond": {}, "expectArguments": { "threadId": 31 } } ] }
            """);
        await harness.LaunchAsync();

        await harness.Session.PauseAsync(Ct).WaitAsync(Safety, Ct);

        Assert.Equal(DebugState.Running, harness.Session.State);   // until the stop arrives
        Assert.Equal(["threads", "pause"], harness.SentCommands().TakeLast(2));
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-401")]
    public async Task Step_WhileRunning_InvalidState_NothingSent()
    {
        await using var harness = SessionHarness.Create("{}");
        await harness.LaunchAsync();

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => harness.Session.StepOverAsync(1, Ct).WaitAsync(Safety, Ct));

        Assert.Equal(DebugErrorCode.InvalidState, exception.Code);
        Assert.Equal("StepOverAsync is not allowed in state Running.", exception.Message);
        await EndAsync(harness);
    }
}
