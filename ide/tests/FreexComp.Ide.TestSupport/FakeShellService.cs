using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// <see cref="IShellService"/> that records calls as <c>"&lt;Method&gt; &lt;path&gt;"</c>. A successful
/// <see cref="MoveToRecycleBin"/> deletes the file from the given file system.
/// </summary>
public sealed class FakeShellService(IFileSystem? fileSystem = null) : IShellService
{
    public List<string> Calls { get; } = [];

    /// <summary>Result returned by every method.</summary>
    public bool Succeeds { get; set; } = true;

    /// <summary>Paths for which <see cref="MoveToRecycleBin"/> fails even when <see cref="Succeeds"/> is true.</summary>
    public Func<string, bool>? RecycleFailsFor { get; set; }

    public bool OpenFolder(string directory) => Record(nameof(OpenFolder), directory);

    public bool RevealFile(string filePath) => Record(nameof(RevealFile), filePath);

    /// <summary>The reason a failed <see cref="MoveToRecycleBin(string, out string)"/> gives.</summary>
    public const string RecycleFailure = "Injected recycle failure";

    public bool MoveToRecycleBin(string path) => MoveToRecycleBin(path, out _);

    public bool MoveToRecycleBin(string path, out string reason)
    {
        var succeeded = Record(nameof(MoveToRecycleBin), path) && RecycleFailsFor?.Invoke(path) != true;
        if (succeeded)
        {
            fileSystem?.DeleteFile(path);
        }

        reason = succeeded ? "" : RecycleFailure;
        return succeeded;
    }

    /// <summary>The reason a failed <see cref="OpenConsole(string, out string)"/> gives.</summary>
    public const string ConsoleFailure = "Injected console failure";

    public bool OpenConsole(string directory, out string reason)
    {
        var succeeded = Record(nameof(OpenConsole), directory);
        reason = succeeded ? "" : ConsoleFailure;
        return succeeded;
    }

    private bool Record(string method, string path)
    {
        Calls.Add(method + " " + path);
        return Succeeds;
    }
}
