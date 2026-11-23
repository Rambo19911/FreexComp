using FreexComp.Debugger.Tests.TestSupport;
using FreexComp.Debugger.Values;

namespace FreexComp.Debugger.Tests;

/// <summary>Debuggee memory: requests, block cache, splitting, unreadable ranges (DAP usage DBG-086 … DBG-088).</summary>
public sealed class MemoryReaderTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Readable address ranges with a position-dependent content; records every request.</summary>
    private sealed class Source
    {
        private readonly List<(ulong From, ulong To)> _regions = [];

        public List<string> Requests { get; } = [];

        public Source Readable(ulong from, ulong to)
        {
            _regions.Add((from, to));
            return this;
        }

        public static byte At(ulong address) => (byte)(address * 7 + (address >> 8) * 13);

        public static byte[] Expected(ulong address, int count) => [.. Enumerable.Range(0, count).Select(i => At(address + (ulong)i))];

        public Task<byte[]?> Fetch(ulong address, int count, CancellationToken ct)
        {
            Requests.Add($"{address:X}+{count}");
            var readable = 0;
            while (readable < count && _regions.Any(r => r.From <= address + (ulong)readable && address + (ulong)readable < r.To))
                readable++;
            return Task.FromResult(readable == 0 ? null : Expected(address, readable));
        }
    }

    [Fact]
    [Trait("Req", "DBG-088")]
    public async Task Read_Cached()
    {
        var source = new Source().Readable(0x20000, 0x20400);
        var reader = new MemoryReader(source.Fetch);

        Assert.Equal(Source.Expected(0x20010, 4), await reader.ReadAsync(0x20010, 4, Ct));
        // The whole 256-byte block around the read was fetched, aligned to 256.
        Assert.Equal(["20000+256"], source.Requests);

        // Anything else inside the block is served without a request.
        Assert.Equal(Source.Expected(0x20020, 8), await reader.ReadAsync(0x20020, 8, Ct));
        Assert.Equal(Source.Expected(0x200FF, 1), await reader.ReadAsync(0x200FF, 1, Ct));
        Assert.Equal(Source.Expected(0x20000, 256), await reader.ReadAsync(0x20000, 256, Ct));
        Assert.Equal(Source.Expected(0x20010, 4), await reader.ReadAsync(0x20010, 4, Ct));
        Assert.Empty(await reader.ReadAsync(0x20010, 0, Ct) ?? [1]);
        Assert.Equal(["20000+256"], source.Requests);
        Assert.Equal(1, reader.CachedBlockCount);

        // A result is the caller's own copy.
        (await reader.ReadAsync(0x20010, 4, Ct))![0] ^= 0xFF;
        Assert.Equal(Source.Expected(0x20010, 4), await reader.ReadAsync(0x20010, 4, Ct));

        // The cache lives for one stop.
        reader.Clear();
        Assert.Equal(0, reader.CachedBlockCount);
        Assert.Equal(Source.Expected(0x20010, 4), await reader.ReadAsync(0x20010, 4, Ct));
        Assert.Equal(["20000+256", "20000+256"], source.Requests);
    }

    [Fact]
    [Trait("Req", "DBG-088")]
    public async Task Read_SpansBlocks()
    {
        var source = new Source().Readable(0x20000, 0x21000);
        var reader = new MemoryReader(source.Fetch);

        // Two neighbouring blocks: one request for both.
        Assert.Equal(Source.Expected(0x200F0, 0x20), await reader.ReadAsync(0x200F0, 0x20, Ct));
        Assert.Equal(["20000+512"], source.Requests);
        Assert.Equal(Source.Expected(0x20100, 256), await reader.ReadAsync(0x20100, 256, Ct));
        Assert.Equal(["20000+512"], source.Requests);

        // Cached blocks in the middle of a read are not fetched again: only the gaps are.
        Assert.Equal(Source.Expected(0x20400, 1), await reader.ReadAsync(0x20400, 1, Ct));
        source.Requests.Clear();
        Assert.Equal(Source.Expected(0x20301, 0x2FE), await reader.ReadAsync(0x20301, 0x2FE, Ct));
        Assert.Equal(["20300+256", "20500+256"], source.Requests);
        Assert.Equal(5, reader.CachedBlockCount);
    }

    [Fact]
    [Trait("Req", "DBG-086")]
    public async Task Read_Large_SplitInto64KRequests()
    {
        var source = new Source().Readable(0x30000, 0x60000);
        var reader = new MemoryReader(source.Fetch);

        var data = await reader.ReadAsync(0x30010, 150000, Ct);

        Assert.Equal(Source.Expected(0x30010, 150000), data);
        // No request exceeds 65536 bytes.
        Assert.Equal(["30000+65536", "40000+65536", "50000+18944"], source.Requests);

        // More blocks than the cache holds: the read still succeeds, the cache stays bounded.
        reader.Clear();
        var huge = await reader.ReadAsync(0x100000, 5000 * 256, Ct);
        Assert.Null(huge);   // not readable at all
        source.Readable(0x100000, 0x100000 + 5000 * 256);
        reader.Clear();
        Assert.Equal(Source.Expected(0x100000, 5000 * 256), await reader.ReadAsync(0x100000, 5000 * 256, Ct));
        Assert.InRange(reader.CachedBlockCount, 1, MemoryReader.MaxBlocks);
    }

    [Fact]
    [Trait("Req", "DBG-087")]
    public async Task Unreadable_ReturnsNull()
    {
        // 0x20000..0x20100 is one readable block; 0x20100 onwards is not mapped; 0x40000..0x40010 ends inside a block.
        var source = new Source().Readable(0x20000, 0x20100).Readable(0x40000, 0x40010);
        var reader = new MemoryReader(source.Fetch);

        // Nothing readable: the block request and one request for the exact range, then no more requests.
        Assert.Null(await reader.ReadAsync(0x50000, 4, Ct));
        Assert.Equal(["50000+256", "50000+4"], source.Requests);
        Assert.Null(await reader.ReadAsync(0x50000, 4, Ct));
        Assert.Null(await reader.ReadAsync(0x50000, 4, Ct));
        Assert.Equal(["50000+256", "50000+4"], source.Requests);

        // A read that runs into unreadable memory is not satisfied in full: null, never a short result.
        source.Requests.Clear();
        Assert.Null(await reader.ReadAsync(0x200F0, 0x20, Ct));
        Assert.Equal(["20000+512", "200F0+32"], source.Requests);
        // The readable block of that request is kept; a sibling value in it needs no request.
        Assert.Equal(Source.Expected(0x200F0, 0x10), await reader.ReadAsync(0x200F0, 0x10, Ct));
        Assert.Equal(["20000+512", "200F0+32"], source.Requests);

        // Memory that ends inside a block: exactly the readable range can still be read (and is remembered).
        source.Requests.Clear();
        Assert.Equal(Source.Expected(0x40008, 8), await reader.ReadAsync(0x40008, 8, Ct));
        Assert.Equal(["40000+256", "40008+8"], source.Requests);
        Assert.Equal(Source.Expected(0x40008, 8), await reader.ReadAsync(0x40008, 8, Ct));
        Assert.Null(await reader.ReadAsync(0x40008, 9, Ct));
        Assert.Equal(["40000+256", "40008+8", "40008+9"], source.Requests);

        // A negative count is not a read.
        Assert.Null(await reader.ReadAsync(0x20000, -1, Ct));
    }

    [Fact]
    [Trait("Req", "DBG-088")]
    public async Task LowAddresses_NotRequested()
    {
        var source = new Source().Readable(0, 0x20000);
        var reader = new MemoryReader(source.Fetch);

        // The nil-pointer page 0 … 0xFFFF is unreadable by definition.
        Assert.Null(await reader.ReadAsync(0, 8, Ct));
        Assert.Null(await reader.ReadAsync(8, 4, Ct));
        Assert.Null(await reader.ReadAsync(0xFFFF, 1, Ct));
        Assert.Null(await reader.ReadAsync(0xFFF8, 16, Ct));
        Assert.Empty(source.Requests);

        Assert.Equal(Source.Expected(0x10000, 4), await reader.ReadAsync(0x10000, 4, Ct));
        Assert.Equal(["10000+256"], source.Requests);

        // The end of the address space cannot be read either.
        Assert.Null(await reader.ReadAsync(ulong.MaxValue - 3, 8, Ct));
        Assert.Null(await reader.ReadAsync(ulong.MaxValue - 7, 8, Ct));
        Assert.Equal(["10000+256"], source.Requests);
    }

    // ---- over the adapter ----

    private const string StopAtStart = """
        { "on": "configurationDone", "respond": {}, "thenEvents": [ { "event": "stopped", "body": { "reason": "step", "threadId": 1 } } ] },
        { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
        { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 1000, "name": "Run", "line": 71, "column": 3, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } } ] } }
        """;

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-086")]
    [Trait("Req", "DBG-087")]
    public async Task Session_ReadMemory_RequestShape_CachePerStop()
    {
        var block = string.Concat(Enumerable.Range(0, 512).Select(i => ((byte)i).ToString("X2")));
        await using var harness = SessionHarness.Create($$"""
            { "memory": [ { "address": "0xA3C01FF600", "hex": "{{block}}" }, { "address": "0x7FF6A1B20010", "hex": "18000000FFFFFFFF" } ],
              "steps": [ {{StopAtStart}},
                { "on": "next", "respond": {}, "thenEvents": [ { "event": "stopped", "body": { "reason": "step", "threadId": 1 } } ] },
                { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
                { "on": "stackTrace", "respond": { "stackFrames": [] } } ] }
            """);
        var session = harness.Session;

        await harness.LaunchAsync();
        await harness.WaitForEventAsync("stopped");

        var bytes = await session.ReadMemoryAsync(0xA3C01FF7B4, 4, Ct).WaitAsync(Safety, Ct);
        Assert.Equal([0xB4, 0xB5, 0xB6, 0xB7], bytes);
        Assert.Equal([0xFE, 0xFF, 0x00, 0x01], await session.ReadMemoryAsync(0xA3C01FF6FE, 4, Ct).WaitAsync(Safety, Ct));
        Assert.Empty(await session.ReadMemoryAsync(0xA3C01FF6FE, 0, Ct).WaitAsync(Safety, Ct));
        // "0x", upper-case digits, no leading zeros; offset always 0; the aligned 256-byte block (and the cache hit after it).
        Assert.Equal(
            ["""{"memoryReference":"0xA3C01FF700","offset":0,"count":256}""", """{"memoryReference":"0xA3C01FF600","offset":0,"count":256}"""],
            harness.SentRequests("readMemory").Select(Arguments));

        // Memory smaller than a block: the exact range is read.
        Assert.Equal([0x18, 0, 0, 0], await session.ReadMemoryAsync(0x7FF6A1B20010, 4, Ct).WaitAsync(Safety, Ct));
        Assert.Equal(
            ["""{"memoryReference":"0x7FF6A1B20000","offset":0,"count":256}""", """{"memoryReference":"0x7FF6A1B20010","offset":0,"count":4}"""],
            harness.SentRequests("readMemory").Skip(2).Select(Arguments));

        // Not readable in full: MemoryUnreadable; the nil page without any request.
        var sent = harness.SentRequests("readMemory").Count;
        var exception = await Assert.ThrowsAsync<DebuggerException>(() => session.ReadMemoryAsync(0x7FF6A1B20014, 8, Ct).WaitAsync(Safety, Ct));
        Assert.Equal(DebugErrorCode.MemoryUnreadable, exception.Code);
        Assert.Equal("Memory at $00007FF6A1B20014 (8 bytes) cannot be read.", exception.Message);
        Assert.Equal(sent + 1, harness.SentRequests("readMemory").Count);
        exception = await Assert.ThrowsAsync<DebuggerException>(() => session.ReadMemoryAsync(0x10, 8, Ct).WaitAsync(Safety, Ct));
        Assert.Equal(DebugErrorCode.MemoryUnreadable, exception.Code);
        Assert.Equal(sent + 1, harness.SentRequests("readMemory").Count);

        // The blocks belong to one stop: after a step the same read asks the adapter again.
        await session.StepOverAsync(1, Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("stopped Step epoch=2");
        sent = harness.SentRequests("readMemory").Count;
        Assert.Equal(bytes, await session.ReadMemoryAsync(0xA3C01FF7B4, 4, Ct).WaitAsync(Safety, Ct));
        Assert.Equal(sent + 1, harness.SentRequests("readMemory").Count);

        await session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-401")]
    public async Task Session_ReadMemory_WhileRunning_InvalidState()
    {
        await using var harness = SessionHarness.Create("{}");
        await harness.LaunchAsync();

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => harness.Session.ReadMemoryAsync(0x20000, 4, Ct).WaitAsync(Safety, Ct));

        Assert.Equal(DebugErrorCode.InvalidState, exception.Code);
        Assert.Empty(harness.SentRequests("readMemory"));
    }

    private static string Arguments(string requestJson) =>
        System.Text.Json.JsonDocument.Parse(requestJson).RootElement.GetProperty("arguments").GetRawText();
}
