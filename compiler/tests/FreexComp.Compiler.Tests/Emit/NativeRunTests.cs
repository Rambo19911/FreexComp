using System.Diagnostics;
using FreexComp.Tests.Shared;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-249: the native run limit is load-tolerant — the program's CPU time, not the wall clock, decides a busy hang; a
/// blocked program meets the wall-clock cap; a stopped run reports elapsed time and process state.
/// C-341: that state is the one at the stop — a test that expects a waiting thread first waits until the program waits.
/// </summary>
[Trait("Phase", "2")]
public sealed class NativeRunTests
{
    private static readonly TimeSpan S = TimeSpan.FromSeconds(1);

    [Theory]
    [Trait("Kind", "Unit")]
    [InlineData(5, 20, 20, 120, null, 15)]       // a loaded machine: 20 s wall, 5 s CPU — goes on; CPU cannot reach 20 s before 15 s more
    [InlineData(0, 0, 20, 120, null, 20)]
    [InlineData(19, 110, 20, 120, null, 1)]
    [InlineData(2, 119, 20, 120, null, 1)]       // the wall cap is nearer
    [InlineData(20, 25, 20, 120, "CPU limit 20.0 s reached", 0)]
    [InlineData(3, 120, 20, 120, "wall-clock limit 120.0 s reached", 0)]
    public void Decide_StopsOnCpuTimeOrTheWallCap(int cpu, int wall, int cpuLimit, int wallLimit, string? reason, int next)
    {
        var decision = NativeRun.Decide(cpu * S, wall * S, cpuLimit * S, wallLimit * S);

        Assert.Equal((reason, next * S), decision);
    }

    [Fact]
    [Trait("Kind", "Unit")]
    public void Decide_NeverChecksMoreOftenThanEveryFiftyMilliseconds() =>
        Assert.Equal(TimeSpan.FromMilliseconds(50), NativeRun.Decide(TimeSpan.FromMilliseconds(19_990), S, 20 * S, 120 * S).NextCheck);

    [Fact]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    public async Task BusyHang_IsStoppedByTheCpuLimit_WithTheRunState()
    {
        var (exe, dir) = EmitTests.Build("Busy", "program Busy; var I: Integer; begin I := 0; while I >= 0 do I := (I + 1) and 1023; end.", OptimizationLevel.O0);

        var failure = await Assert.ThrowsAsync<Xunit.Sdk.FailException>(() =>
            NativeRun.RunAsync(exe, dir, _ => { }, _ => { }, S, 60 * S, TestContext.Current.CancellationToken));

        TestContext.Current.TestOutputHelper?.WriteLine(failure.Message);
        Assert.StartsWith("CPU limit 1.0 s reached: Busy.exe stopped after ", failure.Message);
        Assert.Matches(@" s wall, \d+\.\d s CPU, still running, threads \[.+\]$", failure.Message);
        TestDirectories.Delete(dir);
    }

    [Fact]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    public async Task BlockedHang_IsStoppedByTheWallCap_WithTheWaitingThreads()
    {
        var (exe, dir) = EmitTests.Build("Blocked",
            "program Blocked; procedure Sleep(Ms: Cardinal); stdcall; external 'kernel32.dll'; begin Sleep($FFFFFFFF); end.", OptimizationLevel.O0);
        var ct = TestContext.Current.CancellationToken;
        string? report;

        // C-341: the report shows the threads as they are at the stop, so the cap is counted from the moment the program is in
        // its wait — counted from the start, a cap of any fixed length can run out before that on a loaded machine
        using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir })!;
        try
        {
            await MainThreadSleepsAsync(process, ct);
            using var limit = new RunLimit(Path.GetFileName(exe), 20 * S, S, ct);
            limit.Attach(process.Id);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout.InfiniteTimeSpan, limit.Token));
            report = limit.Report;
        }
        finally
        {
            await NativeRun.KillAsync(process, ct);
        }

        Assert.NotNull(report);
        TestContext.Current.TestOutputHelper?.WriteLine(report);
        Assert.StartsWith("wall-clock limit 1.0 s reached: Blocked.exe stopped after ", report);
        Assert.Contains("still running, threads [wait:ExecutionDelay", report);  // the program's own wait, not one of its start
        TestDirectories.Delete(dir);
    }

    /// <summary>Load tolerance: a program whose wall time exceeds the CPU limit (here by waiting) is not stopped.</summary>
    [Fact]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    public async Task WallTimeBeyondTheCpuLimit_DoesNotStopTheProgram()
    {
        var (exe, dir) = EmitTests.Build("Slow",
            "program Slow; procedure Sleep(Ms: Cardinal); stdcall; external 'kernel32.dll'; begin Sleep(1500); Writeln('done'); end.", OptimizationLevel.O0);
        var stdout = new List<string>();

        var exitCode = await NativeRun.RunAsync(exe, dir, stdout.Add, _ => { }, S, 60 * S, TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal(["done"], stdout);
        TestDirectories.Delete(dir);
    }

    /// <summary>
    /// C-341: the state in the report is read at the stop, whatever the program does then. A program that is still on its way
    /// to its wait when the cap runs out — on a loaded machine any program; here one that never gets there — is reported with
    /// the states its threads have: a waiting thread with what it waits for, any other with its state.
    /// </summary>
    [Fact]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    public async Task WallCap_ReportsThreadStates_AlsoWhenTheProgramHasNotReachedItsWait()
    {
        var (exe, dir) = EmitTests.Build("Late",
            "program Late; procedure Sleep(Ms: Cardinal); stdcall; external 'kernel32.dll'; var I: Integer; "
            + "begin I := 0; while I >= 0 do I := (I + 1) and 1023; Sleep($FFFFFFFF); end.", OptimizationLevel.O0);

        var failure = await Assert.ThrowsAsync<Xunit.Sdk.FailException>(() =>
            NativeRun.RunAsync(exe, dir, _ => { }, _ => { }, 20 * S, S, TestContext.Current.CancellationToken));

        TestContext.Current.TestOutputHelper?.WriteLine(failure.Message);
        Assert.StartsWith("wall-clock limit 1.0 s reached: Late.exe stopped after ", failure.Message);
        Assert.Matches(RunLimit.StillRunningPattern + "$", failure.Message);
        TestDirectories.Delete(dir);
    }

    /// <summary>
    /// Completes when the main thread of <paramref name="process"/> is in the wait of <c>Sleep</c>; from then on it stays there.
    /// The system has no event for "a thread began to wait", so the state is sampled — the reading the report of a stop takes —
    /// until it holds, at most for the wall cap of every native run and the test's token.
    /// </summary>
    private static async Task MainThreadSleepsAsync(Process process, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(NativeRun.WallLimit);
        using var sample = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        try
        {
            do
            {
                process.Refresh();
                if (process.Threads.Count > 0 && process.Threads[0].ThreadState == System.Diagnostics.ThreadState.Wait
                    && process.Threads[0].WaitReason == ThreadWaitReason.ExecutionDelay)
                {
                    return;
                }
            }
            while (await sample.WaitForNextTickAsync(limit.Token));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Assert.Fail($"{process.ProcessName} did not reach its wait within {NativeRun.Seconds(NativeRun.WallLimit)}");
        }
    }
}
