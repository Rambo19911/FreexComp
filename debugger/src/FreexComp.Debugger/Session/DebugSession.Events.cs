using System.Text.Json;
using FreexComp.Debugger.Dap;

namespace FreexComp.Debugger.Session;

// Adapter events (DAP usage §8, §9, §10). Every event is one work item, processed in arrival order.
internal sealed partial class DebugSession
{
    // Session loop only: module id (number or string, as text) -> file name of the module (DAP usage §9).
    private readonly Dictionary<string, string> _modules = new(StringComparer.Ordinal);

    // Session loop only: the session's own relay of the debuggee's output (DBG-058) and the timer of its next read.
    private OutputRelay? _outputRelay;
    private ITimer? _outputRelayTimer;

    private async Task OnAdapterEventAsync(string name, JsonElement body)
    {
        if (State is DebugState.Idle or DebugState.Terminating or DebugState.Terminated)
            return;

        switch (name)
        {
            case "initialized":
                await ConfigureAsync().ConfigureAwait(false);
                break;
            case "stopped":
                await OnStoppedAsync(DapJson.Deserialize<StoppedEventBody>(body)).ConfigureAwait(false);
                break;
            case "exited":
                // Recorded; the session ends with the terminated event (DBG-021). Windows exit codes are 32 bits wide
                // and may be reported unsigned.
                if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("exitCode", out var code) && code.TryGetInt64(out var exitCode))
                    _exitCode = unchecked((int)exitCode);
                break;
            case "terminated":
                await OnDebuggeeTerminatedAsync().ConfigureAwait(false);
                break;
            case "thread":
                OnThreadEvent(DapJson.Deserialize<ThreadEventBody>(body));
                break;
            case "output":
                OnOutputEvent(DapJson.Deserialize<OutputEventBody>(body));
                break;
            case "breakpoint":
                OnBreakpointEvent(DapJson.Deserialize<BreakpointEventBody>(body));
                break;
            case "module":
                OnModuleEvent(DapJson.Deserialize<ModuleEventBody>(body));
                break;
            case "process":
                if (DapJson.Deserialize<ProcessEventBody>(body).SystemProcessId is { } processId)
                    _processId = processId;
                break;
            case "capabilities":
                // Merged shallowly, property by property (DBG-051).
                if (_capabilities is not null)
                {
                    foreach (var (capability, value) in DapJson.Deserialize<CapabilitiesEventBody>(body).Capabilities)
                        _capabilities[capability] = value.Clone();
                }

                break;
            default:
                break;   // "continued" (we already are Running, DBG-018) and everything we do not use: ignored (traced by the client)
        }
    }

    /// <summary>
    /// The debuggee ended by itself (DBG-022): cause <c>Exited</c>, with the exit code when the adapter reported one
    /// (DBG-096). A launch that is still pending completes first: the debuggee did run.
    /// </summary>
    private async Task OnDebuggeeTerminatedAsync()
    {
        if (State == DebugState.Starting)
        {
            SetState(DebugState.Running);
            CompleteLaunch();
        }

        await ShutdownAsync(TerminationCause.Exited).ConfigureAwait(false);
    }

    private void OnThreadEvent(ThreadEventBody body)
    {
        ThreadChange? change = body.Reason switch
        {
            "started" => ThreadChange.Started,
            "exited" => ThreadChange.Exited,
            _ => null,
        };
        if (change == ThreadChange.Exited)
            _raiseReportedThreads.Remove(body.ThreadId);   // nothing more comes from this thread (DBG-248)
        if (change is { } value)
            _events.Raise(() => ThreadChanged, new ThreadChangedEventArgs(body.ThreadId, value));
    }

    /// <summary>Text is passed through unchanged; telemetry is dropped (DAP usage §10).</summary>
    private void OnOutputEvent(OutputEventBody body)
    {
        OutputCategory? category = body.Category switch
        {
            "stdout" => OutputCategory.Stdout,
            "stderr" => OutputCategory.Stderr,
            "telemetry" => null,
            _ => OutputCategory.Debugger,
        };
        if (category is { } value)
            _events.Raise(() => Output, new OutputEventArgs(value, body.Output));
    }

    /// <summary>
    /// Mode <see cref="OutputRelayMode.Files"/>: creates the two files the debuggee writes to and starts reading them
    /// (DBG-058). A directory that cannot be created fails the launch.
    /// </summary>
    private void StartOutputRelay()
    {
        // Mode Events: nothing to do, the adapter's output events are all there is (§10).
        if (OutputRelay.Mode == OutputRelayMode.Files)
        {
            try
            {
                _outputRelay = OutputRelay.Create();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new DebuggerException(DebugErrorCode.LaunchFailed, Messages.OutputRelayFailed + e.Message, null, e);
            }

            ArmOutputRelay();
        }
    }

    /// <summary>
    /// DBG-557: waits (off the session loop) until the program's side has opened the standard-input pipe, then has the
    /// loop close the session's end. A relay that ends first - the session ended, nobody ever opened the pipe - gives
    /// the wait up: the pipe stays as it is until then.
    /// </summary>
    private async Task CloseStdinOnceOpenedAsync(OutputRelay relay)
    {
        try
        {
            await relay.StdinClientConnected().ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
        {
            return;
        }

        Post(() =>
        {
            relay.CloseStdin();
            return Task.CompletedTask;
        });
    }

    private void ArmOutputRelay() =>
        _outputRelayTimer = _time.CreateTimer(_ => Post(PollOutputRelay), null, OutputRelay.PollInterval, Timeout.InfiniteTimeSpan);

    /// <summary>Session loop: what the debuggee wrote since the last read, as <c>Output</c> events; then the next read is due.</summary>
    private Task PollOutputRelay()
    {
        if (_outputRelay is not { } relay)
            return Task.CompletedTask;
        _outputRelayTimer?.Dispose();
        relay.Pump(RelayRead.Poll, RaiseDebuggeeOutput);
        ArmOutputRelay();
        return Task.CompletedTask;
    }

    /// <summary>Session loop: everything still unread is reported, then the files and their directory are removed.</summary>
    private void EndOutputRelay()
    {
        if (_outputRelay is not { } relay)
            return;
        _outputRelay = null;
        _outputRelayTimer?.Dispose();
        _outputRelayTimer = null;
        relay.Pump(RelayRead.Final, RaiseDebuggeeOutput);
        relay.Dispose();
    }

    private void RaiseDebuggeeOutput(OutputCategory category, string text) =>
        _events.Raise(() => Output, new OutputEventArgs(category, text));

    private void OnModuleEvent(ModuleEventBody body)
    {
        if (DapJson.IdText(body.Module.Id) is not { } id)
            return;
        if (body.Reason == "removed")
        {
            _modules.Remove(id);
            return;
        }

        if (body.Reason is not ("new" or "changed"))
            return;
        var name = body.Module.Name ?? "";
        var path = body.Module.Path ?? "";
        _modules[id] = path.Length > 0 ? Path.GetFileName(path) : name;
        var hasSymbols = body.Module.SymbolStatus is { } status && status.Contains("loaded", StringComparison.OrdinalIgnoreCase);
        _events.Raise(() => ModuleLoaded, new ModuleEventArgs(name, path, hasSymbols));
    }
}
