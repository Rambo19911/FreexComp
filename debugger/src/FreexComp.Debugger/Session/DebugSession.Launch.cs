using System.Text.Json;
using FreexComp.Debugger.Dap;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Session;

// Launch sequence (DAP usage §2, §3; architecture DBG-013, DBG-014). The launch is not one work item: the session
// awaits three things independently (DBG-053) - the initialized event, the launch response and the
// configurationDone response - and each arrives as its own work item, in the order the adapter sent them.
internal sealed partial class DebugSession
{
    private static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InitializedTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] RequiredCapabilities = ["supportsConfigurationDoneRequest", "supportsReadMemoryRequest"];

    // Session loop only.
    private TaskCompletionSource? _launch;          // non-null from LaunchAsync on; completed when the launch ended either way
    private LaunchRequest? _launchRequest;
    private CancellationToken _launchCancellation;
    private CancellationTokenRegistration _launchCancelRegistration;
    private ITimer? _launchTimer;
    private ITimer? _initializedTimer;
    private bool _initializedProcessed;
    private bool _launchResponded;
    private bool _configurationDone;

    private bool LaunchPending => _launch is { Task.IsCompleted: false };

    /// <summary>Test hook, set before the launch: how the stderr reader of the adapter is started (<see cref="AdapterProcess.Start"/>).</summary>
    internal Func<Func<Task>, Task>? StderrReaderStarter { get; set; }

    public Task LaunchAsync(LaunchRequest request, CancellationToken ct = default)
    {
        if (_disposed)
            return Task.FromException(new ObjectDisposedException(nameof(IDebugSession)));
        if (ct.IsCancellationRequested)
            return Task.FromCanceled(ct);

        var launch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = Post(async () =>
        {
            try
            {
                _state.Require(SessionMethod.LaunchAsync);
            }
            catch (DebuggerException e)
            {
                launch.TrySetException(e);
                return;
            }

            _launch = launch;
            await StartLaunchAsync(request, ct).ConfigureAwait(false);
        });
        return accepted ? launch.Task : Task.FromException(new ObjectDisposedException(nameof(IDebugSession)));
    }

    /// <summary>Starts the adapter, sends <c>initialize</c>, checks the capabilities and sends <c>launch</c> (not awaited).</summary>
    private async Task StartLaunchAsync(LaunchRequest request, CancellationToken ct)
    {
        _launchRequest = request;
        _launchCancellation = ct;
        _exceptionSettings = request.Exceptions;
        foreach (var source in request.Breakpoints)
            _breakpoints.Set(PathText.Normalize(source.Path), source.Breakpoints);
        SetState(DebugState.Starting);

        // Overall limit until the debuggee runs or stops for the first time (DBG-055).
        _launchTimer = _time.CreateTimer(
            _ => Post(() => FailLaunchAsync(new DebuggerException(DebugErrorCode.RequestTimeout, Messages.LaunchTimedOut, "launch"))),
            null, _options.LaunchTimeout, Timeout.InfiniteTimeSpan);
        // Cancelling the launch terminates the session (DBG-414).
        _launchCancelRegistration = ct.UnsafeRegister(_ => Post(() => FailLaunchAsync(new OperationCanceledException(ct))), null);

        try
        {
            if (!File.Exists(request.Program))
                throw new DebuggerException(DebugErrorCode.ProgramNotFound, Messages.ProgramNotFound + request.Program);

            // The type-information file is read once, here. Missing: not an error, values are shown as the adapter
            // prints them; present but unusable: the launch fails before the adapter is started (DBG-403).
            var typeInfoPath = request.TypeInfoPath ?? Path.ChangeExtension(request.Program, ".pdi");
            _typeInfo = PdiLoader.Load(typeInfoPath);
            _hasTypeInfo = _typeInfo is not null;
            if (_typeInfo is null)
                _events.Raise(() => Output, new OutputEventArgs(OutputCategory.Debugger, Messages.TypeInfoMissing(typeInfoPath)));

            _adapter = AdapterProcess.Start(_options.AdapterPath, _options.AdapterArguments, TraceStderr, StderrReaderStarter);
            _adapterProcessId = _adapter.ProcessId;
            _client = new DapClient(
                new DapTransport(_adapter.StandardOutput, _adapter.StandardInput),
                onEvent: (name, body) => Post(() => OnAdapterEventAsync(name, body)),
                onClosed: error => Post(() => OnAdapterClosedAsync(error)),
                _options.Trace, _time);
            _client.Start();

            JsonElement capabilities;
            try
            {
                capabilities = await RequestAsync("initialize", new InitializeArguments(), CancellationToken.None, InitializeTimeout).ConfigureAwait(false);
            }
            catch (DebuggerException e) when (e.Code is DebugErrorCode.RequestTimeout or DebugErrorCode.RequestFailed)
            {
                throw new DebuggerException(DebugErrorCode.AdapterStartFailed, Messages.AdapterStartFailed + e.Message, "initialize", e);
            }

            _capabilities = capabilities.ValueKind == JsonValueKind.Object
                ? capabilities.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal)
                : [];
            foreach (var required in RequiredCapabilities)
            {
                if (!HasCapability(required))
                    throw new DebuggerException(DebugErrorCode.MissingCapability, Messages.MissingCapability + required);
            }

            // The adapter must announce that it accepts configuration within 10 s of the initialize response (DBG-056).
            _initializedTimer = _time.CreateTimer(
                _ => Post(() => FailLaunchAsync(new DebuggerException(DebugErrorCode.LaunchFailed, Messages.AdapterNotReady))),
                null, InitializedTimeout, Timeout.InfiniteTimeSpan);

            // The response may arrive before or after the initialized event and before or after the configurationDone
            // response (DBG-053). It is handed to the loop from the reader task so that it keeps its place among the
            // events. Its only time limit is the overall launch limit.
            var arguments = new LaunchArguments
            {
                Program = request.Program,
                Args = request.Arguments,
                Cwd = request.WorkingDirectory ?? Path.GetDirectoryName(Path.GetFullPath(request.Program)) ?? "",
                Env = request.Environment,
                StopOnEntry = request.StopOnEntry || request.StopAtMainBody,
            };
            _ = _client.RequestAsync("launch", arguments, Timeout.InfiniteTimeSpan, _abort.Token,
                response =>
                {
                    _ = response.Exception;   // observed here: the loop may no longer care about a late failure
                    Post(() => OnLaunchResponseAsync(response));
                });
        }
        catch (DebuggerException e) when (e.Code == DebugErrorCode.AdapterCrashed)
        {
            // The adapter went away: the "adapter closed" work item that follows ends the launch (with stderr and exit code).
        }
        catch (DebuggerException e)
        {
            await FailLaunchAsync(e).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The session is ending; the work item that ends it follows.
        }
    }

    private async Task OnLaunchResponseAsync(Task<JsonElement> response)
    {
        if (!LaunchPending || State != DebugState.Starting)
            return;
        if (response.IsCompletedSuccessfully)
        {
            _launchResponded = true;
            CompleteLaunchIfRunning();
        }
        else if (response.Exception?.InnerException is DebuggerException { Code: DebugErrorCode.RequestFailed } failure)
        {
            await FailLaunchAsync(new DebuggerException(DebugErrorCode.LaunchFailed, failure.Message, "launch", failure)).ConfigureAwait(false);
        }

        // Otherwise the adapter went away or the session is ending: the work item for that follows.
    }

    /// <summary>
    /// Configuration phase, started by the <c>initialized</c> event (DBG-054): all <c>setBreakpoints</c>,
    /// <c>setFunctionBreakpoints</c>, <c>setExceptionBreakpoints</c>, <c>configurationDone</c>, each awaited before
    /// the next is sent.
    /// </summary>
    private async Task ConfigureAsync()
    {
        if (State != DebugState.Starting || _initializedProcessed)
            return;
        _initializedProcessed = true;
        _initializedTimer?.Dispose();
        try
        {
            foreach (var source in _breakpoints.Sources)
                await SendBreakpointsAsync(source, CancellationToken.None, raiseChanges: true).ConfigureAwait(false);
            // The function breakpoint on the raise hook: only when language exceptions are to be reported and the
            // type information names the hook. The adapter's own exception filters are never used (§4.3).
            await SyncRaiseHookAsync(CancellationToken.None).ConfigureAwait(false);
            await RequestAsync("setExceptionBreakpoints", new SetExceptionBreakpointsArguments(), CancellationToken.None).ConfigureAwait(false);
            await RequestAsync("configurationDone", null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (DebuggerException e) when (e.Code == DebugErrorCode.AdapterCrashed)
        {
            return;   // the "adapter closed" work item follows
        }
        catch (DebuggerException e)
        {
            // A timeout stays RequestTimeout; a rejected configuration request is a failed launch.
            await FailLaunchAsync(e.Code == DebugErrorCode.RequestFailed
                ? new DebuggerException(DebugErrorCode.LaunchFailed, e.Message, e.DapCommand, e)
                : e).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException)
        {
            return;   // the session is ending
        }

        _configurationDone = true;
        CompleteLaunchIfRunning();
    }

    /// <summary>
    /// DBG-055: the launch completes in state <c>Running</c> when both the launch response and the configurationDone
    /// response have arrived; with <c>StopOnEntry</c> or <c>StopAtMainBody</c> it waits for the stop instead (completed
    /// by the stop pipeline).
    /// </summary>
    private void CompleteLaunchIfRunning()
    {
        if (State != DebugState.Starting || !_launchResponded || !_configurationDone || _launchRequest!.StopOnEntry || _launchRequest.StopAtMainBody)
            return;
        SetState(DebugState.Running);
        CompleteLaunch();
    }

    private void CompleteLaunch()
    {
        if (!LaunchPending)
            return;
        ReleaseLaunchWatchers();
        _launch!.TrySetResult();
    }

    private void ReleaseLaunchWatchers()
    {
        _launchTimer?.Dispose();
        _initializedTimer?.Dispose();
        _launchCancelRegistration.Dispose();
    }

    /// <summary>
    /// Ends a launch that is still pending: the session becomes <c>Terminated</c> with cause <c>LaunchFailed</c> and the
    /// <c>Terminated</c> event is raised before the launch task faults (DBG-402). No-op when the launch has ended.
    /// </summary>
    private Task FailLaunchAsync(Exception error)
    {
        if (!LaunchPending)
            return Task.CompletedTask;
        var message = error is OperationCanceledException ? Messages.LaunchCancelled : error.Message;
        return FailSessionAsync(TerminationCause.LaunchFailed, message, error);
    }

    /// <summary>Completes the pending launch task as failed, after the events queued so far have been delivered.</summary>
    private async Task FaultLaunchAsync(Exception error)
    {
        if (!LaunchPending)
            return;
        ReleaseLaunchWatchers();
        await _events.FlushAsync().ConfigureAwait(false);
        if (error is OperationCanceledException)
            _launch!.TrySetCanceled(_launchCancellation);
        else
            _launch!.TrySetException(error);
    }
}
