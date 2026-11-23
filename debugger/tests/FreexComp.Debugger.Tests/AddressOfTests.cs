using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>"Address of" a root variable through <c>evaluate</c> (DAP usage DBG-082).</summary>
[Trait("Category", "Integration")]
[Trait("Req", "DBG-082")]
public sealed class AddressOfTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SessionHarness> StoppedAsync(string evaluateSteps)
    {
        var harness = SessionHarness.Create($$"""
            { "steps": [
                { "on": "configurationDone", "respond": {}, "thenEvents": [ { "event": "stopped", "body": { "reason": "step", "threadId": 1 } } ] },
                { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
                { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 524288, "name": "Run", "line": 71, "column": 3, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } } ] } },
                {{evaluateSteps}} ] }
            """);
        await harness.LaunchAsync();
        await harness.WaitForEventAsync("stopped");
        return harness;
    }

    private static Task<ulong?> AddressOfAsync(SessionHarness harness, int frameId, string name) =>
        harness.Session.InspectAsync(debuggee => debuggee.AddressOfAsync(frameId, name, Ct), Ct).WaitAsync(Safety, Ct);

    private static async Task EndAsync(SessionHarness harness)
    {
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");
        harness.AssertScenarioComplete();
    }

    [Fact]
    public async Task MemoryReference_Preferred()
    {
        await using var harness = await StoppedAsync("""
            { "on": "evaluate", "expectArguments": { "expression": "&Total", "frameId": 524288, "context": "watch" },
              "respond": { "result": "0x000000A3C01FF7B4", "type": "Integer *", "variablesReference": 4, "memoryReference": "0xA3C01FF7B8" } }
            """);

        var address = await AddressOfAsync(harness, 524288, "Total");

        Assert.Equal(0xA3C01FF7B8ul, address);
        // "&" + the declared spelling, context "watch", the adapter's frame id; nothing else is sent.
        Assert.Equal("""{"expression":"&Total","frameId":524288,"context":"watch"}""", harness.SentArguments("evaluate"));
        await EndAsync(harness);
    }

    [Fact]
    public async Task ParsesResultHex()
    {
        await using var harness = await StoppedAsync("""
            { "on": "evaluate", "expectArguments": { "expression": "&Total" }, "respond": { "result": "0x0000007f3a6ffb84", "type": "Integer *", "variablesReference": 4 } },
            { "on": "evaluate", "expectArguments": { "expression": "&S" }, "respond": { "result": "(string *) 0x000001F3A2B41C60 and 0x10", "variablesReference": 0 } },
            { "on": "evaluate", "expectArguments": { "expression": "&P" }, "respond": { "result": "0X7FF6A1B21234", "memoryReference": "not an address" } }
            """);

        // The pinned adapter sends no memoryReference: the address is the first 0x token of the result.
        Assert.Equal(0x7f3a6ffb84ul, await AddressOfAsync(harness, 524288, "Total"));
        Assert.Equal(0x1F3A2B41C60ul, await AddressOfAsync(harness, 524288, "S"));
        Assert.Equal(0x7FF6A1B21234ul, await AddressOfAsync(harness, 524288, "P"));
        await EndAsync(harness);
    }

    [Fact]
    public async Task Failure_Null()
    {
        await using var harness = await StoppedAsync("""
            { "on": "evaluate", "fail": "no variable named 'Total' found in this frame" },
            { "on": "evaluate", "respond": { "result": "<unavailable>", "variablesReference": 0 } },
            { "on": "evaluate", "respond": { "result": "0x1FFFFFFFFFFFFFFFFF", "variablesReference": 0 } },
            { "on": "evaluate", "respond": { "result": "0x7FF6A1B20010", "variablesReference": 0 } }
            """);

        // A failed request, a result without a 0x token, a token that is no 64-bit address: address unknown.
        Assert.Null(await AddressOfAsync(harness, 524288, "Total"));
        Assert.Null(await AddressOfAsync(harness, 524288, "Total"));
        Assert.Null(await AddressOfAsync(harness, 524288, "Total"));
        // The session stays usable.
        Assert.Equal(DebugState.Stopped, harness.Session.State);
        Assert.Equal(0x7FF6A1B20010ul, await AddressOfAsync(harness, 524288, "Total"));
        await EndAsync(harness);
    }
}
