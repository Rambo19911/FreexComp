using System.Diagnostics;
using System.Text;

namespace FreexComp.Compiler.Toolchain;

/// <summary>
/// Starts one external process (docs/interfaces/compiler-cli.md §4; injectable so command lines can be tested
/// without processes). Returns the exit code; cancelling <paramref name="ct"/> kills the process tree.
/// </summary>
public interface IProcessRunner
{
    Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string> onStdOut,
        Action<string> onStdErr,
        CancellationToken ct);

    /// <summary>
    /// The same with the environment variables named by <paramref name="removedEnvironment"/> removed from the environment of
    /// the started process (C-335: a tool must not read the machine's settings). A runner that starts no real process has no
    /// environment and need not implement this: the default ignores the names.
    /// </summary>
    Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyList<string> removedEnvironment,
        Action<string> onStdOut,
        Action<string> onStdErr,
        CancellationToken ct) =>
        RunAsync(fileName, arguments, workingDirectory, onStdOut, onStdErr, ct);
}

/// <summary>
/// Real processes (CMP-602): no shell, arguments as a list, stdin closed, stdout/stderr read as UTF-8 lines — each pipe on
/// a dedicated thread (<see cref="PipeThread"/>), which is where the line callbacks run; every line is delivered before the
/// exit code is returned. Process tree killed in <c>finally</c> when it has not exited (cancellation or timeout).
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static ProcessRunner Instance { get; } = new();

    /// <summary>Test hook: receives the id of each started process.</summary>
    internal Action<int>? ProcessStarted { get; set; }

    /// <summary>Test hook: sees the start information of each process before it starts (a test sets variables of the child only).</summary>
    internal Action<ProcessStartInfo>? Starting { get; set; }

    public Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string> onStdOut,
        Action<string> onStdErr,
        CancellationToken ct) =>
        RunAsync(fileName, arguments, workingDirectory, [], onStdOut, onStdErr, ct);

    public async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyList<string> removedEnvironment,
        Action<string> onStdOut,
        Action<string> onStdErr,
        CancellationToken ct)
    {
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Starting?.Invoke(start);
        foreach (var variable in removedEnvironment)
        {
            start.Environment.Remove(variable);
        }

        using var process = new Process { StartInfo = start };
        ct.ThrowIfCancellationRequested();
        process.Start();
        try
        {
            ProcessStarted?.Invoke(process.Id);
            process.StandardInput.Close();

            // C-319: each pipe on its own thread; an asynchronous read would park a pool thread until the child writes
            var name = Path.GetFileName(fileName);
            var pipes = Task.WhenAll(
                PipeThread.ReadLines(process.StandardOutput, onStdOut, name + " stdout"),
                PipeThread.ReadLines(process.StandardError, onStdErr, name + " stderr"));
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            await pipes.WaitAsync(ct).ConfigureAwait(false);                    // every line is delivered before the exit code
            return process.ExitCode;
        }
        finally
        {
            await KillAsync(process).ConfigureAwait(false);
        }
    }

    /// <summary>Kills the tree of a process that has not exited and awaits its exit (at most 20 s); no thread waits (C-269).</summary>
    private static async Task KillAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
            // already exited between the check and the kill
        }
        catch (OperationCanceledException)
        {
            // the tree did not exit within the limit: nothing more can be done for it
        }
    }
}
