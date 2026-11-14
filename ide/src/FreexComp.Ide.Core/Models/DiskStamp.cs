using System.Security.Cryptography;

namespace FreexComp.Ide.Core.Models;

/// <summary>
/// What a document knows about its file on disk since the last load, save or reload (IDE-P1-110, IDE-P1-112): the
/// last-write time and length that the external-change check compares, and a hash of the content so that a rewrite
/// with identical bytes is recognized.
/// </summary>
public sealed record DiskStamp(DateTime LastWriteUtc, long Length, string Hash)
{
    /// <summary>The stamp of <paramref name="bytes"/>, read or written at <paramref name="lastWriteUtc"/>.</summary>
    public static DiskStamp Of(DateTime lastWriteUtc, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return new DiskStamp(lastWriteUtc, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>True when the file still has the recorded time and length (nothing to read).</summary>
    public bool Matches(DateTime lastWriteUtc, long length) => LastWriteUtc == lastWriteUtc && Length == length;
}
