using System.Text.Json;
using System.Threading.Channels;
using FreexComp.Debugger.Dap;

namespace FreexComp.Debugger.Session;

/// <summary>Internal test hook (debugger testing §1).</summary>
internal interface IDebugSessionDiagnostics
{
    int? AdapterProcessId { get; }
    int InternalStopCount { get; }
}

/// <summary>
/// The session (architecture §5, §6). This file holds the session loop, the snapshot properties, the public method
/// surface and disposal; the other parts are: <c>Launch</c> (launch sequence), <c>Lifetime</c> (termination, natural
/// exit, adapter crash), <c>Stops</c> (stop pipeline, execution control, threads and stack), <c>Breakpoints</c> and
/// <c>Events</c> (adapter events mapped to public events).
/// </summary>
/// <remarks>
/// All session state is mutated only by the session loop: one task draining the work items (public calls, adapter
/// events, responses that must stay in order with events, timers, "adapter closed"). While a work item awaits a
/// response the loop does not take the next item; responses complete from the reader task and never need the loop.
/// </remarks>
internal sealed partial class DebugSession : IDebugSession, IDebugSessionDiagnostics
{
    private readonly DebugSessionOptions _options;
    private readonly TimeProvider _time;
    private readonly SessionStateMachine _state;
    private readonly EventDispatcher _events;
    private readonly Channel<Func<Task>> _work =
        Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _loop;
    private readonly Lock _gate = new();
    // Cancelled when the session starts to end (terminate, dispose, crash): aborts the work item in progress.
    private readonly CancellationTokenSource _abort = new();
    private Task? _disposeTask;
    private Task? _terminateTask;
    private volatile bool _disposed;

    // Snapshots: immutable values in volatile fields, updated by the session loop before the event is queued (DBG-024).
    private volatile StopInfo? _currentStop;
    private volatile object? _processId;
    private volatile object? _exitCode;
    private volatile object? _adapterProcessId;
#pragma warning disable CS0649 // assigned by the type-information tasks
    private volatile bool _hasTypeInfo;
#pragma warning restore CS0649

    // Session loop only.
    private AdapterProcess? _adapter;
    private DapClient? _client;
    private Dictionary<string, JsonElement>? _capabilities;

    public DebugSession(DebugSessionOptions options)
        : this(options, DebugState.Idle, TimeProvider.System)
    {
    }

    /// <summary>Test hook: a session on an injected clock.</summary>
    internal DebugSession(DebugSessionOptions options, TimeProvider time)
        : this(options, DebugState.Idle, time)
    {
    }

    /// <summary>Test hook: a session that is already in <paramref name="initialState"/> (no adapter is started).</summary>
    internal DebugSession(DebugSessionOptions options, DebugState initialState, TimeProvider? time = null)
    {
        _options = options;
        _time = time ?? TimeProvider.System;
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

    int? IDebugSessionDiagnostics.AdapterProcessId => (int?)_adapterProcessId;
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

    // ---- execution control ----

    public Task ContinueAsync(CancellationToken ct = default) =>
        PostAsync(SessionMethod.ContinueAsync, () => ResumeAsync("continue", _currentStop!.ThreadId, ct), ct);

    public Task PauseAsync(CancellationToken ct = default) =>
        PostAsync(SessionMethod.PauseAsync, () => PauseCoreAsync(ct), ct);

    public Task StepOverAsync(int threadId, CancellationToken ct = default) =>
        PostAsync(SessionMethod.StepOverAsync, () => ResumeAsync("next", threadId, ct), ct);

    public Task StepIntoAsync(int threadId, CancellationToken ct = default) =>
        PostAsync(SessionMethod.StepIntoAsync, () => ResumeAsync("stepIn", threadId, ct), ct);

    public Task StepOutAsync(int threadId, CancellationToken ct = default) =>
        PostAsync(SessionMethod.StepOutAsync, () => ResumeAsync("stepOut", threadId, ct), ct);

    public Task RunToLineAsync(string path, int line, CancellationToken ct = default) =>
        NotYetAsync<bool>(SessionMethod.RunToLineAsync, "G-052", ct);

    // ---- breakpoints ----

    public Task<IReadOnlyList<BreakpointStatus>> SetBreakpointsAsync(
        string path, IReadOnlyList<BreakpointSpec> breakpoints, CancellationToken ct = default) =>
        PostAsync(SessionMethod.SetBreakpointsAsync, () => SetBreakpointsCoreAsync(path, breakpoints, ct), ct);

    public Task SetExceptionSettingsAsync(ExceptionSettings settings, CancellationToken ct = default) =>
        NotYetAsync<bool>(SessionMethod.SetExceptionSettingsAsync, "G-055", ct);

    // ---- inspection ----

    public Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(CancellationToken ct = default) =>
        PostAsync(SessionMethod.GetThreadsAsync, () => GetThreadsCoreAsync(ct), ct);

    public Task<StackTraceResult> GetStackTraceAsync(int threadId, int startFrame = 0, int maxFrames = 200, CancellationToken ct = default) =>
        PostAsync(SessionMethod.GetStackTraceAsync, () => GetStackTraceCoreAsync(threadId, startFrame, maxFrames, ct), ct);

    public Task<IReadOnlyList<VariableScope>> GetScopesAsync(int frameId, CancellationToken ct = default) =>
        PostAsync<IReadOnlyList<VariableScope>>(SessionMethod.GetScopesAsync, () =>
        {
            RequireFrame(frameId);   // a stale id faults before anything is sent (DBG-408)
            throw NotYet(SessionMethod.GetScopesAsync, "G-041");
        }, ct);

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
        lock (_gate)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        _abort.Cancel();
        Post(async () =>
        {
            try
            {
                await ShutdownAsync(TerminationCause.Disposed).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Errors are swallowed (DBG-407); the adapter process is released below in any case.
            }
        });
        _work.Writer.TryComplete();
        await _loop.ConfigureAwait(false);
        if (_adapter is { } adapter)
            await adapter.DisposeAsync().ConfigureAwait(false);

        // Let the dispatcher deliver what is queued (Terminated is the last event) unless we are inside a handler.
        if (Thread.CurrentThread.Name != EventDispatcher.ThreadName)
            await _events.FlushAsync().ConfigureAwait(false);

        _events.Dispose();
    }

    // ---- session loop ----

    private async Task RunLoopAsync()
    {
        await foreach (var item in _work.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await item().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // Never crashes the host process (architecture §9): the session ends instead.
                try
                {
                    var message = e is DebuggerException { Code: DebugErrorCode.ProtocolError }
                        ? e.Message
                        : Messages.InternalErrorPrefix + e.GetType().Name + ": " + e.Message;
                    await FailSessionAsync(TerminationCause.AdapterCrashed, message).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Nothing more can be done for this session.
                }
            }
        }
    }

    /// <summary>Queues a work item; false once the session is disposed.</summary>
    private bool Post(Func<Task> item) => _work.Writer.TryWrite(item);

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
        var accepted = Post(async () =>
        {
            try
            {
                _state.Require(method);
                completion.TrySetResult(await body().ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && _abort.IsCancellationRequested)
            {
                // The session ended while the call was in flight (DBG-026).
                completion.TrySetException(new DebuggerException(DebugErrorCode.StaleReference, Messages.SessionEnded));
            }
            catch (OperationCanceledException e)
            {
                completion.TrySetCanceled(ct.IsCancellationRequested ? ct : e.CancellationToken);
            }
            catch (Exception e)
            {
                completion.TrySetException(e);
            }
        });
        return accepted ? completion.Task : Task.FromException<T>(new ObjectDisposedException(nameof(IDebugSession)));
    }

    /// <summary>
    /// Sends a request for the work item in progress and awaits the response. Cancelled by the caller's token and
    /// when the session starts to end.
    /// </summary>
    private async Task<JsonElement> RequestAsync(string command, object? arguments, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _abort.Token);
        return await _client!.RequestAsync(command, arguments, timeout ?? _options.RequestTimeout, linked.Token).ConfigureAwait(false);
    }

    /// <summary>Session loop only: the snapshot changes before the event is queued (DBG-024, DBG-422).</summary>
    private void SetState(DebugState state)
    {
        var old = _state.Transition(state);
        _events.Raise(() => StateChanged, new StateChangedEventArgs(old, state));
    }

    /// <summary>True when the stored capabilities (initialize response merged with capabilities events) say so; absent counts as false.</summary>
    private bool HasCapability(string name) =>
        _capabilities is not null && _capabilities.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.True;

    private void TraceStderr(string line)
    {
        if (_options.Trace is not { } trace)
            return;
        try
        {
            trace(new DapTraceEntry(_time.GetUtcNow(), DapDirection.AdapterStderr, line));
        }
        catch (Exception)
        {
            // A failing trace sink must never break the session.
        }
    }

    /// <summary>A call whose behaviour belongs to a later task: state-checked like every call, then not implemented.</summary>
    private Task<T> NotYetAsync<T>(SessionMethod method, string task, CancellationToken ct) =>
        PostAsync<T>(method, () => throw NotYet(method, task), ct);

    private static NotImplementedException NotYet(SessionMethod method, string task) =>
        new($"{method} is implemented by task {task}.");
}
