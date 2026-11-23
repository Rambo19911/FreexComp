using System.Text.Json;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>The scripted DAP server used by all session tests (debugger testing §3).</summary>
[Trait("Category", "Integration")]
public sealed class FakeAdapterTests
{
    /// <summary>The scenario of the testing specification (§3), with one delayed step.</summary>
    private const string MinimalScenario = """
        {
          "capabilities": { "supportsConfigurationDoneRequest": true, "supportsReadMemoryRequest": true,
                            "supportsFunctionBreakpoints": true, "supportsWriteMemoryRequest": true },
          "initializedAfter": "launch",
          "launchResponseAfter": "configurationDone",
          "steps": [
            { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 69 } ] } },
            { "on": "configurationDone", "respond": {},
              "thenEvents": [ { "event": "stopped", "body": { "reason": "breakpoint", "threadId": 1, "hitBreakpointIds": [1] } } ] },
            { "on": "threads", "delayMs": 1, "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
            { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 100, "name": "Run", "line": 69, "column": 3,
                "source": { "path": "C:\\t\\DbgDemo.pas" } } ], "totalFrames": 1 } },
            { "on": "continue", "respond": { "allThreadsContinued": true },
              "thenEvents": [ { "event": "exited", "body": { "exitCode": 0 } }, { "event": "terminated" } ] }
          ],
          "onUnexpected": "fail"
        }
        """;

    // ---- G-011 ----

    [Fact]
    public async Task Scenario_Minimal_Completes()
    {
        await using var adapter = FakeAdapterRunner.Start(MinimalScenario);

        var initialize = await adapter.RequestAsync("initialize", """{"adapterID":"lldb-dap"}""");
        Assert.True(initialize.GetProperty("success").GetBoolean());
        Assert.Equal(
            """{"supportsConfigurationDoneRequest":true,"supportsReadMemoryRequest":true,"supportsFunctionBreakpoints":true,"supportsWriteMemoryRequest":true}""",
            JsonSerializer.Serialize(initialize.GetProperty("body")));

        await adapter.SendAsync("launch", """{"program":"C:\\t\\DbgDemo.exe"}""");
        Assert.Equal("event:initialized", await adapter.ReadKindAsync());

        var breakpoints = await adapter.RequestAsync("setBreakpoints", """{"source":{"path":"C:\\t\\DbgDemo.pas"},"breakpoints":[{"line":69}]}""");
        Assert.Equal("""{"breakpoints":[{"id":1,"verified":true,"line":69}]}""", JsonSerializer.Serialize(breakpoints.GetProperty("body")));

        await adapter.SendAsync("configurationDone");
        Assert.Equal("response:configurationDone", await adapter.ReadKindAsync());
        Assert.Equal("response:launch", await adapter.ReadKindAsync());
        var stopped = await adapter.ReadAsync();
        Assert.Equal("event", stopped.GetProperty("type").GetString());
        Assert.Equal("stopped", stopped.GetProperty("event").GetString());
        Assert.Equal("""{"reason":"breakpoint","threadId":1,"hitBreakpointIds":[1]}""", JsonSerializer.Serialize(stopped.GetProperty("body")));

        var threads = await adapter.RequestAsync("threads");
        Assert.Equal("main", threads.GetProperty("body").GetProperty("threads")[0].GetProperty("name").GetString());
        var stack = await adapter.RequestAsync("stackTrace", """{"threadId":1,"startFrame":0,"levels":20}""");
        Assert.Equal(@"C:\t\DbgDemo.pas", stack.GetProperty("body").GetProperty("stackFrames")[0].GetProperty("source").GetProperty("path").GetString());

        var resumed = await adapter.RequestAsync("continue", """{"threadId":1}""");
        Assert.True(resumed.GetProperty("body").GetProperty("allThreadsContinued").GetBoolean());
        var exited = await adapter.ReadAsync();
        Assert.Equal("exited", exited.GetProperty("event").GetString());
        Assert.Equal(0, exited.GetProperty("body").GetProperty("exitCode").GetInt32());
        var terminated = await adapter.ReadAsync();
        Assert.Equal("terminated", terminated.GetProperty("event").GetString());
        Assert.False(terminated.TryGetProperty("body", out _));

        var disconnect = await adapter.RequestAsync("disconnect", """{"terminateDebuggee":true}""");
        Assert.True(disconnect.GetProperty("success").GetBoolean());
        Assert.Equal(0, await adapter.WaitForExitAsync());
        Assert.Contains("SCENARIO-COMPLETE", await adapter.StderrAsync());
    }

    [Fact]
    public async Task Scenario_FromEnvironment_WhenNoArgument()
    {
        await using var adapter = FakeAdapterRunner.Start("""{ "steps": [ { "on": "threads", "respond": { "threads": [] } } ] }""", viaEnvironment: true);

        var initialize = await adapter.RequestAsync("initialize");
        // Default capabilities: the four the session needs.
        Assert.Equal(
            """{"supportsConfigurationDoneRequest":true,"supportsReadMemoryRequest":true,"supportsFunctionBreakpoints":true,"supportsWriteMemoryRequest":true}""",
            JsonSerializer.Serialize(initialize.GetProperty("body")));
        await adapter.RequestAsync("threads");
        adapter.CloseInput();

        Assert.Equal(0, await adapter.WaitForExitAsync());
        Assert.Contains("SCENARIO-COMPLETE", await adapter.StderrAsync());
    }

    [Fact]
    public async Task Scenario_StepsLeft_NoCompleteMarker()
    {
        await using var adapter = FakeAdapterRunner.Start("""{ "steps": [ { "on": "threads", "respond": {} } ] }""");

        await adapter.RequestAsync("initialize");
        adapter.CloseInput();

        Assert.Equal(0, await adapter.WaitForExitAsync());
        Assert.DoesNotContain("SCENARIO-COMPLETE", await adapter.StderrAsync());
    }

    [Fact]
    public async Task Steps_MatchedInOrderPerCommand_FailAndNoResponse()
    {
        await using var adapter = FakeAdapterRunner.Start("""
            { "steps": [
                { "on": "evaluate", "expectArguments": { "expression": "&A" }, "respond": { "result": "0x10" } },
                { "on": "threads", "respond": { "threads": [] } },
                { "on": "evaluate", "fail": "no variable named 'B' found in this frame" },
                { "on": "evaluate" },
                { "on": "evaluate", "respond": { "result": "ā€" } } ] }
            """);

        var first = await adapter.RequestAsync("evaluate", """{"expression":"&A","frameId":1,"context":"watch"}""");
        Assert.Equal("0x10", first.GetProperty("body").GetProperty("result").GetString());
        var failed = await adapter.RequestAsync("evaluate", """{"expression":"&B"}""");
        Assert.False(failed.GetProperty("success").GetBoolean());
        Assert.Equal("no variable named 'B' found in this frame", failed.GetProperty("message").GetString());
        Assert.Equal("no variable named 'B' found in this frame", failed.GetProperty("body").GetProperty("error").GetProperty("format").GetString());
        await adapter.RequestAsync("threads");

        // A step with neither "respond" nor "fail" is consumed without an answer: the next message is the answer to the
        // request after it.
        await adapter.SendAsync("evaluate", """{"expression":"&C"}""");
        var last = await adapter.RequestAsync("evaluate", """{"expression":"&D"}""");
        Assert.Equal("ā€", last.GetProperty("body").GetProperty("result").GetString());

        adapter.CloseInput();
        Assert.Equal(0, await adapter.WaitForExitAsync());
        Assert.Contains("SCENARIO-COMPLETE", await adapter.StderrAsync());
    }

    [Fact]
    public async Task Unexpected_FailsWithCode3()
    {
        await using var adapter = FakeAdapterRunner.Start("""
            { "steps": [ { "on": "threads", "respond": { "threads": [] } } ], "onUnexpected": "fail" }
            """);

        await adapter.RequestAsync("threads");
        await adapter.SendAsync("stackTrace", """{"threadId":1}""");

        Assert.Equal(3, await adapter.WaitForExitAsync());
        var stderr = await adapter.StderrAsync();
        Assert.Contains("UNEXPECTED: stackTrace", stderr);
        Assert.DoesNotContain("SCENARIO-COMPLETE", stderr);
    }

    [Fact]
    public async Task Unexpected_DefaultIsFail()
    {
        await using var adapter = FakeAdapterRunner.Start("""{ "steps": [] }""");

        await adapter.SendAsync("threads");

        Assert.Equal(3, await adapter.WaitForExitAsync());
    }

    [Fact]
    public async Task Unexpected_Ignore_RespondsEmptySuccess()
    {
        await using var adapter = FakeAdapterRunner.Start("""{ "steps": [], "onUnexpected": "ignore" }""");

        var response = await adapter.RequestAsync("stackTrace", """{"threadId":1}""");

        Assert.True(response.GetProperty("success").GetBoolean());
        Assert.Equal("{}", JsonSerializer.Serialize(response.GetProperty("body")));
        adapter.CloseInput();
        Assert.Equal(0, await adapter.WaitForExitAsync());
        Assert.Contains("SCENARIO-COMPLETE", await adapter.StderrAsync());
    }

    [Fact]
    public async Task ExpectArguments_SubsetMatches_MismatchFailsWithCode3()
    {
        const string scenario = """
            { "steps": [
                { "on": "launch", "expectArguments": { "program": "C:\\t\\a.exe", "args": ["x", "y"], "env": { "A": "1" }, "stopOnEntry": false },
                  "respond": {} },
                { "on": "launch", "expectArguments": { "program": "C:\\t\\a.exe", "args": ["x", "y"] }, "respond": {} } ],
              "initializedAfter": "initialize", "launchResponseAfter": "launch" }
            """;
        await using var adapter = FakeAdapterRunner.Start(scenario);

        // Extra properties are allowed (subset match), also inside nested objects.
        var ok = await adapter.RequestAsync("launch", """{"program":"C:\\t\\a.exe","args":["x","y"],"cwd":"C:\\t","env":{"A":"1","B":"2"},"stopOnEntry":false}""");
        Assert.True(ok.GetProperty("success").GetBoolean());

        await adapter.SendAsync("launch", """{"program":"C:\\t\\a.exe","args":["x","z"]}""");

        Assert.Equal(3, await adapter.WaitForExitAsync());
        var stderr = await adapter.StderrAsync();
        Assert.Contains("ARGUMENT-MISMATCH: launch at $.args[1]: expected \"y\", actual \"z\"", stderr);
        Assert.DoesNotContain("SCENARIO-COMPLETE", stderr);
    }

    [Fact]
    public async Task BadScenario_ExitsWithCode2()
    {
        await using var adapter = FakeAdapterRunner.Start("""{ "steps": [ { "respond": {} } ] }""");

        Assert.Equal(2, await adapter.WaitForExitAsync());
        Assert.Contains("SCENARIO-ERROR", await adapter.StderrAsync());
    }

    // ---- G-012 ----

    [Fact]
    public async Task Memory_ReadWrite()
    {
        await using var adapter = FakeAdapterRunner.Start("""
            { "memory": [ { "address": "0x1000", "hex": "18000000" }, { "address": "0x1004", "hex": "FFEE" } ], "steps": [] }
            """);

        var read = await adapter.RequestAsync("readMemory", """{"memoryReference":"0x1000","offset":0,"count":4}""");
        Assert.Equal("""{"address":"0x1000","data":"GAAAAA==","unreadableBytes":0}""", JsonSerializer.Serialize(read.GetProperty("body")));

        // Adjacent regions are contiguous; the tail beyond the map is unreadable; the offset is added to the reference.
        var partial = await adapter.RequestAsync("readMemory", """{"memoryReference":"0x1000","offset":2,"count":8}""");
        Assert.Equal("""{"address":"0x1002","data":"AAD/7g==","unreadableBytes":4}""", JsonSerializer.Serialize(partial.GetProperty("body")));

        var unmapped = await adapter.RequestAsync("readMemory", """{"memoryReference":"0x2000","offset":0,"count":4}""");
        Assert.True(unmapped.GetProperty("success").GetBoolean());
        Assert.Equal("""{"address":"0x2000","unreadableBytes":4}""", JsonSerializer.Serialize(unmapped.GetProperty("body")));

        var write = await adapter.RequestAsync("writeMemory", """{"memoryReference":"0x1000","offset":0,"allowPartial":false,"data":"YwAAAA=="}""");
        Assert.Equal("""{"bytesWritten":4}""", JsonSerializer.Serialize(write.GetProperty("body")));
        var reread = await adapter.RequestAsync("readMemory", """{"memoryReference":"0x1000","offset":0,"count":6}""");
        Assert.Equal("YwAAAP/u", reread.GetProperty("body").GetProperty("data").GetString());

        var badWrite = await adapter.RequestAsync("writeMemory", """{"memoryReference":"0x3000","offset":0,"data":"AA=="}""");
        Assert.False(badWrite.GetProperty("success").GetBoolean());

        adapter.CloseInput();
        Assert.Equal(0, await adapter.WaitForExitAsync());
        Assert.Contains("SCENARIO-COMPLETE", await adapter.StderrAsync());
    }

    [Fact]
    public async Task Crash_ExitCode()
    {
        await using var adapter = FakeAdapterRunner.Start("""
            { "steps": [
                { "on": "threads", "respond": { "threads": [] }, "thenEvents": [ { "event": "output", "body": { "output": "bye" } } ], "thenExit": 57 },
                { "on": "stackTrace", "respond": {} } ] }
            """);

        // Response and events are flushed before the simulated crash.
        await adapter.RequestAsync("threads");
        Assert.Equal("event:output", await adapter.ReadKindAsync());

        Assert.Equal(57, await adapter.WaitForExitAsync());
        Assert.DoesNotContain("SCENARIO-COMPLETE", await adapter.StderrAsync());
    }

    [Fact]
    public async Task Crash_WithoutResponse_ExitCode()
    {
        await using var adapter = FakeAdapterRunner.Start("""{ "steps": [ { "on": "launch", "thenExit": 9 } ] }""");

        await adapter.SendAsync("launch", "{}");

        Assert.Equal(9, await adapter.WaitForExitAsync());
        Assert.Contains("SCENARIO-COMPLETE", await adapter.StderrAsync());
    }

    [Theory]
    // initialized right after the initialize response; launch answered at once
    [InlineData("initialize", "launch",
        "response:initialize event:initialized | response:launch | response:configurationDone")]
    // initialized on launch, launch answered only after configurationDone (the real adapter's order, and the default)
    [InlineData("launch", "configurationDone",
        "response:initialize | event:initialized | response:configurationDone response:launch")]
    [InlineData(null, null,
        "response:initialize | event:initialized | response:configurationDone response:launch")]
    // launch answered at once, then initialized
    [InlineData("launch", "launch",
        "response:initialize | response:launch event:initialized | response:configurationDone")]
    [InlineData("initialize", "configurationDone",
        "response:initialize event:initialized | | response:configurationDone response:launch")]
    public async Task Ordering_Variants(string? initializedAfter, string? launchResponseAfter, string expected)
    {
        var switches = (initializedAfter is null ? "" : $"\"initializedAfter\": \"{initializedAfter}\", ")
            + (launchResponseAfter is null ? "" : $"\"launchResponseAfter\": \"{launchResponseAfter}\", ");
        // The trailing "threads" request proves that nothing else was sent in between: its response comes last.
        await using var adapter = FakeAdapterRunner.Start("{ " + switches + """ "steps": [ { "on": "threads", "respond": {} } ] }""");
        var counts = expected.Split('|').Select(part => part.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length).ToArray();
        var actual = new List<string>();

        string[] commands = ["initialize", "launch", "configurationDone"];
        for (var i = 0; i < commands.Length; i++)
        {
            await adapter.SendAsync(commands[i], "{}");
            var kinds = new List<string>();
            for (var n = 0; n < counts[i]; n++)
                kinds.Add(await adapter.ReadKindAsync());
            actual.Add(string.Join(' ', kinds));
        }

        await adapter.SendAsync("threads");
        Assert.Equal("response:threads", await adapter.ReadKindAsync());
        Assert.Equal(
            string.Join("|", expected.Split('|').Select(part => part.Trim())),
            string.Join("|", actual));
    }
}
