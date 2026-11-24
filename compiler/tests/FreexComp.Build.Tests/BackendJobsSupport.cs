using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Build.Tests;

/// <summary>
/// C-273 fixtures: <c>Hello</c> using <c>U01…Unn</c>, so the compile jobs in job-key order (CMP-1135: unit initialisation order,
/// the program last) are <c>System, U01, …, Unn, Hello</c>. With <paramref name="dlls"/> two units declare externals of
/// <c>user32.dll</c> and <c>gdi32.dll</c>, so the import-library jobs are <c>gdi32.dll, kernel32.dll, ntdll.dll, user32.dll</c>.
/// </summary>
internal static class JobFixture
{
    public const string ObjectDirectory = @"C:\p\obj\Debug";

    public static readonly ImmutableArray<string> DllsWithExternals = ["gdi32.dll", "kernel32.dll", "ntdll.dll", "user32.dll"];

    public static readonly ImmutableArray<string> DefaultDlls = ["kernel32.dll", "ntdll.dll"];

    public static string UnitName(int i) => "U" + i.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>The compile jobs of <see cref="Workspace"/> in job-key order.</summary>
    public static ImmutableArray<string> CompileJobs(int units) =>
        [.. Enumerable.Range(1, units).Select(UnitName).Prepend("System").Append("Hello")];

    public static BuildWorkspace Workspace(int units, string projectExtra = "", bool dlls = false)
    {
        var names = Enumerable.Range(1, units).Select(UnitName).ToList();
        var program = new StringBuilder("program Hello;");
        if (units > 0)
        {
            program.Append(" uses ").AppendJoin(", ", names).Append(';');
        }

        program.Append(" begin Writeln('hi') end.");
        var w = new BuildWorkspace(program.ToString(), BuildWorkspace.ProjectJson("Hello", extra: projectExtra));
        for (var i = 1; i <= units; i++)
        {
            w.Fs.Add($@"C:\p\{UnitName(i)}.pas", UnitSource(i, dlls));
        }

        return w;
    }

    public static string UnitSource(int i, bool dlls, int value = 0)
    {
        var external = !dlls ? ""
            : i == 1 ? " function IsAlpha(C: WideChar): LongBool; stdcall; external 'user32.dll' name 'IsCharAlphaW';"
            : i == 2 ? " function GetStockObject(I: Integer): Pointer; stdcall; external 'gdi32.dll';"
            : "";
        return $"unit {UnitName(i)}; interface const C{i:00} = {i};{external} implementation const Hidden = {value}; end.";
    }

    public static string Obj(string unit) => Path.Combine(ObjectDirectory, unit + ".obj");

    public static string Pcu(string unit) => Path.Combine(ObjectDirectory, unit + ".pcu");

    public static string Lib(string dll) => Path.Combine(ObjectDirectory, "imports", Path.GetFileNameWithoutExtension(dll) + ".lib");
}

/// <param name="Tool"><c>clang.exe</c> (compile job) or <c>llvm-dlltool.exe</c> (import-library job).</param>
/// <param name="Name">The unit name, or the DLL name.</param>
/// <param name="OutputPath">The <c>-o</c> object or the <c>-l</c> library.</param>
internal sealed record JobStart(string Tool, string Name, IReadOnlyList<string> Arguments, string OutputPath);

/// <summary>
/// The fake <see cref="IProcessRunner"/> of parallel-backend.md §11 on top of <see cref="FakeToolchain"/> (which writes the
/// <c>-o</c> / <c>-l</c> output): it records the start and the finish of every back-end job and the peak number of jobs running at
/// the same time, can hold a job (<see cref="Hold"/>: at a gate, until a scripted moment, for ever), make it print lines and fail
/// (<see cref="Script"/>). Everything that is not a job (<c>clang --version</c>, the link, the built program) goes straight to the
/// inner toolchain.
/// </summary>
internal sealed class JobRunner(FakeToolchain inner) : IProcessRunner
{
    private readonly Lock _gate = new();
    private readonly List<JobStart> _started = [];
    private readonly List<JobStart> _finished = [];
    private readonly Dictionary<string, (int Running, int Peak)> _perTool = new(StringComparer.Ordinal);
    private int _running;

    /// <summary>Awaited after the start is recorded and before the job does anything; honours the token (kill).</summary>
    public Func<JobStart, CancellationToken, Task>? Hold { get; set; }

    /// <summary>Lines the job prints (a line starting with <c>err:</c> goes to stderr) and its exit code; null → silent success.</summary>
    public Func<JobStart, (string[] Output, int ExitCode)?>? Script { get; set; }

    /// <summary>Runs when a job's process has ended (after it is recorded as finished).</summary>
    public Action<JobStart>? Finished { get; set; }

    /// <summary>Job names in start order.</summary>
    public ImmutableArray<string> Started
    {
        get
        {
            lock (_gate)
            {
                return [.. _started.Select(j => j.Name)];
            }
        }
    }

    /// <summary>Job names in the order their processes ended.</summary>
    public ImmutableArray<string> Ended
    {
        get
        {
            lock (_gate)
            {
                return [.. _finished.Select(j => j.Name)];
            }
        }
    }

    /// <summary>The largest number of jobs (of every kind) running at the same time.</summary>
    public int PeakAll { get; private set; }

    public int Peak(string tool)
    {
        lock (_gate)
        {
            return _perTool.TryGetValue(tool, out var counts) ? counts.Peak : 0;
        }
    }

    public async Task<int> RunAsync(
        string fileName, IReadOnlyList<string> arguments, string workingDirectory, Action<string> onStdOut, Action<string> onStdErr, CancellationToken ct)
    {
        var tool = Path.GetFileName(fileName);
        var job = tool switch
        {
            "clang.exe" when arguments.Contains("-c") => new JobStart(tool, Path.GetFileNameWithoutExtension(After(arguments, "-o")), arguments, After(arguments, "-o")),
            "llvm-dlltool.exe" => new JobStart(tool, After(arguments, "-D"), arguments, After(arguments, "-l")),
            _ => null,
        };
        if (job is null)
        {
            return await inner.RunAsync(fileName, arguments, workingDirectory, onStdOut, onStdErr, ct);
        }

        lock (_gate)
        {
            _started.Add(job);
            PeakAll = Math.Max(PeakAll, ++_running);
            var (running, peak) = _perTool.GetValueOrDefault(tool);
            _perTool[tool] = (running + 1, Math.Max(peak, running + 1));
        }

        try
        {
            if (Hold is { } hold)
            {
                await hold(job, ct);
            }

            ct.ThrowIfCancellationRequested();
            if (Script?.Invoke(job) is { } script)
            {
                foreach (var line in script.Output)
                {
                    (line.StartsWith("err:", StringComparison.Ordinal) ? onStdErr : onStdOut)(line);
                }

                if (script.ExitCode != 0)
                {
                    return script.ExitCode;
                }
            }

            return await inner.RunAsync(fileName, arguments, workingDirectory, onStdOut, onStdErr, ct);
        }
        finally
        {
            lock (_gate)
            {
                _running--;
                var (running, peak) = _perTool[tool];
                _perTool[tool] = (running - 1, peak);
                _finished.Add(job);
            }

            Finished?.Invoke(job);
        }
    }

    private static string After(IReadOnlyList<string> arguments, string option)
    {
        for (var i = 0; i + 1 < arguments.Count; i++)
        {
            if (arguments[i] == option)
            {
                return arguments[i + 1];
            }
        }

        throw new InvalidOperationException("missing " + option);
    }
}

/// <summary>
/// Holds the jobs of <paramref name="tool"/> (null: every job) until <paramref name="size"/> of them wait at the same time (or
/// until the last of the <paramref name="total"/> jobs has arrived), then lets that batch through. A scheduler whose limit is
/// below <paramref name="size"/> never opens the gate (the test's safety net ends it); one whose limit is above it shows a peak
/// above <paramref name="size"/>. Nothing here depends on timing. Seen as a simulation, each opening is one period in which
/// every running job "sleeps" (<see cref="Openings"/> periods in all).
/// </summary>
internal sealed class BatchGate
{
    private readonly Lock _gate = new();
    private readonly List<TaskCompletionSource> _waiting = [];
    private int _arrived;

    public BatchGate(JobRunner runner, int size, int total, string? tool = null)
    {
        runner.Hold = (job, ct) =>
        {
            if (tool is not null && job.Tool != tool)
            {
                return Task.CompletedTask;
            }

            var mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource[] batch = [];
            lock (_gate)
            {
                _waiting.Add(mine);
                _arrived++;
                PeakWaiting = Math.Max(PeakWaiting, _waiting.Count);
                if (_waiting.Count == size || _arrived == total)
                {
                    Openings++;
                    batch = [.. _waiting];
                    _waiting.Clear();
                }
            }

            foreach (var waiter in batch)
            {
                waiter.TrySetResult();
            }

            return mine.Task.WaitAsync(ct);
        };
    }

    /// <summary>The largest number of jobs that waited in the gate at the same time.</summary>
    public int PeakWaiting { get; private set; }

    /// <summary>How often the gate opened.</summary>
    public int Openings { get; private set; }
}

/// <summary>
/// Holds every job until all of <paramref name="order"/> are running, then lets their processes end one after another in
/// exactly that order (the next one is released when the previous process has ended).
/// </summary>
internal sealed class ScriptedCompletion
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, TaskCompletionSource> _held = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<string> _order;
    private int _next;

    public ScriptedCompletion(JobRunner runner, IReadOnlyList<string> order)
    {
        _order = order;
        runner.Hold = (job, ct) =>
        {
            var mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource? first = null;
            lock (_gate)
            {
                _held.Add(job.Name, mine);
                if (_held.Count == _order.Count)
                {
                    first = _held[_order[_next++]];
                }
            }

            first?.TrySetResult();
            return mine.Task.WaitAsync(ct);
        };
        runner.Finished = _ =>
        {
            TaskCompletionSource? next = null;
            lock (_gate)
            {
                if (_next < _order.Count)
                {
                    next = _held[_order[_next++]];
                }
            }

            next?.TrySetResult();
        };
    }
}

/// <summary>A clock that moves only when the test says so; timers (tool timeouts) fire inside <see cref="Advance"/>.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private TimeSpan _now;

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _now.Ticks;
        }
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) + _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _now += by;
            due = [.. _timers.Where(t => t.Due <= _now)];
            foreach (var timer in due)
            {
                _timers.Remove(timer);
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public TimeSpan Due { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                owner._timers.Remove(this);
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    Due = owner._now + dueTime;
                    owner._timers.Add(this);
                }
            }

            return true;
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
