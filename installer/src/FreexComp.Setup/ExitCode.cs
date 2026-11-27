namespace FreexComp.Setup;

/// <summary>Process exit codes of the setup program (INS-005).</summary>
public enum ExitCode
{
    Ok = 0,
    Cancelled = 1,
    BadArguments = 2,
    DamagedPackage = 3,
    TargetNotEmpty = 4,
    ElevationDeclined = 5,
    IoError = 6,
}

/// <summary>The outcome of one setup run: the exit code and the line shown to the user (window, message box or log).</summary>
public sealed record SetupResult(ExitCode Code, string Message)
{
    /// <summary>False when nothing is shown to the user (the hand-over of <c>uninstall.exe</c> to its temporary copy).</summary>
    public bool Show { get; init; } = true;

    public static SetupResult Ok(string message) => new(ExitCode.Ok, message);
}

/// <summary>A failure that ends the run with its exit code and message.</summary>
public sealed class SetupException(ExitCode code, string message, Exception? inner = null) : Exception(message, inner)
{
    public ExitCode Code { get; } = code;

    /// <summary>INS-003 / INS-004: the one message of a damaged package, naming the payload path (or the package file).</summary>
    public static SetupException Damaged(string path, Exception? inner = null) =>
        new(ExitCode.DamagedPackage, $"The installation package is damaged ({path}).", inner);

    public static SetupException Io(string path, Exception inner) =>
        new(ExitCode.IoError, $"Cannot write {path}: {inner.Message}", inner);
}
