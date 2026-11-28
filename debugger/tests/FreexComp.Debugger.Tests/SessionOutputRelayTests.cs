using System.Text.Json;
using System.Text.RegularExpressions;
using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// The session's own relay of the debuggee's output (DAP usage DBG-058, mode <c>Files</c>): the scripted adapter writes
/// to the files the launch request names, as the debuggee would. The harness clock drives the periodic reads; the
/// final read happens before the end of the session is reported, whatever the clock does.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SessionOutputRelayTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The launch request's <c>stdio</c>: stdout and stderr in a fresh directory of their own, stdin the session's pipe
    /// named after that directory - never left to the adapter, whose own standard input carries the protocol (DBG-059).
    /// </summary>
    internal static (string Directory, string Stdout, string Stderr) StdioOf(SessionHarness harness)
    {
        var stdio = JsonDocument.Parse(harness.SentArguments("launch")).RootElement.GetProperty("stdio");
        Assert.Equal(3, stdio.GetArrayLength());
        var stdout = stdio[1].GetString()!;
        var stderr = stdio[2].GetString()!;
        var directory = Path.GetDirectoryName(stdout)!;
        Assert.Equal(Path.Combine(directory, "out.txt"), stdout);
        Assert.Equal(Path.Combine(directory, "err.txt"), stderr);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "FreexComp"), Path.GetDirectoryName(directory));
        Assert.Matches(new Regex($"^dbg-{Environment.ProcessId}-[0-9]+$"), Path.GetFileName(directory));
        Assert.Equal(StdinOf(directory), stdio[0].GetString());
        return (directory, stdout, stderr);
    }

    /// <summary>The path of the standard-input pipe that goes with an output directory.</summary>
    internal static string StdinOf(string directory) => @"\\.\pipe\" + StdinPipeName(directory);

    private static string StdinPipeName(string directory) => "FreexComp." + Path.GetFileName(directory) + ".in";

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DBG-059")]
    public async Task Stdin_IsAPipeOfTheSession_SilentUntilTheSessionEnds()
    {
        // G-103: what the program reads as its standard input is a pipe the session holds and never writes to. A
        // reader - here the test, in the program's place - gets nothing while the session lasts, and the end of the
        // input when the session ends.
        await using var harness = SessionHarness.Create("{}");
        await harness.LaunchAsync();
        var name = StdinPipeName(StdioOf(harness).Directory);

        using var reader = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.In);
        reader.Connect(Safety);
        var received = new MemoryStream();
        var read = DedicatedThread.Run("FreexComp.Tests.DebuggeeStdin", () => reader.CopyTo(received));

        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);

        await read.WaitAsync(Safety, Ct);
        Assert.Equal(0, received.Length);
        // The pipe is gone with the session.
        using var late = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.In);
        Assert.Throws<TimeoutException>(() => late.Connect(0));
    }

    /// <summary>The default request of the harness with <see cref="LaunchRequest.InputAtEnd"/>.</summary>
    private static LaunchRequest InputAtEndRequest => new()
    {
        Program = SessionHarness.Program,
        TypeInfoPath = PdiFixture.DbgDemoPath,
        Exceptions = SessionHarness.NoLanguageExceptions,
        InputAtEnd = true,
    };

    /// <summary>Whether a pipe of that name exists; asking does not connect to it.</summary>
    private static bool PipeExists(string name) => Directory.GetFiles(@"\\.\pipe\", name).Length == 1;

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DBG-557")]
    [Trait("Req", "DBG-434")]
    public async Task InputAtEnd_ClosesThePipeOnceItHasAClient()
    {
        // G-109: with InputAtEnd the session closes its end of the pipe as soon as the program's side - here the test,
        // in the program's place - has opened it and the launch request is out: a read reports the end of the input
        // while the session runs on.
        await using var harness = SessionHarness.Create("""
            { "steps": [
              { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
              { "on": "pause", "respond": {}, "thenEvents": [
                { "write": "stdout", "text": "after the end of the input\n" },
                { "event": "terminated" } ] } ] }
            """);
        await harness.LaunchAsync(InputAtEndRequest);
        // The path stays in stdio[0]: nothing else of captured mode changes.
        var name = StdinPipeName(StdioOf(harness).Directory);

        using var reader = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.In);
        reader.Connect(Safety);
        var received = new MemoryStream();
        await DedicatedThread.Run("FreexComp.Tests.DebuggeeStdin", () => reader.CopyTo(received)).WaitAsync(Safety, Ct);

        Assert.Equal(0, received.Length);
        Assert.Equal(DebugState.Running, harness.Session.State);
        Assert.DoesNotContain(harness.Events.Snapshot(), e => e.StartsWith("state Running->", StringComparison.Ordinal));
        Assert.False(PipeExists(name), "The session still holds its end of the standard-input pipe.");

        // The output files still relay.
        await harness.Session.PauseAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");
        Assert.Equal([Out("Stdout", "after the end of the input\n")], DebuggeeOutput(harness));
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DBG-557")]
    public async Task InputAtEnd_WithoutAClient_PipeStaysUntilTheEnd()
    {
        // The end is never closed before a client is connected: the adapter could then not open the path and would
        // give the program its own input (DBG-059). The scripted adapter never opens it: the pipe is there after the
        // launch, at a stop, and goes with the session.
        await using var harness = SessionHarness.Create("""
            { "steps": [
              { "on": "configurationDone", "respond": {}, "thenEvents": [
                { "event": "stopped", "body": { "reason": "breakpoint", "threadId": 1 } } ] },
              { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
              { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 100, "name": "Run", "line": 69, "column": 3,
                  "source": { "path": "C:\\t\\DbgDemo.pas" } } ], "totalFrames": 1 } } ] }
            """);
        await harness.LaunchAsync(InputAtEndRequest);
        var name = StdinPipeName(StdioOf(harness).Directory);
        Assert.True(PipeExists(name), "The standard-input pipe was closed although nothing had opened it.");

        await harness.WaitForEventAsync("stopped");
        Assert.True(PipeExists(name), "The standard-input pipe was closed although nothing had opened it.");

        // A late client is still the program's side: it gets the end of the input, not a pipe that is gone.
        using var reader = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.In);
        reader.Connect(Safety);
        var received = new MemoryStream();
        await DedicatedThread.Run("FreexComp.Tests.DebuggeeStdin", () => reader.CopyTo(received)).WaitAsync(Safety, Ct);
        Assert.Equal(0, received.Length);
        Assert.Equal(DebugState.Stopped, harness.Session.State);

        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        Assert.False(PipeExists(name));
        harness.AssertScenarioComplete();
    }

    /// <summary>An <c>Output</c> event as the harness records it.</summary>
    private static string Out(string category, string text) => $"output {category} {JsonSerializer.Serialize(text)}";

    /// <summary>The debuggee's output events, in order.</summary>
    private static List<string> DebuggeeOutput(SessionHarness harness) =>
        [.. harness.Events.Snapshot().Where(e => e.StartsWith("output Std", StringComparison.Ordinal))];

    [Fact]
    [Trait("Req", "DBG-058")]
    [Trait("Req", "DBG-421")]
    public async Task OutputAppendedAfterExited_IsDeliveredBeforeTerminated()
    {
        // The program's last line reaches the file after the adapter reported the exit (its reader is not ours): it
        // still comes before the end of the session. The clock never moves: only the final read can deliver it.
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [
                { "write": "stdout", "text": "first\r\n" },
                { "event": "exited", "body": { "exitCode": 0 } },
                { "write": "stdout", "text": "last\r\n" },
                { "event": "terminated" } ] } ] }
            """);

        await harness.LaunchAsync();
        await harness.WaitForEventAsync("terminated");

        // The final read is the last thing before the end: the adapter and the program are gone.
        Assert.Equal(
            ["state Running->Terminating", Out("Stdout", "first\r\nlast\r\n"), "state Terminating->Terminated", "terminated Exited 0 null"],
            harness.Events.Snapshot().TakeLast(4));
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-058")]
    [Trait("Req", "DBG-424")]
    public async Task Utf8SequenceSplitAcrossReads_IsDecodedWhole()
    {
        // "a" and the first two bytes of the euro sign are read while the program runs; the third byte comes later.
        await using var harness = SessionHarness.Create("""
            { "steps": [
              { "on": "configurationDone", "respond": {}, "thenEvents": [
                { "write": "stdout", "hex": "61E282" },
                { "event": "output", "body": { "category": "console", "output": "written" } } ] },
              { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
              { "on": "pause", "respond": {}, "thenEvents": [
                { "write": "stdout", "hex": "AC0A" },
                { "event": "terminated" } ] } ] }
            """);

        await harness.LaunchAsync();
        await harness.WaitForEventAsync(Out("Debugger", "written"));
        harness.Clock.Advance(OutputRelay.PollInterval);
        await harness.WaitForEventAsync("output Stdout");
        await harness.Session.PauseAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");

        Assert.Equal([Out("Stdout", "a"), Out("Stdout", "€\n")], DebuggeeOutput(harness));
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-058")]
    [Trait("Req", "DBG-421")]
    public async Task OutputWrittenBeforeAStop_IsDeliveredBeforeStopped()
    {
        // The clock never moves: what the program wrote before the stop is read when the stop is published.
        await using var harness = SessionHarness.Create("""
            { "steps": [
              { "on": "configurationDone", "respond": {}, "thenEvents": [
                { "write": "stdout", "text": "before the stop\n" },
                { "event": "stopped", "body": { "reason": "breakpoint", "threadId": 1 } } ] },
              { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
              { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 100, "name": "Run", "line": 69, "column": 3,
                  "source": { "path": "C:\\t\\DbgDemo.pas" } } ], "totalFrames": 1 } } ] }
            """);

        await harness.LaunchAsync();
        await harness.WaitForEventAsync("stopped");

        var events = harness.Events.Snapshot().ToList();
        var stopped = events.FindIndex(e => e.StartsWith("stopped", StringComparison.Ordinal));
        Assert.Equal(Out("Stdout", "before the stop\n"), events[stopped - 2]);
        Assert.EndsWith("->Stopped", events[stopped - 1], StringComparison.Ordinal);
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-058")]
    public async Task SequenceCutByTheEnd_IsReplaced()
    {
        // Nothing completes a sequence once the program is gone: the bytes give U+FFFD.
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [
                { "write": "stdout", "hex": "6F6BE282" },
                { "event": "terminated" } ] } ] }
            """);

        await harness.LaunchAsync();
        await harness.WaitForEventAsync("terminated");

        Assert.Equal([Out("Stdout", "ok�")], DebuggeeOutput(harness));
    }

    [Fact]
    [Trait("Req", "DBG-058")]
    [Trait("Req", "DBG-424")]
    public async Task Stderr_IsItsOwnFileAndCategory()
    {
        await using var harness = SessionHarness.Create("""
            { "steps": [
              { "on": "configurationDone", "respond": {}, "thenEvents": [
                { "write": "stderr", "text": "oops\n" },
                { "write": "stdout", "text": "out 1\n" },
                { "event": "output", "body": { "category": "console", "output": "written" } } ] },
              { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
              { "on": "pause", "respond": {}, "thenEvents": [
                { "write": "stdout", "text": "out 2\n" },
                { "write": "stderr", "text": "Grüße\n" },
                { "event": "terminated" } ] } ] }
            """);

        await harness.LaunchAsync();
        await harness.WaitForEventAsync(Out("Debugger", "written"));
        harness.Clock.Advance(OutputRelay.PollInterval);
        await harness.WaitForEventAsync("output Stderr");
        await harness.Session.PauseAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");

        // Each read reports stdout first, then stderr; within a category the order is the program's.
        Assert.Equal(
            [Out("Stdout", "out 1\n"), Out("Stderr", "oops\n"), Out("Stdout", "out 2\n"), Out("Stderr", "Grüße\n")],
            DebuggeeOutput(harness));
        harness.AssertScenarioComplete();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "DBG-058")]
    public async Task Directory_IsFreshAndRemovedAtTheEnd(bool naturalExit)
    {
        // Two sessions at a time each have a directory of their own; it exists while the program runs and is gone
        // when the end is reported, after a natural exit as after Terminate.
        var scenario = naturalExit
            ? """
              { "steps": [ { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
                { "on": "pause", "respond": {}, "thenEvents": [ { "write": "stdout", "text": "bye" }, { "event": "terminated" } ] } ] }
              """
            : "{}";
        await using var harness = SessionHarness.Create(scenario);
        await using var other = SessionHarness.Create("{}");
        await harness.LaunchAsync();
        await other.LaunchAsync();

        var (directory, stdout, stderr) = StdioOf(harness);
        var otherDirectory = StdioOf(other).Directory;
        Assert.NotEqual(directory, otherDirectory);
        Assert.True(File.Exists(stdout));
        Assert.True(File.Exists(stderr));
        var atEnd = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Session.Terminated += (_, _) => atEnd.TrySetResult(Directory.Exists(directory));

        if (naturalExit)
            await harness.Session.PauseAsync(Ct).WaitAsync(Safety, Ct);
        else
            await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);

        Assert.False(await atEnd.Task.WaitAsync(Safety, Ct), "The output directory still existed when the end was reported.");
        Assert.Equal(naturalExit ? [Out("Stdout", "bye")] : [], DebuggeeOutput(harness));
        Assert.True(Directory.Exists(otherDirectory));
        await other.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        Assert.False(Directory.Exists(otherDirectory));
    }

    [Fact]
    [Trait("Req", "DBG-058")]
    public async Task FailedLaunch_RemovesTheDirectory()
    {
        await using var harness = SessionHarness.Create("""{ "steps": [ { "on": "launch", "fail": "'x.exe' is not a valid executable" } ] }""");

        await Assert.ThrowsAsync<DebuggerException>(() => harness.LaunchAsync());

        Assert.False(Directory.Exists(StdioOf(harness).Directory));
    }
}
