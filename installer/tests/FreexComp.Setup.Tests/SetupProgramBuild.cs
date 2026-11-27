using System.Diagnostics;

namespace FreexComp.Setup.Tests;

/// <summary>
/// A runnable single-file setup program (framework-dependent, so it needs no runtime pack), published once per test run
/// into this test's output folder with its own build folder (the solution's build output of the setup is not touched).
/// The tests that need a real process (uninstall hand-over, exit codes of the program) use it as the package's program.
/// </summary>
internal static class SetupProgramBuild
{
    private static readonly Lazy<string> Program = new(Publish, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string ProgramPath => Program.Value;

    private static string Publish()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var configuration = output.Parent!.Name;   // bin/<configuration>/<framework>/
        var work = Path.Combine(output.FullName, "setup-program");
        var project = Path.Combine(RepositoryRoot(), "installer", "src", "FreexComp.Setup", "FreexComp.Setup.csproj");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in (string[])["publish", project, "-c", configuration, "-r", "win-x64", "--self-contained", "false",
                     "-p:PublishSingleFile=true", "-o", Path.Combine(work, "out"), "--artifacts-path", Path.Combine(work, "build"), "--nologo"])
            start.ArgumentList.Add(arg);
        // a nested build: the settings the outer build engine left in the environment do not leak in, and no build
        // server or node stays behind
        const string noNodeReuse = "MSBUILDDISABLENODEREUSE";
        foreach (var name in start.Environment.Keys.Where(k => k.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase)).ToList())
            start.Environment.Remove(name);
        start.Environment[noNodeReuse] = "1";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = Process.Start(start)!;
        // stderr on a dedicated thread: a child's pipes are synchronous handles, so no asynchronous read (L-022)
        var stderrText = string.Empty;
        var stderrReader = new Thread(() => stderrText = process.StandardError.ReadToEnd()) { IsBackground = true, Name = "setup publish stderr" };
        stderrReader.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("publishing the setup program took more than 5 minutes");
        }

        stderrReader.Join();
        var program = Path.Combine(work, "out", "FreexComp.Setup.exe");
        if (process.ExitCode != 0 || !File.Exists(program))
            throw new InvalidOperationException($"publishing the setup program failed ({process.ExitCode}):\n{stdout}\n{stderrText}");
        return program;
    }

    public static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FreexComp.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException("FreexComp.sln not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Runs <paramref name="program"/> with the sandbox's environment variables; returns its exit code.</summary>
    public static int Run(string program, IEnumerable<string> args, IReadOnlyDictionary<string, string> variables, TimeSpan limit)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        foreach (var (name, value) in variables)
            start.Environment[name] = value;
        using var process = Process.Start(start)!;
        if (!process.WaitForExit(limit))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{program} did not exit within {limit}");
        }

        return process.ExitCode;
    }
}
