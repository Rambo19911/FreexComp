using System.Globalization;
using System.Text.Json;
using FreexComp.Debugger.Dap;

namespace FreexComp.Debugger.Session;

// Terminal mode (DBG-540 ... DBG-554): the program runs in a console that the caller's terminal opens. The adapter
// asks for that console with the reverse request "runInTerminal": the command it wants started there is its own
// executable in launcher mode, which creates the program in the console it runs in and tells the adapter the
// program's process id. The adapter goes on - "initialized", the launch response - only once the request was answered
// with success; after a refusal it says nothing more, so a start that fails ends the launch at once.
// The console belongs to the session until its end: it is closed exactly once before the end is reported (DBG-551),
// and when it goes away first - the user closed the window - the session ends as if the user had stopped it (DBG-550).
internal sealed partial class DebugSession
{
    private const string RunInTerminalCommand = "runInTerminal";
    private const string ExternalTerminal = "externalTerminal";

    // Reader task: 1 once the adapter's first runInTerminal request has arrived. Every later one is refused.
    private int _runInTerminalSeen;

    // Session loop only.
    private IDebuggeeTerminal? _terminal;            // non-null: terminal mode
    private string _launchDirectory = "";            // the launch request's cwd: the console's directory unless the adapter names one
    private bool _terminalRequestAccepted;           // the adapter asked for the console, with valid arguments
    private IDebuggeeTerminalRun? _terminalRun;      // the console, once the terminal has started it
    private bool _terminalCloseAsked;                // CloseAsync was called for it: once and never again (DBG-432)
    private ITimer? _commandEndedTimer;              // the command ended with a code: the wait for the adapter's report of the exit

    /// <summary>
    /// Reader task, terminal mode only: decides about a request of the adapter. The first <c>runInTerminal</c> goes to
    /// the session loop, which answers it (DBG-543); every other request is refused at once by the client (false).
    /// </summary>
    private bool OnReverseRequest(int requestSeq, string command, JsonElement arguments) =>
        command == RunInTerminalCommand
        && Interlocked.Exchange(ref _runInTerminalSeen, 1) == 0
        && Post(() => OnRunInTerminalAsync(requestSeq, arguments));

    /// <summary>
    /// The adapter asks for the program's console. While the session starts, the terminal is told to start the command
    /// (DBG-544) - not awaited here: events and the launch time limit go on while the console opens.
    /// </summary>
    private async Task OnRunInTerminalAsync(int requestSeq, JsonElement arguments)
    {
        if (State != DebugState.Starting || _terminal is not { } terminal)
        {
            _client?.Respond(requestSeq, RunInTerminalCommand, success: false, Messages.ReverseRequestNotSupported);
            return;
        }

        if (ParseTerminalRequest(arguments) is not { } request)
        {
            _client?.Respond(requestSeq, RunInTerminalCommand, success: false, Messages.ReverseRequestInvalidArguments);
            await FailLaunchAsync(new DebuggerException(DebugErrorCode.LaunchFailed, Messages.ConsoleRequestInvalid)).ConfigureAwait(false);
            return;
        }

        _terminalRequestAccepted = true;
        Task<IDebuggeeTerminalRun> start;
        try
        {
            // The token is cancelled when the launch is cancelled or the session ends.
            start = terminal.StartAsync(request, _abort.Token);
        }
        catch (Exception e)
        {
            start = Task.FromException<IDebuggeeTerminalRun>(e);
        }

        _ = start.ContinueWith(
            completed =>
            {
                // A disposed session has no loop any more: a console that opens now is closed from here.
                if (!Post(() => OnTerminalStartedAsync(requestSeq, completed)))
                    _ = CloseLateRunAsync(completed);
            },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    /// The command exactly as the adapter gave it - element 0 is the executable, nothing is quoted, joined or split,
    /// and it is never given to a shell (<c>kind</c>, <c>title</c> and <c>argsCanBeInterpretedByShell</c> are ignored).
    /// Null when the arguments are not usable (DBG-543): <c>args</c> must be a non-empty array of strings, <c>cwd</c> a
    /// string when present, <c>env</c> an object of strings and nulls when present.
    /// </summary>
    private DebuggeeTerminalRequest? ParseTerminalRequest(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("args", out var args) || args.ValueKind != JsonValueKind.Array || args.GetArrayLength() == 0
            || args.EnumerateArray().Any(argument => argument.ValueKind != JsonValueKind.String))
            return null;

        var directory = _launchDirectory;
        if (arguments.TryGetProperty("cwd", out var cwd))
        {
            if (cwd.ValueKind != JsonValueKind.String)
                return null;
            if (cwd.GetString() is { Length: > 0 } given)
                directory = given;
        }

        // A null value stays null: "remove this variable". No variables at all: no environment.
        Dictionary<string, string?>? environment = null;
        if (arguments.TryGetProperty("env", out var env))
        {
            if (env.ValueKind != JsonValueKind.Object)
                return null;
            foreach (var variable in env.EnumerateObject())
            {
                if (variable.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    return null;
                (environment ??= [])[variable.Name] = variable.Value.GetString();
            }
        }

        return new DebuggeeTerminalRequest(
            [.. args.EnumerateArray().Select(argument => argument.GetString()!)], directory, environment,
            Path.GetFileName(_launchRequest!.Program));
    }

    /// <summary>
    /// The terminal's start has completed. Started: the adapter gets the id of the console's owner (DBG-545) and goes
    /// on. Failed or cancelled: the adapter is refused and the launch fails without waiting for it (DBG-546).
    /// </summary>
    private async Task OnTerminalStartedAsync(int requestSeq, Task<IDebuggeeTerminalRun> start)
    {
        IDebuggeeTerminalRun run;
        try
        {
            run = await start.ConfigureAwait(false) ?? throw new InvalidOperationException("");
        }
        catch (Exception e)
        {
            if (State is DebugState.Terminating or DebugState.Terminated)
                return;   // the session ended first (that is what cancelled the start)
            var reason = ReasonOf(e);
            _client?.Respond(requestSeq, RunInTerminalCommand, success: false, reason);
            await FailLaunchAsync(new DebuggerException(DebugErrorCode.LaunchFailed, Messages.ConsoleStartFailed + reason, null, e)).ConfigureAwait(false);
            return;
        }

        if (State is DebugState.Terminating or DebugState.Terminated)
        {
            // The session began to end while the console opened: nobody uses it (DBG-551). The end was reported
            // without it, so a failure of this close has nowhere to go.
            await CloseRunAsync(run, keepWindow: false).ConfigureAwait(false);
            return;
        }

        _terminalRun = run;
        _client?.Respond(requestSeq, RunInTerminalCommand, success: true, message: null, new RunInTerminalResponseBody(run.ProcessId));
        _ = run.Ended.ContinueWith(
            ended =>
            {
                // By contract it never faults; a fault is taken for "the console went away".
                var exitCode = ended.IsCompletedSuccessfully ? ended.Result : null;
                _ = ended.Exception;
                // A console that went away ends the session whatever its state, as a terminate of the user does:
                // the work item in progress is given up at once.
                if (exitCode is null)
                    _abort.Cancel();
                Post(() => OnTerminalEndedAsync(run, exitCode));
            },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    /// The command in the console ended (its exit code), or the console went away (null). Before the program runs
    /// either is a failed launch (DBG-549). While it runs or is stopped (DBG-550): a console that went away is the
    /// user's stop; an exit code is the program's, which the adapter reports itself - only when it does not, the
    /// session ends on its own after the wait for the adapter. Once the session is ending nothing of this matters.
    /// </summary>
    private async Task OnTerminalEndedAsync(IDebuggeeTerminalRun run, int? exitCode)
    {
        if (!ReferenceEquals(run, _terminalRun))
            return;
        switch (State)
        {
            case DebugState.Starting:
                var message = exitCode is { } startCode
                    ? string.Format(CultureInfo.InvariantCulture, Messages.ConsoleCommandEnded, startCode)
                    : Messages.ConsoleClosed;
                await FailLaunchAsync(new DebuggerException(DebugErrorCode.LaunchFailed, message)).ConfigureAwait(false);
                break;

            case DebugState.Running or DebugState.Stopped when exitCode is null:
                await ShutdownAsync(TerminationCause.TerminatedByUser).ConfigureAwait(false);
                break;

            case DebugState.Running or DebugState.Stopped when _exitCode is null:
                // No "exited" event yet: the adapter gets the time it gets to end by itself.
                var code = exitCode.Value;
                _commandEndedTimer = _time.CreateTimer(
                    _ => Post(() => OnCommandEndUnreportedAsync(code)), null, _options.ShutdownWaits.AdapterExit, Timeout.InfiniteTimeSpan);
                break;
        }
    }

    /// <summary>The command ended a while ago and the adapter still has not reported the exit: the session ends with the command's code.</summary>
    private async Task OnCommandEndUnreportedAsync(int exitCode)
    {
        if (State is not (DebugState.Running or DebugState.Stopped) || _exitCode is not null)
            return;
        _exitCode = exitCode;
        await ShutdownAsync(TerminationCause.Exited).ConfigureAwait(false);
    }

    /// <summary>
    /// The final transition (DBG-551): the console is closed, exactly once, before the end of the session is reported.
    /// A close that fails or takes too long does not hold the session: one line of the debugger says so.
    /// </summary>
    private async Task CloseTerminalAsync(bool keepWindow)
    {
        _commandEndedTimer?.Dispose();
        _commandEndedTimer = null;
        if (_terminalRun is not { } run || _terminalCloseAsked)
            return;
        _terminalCloseAsked = true;
        if (await CloseRunAsync(run, keepWindow).ConfigureAwait(false) is { } failure)
            _events.Raise(() => Output, new OutputEventArgs(OutputCategory.Debugger, Messages.ConsoleCloseFailed + failure));
    }

    /// <summary>
    /// Asks the terminal to close a console and waits for it, at most <see cref="ShutdownWaits.TerminalClose"/> on the
    /// session clock. Null when it closed; else why not (the first line of the fault, or that the time ran out - the
    /// close is then told to give up).
    /// </summary>
    private async Task<string?> CloseRunAsync(IDebuggeeTerminalRun run, bool keepWindow)
    {
        using var giveUp = new CancellationTokenSource();
        Task close;
        try
        {
            close = run.CloseAsync(keepWindow, giveUp.Token);
        }
        catch (Exception e)
        {
            return ReasonOf(e);
        }

        try
        {
            await close.WaitAsync(_options.ShutdownWaits.TerminalClose, _time).ConfigureAwait(false);
            return null;
        }
        catch (TimeoutException) when (!close.IsCompleted)
        {
            giveUp.Cancel();
            _ = close.ContinueWith(static late => _ = late.Exception, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return Messages.ConsoleCloseTimedOut;
        }
        catch (Exception e)
        {
            return ReasonOf(e);
        }
    }

    /// <summary>A console whose start completed when the session had no loop any more (it is disposed): closed, nothing kept.</summary>
    private async Task CloseLateRunAsync(Task<IDebuggeeTerminalRun> start)
    {
        if (!start.IsCompletedSuccessfully)
        {
            _ = start.Exception;
            return;
        }

        if (start.Result is { } run)
            await CloseRunAsync(run, keepWindow: false).ConfigureAwait(false);
    }

    /// <summary>
    /// DBG-548: in terminal mode the adapter reports the launch answered, or the program's process, although it never
    /// asked for the console. The program then has no console of its own - and the adapter's standard input (DBG-059):
    /// the launch fails, and the shutdown ends the program. True when it did.
    /// </summary>
    private async Task<bool> FailIfConsoleNotRequestedAsync()
    {
        if (_terminal is null || _terminalRequestAccepted || State != DebugState.Starting || !LaunchPending)
            return false;
        await FailLaunchAsync(new DebuggerException(DebugErrorCode.LaunchFailed, Messages.ConsoleNotRequested)).ConfigureAwait(false);
        return true;
    }

    /// <summary>Why a call of the terminal failed: the first line of the fault's message, or <c>failed</c> when it has none.</summary>
    private static string ReasonOf(Exception e)
    {
        var end = e.Message.AsSpan().IndexOfAny('\r', '\n');
        return (end < 0 ? e.Message : e.Message[..end]).Trim() is { Length: > 0 } line ? line : Messages.ReverseRequestFailed;
    }
}
