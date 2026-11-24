using System.Diagnostics;
using System.Text;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.IntegrationTests.TestSupport;

/// <summary>A test program compiled into its own temp directory.</summary>
/// <param name="Directory">The fresh directory that holds everything below.</param>
/// <param name="SourcePath">The copy of the program source that was compiled (the paths in the debug information).</param>
/// <param name="ExePath">The executable.</param>
/// <param name="TypeInfoPath">The type-information file next to the executable (absent without debug information).</param>
internal sealed record BuiltProgram(string Directory, string SourcePath, string ExePath, string TypeInfoPath);

/// <summary>
/// What the integration tests share (scripted test DBG-301, DBG-302): the repository root, the toolchain with the real
/// debug adapter, the Pascal compiler, and the test programs built with it. Anything missing fails the test that asks
/// for it, with a message that names it - integration tests never skip.
/// </summary>
public sealed class IntegrationFixture : IDisposable
{
    /// <summary>Safety net for waits on a loaded machine; every wait is on an event, never on a duration.</summary>
    public static readonly TimeSpan Safety = TimeSpan.FromSeconds(120);

    private static readonly TimeSpan CompileLimit = TimeSpan.FromMinutes(5);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, BuiltProgram> _built = new(StringComparer.Ordinal);
    private readonly string _buildRoot = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "dbg-" + Guid.NewGuid().ToString("N"));

    public IntegrationFixture()
    {
        RepositoryRoot = FindRepositoryRoot();
        ToolchainDirectory = Path.Combine(RepositoryRoot, "third_party", "llvm", "bin");
        AdapterPath = Path.Combine(ToolchainDirectory, "lldb-dap.exe");
    }

    public string RepositoryRoot { get; }

    /// <summary>The LLVM tools directory, resolved like the compiler's default.</summary>
    public string ToolchainDirectory { get; }

    /// <summary>The real debug adapter.</summary>
    public string AdapterPath { get; }

    private string RtlDirectory => Path.Combine(RepositoryRoot, "rtl", "src");

    private string ProgramsDirectory => Path.Combine(RepositoryRoot, "debugger", "tests", "programs");

    /// <summary>The source of a test program as it is checked in.</summary>
    internal string ProgramSource(string name) => Path.Combine(ProgramsDirectory, name, name + ".pas");

    /// <summary>Fails, naming what is missing, unless the toolchain with the debug adapter is installed.</summary>
    internal void RequireToolchain()
    {
        Assert.True(Directory.Exists(ToolchainDirectory), $"Toolchain not found: {ToolchainDirectory} (run scripts/bootstrap.ps1).");
        foreach (var tool in (string[])["clang.exe", "lld-link.exe", "lldb-dap.exe"])
        {
            var path = Path.Combine(ToolchainDirectory, tool);
            Assert.True(File.Exists(path), $"Toolchain tool not found: {path} (run scripts/bootstrap.ps1).");
        }

        Assert.True(File.Exists(Path.Combine(RtlDirectory, "System.pas")), $"Runtime library sources not found: {RtlDirectory}.");
    }

    /// <summary>
    /// The program built once per test run: its source copied into a fresh directory and compiled there with the
    /// Pascal compiler, without optimisation and (unless <paramref name="debugInfo"/> is false) with debug information.
    /// </summary>
    internal BuiltProgram Build(string name, bool debugInfo = true)
    {
        lock (_gate)
        {
            var key = name + (debugInfo ? "" : "-nodebug");
            if (!_built.TryGetValue(key, out var program))
                _built.Add(key, program = Compile(name, key, debugInfo));
            return program;
        }
    }

    /// <summary>
    /// The test library and one of its hosts, built once per test run into one fresh directory (the host finds the
    /// library beside itself): the library with debug information, the host with or without.
    /// </summary>
    /// <param name="host">Name of the host's source file in the folder of the host programs.</param>
    /// <param name="hostDebugInfo">False: the host is built without debug information (no type-information file).</param>
    internal (BuiltProgram Host, BuiltProgram Library) BuildLibraryWithHost(string host = "DllHost", bool hostDebugInfo = true)
    {
        lock (_gate)
        {
            var key = host + "+DllLib" + (hostDebugInfo ? "" : "-nodebug");
            if (!_built.TryGetValue(key, out var builtHost))
            {
                RequireToolchain();
                var directory = Path.Combine(_buildRoot, key);
                Directory.CreateDirectory(directory);
                foreach (var checkedIn in (string[])[.. Directory.GetFiles(Path.Combine(ProgramsDirectory, "DllLib"), "*.pas"), Path.Combine(ProgramsDirectory, "DllHost", host + ".pas")])
                    File.Copy(checkedIn, Path.Combine(directory, Path.GetFileName(checkedIn)));
                _built.Add(key + ":library", RunCompiler(directory, "DllLib", ".dll", debugInfo: true));
                _built.Add(key, builtHost = RunCompiler(directory, host, ".exe", hostDebugInfo));
            }

            return (builtHost, _built[key + ":library"]);
        }
    }

    private BuiltProgram Compile(string name, string key, bool debugInfo)
    {
        RequireToolchain();
        var checkedIn = ProgramSource(name);
        Assert.True(File.Exists(checkedIn), $"Test program not found: {checkedIn}.");

        var directory = Path.Combine(_buildRoot, key);
        Directory.CreateDirectory(directory);
        File.Copy(checkedIn, Path.Combine(directory, name + ".pas"));
        return RunCompiler(directory, name, ".exe", debugInfo);
    }

    /// <summary>Compiles <c>name.pas</c> of <paramref name="directory"/> into that directory (import libraries are looked for there too).</summary>
    private BuiltProgram RunCompiler(string directory, string name, string extension, bool debugInfo)
    {
        var source = Path.Combine(directory, name + ".pas");
        Assert.True(File.Exists(source), $"Test program not found: {source}.");
        var exe = Path.Combine(directory, name + extension);

        var (command, prefix) = CompilerCommand();
        var info = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = directory,
        };
        string[] options = debugInfo ? ["-g", "-O0"] : ["-O0"];
        foreach (var argument in (string[])[.. prefix, source, .. options, "-o", exe, "-L", directory, "--diagnostics", "json", "--rtl", RtlDirectory, "--toolchain", ToolchainDirectory])
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("The compiler could not be started: " + command);
        ProcessLeakFixture.Registry.Track(process.Id, "compiler for " + name);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(CompileLimit))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Assert.Fail($"Compiling {name} did not finish within {CompileLimit.TotalMinutes} minutes.");
        }

        var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, $"Compiling {name} failed with exit code {process.ExitCode}:\n{output}");
        Assert.True(File.Exists(exe), $"The compiler reported success but wrote no {exe}:\n{output}");
        return new BuiltProgram(directory, source, exe, Path.ChangeExtension(exe, ".pdi"));
    }

    /// <summary>
    /// The compiler: the built output of the compiler project (same configuration as this test assembly first), else
    /// the project run through the .NET SDK.
    /// </summary>
    private (string Command, string[] Prefix) CompilerCommand()
    {
        var project = Path.Combine(RepositoryRoot, "compiler", "src", "opc");
        var configuration = AppContext.BaseDirectory.Contains(@"\Release\", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
        foreach (var candidate in (string[])[configuration, "Debug", "Release"])
        {
            var exe = Path.Combine(project, "bin", candidate, "net10.0", "opc.exe");
            if (File.Exists(exe))
                return (exe, []);
        }

        Assert.True(Directory.Exists(project), $"Pascal compiler project not found: {project}.");
        return ("dotnet", ["run", "--project", project, "--"]);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreexComp.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("FreexComp.sln not found above " + AppContext.BaseDirectory);
    }

    /// <summary>The processes whose executable is one of this run's built programs: debuggees that are (still) running.</summary>
    private List<Process> RunningBuiltPrograms()
    {
        List<BuiltProgram> programs;
        lock (_gate)
            programs = [.. _built.Values];
        var running = new List<Process>();
        foreach (var program in programs)
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(program.ExePath)))
            {
                var ours = false;
                try
                {
                    ours = !process.HasExited && string.Equals(process.MainModule?.FileName, program.ExePath, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Ended meanwhile, or a process of the same name that is not ours to look at.
                }

                if (ours)
                    running.Add(process);
                else
                    process.Dispose();
            }
        }

        return running;
    }

    /// <summary>
    /// Kills every running process of this run's built programs and returns their descriptions. A process on its way
    /// out gets <paramref name="grace"/> to end by itself first (the wait is on its exit).
    /// </summary>
    internal IReadOnlyList<string> KillRunningBuiltPrograms(TimeSpan grace)
    {
        var leaked = new List<string>();
        foreach (var process in RunningBuiltPrograms())
        {
            using (process)
            {
                try
                {
                    if (process.WaitForExit(grace))
                        continue;
                    leaked.Add($"{process.ProcessName}, process id {process.Id}");
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(TimeSpan.FromSeconds(30));
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Ended meanwhile.
                }
            }
        }

        return leaked;
    }

    public void Dispose()
    {
        // The debuggees were built into this run's own directory: one that is still running was left behind by a test.
        var leaked = KillRunningBuiltPrograms(TimeSpan.Zero);
        try
        {
            if (Directory.Exists(_buildRoot))
                Directory.Delete(_buildRoot, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort (DBG-305).
        }

        Assert.True(leaked.Count == 0, "Debuggees left running by the tests (killed now): " + string.Join(", ", leaked));
    }
}

/// <summary>All tests against the real adapter run in this one collection: no two debuggees at a time (DBG-335).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealAdapterCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Name = "Real debug adapter";
}
