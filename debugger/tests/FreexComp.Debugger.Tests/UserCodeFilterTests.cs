using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>The user-code filter for step results (features DBG-222).</summary>
[Trait("Category", "Integration")]
[Trait("Req", "DBG-222")]
public sealed class UserCodeFilterTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Demo = DemoSession.Source;
    private static readonly string[] Roots = [@"C:\work\DbgDemo"];

    /// <summary>A step ended inside the run-time library (source outside the user's roots), called from Run.</summary>
    private const string InLibrary = """
        { "on": "stackTrace", "respond": { "totalFrames": 3, "stackFrames": [
            { "id": 700, "name": "_UStrCat", "line": 1200, "column": 1, "source": { "path": "C:\\rtl\\System.pas" } },
            { "id": 524288, "name": "Run", "line": 70, "column": 3, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } },
            { "id": 524289, "name": "DbgDemo", "line": 77, "column": 3, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } } ] } }
        """;

    /// <summary>A step ended in code without source (a frame as the adapter reports a system routine), called from Run.</summary>
    private const string InSystem = """
        { "on": "stackTrace", "respond": { "totalFrames": 2, "stackFrames": [
            { "id": 701, "name": "kernel32.dll`WriteFile", "line": 12, "column": 1, "source": { "name": "kernel32.dll`WriteFile", "sourceReference": 1001 },
              "instructionPointerReference": "0x7FFB12345678" },
            { "id": 524288, "name": "Run", "line": 70, "column": 3, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } } ] } }
        """;

    /// <summary>No frame on the stack is user code.</summary>
    private const string NoUserFrame = """
        { "on": "stackTrace", "respond": { "totalFrames": 1, "stackFrames": [
            { "id": 702, "name": "_Halt0", "line": 3000, "column": 1, "source": { "path": "C:\\rtl\\System.pas" } } ] } }
        """;

    private static string Step(string command, string? then = "step") => then is null
        ? $$"""{ "on": "{{command}}", "expectArguments": { "threadId": 1 }, "respond": {} }"""
        : $$"""{ "on": "{{command}}", "expectArguments": { "threadId": 1 }, "respond": {}, "thenEvents": [ {{DemoSession.StoppedEvent(then)}} ] }""";

    private static Task<SessionHarness> StoppedAsync(string steps, bool withRoots = true) =>
        DemoSession.StoppedInRunAsync(steps, configure: withRoots ? options => options with { UserSourceRoots = Roots } : null);

    private static int InternalStops(SessionHarness harness) => ((IDebugSessionDiagnostics)harness.Session).InternalStopCount;

    [Fact]
    public async Task StepInto_NonUser_StepOutThenStepIn()
    {
        // Step Into entered library code: step out of it, then step in again on the same thread, so that the next
        // call on the same statement can still be entered.
        await using var harness = await StoppedAsync($$"""
            {{Step("stepIn")}}, {{DemoSession.Threads}}, {{InLibrary}},
            {{Step("stepOut")}}, {{DemoSession.Threads}}, {{DemoSession.Stack(70)}},
            {{Step("stepIn")}}, {{DemoSession.Threads}}, {{DemoSession.Stack(26, "TAnimal.Describe")}}
            """);
        var before = harness.SentCommands().Count;

        await harness.Session.StepIntoAsync(1, Ct).WaitAsync(Safety, Ct);
        var stop = await harness.WaitForEventAsync("stopped Step epoch=2");

        Assert.Equal($"stopped Step epoch=2 thread=1 loc={Demo}:26:3 hits=[] desc=Step completed", stop);
        Assert.Equal(
            ["stepIn", "threads", "stackTrace", "stepOut", "threads", "stackTrace", "stepIn", "threads", "stackTrace"],
            harness.SentCommands().Skip(before));
        // The two internal steps raised no public events (DBG-425).
        Assert.Equal(
            ["state Stopped->Running", "continued", "state Running->Stopped", stop],
            harness.Events.Snapshot().SkipWhile(e => e != "state Stopped->Running"));
        Assert.Equal(2, InternalStops(harness));
        await DemoSession.EndAsync(harness);
    }

    [Theory]
    [InlineData("next", InLibrary)]
    [InlineData("stepOut", InLibrary)]
    [InlineData("next", InSystem)]          // no source at all is not user code either
    public async Task StepOverOrOut_NonUser_StepsOut(string command, string nonUserStack)
    {
        await using var harness = await StoppedAsync($$"""
            {{Step(command)}}, {{DemoSession.Threads}}, {{nonUserStack}},
            {{Step("stepOut")}}, {{DemoSession.Threads}}, {{DemoSession.Stack(70)}}
            """);
        var before = harness.SentCommands().Count;

        await (command == "next" ? harness.Session.StepOverAsync(1, Ct) : harness.Session.StepOutAsync(1, Ct)).WaitAsync(Safety, Ct);
        var stop = await harness.WaitForEventAsync("stopped Step epoch=2");

        // Back in user code: the stop is published there, without a further step in.
        Assert.Equal($"stopped Step epoch=2 thread=1 loc={Demo}:70:3 hits=[] desc=Step completed", stop);
        Assert.Equal([command, "threads", "stackTrace", "stepOut", "threads", "stackTrace"], harness.SentCommands().Skip(before));
        Assert.Equal(1, InternalStops(harness));
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    public async Task StepInUserCode_NoInternalSteps()
    {
        await using var harness = await StoppedAsync($"{Step("next")}, {DemoSession.Threads}, {DemoSession.Stack(72)}");

        await harness.Session.StepOverAsync(1, Ct).WaitAsync(Safety, Ct);

        Assert.Equal($"stopped Step epoch=2 thread=1 loc={Demo}:72:3 hits=[] desc=Step completed", await harness.WaitForEventAsync("stopped Step epoch=2"));
        Assert.Equal(0, InternalStops(harness));
        await DemoSession.EndAsync(harness);
    }

    [Fact]
    public async Task Limit8_Continue()
    {
        // Every internal step out ends in non-user code again: after 8 of them the program just runs.
        var rounds = string.Join(", ", Enumerable.Range(0, 8).Select(_ => $"{Step("stepOut")}, {DemoSession.Threads}, {InLibrary}"));
        await using var harness = await StoppedAsync($$"""
            {{Step("next")}}, {{DemoSession.Threads}}, {{InLibrary}},
            {{rounds}},
            { "on": "continue", "expectArguments": { "threadId": 1 }, "respond": { "allThreadsContinued": true } }
            """);
        var before = harness.SentCommands().Count;

        await harness.Session.StepOverAsync(1, Ct).WaitAsync(Safety, Ct);
        await harness.WaitForResponseAsync("continue");

        var sent = harness.SentCommands().Skip(before).Where(command => command is not ("threads" or "stackTrace")).ToList();
        Assert.Equal(["next", .. Enumerable.Repeat("stepOut", 8), "continue"], sent);
        Assert.Equal(DebugState.Running, harness.Session.State);
        await DemoSession.EndAsync(harness);
        Assert.Equal(9, InternalStops(harness));
        Assert.DoesNotContain(harness.Events.Snapshot(), e => e.Contains("epoch=2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoUserFrameOnTheStack_Continue()
    {
        await using var harness = await StoppedAsync($$"""
            {{Step("stepOut")}}, {{DemoSession.Threads}}, {{NoUserFrame}},
            { "on": "continue", "respond": { "allThreadsContinued": true } }
            """);

        await harness.Session.StepOutAsync(1, Ct).WaitAsync(Safety, Ct);
        await harness.WaitForResponseAsync("continue");

        Assert.Single(harness.SentRequests("stepOut"));
        Assert.Equal(DebugState.Running, harness.Session.State);
        await DemoSession.EndAsync(harness);
        Assert.Equal(1, InternalStops(harness));
    }

    [Theory]
    [InlineData(InLibrary, @"C:\rtl\System.pas:1200:1")]
    [InlineData(InSystem, @"C:\work\DbgDemo\DbgDemo.pas:70:3")]
    public async Task EmptyRoots_Disabled(string stack, string location)
    {
        // Without user source roots the filter is off: the step stops where it ended.
        await using var harness = await StoppedAsync($"{Step("stepIn")}, {DemoSession.Threads}, {stack}", withRoots: false);

        await harness.Session.StepIntoAsync(1, Ct).WaitAsync(Safety, Ct);

        Assert.Equal($"stopped Step epoch=2 thread=1 loc={location} hits=[] desc=Step completed", await harness.WaitForEventAsync("stopped Step epoch=2"));
        Assert.Equal(0, InternalStops(harness));
        Assert.DoesNotContain("stepOut", harness.SentCommands());
        await DemoSession.EndAsync(harness);
    }
}
