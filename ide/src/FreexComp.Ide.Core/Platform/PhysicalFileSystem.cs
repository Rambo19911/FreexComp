namespace FreexComp.Ide.Core.Platform;

/// <summary><see cref="IFileSystem"/> over <see cref="System.IO"/>.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void WriteAllBytes(string path, byte[] data) => File.WriteAllBytes(path, data);

    public void WriteAllBytesDurable(string path, byte[] data)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(data);
        stream.Flush(flushToDisk: true);
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path) => File.Delete(path);

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Move(sourcePath, destinationPath, overwrite);

    public void ReplaceFile(string sourcePath, string destinationPath) =>
        File.Replace(sourcePath, destinationPath, destinationBackupFileName: null);

    public void OverwriteFile(string path, byte[] data)
    {
        using var stream = new FileStream(path, FileMode.Truncate, FileAccess.Write, FileShare.None);
        stream.Write(data);
        stream.Flush(flushToDisk: true);
    }

    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern) =>
        [.. Directory.EnumerateFiles(directory, searchPattern)];

    public IReadOnlyList<string> EnumerateDirectories(string directory) =>
        [.. Directory.EnumerateDirectories(directory)];

    public DateTime GetLastWriteTimeUtc(string path)
    {
        RequireFile(path);
        return File.GetLastWriteTimeUtc(path);
    }

    public long GetLength(string path) => new FileInfo(path).Length;

    public bool IsReadOnly(string path) =>
        File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly);

    public void SetReadOnly(string path, bool readOnly)
    {
        RequireFile(path);
        var attributes = File.GetAttributes(path);
        File.SetAttributes(path, readOnly ? attributes | FileAttributes.ReadOnly : attributes & ~FileAttributes.ReadOnly);
    }

    public bool CanOpenForWrite(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true; // e.g. a sharing violation: the file is locked, not read-only
        }
    }

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("File not found.", path);
        }
    }
}
