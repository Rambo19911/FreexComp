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

    [Fact]
    [Trait("Req", "DBG-026")]
    public async Task Inspection_AfterResume_Stale()
    {
        await using var harness = await StoppedAsync(
            $$"""
            { "on": "continue", "respond": {}, "thenEvents": [ {{StepStop}} ] }, {{Threads}}, {{DemoSession.Stack(72)}},
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
        await harness.WaitForEventAsync("stopped Step epoch=2");
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
