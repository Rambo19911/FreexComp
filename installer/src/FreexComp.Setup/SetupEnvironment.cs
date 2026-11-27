using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace FreexComp.Setup;

/// <summary>
/// What the setup program reads from the machine: its own file, environment variables, elevation and process starts.
/// The real one is <see cref="Current"/>; tests replace parts of it (never the registry or folders outside their scratch
/// folder, which they reach only through the test switches).
/// </summary>
public sealed class SetupEnvironment
{
    /// <summary>The running program: the package (setup exe with payload) or <c>uninstall.exe</c> (no payload).</summary>
    public required string ProgramPath { get; init; }

    public required Func<string, string?> Variable { get; init; }

    /// <summary>The product version this setup installs (INS-009 <c>DisplayVersion</c>, INS-010).</summary>
    public required string Version { get; init; }

    public bool IsElevated { get; init; }

    /// <summary>Runs the program again elevated with these arguments and returns its exit code; null when the user declined.</summary>
    public Func<IReadOnlyList<string>, int?> RunElevated { get; init; } = _ => null;

    /// <summary>Starts a detached hidden process (the cleanup of the temporary uninstall copy, INS-011).</summary>
    public Action<string, string> StartDetached { get; init; } = (_, _) => { };

    /// <summary>Whether shell notifications (association change) are sent; off under a test registry root.</summary>
    public bool NotifyShell { get; init; } = true;

    public string TempDirectory => Path.GetFullPath(Variable("TEMP") ?? Variable("TMP") ?? Path.GetTempPath());

    public string LogPath => Path.Combine(TempDirectory, "FreexComp-setup.log");

    public string Folder(string variable) =>
        Variable(variable) is { Length: > 0 } value ? value : throw new SetupException(ExitCode.IoError, $"The environment variable {variable} is not set.");

    public static SetupEnvironment Current()
    {
        var program = Environment.ProcessPath ?? throw new InvalidOperationException("The program path is unknown.");
        var version = typeof(SetupEnvironment).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        using var identity = WindowsIdentity.GetCurrent();
        return new SetupEnvironment
        {
            ProgramPath = program,
            Variable = Environment.GetEnvironmentVariable,
            Version = version.Split('+')[0],
            IsElevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
            RunElevated = args => RunElevatedProcess(program, args),
            StartDetached = (file, arguments) =>
            {
                using var _ = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true });
            },
        };
    }

    /// <summary>A Windows command line of <paramref name="args"/> (each quoted when it holds a space or a quote).</summary>
    public static string Quote(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));

    private static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
            return arg;
        var text = new System.Text.StringBuilder("\"");
        var slashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }

            text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
            slashes = 0;
        }

        return text.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static int? RunElevatedProcess(string program, IReadOnlyList<string> args)
    {
        var start = new ProcessStartInfo(program, Quote(args)) { UseShellExecute = true, Verb = "runas" };
        try
        {
            using var process = Process.Start(start)!;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)   // ERROR_CANCELLED: the consent prompt was declined
        {
            return null;
        }
    }
}
