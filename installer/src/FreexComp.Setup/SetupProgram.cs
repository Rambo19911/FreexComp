using System.Globalization;

namespace FreexComp.Setup;

/// <summary>
/// The setup program's flow (INS-005): parse the command line, then verify, install (window or <c>/S</c>) or uninstall.
/// <see cref="Execute"/> shows no window, so tests run it in-process.
/// </summary>
public static class SetupProgram
{
    public const string Title = "FreexComp Setup";

    public static int Run(IReadOnlyList<string> args, SetupEnvironment environment)
    {
        SetupOptions options;
        try
        {
            options = CommandLine.Parse(args, environment.Variable);
        }
        catch (SetupException e)
        {
            return Finish(environment, CommandLine.IsSilent(args), new SetupResult(e.Code, e.Message));
        }

        if (!options.Silent && !options.Uninstall && !options.VerifyOnly)
            return SetupWindow.RunWindow(options, environment);
        var result = Execute(options, environment, options.Silent ? null : SetupWindow.Confirm);
        return Finish(environment, options.Silent, result);
    }

    /// <summary>Runs a parsed command line without a window; <paramref name="confirm"/> (null = yes) asks before an uninstall.</summary>
    public static SetupResult Execute(SetupOptions options, SetupEnvironment environment, Func<string, bool>? confirm = null,
        IProgress<InstallProgress>? progress = null, CancellationToken cancel = default)
    {
        try
        {
            if (options.Uninstall)
                return Uninstall(options, environment, confirm);
            if (options.AllUsers && !options.VerifyOnly && !environment.IsElevated)
                return Elevate(options, environment);
            using var package = SetupPackage.Open(environment.ProgramPath);
            if (options.VerifyOnly)
            {
                package.Verify(cancel: cancel);
                return SetupResult.Ok("The installation package is intact.");
            }

            return new Installer(environment).Install(package, options, progress, cancel);
        }
        catch (SetupException e)
        {
            return new SetupResult(e.Code, e.Message);
        }
        catch (OperationCanceledException)
        {
            return new SetupResult(ExitCode.Cancelled, "Setup was cancelled; nothing was installed.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SetupResult(ExitCode.IoError, e.Message);
        }
    }

    /// <summary>The arguments of an elevated rerun: the same choices, silent (the window, if any, shows the result).</summary>
    public static IReadOnlyList<string> Arguments(SetupOptions options, string? target)
    {
        var args = new List<string> { "/S" };
        if (options.Uninstall)
            args.Add("/uninstall");
        if (options.AllUsers)
            args.Add("/allusers");
        if (options.NoShortcuts)
            args.Add("/noshortcuts");
        if (options.NoAssociations)
            args.Add("/noassoc");
        if (target is not null)
            args.Add("/D=" + target);
        if (options.RegistryRoot is { } root)
            args.Add("/regroot=" + root);
        if (options.StartMenuDirectory is { } menu)
            args.Add("/startmenu=" + menu);
        return args;
    }

    private static SetupResult Elevate(SetupOptions options, SetupEnvironment environment)
    {
        var target = options.TargetDirectory ?? new Installer(environment).DefaultTarget(allUsers: true);
        return environment.RunElevated(Arguments(options, target)) is { } code
            ? new SetupResult((ExitCode)code, code == 0 ? $"FreexComp was installed in {target}." : $"Setup ended with exit code {code}; see {environment.LogPath}.")
            : new SetupResult(ExitCode.ElevationDeclined, "Installing for all users needs administrator rights; the request was declined.");
    }

    /// <summary>
    /// INS-011. Run as <c>H\uninstall.exe</c> it copies itself to <c>%TEMP%\FreexComp-uninstall-&lt;n&gt;.exe</c> and
    /// hands over to that copy through a detached <c>cmd.exe</c>, which waits for the copy and then deletes it.
    /// </summary>
    private static SetupResult Uninstall(SetupOptions options, SetupEnvironment environment, Func<string, bool>? confirm)
    {
        var self = Path.GetFullPath(environment.ProgramPath);
        var home = options.TargetDirectory ?? Path.GetDirectoryName(self)!;
        if (options.TargetDirectory is null && Path.GetFileName(self).Equals(InstallRecord.UninstallProgram, StringComparison.OrdinalIgnoreCase))
            return HandOver(self, home, options, environment);

        var record = InstallRecord.Read(home)
                     ?? throw new SetupException(ExitCode.TargetNotEmpty, $"The folder {home} holds no FreexComp installation.");
        if (record.Scope == "machine" && !environment.IsElevated)
        {
            return environment.RunElevated(Arguments(options, home)) is { } code
                ? new SetupResult((ExitCode)code, code == 0 ? $"FreexComp was removed from {home}." : $"Uninstall ended with exit code {code}; see {environment.LogPath}.")
                : new SetupResult(ExitCode.ElevationDeclined, "Removing an installation for all users needs administrator rights; the request was declined.");
        }

        if (confirm is not null && !confirm($"Remove FreexComp {record.Version} from {home}?\n\nYour projects and settings are kept."))
            return new SetupResult(ExitCode.Cancelled, "Uninstall was cancelled; nothing was removed.");
        Installer.Remove(record, home, removeHome: true);
        return SetupResult.Ok($"FreexComp was removed from {home}.");
    }

    private static SetupResult HandOver(string self, string home, SetupOptions options, SetupEnvironment environment)
    {
        var temp = environment.TempDirectory;
        Directory.CreateDirectory(temp);
        string copy;
        for (var n = 1; ; n++)
        {
            copy = Path.Combine(temp, $"FreexComp-uninstall-{n.ToString(CultureInfo.InvariantCulture)}.exe");
            try
            {
                File.Copy(self, copy, overwrite: false);
                break;
            }
            catch (IOException) when (File.Exists(copy) && n < 100)
            {
                // a copy of an earlier run is still there (or running): take the next number
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw SetupException.Io(copy, e);
            }
        }

        var args = new List<string> { "/uninstall", "/D=" + home };
        if (options.Silent)
            args.Add("/S");
        var cmd = Path.Combine(environment.Variable("SystemRoot") ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cmd.exe");
        environment.StartDetached(cmd, $"/d /s /c \"start \"\" /wait \"{copy}\" {SetupEnvironment.Quote(args)} & del /f /q \"{copy}\"\"");
        return new SetupResult(ExitCode.Ok, $"The uninstall continues in {copy}.") { Show = false };
    }

    private static int Finish(SetupEnvironment environment, bool silent, SetupResult result)
    {
        Log(environment, result);
        if (!silent && result.Show)
            SetupWindow.Report(result);
        return (int)result.Code;
    }

    /// <summary>Appends the result to <c>%TEMP%\FreexComp-setup.log</c> (INS-005); a log that cannot be written is skipped.</summary>
    public static void Log(SetupEnvironment environment, SetupResult result)
    {
        try
        {
            Directory.CreateDirectory(environment.TempDirectory);
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{environment.ProgramPath}] exit {(int)result.Code}: {result.Message}{Environment.NewLine}");
            File.AppendAllText(environment.LogPath, line);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // logging never changes the result
        }
    }
}
