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

    public bool OpenFolder(string directory) => Record(nameof(OpenFolder), directory);

    public bool RevealFile(string filePath) => Record(nameof(RevealFile), filePath);

    public bool MoveToRecycleBin(string path)
    {
        if (Record(nameof(MoveToRecycleBin), path))
        {
            fileSystem?.DeleteFile(path);
        }

        return Succeeds;
    }

    private bool Record(string method, string path)
    {
        Calls.Add(method + " " + path);
        return Succeeds;
    }
}
