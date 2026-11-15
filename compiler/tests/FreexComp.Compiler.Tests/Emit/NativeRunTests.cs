using FreexComp.Tests.Shared;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-249: the native run limit is load-tolerant — the program's CPU time, not the wall clock, decides a busy hang; a
/// blocked program meets the wall-clock cap; a stopped run reports elapsed time and process state.
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

        var failure = await Assert.ThrowsAsync<Xunit.Sdk.FailException>(() =>
            NativeRun.RunAsync(exe, dir, _ => { }, _ => { }, 20 * S, S, TestContext.Current.CancellationToken));

        TestContext.Current.TestOutputHelper?.WriteLine(failure.Message);
        Assert.StartsWith("wall-clock limit 1.0 s reached: Blocked.exe stopped after ", failure.Message);
        Assert.Contains("still running, threads [wait:", failure.Message);
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
}
