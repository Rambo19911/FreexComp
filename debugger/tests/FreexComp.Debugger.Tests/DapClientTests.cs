using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FreexComp.Debugger.Dap;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>Request/response correlation, timeouts, events, reverse requests and tracing (architecture §4.2).</summary>
public sealed class DapClientTests
{
    /// <summary>Safety net only: every wait below normally completes in milliseconds.</summary>
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- G-008 ----

    [Fact]
    public async Task Request_Success_ReturnsBody_SeqStartsAt1()
    {
        await using var h = new Harness();

        var first = h.Client.RequestAsync("threads", null, RequestTimeout, Ct);
        var request1 = await h.ReceiveAsync();
        var second = h.Client.RequestAsync("scopes", new ScopesArguments(7), RequestTimeout, Ct);
        var request2 = await h.ReceiveAsync();

        Assert.Equal("""{"seq":1,"type":"request","command":"threads"}""", request1.GetRawText());
        Assert.Equal("""{"seq":2,"type":"request","command":"scopes","arguments":{"frameId":7}}""", request2.GetRawText());

        // Answered in reverse order: correlation is by request_seq, not by arrival order.
        await h.SendAsync("""{"seq":1,"type":"response","request_seq":2,"success":true,"command":"scopes","body":{"scopes":[]}}""");
        await h.SendAsync("""{"seq":2,"type":"response","request_seq":1,"success":true,"command":"threads","body":{"threads":[{"id":1,"name":"main"}]}}""");

        Assert.Equal("""{"scopes":[]}""", (await second.WaitAsync(Safety, Ct)).GetRawText());
        Assert.Equal("""{"threads":[{"id":1,"name":"main"}]}""", (await first.WaitAsync(Safety, Ct)).GetRawText());
        Assert.Equal(0, h.Time.ActiveTimers);
    }

    [Fact]
    [Trait("Req", "DBG-053")]
    public async Task Request_OnCompleted_RunsInOrderWithEvents()
    {
        await using var h = new Harness();

        // The callback runs on the reader task before the next message is read: the response keeps its place among the events.
        var task = h.Client.RequestAsync("launch", null, RequestTimeout, Ct,
            completed => h.Order.Enqueue("response:" + completed.Status));
        await h.ReceiveAsync();
        await h.SendAsync("""{"seq":1,"type":"event","event":"initialized"}""");
        await h.SendAsync("""{"seq":2,"type":"response","request_seq":1,"success":true,"command":"launch"}""");
        await h.SendAsync("""{"seq":3,"type":"event","event":"stopped","body":{"reason":"entry"}}""");
        await h.NextEventAsync();
        await h.NextEventAsync();
        await task.WaitAsync(Safety, Ct);

        Assert.Equal(["event:initialized", "response:RanToCompletion", "event:stopped"], h.Order);
    }

    [Fact]
    public async Task Request_OnCompleted_CalledOnceForTimeoutCancellationAndClose()
    {
        await using var h = new Harness();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        void Record(Task<JsonElement> completed) => h.Order.Enqueue(completed.Status.ToString());

        var timedOut = h.Client.RequestAsync("threads", null, RequestTimeout, Ct, Record);
        h.Time.Advance(RequestTimeout);
        await Assert.ThrowsAsync<DebuggerException>(() => timedOut.WaitAsync(Safety, Ct));
        var cancelled = h.Client.RequestAsync("threads", null, RequestTimeout, cancel.Token, Record);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(Safety, Ct));
        var pending = h.Client.RequestAsync("threads", null, RequestTimeout, Ct, Record);
        h.Client.Close();
        await Assert.ThrowsAsync<DebuggerException>(() => pending.WaitAsync(Safety, Ct));
        var afterClose = h.Client.RequestAsync("threads", null, RequestTimeout, Ct, Record);
        await Assert.ThrowsAsync<DebuggerException>(() => afterClose.WaitAsync(Safety, Ct));
        var preCancelled = h.Client.RequestAsync("threads", null, RequestTimeout, cancel.Token, Record);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preCancelled.WaitAsync(Safety, Ct));

        Assert.Equal(["Faulted", "Canceled", "Faulted", "Faulted", "Canceled"], h.Order);
    }

    [Fact]
    public async Task Request_Success_WithoutBody_ReturnsUndefined()
    {
        await using var h = new Harness();

        var task = h.Client.RequestAsync("configurationDone", null, RequestTimeout, Ct);
        await h.ReceiveAsync();
        await h.SendAsync("""{"seq":1,"type":"response","request_seq":1,"success":true,"command":"configurationDone"}""");

        Assert.Equal(JsonValueKind.Undefined, (await task.WaitAsync(Safety, Ct)).ValueKind);
    }

    [Fact]
    [Trait("Req", "DBG-011")]
    public async Task Request_ManyCallers_SentInCallOrder()
    {
        await using var h = new Harness();

        var tasks = Enumerable.Range(1, 50).Select(i => h.Client.RequestAsync("cmd" + i, null, RequestTimeout, Ct)).ToList();

        for (var i = 1; i <= 50; i++)
        {
            var request = await h.ReceiveAsync();
            Assert.Equal(i, request.GetProperty("seq").GetInt32());
            Assert.Equal("cmd" + i, request.GetProperty("command").GetString());
            await h.SendAsync($$"""{"seq":0,"type":"response","request_seq":{{i}},"success":true,"command":"cmd{{i}}"}""");
        }

        await Task.WhenAll(tasks).WaitAsync(Safety, Ct);
    }

    [Theory]
    [InlineData("""{"seq":0,"type":"response","request_seq":1,"success":false,"command":"launch","message":"short text","body":{"error":{"id":1,"format":"'{path}' is not a valid executable ({code})","variables":{"path":"C:\\work\\x.exe","code":"193"},"showUser":true}}}""",
        @"'C:\work\x.exe' is not a valid executable (193)")]
    [InlineData("""{"seq":0,"type":"response","request_seq":1,"success":false,"command":"launch","message":"no variable named 'Total' found in this frame"}""",
        "no variable named 'Total' found in this frame")]
    [InlineData("""{"seq":0,"type":"response","request_seq":1,"success":false,"command":"launch","body":{}}""",
        "launch failed")]
    [InlineData("""{"seq":0,"type":"response","request_seq":1,"command":"launch"}""",
        "launch failed")]
    public async Task Request_ErrorResponse_MapsMessage(string response, string expectedMessage)
    {
        await using var h = new Harness();

        var task = h.Client.RequestAsync("launch", null, RequestTimeout, Ct);
        await h.ReceiveAsync();
        await h.SendAsync(response);

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => task.WaitAsync(Safety, Ct));
        Assert.Equal(DebugErrorCode.RequestFailed, exception.Code);
        Assert.Equal(expectedMessage, exception.Message);
        Assert.Equal("launch", exception.DapCommand);
        Assert.Equal(0, h.Time.ActiveTimers);
    }

    [Fact]
    public async Task Request_UnknownResponse_Dropped()
    {
        await using var h = new Harness();

        // Responses nobody waits for (unknown request_seq, or none at all) are ignored and break nothing.
        await h.SendAsync("""{"seq":1,"type":"response","request_seq":99,"success":true,"command":"threads"}""");
        await h.SendAsync("""{"seq":2,"type":"response","success":true,"command":"threads"}""");
        var task = h.Client.RequestAsync("threads", null, RequestTimeout, Ct);
        await h.ReceiveAsync();
        await h.SendAsync("""{"seq":3,"type":"response","request_seq":1,"success":true,"command":"threads","body":{"ok":true}}""");

        Assert.Equal("""{"ok":true}""", (await task.WaitAsync(Safety, Ct)).GetRawText());
        Assert.False(h.Closed.Task.IsCompleted);
    }

    // ---- G-009 ----

    [Fact]
    public async Task Request_Timeout_FaultsRequestTimeout_LateResponseDropped()
    {
        await using var h = new Harness();

        var task = h.Client.RequestAsync("stackTrace", new StackTraceArguments(1, 0, 20), RequestTimeout, Ct);
        await h.ReceiveAsync();
        h.Time.Advance(RequestTimeout - TimeSpan.FromMilliseconds(1));
        Assert.False(task.IsCompleted);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => task.WaitAsync(Safety, Ct));
        Assert.Equal(DebugErrorCode.RequestTimeout, exception.Code);
        Assert.Equal("stackTrace", exception.DapCommand);
        Assert.Equal("Debug adapter did not answer 'stackTrace' in time.", exception.Message);

        // The late response is discarded; the client keeps working.
        await h.SendAsync("""{"seq":1,"type":"response","request_seq":1,"success":true,"command":"stackTrace","body":{"late":true}}""");
        var next = h.Client.RequestAsync("threads", null, RequestTimeout, Ct);
        Assert.Equal(2, (await h.ReceiveAsync()).GetProperty("seq").GetInt32());
        await h.SendAsync("""{"seq":2,"type":"response","request_seq":2,"success":true,"command":"threads","body":{"late":false}}""");
        Assert.Equal("""{"late":false}""", (await next.WaitAsync(Safety, Ct)).GetRawText());
        Assert.Equal(0, h.Time.ActiveTimers);
    }

    [Fact]
    [Trait("Req", "DBG-414")]
    public async Task Request_Cancelled_FaultsOperationCanceled_LateResponseDropped()
    {
        await using var h = new Harness();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var task = h.Client.RequestAsync("variables", new VariablesArguments(1), RequestTimeout, cts.Token);
        await h.ReceiveAsync();
        cts.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Safety, Ct));
        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.Equal(0, h.Time.ActiveTimers);

        await h.SendAsync("""{"seq":1,"type":"response","request_seq":1,"success":true,"command":"variables"}""");
        var next = h.Client.RequestAsync("threads", null, RequestTimeout, Ct);
        await h.ReceiveAsync();
        await h.SendAsync("""{"seq":2,"type":"response","request_seq":2,"success":true,"command":"threads"}""");
        await next.WaitAsync(Safety, Ct);
    }

    [Fact]
    public async Task Request_AlreadyCancelled_SendsNothing()
    {
        await using var h = new Harness();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => h.Client.RequestAsync("threads", null, RequestTimeout, new CancellationToken(canceled: true)));

        // The next request is the first one on the wire.
        _ = h.Client.RequestAsync("scopes", new ScopesArguments(1), RequestTimeout, Ct);
        var request = await h.ReceiveAsync();
        Assert.Equal("scopes", request.GetProperty("command").GetString());
        Assert.Equal(1, request.GetProperty("seq").GetInt32());
    }

    [Fact]
    public async Task Eof_FailsPending_AdapterCrashed()
    {
        await using var h = new Harness();

        var first = h.Client.RequestAsync("threads", null, RequestTimeout, Ct);
        var second = h.Client.RequestAsync("stackTrace", new StackTraceArguments(1, 0, 20), RequestTimeout, Ct);
        await h.ReceiveAsync();
        await h.ReceiveAsync();
        h.CloseAdapterOutput();

        foreach (var (task, command) in new[] { (first, "threads"), (second, "stackTrace") })
        {
            var exception = await Assert.ThrowsAsync<DebuggerException>(() => task.WaitAsync(Safety, Ct));
            Assert.Equal(DebugErrorCode.AdapterCrashed, exception.Code);
            Assert.Equal("Debug adapter exited unexpectedly.", exception.Message);
            Assert.Equal(command, exception.DapCommand);
        }

        Assert.Null(await h.Closed.Task.WaitAsync(Safety, Ct));   // plain end of stream, no protocol error
        var late = await Assert.ThrowsAsync<DebuggerException>(() => h.Client.RequestAsync("threads", null, RequestTimeout, Ct));
        Assert.Equal(DebugErrorCode.AdapterCrashed, late.Code);
        Assert.Equal(0, h.Time.ActiveTimers);
        await h.Client.Completion.WaitAsync(Safety, Ct);
    }

    [Theory]
    [InlineData("{not json", "Protocol error: malformed JSON: ")]
    [InlineData("""{"seq":1,"event":"stopped"}""", "Protocol error: message without type")]
    [InlineData("""[1,2]""", "Protocol error: message without type")]
    [InlineData("""{"seq":1,"type":"gossip"}""", "Protocol error: unknown message type: gossip")]
    public async Task ProtocolError_FailsPending_AndReportsClosed(string message, string expectedStart)
    {
        await using var h = new Harness();

        var pending = h.Client.RequestAsync("threads", null, RequestTimeout, Ct);
        await h.ReceiveAsync();
        await h.SendAsync(message);

        var closed = await h.Closed.Task.WaitAsync(Safety, Ct);
        Assert.NotNull(closed);
        Assert.Equal(DebugErrorCode.ProtocolError, closed.Code);
        Assert.StartsWith(expectedStart, closed.Message);
        var exception = await Assert.ThrowsAsync<DebuggerException>(() => pending.WaitAsync(Safety, Ct));
        Assert.Equal(DebugErrorCode.AdapterCrashed, exception.Code);
        Assert.StartsWith(expectedStart, exception.Message);
    }

    [Fact]
    public async Task Close_ByOwner_FailsPending_WithoutClosedCallback()
    {
        await using var h = new Harness();

        var pending = h.Client.RequestAsync("threads", null, RequestTimeout, Ct);
        await h.ReceiveAsync();
        h.Client.Close();

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => pending.WaitAsync(Safety, Ct));
        Assert.Equal(DebugErrorCode.AdapterCrashed, exception.Code);
        h.CloseAdapterOutput();
        await h.Client.Completion.WaitAsync(Safety, Ct);
        Assert.False(h.Closed.Task.IsCompleted);
    }

    // ---- G-010 ----

    [Fact]
    public async Task Events_Ordered()
    {
        await using var h = new Harness();

        var request = h.Client.RequestAsync("threads", null, RequestTimeout, Ct);
        await h.ReceiveAsync();
        for (var i = 0; i < 200; i++)
        {
            await h.SendAsync($$$"""{"seq":{{{i}}},"type":"event","event":"output","body":{"category":"stdout","output":"{{{i}}}"}}""");
            if (i == 100)
                await h.SendAsync("""{"seq":0,"type":"response","request_seq":1,"success":true,"command":"threads"}""");
        }

        await h.SendAsync("""{"seq":999,"type":"event","event":"terminated"}""");

        for (var i = 0; i < 200; i++)
        {
            var (name, body) = await h.NextEventAsync();
            Assert.Equal("output", name);
            Assert.Equal(i.ToString(System.Globalization.CultureInfo.InvariantCulture), body.GetProperty("output").GetString());
        }

        var (last, lastBody) = await h.NextEventAsync();
        Assert.Equal("terminated", last);
        Assert.Equal(JsonValueKind.Undefined, lastBody.ValueKind);
        await request.WaitAsync(Safety, Ct);
    }

    [Fact]
    public async Task ReverseRequest_Rejected()
    {
        await using var h = new Harness();

        await h.SendAsync("""{"seq":5,"type":"request","command":"runInTerminal","arguments":{"args":["x"]}}""");

        Assert.Equal(
            """{"seq":1,"type":"response","request_seq":5,"success":false,"command":"runInTerminal","message":"not supported"}""",
            (await h.ReceiveAsync()).GetRawText());

        // The rejection used seq 1; our own next request continues the same counter.
        _ = h.Client.RequestAsync("threads", null, RequestTimeout, Ct);
        Assert.Equal(2, (await h.ReceiveAsync()).GetProperty("seq").GetInt32());
        Assert.False(h.Closed.Task.IsCompleted);
    }

    [Fact]
    [Trait("Req", "DBG-012")]
    public async Task Trace_BothDirections()
    {
        var trace = new ConcurrentQueue<DapTraceEntry>();
        await using var h = new Harness(trace.Enqueue);
        h.Time.Advance(TimeSpan.FromSeconds(5));
        var now = h.Time.GetUtcNow();

        var request = h.Client.RequestAsync("threads", null, RequestTimeout, Ct);
        await h.ReceiveAsync();
        // Reported before sending: the entry exists as soon as the adapter side has the bytes.
        Assert.Equal([new DapTraceEntry(now, DapDirection.ToAdapter, """{"seq":1,"type":"request","command":"threads"}""")], trace);

        const string response = """{"seq":1,"type":"response","request_seq":1,"success":true,"command":"threads","body":{"threads":[]}}""";
        const string @event = """{"seq":2,"type":"event","event":"output","body":{"output":"ā"}}""";
        const string reverse = """{"seq":3,"type":"request","command":"startDebugging"}""";
        await h.SendAsync(response);
        await h.SendAsync(@event);
        await h.SendAsync(reverse);
        await request.WaitAsync(Safety, Ct);
        await h.NextEventAsync();
        await h.ReceiveAsync();

        Assert.Equal(
        [
            new DapTraceEntry(now, DapDirection.ToAdapter, """{"seq":1,"type":"request","command":"threads"}"""),
            new DapTraceEntry(now, DapDirection.FromAdapter, response),
            new DapTraceEntry(now, DapDirection.FromAdapter, @event),
            new DapTraceEntry(now, DapDirection.FromAdapter, reverse),
            new DapTraceEntry(now, DapDirection.ToAdapter, """{"seq":2,"type":"response","request_seq":3,"success":false,"command":"startDebugging","message":"not supported"}"""),
        ], trace);
    }

    [Fact]
    public async Task Trace_SinkThrows_ClientUnaffected()
    {
        await using var h = new Harness(_ => throw new InvalidOperationException("sink"));

        var request = h.Client.RequestAsync("threads", null, RequestTimeout, Ct);
        await h.ReceiveAsync();
        await h.SendAsync("""{"seq":1,"type":"response","request_seq":1,"success":true,"command":"threads"}""");

        await request.WaitAsync(Safety, Ct);
    }

    /// <summary>A client connected to a hand-driven adapter side through two in-memory pipes.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly Pipe _toAdapter = new();
        private readonly Pipe _fromAdapter = new();
        private readonly DapTransport _adapterSide;
        private readonly Channel<(string Name, JsonElement Body)> _events = Channel.CreateUnbounded<(string, JsonElement)>();

        public Harness(Action<DapTraceEntry>? trace = null)
        {
            var clientSide = new DapTransport(_fromAdapter.Reader.AsStream(), _toAdapter.Writer.AsStream());
            _adapterSide = new DapTransport(_toAdapter.Reader.AsStream(), _fromAdapter.Writer.AsStream());
            Client = new DapClient(clientSide, (name, body) =>
            {
                Order.Enqueue("event:" + name);
                _events.Writer.TryWrite((name, body));
            }, Closed.SetResult, trace, Time);
            Client.Start();
        }

        public ManualTimeProvider Time { get; } = new();
        public DapClient Client { get; }

        /// <summary>Events ("event:name") and whatever a test adds, in the order the reader task produced them.</summary>
        public ConcurrentQueue<string> Order { get; } = new();
        public TaskCompletionSource<DebuggerException?> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The next message the client wrote.</summary>
        public async Task<JsonElement> ReceiveAsync()
        {
            var body = await _adapterSide.ReadAsync(Ct).WaitAsync(Safety, Ct);
            Assert.NotNull(body);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }

        public Task SendAsync(string json) => _adapterSide.SendAsync(Encoding.UTF8.GetBytes(json), Ct);

        public async Task<(string Name, JsonElement Body)> NextEventAsync() =>
            await _events.Reader.ReadAsync(Ct).AsTask().WaitAsync(Safety, Ct);

        /// <summary>End of the adapter's output stream: the adapter exited.</summary>
        public void CloseAdapterOutput() => _fromAdapter.Writer.Complete();

        public async ValueTask DisposeAsync()
        {
            Client.Close();
            _fromAdapter.Writer.Complete();
            _toAdapter.Reader.Complete();
            await Client.Completion.WaitAsync(Safety, CancellationToken.None);
        }
    }
}
