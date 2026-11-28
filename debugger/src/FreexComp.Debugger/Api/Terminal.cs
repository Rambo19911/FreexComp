namespace FreexComp.Debugger;

/// <summary>Opens a console for the debugged program (terminal mode). Implemented by the caller of the session.</summary>
public interface IDebuggeeTerminal
{
    /// <summary>Starts <see cref="DebuggeeTerminalRequest.Command"/> in a console of its own. A fault fails the launch.</summary>
    Task<IDebuggeeTerminalRun> StartAsync(DebuggeeTerminalRequest request, CancellationToken ct = default);
}

/// <param name="Command">Executable and arguments, one element each; run as given, never through a shell.</param>
/// <param name="WorkingDirectory">Absolute directory the command starts in.</param>
/// <param name="Environment">Variables to add to (value) or remove from (null) the terminal's own environment; null: none.</param>
/// <param name="Title">Title for the console window: the program's file name.</param>
public sealed record DebuggeeTerminalRequest(
    IReadOnlyList<string> Command, string WorkingDirectory,
    IReadOnlyDictionary<string, string?>? Environment, string Title);

/// <summary>A console that <see cref="IDebuggeeTerminal.StartAsync"/> opened.</summary>
public interface IDebuggeeTerminalRun
{
    /// <summary>Id of the process that owns the console.</summary>
    int ProcessId { get; }

    /// <summary>Completes, never faults: the command's exit code when it ended; null when the console went away before that.</summary>
    Task<int?> Ended { get; }

    /// <summary>End of the session. False: end every process started and close the console. True: the console may stay.</summary>
    Task CloseAsync(bool keepWindow, CancellationToken ct = default);
}
