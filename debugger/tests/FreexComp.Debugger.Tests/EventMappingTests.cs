using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>Adapter events mapped to public events (DAP usage §8, §9, §10; debug-session DBG-424).</summary>
[Trait("Category", "Integration")]
public sealed class EventMappingTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Launches; the adapter then sends <paramref name="events"/> and ends the program. Returns the public events after launch.</summary>
    private static async Task<(SessionHarness Harness, List<string> Events)> RunAsync(string events)
    {
        var harness = SessionHarness.Create($$"""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [ {{events}},
                { "event": "exited", "body": { "exitCode": 0 } }, { "event": "terminated" } ] } ] }
            """);
        await harness.LaunchAsync();
        await harness.WaitForEventAsync("terminated");
        harness.AssertScenarioComplete();
        var lines = harness.Events.Snapshot().Where(e => !e.StartsWith("state ", StringComparison.Ordinal) && !e.StartsWith("terminated", StringComparison.Ordinal)).ToList();
        return (harness, lines);
    }

    [Fact]
    [Trait("Req", "DBG-424")]
    public async Task Output_Categories()
    {
        var (harness, events) = await RunAsync("""
            { "event": "output", "body": { "category": "stdout", "output": "Dog Rex\r\n" } },
            { "event": "output", "body": { "category": "stderr", "output": "oops" } },
            { "event": "output", "body": { "category": "stdout", "output": "Zoë €" } },
            { "event": "output", "body": { "category": "console", "output": "Launched\n" } },
            { "event": "output", "body": { "category": "important", "output": "note" } },
            { "event": "output", "body": { "output": "no category" } },
            { "event": "output", "body": { "category": "somethingNew", "output": "other", "group": "start", "variablesReference": 4, "line": 3 } }
            """);
        await using var _ = harness;

        // Text unchanged (chunking and line ends as received), in adapter order.
        Assert.Equal(
        [
            """output Stdout "Dog Rex\r\n" """.TrimEnd(),
            """output Stderr "oops" """.TrimEnd(),
            "output Stdout " + System.Text.Json.JsonSerializer.Serialize("Zoë €"),
            """output Debugger "Launched\n" """.TrimEnd(),
            """output Debugger "note" """.TrimEnd(),
            """output Debugger "no category" """.TrimEnd(),
            """output Debugger "other" """.TrimEnd(),
        ], events);
    }

    [Fact]
    public async Task Telemetry_Dropped()
    {
        var (harness, events) = await RunAsync("""
            { "event": "output", "body": { "category": "telemetry", "output": "usage", "data": { "x": 1 } } },
            { "event": "output", "body": { "category": "stdout", "output": "after" } },
            { "event": "memory", "body": { "memoryReference": "0x10", "offset": 0, "count": 4 } },
            { "event": "loadedSource", "body": { "reason": "new", "source": { "path": "C:\\x.pas" } } },
            { "event": "progressStart", "body": { "progressId": "1", "title": "t" } },
            { "event": "invalidated" }
            """);
        await using var _ = harness;

        // Telemetry is never shown; unknown events are ignored.
        Assert.Equal(["""output Stdout "after" """.TrimEnd()], events);
        Assert.Equal(DebugState.Terminated, harness.Session.State);
    }

    [Fact]
    [Trait("Req", "DBG-051")]
    public async Task Module_New_Raises()
    {
        var (harness, events) = await RunAsync("""
            { "event": "module", "body": { "reason": "new", "module": { "id": "1A2B", "name": "DbgDemo.exe", "path": "C:\\work\\DbgDemo.exe", "symbolStatus": "Symbols LOADED." } } },
            { "event": "module", "body": { "reason": "new", "module": { "id": 7, "name": "ntdll.dll" } } },
            { "event": "module", "body": { "reason": "new", "module": { "id": 8, "name": "kernel32.dll", "path": "C:\\Windows\\System32\\kernel32.dll", "symbolStatus": "Symbols not found." } } },
            { "event": "module", "body": { "reason": "changed", "module": { "id": 7, "name": "ntdll.dll", "path": "C:\\Windows\\System32\\ntdll.dll", "symbolStatus": "Symbols loaded (PDB)." } } },
            { "event": "module", "body": { "reason": "removed", "module": { "id": 8, "name": "kernel32.dll" } } }
            """);
        await using var _ = harness;

        Assert.Equal(
        [
            @"module DbgDemo.exe|C:\work\DbgDemo.exe|True",
            "module ntdll.dll||False",
            @"module kernel32.dll|C:\Windows\System32\kernel32.dll|False",
            @"module ntdll.dll|C:\Windows\System32\ntdll.dll|True",   // changed: raised again
            // removed: no event
        ], events);
    }

    [Fact]
    public async Task Process_SetsPid()
    {
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [
                { "event": "process", "body": { "name": "C:\\work\\DbgDemo.exe", "systemProcessId": 4242, "isLocalProcess": true, "startMethod": "launch" } },
                { "event": "output", "body": { "category": "stdout", "output": "marker" } } ] } ] }
            """);
        Assert.Null(harness.Session.ProcessId);

        await harness.LaunchAsync();
        // Events are processed in order: when the output arrives the process event has been applied.
        await harness.WaitForEventAsync("output");

        Assert.Equal(4242, harness.Session.ProcessId);
        Assert.Equal(DebugState.Running, harness.Session.State);
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        Assert.Equal(4242, harness.Session.ProcessId);
        harness.AssertScenarioComplete();
    }

    [Fact]
    public async Task Thread_StartedExited()
    {
        var (harness, events) = await RunAsync("""
            { "event": "thread", "body": { "reason": "started", "threadId": 8120 } },
            { "event": "thread", "body": { "reason": "somethingElse", "threadId": 1 } },
            { "event": "thread", "body": { "reason": "exited", "threadId": 8120 } }
            """);
        await using var _ = harness;

        Assert.Equal(["thread 8120 Started", "thread 8120 Exited"], events);
    }

    [Fact]
    [Trait("Req", "DBG-051")]
    public async Task Capabilities_Event_MergesIntoStoredCapabilities()
    {
        // The adapter announces supportTerminateDebuggee only later: disconnect then carries terminateDebuggee.
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [
                { "event": "capabilities", "body": { "capabilities": { "supportTerminateDebuggee": true } } },
                { "event": "output", "body": { "category": "console", "output": "marker" } } ] } ] }
            """);
        await harness.LaunchAsync();
        await harness.WaitForEventAsync("output");

        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);

        Assert.Equal("""{"terminateDebuggee":true}""", harness.SentArguments("disconnect"));
    }

    [Fact]
    [Trait("Req", "DBG-423")]
    public async Task Handler_Throws_ReportedOnce_SessionContinues()
    {
        await using var harness = SessionHarness.Create("""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [
                { "event": "thread", "body": { "reason": "started", "threadId": 5 } },
                { "event": "thread", "body": { "reason": "exited", "threadId": 5 } },
                { "event": "exited", "body": { "exitCode": 0 } }, { "event": "terminated" } ] } ] }
            """);
        harness.Session.ThreadChanged += (_, e) =>
        {
            if (e.Change == ThreadChange.Started)
                throw new InvalidOperationException("handler bug");
        };

        await harness.LaunchAsync();
        await harness.WaitForEventAsync("terminated");

        Assert.Equal(
            ["thread 5 Started", """output Debugger "Event handler failed: InvalidOperationException: handler bug" """.TrimEnd(), "thread 5 Exited"],
            harness.Events.Snapshot().Where(e => e.StartsWith("thread", StringComparison.Ordinal) || e.StartsWith("output", StringComparison.Ordinal)));
        Assert.Equal("terminated Exited 0 null", harness.Events.Snapshot()[^1]);
    }
}
