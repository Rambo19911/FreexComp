using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// Decorator that records every call as <c>"Method path[ -&gt; path2]"</c> and can make chosen operations fail.
/// Thread-safe as long as the inner file system is.
/// </summary>
public sealed class RecordingFileSystem(IFileSystem inner) : IFileSystem
{
    private readonly object _gate = new();
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>When set and returning true for a recorded call text, that call throws <see cref="IOException"/>.</summary>
    public Func<string, bool>? FailWhen { get; set; }

    public bool FileExists(string path) => Run($"FileExists {path}", () => inner.FileExists(path));

    public bool DirectoryExists(string path) => Run($"DirectoryExists {path}", () => inner.DirectoryExists(path));

    public byte[] ReadAllBytes(string path) => Run($"ReadAllBytes {path}", () => inner.ReadAllBytes(path));

    public void WriteAllBytes(string path, byte[] data) => Run($"WriteAllBytes {path}", () => inner.WriteAllBytes(path, data));

    public void CreateDirectory(string path) => Run($"CreateDirectory {path}", () => inner.CreateDirectory(path));

    public void DeleteFile(string path) => Run($"DeleteFile {path}", () => inner.DeleteFile(path));

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite) =>
        Run($"MoveFile {sourcePath} -> {destinationPath}", () => inner.MoveFile(sourcePath, destinationPath, overwrite));

    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern) =>
        Run($"EnumerateFiles {directory}", () => inner.EnumerateFiles(directory, searchPattern));

    public IReadOnlyList<string> EnumerateDirectories(string directory) =>
        Run($"EnumerateDirectories {directory}", () => inner.EnumerateDirectories(directory));

    public DateTime GetLastWriteTimeUtc(string path) => Run($"GetLastWriteTimeUtc {path}", () => inner.GetLastWriteTimeUtc(path));

    public long GetLength(string path) => Run($"GetLength {path}", () => inner.GetLength(path));

    public bool IsReadOnly(string path) => Run($"IsReadOnly {path}", () => inner.IsReadOnly(path));

    private void Run(string call, Action action) => Run(call, () =>
    {
        action();
        return 0;
    });

    private T Run<T>(string call, Func<T> action)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }

        if (FailWhen?.Invoke(call) == true)
        {
            throw new IOException("Injected failure: " + call);
        }

        return action();
    }
}
