using System.Diagnostics;
using System.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Build;

/// <summary>
/// Runs one build event command (build-events.md CMP-1270): the only way a command of a project reaches a process. Every line
/// the command writes is handed to <c>onStdOut</c> / <c>onStdErr</c> before the exit code is returned; cancelling
/// <c>ct</c> kills the command's process tree and throws <see cref="OperationCanceledException"/>.
/// </summary>
public interface ICommandRunner
{
    /// <param name="command">The command line, as it would be typed in a command prompt.</param>
    /// <param name="workingDirectory">The directory the command runs in.</param>
    /// <param name="onStdOut">Receives each line of standard output.</param>
    /// <param name="onStdErr">Receives each line of standard error.</param>
    /// <param name="ct">Cancels the command.</param>
    /// <returns>The exit code of the command.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">The command could not be started.</exception>
    Task<int> RunAsync(string command, string workingDirectory, Action<string> onStdOut, Action<string> onStdErr, CancellationToken ct);
}

/// <summary>
/// The real runner (build-events.md CMP-1271, 1272): the command interpreter of the system directory — never one found through
/// the search path, an environment variable or the working directory. An outer interpreter with a fixed command line switches
/// the output code page to UTF-8 and starts a second interpreter, which runs the command: an interpreter keeps the code page it
/// found when it started, so only the second one writes UTF-8. The command text never appears on the outer command line — it
/// travels in the environment variable <see cref="CommandVariable"/> (the only one the runner adds, CMP-1263) and is inserted
/// by delayed expansion, which is not parsed again; the second interpreter gets it as <c>/s /c "…"</c>, taken as typed.
/// No window; standard input is closed at once. There is no time limit (CMP-1273).
/// </summary>
public sealed class ShellCommandRunner : ICommandRunner
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    internal ShellCommandRunner()
    {
    }

    public static ShellCommandRunner Instance { get; } = new();

    /// <summary>
    /// CMP-1272: how long the output of a process that outlives the interpreter (and still holds its pipes) is waited for.
    /// </summary>
    internal TimeSpan DetachedOutputWait { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Test hook: sees the start information before the process is started (and may change the child's environment).</summary>
    internal Action<ProcessStartInfo>? Starting { get; init; }

    /// <summary>Test hook: receives the id of the started interpreter.</summary>
    internal Action<int>? ProcessStarted { get; init; }

    /// <summary>The interpreter: <c>&lt;system directory&gt;\cmd.exe</c>.</summary>
    internal static string InterpreterPath => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary>The environment variable of the child that holds the command text.</summary>
    internal const string CommandVariable = "FREEXCOMP_EVENT_COMMAND";

    /// <summary>
    /// The argument text of the outer interpreter — the same for every command: <c>/d</c> (no auto-run commands), <c>/v:on</c>
    /// (delayed expansion, for the one reference below), <c>/s /c "…"</c>; inside, the switch of the output code page joined
    /// with <c>&amp;</c> to the second interpreter (<c>/d /v:off /s /c "…"</c>), so the second one runs whatever the switch
    /// returns and its exit code — the command's — is the exit code of the whole.
    /// </summary>
    internal static string Arguments { get; } =
        $"/d /v:on /s /c \"\"{Path.Combine(Environment.SystemDirectory, "chcp.com")}\" 65001>nul&\"{InterpreterPath}\" /d /v:off /s /c \"!{CommandVariable}!\"\"";

    public async Task<int> RunAsync(string command, string workingDirectory, Action<string> onStdOut, Action<string> onStdErr, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(onStdOut);
        ArgumentNullException.ThrowIfNull(onStdErr);
        var start = new ProcessStartInfo(InterpreterPath)
        {
            Arguments = Arguments,                                              // one raw string: the interpreter has its own quote rule
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            WorkingDirectory = workingDirectory,
        };
        start.Environment[CommandVariable] = command;                           // for the child only; replaces an inherited one
        Starting?.Invoke(start);

        using var process = new Process { StartInfo = start };
        ct.ThrowIfCancellationRequested();
        process.Start();
        var lines = new LineGate();
        var completed = false;
        try
        {
            ProcessStarted?.Invoke(process.Id);
            process.StandardInput.Close();                                      // a command that reads input gets end-of-file

            // each pipe on its own thread (C-319)
            var pipes = Task.WhenAll(
                PipeThread.ReadLines(process.StandardOutput, line => lines.Deliver(onStdOut, line), "command stdout"),
                PipeThread.ReadLines(process.StandardError, line => lines.Deliver(onStdErr, line), "command stderr"));
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            try
            {
                // every line is delivered before the exit code; CMP-1272: a process the command left running may still hold
                // the pipes — its output is not waited for beyond the limit, and it is left running
                await pipes.WaitAsync(DetachedOutputWait, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }

            completed = true;
            return process.ExitCode;
        }
        finally
        {
            lines.Close();                                                      // nothing is delivered after the call has ended
            if (!completed)
            {
                await KillTreeAsync(process).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Cancellation (CMP-1269): kills the interpreter and everything it started — also when the interpreter itself has already
    /// exited — and awaits its exit (at most 20 s); no thread waits.
    /// </summary>
    private static async Task KillTreeAsync(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException or OperationCanceledException)
        {
            // already gone, not ours to end, or it did not exit within the limit: nothing more can be done for it
        }
    }

    /// <summary>Hands lines to the caller until the call ends; a line that arrives later (a detached process) is dropped.</summary>
    private sealed class LineGate
    {
        private readonly Lock _gate = new();
        private bool _closed;

        public void Deliver(Action<string> sink, string line)
        {
            lock (_gate)
            {
                if (!_closed)
                {
                    sink(line);
                }
            }
        }

        public void Close()
        {
            lock (_gate)
            {
                _closed = true;
            }
        }
    }
}
