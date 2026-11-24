using System.Collections.Concurrent;
using System.Text;
using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// G-083: the blocking reads of the adapter's pipes run on threads of their own. On thread-pool threads every session
/// held two of them for its whole life, and a run of many sessions on a loaded machine stalled: all pool threads sat
/// in pipe reads whose data could only come after work that was queued behind them.
/// </summary>
public sealed class StreamPumpTests
{
    private static readonly TimeSpan Safety = FakeAdapterRunner.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A source that can only be read synchronously (like the pipe of a child process): every read blocks until the
    /// test supplies a chunk, and records the thread it ran on. An empty chunk is the end; a null chunk breaks the pipe.
    /// </summary>
    private sealed class BlockingSource : Stream
    {
        private readonly BlockingCollection<byte[]?> _chunks = [];

        public ConcurrentQueue<(bool OnPool, string? Name, bool Background)> Reads { get; } = new();

        public void Supply(byte[]? chunk) => _chunks.Add(chunk);

        public override int Read(Span<byte> buffer)
        {
            var thread = Thread.CurrentThread;
            Reads.Enqueue((thread.IsThreadPoolThread, thread.Name, thread.IsBackground));
            var chunk = _chunks.Take() ?? throw new IOException("The pipe has been ended.");
            Assert.True(chunk.Length <= buffer.Length, "The test supplies chunks that fit one read.");
            chunk.CopyTo(buffer);
            return chunk.Length;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("An asynchronous read of a synchronous pipe blocks a thread-pool thread.");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An asynchronous read of a synchronous pipe blocks a thread-pool thread.");

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

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, Ct).AsTask().WaitAsync(Safety, Ct);
        return buffer;
    }

    [Fact]
    [Trait("Req", "DBG-005")]
    public async Task Pump_ReadsTheSourceOnItsOwnThread_AndDeliversEveryByteInOrder()
    {
        var source = new BlockingSource();
        var pumped = StreamPump.Start(source, "test pump");

        source.Supply("Content-Le"u8.ToArray());
        Assert.Equal("Content-Le", Encoding.UTF8.GetString(await ReadExactlyAsync(pumped, 10)));
        source.Supply("ngth: 2\r\n\r\n"u8.ToArray());
        source.Supply("{}"u8.ToArray());
        Assert.Equal("ngth: 2\r\n\r\n{}", Encoding.UTF8.GetString(await ReadExactlyAsync(pumped, 13)));

        // A read that is waiting for data occupies no thread at all; it completes when the data comes.
        var pending = pumped.ReadAsync(new byte[16], Ct).AsTask();
        Assert.False(pending.IsCompleted);
        source.Supply("x"u8.ToArray());
        Assert.Equal(1, await pending.WaitAsync(Safety, Ct));

        source.Supply([]);
        Assert.Equal(0, await pumped.ReadAsync(new byte[16], Ct).AsTask().WaitAsync(Safety, Ct));
        Assert.Equal(0, await pumped.ReadAsync(new byte[16], Ct).AsTask().WaitAsync(Safety, Ct));

        Assert.Equal(5, source.Reads.Count);
        Assert.All(source.Reads, read => Assert.Equal((false, "test pump", true), read));
    }

    [Fact]
    [Trait("Req", "DBG-005")]
    public async Task Pump_BrokenSource_IsTheEndOfTheStream()
    {
        var source = new BlockingSource();
        var pumped = StreamPump.Start(source, "test pump");

        source.Supply("ab"u8.ToArray());
        source.Supply(null);

        Assert.Equal("ab", Encoding.UTF8.GetString(await ReadExactlyAsync(pumped, 2)));
        Assert.Equal(0, await pumped.ReadAsync(new byte[16], Ct).AsTask().WaitAsync(Safety, Ct));
    }

    [Fact]
    [Trait("Req", "DBG-005")]
    public async Task Pump_UnreadData_NeverHoldsTheSourceBack()
    {
        // Nobody reads the pumped stream: the pump still takes everything the source has (a megabyte here), so its
        // thread ends with the source instead of waiting for a reader that may never come.
        var source = new BlockingSource();
        var pumped = StreamPump.Start(source, "test pump");
        var chunk = new byte[4096];
        for (var i = 0; i < 256; i++)
        {
            chunk.AsSpan().Fill((byte)i);
            source.Supply([.. chunk]);
        }

        source.Supply([]);

        var all = await ReadExactlyAsync(pumped, 256 * 4096);
        for (var i = 0; i < 256; i++)
            Assert.Equal((byte)i, all[(i * 4096) + 4095]);
        Assert.Equal(0, await pumped.ReadAsync(new byte[16], Ct).AsTask().WaitAsync(Safety, Ct));
    }

    [Fact]
    [Trait("Req", "DBG-005")]
    public async Task DedicatedThread_RunsTheWorkOffThePool_AndReportsItsOutcome()
    {
        (bool OnPool, string? Name, bool Background) seen = default;
        await DedicatedThread.Run("test work", () =>
        {
            var thread = Thread.CurrentThread;
            seen = (thread.IsThreadPoolThread, thread.Name, thread.IsBackground);
        }).WaitAsync(Safety, Ct);
        Assert.Equal((false, "test work", true), seen);

        var failure = await Assert.ThrowsAsync<InvalidDataException>(
            () => DedicatedThread.Run("test work", () => throw new InvalidDataException("boom")).WaitAsync(Safety, Ct));
        Assert.Equal("boom", failure.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-005")]
    [Trait("Req", "DBG-006")]
    public async Task AdapterProcess_ReadsStderrOnItsOwnThread()
    {
        var threads = new ConcurrentQueue<(bool OnPool, string? Name)>();
        await using var adapter = AdapterProcess.Start(
            FakeAdapterRunner.ExePath, ["--stderr-lines", "3"],
            _ => threads.Enqueue((Thread.CurrentThread.IsThreadPoolThread, Thread.CurrentThread.Name)));
        ProcessLeakFixture.Registry.Track(adapter.ProcessId, nameof(StreamPumpTests));

        await adapter.WaitForExitAsync(Ct).WaitAsync(Safety, Ct);
        await adapter.StderrCompleted.WaitAsync(Safety, Ct);

        Assert.Equal(3, threads.Count);
        Assert.All(threads, thread => Assert.Equal((false, "FreexComp.Debugger.AdapterStderr"), thread));
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-005")]
    public async Task AdapterProcess_OutputIsPumped_NotThePipeItself()
    {
        await using var adapter = AdapterProcess.Start(FakeAdapterRunner.ExePath, ["--stderr-lines", "0"], null);
        ProcessLeakFixture.Registry.Track(adapter.ProcessId, nameof(StreamPumpTests));

        // The pipe of a child process is a synchronous handle; the stream handed to the protocol reader is not it.
        Assert.IsNotType<FileStream>(adapter.StandardOutput, exactMatch: false);
        Assert.IsNotType<System.IO.Pipes.PipeStream>(adapter.StandardOutput, exactMatch: false);

        await adapter.WaitForExitAsync(Ct).WaitAsync(Safety, Ct);
        Assert.Equal(0, await adapter.StandardOutput.ReadAsync(new byte[16], Ct).AsTask().WaitAsync(Safety, Ct));
    }
}
