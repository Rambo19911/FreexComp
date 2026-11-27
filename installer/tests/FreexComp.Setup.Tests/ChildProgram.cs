using System.Diagnostics;
using System.Text;
using FreexComp.Tests.Shared;

namespace FreexComp.Setup.Tests;

/// <summary>What a child program printed and how it ended.</summary>
internal sealed record ChildResult(int ExitCode, string Output, string Error)
{
    /// <summary>Everything for a failure message.</summary>
    public string Describe(string program) => $"{program} ended with exit code {ExitCode}\nstdout:\n{Output}\nstderr:\n{Error}";
}

/// <summary>
/// Runs a program of the installed product to its end: a hard time limit (the whole process tree is stopped when it is
/// reached), both pipes read on dedicated threads (L-022) and exactly the environment the caller gives.
/// </summary>
internal static class ChildProgram
{
    /// <param name="environment">The complete environment of the child; null: the test's own.</param>
    public static ChildResult Run(string program, IEnumerable<string> arguments, string workingDirectory,
        IReadOnlyDictionary<string, string>? environment, TimeSpan limit)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        if (environment is not null)
        {
            start.Environment.Clear();
            foreach (var (name, value) in environment)
                start.Environment[name] = value;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException(program + " did not start");
        var output = ChildPipe.ReadToEndOnThread(process.StandardOutput, "stdout");
        var error = ChildPipe.ReadToEndOnThread(process.StandardError, "stderr");
        if (!process.WaitForExit(limit))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(TimeSpan.FromSeconds(30));
            throw new TimeoutException($"{program} did not end within {limit}");
        }

        // the pipes end when the child and everything that inherited them has ended
        if (!Task.WaitAll([output, error], TimeSpan.FromSeconds(30)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{program} ended, but its output pipes stayed open (a child of it is still running)");
        }

        return new ChildResult(process.ExitCode, output.Result, error.Result);
    }
}
