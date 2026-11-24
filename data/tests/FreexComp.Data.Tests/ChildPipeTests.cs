using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace FreexComp.Data.Tests;

/// <summary>
/// Child-process pipes stay off the thread pool (task A-080): the helper reads on a thread of its own, the runner reads
/// both pipes at once through it, stops a silent child without waiting for its pipes, and no test code of the area
/// reads a child's pipe asynchronously.
/// </summary>
public sealed partial class ChildPipeTests
{
    private const int Lines = 4000;

    private static readonly string OutLine = new('o', 100);

    private static readonly string ErrLine = new('e', 100);

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(120);          // a safety net against a hang, not a measurement

    private static string CommandInterpreter => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    // An asynchronous member called on a redirected pipe, or the event-based line reader of the process class.
    [GeneratedRegex(@"Standard(?:Output|Error)\s*\.\s*(?:BaseStream\s*\.\s*)?\w+(?:Async)\b|Begin(?:Output|Error)(?:ReadLine)|(?:Output|Error)(?:DataReceived)")]
    private static partial Regex AsynchronousPipeRead();

    [Fact]
    public async Task ReadToEnd_ReadsOnANamedThreadOutsideThePool()
    {
        var reader = new ProbeReader("first\n", "second\n");

        // started from a pool thread, as the runner does after an await
        var text = await Task.Run(() => ChildPipe.ReadToEnd(reader, "probe pipe"), TestContext.Current.CancellationToken);

        Assert.Equal("first\nsecond\n", text);
        Assert.Equal(3, reader.Reads.Count);                                     // two chunks and the end
        Assert.All(reader.Reads, read => Assert.False(read.OnPoolThread, "a pipe read ran on a thread-pool thread"));
        Assert.All(reader.Reads, read => Assert.True(read.OnBackgroundThread, "a pipe read ran on a foreground thread"));
        Assert.All(reader.Reads, read => Assert.Equal("probe pipe", read.ThreadName));
    }

    [Fact]
    public async Task ReadToEnd_BrokenPipe_EndsWithTheTextReadSoFar()
    {
        var text = await ChildPipe.ReadToEnd(new ProbeReader("kept") { FailAtEnd = new IOException("the pipe broke") }, "probe pipe");

        Assert.Equal("kept", text);
    }

    [Fact]
    public async Task ReadToEnd_OtherFailure_FaultsTheTask()
    {
        var read = ChildPipe.ReadToEnd(new ProbeReader("lost") { FailAtEnd = new InvalidOperationException("not a pipe failure") }, "probe pipe");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => read);
        Assert.Equal("not a pipe failure", failure.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunProcess_LargeOutputOnBothStreamsAtOnce_IsReadCompletely()
    {
        // every round writes one line to each stream: far more than a pipe holds, so reading one pipe after the other would hang
        var command = $"for /L %i in (1,1,{Lines}) do @(echo {OutLine}&echo {ErrLine} 1>&2)";

        var result = await GoldenProgramRunner.RunProcessAsync(CommandInterpreter, ["/d", "/c", command], Path.GetTempPath(), Limit, TestContext.Current.CancellationToken);

        Assert.Null(result.Stopped);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Repeated(OutLine), GoldenProgramRunner.Normalize(result.StdOut));
        Assert.Equal(Repeated(ErrLine + " "), GoldenProgramRunner.Normalize(result.StdErr));   // the blank before the redirection is echoed
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunProcess_Cancelled_StopsASilentChildWithoutWaitingForItsPipes()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // the child waits for a signal nobody sends and writes nothing: both pipe reads are blocked when the run is stopped
        var result = await GoldenProgramRunner.RunProcessAsync(
            Path.Combine(Environment.SystemDirectory, "waitfor.exe"), ["/t", "600", "FreexComp" + Guid.NewGuid().ToString("N")[..16]], Path.GetTempPath(), Limit, cancelled.Token);

        Assert.Equal("test cancelled: waitfor.exe stopped", result.Stopped);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    [Trait("Category", "Spec")]
    public void NoTestCodeOfTheAreaReadsAChildPipeAsynchronously()
    {
        var area = Path.Combine(DataCorpus.RepositoryRoot, "data");
        var separator = Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(area, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase) && !file.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(files);

        var offenders = files
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(x => AsynchronousPipeRead().IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.file)}({x.number}): {x.line.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0, "a child-process pipe is read asynchronously (use ChildPipe.ReadToEnd):\n" + string.Join("\n", offenders));
    }

    private static string Repeated(string line) => new StringBuilder().Insert(0, line + "\n", Lines).ToString();

    private readonly record struct Read(bool OnPoolThread, bool OnBackgroundThread, string? ThreadName);

    /// <summary>Hands out its chunks one read at a time, then the end (or <see cref="FailAtEnd"/>), and records the thread of every read.</summary>
    private sealed class ProbeReader(params string[] chunks) : TextReader
    {
        private int _next;

        public ConcurrentQueue<Read> Reads { get; } = new();

        public Exception? FailAtEnd { get; init; }

        public override int Read(char[] buffer, int index, int count)
        {
            var thread = Thread.CurrentThread;
            Reads.Enqueue(new(thread.IsThreadPoolThread, thread.IsBackground, thread.Name));
            if (_next == chunks.Length)
            {
                return FailAtEnd is null ? 0 : throw FailAtEnd;
            }

            var chunk = chunks[_next++];
            chunk.CopyTo(0, buffer, index, chunk.Length);
            return chunk.Length;
        }
    }
}
