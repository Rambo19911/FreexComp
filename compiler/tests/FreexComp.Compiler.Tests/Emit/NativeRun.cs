using System.Diagnostics;
using System.Globalization;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>
/// C-249: runs a native test program with a load-tolerant limit. A program that hangs busy burns CPU, so the main limit is
/// the program's own CPU time (<see cref="CpuLimit"/>, TESTING.md's 20 s) rather than the wall clock, which grows with the
/// load of a parallel full-solution run; a program blocked without using CPU is stopped by the wall-clock cap
/// <see cref="WallLimit"/>, and the test's own cancellation token bounds both. On every cancellation the failure reports
/// the elapsed wall time, the CPU time and the process state (threads and what they wait for), captured before the kill.
/// No polling: one timer is re-armed for the earliest moment either limit can be reached.
/// </summary>
internal static class NativeRun
{
    public static readonly TimeSpan CpuLimit = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan WallLimit = TimeSpan.FromSeconds(120);

    /// <summary>
    /// The limit decision at one check: a reason to stop the program, or the delay until the next check — the earliest
    /// wall-clock moment at which the CPU limit (CPU time grows at most as fast as the wall clock per thread) or the wall
    /// limit can be reached.
    /// </summary>
    public static (string? StopReason, TimeSpan NextCheck) Decide(TimeSpan cpu, TimeSpan wall, TimeSpan cpuLimit, TimeSpan wallLimit)
    {
        if (cpu >= cpuLimit)
        {
            return ($"CPU limit {Seconds(cpuLimit)} reached", TimeSpan.Zero);
        }

        if (wall >= wallLimit)
        {
            return ($"wall-clock limit {Seconds(wallLimit)} reached", TimeSpan.Zero);
        }

        var next = cpuLimit - cpu < wallLimit - wall ? cpuLimit - cpu : wallLimit - wall;
        return (null, next < MinimumCheck ? MinimumCheck : next);
    }

    private static readonly TimeSpan MinimumCheck = TimeSpan.FromMilliseconds(50);

    /// <summary>Runs <paramref name="exe"/> (no arguments) and returns its exit code; fails the test with the run's state when a limit or the test's cancellation stops it.</summary>
    public static Task<int> RunAsync(string exe, string workingDirectory, Action<string> onStdOut, Action<string> onStdErr) =>
        RunAsync(exe, workingDirectory, onStdOut, onStdErr, CpuLimit, WallLimit, TestContext.Current.CancellationToken);

    public static async Task<int> RunAsync(string exe, string workingDirectory, Action<string> onStdOut, Action<string> onStdErr,
        TimeSpan cpuLimit, TimeSpan wallLimit, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var watch = Stopwatch.StartNew();
        var gate = new object();
        Process? process = null;
        string? report = null;

        Timer? timer = null;

        void Stop(string reason)
        {
            lock (gate)
            {
                report ??= $"{reason}: {Path.GetFileName(exe)} stopped after {Seconds(watch.Elapsed)} wall, {State(process)}";
            }

            stop.Cancel();
        }

        void ReArm(TimeSpan due)
        {
            try
            {
                timer?.Change(due, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // the run ended
            }
        }

        void Check()
        {
            string? reason;
            TimeSpan next;
            lock (gate)
            {
                if (report is not null || process is null)
                {
                    return;
                }

                (reason, next) = Decide(Cpu(process), watch.Elapsed, cpuLimit, wallLimit);
            }

            if (reason is not null)
            {
                Stop(reason);
            }
            else
            {
                ReArm(next);
            }
        }

        timer = new Timer(_ => Check());
        using var disposeTimer = timer;
        using var cancelled = ct.Register(() => Stop("test cancelled"));
        var runner = new ProcessRunner
        {
            ProcessStarted = id =>
            {
                lock (gate)
                {
                    try
                    {
                        process = Process.GetProcessById(id);
                    }
                    catch (ArgumentException)
                    {
                        // already exited
                    }
                }

                ReArm(Decide(TimeSpan.Zero, watch.Elapsed, cpuLimit, wallLimit).NextCheck);
            },
        };

        try
        {
            return await runner.RunAsync(exe, [], workingDirectory, onStdOut, onStdErr, stop.Token);
        }
        catch (OperationCanceledException) when (report is not null)
        {
            Assert.Fail(report);
            throw;
        }
        finally
        {
            lock (gate)
            {
                process?.Dispose();
                process = null;
            }
        }
    }

    private static TimeSpan Cpu(Process process)
    {
        try
        {
            return process.TotalProcessorTime;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return TimeSpan.Zero;                                       // exited meanwhile
        }
    }

    /// <summary>CPU time, whether the process still runs, and each thread's state (and wait reason).</summary>
    private static string State(Process? process)
    {
        if (process is null)
        {
            return "process not started or already gone";
        }

        try
        {
            process.Refresh();
            if (process.HasExited)
            {
                return $"process exited with code {process.ExitCode}";
            }

            var threads = process.Threads.Cast<ProcessThread>()
                .Select(t => t.ThreadState == System.Diagnostics.ThreadState.Wait ? $"wait:{t.WaitReason}" : t.ThreadState.ToString());
            return $"{Seconds(process.TotalProcessorTime)} CPU, still running, threads [{string.Join(", ", threads)}]";
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return $"state unavailable ({e.GetType().Name})";
        }
    }

    private static string Seconds(TimeSpan time) => time.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s";
}
