using System.Reflection;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Composition;

/// <summary>
/// The parsed command line (phase1-shell.md §2, IDE-ARCH-015 step 1):
/// <c>FreexComp.exe [&lt;path&gt; …] [--new-window] [--user-dir &lt;dir&gt;] [--safe] [--version] [--help]</c>.
/// Options are matched case-insensitively. <see cref="Arguments"/> keeps every argument except the <c>--user-dir</c>
/// pair, in order.
/// </summary>
public sealed record StartupOptions(string? UserDir, IReadOnlyList<string> Arguments)
{
    public const string UserDirOption = "--user-dir";
    public const string SafeOption = "--safe";
    public const string VersionOption = "--version";
    public const string HelpOption = "--help";
    public const string NewWindowOption = "--new-window";

    /// <summary>IDE-P1-010: the path arguments, made absolute against the working directory, in order.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>IDE-P1-012.</summary>
    public bool Safe { get; init; }

    /// <summary>IDE-P1-013.</summary>
    public bool ShowVersion { get; init; }

    /// <summary>IDE-P1-013.</summary>
    public bool ShowHelp { get; init; }

    /// <summary>IDE-P1-013: the first unknown option (or <c>--user-dir</c> without a value); the IDE does not start.</summary>
    public string? UnknownOption { get; init; }

    /// <param name="workingDirectory">Relative paths resolve against it (IDE-P1-010); null = the process's current directory.</param>
    public static StartupOptions Parse(IReadOnlyList<string> args, string? workingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var baseDirectory = workingDirectory ?? Environment.CurrentDirectory;
        string? userDir = null;
        string? unknown = null;
        bool safe = false, version = false, help = false;
        var rest = new List<string>();
        var paths = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (Is(arg, UserDirOption) && i + 1 < args.Count)
            {
                userDir = args[++i]; // IDE-P1-011: the last occurrence wins
                continue;
            }

            rest.Add(arg);
            if (Is(arg, SafeOption))
                safe = true;
            else if (Is(arg, VersionOption))
                version = true;
            else if (Is(arg, HelpOption))
                help = true;
            else if (Is(arg, NewWindowOption))
                continue; // IDE-P1-014: accepted and ignored (reserved)
            else if (arg.StartsWith("--", StringComparison.Ordinal))
                unknown ??= arg;
            else if (arg.Length > 0)
                paths.Add(Path.GetFullPath(arg, baseDirectory));
        }

        return new StartupOptions(userDir, rest)
        {
            Paths = paths,
            Safe = safe,
            ShowVersion = version,
            ShowHelp = help,
            UnknownOption = unknown,
        };
    }

    /// <summary>
    /// IDE-P1-013, before any window exists: an unknown option prints the usage to <paramref name="error"/> (exit code 1),
    /// <c>--help</c> prints it to <paramref name="output"/>, <c>--version</c> prints <c>FreexComp &lt;version&gt;</c> (both
    /// exit code 0). Null: start the IDE.
    /// </summary>
    public int? HandleInformationOptions(TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (UnknownOption is not null)
        {
            error.WriteLine(Strings.App_Usage);
            return 1;
        }

        if (ShowHelp)
        {
            output.WriteLine(Strings.App_Usage);
            return 0;
        }

        if (ShowVersion)
        {
            output.WriteLine($"{Strings.App_ProductName} {Version}");
            return 0;
        }

        return null;
    }

    /// <summary>The product version: the informational version of this assembly.</summary>
    public static string Version =>
        typeof(StartupOptions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(StartupOptions).Assembly.GetName().Version?.ToString() ?? "";

    private static bool Is(string arg, string option) => string.Equals(arg, option, StringComparison.OrdinalIgnoreCase);
}
