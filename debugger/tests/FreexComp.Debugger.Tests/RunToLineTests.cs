using System.Text.Json;
using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>Run to line (features §4, DBG-230 … DBG-233; DAP usage DBG-065).</summary>
[Trait("Category", "Integration")]
public sealed class RunToLineTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Demo = DemoSession.Source;

    // One user breakpoint on line 69 (IDE id 5, adapter id 1), sent during launch.
    private const string SetUserBreakpoint = """
        { "on": "setBreakpoints", "expectArguments": { "breakpoints": [ { "line": 69 } ] },
          "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 69 } ] } },
        """;

    private static string SetWithTemp(int line, string tempResponse) => $$"""
        { "on": "setBreakpoints", "expectArguments": { "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" }, "breakpoints": [ { "line": 69 }, { "line": {{line}} } ] },
          "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 69 }, {{tempResponse}} ] } }
        """;

    private const string SetWithoutTemp = """
        { "on": "setBreakpoints", "expectArguments": { "breakpoints": [ { "line": 69 } ] },
          "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 69 } ] } }
        """;

    private static string ContinueThen(string events) => $$"""{ "on": "continue", "respond": { "allThreadsContinued": true }, "thenEvents": [ {{events}} ] }""";

    private static string Hit(int adapterId) => DemoSession.StoppedEvent("breakpoint", $$""", "hitBreakpointIds": [{{adapterId}}]""");

    private static async Task<SessionHarness> StoppedAsync(string steps, BreakpointSpec? user = null, string? setup = null)
    {
        var harness = SessionHarness.Create(DemoSession.Scenario(steps, setup: setup ?? SetUserBreakpoint));
        await harness.LaunchAsync(new LaunchRequest
        {
            Program = SessionHarness.Program,
            TypeInfoPath = PdiFixture.DbgDemoPath,
            Breakpoints = [new SourceBreakpoints(Demo, [user ?? new BreakpointSpec { Id = 5, Line = 69 }])],
        });
        await harness.WaitForEventAsync("stopped Step epoch=1");
        return harness;
    }

    /// <summary>The line lists of all <c>setBreakpoints</c> requests sent so far, e.g. "69,73".</summary>
    private static List<string> SentLines(SessionHarness harness) =>
        [.. harness.SentRequests("setBreakpoints").Select(json => string.Join(",",
            JsonDocument.Parse(json).RootElement.GetProperty("arguments").GetProperty("breakpoints").EnumerateArray()
                .Select(b => b.GetProperty("line").GetInt32())))];

    [Fact]
    [Trait("Req", "DBG-230")]
    [Trait("Req", "DBG-231")]
    [Trait("Req", "DBG-065")]
    public async Task Stops_RunToLine()
    {
        await using var harness = await StoppedAsync($$"""
            {{SetWithTemp(73, """{ "id": 9, "verified": true, "line": 73 }""")}},
            {{ContinueThen(Hit(9))}}, {{DemoSession.Threads}}, {{DemoSession.Stack(73)}},
            {{SetWithoutTemp}},
            { "on": "setBreakpoints", "expectArguments": { "breakpoints": [] }, "respond": { "breakpoints": [] } }
            """);
        var session = harness.Session;

        // Completes on the adapter's acknowledgement of the continue (DBG-404).
        await session.RunToLineAsync(@"c:\work\DbgDemo\DbgDemo.pas", 73, Ct).WaitAsync(Safety, Ct);
        var stop = await harness.WaitForEventAsync("stopped RunToLine");

        Assert.Equal($"stopped RunToLine epoch=2 thread=1 loc={Demo}:73:3 hits=[] desc=Run to line completed", stop);
        Assert.Equal(StopReason.RunToLine, session.CurrentStop!.Reason);
        // The temporary breakpoint is merged after the user breakpoints of its source and removed before the stop is published.
        Assert.Equal(["69", "69,73", "69"], SentLines(harness));
        var commands = harness.SentCommands().ToList();
        Assert.True(commands.LastIndexOf("setBreakpoints") > commands.LastIndexOf("stackTrace"));
        Assert.Equal(
            ["state Stopped->Running", "continued", "state Running->Stopped", stop],
            harness.Events.Snapshot().SkipWhile(e => e != "state Stopped->Running"));
        // Only "line" is ever sent for a breakpoint (DBG-060); the user's breakpoint is untouched.
        Assert.All(harness.SentRequests("setBreakpoints"), json => Assert.DoesNotContain("condition", json, StringComparison.OrdinalIgnoreCase));
        var statuses = await session.SetBreakpointsAsync(Demo, [new BreakpointSpec { Id = 5, Line = 69, Enabled = false }], Ct).WaitAsync(Safety, Ct);
        Assert.Equal("5 Disabled line=69 hits=0 msg=null", SessionHarness.Status(Assert.Single(statuses)));
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-230")]
    [Trait("Req", "DBG-063")]
    public async Task Stops_RunToLine_InAFileWithoutUserBreakpoints()
    {
        // The stop names no breakpoint ids: matched by the top frame's location (DBG-071).
        await using var harness = await StoppedAsync($$"""
            { "on": "setBreakpoints", "expectArguments": { "source": { "name": "System.pas", "path": "C:\\rtl\\System.pas" }, "breakpoints": [ { "line": 2990 } ] },
              "respond": { "breakpoints": [ { "id": 9, "verified": true, "line": 2991 } ] } },
            {{ContinueThen(DemoSession.StoppedEvent("breakpoint"))}}, {{DemoSession.Threads}}, {{DemoSession.Stack(2991, "_Raise", @"C:\rtl\System.pas")}},
            { "on": "setBreakpoints", "expectArguments": { "source": { "path": "C:\\rtl\\System.pas" }, "breakpoints": [] }, "respond": { "breakpoints": [] } }
            """);

        await harness.Session.RunToLineAsync(@"C:\rtl\System.pas", 2990, Ct).WaitAsync(Safety, Ct);

        // The adapter slid the breakpoint to the next line with code; the stop is still the run-to-line stop.
        Assert.Equal(
            @"stopped RunToLine epoch=2 thread=1 loc=C:\rtl\System.pas:2991:3 hits=[] desc=Run to line completed",
            await harness.WaitForEventAsync("stopped RunToLine"));
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-231")]
    [Trait("Req", "DBG-233")]
    public async Task OtherStop_RemovesTemp()
    {
        // On the way the user's breakpoint on line 69 is hit: that is the stop, and the temporary breakpoint goes.
        await using var harness = await StoppedAsync($$"""
            {{SetWithTemp(73, """{ "id": 9, "verified": true, "line": 73 }""")}},
            {{ContinueThen(Hit(1))}}, {{DemoSession.Threads}}, {{DemoSession.Stack(69)}},
            {{SetWithoutTemp}},
            {{ContinueThen(Hit(9))}}, {{DemoSession.Threads}}, {{DemoSession.Stack(73)}}
            """);
        var session = harness.Session;

        await session.RunToLineAsync(Demo, 73, Ct).WaitAsync(Safety, Ct);
        var stop = await harness.WaitForEventAsync("stopped Breakpoint");

        Assert.Equal($"stopped Breakpoint epoch=2 thread=1 loc={Demo}:69:3 hits=[5] desc=Breakpoint at DbgDemo.pas:69", stop);
        Assert.Equal(["69", "69,73", "69"], SentLines(harness));
        Assert.Contains("breakpoint 5 Verified line=69 hits=1 msg=null", harness.Events.Snapshot());

        // The run is over: a later stop at the adapter's stale temporary id is no run-to-line stop.
        await session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        Assert.Equal($"stopped Pause epoch=3 thread=1 loc={Demo}:73:3 hits=[] desc=Paused", await harness.WaitForEventAsync("stopped Pause"));
        Assert.Equal(3, SentLines(harness).Count);
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-231")]
    [Trait("Req", "DBG-233")]
    public async Task OtherStop_SkippedUserBreakpoint_KeepsRunning_StepStopEndsTheRun()
    {
        // A user breakpoint with a false condition on the way is honoured (skipped); the run goes on with the
        // temporary breakpoint in place and ends at the first public stop of any kind - here a pause.
        await using var harness = await StoppedAsync($$"""
            {{SetWithTemp(73, """{ "id": 9, "verified": true, "line": 73 }""")}},
            {{ContinueThen(Hit(1))}}, {{DemoSession.Threads}}, {{DemoSession.Stack(69)}},
            {{ContinueThen(DemoSession.StoppedEvent("pause"))}}, {{DemoSession.Threads}}, {{DemoSession.Stack(70)}},
            {{SetWithoutTemp}}
            """, user: new BreakpointSpec { Id = 5, Line = 69, Condition = "Total = 0" });

        await harness.Session.RunToLineAsync(Demo, 73, Ct).WaitAsync(Safety, Ct);
        var stop = await harness.WaitForEventAsync("stopped Pause");

        Assert.Equal($"stopped Pause epoch=2 thread=1 loc={Demo}:70:3 hits=[] desc=Paused", stop);
        Assert.Equal(["69", "69,73", "69"], SentLines(harness));
        Assert.Equal(1, ((IDebugSessionDiagnostics)harness.Session).InternalStopCount);
        Assert.Single(harness.Events.Snapshot(), e => e.StartsWith("stopped", StringComparison.Ordinal) && e.Contains("epoch=2", StringComparison.Ordinal));
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-230")]
    public async Task Unverified_Fails_StaysStopped()
    {
        await using var harness = await StoppedAsync($$"""
            {{SetWithTemp(2, """{ "id": 9, "verified": false, "line": 2, "message": "no locations found", "reason": "pending" }""")}},
            {{SetWithoutTemp}}
            """);
        var session = harness.Session;
        var eventsBefore = harness.Events.Snapshot().Count;

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => session.RunToLineAsync(Demo, 2, Ct).WaitAsync(Safety, Ct));

        Assert.Equal(DebugErrorCode.RequestFailed, exception.Code);
        Assert.Equal("No code at DbgDemo.pas:2.", exception.Message);
        // The temporary breakpoint was removed again; nothing ran, nothing was published.
        Assert.Equal(["69", "69,2", "69"], SentLines(harness));
        Assert.DoesNotContain("continue", harness.SentCommands());
        Assert.Equal(DebugState.Stopped, session.State);
        Assert.Equal(1, session.CurrentStop!.Epoch);
        Assert.Equal(eventsBefore, harness.Events.Snapshot().Count);
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-065")]
    public async Task ExistingUserBreakpoint_Bypassed()
    {
        // A user breakpoint already sits on the target line; its condition is false.
        await using var harness = await StoppedAsync($$"""
            {{ContinueThen(Hit(1))}}, {{DemoSession.Threads}}, {{DemoSession.Stack(73)}},
            {{ContinueThen(Hit(1))}}, {{DemoSession.Threads}}, {{DemoSession.Stack(73)}},
            { "on": "continue", "respond": { "allThreadsContinued": true } }
            """,
            user: new BreakpointSpec { Id = 5, Line = 73, Condition = "Total = 0" },
            setup: """
                { "on": "setBreakpoints", "expectArguments": { "breakpoints": [ { "line": 73 } ] },
                  "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 73 } ] } },
                """);
        var session = harness.Session;

        await session.RunToLineAsync(Demo, 73, Ct).WaitAsync(Safety, Ct);
        var stop = await harness.WaitForEventAsync("stopped RunToLine");

        // No temporary breakpoint was needed; for this one run the user breakpoint's condition is bypassed.
        Assert.Equal($"stopped RunToLine epoch=2 thread=1 loc={Demo}:73:3 hits=[] desc=Run to line completed", stop);
        Assert.Equal(["73"], SentLines(harness));
        Assert.Contains("breakpoint 5 Verified line=73 hits=1 msg=null", harness.Events.Snapshot());

        // Afterwards the condition counts again: the next arrival is skipped.
        await session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("breakpoint 5 Verified line=73 hits=2");
        await harness.WaitForResponsesAsync("continue", 3);
        await DemoSession.EndAsync(harness);
        Assert.Equal(3, harness.SentRequests("continue").Count);
        Assert.DoesNotContain(harness.Events.Snapshot(), e => e.Contains("epoch=3", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "DBG-065")]
    [Trait("Req", "DBG-207")]
    public async Task ExistingUserBreakpoint_SetAgainDuringTheRun_StillBypassed()
    {
        // The user breakpoint on the target line has a false condition. While the run is under way the IDE sends the
        // file's set again (same id, same line): the table entry is a new object, the run's bypass must survive it.
        const string set = """{ "on": "setBreakpoints", "expectArguments": { "breakpoints": [ { "line": 73 } ] }, "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 73 } ] }""";
        await using var harness = await StoppedAsync($$"""
            { "on": "continue", "respond": { "allThreadsContinued": true } },
            {{set}}, "thenEvents": [ {{Hit(1)}} ] },
            {{DemoSession.Threads}}, {{DemoSession.Stack(73)}}
            """,
            user: new BreakpointSpec { Id = 5, Line = 73, Condition = "Total = 0" },
            setup: set + " },");
        var session = harness.Session;

        await session.RunToLineAsync(Demo, 73, Ct).WaitAsync(Safety, Ct);
        var statuses = await session.SetBreakpointsAsync(Demo, [new BreakpointSpec { Id = 5, Line = 73, Condition = "Total = 0" }], Ct).WaitAsync(Safety, Ct);
        var outcome = await harness.Events.WaitAsync(e => e.Contains("epoch=2", StringComparison.Ordinal) || e.StartsWith("terminated", StringComparison.Ordinal));

        Assert.Equal("5 Verified line=73 hits=0 msg=null", SessionHarness.Status(Assert.Single(statuses)));
        Assert.Equal($"stopped RunToLine epoch=2 thread=1 loc={Demo}:73:3 hits=[] desc=Run to line completed", outcome);
        Assert.Contains("breakpoint 5 Verified line=73 hits=1 msg=null", harness.Events.Snapshot());
        Assert.Equal(0, ((IDebugSessionDiagnostics)session).InternalStopCount);
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-230")]
    public async Task ContinueRejected_RemovesTemp_StaysStopped()
    {
        await using var harness = await StoppedAsync($$"""
            {{SetWithTemp(73, """{ "id": 9, "verified": true, "line": 73 }""")}},
            { "on": "continue", "fail": "process is not stopped" },
            {{SetWithoutTemp}}
            """);

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => harness.Session.RunToLineAsync(Demo, 73, Ct).WaitAsync(Safety, Ct));

        Assert.Equal("process is not stopped", exception.Message);
        Assert.Equal(["69", "69,73", "69"], SentLines(harness));
        Assert.Equal(DebugState.Stopped, harness.Session.State);
        await DemoSession.EndAsync(harness);
    }
}
