using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Platform.Windows;
using FreexComp.Ide.TestHelper;
using FreexComp.Ide.Tests.Fixtures;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Platform;

/// <summary>The Windows process launcher with its Job Object (I-104, IDE-P3-073/083/094), against real processes.</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class ProcessTreeTests
{
    private static ProcessStartRequest Request(string mode)
    {
        var (file, arguments) = ProcessHelper.Command(mode);
        return new ProcessStartRequest(file, arguments, Path.GetTempPath());
    }

    /// <summary>
    /// I-216 diagnostics for an exit wait that used up the test's time: when the synchronous start returned, whether the
    /// program had already ended, and the thread pool's state (the exit report is delivered by a pool thread).
    /// </summary>
    private static async Task<int> WaitForExitReportAsync(IRunningProcess process, TimeSpan startReturnedAt)
    {
        try
        {
            return await CurrentTest.Deadline.WaitAsync(process.Exited, "the helper's exit");
        }
        catch (TimeoutException e)
        {
            throw new TimeoutException(e.Message + string.Create(CultureInfo.InvariantCulture,
                $"; Start returned {startReturnedAt.TotalSeconds:0.0} s into the test; program ended: {IsGone(process.ProcessId)}; pool threads {ThreadPool.ThreadCount}, queued work items {ThreadPool.PendingWorkItemCount}"), e);
        }
    }

    private static bool IsGone(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.HasExited;
        }
        catch (ArgumentException)
        {
            return true; // no such process
        }
    }

    [Fact(Timeout = TestDeadline.ProcessTestTimeout)]
    [Trait("Req", "IDE-P3-083")]
    [Trait("Req", "IDE-P3-090")]
    [Trait("Req", "IDE-P3-094")]
    public async Task KillTreeTerminatesGrandchildren()
    {
        var deadline = CurrentTest.Deadline;
        var childLine = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var launcher = new WindowsProcessLauncher(new ManualClock());
        using var process = launcher.Start(Request(HelperProgram.SpawnChild), line =>
        {
            if (line.Text.StartsWith("child ", StringComparison.Ordinal))
                childLine.TrySetResult(line.Text);
        });
        var grandchild = int.Parse((await deadline.WaitAsync(childLine.Task, "the helper's child line"))["child ".Length..], CultureInfo.InvariantCulture);
        Assert.False(IsGone(process.ProcessId));
        using var grandchildProcess = Process.GetProcessById(grandchild);
        Assert.False(grandchildProcess.HasExited);

        // The kill is the job's termination, not a timeout (I-220: no wall-clock budget): the launcher's clock is manual and
        // never advanced, so no timed path of the launcher can act, and both helpers wait forever, so neither ends on its
        // own: the grandchild's exit and the child's TerminateJobObject exit code come from the kill alone.
        Assert.True(await process.KillTreeAsync());
        await deadline.WaitAsync(grandchildProcess.WaitForExitAsync(TestContext.Current.CancellationToken), "the grandchild's exit");
        Assert.True(IsGone(process.ProcessId));
        Assert.Equal(1, await deadline.WaitAsync(process.Exited, "the exit report")); // TerminateJobObject exit code
    }

    [Fact(Timeout = TestDeadline.ProcessTestTimeout)]
    [Trait("Req", "IDE-P3-073")]
    [Trait("Req", "IDE-P3-082")]
    [Trait("Req", "IDE-P3-084")]
    public async Task CapturesStdoutAndStderr()
    {
        var lines = new ConcurrentQueue<ProcessOutputLine>();
        var launcher = new WindowsProcessLauncher(new ManualClock());
        using var process = launcher.Start(Request(HelperProgram.WriteBoth), lines.Enqueue);

        Assert.Equal(3, await CurrentTest.Deadline.WaitAsync(process.Exited, "the helper's exit"));

        // Each pipe keeps its own order; the interleaving of the two is not asserted (IDE-P3-074).
        Assert.Equal(["out 1", "grün", "cr", "Name: "], lines.Where(l => !l.IsError).Select(l => l.Text));
        Assert.Equal(["err 1", "stdin 0"], lines.Where(l => l.IsError).Select(l => l.Text));
    }

    [Fact(Timeout = TestDeadline.ProcessTestTimeout)]
    [Trait("Req", "IDE-P3-082")]
    [Trait("Req", "IDE-P3-083")]
    public async Task OnlyPipeHandlesAreInherited()
    {
        // An inheritable pipe of this process that holds a marker: a child that inherits it can peek the marker.
        using var leak = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out, HandleInheritability.Inheritable);
        var marker = System.Text.Encoding.ASCII.GetBytes(HelperProgram.PipeMarker);
        leak.Write(marker);
        leak.Flush();
        var readEnd = leak.ClientSafePipeHandle.DangerousGetHandle().ToInt64();

        var lines = new ConcurrentQueue<ProcessOutputLine>();
        var launcher = new WindowsProcessLauncher(new ManualClock());
        using (var process = launcher.Start(Request(HelperProgram.PeekHandle + " " + readEnd.ToString(CultureInfo.InvariantCulture)), lines.Enqueue))
            Assert.Equal(0, await WaitForExitReportAsync(process, CurrentTest.Deadline.Elapsed));

        // The child still got its own pipe ends: its answer arrived on the captured stdout.
        Assert.Equal([HelperProgram.NotInherited], lines.Where(l => !l.IsError).Select(l => l.Text));
        leak.DisposeLocalCopyOfClientHandle();
    }

    /// <summary>
    /// I-216: while captured programs start, other code of the IDE process starts programs with <see cref="Process"/> and
    /// redirected streams (the build engine's tools, the E2E sample runs); such a child inherits every handle of the
    /// process that is inheritable at that moment. None of them may get a pipe end of a captured program: a holder keeps the
    /// pipe open, so the program's final partial line (emitted only at end-of-file under a manual clock) never arrives.
    /// Each round starts two captured programs and three long-lived redirected programs at the same instant (a barrier).
    /// <para>
    /// I-258: the five starting threads live for all rounds (one barrier phase per round) and a round ends when its
    /// starts have returned — the window in question is the start. The captured programs' ends are awaited together
    /// after the last round: a program's run time (seconds on a machine whose cores are all busy) is paid once, not
    /// once per round, and no thread is created between rounds.
    /// </para>
    /// </summary>
    [Fact(Timeout = TestDeadline.ProcessTestTimeout)]
    [Trait("Req", "IDE-P3-082")]
    [Trait("Req", "IDE-P3-084")]
    public async Task ConcurrentLaunchesDoNotInheritEachOthersPipes()
    {
        const int Rounds = 8, Launches = 2, Holders = 3;
        Assert.True(WindowsProcessLauncher.SharesRuntimeLock, "the launcher does not share the runtime's process-creation lock");
        var deadline = CurrentTest.Deadline;
        var holders = new ConcurrentBag<Process>();
        var captured = new ConcurrentBag<Task>();
        var failures = new ConcurrentQueue<string>();
        var launcher = new WindowsProcessLauncher(new ManualClock());
        var shell = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        // The end of one captured program: its exit report, then its complete standard output.
        async Task EndAsync(IRunningProcess process, ConcurrentQueue<ProcessOutputLine> lines, string name)
        {
            using (process)
            {
                await deadline.WaitAsync(process.Exited, "the exit of " + name);
                var stdout = string.Join("|", lines.Where(x => !x.IsError).Select(x => x.Text));
                if (stdout != "out 1|grün|cr|Name: ")
                    failures.Enqueue(name + ": stdout " + stdout);
            }
        }

        using var barrier = new Barrier(Launches + Holders);
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token); // a start that failed releases the others

        // One thread per starter, for all rounds; a failure of one stops the others at the barrier.
        Task Starter(Action<int> startOne) => Task.Factory.StartNew(() =>
        {
            try
            {
                for (var round = 0; round < Rounds; round++)
                {
                    barrier.SignalAndWait(abort.Token);
                    startOne(round);
                }
            }
            catch (OperationCanceledException)
            {
                // another starter failed (its failure is recorded) or the test's time is up (the wait below reports it)
            }
            catch (Exception e)
            {
                failures.Enqueue("a start failed: " + e);
                abort.Cancel();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        try
        {
            var starters = new List<Task>();
            for (var h = 0; h < Holders; h++)
            {
                starters.Add(Starter(_ =>
                {
                    // Started the way ProcessRunner starts the toolchain: all three streams redirected. It reads its
                    // never-closed stdin, so it lives until the test kills it.
                    var start = new ProcessStartInfo(shell, "/d /q /k")
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    };
                    var holder = Process.Start(start)!;
                    ChildProcessOutput.Read(holder); // I-262: drained, so that a full pipe never blocks the holder
                    holders.Add(holder);
                }));
            }

            for (var l = 0; l < Launches; l++)
            {
                var launch = l;
                starters.Add(Starter(round =>
                {
                    var lines = new ConcurrentQueue<ProcessOutputLine>();
                    var process = launcher.Start(Request(HelperProgram.WriteBoth), lines.Enqueue);
                    captured.Add(EndAsync(process, lines, string.Create(CultureInfo.InvariantCulture, $"round {round} launch {launch}")));
                }));
            }

            await deadline.WaitAsync(Task.WhenAll(starters), "the starts of all rounds");
            await deadline.WaitAsync(Task.WhenAll(captured), "the ends of the captured programs");

            Assert.True(failures.IsEmpty, $"{failures.Count} of {Rounds * Launches} captured programs lost their end-of-file to a "
                + "concurrently started program: " + string.Join("; ", failures));
            Assert.Equal((Rounds * Launches, Rounds * Holders), (captured.Count, holders.Count));
        }
        finally
        {
            await abort.CancelAsync(); // no starter stays at the barrier
            foreach (var holder in holders)
            {
                try
                {
                    holder.Kill(entireProcessTree: true);
                    holder.WaitForExit();
                }
                catch (InvalidOperationException)
                {
                    // already gone
                }

                holder.Dispose();
            }
        }
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-081")]
    public void MissingExecutableThrows()
    {
        var launcher = new WindowsProcessLauncher(new ManualClock());
        Assert.Throws<System.ComponentModel.Win32Exception>(() =>
            launcher.Start(new ProcessStartRequest(Path.Combine(Path.GetTempPath(), "no-such-program-" + Guid.NewGuid().ToString("N") + ".exe"), "", Path.GetTempPath()), _ => { }));
    }
}
