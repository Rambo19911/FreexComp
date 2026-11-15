namespace FreexComp.Ide.Core.Platform;

/// <summary>
/// Disk access of the IDE (IDE-ARCH-012, IDE-ARCH-023). Paths are absolute; comparison is case-insensitive.
/// Failures surface as the <see cref="System.IO"/> exceptions named below; services turn them into results
/// (IDE-ARCH-092). Encoding decisions are made by the caller: this interface moves bytes only.
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    byte[] ReadAllBytes(string path);

    /// <summary>Creates or truncates the file.</summary>
    /// <exception cref="DirectoryNotFoundException">The parent directory does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The file is read-only.</exception>
    void WriteAllBytes(string path, byte[] data);

    /// <summary>
    /// <see cref="WriteAllBytes"/> that returns only after the data is flushed to the disk (temp file of an atomic
    /// save, IDE-P1-081).
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The parent directory does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The file is read-only.</exception>
    void WriteAllBytesDurable(string path, byte[] data);

    /// <summary>Creates the directory and every missing parent; no-op when it exists.</summary>
    void CreateDirectory(string path);

    /// <summary>No-op when the file does not exist.</summary>
    void DeleteFile(string path);

    /// <summary>Deletes an empty directory; no-op when it does not exist.</summary>
    /// <exception cref="IOException">The directory is not empty.</exception>
    void DeleteDirectory(string path);

    /// <exception cref="FileNotFoundException">The source does not exist.</exception>
    /// <exception cref="IOException">The destination exists and <paramref name="overwrite"/> is false.</exception>
    void MoveFile(string sourcePath, string destinationPath, bool overwrite);

    /// <summary>
    /// Replaces the content of the existing <paramref name="destinationPath"/> by the file <paramref name="sourcePath"/>
    /// (which is gone afterwards), keeping the destination's attributes and creation time; no backup.
    /// </summary>
    /// <exception cref="FileNotFoundException">The source or the destination does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The destination is read-only or its directory is not writable.</exception>
    void ReplaceFile(string sourcePath, string destinationPath);

    /// <summary>
    /// Overwrites the existing file in place (truncate, then write), keeping its attributes and creation time; returns
    /// after the data is flushed to the disk.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The file is read-only.</exception>
    void OverwriteFile(string path, byte[] data);

    /// <summary>Full paths of the files directly in <paramref name="directory"/> matching a <c>*</c>/<c>?</c> pattern.</summary>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern);

    /// <summary>Full paths of the directories directly in <paramref name="directory"/>.</summary>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    IReadOnlyList<string> EnumerateDirectories(string directory);

    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    DateTime GetLastWriteTimeUtc(string path);

    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    long GetLength(string path);

    /// <summary>True when the file exists and carries the read-only attribute.</summary>
    bool IsReadOnly(string path);

    /// <summary>
    /// The attributes of an existing file (Find in Files skips hidden and system files, IDE-ED-120). The default knows only
    /// the read-only flag; the physical file system returns the real attributes.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file does not exist (physical file system).</exception>
    FileAttributes GetAttributes(string path) => IsReadOnly(path) ? FileAttributes.ReadOnly : FileAttributes.Normal;

    /// <summary>Sets or clears the read-only attribute of an existing file.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The attributes may not be changed.</exception>
    void SetReadOnly(string path, bool readOnly);

    /// <summary>
    /// The open-for-write probe of IDE-P1-040: false when the file does not exist or opening it for writing is denied
    /// (read-only attribute or access rights). Nothing is written; a file locked by another process counts as writable.
    /// </summary>
    bool CanOpenForWrite(string path);
}
