using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>Launch sequence against the scripted adapter (DAP usage §2, §3; architecture DBG-013, DBG-014).</summary>
[Trait("Category", "Integration")]
public sealed class SessionLaunchTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "DBG-402")]
    public async Task Launch_RunsToRunning()
    {
        await using var harness = SessionHarness.Create("{}");

        await harness.LaunchAsync();

        Assert.Equal(DebugState.Running, harness.Session.State);
        Assert.Null(harness.Session.CurrentStop);
        Assert.True(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
        // The constant initialize request (DBG-050).
        Assert.Equal(
            """{"seq":1,"type":"request","command":"initialize","arguments":{"clientID":"freexcomp","clientName":"FreexComp","adapterID":"lldb-dap","locale":"en-US","linesStartAt1":true,"columnsStartAt1":true,"pathFormat":"path","supportsVariableType":true,"supportsVariablePaging":false,"supportsRunInTerminalRequest":false,"supportsMemoryReferences":true,"supportsProgressReporting":false,"supportsInvalidatedEvent":false,"supportsMemoryEvent":false,"supportsArgsCanBeInterpretedByShell":false,"supportsStartDebuggingRequest":false,"supportsANSIStyling":false}}""",
            Assert.Single(harness.SentRequests("initialize")));
        Assert.Equal("""{"seq":3,"type":"request","command":"setExceptionBreakpoints","arguments":{"filters":[]}}""",
            Assert.Single(harness.SentRequests("setExceptionBreakpoints")));
        Assert.Equal("""{"seq":4,"type":"request","command":"configurationDone"}""", Assert.Single(harness.SentRequests("configurationDone")));

        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");

        Assert.Equal(["initialize", "launch", "setExceptionBreakpoints", "configurationDone", "disconnect"], harness.SentCommands());
        Assert.Equal(
        [
            "state Idle->Starting", "state Starting->Running", "state Running->Terminating", "state Terminating->Terminated",
            "terminated TerminatedByUser null null",
        ], harness.Events.Snapshot());
        harness.AssertScenarioComplete();
    }

    [Theory]
    [InlineData("supportsConfigurationDoneRequest", """{ "supportsReadMemoryRequest": true }""")]
    [InlineData("supportsReadMemoryRequest", """{ "supportsConfigurationDoneRequest": true, "supportsReadMemoryRequest": false }""")]
    [Trait("Req", "DBG-402")]
    public async Task MissingCapability_Fails(string capability, string capabilities)
    {
        await using var harness = SessionHarness.Create($$"""{ "capabilities": {{capabilities}} }""");
        // The Terminated event has been raised before the task faults (DBG-402).
        var eventsAtFault = new List<string>();

        var exception = await Assert.ThrowsAsync<DebuggerException>(async () =>
        {
            try
            {
                await harness.LaunchAsync();
            }
            finally
            {
                eventsAtFault.AddRange(harness.Events.Snapshot());
            }
        });

        Assert.Equal(DebugErrorCode.MissingCapability, exception.Code);
        Assert.Equal("Debug adapter lacks a required capability: " + capability, exception.Message);
        Assert.Equal(DebugState.Terminated, harness.Session.State);
        Assert.Equal(
        [
            "state Idle->Starting", "state Starting->Terminated",
            "terminated LaunchFailed null Debug adapter lacks a required capability: " + capability,
        ], eventsAtFault);
        Assert.Equal(["initialize"], harness.SentCommands());
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
    }

    [Fact]
    [Trait("Req", "DBG-402")]
    public async Task ProgramNotFound_Fails()
    {
        await using var harness = SessionHarness.Create("{}");
        var program = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "missing-" + Guid.NewGuid().ToString("N") + ".exe");

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => harness.LaunchAsync(new LaunchRequest { Program = program }));

        Assert.Equal(DebugErrorCode.ProgramNotFound, exception.Code);
        Assert.Equal("Program not found: " + program, exception.Message);
        Assert.Equal(DebugState.Terminated, harness.Session.State);
        // Checked before the adapter is started.
        Assert.Null(harness.AdapterProcessId);
        Assert.Empty(harness.Trace.Snapshot());
        Assert.Equal(
            ["state Idle->Starting", "state Starting->Terminated", "terminated LaunchFailed null Program not found: " + program],
            harness.Events.Snapshot());
    }

    [Fact]
    [Trait("Req", "DBG-004")]
    public async Task Launch_AdapterMissing_FailsAdapterNotFound()
    {
        var adapter = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "no-adapter-" + Guid.NewGuid().ToString("N") + ".exe");
        await using var harness = SessionHarness.Create("{}", configure: o => o with { AdapterPath = adapter });

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => harness.LaunchAsync());

        Assert.Equal(DebugErrorCode.AdapterNotFound, exception.Code);
        Assert.Equal("Debug adapter not found: " + adapter, exception.Message);
        Assert.Equal(DebugState.Terminated, harness.Session.State);
        Assert.Equal("terminated LaunchFailed null Debug adapter not found: " + adapter, harness.Events.Snapshot()[^1]);
    }

    [Theory]
    [InlineData("launch")]
    [InlineData("initialize")]
    [Trait("Req", "DBG-053")]
    public async Task Launch_InitializedBeforeLaunchResponse_Completes(string initializedAfter)
    {
        await using var harness = SessionHarness.Create(
            $$"""{ "initializedAfter": "{{initializedAfter}}", "launchResponseAfter": "configurationDone" }""");

        await harness.LaunchAsync();

        Assert.Equal(DebugState.Running, harness.Session.State);
        Assert.Equal(["initialize", "launch", "setExceptionBreakpoints", "configurationDone"], harness.SentCommands());
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-053")]
    public async Task Launch_ResponseBeforeInitialized_Completes()
    {
        await using var harness = SessionHarness.Create("""{ "initializedAfter": "launch", "launchResponseAfter": "launch" }""");

        await harness.LaunchAsync();

        Assert.Equal(DebugState.Running, harness.Session.State);
        Assert.Equal(["initialize", "launch", "setExceptionBreakpoints", "configurationDone"], harness.SentCommands());
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-056")]
    public async Task Launch_NoInitialized_Fails()
    {
        await using var harness = SessionHarness.Create("""{ "initializedAfter": "never", "launchResponseAfter": "launch" }""");

        var launch = harness.LaunchAsync();
        // The 10 s limit starts with the initialize response; it is armed before the launch request goes out.
        await harness.WaitForRequestAsync("launch");
        Assert.False(launch.IsCompleted);
        harness.Clock.Advance(TimeSpan.FromSeconds(10));

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => launch);
        Assert.Equal(DebugErrorCode.LaunchFailed, exception.Code);
        Assert.Equal("Debug adapter did not become ready.", exception.Message);
        Assert.Equal(DebugState.Terminated, harness.Session.State);
        Assert.Equal("terminated LaunchFailed null Debug adapter did not become ready.", harness.Events.Snapshot()[^1]);
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
        Assert.DoesNotContain("configurationDone", harness.SentCommands());
    }

    [Fact]
    [Trait("Req", "DBG-055")]
    public async Task Launch_Timeout_Terminates()
    {
        // The launch request is never answered: configuration completes, the debuggee never reports running.
        await using var harness = SessionHarness.Create("""{ "steps": [ { "on": "launch" } ] }""");

        var launch = harness.LaunchAsync();
        await harness.WaitForResponseAsync("configurationDone");
        harness.Clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(launch.IsCompleted);
        Assert.Equal(DebugState.Starting, harness.Session.State);
        harness.Clock.Advance(TimeSpan.FromSeconds(1));

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => launch);
        Assert.Equal(DebugErrorCode.RequestTimeout, exception.Code);
        Assert.Equal("launch", exception.DapCommand);
        Assert.Equal(DebugState.Terminated, harness.Session.State);
        Assert.Equal(
            ["state Idle->Starting", "state Starting->Terminated", "terminated LaunchFailed null " + exception.Message],
            harness.Events.Snapshot());
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
    }

    [Fact]
    [Trait("Req", "DBG-402")]
    public async Task Launch_ResponseFails_LaunchFailedWithAdapterMessage()
    {
        await using var harness = SessionHarness.Create(
            """{ "steps": [ { "on": "launch", "fail": "'C:\\work\\x.exe' is not a valid executable" } ] }""");

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => harness.LaunchAsync());

        Assert.Equal(DebugErrorCode.LaunchFailed, exception.Code);
        Assert.Equal(@"'C:\work\x.exe' is not a valid executable", exception.Message);
        Assert.Equal("launch", exception.DapCommand);
        Assert.Equal(DebugState.Terminated, harness.Session.State);
        Assert.Equal(@"terminated LaunchFailed null 'C:\work\x.exe' is not a valid executable", harness.Events.Snapshot()[^1]);
    }

    [Fact]
    [Trait("Req", "DBG-414")]
    public async Task Launch_Cancelled_TerminatesWithLaunchFailedCancelled()
    {
        await using var harness = SessionHarness.Create("""{ "initializedAfter": "never", "steps": [ { "on": "launch" } ] }""");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var launch = harness.Session.LaunchAsync(new LaunchRequest { Program = SessionHarness.Program }, cancel.Token);
        await harness.WaitForRequestAsync("launch");
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launch.WaitAsync(Safety, Ct));
        Assert.Equal(DebugState.Terminated, harness.Session.State);
        Assert.Equal("terminated LaunchFailed null Cancelled", harness.Events.Snapshot()[^1]);
    }

    [Fact]
    [Trait("Req", "DBG-057")]
    public async Task LaunchArguments_ExactJson()
    {
        // The adapter checks the values (subset match); the trace is compared as text: no other property is sent.
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "launch", "respond": {}, "expectArguments": {
                "program": "PROGRAM", "args": ["--verbose", "input file.txt"], "cwd": "C:\\work dir", "env": { "MY_VAR": "1" },
                "stopOnEntry": false, "enableAutoVariableSummaries": false, "enableSyntheticChildDebugging": false,
                "displayExtendedBacktrace": false, "commandEscapePrefix": "`",
                "initCommands": ["settings set target.process.thread.step-avoid-regexp \"\""], "sourceMap": [] } } ] }
            """.Replace("PROGRAM", Escape(SessionHarness.Program)));

        await harness.LaunchAsync(new LaunchRequest
        {
            Program = SessionHarness.Program,
            Arguments = ["--verbose", "input file.txt"],
            WorkingDirectory = @"C:\work dir",
            Environment = new Dictionary<string, string> { ["MY_VAR"] = "1" },
        });

        Assert.Equal(
            $$"""{"program":"{{Escape(SessionHarness.Program)}}","args":["--verbose","input file.txt"],"cwd":"C:\\work dir","env":{"MY_VAR":"1"},"stopOnEntry":false,"enableAutoVariableSummaries":false,"enableSyntheticChildDebugging":false,"displayExtendedBacktrace":false,"commandEscapePrefix":"`","initCommands":["settings set target.process.thread.step-avoid-regexp \"\""],"sourceMap":[],"stdio":{{Stdio(harness)}}}""",
            harness.SentArguments("launch"));
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-057")]
    public async Task LaunchArguments_Defaults_CwdIsProgramDirectory_NoEnv()
    {
        await using var harness = SessionHarness.Create("{}");

        await harness.LaunchAsync();

        Assert.Equal(
            $$"""{"program":"{{Escape(SessionHarness.Program)}}","args":[],"cwd":"{{Escape(Path.GetDirectoryName(SessionHarness.Program)!)}}","stopOnEntry":false,"enableAutoVariableSummaries":false,"enableSyntheticChildDebugging":false,"displayExtendedBacktrace":false,"commandEscapePrefix":"`","initCommands":["settings set target.process.thread.step-avoid-regexp \"\""],"sourceMap":[],"stdio":{{Stdio(harness)}}}""",
            harness.SentArguments("launch"));
    }

    /// <summary>The <c>stdio</c> value sent: stdin from the session's own pipe (DBG-059), stdout and stderr to its own files (DBG-058).</summary>
    private static string Stdio(SessionHarness harness)
    {
        var (directory, stdout, stderr) = SessionOutputRelayTests.StdioOf(harness);
        return $"[\"{Escape(SessionOutputRelayTests.StdinOf(directory))}\",\"{Escape(stdout)}\",\"{Escape(stderr)}\"]";
    }

    private static string Escape(string path) => path.Replace(@"\", @"\\");
}
