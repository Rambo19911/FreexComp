using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// R-233: the redirected output of a child process is read on threads of their own (<see cref="ChildPipe"/>), never
/// by an asynchronous read that parks a thread-pool thread until the child writes.
/// </summary>
[Trait("Phase", "2")]
[Trait("Req", "RTL-901")]
public sealed class ChildPipeTests
{
    private const int Lines = 1500;

    private static readonly string OutLine = new('o', 100);

    private static readonly string ErrLine = new('e', 100);

    /// <summary>The command processor writes 150 KB to each of its two streams, line by line in turn: far more than a pipe holds.</summary>
    private static readonly string BothStreams = $"/d /c \"for /L %i in (1,1,{Lines}) do @((echo {OutLine})&(1>&2 echo {ErrLine}))\"";

    private static string CommandProcessor => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary>Records which thread reads it.</summary>
    private sealed class ObservedStream(Stream inner) : Stream
    {
        public bool ReadOnPoolThread { get; private set; }

        public string? ReaderName { get; private set; }

        public int Reads { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            Reads++;
            ReadOnPoolThread |= Thread.CurrentThread.IsThreadPoolThread;
            ReaderName = Thread.CurrentThread.Name;
            return inner.Read(buffer);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static void AssertAllLines(string text, string line)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(Lines, lines.Length);
        Assert.All(lines, l => Assert.Equal(line, l));
    }

    [Fact]
    public async Task ReadToEnd_ReadsBothPipesOfAChildOnDedicatedThreads_WithoutDeadlock()
    {
        var info = new ProcessStartInfo(CommandProcessor, BothStreams)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var process = Process.Start(info)!;
        var stdout = new ObservedStream(process.StandardOutput.BaseStream);
        var stderr = new ObservedStream(process.StandardError.BaseStream);

        var readOut = ChildPipe.ReadToEnd(stdout, "test stdout");
        var readErr = ChildPipe.ReadToEnd(stderr, "test stderr");
        var output = await readOut.WaitAsync(TestContext.Current.CancellationToken);
        var errors = await readErr.WaitAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, process.ExitCode);
        AssertAllLines(System.Text.Encoding.ASCII.GetString(output), OutLine);
        AssertAllLines(System.Text.Encoding.ASCII.GetString(errors), ErrLine);
        Assert.True(stdout.Reads > 1 && stderr.Reads > 1);
        Assert.False(stdout.ReadOnPoolThread, "stdout was read on a thread-pool thread");
        Assert.False(stderr.ReadOnPoolThread, "stderr was read on a thread-pool thread");
        Assert.Equal("test stdout", stdout.ReaderName);
        Assert.Equal("test stderr", stderr.ReaderName);
    }

    [Fact]
    public async Task ReadToEnd_ABrokenSourceEndsTheCapture_AnyOtherFailureFaultsTheTask()
    {
        Assert.Equal([1, 2, 3], await ChildPipe.ReadToEnd(new MemoryStream([1, 2, 3]), "test memory"));
        Assert.Empty(await ChildPipe.ReadToEnd(new Failing(new IOException("broken pipe")), "test broken"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ChildPipe.ReadToEnd(new Failing(new InvalidOperationException("bug")), "test failing"));
    }

    private sealed class Failing(Exception failure) : MemoryStream
    {
        public override int Read(Span<byte> buffer) => throw failure;

        public override int Read(byte[] buffer, int offset, int count) => throw failure;
    }

    /// <summary>The golden runner's process run: both streams larger than a pipe, at once.</summary>
    [Fact]
    public async Task GoldenRunner_CapturesLargeOutputOnBothStreams()
    {
        var run = await GoldenRunner.RunProcessAsync(CommandProcessor, BothStreams, Environment.SystemDirectory, [], RunLimits.Default, TestContext.Current.CancellationToken);

        Assert.Null(run.Stopped);
        Assert.Equal(0, run.ExitCode);
        AssertAllLines(run.StdOut, OutLine);
        AssertAllLines(run.StdErr, ErrLine);
    }

    /// <summary>The import-table tests' process run: stdout then stderr, both larger than a pipe.</summary>
    [Fact]
    public async Task ImportTableRun_CapturesLargeOutputOnBothStreams()
    {
        var (exitCode, output) = await ImportTableTests.RunAsync(CommandProcessor, BothStreams, Environment.SystemDirectory);

        Assert.Equal(0, exitCode);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(Enumerable.Repeat(OutLine, Lines).Concat(Enumerable.Repeat(ErrLine, Lines)), lines);
    }

    /// <summary>No harness source reads a child's pipe asynchronously or through the process's own line events.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void HarnessSources_ReadChildPipesOnlyThroughChildPipe()
    {
        var asyncRead = new Regex(@"Standard(Output|Error)\b[^;\n]*\b(CopyToAsync|ReadToEndAsync|ReadLineAsync|ReadAsync)\b|\bBegin(Output|Error)ReadLine\b", RegexOptions.CultureInvariant);
        var harness = Path.Combine(RtlSetup.RepositoryRoot, "rtl", "tests", "Harness");

        var offenders = Directory.EnumerateFiles(harness, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(x => asyncRead.IsMatch(x.line) && !x.line.Contains("new Regex(", StringComparison.Ordinal))
            .Select(x => $"{Path.GetFileName(x.file)}({x.index + 1}): {x.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "asynchronous reads of a child's pipe:\n" + string.Join("\n", offenders));
    }
}
