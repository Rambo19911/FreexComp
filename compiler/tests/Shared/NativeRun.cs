using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FreexComp.Tests.Shared;

/// <summary>
/// C-249 (shared by C-258): runs a native test program with a load-tolerant limit. A program that hangs busy burns CPU, so
/// the main limit is the program's own CPU time (<see cref="CpuLimit"/>, TESTING.md's 20 s) rather than the wall clock,
/// which grows with the load of a parallel full-solution run; a program blocked without using CPU is stopped by the
/// wall-clock cap <see cref="WallLimit"/>, and the test's own cancellation token bounds both. On every cancellation the
/// failure reports the elapsed wall time, the CPU time and the process state (threads and what they wait for), captured
/// before the kill. No polling: one timer is re-armed for the earliest moment either limit can be reached.
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
        // as ProcessRunner (CMP-602): no shell, stdin closed, stdout/stderr as UTF-8 lines, process tree killed in finally
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            WorkingDirectory = workingDirectory,
        };
        using var limit = new RunLimit(Path.GetFileName(exe), cpuLimit, wallLimit, ct);
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onStdOut(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onStdErr(e.Data);
            }
        };

        ct.ThrowIfCancellationRequested();
        process.Start();
        try
        {
            limit.Attach(process.Id);
            process.StandardInput.Close();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(limit.Token);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (limit.Report is not null)
        {
            Assert.Fail(limit.Report);
            throw;
        }
        finally
        {
            Kill(process);
        }
    }

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(20_000);
            }
        }
        catch (InvalidOperationException)
        {
            // already exited between the check and the kill
        }
    }

    internal static string Seconds(TimeSpan time) => time.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s";
}

/// <summary>
/// The C-249 limit of one process run: <see cref="Token"/> is cancelled when the process attached by <see cref="Attach"/>
/// reaches its CPU limit or the wall cap (counted from construction), or when the test's token is cancelled; <see cref="Report"/>
/// then says why, after how much wall and CPU time, and in which state the process was.
/// </summary>
internal sealed class RunLimit : IDisposable
{
    private readonly string _name;
    private readonly TimeSpan _cpuLimit;
    private readonly TimeSpan _wallLimit;
    private readonly CancellationTokenSource _stop;
    private readonly CancellationTokenRegistration _cancelled;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly Lock _gate = new();
    private readonly Timer _timer;
    private Process? _process;
    private string? _report;

    public RunLimit(string name, TimeSpan cpuLimit, TimeSpan wallLimit, CancellationToken ct)
    {
        (_name, _cpuLimit, _wallLimit) = (name, cpuLimit, wallLimit);
        _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _timer = new Timer(_ => Check());
        _cancelled = ct.Register(() => Stop("test cancelled"));
    }

    public CancellationToken Token => _stop.Token;

    public string? Report
    {
        get
        {
            lock (_gate)
            {
                return _report;
            }
        }
    }

    /// <summary>Starts watching the started process <paramref name="processId"/>.</summary>
    public void Attach(int processId)
    {
        lock (_gate)
        {
            try
            {
                _process = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                // already exited
            }
        }

        ReArm(NativeRun.Decide(TimeSpan.Zero, _watch.Elapsed, _cpuLimit, _wallLimit).NextCheck);
    }

    public void Dispose()
    {
        _cancelled.Dispose();
        lock (_gate)
        {
            _timer.Dispose();
            _process?.Dispose();
            _process = null;
        }

        _stop.Dispose();
    }

    private void Stop(string reason)
    {
        lock (_gate)
        {
            _report ??= $"{reason}: {_name} stopped after {NativeRun.Seconds(_watch.Elapsed)} wall, {State(_process)}";
        }

        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // the run ended
        }
    }

    private void ReArm(TimeSpan due)
    {
        try
        {
            _timer.Change(due, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // the run ended
        }
    }

    private void Check()
    {
        string? reason;
        TimeSpan next;
        lock (_gate)
        {
            if (_report is not null || _process is null)
            {
                return;
            }

            (reason, next) = NativeRun.Decide(Cpu(_process), _watch.Elapsed, _cpuLimit, _wallLimit);
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
            return $"{NativeRun.Seconds(process.TotalProcessorTime)} CPU, still running, threads [{string.Join(", ", threads)}]";
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return $"state unavailable ({e.GetType().Name})";
        }
    }
}
