using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>
/// C-319 (CMP-602): the redirected stdout/stderr of a child process are synchronous handles, so every pipe is read on a
/// dedicated background thread — never by an asynchronous read, which parks a thread-pool thread until the child writes.
/// </summary>
[Trait("Phase", "8")]
[Trait("Req", "CMP-602")]
public sealed partial class ProcessRunnerTests
{
    private const int Children = 8;
    private const int Lines = 3000;

    /// <summary>
    /// <see cref="Children"/> processes at once, each writing <see cref="Lines"/> lines to stdout and to stderr (far more than a
    /// pipe buffers, so a stream nobody reads blocks its writer): every line arrives in order on a named thread that is no
    /// pool thread, one thread per pipe, and every process ends with its exit code. The pool keeps its default minimum.
    /// </summary>
    [Fact]
    [Trait("Kind", "Native")]
    [Trait("Category", "Integration")]
    public async Task PipesAreReadOnDedicatedThreads()
    {
        var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var script = $"for /L %i in (1,1,{Lines}) do @(echo out %i& echo err %i 1>&2)";
        // C-325: keyed by the Thread object, not its managed id — an id is reused once a pipe thread has ended, so two pipes of
        // different children can report the same id during one run; the objects stay distinct (and alive in this map)
        var threads = new ConcurrentDictionary<(int Child, bool Error, Thread Thread), (bool IsPool, string? Name)>();
        var output = Enumerable.Range(0, Children).Select(_ => (Out: new List<string>(), Err: new List<string>())).ToArray();

        void Record(int child, bool error, string line)
        {
            var thread = Thread.CurrentThread;
            threads[(child, error, thread)] = (thread.IsThreadPoolThread, thread.Name);
            (error ? output[child].Err : output[child].Out).Add(line.TrimEnd());     // one thread per list
        }

        var runs = Enumerable.Range(0, Children).Select(child => new ProcessRunner().RunAsync(
            cmd, ["/d", "/c", script], Path.GetTempPath(), line => Record(child, false, line), line => Record(child, true, line),
            TestContext.Current.CancellationToken)).ToArray();
        var exitCodes = await Task.WhenAll(runs);

        Assert.All(exitCodes, code => Assert.Equal(0, code));
        var expectedOut = Enumerable.Range(1, Lines).Select(i => "out " + i).ToList();
        var expectedErr = Enumerable.Range(1, Lines).Select(i => "err " + i).ToList();
        Assert.All(output, o => Assert.Equal(expectedOut, o.Out));
        Assert.All(output, o => Assert.Equal(expectedErr, o.Err));

        Assert.Equal(2 * Children, threads.Count);                           // one thread per pipe
        Assert.Equal(2 * Children, threads.Keys.Select(k => k.Thread).Distinct(ReferenceEqualityComparer.Instance).Count());    // no thread reads two pipes
        Assert.All(threads.Values, t => Assert.False(t.IsPool, "a pipe was read on a thread-pool thread"));
        Assert.All(threads.Values, t => Assert.StartsWith("FreexComp pipe ", t.Name, StringComparison.Ordinal));
    }

    /// <summary>A cancelled run kills the child; its pipe threads end with the pipes and deliver nothing afterwards.</summary>
    [Fact]
    [Trait("Kind", "Native")]
    [Trait("Category", "Integration")]
    public async Task Cancellation_KillsTheChild_AndEndsThePipeThreads()
    {
        var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new ProcessRunner();
        var pid = 0;
        runner.ProcessStarted = id => pid = id;

        // prints one line, then loops for ever (a step of 0)
        var run = runner.RunAsync(cmd, ["/d", "/c", "echo ready& for /L %i in (0,0,1) do @rem"], Path.GetTempPath(), _ => first.TrySetResult(), _ => { }, cts.Token);
        await first.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.NotEqual(0, pid);
        Assert.True(HasExited(pid), "the cancelled child is still running");
    }

    private static bool HasExited(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;                                                     // no such process any more
        }
    }

    /// <summary>
    /// No source under <c>compiler/</c> reads a child's stdout or stderr asynchronously or through the event-based line reader:
    /// both park pool threads on the synchronous pipe handle. The product reads through <c>PipeThread</c>, tests through
    /// <c>PipePump</c>.
    /// </summary>
    [Fact]
    [Trait("Kind", "Unit")]
    public void NoSourceReadsChildPipesOnThePool()
    {
        var root = Path.Combine(RepoPaths.Root, "compiler");
        var separator = Path.DirectorySeparatorChar;
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
                || relative.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (PoolPipeReadRegex().IsMatch(lines[i]))
                {
                    offenders.Add($"{relative}({i + 1}): {lines[i].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "child pipes read on the thread pool:\n" + string.Join("\n", offenders));
    }

    [GeneratedRegex(@"Begin(Output|Error)ReadLine|\b(Output|Error)DataReceived\b|Standard(Output|Error)\s*(\.\s*BaseStream)?\s*\.\s*(ReadToEndAsync|ReadLineAsync|ReadBlockAsync|ReadAsync|CopyToAsync)\b")]
    private static partial Regex PoolPipeReadRegex();
}
