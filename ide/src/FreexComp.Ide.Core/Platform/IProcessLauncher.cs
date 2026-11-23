namespace FreexComp.Ide.Core.Platform;

/// <summary>
/// What <see cref="IProcessLauncher.Start"/> runs (IDE-P3-081…083). <paramref name="Arguments"/> is the command-line tail,
/// passed verbatim (<c>run.arguments</c>). The environment is the IDE's.
/// </summary>
public sealed record ProcessStartRequest(string FileName, string Arguments, string WorkingDirectory)
{
    /// <summary>
    /// True (output mode, GUI programs): no console window, stdout/stderr captured and reported as lines, stdin redirected
    /// and closed at once so reads return end-of-file (IDE-P3-082). False: the program gets its own console window and
    /// nothing is redirected (external console).
    /// </summary>
    public bool Capture { get; init; } = true;

    /// <summary>
    /// The program runs inside the IDE's console host (IDE-P3-087/088, I-138): the launcher starts the IDE executable with
    /// <c>--console-host</c> in the job, nothing is redirected. <see cref="IRunningProcess.ProcessId"/> is the host's,
    /// <see cref="IRunningProcess.Exited"/> completes with the program's exit code when the host reports <c>exited</c>
    /// (the host then still waits at its key prompt until it is disposed) and is canceled when the host ends or its pipe
    /// breaks before that.
    /// </summary>
    public bool ConsoleHost { get; init; }
}

/// <summary>One captured output line of a program (IDE-P3-073): stderr lines have <see cref="IsError"/>.</summary>
public sealed record ProcessOutputLine(string Text, bool IsError);

/// <summary>
/// A program started by <see cref="IProcessLauncher"/>, together with every process it starts (one Job Object,
/// IDE-P3-083). Disposing closes the job, which terminates any descendant still alive (IDE-P3-084).
/// </summary>
public interface IRunningProcess : IDisposable
{
    int ProcessId { get; }

    /// <summary>
    /// Completes with the exit code when the root process has exited and both pipes were read to their end (at most 2 s
    /// after the exit, IDE-P3-084). Completes on a background thread.
    /// </summary>
    Task<int> Exited { get; }

    /// <summary>
    /// IDE-P3-090: terminates the whole process tree (<c>TerminateJobObject</c>, exit code 1) and waits up to 3 s for the root
    /// process; false when termination failed or timed out (IDE-P3-091).
    /// </summary>
    Task<bool> KillTreeAsync();
}

/// <summary>
/// Starts programs for Run (IDE-ARCH-012, IDE-P3-083). Captured lines go to <c>onOutput</c> as they arrive, on background
/// threads: CRLF, LF and lone CR end a line, ANSI sequences are removed, a partial last line is flushed after 100 ms of
/// silence and when the pipe ends; output is decoded as UTF-8 (invalid sequences → U+FFFD).
/// </summary>
public interface IProcessLauncher
{
    /// <exception cref="System.ComponentModel.Win32Exception">The process could not be created (IDE-P3-081 <c>Msg.Run.StartFailed</c>).</exception>
    IRunningProcess Start(ProcessStartRequest request, Action<ProcessOutputLine> onOutput);

    /// <summary>
    /// IDE-P3-035 (<c>Stop and Build</c> of <c>Msg.Build.OutputLocked</c>): terminates every process, with its descendants,
    /// whose executable is <paramref name="exePath"/> (a program started outside the IDE that holds the output exe) and waits
    /// up to 3 s for each to end; false when one could not be terminated in time.
    /// </summary>
    Task<bool> TerminateByPathAsync(string exePath);
}
