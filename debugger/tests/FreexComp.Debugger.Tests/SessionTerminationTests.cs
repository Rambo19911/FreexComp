using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>Termination, natural exit and adapter crashes (architecture DBG-019 … DBG-023; DAP usage §7).</summary>
[Trait("Category", "Integration")]
public sealed class SessionTerminationTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Stops at a breakpoint as soon as configuration is done.</summary>
    internal const string StopAtStart = """
        { "on": "configurationDone", "respond": {},
          "thenEvents": [ { "event": "stopped", "body": { "reason": "breakpoint", "threadId": 1 } } ] },
        { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
        { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 100, "name": "Run", "line": 69, "column": 3,
            "source": { "path": "C:\\t\\DbgDemo.pas" } } ], "totalFrames": 1 } }
        """;

    [Theory]
    [InlineData(DebugState.Idle)]
    [InlineData(DebugState.Starting)]
    [InlineData(DebugState.Running)]
    [InlineData(DebugState.Stopped)]
    [InlineData(DebugState.Terminated)]
    [Trait("Req", "DBG-406")]
    public async Task Terminate_FromEachState(DebugState state)
    {
        var scenario = state switch
        {
            // Neither the initialized event nor the launch response ever arrives: the session stays in Starting.
            DebugState.Starting => """{ "initializedAfter": "never", "steps": [ { "on": "launch" } ] }""",
            DebugState.Stopped => $$"""{ "steps": [ {{StopAtStart}} ] }""",
            DebugState.Terminated => """
                { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [ { "event": "terminated" } ] } ] }
                """,
            _ => "{}",
        };
        await using var harness = SessionHarness.Create(scenario);
        Task? launch = null;
        switch (state)
        {
            case DebugState.Idle:
                break;
            case DebugState.Starting:
                launch = harness.LaunchAsync();
                await harness.WaitForRequestAsync("launch");
                await harness.WaitForEventAsync("state Idle->Starting");
                break;
            case DebugState.Stopped:
                await harness.LaunchAsync();
                await harness.WaitForEventAsync("stopped");
                break;
            case DebugState.Terminated:
                await harness.LaunchAsync();
                await harness.WaitForEventAsync("terminated");
                break;
            default:
                await harness.LaunchAsync();
                await harness.WaitForEventAsync("state Starting->Running");
                break;
        }

        // Every event raised so far has been delivered (each case waited for the last one).
        Assert.Equal(state, harness.Session.State);
        var eventsBefore = harness.Events.Snapshot().Count;

        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);

        Assert.Equal(DebugState.Terminated, harness.Session.State);
        Assert.Null(harness.Session.CurrentStop);
        // Completes after the adapter process has exited.
        if (harness.AdapterProcessId is { } adapter)
            Assert.False(SessionHarness.IsAlive(adapter));
        var events = harness.Events.Snapshot().Skip(eventsBefore).ToList();
        switch (state)
        {
            case DebugState.Idle:
                await harness.WaitForEventAsync("terminated");
                Assert.Equal(["state Idle->Terminated", "terminated TerminatedByUser null null"], harness.Events.Snapshot());
                Assert.Null(harness.AdapterProcessId);
                break;
            case DebugState.Terminated:
                Assert.Empty(events);   // no-op
                break;
            default:
                await harness.WaitForEventAsync("terminated");
                Assert.Equal(
                    [$"state {state}->Terminating", "state Terminating->Terminated", "terminated TerminatedByUser null null"],
                    harness.Events.Snapshot().Skip(eventsBefore));
                Assert.Equal("""{}""", harness.SentArguments("disconnect"));   // the default capabilities lack supportTerminateDebuggee
                break;
        }

        if (launch is not null)
        {
            var exception = await Assert.ThrowsAsync<DebuggerException>(() => launch);
            Assert.Equal(DebugErrorCode.LaunchFailed, exception.Code);
            Assert.Equal("The debug session was terminated.", exception.Message);
        }
    }

    [Fact]
    [Trait("Req", "DBG-094")]
    public async Task Terminate_WithTerminateDebuggeeCapability_SendsTerminateDebuggee()
    {
        await using var harness = SessionHarness.Create("""
            { "capabilities": { "supportsConfigurationDoneRequest": true, "supportsReadMemoryRequest": true, "supportTerminateDebuggee": true },
              "steps": [ { "on": "disconnect", "respond": {}, "expectArguments": { "terminateDebuggee": true }, "thenExit": 0 } ] }
            """);
        await harness.LaunchAsync();

        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);

        Assert.Equal("""{"terminateDebuggee":true}""", harness.SentArguments("disconnect"));
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-026")]
    public async Task Terminate_WhileRequestInFlight_AbortsIt()
    {
        // The second stackTrace request is never answered; terminating does not wait for its timeout.
        await using var harness = SessionHarness.Create($$"""{ "steps": [ {{StopAtStart}}, { "on": "stackTrace" } ] }""");
        await harness.LaunchAsync();
        await harness.WaitForEventAsync("stopped");
        var inspection = harness.Session.GetStackTraceAsync(1, ct: Ct);
        await harness.Trace.WaitAsync(e => e.Direction == DapDirection.ToAdapter && e.Json.Contains("\"levels\":200", StringComparison.Ordinal));

        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => inspection.WaitAsync(Safety, Ct));
        Assert.Equal(DebugErrorCode.StaleReference, exception.Code);
        Assert.Equal(DebugState.Terminated, harness.Session.State);
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-406")]
    public async Task Terminate_Idempotent()
    {
        await using var harness = SessionHarness.Create("{}");
        await harness.LaunchAsync();

        var first = harness.Session.TerminateAsync(Ct);
        var second = harness.Session.TerminateAsync(Ct);
        await first.WaitAsync(Safety, Ct);
        var third = harness.Session.TerminateAsync(Ct);
        await harness.WaitForEventAsync("terminated");

        Assert.Same(first, second);
        Assert.Same(first, third);
        Assert.True(third.IsCompletedSuccessfully);
        Assert.Single(harness.SentRequests("disconnect"));
        Assert.Single(harness.Events.Snapshot(), e => e.StartsWith("terminated", StringComparison.Ordinal));
        Assert.Equal("terminated TerminatedByUser null null", harness.Events.Snapshot()[^1]);
    }

    [Fact]
    [Trait("Req", "DBG-020")]
    public async Task AdapterIgnoresDisconnect_Killed()
    {
        // The adapter neither answers disconnect nor reacts to its input being closed.
        await using var harness = SessionHarness.Create("""{ "steps": [ { "on": "disconnect", "hang": true } ] }""", manualClock: true);
        await harness.LaunchAsync();
        var adapter = Assert.NotNull(harness.AdapterProcessId);
        var disconnectWait = harness.Clock!.WhenTimerCreated(TimeSpan.FromSeconds(3));
        var exitWait = harness.Clock.WhenTimerCreated(TimeSpan.FromSeconds(2));

        var terminate = harness.Session.TerminateAsync(Ct);
        await disconnectWait.WaitAsync(Safety, Ct);
        Assert.Equal(DebugState.Terminating, harness.Session.State);
        Assert.False(exitWait.IsCompleted);
        harness.Clock.Advance(TimeSpan.FromSeconds(3));    // disconnect timeout: ignored
        await exitWait.WaitAsync(Safety, Ct);
        Assert.True(SessionHarness.IsAlive(adapter));
        Assert.False(terminate.IsCompleted);
        harness.Clock.Advance(TimeSpan.FromSeconds(2));    // exit wait over: the process tree is killed

        await terminate.WaitAsync(Safety, Ct);
        Assert.False(SessionHarness.IsAlive(adapter));
        Assert.Equal(DebugState.Terminated, harness.Session.State);
        await harness.WaitForEventAsync("terminated");
        Assert.Equal("terminated TerminatedByUser null null", harness.Events.Snapshot()[^1]);
    }

    [Fact]
    [Trait("Req", "DBG-407")]
    public async Task Dispose_ThenCalls_Throw()
    {
        await using var harness = SessionHarness.Create("{}");
        await harness.LaunchAsync();
        var adapter = Assert.NotNull(harness.AdapterProcessId);
        var session = harness.Session;

        await session.DisposeAsync().AsTask().WaitAsync(Safety, Ct);

        // Everything was delivered before DisposeAsync returned; the adapter is gone.
        Assert.Equal(
        [
            "state Idle->Starting", "state Starting->Running", "state Running->Terminating", "state Terminating->Terminated",
            "terminated Disposed null null",
        ], harness.Events.Snapshot());
        Assert.False(SessionHarness.IsAlive(adapter));
        Assert.Equal(DebugState.Terminated, session.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.LaunchAsync(new LaunchRequest { Program = SessionHarness.Program }, Ct));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.TerminateAsync(Ct));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ContinueAsync(Ct));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.PauseAsync(Ct));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.StepOverAsync(1, Ct));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SetBreakpointsAsync(@"C:\t\a.pas", [], Ct));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.GetThreadsAsync(Ct));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.GetStackTraceAsync(1, ct: Ct));
        await session.DisposeAsync();   // idempotent
        Assert.Equal(5, harness.Events.Snapshot().Count);
    }

    // ---- natural exit (G-020) ----

    [Fact]
    [Trait("Req", "DBG-426")]
    public async Task Exit_ReportsCode()
    {
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "configurationDone", "respond": {},
                "thenEvents": [ { "event": "exited", "body": { "exitCode": 3 } }, { "event": "terminated" } ] } ] }
            """);

        await harness.LaunchAsync();
        await harness.WaitForEventAsync("terminated");

        Assert.Equal(DebugState.Terminated, harness.Session.State);
        Assert.Equal(3, harness.Session.ExitCode);
        Assert.Equal(
        [
            "state Idle->Starting", "state Starting->Running", "state Running->Terminating", "state Terminating->Terminated",
            "terminated Exited 3 null",
        ], harness.Events.Snapshot());
        // The adapter is still asked to end, without terminateDebuggee (DBG-095); then its process is gone.
        Assert.Equal("{}", harness.SentArguments("disconnect"));
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-426")]
    public async Task Exit_CodeAboveInt32_ReportedAsSignedValue()
    {
        // An unhandled access violation ends the process with 0xC0000005.
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "configurationDone", "respond": {},
                "thenEvents": [ { "event": "exited", "body": { "exitCode": 3221225477 } }, { "event": "terminated" } ] } ] }
            """);

        await harness.LaunchAsync();
        await harness.WaitForEventAsync("terminated");

        Assert.Equal(unchecked((int)0xC0000005), harness.Session.ExitCode);
        Assert.Equal("terminated Exited -1073741819 null", harness.Events.Snapshot()[^1]);
    }

    [Fact]
    [Trait("Req", "DBG-096")]
    public async Task Terminated_WithoutExited_NullCode()
    {
        await using var harness = SessionHarness.Create("""
            { "capabilities": { "supportsConfigurationDoneRequest": true, "supportsReadMemoryRequest": true, "supportTerminateDebuggee": true },
              "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [ { "event": "terminated" } ] } ] }
            """);

        await harness.LaunchAsync();
        await harness.WaitForEventAsync("terminated");

        Assert.Null(harness.Session.ExitCode);
        Assert.Equal("terminated Exited null null", harness.Events.Snapshot()[^1]);
        Assert.Equal("{}", harness.SentArguments("disconnect"));   // never terminateDebuggee after a natural end
    }

    // ---- adapter crash (G-021) ----

    [Fact]
    [Trait("Req", "DBG-023")]
    public async Task Crash_WhileRunning_AdapterCrashed()
    {
        await using var harness = SessionHarness.Create("""{ "steps": [ { "on": "configurationDone", "respond": {}, "thenExit": 7 } ] }""");

        await harness.LaunchAsync();
        var terminated = await harness.WaitForEventAsync("terminated");

        // Exit code and the adapter's last stderr lines are part of the message (DBG-006).
        Assert.Equal("terminated AdapterCrashed null Debug adapter exited unexpectedly (exit code 7).\nSCENARIO-COMPLETE", terminated);
        Assert.Equal(
            ["state Idle->Starting", "state Starting->Running", "state Running->Terminated", terminated],
            harness.Events.Snapshot());
        Assert.Equal(DebugState.Terminated, harness.Session.State);
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
        Assert.DoesNotContain("disconnect", harness.SentCommands());
    }

    [Fact]
    [Trait("Req", "DBG-023")]
    public async Task Crash_WhileStopped_PendingRequestFails_AdapterCrashed()
    {
        await using var harness = SessionHarness.Create($$"""
            { "steps": [ {{StopAtStart}}, { "on": "continue", "thenExit": 9 } ] }
            """);
        await harness.LaunchAsync();
        await harness.WaitForEventAsync("stopped");

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => harness.Session.ContinueAsync(Ct).WaitAsync(Safety, Ct));

        Assert.Equal(DebugErrorCode.AdapterCrashed, exception.Code);
        var terminated = await harness.WaitForEventAsync("terminated");
        Assert.StartsWith("terminated AdapterCrashed null Debug adapter exited unexpectedly (exit code 9).", terminated);
        Assert.Equal("state Stopped->Terminated", harness.Events.Snapshot()[^2]);
        Assert.Null(harness.Session.CurrentStop);
    }

    [Fact]
    [Trait("Req", "DBG-023")]
    public async Task Crash_WhileStarting_LaunchFailed()
    {
        await using var harness = SessionHarness.Create("""{ "initializedAfter": "never", "steps": [ { "on": "launch", "thenExit": 5 } ] }""");

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => harness.LaunchAsync());

        Assert.Equal(DebugErrorCode.LaunchFailed, exception.Code);
        Assert.Equal("Debug adapter exited unexpectedly (exit code 5).\nSCENARIO-COMPLETE", exception.Message);
        Assert.Equal(
            ["state Idle->Starting", "state Starting->Terminated", "terminated LaunchFailed null " + exception.Message],
            harness.Events.Snapshot());
    }

    [Fact]
    [Trait("Req", "DBG-007")]
    public async Task Crash_BeforeInitializeResponse_AdapterStartFailed()
    {
        // Helper mode of the test adapter: three stderr lines, then exit code 0 without serving anything.
        await using var harness = SessionHarness.Create("{}", configure: o => o with { AdapterArguments = ["--stderr-lines", "3"] });

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => harness.LaunchAsync());

        Assert.Equal(DebugErrorCode.AdapterStartFailed, exception.Code);
        Assert.Equal("Debug adapter could not be started: it exited with code 0.\nline 1\nline 2\nline 3", exception.Message);
        Assert.Equal("terminated LaunchFailed null " + exception.Message, harness.Events.Snapshot()[^1]);
        Assert.Equal(DebugState.Terminated, harness.Session.State);
    }

    [Fact]
    [Trait("Req", "DBG-007")]
    public async Task Crash_BeforeInitializeResponse_MissingPythonRuntime_PointsToQuestions()
    {
        // The test adapter reports the unreadable scenario path on stderr and exits: the text names "python" and ".dll".
        var missing = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "python311.dll." + Guid.NewGuid().ToString("N"));
        await using var harness = SessionHarness.Create("{}", configure: o => o with { AdapterArguments = [missing] });

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => harness.LaunchAsync());

        Assert.Equal(DebugErrorCode.AdapterStartFailed, exception.Code);
        Assert.StartsWith("Debug adapter could not be started: it exited with code 2.\nSCENARIO-ERROR: ", exception.Message);
        Assert.EndsWith("See docs/QUESTIONS.md: lldb-dap requires a Python runtime that is not installed.", exception.Message);
    }

    /// <summary>
    /// G-075: the stderr reader of an adapter that exits by itself may have failed by the time the session collects
    /// what the adapter left behind (it got its first turn after the process object was disposed). The session must
    /// end all the same, in every state: here the reader is replaced by one that has already failed.
    /// </summary>
    [Theory]
    [InlineData("exit 0 before initialize")]
    [InlineData("exit 2 before initialize")]
    [InlineData("starting")]
    [InlineData("running")]
    [Trait("Req", "DBG-007")]
    [Trait("Req", "DBG-023")]
    public async Task Crash_StderrReaderFailed_SessionStillEnds(string when)
    {
        var missing = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "scenario." + Guid.NewGuid().ToString("N"));
        var scenario = when switch
        {
            "starting" => """{ "initializedAfter": "never", "steps": [ { "on": "launch", "thenExit": 5 } ] }""",
            "running" => """{ "steps": [ { "on": "configurationDone", "respond": {}, "thenExit": 7 } ] }""",
            _ => "{}",
        };
        await using var harness = SessionHarness.Create(scenario, configure: o => when switch
        {
            "exit 0 before initialize" => o with { AdapterArguments = ["--stderr-lines", "3"] },
            "exit 2 before initialize" => o with { AdapterArguments = [missing] },
            _ => o,
        });
        harness.Session.StderrReaderStarter = _ => Task.FromException(new ObjectDisposedException("System.Diagnostics.Process"));

        // No stderr line was read: the messages carry the exit code only.
        if (when == "running")
        {
            await harness.LaunchAsync();
            var terminated = await harness.WaitForEventAsync("terminated");

            Assert.Equal("terminated AdapterCrashed null Debug adapter exited unexpectedly (exit code 7).", terminated);
            Assert.Equal(
                ["state Idle->Starting", "state Starting->Running", "state Running->Terminated", terminated],
                harness.Events.Snapshot());
        }
        else
        {
            var exception = await Assert.ThrowsAsync<DebuggerException>(() => harness.LaunchAsync());

            var (code, message) = when switch
            {
                "exit 0 before initialize" => (DebugErrorCode.AdapterStartFailed, "Debug adapter could not be started: it exited with code 0."),
                "exit 2 before initialize" => (DebugErrorCode.AdapterStartFailed, "Debug adapter could not be started: it exited with code 2."),
                _ => (DebugErrorCode.LaunchFailed, "Debug adapter exited unexpectedly (exit code 5)."),
            };
            Assert.Equal(code, exception.Code);
            Assert.Equal(message, exception.Message);
            Assert.Equal(
                ["state Idle->Starting", "state Starting->Terminated", "terminated LaunchFailed null " + message],
                harness.Events.Snapshot());
        }

        Assert.Equal(DebugState.Terminated, harness.Session.State);
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
    }

    [Fact]
    [Trait("Req", "DBG-023")]
    public async Task MalformedJson_ProtocolError()
    {
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenRaw": "{ this is not JSON" } ] }
            """);

        await harness.LaunchAsync();
        var terminated = await harness.WaitForEventAsync("terminated");

        Assert.StartsWith("terminated AdapterCrashed null Protocol error: malformed JSON: ", terminated);
        Assert.Equal(["state Idle->Starting", "state Starting->Running", "state Running->Terminated", terminated], harness.Events.Snapshot());
        // The adapter was still alive: its process tree is killed.
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
    }
}
