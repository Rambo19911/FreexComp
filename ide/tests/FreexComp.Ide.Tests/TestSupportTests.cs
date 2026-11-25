using System.Diagnostics;
using System.Globalization;
using Avalonia.Headless.XUnit;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.TestHelper;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests;

[Trait("Phase", "1")]
public sealed class TestSupportTests
{
    [Fact(Timeout = 30000)]
    public async Task FakesUsableFromIdeTests()
    {
        IFileSystem fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\p");
        fs.WriteAllBytes(@"C:\p\a.pas", [1]);

        var clock = new ManualClock();
        var fired = false;
        clock.Schedule(TimeSpan.FromSeconds(1), () => fired = true);
        clock.Advance(TimeSpan.FromSeconds(1));

        IClipboardService clipboard = new FakeClipboard();
        await clipboard.SetTextAsync("x");

        IShellService shell = new FakeShellService(fs);
        shell.MoveToRecycleBin(@"C:\p\a.pas");

        IUiDispatcher dispatcher = new ImmediateDispatcher();
        var posted = false;
        dispatcher.Post(() => posted = true);

        IFileWatcherFactory watchers = new ManualFileWatcherFactory();
        using var watcher = watchers.Watch(@"C:\p");

        Assert.True(fired);
        Assert.Equal("x", await clipboard.GetTextAsync());
        Assert.False(fs.FileExists(@"C:\p\a.pas"));
        Assert.True(posted);
        Assert.Equal(@"C:\p", watcher.Directory);
    }

    /// <summary>
    /// I-258: a session over the scripted adapter has no wall-clock limit shorter than the test's own safety net. The
    /// product's limits (10 s per request, 30 s for the launch) are for a person at a debugger; on a machine that runs
    /// every test project at once the adapter process alone can take longer to start, and the launch would fail for a
    /// reason no test is about. What the IDE asked for is recorded unchanged.
    /// </summary>
    [Fact(Timeout = 30000)]
    public void ScriptedSessionsAreBoundedByTheTestsSafetyNetOnly()
    {
        using var sessions = new FakeAdapterSessions();
        var asked = new DebugSessionOptions { AdapterPath = @"C:	cdapter.exe" };
        var net = TimeSpan.FromMilliseconds(TestDeadline.DebugTestTimeout);

        sessions.UseScenario("""{ "steps": [] }""");
        sessions.Create(asked);
        Assert.Same(asked, Assert.Single(sessions.Requested));
        Assert.Equal((TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)), (asked.RequestTimeout, asked.LaunchTimeout));
        Assert.Equal((FakeAdapterSessions.AdapterExe, net, net), (sessions.Created[0].AdapterPath, sessions.Created[0].RequestTimeout, sessions.Created[0].LaunchTimeout));

        // Without the scripted adapter the options stay the product's own.
        sessions.UseNoAdapter();
        sessions.Create(asked);
        Assert.Equal((asked.AdapterPath, asked.RequestTimeout, asked.LaunchTimeout),
            (sessions.Created[1].AdapterPath, sessions.Created[1].RequestTimeout, sessions.Created[1].LaunchTimeout));
    }

    /// <summary>
    /// I-263: the thread pool of this test process starts with <see cref="Infrastructure.TestThreadPool.WorkerMinimum"/>
    /// workers on demand. The headless session runs its loop on a pool thread and every UI test blocks one more while
    /// its body runs there; with the default minimum (one per processor) the first tests of the run block every worker,
    /// and on a machine whose processors are all busy the pool adds one only after about a second per existing worker.
    /// Whatever needs a worker meanwhile — the exit notification of a child process, a test's own timeout — waits.
    /// </summary>
    [Fact(Timeout = 30000)]
    public void ThreadPoolKeepsWorkersForBlockedUiTests()
    {
        ThreadPool.GetMinThreads(out var workers, out _);
        Assert.True(Infrastructure.TestThreadPool.WorkerMinimum >= 4 * Environment.ProcessorCount);
        Assert.True(workers >= Infrastructure.TestThreadPool.WorkerMinimum, $"The pool's worker minimum is {workers}.");
    }

    /// <summary>
    /// I-262: no source file under <c>ide/</c> reads a child process's redirected stdout or stderr asynchronously. Such a
    /// pipe is a synchronous handle, so the "asynchronous" read parks a thread-pool thread until the child writes, and
    /// test projects running in parallel starve the pool. <see cref="ChildProcessOutput"/> reads on dedicated threads.
    /// </summary>
    [Fact(Timeout = 30000)]
    public void NoAsyncReadsOfChildProcessPipes()
    {
        var offenders = AsyncChildPipeReads(Path.Combine(SpecTables.RepoRootPath(), "ide"));
        Assert.True(offenders.Count == 0,
            "Asynchronous reads of a child process pipe (I-262; use ChildProcessOutput.Read):\n" + string.Join("\n", offenders));
    }

    [Fact(Timeout = 30000)]
    public void ChildPipeScanFindsAsyncReads()
    {
        var root = Directory.CreateTempSubdirectory("fxc-i262-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "obj"));
            File.WriteAllText(Path.Combine(root, "A.cs"), "var a = process.StandardOutput.ReadToEndAsync(token);\nvar b = p.StandardError . ReadLineAsync();\nvar ok = p.StandardOutput.ReadToEnd();");
            File.WriteAllText(Path.Combine(root, "B.cs"), "await p.StandardOutput.BaseStream.CopyToAsync(sink);\nawait p.StandardError.BaseStream.ReadAsync(buffer);\np.StandardInput.WriteLineAsync(x);");
            File.WriteAllText(Path.Combine(root, "C.cs"), "p.BeginOutputReadLine();\np.BeginErrorReadLine();\np.OutputDataReceived += (_, e) => { };\np.ErrorDataReceived += Handler;\nvar r = ChildProcessOutput.Read(p);\nvar t = await r.StandardOutput.WaitAsync(token);");
            File.WriteAllText(Path.Combine(root, "obj", "D.cs"), "p.BeginOutputReadLine();");
            File.WriteAllText(Path.Combine(root, "TestSupportTests.cs"), "p.BeginOutputReadLine();");
            Assert.Equal(
                ["A.cs(1)", "A.cs(2)", "B.cs(1)", "B.cs(2)", "C.cs(1)", "C.cs(2)", "C.cs(3)", "C.cs(4)"],
                AsyncChildPipeReads(root).Select(o => o[..o.IndexOf(':', StringComparison.Ordinal)]).Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// <c>file(line): text</c> of every line that reads <c>StandardOutput</c> / <c>StandardError</c> (or their
    /// <c>BaseStream</c>) with a <c>Read…Async</c> or <c>CopyToAsync</c> method, starts the event-based line reader or
    /// subscribes to its events. Waiting for a text of <see cref="ChildProcessOutput"/> (<c>WaitAsync</c>) is not a read.
    /// <c>bin</c>, <c>obj</c> and this file (it holds the samples) are not scanned.
    /// </summary>
    private static List<string> AsyncChildPipeReads(string root)
    {
        var pattern = new System.Text.RegularExpressions.Regex(
            @"\bStandard(Output|Error)\s*\.\s*(BaseStream\s*\.\s*)?(Read\w*Async|CopyToAsync)\b|\bBegin(Output|Error)ReadLine\b|\b(Output|Error)DataReceived\b");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj") || Path.GetFileName(file) == "TestSupportTests.cs")
                continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (pattern.IsMatch(lines[i]))
                    offenders.Add($"{relative}({i + 1}): {lines[i].Trim()}");
            }
        }

        return offenders;
    }

    /// <summary>
    /// I-262: <see cref="ChildProcessOutput"/> reads each redirected stream of a child on its own named background
    /// thread, never on the thread pool, and both at once: a megabyte on stdout and on stderr at the same time (far more
    /// than a pipe holds) arrives whole, where reading one stream after the other would leave the child blocked.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task ChildProcessOutputReadOnDedicatedThreads()
    {
        const int Chars = 1 << 20;
        var token = TestContext.Current.CancellationToken;
        var flood = new ProcessStartInfo(HelperProgram.ExecutablePath, string.Create(CultureInfo.InvariantCulture, $"{HelperProgram.Flood} {Chars}"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using (var process = Process.Start(flood)!)
        {
            try
            {
                var output = ChildProcessOutput.Read(process);

                Assert.Equal(
                    [$"child-process {process.Id} stdout", $"child-process {process.Id} stderr"],
                    output.ReaderThreads.Select(t => t.Name));
                Assert.All(output.ReaderThreads, t => Assert.True(t is { IsThreadPoolThread: false, IsBackground: true }, t.Name));

                var stdout = await output.StandardOutput.WaitAsync(token);
                var stderr = await output.StandardError.WaitAsync(token);
                await process.WaitForExitAsync(token);
                Assert.Equal(0, process.ExitCode);
                Assert.Equal((Chars, Chars), (stdout.Length, stderr.Length));
                Assert.True(stdout.All(c => c == 'o') && stderr.All(c => c == 'e'));
                Assert.True(output.Completed.IsCompletedSuccessfully);
                foreach (var thread in output.ReaderThreads)
                    thread.Join(); // each ends with its stream
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
        }

        // A stream that is not redirected has no thread and reads as the empty text.
        var greeting = new ProcessStartInfo(HelperProgram.ExecutablePath, HelperProgram.ExitWith + " 0")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
        };
        using (var process = Process.Start(greeting)!)
        {
            try
            {
                var output = ChildProcessOutput.Read(process);
                Assert.Equal($"child-process {process.Id} stdout", Assert.Single(output.ReaderThreads).Name);
                Assert.True(output.StandardError.IsCompletedSuccessfully);
                Assert.Equal("", await output.StandardError);
                Assert.Equal(HelperProgram.ChildGreeting, (await output.StandardOutput.WaitAsync(token)).TrimEnd('\r', '\n'));
                await process.WaitForExitAsync(token);
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
        }
    }

    /// <summary>
    /// I-264: the headless session builds a new application for every UI test (per-test isolation, its default), so the
    /// application set-up runs before each of them; the warm-up shell is built by the first one only. Two UI tests:
    /// whichever runs second has seen at least two set-ups.
    /// </summary>
    [AvaloniaTheory(Timeout = 30000)]
    [InlineData(1)]
    [InlineData(2)]
    public void WarmUpRunsOncePerProcess(int uiTest)
    {
        Assert.InRange(uiTest, 1, 2);
        Assert.NotNull(Avalonia.Application.Current);
        Assert.Equal(1, TestAppBuilder.WarmUpRuns);
    }
}
