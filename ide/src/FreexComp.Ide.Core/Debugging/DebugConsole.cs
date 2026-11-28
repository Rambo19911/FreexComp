using System.ComponentModel;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Debugging;

/// <summary>
/// IDE-P3-097 (F14-047): the console a debugged console program runs in when <c>run.consoleMode</c> is <c>external</c> —
/// the adapter's command started in the IDE's console host through <see cref="IProcessLauncher"/>, its window titled
/// with the program's file name. A run that ends with <c>keepWindow</c> becomes the one waiting host that Run Without
/// Debugging uses as well (IDE-P3-089). The session calls these methods on its own threads; nothing here needs the UI
/// thread (the waiting-host slot is guarded by <see cref="RunService"/>), so a close at IDE exit is never left to a
/// dispatcher that no longer runs.
/// </summary>
internal sealed class DebugConsole(IProcessLauncher launcher, RunService run, ILog log) : IDebuggeeTerminal
{
    private const string Category = "debug";

    /// <summary>Why a request with an environment is refused (IDE-P3-097): the IDE starts programs with its own environment.</summary>
    public const string EnvironmentRefused = "the IDE starts the program with its own environment";

    public Task<IDebuggeeTerminalRun> StartAsync(DebuggeeTerminalRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        if (request.Environment is { Count: > 0 })
            throw new NotSupportedException(EnvironmentRefused);
        if (request.Command.Count == 0)
            throw new ArgumentException("The command is empty.", nameof(request));
        var arguments = CommandLine.Join([.. request.Command.Skip(1)]);
        var process = launcher.Start(new ProcessStartRequest(request.Command[0], arguments, request.WorkingDirectory)
        {
            Capture = false,
            ConsoleHost = true,
            ConsoleTitle = request.Title,
        }, static _ => { });
        return Task.FromResult<IDebuggeeTerminalRun>(new Run(process, run, log));
    }

    private sealed class Run : IDebuggeeTerminalRun
    {
        private readonly IRunningProcess _process;
        private readonly RunService _run;
        private readonly ILog _log;

        public Run(IRunningProcess process, RunService run, ILog log)
        {
            _process = process;
            _run = run;
            _log = log;
            // The host reports the command's exit code; a host that ends or loses its pipe first cancels Exited.
            Ended = process.Exited.ContinueWith(static t => t.IsCompletedSuccessfully ? t.Result : (int?)null, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public int ProcessId => _process.ProcessId;

        public Task<int?> Ended { get; }

        public async Task CloseAsync(bool keepWindow, CancellationToken ct = default)
        {
            if (keepWindow)
            {
                _run.AdoptWaitingHost(_process); // IDE-P3-101: the window stays at its key prompt until the next start
                return;
            }

            // IDE-P3-090: the whole tree, at once and without a prompt; IDE-P3-091: a failure is logged.
            try
            {
                if (!await _process.KillTreeAsync().ConfigureAwait(false))
                    _log.Error(Category, "The program's console window could not be closed.");
            }
            catch (Win32Exception e)
            {
                _log.Error(Category, "The program's console window could not be closed: " + e.Message);
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
