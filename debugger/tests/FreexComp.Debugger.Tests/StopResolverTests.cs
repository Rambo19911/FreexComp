using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>Internal stops while a step is in progress (architecture DBG-030; DAP usage DBG-093).</summary>
[Trait("Category", "Integration")]
[Trait("Req", "DBG-030")]
public sealed class StopResolverTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Demo = DemoSession.Source;

    // A conditional breakpoint in Factorial (IDE id 7, adapter id 3) whose condition is false.
    private const string SetBreakpoint = """{ "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 3, "verified": true, "line": 40 } ] } },""";

    private static LaunchRequest Launch => new()
    {
        Program = SessionHarness.Program,
        TypeInfoPath = PdiFixture.DbgDemoPath,
        Breakpoints = [new SourceBreakpoints(Demo, [new BreakpointSpec { Id = 7, Line = 40, Condition = "1 = 2" }])],
    };

    private static string Hit(int threadId = 1) =>
        $$"""{ "event": "stopped", "body": { "reason": "breakpoint", "threadId": {{threadId}}, "hitBreakpointIds": [3] } }""";

    /// <summary>The stack inside Factorial, called from Run: <paramref name="totalFrames"/> frames deep.</summary>
    private static string FactorialStack(int totalFrames) => $$"""
        { "on": "stackTrace", "respond": { "totalFrames": {{totalFrames}}, "stackFrames": [
            { "id": 600, "name": "Factorial", "line": 40, "column": 5, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } },
            { "id": 524288, "name": "Run", "line": 69, "column": 3, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } } ] } }
        """;

    private static async Task<SessionHarness> StoppedAsync(string steps)
    {
        var harness = SessionHarness.Create(DemoSession.Scenario(steps, setup: SetBreakpoint));
        await harness.LaunchAsync(Launch);
        await harness.WaitForEventAsync("stopped Step epoch=1");
        return harness;
    }

    [Theory]
    [InlineData("next")]
    [InlineData("stepIn")]
    [InlineData("stepOut")]
    public async Task SkippedBreakpoint_DuringStep_ReissuesStep(string command)
    {
        // The step started 2 frames deep; the breakpoint is hit 3 deep on the stepping thread: the same step again.
        await using var harness = await StoppedAsync($$"""
            { "on": "{{command}}", "expectArguments": { "threadId": 1 }, "respond": {}, "thenEvents": [ {{Hit()}} ] },
            {{DemoSession.Threads}}, {{FactorialStack(3)}},
            { "on": "{{command}}", "expectArguments": { "threadId": 1 }, "respond": {}, "thenEvents": [ {{DemoSession.StoppedEvent("step")}} ] },
            {{DemoSession.Threads}}, {{DemoSession.Stack(72)}}
            """);
        var session = harness.Session;
        var before = harness.SentCommands().Count;

        await (command switch
        {
            "next" => session.StepOverAsync(1, Ct),
            "stepIn" => session.StepIntoAsync(1, Ct),
            _ => session.StepOutAsync(1, Ct),
        }).WaitAsync(Safety, Ct);
        var stop = await harness.WaitForEventAsync("stopped Step epoch=2");

        // One step for the user: the skipped breakpoint did not turn it into a free run and raised no public events.
        Assert.Equal($"stopped Step epoch=2 thread=1 loc={Demo}:72:3 hits=[] desc=Step completed", stop);
        Assert.Equal(
            [command, "threads", "stackTrace", command, "threads", "stackTrace"],
            harness.SentCommands().Skip(before));
        Assert.DoesNotContain("continue", harness.SentCommands());
        Assert.Equal(
            ["state Stopped->Running", "continued", "breakpoint 7 Verified line=40 hits=1 msg=null", "state Running->Stopped", stop],
            harness.Events.Snapshot().SkipWhile(e => e != "state Stopped->Running"));
        Assert.Equal(1, ((IDebugSessionDiagnostics)session).InternalStopCount);
        await DemoSession.EndAsync(harness);
    }

    [Theory]
    [InlineData(1, 1)]     // shallower than where the step started: the stepped routine has returned
    [InlineData(2, 3)]     // another thread hit the breakpoint
    public async Task SkippedBreakpoint_ShallowerOrOtherThread_Continues(int threadId, int totalFrames)
    {
        await using var harness = await StoppedAsync($$"""
            { "on": "next", "respond": {}, "thenEvents": [ {{Hit(threadId)}} ] },
            { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" }, { "id": 2, "name": "worker" } ] } },
            {{FactorialStack(totalFrames)}},
            { "on": "continue", "expectArguments": { "threadId": {{threadId}} }, "respond": { "allThreadsContinued": true } }
            """);

        await harness.Session.StepOverAsync(1, Ct).WaitAsync(Safety, Ct);
        await harness.WaitForRequestAsync("continue");
        await harness.WaitForResponseAsync("continue");

        Assert.Single(harness.SentRequests("next"));
        Assert.Equal(DebugState.Running, harness.Session.State);
        await DemoSession.EndAsync(harness);
        Assert.DoesNotContain(harness.Events.Snapshot(), e => e.StartsWith("stopped Step epoch=2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SkippedBreakpoint_AfterContinue_Continues()
    {
        // No step in progress: the internal resume is a plain continue.
        await using var harness = await StoppedAsync($$"""
            { "on": "continue", "respond": { "allThreadsContinued": true }, "thenEvents": [ {{Hit()}} ] },
            {{DemoSession.Threads}}, {{FactorialStack(3)}},
            { "on": "continue", "respond": { "allThreadsContinued": true } }
            """);

        await harness.Session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("breakpoint 7 Verified line=40 hits=1");
        await harness.WaitForResponsesAsync("continue", 2);

        await DemoSession.EndAsync(harness);
        Assert.Equal(2, harness.SentRequests("continue").Count);
        Assert.DoesNotContain("next", harness.SentCommands());
    }
}
