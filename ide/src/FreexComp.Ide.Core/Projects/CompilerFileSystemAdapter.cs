using System.Text;
using FreexComp.Ide.Core.Documents;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Projects;

/// <summary>
/// The IDE's byte-oriented <see cref="IdeFileSystem"/> seen as the compiler's text-oriented
/// <see cref="FreexComp.Compiler.IFileSystem"/> (IDE-ARCH-012, lead decision: the adapter belongs to I-064), so
/// <c>FreexComp.Build</c> and the compiler read and write through the same file system as the IDE.
/// </summary>
public sealed class CompilerFileSystemAdapter(IdeFileSystem fileSystem) : FreexComp.Compiler.IFileSystem
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public bool FileExists(string path) => fileSystem.FileExists(path);

    /// <summary>UTF-8 with the BOM stripped; every failure is an <see cref="IOException"/> (the compiler's contract).</summary>
    public string ReadAllText(string path)
    {
        var bytes = ReadAllBytes(path);
        var start = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        return Utf8.GetString(bytes, start, bytes.Length - start);
    }

    public byte[] ReadAllBytes(string path)
    {
        try
        {
            return fileSystem.ReadAllBytes(path);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new IOException(e.Message, e);
        }
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) =>
        fileSystem.DirectoryExists(directory) ? fileSystem.EnumerateFiles(directory, searchPattern) : [];

    public IEnumerable<string> EnumerateDirectories(string directory) =>
        fileSystem.DirectoryExists(directory) ? fileSystem.EnumerateDirectories(directory) : [];

    public DateTime GetLastWriteTimeUtc(string path) => fileSystem.GetLastWriteTimeUtc(path);

    public long GetLength(string path) => fileSystem.GetLength(path);

    /// <summary>Creates the parent directories, then writes atomically (IDE-P1-081).</summary>
    public void WriteAllBytes(string path, byte[] data)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
        {
            fileSystem.CreateDirectory(directory);
        }

        AtomicFileWriter.Write(fileSystem, path, data);
    }

    /// <summary>A missing file (or folder) is not an error, as in the compiler's physical file system.</summary>
    public void Delete(string path)
    {
        if (fileSystem.FileExists(path))
            fileSystem.DeleteFile(path);
    }

    public void CreateDirectory(string path) => fileSystem.CreateDirectory(path);
}
