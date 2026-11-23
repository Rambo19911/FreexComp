using System.Threading.Channels;

namespace FreexComp.Debugger.Session;

/// <summary>Internal test hook (debugger testing §1).</summary>
internal interface IDebugSessionDiagnostics
{
    int? AdapterProcessId { get; }
    int InternalStopCount { get; }
}

/// <summary>
/// The session (architecture §5, §6). Skeleton of task G-015: the session loop, the state checks of every public
/// method, the snapshot properties, event delivery through the dispatcher, termination from Idle and disposal.
/// The launch sequence and everything that talks to the adapter arrive with tasks G-016 and later.
/// </summary>
internal sealed class DebugSession : IDebugSession, IDebugSessionDiagnostics
{
    private readonly DebugSessionOptions _options;
    private readonly SessionStateMachine _state;
    private readonly EventDispatcher _events;
    // Session loop: one reader drains the work items; all session state is mutated only there (no locks).
    private readonly Channel<Func<Task>> _work =
        Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _loop;
    private readonly Lock _disposeGate = new();
    private Task? _disposeTask;
    private volatile bool _disposed;

    // Skeleton (G-015): the fields and events below are assigned and raised by the launch, stop and event-mapping tasks.
#pragma warning disable CS0067, CS0649
    // Snapshots: immutable values in volatile fields, updated by the session loop before the event is queued (DBG-024).
    private volatile StopInfo? _currentStop;
    private volatile object? _processId;
    private volatile object? _exitCode;
    private volatile bool _hasTypeInfo;

    public DebugSession(DebugSessionOptions options)
        : this(options, DebugState.Idle)
    {
    }

    /// <summary>Test hook: a session that is already in <paramref name="initialState"/> (no adapter is started).</summary>
    internal DebugSession(DebugSessionOptions options, DebugState initialState)
    {
        _options = options;
        _state = new SessionStateMachine(initialState);
        _events = new EventDispatcher(this, () => Output);
        _loop = Task.Run(RunLoopAsync);
    }

    // ---- state ----

    public DebugState State => _state.State;
    public StopInfo? CurrentStop => _currentStop;
    public int? ProcessId => (int?)_processId;
    public int? ExitCode => (int?)_exitCode;
    public bool HasTypeInfo => _hasTypeInfo;

    int? IDebugSessionDiagnostics.AdapterProcessId => null;
    int IDebugSessionDiagnostics.InternalStopCount => 0;

    // ---- events ----

    public event EventHandler<StateChangedEventArgs>? StateChanged;
    public event EventHandler<StopInfo>? Stopped;
    public event EventHandler? Continued;
    public event EventHandler<OutputEventArgs>? Output;
    public event EventHandler<BreakpointChangedEventArgs>? BreakpointChanged;
    public event EventHandler<ThreadChangedEventArgs>? ThreadChanged;
    public event EventHandler<ModuleEventArgs>? ModuleLoaded;
    public event EventHandler<TerminatedEventArgs>? Terminated;
#pragma warning restore CS0067, CS0649

    // ---- lifetime ----

    public Task LaunchAsync(LaunchRequest request, CancellationToken ct = default) =>
        NotYetAsync<bool>(SessionMethod.LaunchAsync, "G-016", ct);

    public Task TerminateAsync(CancellationToken ct = default) =>
        PostAsync(SessionMethod.TerminateAsync, () =>
        {
            switch (State)
            {
                case DebugState.Terminated:
                    break;   // no-op (DBG-406)
                case DebugState.Idle:
                    Terminate(TerminationCause.TerminatedByUser, null);
                    break;
                default:
                    throw NotYet(SessionMethod.TerminateAsync, "G-019");
            }

            return Task.FromResult(true);
        }, ct);

    // ---- execution control ----

    public Task ContinueAsync(CancellationToken ct = default) =>
        NotYetAsync<bool>(SessionMethod.ContinueAsync, "G-025", ct);

    public Task PauseAsync(CancellationToken ct = default) =>
        NotYetAsync<bool>(SessionMethod.PauseAsync, "G-025", ct);

    public Task StepOverAsync(int threadId, CancellationToken ct = default) =>
        NotYetAsync<bool>(SessionMethod.StepOverAsync, "G-025", ct);

    public Task StepIntoAsync(int threadId, CancellationToken ct = default) =>
        NotYetAsync<bool>(SessionMethod.StepIntoAsync, "G-025", ct);

    public Task StepOutAsync(int threadId, CancellationToken ct = default) =>
        NotYetAsync<bool>(SessionMethod.StepOutAsync, "G-025", ct);

    public Task RunToLineAsync(string path, int line, CancellationToken ct = default) =>
        NotYetAsync<bool>(SessionMethod.RunToLineAsync, "G-052", ct);

    // ---- breakpoints ----

    public Task<IReadOnlyList<BreakpointStatus>> SetBreakpointsAsync(
        string path, IReadOnlyList<BreakpointSpec> breakpoints, CancellationToken ct = default) =>
        NotYetAsync<IReadOnlyList<BreakpointStatus>>(SessionMethod.SetBreakpointsAsync, "G-022", ct);

    public Task SetExceptionSettingsAsync(ExceptionSettings settings, CancellationToken ct = default) =>
        NotYetAsync<bool>(SessionMethod.SetExceptionSettingsAsync, "G-055", ct);

    // ---- inspection ----

    public Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(CancellationToken ct = default) =>
        NotYetAsync<IReadOnlyList<ThreadInfo>>(SessionMethod.GetThreadsAsync, "G-026", ct);

    public Task<StackTraceResult> GetStackTraceAsync(int threadId, int startFrame = 0, int maxFrames = 200, CancellationToken ct = default) =>
        NotYetAsync<StackTraceResult>(SessionMethod.GetStackTraceAsync, "G-026", ct);

    public Task<IReadOnlyList<VariableScope>> GetScopesAsync(int frameId, CancellationToken ct = default) =>
        NotYetAsync<IReadOnlyList<VariableScope>>(SessionMethod.GetScopesAsync, "G-041", ct);

    public Task<IReadOnlyList<VariableInfo>> GetChildrenAsync(VariableHandle handle, int start = 0, int count = 0, CancellationToken ct = default) =>
        NotYetAsync<IReadOnlyList<VariableInfo>>(SessionMethod.GetChildrenAsync, "G-041", ct);

    public Task<EvaluateResult> EvaluateAsync(string expression, int? frameId, EvaluateContext context, CancellationToken ct = default) =>
        NotYetAsync<EvaluateResult>(SessionMethod.EvaluateAsync, "G-047", ct);

    public Task<VariableInfo> SetValueAsync(string evaluateName, string valueExpression, int? frameId, CancellationToken ct = default) =>
        NotYetAsync<VariableInfo>(SessionMethod.SetValueAsync, "G-048", ct);

    public Task<byte[]> ReadMemoryAsync(ulong address, int count, CancellationToken ct = default) =>
        NotYetAsync<byte[]>(SessionMethod.ReadMemoryAsync, "G-030", ct);

    // ---- disposal ----

    /// <summary>
    /// Ends the session (cause <c>Disposed</c> unless already terminated), delivers the events queued so far, then stops
    /// the loop and the dispatcher. Afterwards every method faults with <see cref="ObjectDisposedException"/> (DBG-407).
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        _work.Writer.TryWrite(() =>
        {
            if (State != DebugState.Terminated)
                Terminate(TerminationCause.Disposed, null);
            return Task.CompletedTask;
        });
        _work.Writer.TryComplete();
        await _loop.ConfigureAwait(false);

        // Let the dispatcher deliver what is queued (Terminated is the last event) unless we are inside a handler.
        if (Thread.CurrentThread.Name != EventDispatcher.ThreadName)
        {
            var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _events.Post(delivered.SetResult);
            await delivered.Task.ConfigureAwait(false);
        }

        _events.Dispose();
    }

    // ---- session loop ----

    private async Task RunLoopAsync()
    {
        await foreach (var item in _work.Reader.ReadAllAsync().ConfigureAwait(false))
            await item().ConfigureAwait(false);
    }

    /// <summary>
    /// Posts a public call to the session loop. The returned task faults with <c>InvalidState</c> when the state at
    /// the time the call is processed does not allow it (DBG-401, DBG-026); nothing is sent to the adapter then.
    /// </summary>
    private Task<T> PostAsync<T>(SessionMethod method, Func<Task<T>> body, CancellationToken ct)
    {
        if (_disposed)
            return Task.FromException<T>(new ObjectDisposedException(nameof(IDebugSession)));
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<T>(ct);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = _work.Writer.TryWrite(async () =>
        {
            try
            {
                _state.Require(method);
                completion.TrySetResult(await body().ConfigureAwait(false));
            }
            catch (OperationCanceledException e)
            {
                completion.TrySetCanceled(e.CancellationToken);
            }
            catch (Exception e)
            {
                completion.TrySetException(e);
            }
        });
        return accepted ? completion.Task : Task.FromException<T>(new ObjectDisposedException(nameof(IDebugSession)));
    }

    /// <summary>Session loop only: final transition, then <c>StateChanged</c> immediately before <c>Terminated</c> (DBG-421).</summary>
    private void Terminate(TerminationCause cause, string? message)
    {
        _currentStop = null;
        SetState(DebugState.Terminated);
        _events.Raise(() => Terminated, new TerminatedEventArgs(cause, cause == TerminationCause.Exited ? ExitCode : null, message));
    }

    /// <summary>Session loop only: the snapshot changes before the event is queued (DBG-024, DBG-422).</summary>
    private void SetState(DebugState state)
    {
        var old = _state.Transition(state);
        _events.Raise(() => StateChanged, new StateChangedEventArgs(old, state));
    }

    /// <summary>A call whose behaviour belongs to a later task: state-checked like every call, then not implemented.</summary>
    private Task<T> NotYetAsync<T>(SessionMethod method, string task, CancellationToken ct) =>
        PostAsync<T>(method, () => throw NotYet(method, task), ct);

    private static NotImplementedException NotYet(SessionMethod method, string task) =>
        new($"{method} is implemented by task {task}.");
}
