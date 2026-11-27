using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FreexComp.Setup;

/// <summary>
/// The setup package: <c>&lt;setup program&gt;&lt;zip&gt;&lt;trailer&gt;</c> (INS-004). The trailer is the 8-byte magic
/// <c>FXCSETUP</c>, the 8-byte little-endian offset of the zip and the SHA-256 of the manifest <c>payload.sha256</c>
/// (INS-003), which lists every other payload file.
/// </summary>
public static class PackageFormat
{
    public const string ManifestName = "payload.sha256";
    public const int TrailerLength = 8 + 8 + 32;
    public static ReadOnlySpan<byte> Magic => "FXCSETUP"u8;

    public static byte[] Trailer(long zipOffset, byte[] manifestHash)
    {
        if (manifestHash.Length != 32)
            throw new ArgumentException("A SHA-256 hash has 32 bytes.", nameof(manifestHash));
        var trailer = new byte[TrailerLength];
        Magic.CopyTo(trailer);
        BinaryPrimitives.WriteInt64LittleEndian(trailer.AsSpan(8), zipOffset);
        manifestHash.CopyTo(trailer, 16);
        return trailer;
    }

    /// <summary>The zip offset and manifest hash of a trailer, or null when it is not one (wrong magic).</summary>
    public static (long ZipOffset, byte[] ManifestHash)? ReadTrailer(ReadOnlySpan<byte> trailer)
    {
        if (trailer.Length != TrailerLength || !trailer[..8].SequenceEqual(Magic))
            return null;
        return (BinaryPrimitives.ReadInt64LittleEndian(trailer[8..16]), trailer[16..].ToArray());
    }

    /// <summary>Manifest text (INS-003): <c>&lt;lowercase sha256&gt;  &lt;path with \&gt;</c>, sorted ordinal-ignore-case, LF.</summary>
    public static string FormatManifest(IEnumerable<ManifestEntry> entries)
    {
        var text = new StringBuilder();
        foreach (var entry in entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
            text.Append(entry.Hash).Append("  ").Append(entry.Path).Append('\n');
        return text.ToString();
    }

    /// <summary>The entries of a manifest, or null when a line is malformed, a path is unsafe or listed twice.</summary>
    public static IReadOnlyList<ManifestEntry>? ParseManifest(ReadOnlySpan<byte> bytes)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        var entries = new List<ManifestEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0)
                continue;
            if (line.Length < 67 || line[64] != ' ' || line[65] != ' ' || !line[..64].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                return null;
            var path = line[66..];
            if (!IsSafeRelativePath(path) || path.Equals(ManifestName, StringComparison.OrdinalIgnoreCase) || !seen.Add(path))
                return null;
            entries.Add(new ManifestEntry(line[..64], path));
        }

        return entries;
    }

    /// <summary>A relative path with <c>\</c> separators that stays inside its root (no drive, no rooted path, no <c>..</c>).</summary>
    public static bool IsSafeRelativePath(string path) =>
        path.Length > 0 && !path.Contains('/') && !path.Contains(':') && !path.StartsWith('\\')
        && path.Split('\\').All(part => part.Length > 0 && part != "." && part != ".." && part.Trim().Length == part.Length
                                        && part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);

    /// <summary>Zip entry name (<c>/</c>) of a manifest path (<c>\</c>).</summary>
    public static string EntryName(string path) => path.Replace('\\', '/');

    public static string HashHex(byte[] hash) => Convert.ToHexStringLower(hash);

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return HashHex(SHA256.HashData(stream));
    }
}

/// <summary>One manifest line: the lowercase SHA-256 of a payload file and its relative path (<c>\</c> separated).</summary>
public sealed record ManifestEntry(string Hash, string Path);
