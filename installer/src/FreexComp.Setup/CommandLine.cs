namespace FreexComp.Setup;

/// <summary>The parsed command line of the setup program (INS-005).</summary>
public sealed record SetupOptions
{
    /// <summary><c>/S</c>: no window; the result is the exit code and <c>%TEMP%\FreexComp-setup.log</c>.</summary>
    public bool Silent { get; init; }

    /// <summary><c>/D=&lt;dir&gt;</c>: the install folder H (absolute); null = the default of the scope.</summary>
    public string? TargetDirectory { get; init; }

    /// <summary><c>/allusers</c>: per-machine install (elevates).</summary>
    public bool AllUsers { get; init; }

    public bool NoShortcuts { get; init; }

    public bool NoAssociations { get; init; }

    /// <summary><c>/uninstall</c> (from <c>H\uninstall.exe</c>).</summary>
    public bool Uninstall { get; init; }

    /// <summary><c>/verify</c>: the INS-003 check only (exit 0 or 3).</summary>
    public bool VerifyOnly { get; init; }

    /// <summary>Test only, <c>/regroot=&lt;HKCU subkey&gt;</c>: every registry write goes under that key.</summary>
    public string? RegistryRoot { get; init; }

    /// <summary>Test only, <c>/startmenu=&lt;dir&gt;</c>: the folder of the Start-menu shortcut.</summary>
    public string? StartMenuDirectory { get; init; }
}

/// <summary>Parses the setup command line (INS-005). Switches are case-insensitive; a value with spaces is quoted whole (<c>"/D=C:\My dir"</c>).</summary>
public static class CommandLine
{
    /// <summary>The variable that must be <c>1</c> for the test-only switches (<c>/regroot=</c>, <c>/startmenu=</c>).</summary>
    public const string TestVariable = "FREEXCOMP_SETUP_TEST";

    public static SetupOptions Parse(IReadOnlyList<string> args, Func<string, string?> variable)
    {
        var options = new SetupOptions();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args)
        {
            var equals = arg.IndexOf('=');
            var name = equals < 0 ? arg : arg[..(equals + 1)];
            var value = equals < 0 ? null : arg[(equals + 1)..];
            if (!seen.Add(name))
                throw Bad($"The switch {name} is given twice.");
            options = name.ToLowerInvariant() switch
            {
                "/s" => options with { Silent = true },
                "/allusers" => options with { AllUsers = true },
                "/noshortcuts" => options with { NoShortcuts = true },
                "/noassoc" => options with { NoAssociations = true },
                "/uninstall" => options with { Uninstall = true },
                "/verify" => options with { VerifyOnly = true },
                "/d=" => options with { TargetDirectory = AbsoluteDirectory(name, value!) },
                "/regroot=" => options with { RegistryRoot = TestSwitch(name, RegistryKeyPath(value!), variable) },
                "/startmenu=" => options with { StartMenuDirectory = TestSwitch(name, AbsoluteDirectory(name, value!), variable) },
                _ => throw Bad($"Unknown switch: {arg}"),
            };
        }

        if (options.Uninstall && options.VerifyOnly)
            throw Bad("/uninstall and /verify cannot be combined.");
        return options;
    }

    /// <summary>True when the arguments ask for a silent run (decides how a parse error is reported).</summary>
    public static bool IsSilent(IEnumerable<string> args) => args.Any(a => a.Equals("/S", StringComparison.OrdinalIgnoreCase));

    private static string TestSwitch(string name, string value, Func<string, string?> variable) =>
        variable(TestVariable) == "1" ? value : throw Bad($"{name} is a test switch; it needs {TestVariable}=1.");

    private static string AbsoluteDirectory(string name, string value)
    {
        var path = value.Trim();
        if (path.Length == 0 || !Path.IsPathFullyQualified(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw Bad($"{name} needs an absolute folder path.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string RegistryKeyPath(string value)
    {
        var parts = value.Split('\\');
        if (value.Length == 0 || parts.Any(p => p.Length == 0 || p.Trim() != p || p is "." or ".."))
            throw Bad("/regroot= needs a relative HKCU key path such as Software\\Example.");
        return value;
    }

    private static SetupException Bad(string message) => new(ExitCode.BadArguments, message);
}
