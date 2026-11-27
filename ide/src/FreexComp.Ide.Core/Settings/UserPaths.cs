namespace FreexComp.Ide.Core.Settings;

/// <summary>Computes the user directories once, at process start (IDE-SET-001…003, IDE-P1-011, INS-021).</summary>
public sealed class UserPaths : IUserPaths
{
    public const string UserDirVariable = "FREEXCOMP_USER_DIR";

    /// <summary>INS-021: the file next to the IDE executable that makes the installation portable. Only its presence counts.</summary>
    public const string PortableMarkerFile = "portable.json";

    /// <summary>INS-021: the user directory of a portable installation, next to the IDE executable.</summary>
    public const string PortableFolder = "userdata";

    private const string ProductFolder = "FreexComp";

    private readonly Lazy<string> _documents;

    private UserPaths(string roaming, string local, Func<string> documents)
    {
        RoamingDirectory = roaming;
        LocalDirectory = local;
        LogDirectory = Path.Combine(local, "logs");
        _documents = new Lazy<string>(documents);
    }

    public string RoamingDirectory { get; }
    public string LocalDirectory { get; }
    public string LogDirectory { get; }
    public string DocumentsDirectory => _documents.Value;

    /// <summary>Production entry point: reads the process environment, the OS user folders and the executable's directory.</summary>
    public static UserPaths FromProcess(string? userDirArgument) =>
        Create(userDirArgument, Environment.GetEnvironmentVariable, f => Environment.GetFolderPath(f), AppContext.BaseDirectory, File.Exists);

    /// <summary>Production entry point of <see cref="PortableRoot"/>.</summary>
    public static string? PortableRootFromProcess(string? userDirArgument) =>
        PortableRoot(userDirArgument, Environment.GetEnvironmentVariable, AppContext.BaseDirectory, File.Exists);

    /// <summary>
    /// INS-021: <c>&lt;executable directory&gt;\userdata</c> when <see cref="PortableMarkerFile"/> lies next to the IDE
    /// executable and neither <c>--user-dir</c> nor <see cref="UserDirVariable"/> names a user directory (then the marker
    /// is not looked for); null otherwise. The marker is never opened, so its content cannot stop the start-up.
    /// </summary>
    public static string? PortableRoot(
        string? userDirArgument,
        Func<string, string?> getEnvironmentVariable,
        string? executableDirectory,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(fileExists);
        return string.IsNullOrEmpty(userDirArgument) && string.IsNullOrEmpty(getEnvironmentVariable(UserDirVariable))
            ? MarkerRoot(executableDirectory, fileExists)
            : null;
    }

    private static string? MarkerRoot(string? executableDirectory, Func<string, bool> fileExists)
    {
        if (string.IsNullOrEmpty(executableDirectory))
            return null;

        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(executableDirectory));
        return fileExists(Path.Combine(directory, PortableMarkerFile)) ? Path.Combine(directory, PortableFolder) : null;
    }

    /// <summary>
    /// <paramref name="userDirArgument"/> (<c>--user-dir</c>) wins over <see cref="UserDirVariable"/>, which wins over the
    /// portable marker next to the executable (<see cref="PortableRoot"/>; without <paramref name="executableDirectory"/>
    /// there is none). When any of them gives the user directory, <paramref name="getFolder"/> is not called at all
    /// (the real user folders are not touched).
    /// </summary>
    public static UserPaths Create(
        string? userDirArgument,
        Func<string, string?> getEnvironmentVariable,
        Func<Environment.SpecialFolder, string> getFolder,
        string? executableDirectory = null,
        Func<string, bool>? fileExists = null)
    {
        var overrideDir = !string.IsNullOrEmpty(userDirArgument)
            ? userDirArgument
            : getEnvironmentVariable(UserDirVariable);
        if (string.IsNullOrEmpty(overrideDir) && fileExists is not null)
            overrideDir = MarkerRoot(executableDirectory, fileExists);

        if (!string.IsNullOrEmpty(overrideDir))
        {
            var root = Path.GetFullPath(overrideDir);
            return new UserPaths(Path.Combine(root, "roaming"), Path.Combine(root, "local"), () => Path.Combine(root, "documents"));
        }

        return new UserPaths(
            Path.Combine(getFolder(Environment.SpecialFolder.ApplicationData), ProductFolder),
            Path.Combine(getFolder(Environment.SpecialFolder.LocalApplicationData), ProductFolder),
            () => getFolder(Environment.SpecialFolder.MyDocuments));
    }
}
