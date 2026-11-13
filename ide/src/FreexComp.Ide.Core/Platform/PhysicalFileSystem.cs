namespace FreexComp.Ide.Core.Platform;

/// <summary><see cref="IFileSystem"/> over <see cref="System.IO"/>.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void WriteAllBytes(string path, byte[] data) => File.WriteAllBytes(path, data);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path) => File.Delete(path);

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Move(sourcePath, destinationPath, overwrite);

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

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("File not found.", path);
        }
    }
}
