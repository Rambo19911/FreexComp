using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// Terminal mode of the session, protocol part (G-105): the program runs in a console that the caller's terminal
/// opens, asked for by the adapter through the <c>runInTerminal</c> reverse request. The scripted adapter sends that
/// request (step property <c>thenReverseRequest</c>) and, like the real one, goes on only when it was answered with
/// success; a fake terminal records what the session asked it to start.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Phase", "8")]
public sealed class SessionTerminalTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The reverse request as the pinned adapter sends it: its own executable in launcher mode, one element per argument.</summary>
    internal const string RunInTerminal = """
        { "command": "runInTerminal", "arguments": { "kind": "external", "title": "ignored", "argsCanBeInterpretedByShell": true,
            "cwd": "C:\\work dir",
            "args": [ "C:\\llvm\\bin\\lldb-dap.exe", "--comm-file", "\\\\.\\pipe\\lldb-dap-run-in-terminal-comm-1",
                      "--launch-target", "C:\\work dir\\Demo.exe", "a b", "c\"d", "" ],
            "env": { "KEEP": "1", "DROP": null } } }
        """;

    /// <summary>A launch in terminal mode as the pinned adapter runs it: the request, then (answered) the configuration, the program's process, the launch response.</summary>
    internal static string Scenario(string reverseRequest = RunInTerminal, string more = "") => $$"""
        { "steps": [
          { "on": "launch", "respond": {}, "thenReverseRequest": {{reverseRequest}} },
          { "on": "setExceptionBreakpoints", "respond": {}, "thenEvents": [
            { "event": "process", "body": { "name": "Demo.exe", "systemProcessId": 777, "startMethod": "attach" } } ] }{{more}} ] }
        """;

    internal static LaunchRequest Request(IDebuggeeTerminal? terminal) => new()
    {
        Program = SessionHarness.Program,
        TypeInfoPath = PdiFixture.DbgDemoPath,
        Exceptions = SessionHarness.NoLanguageExceptions,
        Terminal = terminal,
    };

    private static string Escape(string text) => JsonSerializer.Serialize(text, DapJsonText)[1..^1];

    private static readonly JsonSerializerOptions DapJsonText = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The session's answer to a reverse request of this command, as traced.</summary>
    internal static async Task<string> AnswerAsync(SessionHarness harness, string command, int nth = 1)
    {
        var seen = 0;
        return (await harness.Trace.WaitAsync(e => e.Direction == DapDirection.ToAdapter
            && e.Json.Contains("\"type\":\"response\"", StringComparison.Ordinal)
            && e.Json.Contains($"\"command\":\"{command}\"", StringComparison.Ordinal) && ++seen == nth)).Json;
    }

    /// <summary>
    /// Ends a session whose scripted adapter has gone silent after a refusal (it answers nothing, <c>disconnect</c>
    /// neither): the wait for that answer is on the harness clock.
    /// </summary>
    private static async Task TerminateSilentAdapterAsync(SessionHarness harness)
    {
        var end = harness.Session.TerminateAsync(Ct);
        await harness.WaitForRequestAsync("disconnect");
        harness.Clock.Advance(new ShutdownWaits().Disconnect);
        await end.WaitAsync(Safety, Ct);
    }

    private static List<string> DebuggeeOutput(SessionHarness harness) =>
        [.. harness.Events.Snapshot().Where(e => e.StartsWith("output Std", StringComparison.Ordinal))];

    [Fact]
    [Trait("Req", "DBG-540")]
    [Trait("Req", "DBG-541")]
    [Trait("Req", "DBG-542")]
    public async Task TerminalMode_AnnouncesTheCapability_SendsConsoleAndNoStdio()
    {
        await using var harness = SessionHarness.Create(Scenario());

        await harness.LaunchAsync(Request(new FakeTerminal()));

        // DBG-050 with the one capability turned on.
        Assert.Equal(
            """{"seq":1,"type":"request","command":"initialize","arguments":{"clientID":"freexcomp","clientName":"FreexComp","adapterID":"lldb-dap","locale":"en-US","linesStartAt1":true,"columnsStartAt1":true,"pathFormat":"path","supportsVariableType":true,"supportsVariablePaging":false,"supportsRunInTerminalRequest":true,"supportsMemoryReferences":true,"supportsProgressReporting":false,"supportsInvalidatedEvent":false,"supportsMemoryEvent":false,"supportsArgsCanBeInterpretedByShell":false,"supportsStartDebuggingRequest":false,"supportsANSIStyling":false}}""",
            Assert.Single(harness.SentRequests("initialize")));
        // The console option is the last property; no stdio.
        Assert.Equal(
            $$$"""{"seq":2,"type":"request","command":"launch","arguments":{"program":"{{{Escape(SessionHarness.Program)}}}","args":[],"cwd":"{{{Escape(Path.GetDirectoryName(SessionHarness.Program)!)}}}","stopOnEntry":false,"enableAutoVariableSummaries":false,"enableSyntheticChildDebugging":false,"displayExtendedBacktrace":false,"commandEscapePrefix":"`","initCommands":["settings set target.process.thread.step-avoid-regexp \"\""],"sourceMap":[],"console":"externalTerminal"}}""",
            Assert.Single(harness.SentRequests("launch")));
        // Neither the output directory with its files nor the standard-input pipe exists: nothing is redirected.
        Assert.Null(((IDebugSessionDiagnostics)harness.Session).OutputDirectory);
        // (A session without a terminal has them, as before: the byte-exact requests of that mode are in SessionLaunchTests.)
        await using var captured = SessionHarness.Create("{}");
        await captured.LaunchAsync(Request(null));
        Assert.Equal(SessionOutputRelayTests.StdioOf(captured).Directory, ((IDebugSessionDiagnostics)captured.Session).OutputDirectory);
        Assert.DoesNotContain("\"console\"", Assert.Single(captured.SentRequests("launch")), StringComparison.Ordinal);

        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        harness.AssertScenarioComplete();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "DBG-543")]
    [Trait("Req", "DBG-544")]
    [Trait("Req", "DBG-545")]
    [Trait("Req", "DBG-553")]
    [Trait("Req", "DBG-431")]
    public async Task RunInTerminal_StartsTheTerminal_AnswersWithItsProcessId(bool withDirectoryAndEnvironment)
    {
        var reverseRequest = withDirectoryAndEnvironment
            ? RunInTerminal
            // No directory (an empty one counts as none) and an empty environment.
            : """{ "command": "runInTerminal", "arguments": { "kind": "external", "cwd": "", "args": [ "C:\\llvm\\bin\\lldb-dap.exe", "--launch-target", "Demo.exe" ], "env": {} } }""";
        await using var harness = SessionHarness.Create(Scenario(reverseRequest));
        var terminal = new FakeTerminal();

        await harness.LaunchAsync(Request(terminal));

        // Started once, with the command exactly as the adapter gave it: nothing quoted, joined or split.
        var request = Assert.Single(terminal.Requests);
        if (withDirectoryAndEnvironment)
        {
            Assert.Equal(
                [@"C:\llvm\bin\lldb-dap.exe", "--comm-file", @"\\.\pipe\lldb-dap-run-in-terminal-comm-1", "--launch-target", @"C:\work dir\Demo.exe", "a b", "c\"d", ""],
                request.Command);
            Assert.Equal(@"C:\work dir", request.WorkingDirectory);
            // A null value stays null: "remove this variable".
            Assert.NotNull(request.Environment);
            Assert.Equal(
                [new KeyValuePair<string, string?>("DROP", null), new KeyValuePair<string, string?>("KEEP", "1")],
                request.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }
        else
        {
            Assert.Equal([@"C:\llvm\bin\lldb-dap.exe", "--launch-target", "Demo.exe"], request.Command);
            // The directory of the launch: here the program's.
            Assert.Equal(Path.GetDirectoryName(SessionHarness.Program), request.WorkingDirectory);
            Assert.Null(request.Environment);
        }

        // The window's title is the program's file name.
        Assert.Equal(Path.GetFileName(SessionHarness.Program), request.Title);

        // The answer names the console's owner, and only that (the scripted adapter's request had its seq 2).
        Assert.Equal(
            """{"seq":3,"type":"response","request_seq":2,"success":true,"command":"runInTerminal","body":{"shellProcessId":4242}}""",
            await AnswerAsync(harness, "runInTerminal"));

        // The launch completed, with the program's process id - not the terminal's; nothing was captured.
        Assert.Equal(DebugState.Running, harness.Session.State);
        Assert.Equal(777, harness.Session.ProcessId);
        Assert.Equal(["initialize", "launch", "setExceptionBreakpoints", "configurationDone"], harness.SentCommands());
        Assert.Empty(DebuggeeOutput(harness));
        Assert.Equal(["state Idle->Starting", "state Starting->Running"], harness.Events.Snapshot());

        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        Assert.Empty(DebuggeeOutput(harness));
        harness.AssertScenarioComplete();
    }

    [Theory]
    [InlineData("startDebugging")]
    [InlineData("second runInTerminal")]
    [InlineData("runInTerminal in captured mode")]
    [InlineData("runInTerminal after the launch")]
    [Trait("Req", "DBG-543")]
    public async Task OtherReverseRequests_AreRefused(string which)
    {
        var terminal = new FakeTerminal();
        var scenario = which switch
        {
            "startDebugging" => Scenario("""{ "command": "startDebugging", "arguments": { "request": "attach", "configuration": {} } }"""),
            // The second one arrives with the configuration, while the session is still starting.
            "second runInTerminal" => $$"""
                { "steps": [
                  { "on": "launch", "respond": {}, "thenReverseRequest": {{RunInTerminal}} },
                  { "on": "setExceptionBreakpoints", "respond": {}, "thenReverseRequest": {{RunInTerminal}} } ] }
                """,
            "runInTerminal in captured mode" => Scenario(),
            _ => Scenario(more: $$""", { "on": "threads", "respond": { "threads": [] }, "thenReverseRequest": {{RunInTerminal}} }"""),
        };
        await using var harness = SessionHarness.Create(scenario);

        var launch = harness.LaunchAsync(Request(which == "runInTerminal in captured mode" ? null : terminal));
        var command = which == "startDebugging" ? "startDebugging" : "runInTerminal";
        Task? pause = null;
        if (which == "runInTerminal after the launch")
        {
            await launch;
            pause = harness.Session.PauseAsync(Ct);   // asks for the threads: that step sends the request
        }

        var accepted = which is "second runInTerminal" or "runInTerminal after the launch" ? 1 : 0;
        var refusal = await AnswerAsync(harness, command, nth: accepted + 1);

        // Refused as every reverse request was before terminal mode existed.
        Assert.Matches(
            new Regex("^\\{\"seq\":[0-9]+,\"type\":\"response\",\"request_seq\":[0-9]+,\"success\":false,\"command\":\"" + command + "\",\"message\":\"not supported\"\\}$"),
            refusal);
        Assert.Equal(accepted, terminal.Requests.Count);

        // The scripted adapter, like the real one, says nothing more after a refusal.
        await TerminateSilentAdapterAsync(harness);
        Assert.Equal(accepted, terminal.Requests.Count);
        if (pause is not null)
            await Assert.ThrowsAnyAsync<Exception>(() => pause);
        else
            await Assert.ThrowsAsync<DebuggerException>(() => launch);
    }

    [Theory]
    [InlineData("""{ "kind": "external", "cwd": "C:\\work" }""")]
    [InlineData("""{ "args": [] }""")]
    [InlineData("""{ "args": [ "C:\\llvm\\bin\\lldb-dap.exe", 1 ] }""")]
    [InlineData("""{ "args": "C:\\llvm\\bin\\lldb-dap.exe" }""")]
    [InlineData("""{ "args": [ "C:\\llvm\\bin\\lldb-dap.exe" ], "cwd": 5 }""")]
    [InlineData("""{ "args": [ "C:\\llvm\\bin\\lldb-dap.exe" ], "env": { "A": 1 } }""")]
    [Trait("Req", "DBG-543")]
    [Trait("Req", "DBG-558")]
    public async Task InvalidArguments_FailTheLaunch(string arguments)
    {
        await using var harness = SessionHarness.Create(Scenario($$"""{ "command": "runInTerminal", "arguments": {{arguments}} }"""));
        var terminal = new FakeTerminal();

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => harness.LaunchAsync(Request(terminal)));

        Assert.Equal(DebugErrorCode.LaunchFailed, exception.Code);
        Assert.Equal("The debug adapter sent an invalid request for the program's console.", exception.Message);
        Assert.Equal(
            """{"seq":3,"type":"response","request_seq":2,"success":false,"command":"runInTerminal","message":"invalid arguments"}""",
            await AnswerAsync(harness, "runInTerminal"));
        Assert.Empty(terminal.Requests);
        Assert.Equal(
        [
            "state Idle->Starting", "state Starting->Terminated",
            "terminated LaunchFailed null The debug adapter sent an invalid request for the program's console.",
        ], harness.Events.Snapshot());
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
    }

    [Theory]
    [InlineData("throws", "Access is denied.", "Access is denied.")]
    [InlineData("faults", "Access is denied.\r\nSecond line.", "Access is denied.")]
    [InlineData("faults", "", "failed")]
    [InlineData("cancelled", "", "A task was canceled.")]
    [Trait("Req", "DBG-546")]
    [Trait("Req", "DBG-558")]
    [Trait("Req", "DBG-431")]
    public async Task TerminalStartFails_RefusesAndFailsTheLaunch(string how, string message, string reason)
    {
        await using var harness = SessionHarness.Create(Scenario());
        var terminal = new FakeTerminal();
        terminal.OnStart = (_, _) => how switch
        {
            "throws" => throw new Win32Exception(5, message),
            "faults" => Task.FromException<IDebuggeeTerminalRun>(new InvalidOperationException(message)),
            _ => Task.FromCanceled<IDebuggeeTerminalRun>(new CancellationToken(canceled: true)),
        };
        // The Terminated event has been raised before the task faults (DBG-402).
        var eventsAtFault = new List<string>();

        var exception = await Assert.ThrowsAsync<DebuggerException>(async () =>
        {
            try
            {
                await harness.LaunchAsync(Request(terminal));
            }
            finally
            {
                eventsAtFault.AddRange(harness.Events.Snapshot());
            }
        });

        var text = "The program's console window could not be opened: " + reason;
        Assert.Equal(DebugErrorCode.LaunchFailed, exception.Code);
        Assert.Equal(text, exception.Message);
        Assert.Equal(["state Idle->Starting", "state Starting->Terminated", "terminated LaunchFailed null " + text], eventsAtFault);
        // The adapter was told, with the first line of the reason - and not waited for: it says nothing after a refusal.
        Assert.Equal(
            $$"""{"seq":3,"type":"response","request_seq":2,"success":false,"command":"runInTerminal","message":"{{reason}}"}""",
            await AnswerAsync(harness, "runInTerminal"));
        Assert.Single(terminal.Requests);
        Assert.Equal(["initialize", "launch"], harness.SentCommands());
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
        // There is no run: nothing to close.
        Assert.Empty(terminal.Run.Closes);
    }

    [Fact]
    [Trait("Req", "DBG-547")]
    [Trait("Req", "DBG-544")]
    public async Task NoInitializedLimit_LaunchTimeoutStillApplies()
    {
        // The adapter announces that it accepts the configuration only after the console has started and it has
        // attached to the program: the 10 s limit for that announcement does not apply. The start hangs here.
        await using var harness = SessionHarness.Create(Scenario());
        var terminal = new FakeTerminal();
        var pending = new TaskCompletionSource<IDebuggeeTerminalRun>(TaskCreationOptions.RunContinuationsAsynchronously);
        terminal.OnStart = (_, ct) =>
        {
            ct.Register(() => pending.TrySetCanceled(ct));
            return pending.Task;
        };

        var launch = harness.LaunchAsync(Request(terminal));
        var token = await terminal.Started.WaitAsync(Safety, Ct);
        harness.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(DebugState.Starting, harness.Session.State);
        Assert.False(launch.IsCompleted);
        Assert.False(token.IsCancellationRequested);

        // The launch as a whole stays bounded - and what ends it is that limit, not the one for the announcement.
        harness.Clock.Advance(TimeSpan.FromSeconds(20));
        var exception = await Assert.ThrowsAsync<DebuggerException>(() => launch);
        Assert.Equal(DebugErrorCode.RequestTimeout, exception.Code);
        Assert.Equal("The program did not start in time.", exception.Message);
        Assert.Equal("terminated LaunchFailed null The program did not start in time.", harness.Events.Snapshot()[^1]);
        // The pending start was cancelled.
        Assert.True(token.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.Task.WaitAsync(Safety, Ct));
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
    }

    [Theory]
    [InlineData("launch response")]
    [InlineData("process event")]
    [Trait("Req", "DBG-548")]
    [Trait("Req", "DBG-558")]
    public async Task AdapterNeverAsks_FailsTheLaunch(string first)
    {
        // An adapter that starts the program itself gives it no console of its own - and its own standard input.
        var scenario = first == "launch response"
            ? """{ "launchResponseAfter": "launch" }"""
            : """
              { "steps": [ { "on": "setExceptionBreakpoints", "respond": {}, "thenEvents": [
                  { "event": "process", "body": { "name": "Demo.exe", "systemProcessId": 777, "startMethod": "launch" } } ] } ] }
              """;
        await using var harness = SessionHarness.Create(scenario);
        var terminal = new FakeTerminal();

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => harness.LaunchAsync(Request(terminal)));

        Assert.Equal(DebugErrorCode.LaunchFailed, exception.Code);
        Assert.Equal("The debug adapter did not open a console for the program.", exception.Message);
        Assert.Equal("terminated LaunchFailed null The debug adapter did not open a console for the program.", harness.Events.Snapshot()[^1]);
        Assert.DoesNotContain("state Starting->Running", harness.Events.Snapshot());
        Assert.Empty(terminal.Requests);
        // The shutdown ended the adapter and with it the program.
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
    }

    [Theory]
    [InlineData(5, "The program could not be started in its console window (exit code 5).")]
    [InlineData(-1073741515, "The program could not be started in its console window (exit code -1073741515).")]
    [InlineData(null, "The program's console window was closed before the program started.")]
    [Trait("Req", "DBG-549")]
    [Trait("Req", "DBG-558")]
    [Trait("Req", "DBG-433")]
    public async Task CommandEndsBeforeTheProgramRuns_FailsTheLaunch(int? exitCode, string message)
    {
        // The console started and the adapter was told; the adapter waits for the command to report the program -
        // which never comes: the command ended, or the console went away.
        await using var harness = SessionHarness.Create("""{ "initializedAfter": "never", "steps": [ { "on": "launch", "respond": {}, "thenReverseRequest": """ + RunInTerminal + " } ] }");
        var terminal = new FakeTerminal();

        var launch = harness.LaunchAsync(Request(terminal));
        await AnswerAsync(harness, "runInTerminal");
        Assert.Equal(DebugState.Starting, harness.Session.State);
        terminal.Run.End(exitCode);

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => launch);
        Assert.Equal(DebugErrorCode.LaunchFailed, exception.Code);
        Assert.Equal(message, exception.Message);
        Assert.Equal(
            ["state Idle->Starting", "state Starting->Terminated", "terminated LaunchFailed null " + message],
            harness.Events.Snapshot());
        Assert.False(SessionHarness.IsAlive(Assert.NotNull(harness.AdapterProcessId)));
    }
}
