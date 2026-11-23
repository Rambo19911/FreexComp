using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>Breakpoint table and the <c>setBreakpoints</c> traffic (DAP usage §4.1: DBG-060 … DBG-066; debug-session DBG-409, DBG-410).</summary>
[Trait("Category", "Integration")]
public sealed class BreakpointTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Demo = @"C:\work\DbgDemo\DbgDemo.pas";

    private static BreakpointSpec Bp(int id, int line, bool enabled = true) => new() { Id = id, Line = line, Enabled = enabled };

    private static LaunchRequest Launch(params SourceBreakpoints[] breakpoints) =>
        new() { Program = SessionHarness.Program, Breakpoints = breakpoints };

    private static async Task<List<string>> StatusesAsync(Task<IReadOnlyList<BreakpointStatus>> call) =>
        [.. (await call.WaitAsync(Safety, Ct)).Select(SessionHarness.Status)];

    private static async Task EndAsync(SessionHarness harness)
    {
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");   // the last event: everything before it has been delivered
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-054")]
    public async Task Launch_SendsPerSource_Sorted()
    {
        await using var harness = SessionHarness.Create("""
            { "steps": [
              { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 3 }, { "id": 2, "verified": true, "line": 9 } ] },
                "expectArguments": { "source": { "name": "Alpha.pas", "path": "C:\\work\\Alpha.pas" }, "breakpoints": [ { "line": 3 }, { "line": 9 } ], "sourceModified": false } },
              { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 3, "verified": true, "line": 5 } ] },
                "expectArguments": { "source": { "name": "beta.pas", "path": "C:\\work\\beta.pas" }, "breakpoints": [ { "line": 5 } ] } },
              { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 4, "verified": true, "line": 7 } ] },
                "expectArguments": { "source": { "name": "Gamma.pas", "path": "C:\\work\\Gamma.pas" }, "breakpoints": [ { "line": 7 } ] } } ] }
            """);

        // Given out of order: sources are sent in ordinal-ignore-case path order, one request each, all before configurationDone.
        await harness.LaunchAsync(Launch(
            new SourceBreakpoints(@"C:\work\Gamma.pas", [Bp(30, 7)]),
            new SourceBreakpoints(@"C:\work\beta.pas", [Bp(20, 5)]),
            new SourceBreakpoints(@"C:\work\Alpha.pas", [Bp(10, 3), Bp(11, 9)])));

        Assert.Equal(DebugState.Running, harness.Session.State);
        Assert.Equal(
            ["initialize", "launch", "setBreakpoints", "setBreakpoints", "setBreakpoints", "setExceptionBreakpoints", "configurationDone"],
            harness.SentCommands());
        // Only "line" is sent per breakpoint (DBG-060); the text is exact.
        Assert.Equal(
            """{"seq":3,"type":"request","command":"setBreakpoints","arguments":{"source":{"name":"Alpha.pas","path":"C:\\work\\Alpha.pas"},"breakpoints":[{"line":3},{"line":9}],"sourceModified":false}}""",
            harness.SentRequests("setBreakpoints")[0]);
        await harness.WaitForEventAsync("breakpoint 30");
        Assert.Equal(
        [
            "breakpoint 10 Verified line=3 hits=0 msg=null", "breakpoint 11 Verified line=9 hits=0 msg=null",
            "breakpoint 20 Verified line=5 hits=0 msg=null", "breakpoint 30 Verified line=7 hits=0 msg=null",
        ], harness.Events.Snapshot().Where(e => e.StartsWith("breakpoint", StringComparison.Ordinal)));
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-062")]
    [Trait("Req", "DBG-410")]
    public async Task Response_MapsByIndex()
    {
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "setBreakpoints", "respond": { "breakpoints": [
                { "id": 11, "verified": true, "line": 4 },
                { "id": 12, "verified": false, "line": 2, "message": "no locations found", "reason": "pending" },
                { "id": 13, "verified": false },
                { "id": 14, "verified": true } ] } } ] }
            """);
        await harness.LaunchAsync();

        var statuses = await StatusesAsync(harness.Session.SetBreakpointsAsync(Demo, [Bp(1, 3), Bp(2, 20), Bp(3, 30), Bp(4, 40)], Ct));

        Assert.Equal(
        [
            "1 Verified line=4 hits=0 msg=null",               // bound to another line
            "2 Pending line=20 hits=0 msg=no locations found",  // unbound: the requested line
            "3 Pending line=30 hits=0 msg=Not bound yet",
            "4 Verified line=40 hits=0 msg=null",
        ], statuses);
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-061")]
    public async Task Disabled_NotSent()
    {
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 3 } ] },
                "expectArguments": { "breakpoints": [ { "line": 3 } ] } } ] }
            """);

        await harness.LaunchAsync(Launch(
            new SourceBreakpoints(Demo, [Bp(1, 3), Bp(2, 5, enabled: false)]),
            new SourceBreakpoints(@"C:\work\DbgDemo\OnlyDisabled.pas", [Bp(3, 8, enabled: false)])));

        // One request: the file whose breakpoints are all disabled is not sent at all.
        Assert.Equal("""{"source":{"name":"DbgDemo.pas","path":"C:\\work\\DbgDemo\\DbgDemo.pas"},"breakpoints":[{"line":3}],"sourceModified":false}""",
            harness.SentArguments("setBreakpoints"));
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-061")]
    public async Task DuplicateLine_LaterWins_EarlierInvalid()
    {
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 9 }, { "id": 2, "verified": true, "line": 3 } ] },
                "expectArguments": { "breakpoints": [ { "line": 9 }, { "line": 3 } ] } } ] }
            """);
        await harness.LaunchAsync();

        var statuses = await StatusesAsync(harness.Session.SetBreakpointsAsync(Demo, [Bp(1, 3), Bp(2, 9), Bp(3, 3)], Ct));

        Assert.Equal(
        [
            "1 Invalid line=3 hits=0 msg=Duplicate breakpoint",
            "2 Verified line=9 hits=0 msg=null",
            "3 Verified line=3 hits=0 msg=null",
        ], statuses);
        // The remaining specs are sent in input order: id 2 (line 9), then id 3 (line 3).
        Assert.Equal("""{"source":{"name":"DbgDemo.pas","path":"C:\\work\\DbgDemo\\DbgDemo.pas"},"breakpoints":[{"line":9},{"line":3}],"sourceModified":false}""",
            harness.SentArguments("setBreakpoints"));
    }

    [Fact]
    [Trait("Req", "DBG-054")]
    public async Task FailedRequest_Invalid()
    {
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "setBreakpoints", "fail": "source not found" },
                         { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 5, "verified": true, "line": 7 } ] } } ] }
            """);

        // The failing source does not abort the launch; the next source is still sent.
        await harness.LaunchAsync(Launch(
            new SourceBreakpoints(@"C:\work\A.pas", [Bp(1, 3), Bp(2, 4)]),
            new SourceBreakpoints(@"C:\work\B.pas", [Bp(3, 7)])));

        Assert.Equal(DebugState.Running, harness.Session.State);
        await harness.WaitForEventAsync("breakpoint 3");
        Assert.Equal(
        [
            "breakpoint 1 Invalid line=3 hits=0 msg=source not found", "breakpoint 2 Invalid line=4 hits=0 msg=source not found",
            "breakpoint 3 Verified line=7 hits=0 msg=null",
        ], harness.Events.Snapshot().Where(e => e.StartsWith("breakpoint", StringComparison.Ordinal)));
        await EndAsync(harness);
    }

    // ---- SetBreakpointsAsync (G-023) ----

    [Fact]
    [Trait("Req", "DBG-409")]
    public async Task Set_WhileIdle_Stored()
    {
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 12 } ] },
                "expectArguments": { "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" }, "breakpoints": [ { "line": 12 } ] } } ] }
            """);

        var statuses = await StatusesAsync(harness.Session.SetBreakpointsAsync(Demo, [Bp(1, 12), Bp(2, 20, enabled: false)], Ct));

        // One entry per spec, in input order; nothing was started.
        Assert.Equal(["1 Pending line=12 hits=0 msg=null", "2 Disabled line=20 hits=0 msg=null"], statuses);
        Assert.Equal(DebugState.Idle, harness.Session.State);
        Assert.Null(harness.AdapterProcessId);
        Assert.Empty(harness.Events.Snapshot());

        // The stored set is sent during launch.
        await harness.LaunchAsync();
        Assert.Equal("breakpoint 1 Verified line=12 hits=0 msg=null", await harness.WaitForEventAsync("breakpoint 1"));
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-016")]
    public async Task Set_WhileStarting_BeforeInitialized_StoredNotSent()
    {
        await using var harness = SessionHarness.Create("""{ "initializedAfter": "never", "steps": [ { "on": "launch" } ] }""");
        var launch = harness.LaunchAsync();
        await harness.WaitForRequestAsync("launch");

        var statuses = await StatusesAsync(harness.Session.SetBreakpointsAsync(Demo, [Bp(1, 12)], Ct));

        Assert.Equal(DebugState.Starting, harness.Session.State);
        Assert.Equal(["1 Pending line=12 hits=0 msg=null"], statuses);
        Assert.Equal(["initialize", "launch"], harness.SentCommands());
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await Assert.ThrowsAsync<DebuggerException>(() => launch);
    }

    [Fact]
    [Trait("Req", "DBG-016")]
    public async Task Set_WhileStarting_AfterInitialized_SentImmediately()
    {
        // The launch response never arrives: after the configuration phase the session is still Starting.
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "launch" },
                         { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 12 } ] } } ] }
            """);
        var launch = harness.LaunchAsync();
        await harness.WaitForResponseAsync("configurationDone");

        var statuses = await StatusesAsync(harness.Session.SetBreakpointsAsync(Demo, [Bp(1, 12)], Ct));

        Assert.Equal(DebugState.Starting, harness.Session.State);
        Assert.Equal(["1 Verified line=12 hits=0 msg=null"], statuses);
        Assert.Equal(["initialize", "launch", "setExceptionBreakpoints", "configurationDone", "setBreakpoints"], harness.SentCommands());
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await Assert.ThrowsAsync<DebuggerException>(() => launch);
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-066")]
    public async Task Set_WhileRunning_SendsWithoutPausing()
    {
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 8 } ] },
                "expectArguments": { "breakpoints": [ { "line": 8 } ] } } ] }
            """);
        await harness.LaunchAsync();

        var statuses = await StatusesAsync(harness.Session.SetBreakpointsAsync(Demo, [Bp(1, 8)], Ct));

        Assert.Equal(["1 Verified line=8 hits=0 msg=null"], statuses);
        Assert.Equal(DebugState.Running, harness.Session.State);
        Assert.Equal(["initialize", "launch", "setExceptionBreakpoints", "configurationDone", "setBreakpoints"], harness.SentCommands());
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-409")]
    public async Task Set_WhileStopped_Sends()
    {
        await using var harness = SessionHarness.Create($$"""
            { "steps": [ {{SessionTerminationTests.StopAtStart}},
              { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 41 }, { "id": 2, "verified": false } ] },
                "expectArguments": { "breakpoints": [ { "line": 40 }, { "line": 69 } ] } },
              { "on": "setBreakpoints", "fail": "adapter says no" } ] }
            """);
        await harness.LaunchAsync();
        await harness.WaitForEventAsync("stopped");

        var statuses = await StatusesAsync(harness.Session.SetBreakpointsAsync(Demo, [Bp(1, 40), Bp(2, 69), Bp(3, 70, enabled: false)], Ct));

        Assert.Equal(DebugState.Stopped, harness.Session.State);
        Assert.Equal(
            ["1 Verified line=41 hits=0 msg=null", "2 Pending line=69 hits=0 msg=Not bound yet", "3 Disabled line=70 hits=0 msg=null"],
            statuses);

        // A failing request is reported per breakpoint, not as a faulted task.
        var failed = await StatusesAsync(harness.Session.SetBreakpointsAsync(Demo, [Bp(1, 40)], Ct));
        Assert.Equal(["1 Invalid line=40 hits=0 msg=adapter says no"], failed);
        // The result of a call is its return value: no BreakpointChanged events for it.
        Assert.DoesNotContain(harness.Events.Snapshot(), e => e.StartsWith("breakpoint", StringComparison.Ordinal));
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-063")]
    public async Task RemoveAll_SendsEmptyOnce()
    {
        await using var harness = SessionHarness.Create("""
            { "steps": [
              { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 8 } ] } },
              { "on": "setBreakpoints", "respond": { "breakpoints": [] }, "expectArguments": { "breakpoints": [] } } ] }
            """);
        await harness.LaunchAsync();
        await StatusesAsync(harness.Session.SetBreakpointsAsync(Demo, [Bp(1, 8)], Ct));

        // The last breakpoint is disabled: the source is sent once with an empty list ...
        Assert.Equal(["1 Disabled line=8 hits=0 msg=null"], await StatusesAsync(harness.Session.SetBreakpointsAsync(Demo, [Bp(1, 8, enabled: false)], Ct)));
        // ... and is then dropped: removing everything again sends nothing.
        Assert.Empty(await StatusesAsync(harness.Session.SetBreakpointsAsync(Demo, [], Ct)));
        Assert.Empty(await StatusesAsync(harness.Session.SetBreakpointsAsync(@"C:\work\DbgDemo\Never.pas", [], Ct)));

        var requests = harness.SentRequests("setBreakpoints");
        Assert.Equal(2, requests.Count);
        Assert.EndsWith(""","breakpoints":[],"sourceModified":false}}""", requests[1]);
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-064")]
    public async Task BreakpointEvent_RaisesChanged()
    {
        await using var harness = SessionHarness.Create("""
            { "steps": [
              { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 7, "verified": false, "message": "module not loaded" }, { "id": 8, "verified": true, "line": 20 } ] } },
              { "on": "configurationDone", "respond": {}, "thenEvents": [
                { "event": "breakpoint", "body": { "reason": "changed", "breakpoint": { "id": 7, "verified": true, "line": 13 } } },
                { "event": "breakpoint", "body": { "reason": "changed", "breakpoint": { "id": 99, "verified": true, "line": 1 } } },
                { "event": "breakpoint", "body": { "reason": "changed", "breakpoint": { "id": 8, "verified": false, "message": "module unloaded" } } },
                { "event": "exited", "body": { "exitCode": 0 } }, { "event": "terminated" } ] } ] }
            """);

        await harness.LaunchAsync(Launch(new SourceBreakpoints(Demo, [Bp(1, 12), Bp(2, 20)])));
        await harness.WaitForEventAsync("terminated");

        // First the launch result, then the adapter's events; the event for the unknown adapter id 99 is ignored.
        Assert.Equal(
        [
            "breakpoint 1 Pending line=12 hits=0 msg=module not loaded",
            "breakpoint 2 Verified line=20 hits=0 msg=null",
            "breakpoint 1 Verified line=13 hits=0 msg=null",
            "breakpoint 2 Pending line=20 hits=0 msg=module unloaded",
        ], harness.Events.Snapshot().Where(e => e.StartsWith("breakpoint", StringComparison.Ordinal)));
        harness.AssertScenarioComplete();
    }
}
