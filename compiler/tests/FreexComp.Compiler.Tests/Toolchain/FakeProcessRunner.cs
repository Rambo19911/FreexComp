using FreexComp.Compiler.Toolchain;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>Records every call and answers with a scripted result; no process is started.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<Call, CancellationToken, Task<(int ExitCode, string[] StdOut, string[] StdErr)>> _respond;

    public FakeProcessRunner(int exitCode = 0, string[]? stdOut = null, string[]? stdErr = null)
        : this((_, _) => Task.FromResult((exitCode, stdOut ?? [], stdErr ?? [])))
    {
    }

    public FakeProcessRunner(Func<Call, CancellationToken, Task<(int ExitCode, string[] StdOut, string[] StdErr)>> respond)
    {
        _respond = respond;
    }

    public List<Call> Calls { get; } = [];

    public async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string> onStdOut,
        Action<string> onStdErr,
        CancellationToken ct)
    {
        var call = new Call(fileName, [.. arguments], workingDirectory);
        lock (Calls)
        {
            Calls.Add(call);
        }

        var (exitCode, stdOut, stdErr) = await _respond(call, ct);
        foreach (var line in stdOut)
        {
            onStdOut(line);
        }

        foreach (var line in stdErr)
        {
            onStdErr(line);
        }

        return exitCode;
    }

    internal sealed record Call(string FileName, string[] Arguments, string WorkingDirectory);
}
