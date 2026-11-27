using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FreexComp.Setup;

/// <summary>
/// Builds setup packages from a staging folder <c>H</c> (INS-001, INS-003, INS-004); used by
/// <c>scripts/build-installer.ps1</c> (through the SetupPack tool) and by the tests' fake packages.
/// Same inputs give byte-identical output: entries sorted ordinal-ignore-case, fixed time stamps, no extra fields.
/// </summary>
public static class PackageWriter
{
    /// <summary>The time stamp of every zip entry (INS-001).</summary>
    public static readonly DateTimeOffset EntryTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The payload files of <paramref name="payload"/> as manifest paths (<c>\</c>), the manifest itself excluded.</summary>
    public static IReadOnlyList<string> PayloadFiles(string payload)
    {
        var root = Path.GetFullPath(payload);
        return [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f))
            .Where(p => !p.Equals(PackageFormat.ManifestName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Writes <c>payload.sha256</c> into <paramref name="payload"/> (INS-003), replacing an old one.</summary>
    public static void WriteManifest(string payload)
    {
        var entries = new List<ManifestEntry>();
        foreach (var path in PayloadFiles(payload))
        {
            if (!PackageFormat.IsSafeRelativePath(path))
                throw new InvalidOperationException($"The payload path '{path}' cannot be installed.");
            entries.Add(new ManifestEntry(PackageFormat.HashFile(Path.Combine(payload, path)), path));
        }

        File.WriteAllText(Path.Combine(payload, PackageFormat.ManifestName), PackageFormat.FormatManifest(entries), new UTF8Encoding(false));
    }

    /// <summary>Writes a deterministic zip of every file of <paramref name="payload"/> (the manifest included, as it is on disk).</summary>
    public static void WriteZip(string payload, Stream output)
    {
        var root = Path.GetFullPath(payload);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
        foreach (var path in files)
        {
            var entry = zip.CreateEntry(PackageFormat.EntryName(path), CompressionLevel.Optimal);
            entry.LastWriteTime = EntryTime;
            using var target = entry.Open();
            using var source = File.OpenRead(Path.Combine(root, path));
            source.CopyTo(target);
        }
    }

    /// <summary>
    /// Writes the setup exe <c>&lt;program&gt;&lt;zip of payload&gt;&lt;trailer&gt;</c> (INS-004). The payload must hold its
    /// <c>payload.sha256</c> (see <see cref="WriteManifest"/>); it is not recomputed here, so a test can package a payload
    /// that does not match its manifest.
    /// </summary>
    public static void WriteSetup(string program, string payload, string output)
    {
        var manifest = Path.Combine(payload, PackageFormat.ManifestName);
        var manifestHash = SHA256.HashData(File.ReadAllBytes(manifest));
        var zipFile = output + ".zip.tmp";
        try
        {
            using (var zip = new FileStream(zipFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                WriteZip(payload, zip);
            using var stream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None);
            using (var source = File.OpenRead(program))
                source.CopyTo(stream);
            var offset = stream.Position;
            using (var source = File.OpenRead(zipFile))
                source.CopyTo(stream);
            stream.Write(PackageFormat.Trailer(offset, manifestHash));
        }
        finally
        {
            File.Delete(zipFile);
        }
    }
}
