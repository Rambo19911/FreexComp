using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// IDE-P3-094: a scripted <see cref="IProcessLauncher"/>. Every start is recorded in <see cref="Started"/>; the test drives
/// the returned <see cref="FakeRunningProcess"/> (output lines, exit, kill) deterministically on its own thread.
/// <see cref="NextStartFails"/> makes the next start throw like a missing executable.
/// </summary>
public sealed class FakeProcessLauncher : IProcessLauncher
{
    private readonly List<FakeRunningProcess> _started = [];
    private int _nextId = 1000;

    public IReadOnlyList<FakeRunningProcess> Started => _started;

    /// <summary>The most recent start; throws when nothing was started.</summary>
    public FakeRunningProcess Last => _started.Count > 0 ? _started[^1] : throw new InvalidOperationException("No process was started.");

    /// <summary>Win32 error code the next <see cref="Start"/> fails with (2 = file not found); null = it succeeds.</summary>
    public int? NextStartFails { get; set; }

    /// <summary>Raised at the beginning of every <see cref="Start"/> (to check what happened before a launch).</summary>
    public event Action<ProcessStartRequest>? Starting;

    public IRunningProcess Start(ProcessStartRequest request, Action<ProcessOutputLine> onOutput)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onOutput);
        Starting?.Invoke(request);
        if (NextStartFails is { } error)
        {
            NextStartFails = null;
            throw new System.ComponentModel.Win32Exception(error);
        }

        var process = new FakeRunningProcess(request, onOutput, _nextId++);
        _started.Add(process);
        return process;
    }
}

/// <summary>A process of <see cref="FakeProcessLauncher"/>.</summary>
public sealed class FakeRunningProcess(ProcessStartRequest request, Action<ProcessOutputLine> onOutput, int processId) : IRunningProcess
{
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ProcessStartRequest Request { get; } = request;

    public int ProcessId { get; } = processId;

    public Task<int> Exited => _exited.Task;

    public int KillCount { get; private set; }

    public bool IsDisposed { get; private set; }

    /// <summary>False makes <see cref="KillTreeAsync"/> fail (IDE-P3-091).</summary>
    public bool KillSucceeds { get; set; } = true;

    public bool HasExited => _exited.Task.IsCompleted;

    /// <summary>A line on stdout (only when the request captures output).</summary>
    public void WriteStdOut(string line) => Write(line, isError: false);

    public void WriteStdErr(string line) => Write(line, isError: true);

    /// <summary>
    /// The program ends by itself with <paramref name="exitCode"/>; for a console host this is its <c>exited</c> pipe message
    /// (the host keeps waiting at its key prompt until disposed).
    /// </summary>
    public void Exit(int exitCode) => _exited.TrySetResult(exitCode);

    /// <summary>A console host ends (its window was closed) or its pipe breaks before it reported <c>exited</c> (IDE-P3-088).</summary>
    public void HostEnded()
    {
        if (!Request.ConsoleHost)
            throw new InvalidOperationException("The process is not a console host.");
        _exited.TrySetCanceled();
    }

    public Task<bool> KillTreeAsync()
    {
        KillCount++;
        if (!KillSucceeds)
            return Task.FromResult(false);
        _exited.TrySetResult(1); // TerminateJobObject exit code (IDE-P3-090)
        return Task.FromResult(true);
    }

    public void Dispose()
    {
        IsDisposed = true;
        _exited.TrySetResult(1); // closing the job ends the tree
    }

    private void Write(string line, bool isError)
    {
        if (!Request.Capture || Request.ConsoleHost)
            throw new InvalidOperationException("The process does not capture its output.");
        if (HasExited)
            throw new InvalidOperationException("The process has exited.");
        onOutput(new ProcessOutputLine(line, isError));
    }
}
