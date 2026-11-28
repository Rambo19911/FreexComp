namespace FreexComp.Debugger.Tests.TestSupport;

/// <summary>
/// A terminal for sessions over the scripted adapter: it opens nothing, records every call, and behaves as the test
/// tells it to. Calls arrive on session threads; tests read the records once the session has moved on.
/// </summary>
internal sealed class FakeTerminal : IDebuggeeTerminal
{
    private readonly Lock _gate = new();
    private readonly List<DebuggeeTerminalRequest> _requests = [];
    private readonly TaskCompletionSource<CancellationToken> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The run every start returns, unless <see cref="OnStart"/> decides otherwise.</summary>
    public FakeTerminalRun Run { get; } = new();

    /// <summary>What <c>StartAsync</c> does; default: it completes at once with <see cref="Run"/>.</summary>
    public Func<DebuggeeTerminalRequest, CancellationToken, Task<IDebuggeeTerminalRun>>? OnStart { get; set; }

    /// <summary>The requests of all starts so far.</summary>
    public IReadOnlyList<DebuggeeTerminalRequest> Requests
    {
        get
        {
            lock (_gate)
                return [.. _requests];
        }
    }

    /// <summary>Completes with the token of the first start, when it is called.</summary>
    public Task<CancellationToken> Started => _started.Task;

    public Task<IDebuggeeTerminalRun> StartAsync(DebuggeeTerminalRequest request, CancellationToken ct = default)
    {
        lock (_gate)
            _requests.Add(request);
        _started.TrySetResult(ct);
        return OnStart is { } start ? start(request, ct) : Task.FromResult<IDebuggeeTerminalRun>(Run);
    }
}

/// <summary>The console of a <see cref="FakeTerminal"/>: a process id, an end the test completes, and the closes it was asked for.</summary>
internal sealed class FakeTerminalRun : IDebuggeeTerminalRun
{
    private readonly Lock _gate = new();
    private readonly List<string> _closes = [];
    private readonly TaskCompletionSource<int?> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _closeCalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ProcessId { get; init; } = 4242;

    public Task<int?> Ended => _ended.Task;

    /// <summary>The command ended with this exit code, or (null) the console went away.</summary>
    public void End(int? exitCode) => _ended.TrySetResult(exitCode);

    /// <summary>What <c>CloseAsync</c> does after it was recorded; default: it completes at once.</summary>
    public Func<bool, CancellationToken, Task>? OnClose { get; set; }

    /// <summary>
    /// One entry per <c>CloseAsync</c> call: the <c>keepWindow</c> argument and what <see cref="Note"/> returned at that
    /// moment (a test notes there what must not have happened yet).
    /// </summary>
    public IReadOnlyList<string> Closes
    {
        get
        {
            lock (_gate)
                return [.. _closes];
        }
    }

    /// <summary>Text added to each entry of <see cref="Closes"/>, evaluated when the call arrives.</summary>
    public Func<string>? Note { get; set; }

    /// <summary>Completes when <c>CloseAsync</c> was called for the first time.</summary>
    public Task CloseCalled => _closeCalled.Task;

    public Task CloseAsync(bool keepWindow, CancellationToken ct = default)
    {
        lock (_gate)
            _closes.Add($"keepWindow={keepWindow}" + (Note is { } note ? " " + note() : ""));
        _closeCalled.TrySetResult();
        return OnClose is { } close ? close(keepWindow, ct) : Task.CompletedTask;
    }
}
