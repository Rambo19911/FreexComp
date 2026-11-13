namespace FreexComp.Ide.Core.Settings;

/// <summary>Computes the user directories once, at process start (IDE-SET-001…003, IDE-P1-011).</summary>
public sealed class UserPaths : IUserPaths
{
    public const string UserDirVariable = "FREEXCOMP_USER_DIR";
    private const string ProductFolder = "FreexComp";

    private UserPaths(string roaming, string local)
    {
        RoamingDirectory = roaming;
        LocalDirectory = local;
        LogDirectory = Path.Combine(local, "logs");
    }

    public string RoamingDirectory { get; }
    public string LocalDirectory { get; }
    public string LogDirectory { get; }

    /// <summary>Production entry point: reads the process environment and the OS user folders.</summary>
    public static UserPaths FromProcess(string? userDirArgument) =>
        Create(userDirArgument, Environment.GetEnvironmentVariable, f => Environment.GetFolderPath(f));

    /// <summary>
    /// <paramref name="userDirArgument"/> (<c>--user-dir</c>) wins over <see cref="UserDirVariable"/>; when either is
    /// non-empty, <paramref name="getFolder"/> is not called at all (the real user folders are not touched).
    /// </summary>
    public static UserPaths Create(
        string? userDirArgument,
        Func<string, string?> getEnvironmentVariable,
        Func<Environment.SpecialFolder, string> getFolder)
    {
        var overrideDir = !string.IsNullOrEmpty(userDirArgument)
            ? userDirArgument
            : getEnvironmentVariable(UserDirVariable);

        if (!string.IsNullOrEmpty(overrideDir))
        {
            var root = Path.GetFullPath(overrideDir);
            return new UserPaths(Path.Combine(root, "roaming"), Path.Combine(root, "local"));
        }

        return new UserPaths(
            Path.Combine(getFolder(Environment.SpecialFolder.ApplicationData), ProductFolder),
            Path.Combine(getFolder(Environment.SpecialFolder.LocalApplicationData), ProductFolder));
    }
}
