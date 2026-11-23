using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// Arrival processing (features §2.2, DBG-209 … DBG-214): arrivals counter, conditions, hit-count rules, log points with
/// interpolation, the internal continue (debug-session DBG-425, DBG-410).
/// </summary>
public sealed class BreakpointManagerTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Demo = DemoSession.Source;

    // ---- session level: a breakpoint on line 71 of Run (IDE id 5, adapter id 2), fixture memory (Total = 24) ----

    private const string SetBreakpoint = """{ "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 2, "verified": true, "line": 71 } ] } },""";

    private static string Arrival => DemoSession.StoppedEvent("breakpoint", """, "hitBreakpointIds": [2]""");

    private static string ContinueThen(string events) => $$"""{ "on": "continue", "respond": { "allThreadsContinued": true }, "thenEvents": [ {{events}} ] }""";

    private const string Exit = """{ "event": "exited", "body": { "exitCode": 0 } }, { "event": "terminated" }""";

    private static async Task<SessionHarness> LaunchedAsync(BreakpointSpec spec, string steps)
    {
        var harness = SessionHarness.Create(DemoSession.Scenario(steps, firstStop: Arrival, setup: SetBreakpoint));
        await harness.LaunchAsync(new LaunchRequest
        {
            Program = SessionHarness.Program,
            TypeInfoPath = PdiFixture.DbgDemoPath,
            Breakpoints = [new SourceBreakpoints(Demo, [spec])],
        });
        return harness;
    }

    private static List<string> PublicStopEvents(SessionHarness harness) =>
        [.. harness.Events.Snapshot().Where(e => e.StartsWith("stopped", StringComparison.Ordinal) || e == "continued"
            || e.Contains("->Stopped", StringComparison.Ordinal))];

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-425")]
    [Trait("Req", "DBG-210")]
    public async Task Condition_False_Skips_NoPublicEvents()
    {
        // Two arrivals with a false condition, then the program ends.
        await using var harness = await LaunchedAsync(
            new BreakpointSpec { Id = 5, Line = 71, Condition = "Total = 0" },
            $$"""
            {{ContinueThen(Arrival)}}, {{DemoSession.Threads}}, {{DemoSession.Stack(71)}},
            {{ContinueThen(Exit)}}
            """);

        await harness.WaitForEventAsync("terminated");

        // The session resolved both stops itself: no Stopped, no Continued, the public state stayed Running.
        Assert.Empty(PublicStopEvents(harness));
        Assert.Equal(
            ["state Idle->Starting", "state Starting->Running", "state Running->Terminating", "state Terminating->Terminated"],
            harness.Events.Snapshot().Where(e => e.StartsWith("state", StringComparison.Ordinal)));
        // BreakpointChanged for every arrival: the hit count counts arrivals that did not stop (DBG-410).
        Assert.Equal(
            ["breakpoint 5 Verified line=71 hits=1 msg=null", "breakpoint 5 Verified line=71 hits=2 msg=null"],
            harness.Events.Snapshot().Where(e => e.Contains("hits=", StringComparison.Ordinal) && !e.Contains("hits=0", StringComparison.Ordinal)));
        Assert.Equal(2, ((IDebugSessionDiagnostics)harness.Session).InternalStopCount);
        // The condition was evaluated by us in the top frame (address of the variable, memory); then an internal continue.
        Assert.Equal(2, harness.SentRequests("continue").Count);
        Assert.All(harness.SentRequests("continue"), json => Assert.Contains("\"threadId\":1", json));
        Assert.Contains(harness.SentRequests("evaluate"), json => json.Contains("\"expression\":\"&Total\"", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.SentRequests("setBreakpoints"), json => json.Contains("condition", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("terminated Exited 0 null", harness.Events.Snapshot()[^1]);
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-210")]
    public async Task Condition_True_Stops()
    {
        await using var harness = await LaunchedAsync(new BreakpointSpec { Id = 5, Line = 71, Condition = "  Total = 24 " }, "");

        var stop = await harness.WaitForEventAsync("stopped");

        Assert.Equal($"stopped Breakpoint epoch=1 thread=1 loc={Demo}:71:3 hits=[5] desc=Breakpoint at DbgDemo.pas:71", stop);
        // The counter is published before the stop.
        var events = harness.Events.Snapshot().ToList();
        Assert.True(events.IndexOf("breakpoint 5 Verified line=71 hits=1 msg=null") < events.IndexOf(stop));
        Assert.Equal(0, ((IDebugSessionDiagnostics)harness.Session).InternalStopCount);
        await DemoSession.EndAsync(harness);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-211")]
    [InlineData("I >", "Expression expected at column 4")]               // cannot be parsed
    [InlineData("Foo = 1", "Undeclared identifier 'Foo'")]               // cannot be evaluated
    [InlineData("Total", "Boolean expression expected")]                 // not Boolean
    [InlineData("Total,x", "Unexpected ',' at column 6")]                // conditions take no format suffix
    public async Task Condition_Error_Stops(string condition, string message)
    {
        // Set while idle: not validated; the first arrival stops (never silently skips).
        await using var harness = await LaunchedAsync(new BreakpointSpec { Id = 5, Line = 71, Condition = condition }, "");

        var stop = await harness.WaitForEventAsync("stopped");

        Assert.Equal($"stopped Breakpoint epoch=1 thread=1 loc={Demo}:71:3 hits=[5] desc=Breakpoint condition error: {message}", stop);
        Assert.Contains($"output Debugger \"Breakpoint DbgDemo.pas:71: {message.Replace("'", "\\u0027")}\"", harness.Events.Snapshot());
        Assert.Contains("breakpoint 5 Verified line=71 hits=1 msg=null", harness.Events.Snapshot());
        Assert.Equal(DebugState.Stopped, harness.Session.State);
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-410")]
    [Trait("Req", "DBG-212")]
    public async Task HitCount_AlwaysCounts()
    {
        // "Stop from the third pass on": two arrivals are skipped, the third stops.
        await using var harness = await LaunchedAsync(
            new BreakpointSpec { Id = 5, Line = 71, HitCount = new HitCountSpec(HitCountMode.GreaterOrEqual, 3) },
            $$"""
            {{ContinueThen(Arrival)}}, {{DemoSession.Threads}}, {{DemoSession.Stack(71)}},
            {{ContinueThen(Arrival)}}, {{DemoSession.Threads}}, {{DemoSession.Stack(71)}},
            { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 2, "verified": true, "line": 71 } ] } }
            """);

        var stop = await harness.WaitForEventAsync("stopped");

        Assert.Equal($"stopped Breakpoint epoch=1 thread=1 loc={Demo}:71:3 hits=[5] desc=Breakpoint at DbgDemo.pas:71", stop);
        Assert.Equal(
            ["breakpoint 5 Verified line=71 hits=1 msg=null", "breakpoint 5 Verified line=71 hits=2 msg=null", "breakpoint 5 Verified line=71 hits=3 msg=null"],
            harness.Events.Snapshot().Where(e => e.StartsWith("breakpoint 5 Verified line=71 hits=", StringComparison.Ordinal) && !e.Contains("hits=0", StringComparison.Ordinal)));
        Assert.Single(PublicStopEvents(harness), e => e.StartsWith("stopped", StringComparison.Ordinal));
        Assert.Equal(2, ((IDebugSessionDiagnostics)harness.Session).InternalStopCount);

        // The counter of an id that persists is kept when the IDE sends the file's set again (DBG-207).
        var statuses = await harness.Session.SetBreakpointsAsync(Demo, [new BreakpointSpec { Id = 5, Line = 71 }], Ct).WaitAsync(Safety, Ct);
        Assert.Equal("5 Verified line=71 hits=3 msg=null", SessionHarness.Status(Assert.Single(statuses)));
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-213")]
    [Trait("Req", "DBG-425")]
    public async Task LogPoint_Interpolates()
    {
        await using var harness = await LaunchedAsync(
            new BreakpointSpec { Id = 5, Line = 71, LogMessage = "Total={Total} twice={Total * 2} S={S} P={P} Ch={Ch} {{literal}}" },
            ContinueThen(Exit));

        await harness.WaitForEventAsync("terminated");

        // The summary rendering of each expression, strings without quotes; never stops.
        Assert.Contains("output LogPoint \"Total=24 twice=48 S=H\\u00E9llo P=(X: 3; Y: 4) Ch=\\u0027A\\u0027 {literal}\\n\"", harness.Events.Snapshot());
        Assert.Empty(PublicStopEvents(harness));
        Assert.Contains("breakpoint 5 Verified line=71 hits=1 msg=null", harness.Events.Snapshot());
        Assert.Equal(1, ((IDebugSessionDiagnostics)harness.Session).InternalStopCount);
        Assert.Single(harness.SentRequests("continue"));
        harness.AssertScenarioComplete();
    }

    // ---- unit level: the arrival rules over a table of breakpoints ----

    private static BreakpointManager.Entry EntryOf(BreakpointSpec spec, BreakpointManager? manager = null) =>
        (manager ?? new BreakpointManager()).Set(Demo, [spec]).Entries[0];

    private static Task<bool> True(string condition) => Task.FromResult(true);

    private static Task<string> Text(string expression) => Task.FromResult("<" + expression + ">");

    /// <summary>The arrivals (1-based) out of <paramref name="count"/> at which the breakpoint stops.</summary>
    private static async Task<List<int>> StopsAsync(BreakpointManager.Entry entry, int count, Func<int, bool>? condition = null)
    {
        var stops = new List<int>();
        for (var arrival = 1; arrival <= count; arrival++)
        {
            var number = arrival;
            var outcome = await BreakpointManager.EvaluateArrivalAsync(
                [entry], _ => Task.FromResult(condition?.Invoke(number) ?? true), Text);
            if (outcome.Stoppers.Count > 0)
                stops.Add(arrival);
            Assert.Equal(arrival, entry.HitCount);       // arrivals, always
        }

        return stops;
    }

    [Fact]
    [Trait("Req", "DBG-209")]
    public async Task Plain_AlwaysStops()
    {
        var entry = EntryOf(new BreakpointSpec { Id = 1, Line = 40 });

        Assert.Equal([1, 2, 3], await StopsAsync(entry, 3));
        // Empty or whitespace-only: no condition (the evaluator is not asked).
        var blank = EntryOf(new BreakpointSpec { Id = 2, Line = 40, Condition = "   " });
        var outcome = await BreakpointManager.EvaluateArrivalAsync([blank], _ => throw new InvalidOperationException("asked"), Text);
        Assert.Same(blank, Assert.Single(outcome.Stoppers));
        Assert.Null(outcome.Description);
        Assert.Empty(outcome.Outputs);
    }

    [Fact]
    [Trait("Req", "DBG-212")]
    public async Task HitCount_Equal()
    {
        Assert.Equal([3], await StopsAsync(EntryOf(new BreakpointSpec { Id = 1, Line = 40, HitCount = new HitCountSpec(HitCountMode.Equal, 3) }), 6));
        Assert.Equal([1], await StopsAsync(EntryOf(new BreakpointSpec { Id = 1, Line = 40, HitCount = new HitCountSpec(HitCountMode.Equal, 1) }), 6));
    }

    [Fact]
    [Trait("Req", "DBG-212")]
    public async Task HitCount_GreaterOrEqual()
    {
        Assert.Equal([3, 4, 5, 6], await StopsAsync(EntryOf(new BreakpointSpec { Id = 1, Line = 40, HitCount = new HitCountSpec(HitCountMode.GreaterOrEqual, 3) }), 6));
    }

    [Fact]
    [Trait("Req", "DBG-212")]
    public async Task HitCount_Multiple()
    {
        Assert.Equal([2, 4, 6], await StopsAsync(EntryOf(new BreakpointSpec { Id = 1, Line = 40, HitCount = new HitCountSpec(HitCountMode.Multiple, 2) }), 6));
        // The example of the specification: the loop body runs three times, one stop (the second pass).
        Assert.Equal([2], await StopsAsync(EntryOf(new BreakpointSpec { Id = 1, Line = 66, HitCount = new HitCountSpec(HitCountMode.Multiple, 2) }), 3));
    }

    [Fact]
    [Trait("Req", "DBG-212")]
    public async Task HitCount_CountsOnlyArrivalsThatPassedTheCondition()
    {
        // The condition holds on even arrivals; "passes = 2" is therefore arrival 4.
        var entry = EntryOf(new BreakpointSpec { Id = 1, Line = 40, Condition = "even", HitCount = new HitCountSpec(HitCountMode.Equal, 2) });

        Assert.Equal([4], await StopsAsync(entry, 8, arrival => arrival % 2 == 0));
        Assert.Equal(8, entry.HitCount);
        // The example of the specification: Factorial(4) reaches line 40 with N = 4, 3, 2; one stop, hit count 3.
        var factorial = EntryOf(new BreakpointSpec { Id = 2, Line = 40, Condition = "N = 2" });
        Assert.Equal([3], await StopsAsync(factorial, 3, arrival => arrival == 3));
        Assert.Equal(3, factorial.HitCount);
    }

    [Fact]
    [Trait("Req", "DBG-214")]
    [Trait("Req", "DBG-207")]
    public async Task Counters_SurviveResending_AndDisabling()
    {
        var manager = new BreakpointManager();
        var spec = new BreakpointSpec { Id = 1, Line = 40, HitCount = new HitCountSpec(HitCountMode.Equal, 3) };
        await StopsAsync(EntryOf(spec, manager), 2);

        // Disabled, enabled again, a new breakpoint added: the counters of the id that persists go on.
        manager.Set(Demo, [spec with { Enabled = false }]);
        var entries = manager.Set(Demo, [spec, new BreakpointSpec { Id = 2, Line = 41 }]).Entries;

        Assert.Equal(2, entries[0].HitCount);
        Assert.Equal(0, entries[1].HitCount);
        var third = await BreakpointManager.EvaluateArrivalAsync([entries[0]], True, Text);
        Assert.Single(third.Stoppers);                    // the third pass, counted across the re-sending
        Assert.Equal(3, entries[0].HitCount);
    }

    [Fact]
    [Trait("Req", "DBG-211")]
    public async Task Condition_Error_StopsWithDescriptionAndOutput()
    {
        var entry = EntryOf(new BreakpointSpec { Id = 1, Line = 66, Condition = "I >", HitCount = new HitCountSpec(HitCountMode.Equal, 5) });

        var outcome = await BreakpointManager.EvaluateArrivalAsync(
            [entry], _ => throw new DebuggerException(DebugErrorCode.ExpressionSyntax, "Expression expected at column 4"), Text);

        // Stops whatever the hit-count rule says.
        Assert.Same(entry, Assert.Single(outcome.Stoppers));
        Assert.Equal("Breakpoint condition error: Expression expected at column 4", outcome.Description);
        var output = Assert.Single(outcome.Outputs);
        Assert.Equal(OutputCategory.Debugger, output.Category);
        Assert.Equal("Breakpoint DbgDemo.pas:66: Expression expected at column 4", output.Text);
        Assert.Equal(1, entry.HitCount);
    }

    [Fact]
    [Trait("Req", "DBG-210")]
    public async Task SeveralBreakpoints_AscendingId_AnyStopIsPublic()
    {
        var manager = new BreakpointManager();
        // (Two enabled breakpoints cannot share a line; an arrival can still name several ids.)
        var entries = manager.Set(Demo,
        [
            new BreakpointSpec { Id = 9, Line = 40, LogMessage = "nine" },
            new BreakpointSpec { Id = 3, Line = 41, Condition = "false" },
            new BreakpointSpec { Id = 5, Line = 42 },
        ]).Entries;
        var asked = new List<string>();

        var outcome = await BreakpointManager.EvaluateArrivalAsync(
            entries, condition => { asked.Add(condition); return Task.FromResult(false); }, Text);

        Assert.Equal([5], outcome.Stoppers.Select(e => e.Spec.Id));
        Assert.Equal(["nine\n"], outcome.Outputs.Select(o => o.Text));
        Assert.Equal(["false"], asked);
        Assert.All(entries, entry => Assert.Equal(1, entry.HitCount));
    }

    [Fact]
    [Trait("Req", "DBG-213")]
    public async Task LogPoint_NeverStops_GatedByConditionAndHitCount()
    {
        var entry = EntryOf(new BreakpointSpec
        {
            Id = 1, Line = 66, LogMessage = "I={I}", Condition = "odd", HitCount = new HitCountSpec(HitCountMode.GreaterOrEqual, 2),
        });
        var logged = new List<string>();
        for (var arrival = 1; arrival <= 6; arrival++)
        {
            var number = arrival;
            var outcome = await BreakpointManager.EvaluateArrivalAsync(
                [entry], _ => Task.FromResult(number % 2 == 1), _ => Task.FromResult(number.ToString()));
            Assert.Empty(outcome.Stoppers);
            logged.AddRange(outcome.Outputs.Select(o => o.Category + ":" + o.Text));
        }

        // Odd arrivals pass the condition (1, 3, 5); from the second pass on they log.
        Assert.Equal(["LogPoint:I=3\n", "LogPoint:I=5\n"], logged);
        Assert.Equal(6, entry.HitCount);

        // An empty message logs an empty line.
        var empty = await BreakpointManager.EvaluateArrivalAsync([EntryOf(new BreakpointSpec { Id = 2, Line = 66, LogMessage = "" })], True, Text);
        Assert.Equal("\n", Assert.Single(empty.Outputs).Text);
        Assert.Empty(empty.Stoppers);
    }

    [Theory]
    [Trait("Req", "DBG-213")]
    [InlineData("I={I} value={(I + 1) * 10}", "I=<I> value=<(I + 1) * 10>")]
    [InlineData("{A}{B}", "<A><B>")]
    [InlineData("plain text", "plain text")]
    [InlineData("{ S }", "< S >")]
    public async Task LogPoint_Interpolation(string message, string expected)
    {
        Assert.Equal(expected, await BreakpointManager.InterpolateAsync(message, Text));
    }

    [Fact]
    [Trait("Req", "DBG-213")]
    public async Task LogPoint_ErrorPlaceholder()
    {
        static Task<string> Failing(string expression) => expression switch
        {
            "Foo" => throw new DebuggerException(DebugErrorCode.ExpressionEvaluation, "Undeclared identifier 'Foo'"),
            "1 +" => throw new DebuggerException(DebugErrorCode.ExpressionSyntax, "Expression expected at column 4"),
            "" => throw new DebuggerException(DebugErrorCode.ExpressionSyntax, "Expression expected"),
            _ => Task.FromResult("ok"),
        };

        // The message is logged all the same; only the failing expression is replaced.
        Assert.Equal("v=<error: Undeclared identifier 'Foo'> w=ok", await BreakpointManager.InterpolateAsync("v={Foo} w={X}", Failing));
        Assert.Equal("<error: Expression expected at column 4>", await BreakpointManager.InterpolateAsync("{1 +}", Failing));
        Assert.Equal("<error: Expression expected>", await BreakpointManager.InterpolateAsync("{}", Failing));

        // With the real evaluator.
        var bed = EvalBed.Demo();
        Task<string> Real(string expression) => bed.Values.EvaluateLogTextAsync(expression, bed.Frame, Ct);
        Assert.Equal(
            "24 Héllo 'é' (10, 20, 30) <error: Undeclared identifier 'Foo'> <error: Expression expected at column 8>",
            await BreakpointManager.InterpolateAsync("{Total} {S} {S[2]} {Nums} {Foo} {Total +}", Real));
    }

    [Theory]
    [Trait("Req", "DBG-213")]
    [InlineData("{{", "{")]
    [InlineData("}}", "}")]
    [InlineData("{{I}}", "{I}")]
    [InlineData("{{{I}}}", "{<I>}")]
    [InlineData("a {b", "a {b")]                 // an unterminated "{" is literal
    [InlineData("100%} done", "100%} done")]     // a single "}" too
    [InlineData("{I} {", "<I> {")]
    [InlineData("", "")]
    public async Task Braces_Escaped(string message, string expected)
    {
        Assert.Equal(expected, await BreakpointManager.InterpolateAsync(message, Text));
    }

    // ---- validation when a breakpoint is set (DBG-211, DBG-212) ----

    [Fact]
    [Trait("Req", "DBG-212")]
    public void HitCount_BelowOne_Invalid()
    {
        var manager = new BreakpointManager();

        var entries = manager.Set(Demo,
        [
            new BreakpointSpec { Id = 1, Line = 40, HitCount = new HitCountSpec(HitCountMode.Equal, 0) },
            new BreakpointSpec { Id = 2, Line = 41, HitCount = new HitCountSpec(HitCountMode.Multiple, -3) },
            new BreakpointSpec { Id = 3, Line = 42, HitCount = new HitCountSpec(HitCountMode.GreaterOrEqual, 1) },
            new BreakpointSpec { Id = 4, Line = 43, Enabled = false, HitCount = new HitCountSpec(HitCountMode.Equal, 0) },
        ]).Entries;

        Assert.Equal(
        [
            "1 Invalid line=40 hits=0 msg=Hit count must be at least 1",
            "2 Invalid line=41 hits=0 msg=Hit count must be at least 1",
            "3 Pending line=42 hits=0 msg=null",
            "4 Disabled line=43 hits=0 msg=null",
        ], entries.Select(e => SessionHarness.Status(e.Status)));
        Assert.Equal([3], entries.Where(e => e.IsSent).Select(e => e.Spec.Id));      // an invalid breakpoint is never sent
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-211")]
    public async Task Condition_SyntaxError_DuringSession_Invalid_NotSent()
    {
        await using var harness = await DemoSession.StoppedInRunAsync("""
            { "on": "setBreakpoints", "expectArguments": { "breakpoints": [ { "line": 40 } ] },
              "respond": { "breakpoints": [ { "id": 4, "verified": true, "line": 40 } ] } }
            """);

        // During a session the condition is parsed at once; while idle it is not (see Condition_Error_Stops).
        var statuses = await harness.Session.SetBreakpointsAsync(Demo,
        [
            new BreakpointSpec { Id = 1, Line = 40, Condition = "N = 2" },
            new BreakpointSpec { Id = 2, Line = 66, Condition = "I >" },
            new BreakpointSpec { Id = 3, Line = 69, Condition = "Pet as TDog" },
        ], Ct).WaitAsync(Safety, Ct);

        Assert.Equal(
        [
            "1 Verified line=40 hits=0 msg=null",
            "2 Invalid line=66 hits=0 msg=Expression expected at column 4",
            "3 Invalid line=69 hits=0 msg='as' is not supported in debugger expressions at column 5",
        ], statuses.Select(SessionHarness.Status));
        await DemoSession.EndAsync(harness);
    }
}
