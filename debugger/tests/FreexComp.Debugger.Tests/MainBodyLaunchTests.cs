using System.Text.Json;
using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// <c>LaunchRequest.StopAtMainBody</c> (features DBG-225): the first step of a program that is not running yet. The
/// session stops at the entry internally, runs to the first statement of the main block and publishes that stop as a
/// step stop. The fixture's main block: routine <c>DbgDemo</c>, <c>bodyLine</c> 76.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Req", "DBG-225")]
public sealed class MainBodyLaunchTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Demo = DemoSession.Source;

    private const string Launch = """{ "on": "launch", "respond": {}, "expectArguments": { "stopOnEntry": true } }""";

    private static readonly string EntryStop = $$"""
        { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DemoSession.StoppedEvent("entry")}} ] },
        {{DemoSession.Threads}},
        { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 1, "name": "ntdll.dll`LdrInitializeThunk", "line": 12, "column": 1,
            "source": { "name": "ntdll.dll`LdrInitializeThunk", "sourceReference": 1001 }, "instructionPointerReference": "0x7FFB12345678" } ] } }
        """;

    /// <summary>A <c>setBreakpoints</c> step for the demo source: the lines expected, the answers given.</summary>
    private static string Set(string lines, string answers, string then = "") => $$"""
        { "on": "setBreakpoints", "expectArguments": { "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" }, "breakpoints": [ {{lines}} ] },
          "respond": { "breakpoints": [ {{answers}} ] }{{then}} }
        """;

    private static string ContinueThen(string events) =>
        $$"""{ "on": "continue", "expectArguments": { "threadId": 1 }, "respond": { "allThreadsContinued": true }, "thenEvents": [ {{events}} ] }""";

    private static string Hit(int adapterId) => DemoSession.StoppedEvent("breakpoint", $$""", "hitBreakpointIds": [{{adapterId}}]""");

    /// <summary>The main block alone on the stack.</summary>
    private static string MainStack(int line) => $$"""
        { "on": "stackTrace", "respond": { "totalFrames": 1, "stackFrames": [
            { "id": 524288, "name": "$main", "line": {{line}}, "column": 3, "source": { "name": "DbgDemo.pas", "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } } ] } }
        """;

    private static SessionHarness Create(params string[] steps) =>
        SessionHarness.Create($$"""{ "steps": [ {{string.Join(", ", steps)}} ] }""");

    private static LaunchRequest Request(bool stopOnEntry = false, string? typeInfoPath = null, params BreakpointSpec[] breakpoints) => new()
    {
        Program = SessionHarness.Program,
        TypeInfoPath = typeInfoPath ?? PdiFixture.DbgDemoPath,
        Exceptions = SessionHarness.NoLanguageExceptions,
        StopOnEntry = stopOnEntry,
        StopAtMainBody = true,
        Breakpoints = breakpoints.Length == 0 ? [] : [new SourceBreakpoints(Demo, breakpoints)],
    };

    private static List<string> SentLines(SessionHarness harness) =>
        [.. harness.SentRequests("setBreakpoints").Select(json => string.Join(",",
            JsonDocument.Parse(json).RootElement.GetProperty("arguments").GetProperty("breakpoints").EnumerateArray()
                .Select(b => b.GetProperty("line").GetInt32())))];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]   // StopAtMainBody takes precedence over StopOnEntry
    [Trait("Req", "DBG-425")]
    public async Task WithTypeInfo_StepStopAtTheMainBlock_ThenStepOverStepsOn(bool stopOnEntry)
    {
        await using var harness = Create(
            Launch, EntryStop,
            Set("""{ "line": 76 }""", """{ "id": 9, "verified": true, "line": 77 }"""),
            ContinueThen(Hit(9)), DemoSession.Threads, MainStack(77),
            Set("", ""),
            """{ "on": "next", "expectArguments": { "threadId": 1 }, "respond": {}, "thenEvents": [ { "event": "stopped", "body": { "reason": "step", "threadId": 1 } } ] }""",
            DemoSession.Threads, MainStack(78));
        var session = harness.Session;

        // The launch completes with the public stop, not with the internal entry stop.
        await harness.LaunchAsync(Request(stopOnEntry));

        Assert.Equal(DebugState.Stopped, session.State);
        var stop = Assert.IsType<StopInfo>(session.CurrentStop);
        Assert.Equal(StopReason.Step, stop.Reason);
        Assert.Equal(new SourceLocation(Demo, 77, 3), stop.Location);
        await harness.WaitForEventAsync("stopped");
        // The entry stop and the run to the main block raise nothing: no Stopped, Continued or StateChanged of their own.
        Assert.Equal(
            ["state Idle->Starting", "state Starting->Stopped", $"stopped Step epoch=1 thread=1 loc={Demo}:77:3 hits=[] desc=Step completed"],
            harness.Events.Snapshot());
        Assert.Equal(1, ((IDebugSessionDiagnostics)session).InternalStopCount);
        // The temporary breakpoint was removed before the stop was published.
        Assert.Equal(["76", ""], SentLines(harness));
        var commands = harness.SentCommands().ToList();
        Assert.True(commands.LastIndexOf("setBreakpoints") > commands.LastIndexOf("stackTrace"));

        // The step that follows is a step in the main block: it ends on the next statement.
        await session.StepOverAsync(stop.ThreadId, Ct).WaitAsync(Safety, Ct);
        Assert.Equal($"stopped Step epoch=2 thread=1 loc={Demo}:78:3 hits=[] desc=Step completed", await harness.WaitForEventAsync("stopped Step epoch=2"));
        Assert.DoesNotContain("continue", harness.SentCommands().Skip(commands.Count));
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    public async Task WithoutTypeInfo_StopsAtEntry()
    {
        await using var harness = Create(Launch, EntryStop);
        var missing = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"), "None.pdi");

        await harness.LaunchAsync(Request(typeInfoPath: missing));

        Assert.False(harness.Session.HasTypeInfo);
        Assert.Equal("stopped Entry epoch=1 thread=1 loc=null hits=[] desc=Stopped at program entry", await harness.WaitForEventAsync("stopped"));
        Assert.Equal(DebugState.Stopped, harness.Session.State);
        Assert.Equal(0, ((IDebugSessionDiagnostics)harness.Session).InternalStopCount);
        Assert.DoesNotContain("setBreakpoints", harness.SentCommands());
        Assert.DoesNotContain("continue", harness.SentCommands());
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-233")]
    public async Task UserBreakpointBeforeTheMainBlock_HitFirst_Wins()
    {
        // A user breakpoint in code that runs before the main block (line 20): that stop is the public one, with its
        // real reason, and the temporary breakpoint goes. A later stop at the adapter's stale id is no step stop.
        await using var harness = Create(
            Launch,
            Set("""{ "line": 20 }""", """{ "id": 1, "verified": true, "line": 20 }"""),
            EntryStop,
            Set("""{ "line": 20 }, { "line": 76 }""", """{ "id": 1, "verified": true, "line": 20 }, { "id": 9, "verified": true, "line": 77 }"""),
            ContinueThen(Hit(1)), DemoSession.Threads, DemoSession.Stack(20, "TAnimal.Create"),
            Set("""{ "line": 20 }""", """{ "id": 1, "verified": true, "line": 20 }"""),
            ContinueThen(Hit(9)), DemoSession.Threads, MainStack(77));
        var session = harness.Session;

        await harness.LaunchAsync(Request(breakpoints: new BreakpointSpec { Id = 5, Line = 20 }));

        Assert.Equal($"stopped Breakpoint epoch=1 thread=1 loc={Demo}:20:3 hits=[5] desc=Breakpoint at DbgDemo.pas:20", await harness.WaitForEventAsync("stopped"));
        Assert.Equal(["20", "20,76", "20"], SentLines(harness));
        Assert.Equal(
            ["state Starting->Stopped", $"stopped Breakpoint epoch=1 thread=1 loc={Demo}:20:3 hits=[5] desc=Breakpoint at DbgDemo.pas:20"],
            harness.Events.Snapshot().Where(e => e.StartsWith("state Starting", StringComparison.Ordinal) || e.StartsWith("stopped", StringComparison.Ordinal)));

        await session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        Assert.Equal($"stopped Pause epoch=2 thread=1 loc={Demo}:77:3 hits=[] desc=Paused", await harness.WaitForEventAsync("stopped Pause"));
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-065")]
    public async Task UserBreakpointOnTheMainBlockLine_NoTemporaryBreakpoint_StepStop()
    {
        // The user's breakpoint already stands on the first statement of the main block (bound to line 77): no second
        // breakpoint is sent for the line; its hit count rule is bypassed for this arrival, which is still counted.
        await using var harness = Create(
            Launch,
            Set("""{ "line": 76 }""", """{ "id": 1, "verified": true, "line": 77 }"""),
            EntryStop,
            ContinueThen(Hit(1)), DemoSession.Threads, MainStack(77));

        await harness.LaunchAsync(Request(breakpoints: new BreakpointSpec { Id = 5, Line = 76, HitCount = new HitCountSpec(HitCountMode.Equal, 2) }));

        Assert.Equal($"stopped Step epoch=1 thread=1 loc={Demo}:77:3 hits=[] desc=Step completed", await harness.WaitForEventAsync("stopped"));
        Assert.Equal(["76"], SentLines(harness));
        Assert.Contains("breakpoint 5 Verified line=77 hits=1 msg=null", harness.Events.Snapshot());
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    public async Task MainBlockLineWithoutCode_StopsAtEntry()
    {
        // The adapter cannot bind the line (a program built without line information): the stop stays the entry stop.
        await using var harness = Create(
            Launch, EntryStop,
            Set("""{ "line": 76 }""", """{ "id": 9, "verified": false, "message": "no locations found" }"""),
            Set("", ""));

        await harness.LaunchAsync(Request());

        Assert.Equal("stopped Entry epoch=1 thread=1 loc=null hits=[] desc=Stopped at program entry", await harness.WaitForEventAsync("stopped"));
        Assert.Equal(["76", ""], SentLines(harness));
        Assert.DoesNotContain("continue", harness.SentCommands());
        Assert.Equal(0, ((IDebugSessionDiagnostics)harness.Session).InternalStopCount);
        await DemoSession.EndAsync(harness);
    }

    [Theory]
    [InlineData("StepOver")]
    [InlineData("StepInto")]
    [Trait("Req", "DBG-222")]
    public async Task StepAtThePublicEntryStop_RunsToTheMainBlock(string command)
    {
        // StopOnEntry published the entry stop. At its entry the program has no statement to step from (the step
        // would end in system code and the program would run on to its end): the step runs to the first statement of
        // the main block - a "continue" with a temporary breakpoint, no "next"/"stepIn" - and ends there as a step.
        await using var harness = Create(
            Launch, EntryStop,
            Set("""{ "line": 76 }""", """{ "id": 9, "verified": true, "line": 77 }"""),
            ContinueThen(Hit(9)), DemoSession.Threads, MainStack(77),
            Set("", ""));
        var session = harness.Session;
        await harness.LaunchAsync(Request(stopOnEntry: true) with { StopAtMainBody = false });
        await harness.WaitForEventAsync("stopped Entry epoch=1");

        await (command == "StepOver" ? session.StepOverAsync(1, Ct) : session.StepIntoAsync(1, Ct)).WaitAsync(Safety, Ct);
        var stop = await harness.WaitForEventAsync("stopped Step");

        Assert.Equal($"stopped Step epoch=2 thread=1 loc={Demo}:77:3 hits=[] desc=Step completed", stop);
        Assert.Equal(
            ["state Stopped->Running", "continued", "state Running->Stopped", stop],
            harness.Events.Snapshot().SkipWhile(e => e != "state Stopped->Running"));
        Assert.Equal(["76", ""], SentLines(harness));
        Assert.DoesNotContain("next", harness.SentCommands());
        Assert.DoesNotContain("stepIn", harness.SentCommands());
        Assert.Equal(0, ((IDebugSessionDiagnostics)session).InternalStopCount);
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    public async Task StepAtThePublicEntryStop_WithoutTypeInfo_IsAPlainStep()
    {
        await using var harness = Create(Launch, EntryStop, """{ "on": "next", "expectArguments": { "threadId": 1 }, "respond": {} }""");
        var missing = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"), "None.pdi");
        await harness.LaunchAsync(Request(stopOnEntry: true, typeInfoPath: missing) with { StopAtMainBody = false });
        await harness.WaitForEventAsync("stopped Entry epoch=1");

        await harness.Session.StepOverAsync(1, Ct).WaitAsync(Safety, Ct);

        Assert.Equal(DebugState.Running, harness.Session.State);
        Assert.DoesNotContain("setBreakpoints", harness.SentCommands());
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    public async Task WithoutStopAtMainBody_NothingChanges()
    {
        // StopOnEntry alone still publishes the entry stop, with type information present.
        await using var harness = Create(Launch, EntryStop);

        await harness.LaunchAsync(Request(stopOnEntry: true) with { StopAtMainBody = false });

        Assert.Equal("stopped Entry epoch=1 thread=1 loc=null hits=[] desc=Stopped at program entry", await harness.WaitForEventAsync("stopped"));
        Assert.DoesNotContain("setBreakpoints", harness.SentCommands());
        await DemoSession.EndAsync(harness);
    }
}
