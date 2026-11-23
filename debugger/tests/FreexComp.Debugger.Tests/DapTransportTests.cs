using System.Text;
using FreexComp.Debugger.Dap;

namespace FreexComp.Debugger.Tests;

/// <summary>Base-protocol framing (architecture §4.1).</summary>
public sealed class DapTransportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Frame(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        return [.. Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), .. body];
    }

    private static DapTransport Reader(params byte[][] chunks) => new(new ChunkedStream(chunks), Stream.Null);

    private static async Task<string?> ReadTextAsync(DapTransport transport)
    {
        var body = await transport.ReadAsync(Ct);
        return body is null ? null : Encoding.UTF8.GetString(body);
    }

    // ---- reading (G-005) ----

    [Fact]
    public async Task Read_SingleMessage_ReturnsBodyThenEof()
    {
        var transport = Reader(Frame("""{"seq":1,"type":"event","event":"initialized"}"""));

        Assert.Equal("""{"seq":1,"type":"event","event":"initialized"}""", await ReadTextAsync(transport));
        Assert.Null(await ReadTextAsync(transport));
        Assert.Null(await ReadTextAsync(transport));
    }

    [Fact]
    public async Task Read_TwoMessagesInOneBuffer_ReturnsBoth()
    {
        var transport = Reader([.. Frame("""{"a":1}"""), .. Frame("""{"b":2}""")]);

        Assert.Equal("""{"a":1}""", await ReadTextAsync(transport));
        Assert.Equal("""{"b":2}""", await ReadTextAsync(transport));
        Assert.Null(await ReadTextAsync(transport));
    }

    [Fact]
    public async Task Read_HeaderSplitAcrossReads_Reassembled()
    {
        // One byte per read: the header name, the digits, the CR LF CR LF terminator and the body are all split.
        var bytes = Frame("""{"seq":7,"type":"event","event":"terminated"}""");
        var transport = Reader(bytes.Select(b => new[] { b }).ToArray());

        Assert.Equal("""{"seq":7,"type":"event","event":"terminated"}""", await ReadTextAsync(transport));
        Assert.Null(await ReadTextAsync(transport));
    }

    [Fact]
    public async Task Read_Utf8SplitAcrossReads_DecodesIntact()
    {
        // "ā" is 2 bytes, "€" 3 bytes, the emoji 4 bytes; Content-Length counts bytes, not characters.
        const string json = """{"output":"ā€😀"}""";
        var bytes = Frame(json);
        var split = bytes.Length - 6;   // inside the 4-byte sequence
        var transport = Reader(bytes[..split], bytes[split..]);

        Assert.Equal(json, await ReadTextAsync(transport));
    }

    [Fact]
    public async Task Read_UnknownHeaderLines_Ignored()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        var transport = Reader([.. Encoding.ASCII.GetBytes("X-Extra: 1\r\nContent-Length: 2\r\nContent-Type: json\r\n\r\n"), .. body]);

        Assert.Equal("{}", await ReadTextAsync(transport));
    }

    [Fact]
    public async Task Read_EmptyBody_ReturnsEmpty()
    {
        var transport = Reader(Encoding.ASCII.GetBytes("Content-Length: 0\r\n\r\n"));

        Assert.Equal("", await ReadTextAsync(transport));
    }

    [Fact]
    public async Task Read_MissingContentLength_ProtocolError()
    {
        var transport = Reader(Encoding.ASCII.GetBytes("Content-Type: json\r\n\r\n{}"));

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => transport.ReadAsync(Ct));

        Assert.Equal(DebugErrorCode.ProtocolError, exception.Code);
        Assert.StartsWith("Protocol error: ", exception.Message);
        Assert.Contains("Content-Length", exception.Message);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("12x")]
    [InlineData("")]
    [InlineData("99999999999999999999")]
    public async Task Read_NonNumericLength_ProtocolError(string value)
    {
        var transport = Reader(Encoding.ASCII.GetBytes($"Content-Length: {value}\r\n\r\n{{}}"));

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => transport.ReadAsync(Ct));

        Assert.Equal(DebugErrorCode.ProtocolError, exception.Code);
    }

    [Fact]
    public async Task Read_Oversize_ProtocolError()
    {
        // 64 MiB is the limit; one byte more is rejected before any body byte is read or buffered.
        var transport = Reader(Encoding.ASCII.GetBytes($"Content-Length: {64 * 1024 * 1024 + 1}\r\n\r\n"));

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => transport.ReadAsync(Ct));

        Assert.Equal(DebugErrorCode.ProtocolError, exception.Code);
        Assert.Contains("64 MiB", exception.Message);
    }

    [Fact]
    public async Task Read_HeaderWithoutTerminator_ProtocolError()
    {
        var transport = Reader(Encoding.ASCII.GetBytes(new string('x', 70_000)));

        var exception = await Assert.ThrowsAsync<DebuggerException>(() => transport.ReadAsync(Ct));

        Assert.Equal(DebugErrorCode.ProtocolError, exception.Code);
    }

    [Fact]
    public async Task Read_EofInsideMessage_IsEndOfStream()
    {
        var bytes = Frame("""{"seq":1}""");

        Assert.Null(await ReadTextAsync(Reader(bytes[..10])));                  // inside the header
        Assert.Null(await ReadTextAsync(Reader(bytes[..(bytes.Length - 2)])));  // inside the body
        Assert.Null(await ReadTextAsync(Reader()));                             // nothing at all
    }

    // ---- writing (G-006) ----

    [Fact]
    public async Task Write_FramingBytesExact_SingleWrite()
    {
        var output = new RecordingStream();
        var transport = new DapTransport(Stream.Null, output);
        var body = Encoding.UTF8.GetBytes("""{"seq":1,"type":"request","command":"threads","x":"ā"}""");

        await transport.SendAsync(body, Ct);

        byte[] expected = [.. Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), .. body];
        Assert.Equal(expected, output.ToArray());
        Assert.Equal(body.Length, Encoding.UTF8.GetCharCount(body) + 1);   // the length is in bytes
        Assert.Single(output.Writes);
        Assert.Equal(1, output.Flushes);
    }

    [Fact]
    public async Task Write_100ConcurrentSends_NotInterleaved()
    {
        // The stream yields inside every write, so two writes per message, or unserialised sends, would interleave.
        var output = new RecordingStream { YieldInWrite = true };
        var transport = new DapTransport(Stream.Null, output);
        var bodies = Enumerable.Range(0, 100)
            .Select(i => $$"""{"seq":{{i}},"pad":"{{new string((char)('a' + i % 26), i * 7)}}"}""")
            .ToList();
        using var start = new ManualResetEventSlim();

        var sends = bodies.Select(text => Task.Run(async () =>
        {
            start.Wait(Ct);
            await transport.SendAsync(Encoding.UTF8.GetBytes(text), Ct);
        }, Ct)).ToArray();
        start.Set();
        await Task.WhenAll(sends);

        var reader = new DapTransport(new MemoryStream(output.ToArray()), Stream.Null);
        var received = new List<string>();
        while (await ReadTextAsync(reader) is { } text)
            received.Add(text);
        Assert.Equal(bodies.Order(StringComparer.Ordinal), received.Order(StringComparer.Ordinal));
        Assert.Equal(100, output.Writes.Count);
        Assert.Equal(1, output.MaxConcurrentWrites);
    }

    // ---- helpers ----

    /// <summary>Returns exactly one predefined chunk per read call (never more), then end of stream.</summary>
    private sealed class ChunkedStream(byte[][] chunks) : Stream
    {
        private readonly Queue<byte[]> _chunks = new(chunks.Where(c => c.Length > 0));
        private ArraySegment<byte> _current;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_current.Count == 0)
            {
                if (_chunks.Count == 0)
                    return 0;
                _current = _chunks.Dequeue();
            }

            var n = Math.Min(count, _current.Count);
            _current.AsSpan(0, n).CopyTo(buffer.AsSpan(offset));
            _current = _current[n..];
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Records every write call as one chunk and counts flushes and overlapping writes.</summary>
    private sealed class RecordingStream : Stream
    {
        private readonly Lock _gate = new();
        private int _active;

        public List<byte[]> Writes { get; } = [];
        public int Flushes { get; private set; }
        public int MaxConcurrentWrites { get; private set; }
        public bool YieldInWrite { get; init; }

        public byte[] ToArray()
        {
            lock (_gate)
                return Writes.SelectMany(w => w).ToArray();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _active++;
                MaxConcurrentWrites = Math.Max(MaxConcurrentWrites, _active);
            }

            if (YieldInWrite)
                await Task.Yield();
            lock (_gate)
            {
                Writes.Add(buffer.ToArray());
                _active--;
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
            lock (_gate)
                Flushes++;
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            Flush();
            return Task.CompletedTask;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
