using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>An arrival at a breakpoint whose condition cannot be evaluated because the adapter fails, not the expression.</summary>
[Trait("Category", "Integration")]
public sealed class ConditionFaultTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Demo = DemoSession.Source;

    [Fact]
    [Trait("Req", "DBG-410")]
    [Trait("Req", "DBG-425")]
    public async Task Condition_RequestTimesOut_ArrivalIsCountedAndReported()
    {
        // The adapter never answers the request the condition needs. The stop is not lost (it is published without
        // details, DBG-028), and the arrival was counted: the IDE is told the new hit count.
        await using var harness = SessionHarness.Create(DemoSession.Scenario($$"""
            { "on": "continue", "respond": { "allThreadsContinued": true },
              "thenEvents": [ {{DemoSession.StoppedEvent("breakpoint", """, "hitBreakpointIds": [1]""")}} ] },
            {{DemoSession.Threads}}, {{DemoSession.Stack(69)}},
            { "on": "evaluate" }
            """,
            setup: """
                { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 69 } ] } },
                """));
        var session = harness.Session;
        await harness.LaunchAsync(new LaunchRequest
        {
            Program = SessionHarness.Program,
            TypeInfoPath = PdiFixture.DbgDemoPath,
            Exceptions = SessionHarness.NoLanguageExceptions,
            Breakpoints = [new SourceBreakpoints(Demo, [new BreakpointSpec { Id = 5, Line = 69, Condition = "Total = 0" }])],
        });
        await harness.WaitForEventAsync("stopped Step epoch=1");

        await session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForRequestAsync("evaluate");   // the request's timer exists before the request is written
        harness.Clock.Advance(TimeSpan.FromSeconds(10));
        var stop = await harness.Events.WaitAsync(e => e.Contains("epoch=2", StringComparison.Ordinal) || e.StartsWith("terminated", StringComparison.Ordinal));

        Assert.StartsWith("stopped Pause epoch=2 thread=1 loc=null hits=[] desc=Stopped (details unavailable: ", stop);
        var events = harness.Events.Snapshot().ToList();
        Assert.Contains("breakpoint 5 Verified line=69 hits=1 msg=null", events);
        Assert.True(events.IndexOf("breakpoint 5 Verified line=69 hits=1 msg=null") < events.IndexOf(stop));
        await DemoSession.EndAsync(harness);
    }
}
