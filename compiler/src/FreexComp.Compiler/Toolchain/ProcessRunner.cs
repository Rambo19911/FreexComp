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
}

/// <summary>
/// Real processes (CMP-602): no shell, arguments as a list, stdin closed, stdout/stderr read as UTF-8 lines,
/// process tree killed in <c>finally</c> when it has not exited (cancellation or timeout).
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static ProcessRunner Instance { get; } = new();

    /// <summary>Test hook: receives the id of each started process.</summary>
    internal Action<int>? ProcessStarted { get; set; }

    public async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
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

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onStdOut(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onStdErr(e.Data);
            }
        };

        ct.ThrowIfCancellationRequested();
        process.Start();
        try
        {
            ProcessStarted?.Invoke(process.Id);
            process.StandardInput.Close();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode;
        }
        finally
        {
            Kill(process);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(20_000);
            }
        }
        catch (InvalidOperationException)
        {
            // already exited between the check and the kill
        }
    }
}
