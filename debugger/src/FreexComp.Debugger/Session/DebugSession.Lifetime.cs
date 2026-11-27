using FreexComp.Debugger.Dap;

namespace FreexComp.Debugger.Session;

// Termination, natural exit and adapter crash (architecture DBG-019 … DBG-023; DAP usage §7).
internal sealed partial class DebugSession
{
    /// <summary>
    /// Valid in every state and idempotent: every call returns the same task, which completes when the adapter
    /// process has exited or was killed (DBG-406).
    /// </summary>
    public Task TerminateAsync(CancellationToken ct = default)
    {
        if (_disposed)
            return Task.FromException(new ObjectDisposedException(nameof(IDebugSession)));
        if (ct.IsCancellationRequested)
            return Task.FromCanceled(ct);

        TaskCompletionSource done;
        lock (_gate)
        {
            if (_terminateTask is not null)
                return _terminateTask;
            done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _terminateTask = done.Task;
        }

        // The user wins over whatever the session is doing right now (architecture §6).
        _abort.Cancel();
        var accepted = Post(async () =>
        {
            try
            {
                await ShutdownAsync(TerminationCause.TerminatedByUser).ConfigureAwait(false);
                done.TrySetResult();
            }
            catch (Exception e)
            {
                done.TrySetException(e);
            }
        });
        if (!accepted)
            done.TrySetException(new ObjectDisposedException(nameof(IDebugSession)));
        return done.Task;
    }

    /// <summary>
    /// The shutdown sequence (DBG-020): <c>Terminating</c>, <c>disconnect</c>, close the adapter's input, wait for its
    /// exit, kill what is left, <c>Terminated</c>. No-op when the session has already ended.
    /// </summary>
    /// <param name="cause">Why the session ends.</param>
    /// <param name="terminateDebuggee">
    /// Whether the adapter is told to end the debuggee; null: yes unless the debuggee ended by itself (DBG-094, DBG-095).
    /// </param>
    private async Task ShutdownAsync(TerminationCause cause, bool? terminateDebuggee = null)
    {
        if (State == DebugState.Terminated)
            return;
        _abort.Cancel();
        if (State == DebugState.Idle)
        {
            Terminate(cause, null);
            return;
        }

        LeaveStop();
        SetState(DebugState.Terminating);
        // After a natural end the adapter is only asked to end itself (DBG-095); otherwise the debuggee is killed (DBG-094).
        await StopAdapterAsync(disconnect: true, terminateDebuggee: terminateDebuggee ?? cause != TerminationCause.Exited).ConfigureAwait(false);
        Terminate(cause, null);
        await FaultLaunchAsync(new DebuggerException(DebugErrorCode.LaunchFailed, Messages.SessionTerminated)).ConfigureAwait(false);
    }

    /// <summary>
    /// Ends the session at once, without the <c>Terminating</c> state: failed launch, adapter crash, protocol or
    /// internal error (DBG-023). The process tree is killed; a pending launch faults with <paramref name="launchError"/>
    /// (default: <c>LaunchFailed</c> with the same message) after the <c>Terminated</c> event.
    /// </summary>
    private async Task FailSessionAsync(TerminationCause cause, string message, Exception? launchError = null)
    {
        if (State is DebugState.Terminated or DebugState.Terminating)
            return;
        _abort.Cancel();
        LeaveStop();
        await StopAdapterAsync(disconnect: false, terminateDebuggee: false).ConfigureAwait(false);
        Terminate(LaunchPending ? TerminationCause.LaunchFailed : cause, message);
        await FaultLaunchAsync(launchError ?? new DebuggerException(DebugErrorCode.LaunchFailed, message)).ConfigureAwait(false);
    }

    /// <summary>The adapter's output ended (it exited) or it sent something that is not the protocol.</summary>
    private async Task OnAdapterClosedAsync(DebuggerException? protocolError)
    {
        if (State is DebugState.Idle or DebugState.Terminating or DebugState.Terminated || _adapter is not { } adapter)
            return;

        // Collect what the adapter left behind: its exit code and its last stderr lines (DBG-006, DBG-007).
        if (protocolError is null)
            await WaitBrieflyAsync(adapter.WaitForExitAsync(CancellationToken.None), _options.ShutdownWaits.AdapterExit).ConfigureAwait(false);
        var beforeInitialize = _capabilities is null;
        await StopAdapterAsync(disconnect: false, terminateDebuggee: false).ConfigureAwait(false);

        var stderr = adapter.LastStderrLines;
        var message = protocolError?.Message
            ?? (adapter.ExitCode is not { } exitCode ? Messages.AdapterExited
                : beforeInitialize ? Messages.AdapterExitedDuringStart(exitCode)
                : Messages.AdapterExitedWithCode(exitCode));
        if (stderr.Count > 0)
            message += "\n" + string.Join("\n", stderr);
        if (beforeInitialize && stderr.Any(l => l.Contains("python", StringComparison.OrdinalIgnoreCase))
            && stderr.Any(l => l.Contains(".dll", StringComparison.OrdinalIgnoreCase)))
            message += "\n" + Messages.MissingPythonRuntime;

        // While starting this is a failed launch: before the initialize response the adapter could not be started (DBG-007).
        await FailSessionAsync(TerminationCause.AdapterCrashed, message,
            new DebuggerException(beforeInitialize ? DebugErrorCode.AdapterStartFailed : DebugErrorCode.LaunchFailed, message)).ConfigureAwait(false);
    }

    /// <summary>
    /// Steps 1-5 of DBG-020. Safe to call more than once and without an adapter. When it returns the adapter process
    /// and everything it started are gone and its stderr has been read to the end.
    /// </summary>
    private async Task StopAdapterAsync(bool disconnect, bool terminateDebuggee)
    {
        ReleaseLaunchWatchers();
        if (_adapter is not { } adapter)
            return;

        if (disconnect && _client is { } client && _capabilities is not null && !adapter.HasExited)
        {
            try
            {
                var arguments = new DisconnectArguments(terminateDebuggee && HasCapability("supportTerminateDebuggee") ? true : null);
                await client.RequestAsync("disconnect", arguments, _options.ShutdownWaits.Disconnect, CancellationToken.None).ConfigureAwait(false);
            }
            catch (DebuggerException)
            {
                // Rejected, timed out or the adapter is gone: the process tree is killed below.
            }
        }

        adapter.CloseInput();
        if (disconnect)
            await WaitBrieflyAsync(adapter.WaitForExitAsync(CancellationToken.None), _options.ShutdownWaits.AdapterExit).ConfigureAwait(false);
        _client?.Close();
        await adapter.DisposeAsync().ConfigureAwait(false);   // kills the process tree, closes the job object
        await WaitBrieflyAsync(adapter.StderrCompleted, _options.ShutdownWaits.StderrEnd).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits up to <paramref name="limit"/> (one of <see cref="ShutdownWaits"/>) on the session clock. Neither a timeout
    /// nor a failure of what is waited for is an error: these waits only collect what the adapter left behind, and the
    /// end of the session must not depend on them.
    /// </summary>
    private async Task WaitBrieflyAsync(Task task, TimeSpan limit)
    {
        try
        {
            await task.WaitAsync(limit, _time).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Not in time, or the wait itself failed: the caller goes on (and kills the process tree).
        }
    }

    /// <summary>Session loop only: final transition, then <c>StateChanged</c> immediately before <c>Terminated</c> (DBG-421).</summary>
    private void Terminate(TerminationCause cause, string? message)
    {
        // Everything the debuggee wrote is reported before the end (DBG-058); the adapter and the debuggee are gone.
        EndOutputRelay();
        LeaveStop();
        SetState(DebugState.Terminated);
        _events.Raise(() => Terminated, new TerminatedEventArgs(cause, cause == TerminationCause.Exited ? ExitCode : null, message));
    }
}
