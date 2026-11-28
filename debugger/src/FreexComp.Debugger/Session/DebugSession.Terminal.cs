using System.Globalization;
using System.Text.Json;
using FreexComp.Debugger.Dap;

namespace FreexComp.Debugger.Session;

// Terminal mode (DBG-540 ... DBG-549): the program runs in a console that the caller's terminal opens. The adapter
// asks for that console with the reverse request "runInTerminal": the command it wants started there is its own
// executable in launcher mode, which creates the program in the console it runs in and tells the adapter the
// program's process id. The adapter goes on - "initialized", the launch response - only once the request was answered
// with success; after a refusal it says nothing more, so a start that fails ends the launch at once.
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
                if (!Post(() => OnTerminalStartedAsync(requestSeq, completed)))
                    _ = completed.Exception;   // the session is disposed: nobody is left to tell
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
            var reason = FirstLine(e.Message) is { Length: > 0 } line ? line : Messages.ReverseRequestFailed;
            _client?.Respond(requestSeq, RunInTerminalCommand, success: false, reason);
            await FailLaunchAsync(new DebuggerException(DebugErrorCode.LaunchFailed, Messages.ConsoleStartFailed + reason, null, e)).ConfigureAwait(false);
            return;
        }

        if (State is DebugState.Terminating or DebugState.Terminated)
            return;

        _terminalRun = run;
        _client?.Respond(requestSeq, RunInTerminalCommand, success: true, message: null, new RunInTerminalResponseBody(run.ProcessId));
        _ = run.Ended.ContinueWith(
            ended =>
            {
                // By contract it never faults; a fault is taken for "the console went away".
                var exitCode = ended.IsCompletedSuccessfully ? ended.Result : null;
                _ = ended.Exception;
                Post(() => OnTerminalEndedAsync(run, exitCode));
            },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    /// The command in the console ended (its exit code), or the console went away (null). Before the program runs
    /// either is a failed launch (DBG-549).
    /// </summary>
    private async Task OnTerminalEndedAsync(IDebuggeeTerminalRun run, int? exitCode)
    {
        if (!ReferenceEquals(run, _terminalRun) || State != DebugState.Starting)
            return;
        var message = exitCode is { } code
            ? string.Format(CultureInfo.InvariantCulture, Messages.ConsoleCommandEnded, code)
            : Messages.ConsoleClosed;
        await FailLaunchAsync(new DebuggerException(DebugErrorCode.LaunchFailed, message)).ConfigureAwait(false);
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

    private static string FirstLine(string text)
    {
        var end = text.AsSpan().IndexOfAny('\r', '\n');
        return (end < 0 ? text : text[..end]).Trim();
    }
}
