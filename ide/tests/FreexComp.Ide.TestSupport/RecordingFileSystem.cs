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

    /// <summary>When set and returning an exception for a recorded call text, that call throws it.</summary>
    public Func<string, Exception?>? FailWith { get; set; }

    public bool FileExists(string path) => Run($"FileExists {path}", () => inner.FileExists(path));

    public bool DirectoryExists(string path) => Run($"DirectoryExists {path}", () => inner.DirectoryExists(path));

    public byte[] ReadAllBytes(string path) => Run($"ReadAllBytes {path}", () => inner.ReadAllBytes(path));

    public void WriteAllBytes(string path, byte[] data) => Run($"WriteAllBytes {path}", () => inner.WriteAllBytes(path, data));

    public void WriteAllBytesDurable(string path, byte[] data) =>
        Run($"WriteAllBytesDurable {path}", () => inner.WriteAllBytesDurable(path, data));

    public void CreateDirectory(string path) => Run($"CreateDirectory {path}", () => inner.CreateDirectory(path));

    public void DeleteFile(string path) => Run($"DeleteFile {path}", () => inner.DeleteFile(path));

    public void DeleteDirectory(string path) => Run($"DeleteDirectory {path}", () => inner.DeleteDirectory(path));

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite) =>
        Run($"MoveFile {sourcePath} -> {destinationPath}", () => inner.MoveFile(sourcePath, destinationPath, overwrite));

    public void ReplaceFile(string sourcePath, string destinationPath) =>
        Run($"ReplaceFile {sourcePath} -> {destinationPath}", () => inner.ReplaceFile(sourcePath, destinationPath));

    public void OverwriteFile(string path, byte[] data) => Run($"OverwriteFile {path}", () => inner.OverwriteFile(path, data));

    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern) =>
        Run($"EnumerateFiles {directory}", () => inner.EnumerateFiles(directory, searchPattern));

    public IReadOnlyList<string> EnumerateDirectories(string directory) =>
        Run($"EnumerateDirectories {directory}", () => inner.EnumerateDirectories(directory));

    public DateTime GetLastWriteTimeUtc(string path) => Run($"GetLastWriteTimeUtc {path}", () => inner.GetLastWriteTimeUtc(path));

    public long GetLength(string path) => Run($"GetLength {path}", () => inner.GetLength(path));

    public bool IsReadOnly(string path) => Run($"IsReadOnly {path}", () => inner.IsReadOnly(path));

    public FileAttributes GetAttributes(string path) => Run($"GetAttributes {path}", () => inner.GetAttributes(path));

    public void SetReadOnly(string path, bool readOnly) => Run($"SetReadOnly {path}", () => inner.SetReadOnly(path, readOnly));

    public bool CanOpenForWrite(string path) => Run($"CanOpenForWrite {path}", () => inner.CanOpenForWrite(path));

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

        if (FailWith?.Invoke(call) is { } exception)
        {
            throw exception;
        }

        return action();
    }
}
