using System.Security.Cryptography;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Documents;

/// <summary>
/// IDE-P1-081: writes <c>&lt;name&gt;.~tmp&lt;6 hex&gt;</c> next to the target and flushes it to the disk, then replaces the target (or moves the
/// temp file there when there is no target). The temp file is deleted on any failure. When the folder is not writable
/// but the file is (access denied), the existing file is overwritten in place. Replace and overwrite keep the target's
/// attributes and creation time.
/// </summary>
internal static class AtomicFileWriter
{
    public static void Write(IFileSystem fileSystem, string path, byte[] data)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("Not a file path.", nameof(path));
        var temp = Path.Combine(directory, Path.GetFileName(path) + ".~tmp" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3)));
        try
        {
            fileSystem.WriteAllBytesDurable(temp, data);
            if (fileSystem.FileExists(path))
                fileSystem.ReplaceFile(temp, path);
            else
                fileSystem.MoveFile(temp, path, overwrite: false);
        }
        catch (UnauthorizedAccessException) when (fileSystem.FileExists(path) && !fileSystem.IsReadOnly(path))
        {
            DeleteQuietly(fileSystem, temp);
            fileSystem.OverwriteFile(path, data);
        }
        catch
        {
            DeleteQuietly(fileSystem, temp);
            throw;
        }
    }

    private static void DeleteQuietly(IFileSystem fileSystem, string path)
    {
        try
        {
            fileSystem.DeleteFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The original failure is the one reported.
        }
    }
}
