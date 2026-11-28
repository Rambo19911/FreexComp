using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// Request-timeout policy (architecture §9: three consecutive timeouts end the session) and inspection calls that
/// race with a state change (DBG-026).
/// </summary>
[Trait("Category", "Integration")]
public sealed class SessionRobustnessTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Threads = """{ "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } }""";
    private const string Unanswered = """{ "on": "stackTrace" }""";
    private const string Answered = """{ "on": "stackTrace", "respond": { "stackFrames": [], "totalFrames": 0 } }""";
    private const string StepStop = """{ "event": "stopped", "body": { "reason": "step", "threadId": 1 } }""";
    private const string PauseStop = """{ "event": "stopped", "body": { "reason": "pause", "threadId": 1 } }""";

    private static string Scenario(string steps) => $$"""
        { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [ {{StepStop}} ] },
                     {{Threads}}, {{DemoSession.Stack(71)}}{{(steps.Length > 0 ? ", " + steps : "")}} ] }
        """;

    private static async Task<SessionHarness> StoppedAsync(string steps)
    {
        var harness = SessionHarness.Create(Scenario(steps));
        await harness.LaunchAsync();
        await harness.WaitForEventAsync("stopped");
        return harness;
    }

    /// <summary>An inspection call that the adapter never answers: faults when the session clock passes the request timeout.</summary>
    private static async Task<DebuggerException> TimeOutAsync(SessionHarness harness)
    {
        var waiting = harness.Clock.WhenTimerCreated(RequestTimeout);
        var call = harness.Session.GetStackTraceAsync(1, ct: Ct);
        await waiting.WaitAsync(Safety, Ct);
        harness.Clock.Advance(RequestTimeout);
        return await Assert.ThrowsAsync<DebuggerException>(() => call.WaitAsync(Safety, Ct));
    }

    [Fact]
    public async Task ThreeTimeouts_Terminates()
    {
        await using var harness = await StoppedAsync($"{Unanswered}, {Unanswered}, {Unanswered}");

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var timeout = await TimeOutAsync(harness);
            Assert.Equal(DebugErrorCode.RequestTimeout, timeout.Code);
            Assert.Equal("stackTrace", timeout.DapCommand);
            // The task faulted; the session stays usable.
            Assert.Equal(DebugState.Stopped, harness.Session.State);
        }

        Assert.Equal(DebugErrorCode.RequestTimeout, (await TimeOutAsync(harness)).Code);

        // The third consecutive timeout: the adapter is taken for dead.
        Assert.Equal("terminated AdapterCrashed null Debug adapter is not responding.", await harness.WaitForEventAsync("terminated"));
        Assert.Equal(DebugState.Terminated, harness.Session.State);
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
    }

    [Fact]
    public async Task TwoTimeouts_ThenAnswer_CountStartsAgain()
    {
        await using var harness = await StoppedAsync(
            $"{Unanswered}, {Unanswered}, {Answered}, {Unanswered}, {Unanswered}");

        await TimeOutAsync(harness);
        await TimeOutAsync(harness);
        Assert.Equal(0, (await harness.Session.GetStackTraceAsync(1, ct: Ct).WaitAsync(Safety, Ct)).TotalFrames);
        await TimeOutAsync(harness);
        await TimeOutAsync(harness);

        // Four timeouts, but never three in a row.
        Assert.Equal(DebugState.Stopped, harness.Session.State);
        await DemoSession.EndAsync(harness);
    }

    /// <summary>
    /// G-103: an adapter that is slow, not dead. The requests of a stop and of the views that follow it - thread
    /// list, call stack - each time out, and each is answered right after: four timeouts in a row, but an answer after
    /// every one. The stop is published all the same (DBG-028), the session stays stopped and usable, Continue and
    /// Stop work.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DBG-028")]
    public async Task LateAnswerToThreadsOrStackTraceDoesNotEndTheSession()
    {
        var name = "FreexComp-FakeAdapter-" + Guid.NewGuid().ToString("N");
        // After the late answer the adapter says so: once that event is here, the answer before it has been read.
        string Held(string command, int number, string body) => $$"""
            { "on": "{{command}}", "respondAfterEvent": "{{name}}-{{number}}", "respond": {{body}},
              "thenEvents": [ { "event": "output", "body": { "category": "console", "output": "late {{number}}" } } ] }
            """;
        const string ThreadList = """{ "threads": [ { "id": 1, "name": "main" } ] }""";
        const string NoFrames = """{ "stackFrames": [], "totalFrames": 0 }""";
        await using var harness = await StoppedAsync($$"""
            { "on": "continue", "respond": {} },
            { "on": "pause", "respond": {}, "thenEvents": [ {{PauseStop}} ] },
            {{Held("threads", 1, ThreadList)}}, {{Held("threads", 2, ThreadList)}},
            {{Held("stackTrace", 3, NoFrames)}}, {{Held("stackTrace", 4, NoFrames)}},
            {{DemoSession.Stack(72)}},
            { "on": "continue", "respond": {} }
            """);
        using var gates = new Gates(name, 4);   // disposed before the session: no adapter is left holding a request
        var session = harness.Session;

        // The request has been written (its time limit runs); the session's clock passes the limit; then the answer comes.
        async Task LateAsync(string command, int nth, int number, Func<Task>? call)
        {
            var seen = 0;
            var sent = harness.Trace.WaitAsync(e => e.Direction == DapDirection.ToAdapter
                && e.Json.Contains($"\"command\":\"{command}\"", StringComparison.Ordinal) && ++seen == nth);
            var pending = call?.Invoke();
            await sent;
            harness.Clock.Advance(RequestTimeout);
            if (pending is not null)
            {
                var timeout = await Assert.ThrowsAsync<DebuggerException>(() => pending.WaitAsync(Safety, Ct));
                Assert.Equal(DebugErrorCode.RequestTimeout, timeout.Code);
                Assert.Equal(command, timeout.DapCommand);
            }
            else
            {
                // The stop's own request: the stop is published without its details, it is never lost.
                Assert.Equal(
                    "stopped Pause epoch=2 thread=1 loc=null hits=[] desc=Stopped (details unavailable: Debug adapter did not answer 'threads' in time.)",
                    await harness.WaitForEventAsync("stopped Pause"));
            }

            gates.Open(number);
            await harness.WaitForEventAsync($"output Debugger \"late {number}\"");
            Assert.Equal(DebugState.Stopped, session.State);
        }

        await session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        await session.PauseAsync(Ct).WaitAsync(Safety, Ct);
        await LateAsync("threads", 2, 1, null);
        await LateAsync("threads", 3, 2, () => session.GetThreadsAsync(Ct));
        await LateAsync("stackTrace", 2, 3, () => session.GetStackTraceAsync(1, ct: Ct));
        await LateAsync("stackTrace", 3, 4, () => session.GetStackTraceAsync(1, ct: Ct));

        // Four timeouts in a row and the session is where it was: the next call is answered, Continue and Stop work.
        Assert.DoesNotContain(harness.Events.Snapshot(), e => e.StartsWith("terminated", StringComparison.Ordinal));
        Assert.Equal(2, (await session.GetStackTraceAsync(1, ct: Ct).WaitAsync(Safety, Ct)).TotalFrames);
        await session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        Assert.Equal(DebugState.Running, session.State);
        await session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        Assert.Equal("terminated TerminatedByUser null null", await harness.WaitForEventAsync("terminated"));
        harness.AssertScenarioComplete();
    }

    /// <summary>The operating-system events a scenario's <c>respondAfterEvent</c> steps wait for, one per step.</summary>
    private sealed class Gates : IDisposable
    {
        private readonly EventWaitHandle[] _gates;

        public Gates(string name, int count) =>
            _gates = [.. Enumerable.Range(1, count).Select(number => new EventWaitHandle(false, EventResetMode.ManualReset, $"{name}-{number}"))];

        public void Open(int number) => _gates[number - 1].Set();

        public void Dispose()
        {
            // Whatever the test did, no adapter is left waiting.
            foreach (var gate in _gates)
            {
                gate.Set();
                gate.Dispose();
            }
        }
    }

    [Fact]
    [Trait("Req", "DBG-026")]
    public async Task Inspection_AfterResume_Stale()
    {
        await using var harness = await StoppedAsync(
            $$"""
            { "on": "continue", "respond": {} },
            { "on": "pause", "respond": {}, "thenEvents": [ {{PauseStop}} ] }, {{Threads}}, {{DemoSession.Stack(72)}},
            {{Unanswered}}
            """);
        var session = harness.Session;

        // Queued behind the resume: when its turn comes the state has left Stopped. It faults with InvalidState and
        // nothing is sent for it (whichever of the two the loop saw first, the continue is processed first).
        var resume = session.ContinueAsync(Ct);
        var queued = session.GetStackTraceAsync(1, ct: Ct);
        await resume.WaitAsync(Safety, Ct);
        var invalid = await Assert.ThrowsAsync<DebuggerException>(() => queued.WaitAsync(Safety, Ct));
        Assert.Equal(DebugErrorCode.InvalidState, invalid.Code);
        // The next stop comes only now, on request: a stop that followed the resume by itself could be processed
        // before the queued call was even posted, which would then be an ordinary call in the new stop (G-083).
        await session.PauseAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("stopped Pause epoch=2");
        Assert.Equal(2, harness.SentRequests("stackTrace").Count);   // one per stop, none for the queued call

        // In flight when the state leaves Stopped: StaleReference.
        var seen = 0;
        var sent = harness.Trace.WaitAsync(e => e.Direction == DapDirection.ToAdapter
            && e.Json.Contains("\"command\":\"stackTrace\"", StringComparison.Ordinal) && ++seen == 3);
        var inFlight = session.GetStackTraceAsync(1, ct: Ct);
        await sent;
        await session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        var stale = await Assert.ThrowsAsync<DebuggerException>(() => inFlight.WaitAsync(Safety, Ct));
        Assert.Equal(DebugErrorCode.StaleReference, stale.Code);
        Assert.Equal(DebugState.Terminated, session.State);
    }
}
