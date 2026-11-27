using System.IO.Compression;
using System.Security.Cryptography;

namespace FreexComp.Setup;

/// <summary>
/// A setup package opened for reading (INS-003, INS-004): the trailer is read on open, <see cref="Verify"/> checks the
/// whole payload against the manifest without writing anything, <see cref="Extract"/> writes it and checks each file again.
/// </summary>
public sealed class SetupPackage : IDisposable
{
    private readonly FileStream _file;
    private readonly ZipArchive _zip;
    private readonly byte[] _manifestHash;

    private SetupPackage(string path, FileStream file, long zipOffset, byte[] manifestHash, ZipArchive zip)
    {
        FilePath = path;
        _file = file;
        ZipOffset = zipOffset;
        _manifestHash = manifestHash;
        _zip = zip;
    }

    public string FilePath { get; }

    /// <summary>Where the zip starts: the length of the setup program without payload.</summary>
    public long ZipOffset { get; }

    /// <summary>The verified manifest entries (after <see cref="Verify"/>), in file order.</summary>
    public IReadOnlyList<ManifestEntry> Manifest { get; private set; } = [];

    /// <summary>The sum of the uncompressed payload sizes, manifest included (after <see cref="Verify"/>).</summary>
    public long PayloadBytes { get; private set; }

    /// <summary>Opens <paramref name="path"/>; a missing or wrong trailer or an unreadable zip is a damaged package (exit 3).</summary>
    public static SetupPackage Open(string path)
    {
        var name = Path.GetFileName(path);
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw SetupException.Damaged(name, e);
        }

        try
        {
            if (file.Length < PackageFormat.TrailerLength)
                throw SetupException.Damaged(name);
            var trailer = new byte[PackageFormat.TrailerLength];
            file.Seek(-PackageFormat.TrailerLength, SeekOrigin.End);
            file.ReadExactly(trailer);
            var end = file.Length - PackageFormat.TrailerLength;
            if (PackageFormat.ReadTrailer(trailer) is not var (offset, hash) || offset < 0 || offset >= end)
                throw SetupException.Damaged(name);
            ZipArchive zip;
            try
            {
                zip = new ZipArchive(new WindowStream(file, offset, end - offset), ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (InvalidDataException e)
            {
                throw SetupException.Damaged(name, e);
            }

            return new SetupPackage(path, file, offset, hash, zip);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>
    /// INS-003: the manifest matches the trailer hash, and every zip entry matches its manifest line (no missing, no extra
    /// entry). Nothing is written. Throws <see cref="SetupException"/> (exit 3) naming the first bad path.
    /// </summary>
    public void Verify(IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        var manifestEntry = _zip.GetEntry(PackageFormat.ManifestName) ?? throw SetupException.Damaged(PackageFormat.ManifestName);
        var manifestBytes = ReadAll(manifestEntry, PackageFormat.ManifestName);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(manifestBytes), _manifestHash))
            throw SetupException.Damaged(PackageFormat.ManifestName);
        var manifest = PackageFormat.ParseManifest(manifestBytes) ?? throw SetupException.Damaged(PackageFormat.ManifestName);
        var expected = manifest.ToDictionary(e => e.Path, e => e.Hash, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = _zip.Entries.Sum(e => e.Length), done = 0, bytes = manifestBytes.Length;
        foreach (var entry in _zip.Entries)
        {
            cancel.ThrowIfCancellationRequested();
            var path = entry.FullName.Replace('/', '\\');
            if (path.Equals(PackageFormat.ManifestName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!expected.TryGetValue(path, out var hash) || !seen.Add(path))
                throw SetupException.Damaged(path);   // an extra (or repeated) entry
            if (HashEntry(entry, path) != hash)
                throw SetupException.Damaged(path);
            bytes += entry.Length;
            done += entry.Length;
            progress?.Report(total == 0 ? 1 : (double)done / total);
        }

        if (manifest.FirstOrDefault(e => !seen.Contains(e.Path)) is { } missing)
            throw SetupException.Damaged(missing.Path);
        Manifest = manifest;
        PayloadBytes = bytes;
    }

    /// <summary>
    /// Writes the verified payload (manifest included) into <paramref name="target"/>, checking each written file against
    /// its manifest hash; <paramref name="writing"/> receives each relative path before its file is created (for rollback).
    /// </summary>
    public void Extract(string target, Action<string> writing, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (Manifest.Count == 0)
            throw new InvalidOperationException("Verify the package before extracting it.");
        var files = Manifest.Select(e => (e.Path, Hash: e.Hash)).Append((PackageFormat.ManifestName, PackageFormat.HashHex(_manifestHash)));
        long total = PayloadBytes, done = 0;
        foreach (var (path, hash) in files)
        {
            cancel.ThrowIfCancellationRequested();
            var entry = _zip.GetEntry(PackageFormat.EntryName(path)) ?? throw SetupException.Damaged(path);
            var destination = Path.Combine(target, path);
            writing(path);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using (var input = entry.Open())
                using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                    input.CopyTo(output);
            }
            catch (InvalidDataException e)
            {
                throw SetupException.Damaged(path, e);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw SetupException.Io(destination, e);
            }

            // INS-003: each file is verified again after extraction, read back from the disk
            if (PackageFormat.HashFile(destination) != hash)
                throw SetupException.Damaged(path);
            done += entry.Length;
            progress?.Report(total == 0 ? 1 : (double)done / total);
        }
    }

    /// <summary>Writes the setup program without payload (the bytes before the zip) to <paramref name="destination"/>.</summary>
    public void CopyProgramTo(string destination)
    {
        try
        {
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            using var program = new WindowStream(_file, 0, ZipOffset);
            program.CopyTo(output);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw SetupException.Io(destination, e);
        }
    }

    public void Dispose()
    {
        _zip.Dispose();
        _file.Dispose();
    }

    private static byte[] ReadAll(ZipArchiveEntry entry, string path)
    {
        try
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (InvalidDataException e)
        {
            throw SetupException.Damaged(path, e);
        }
    }

    private static string HashEntry(ZipArchiveEntry entry, string path)
    {
        try
        {
            using var stream = entry.Open();
            return PackageFormat.HashHex(SHA256.HashData(stream));
        }
        catch (InvalidDataException e)
        {
            throw SetupException.Damaged(path, e);
        }
    }

    /// <summary>A read-only, seekable view of <c>[start, start + length)</c> of a file stream (the appended zip).</summary>
    private sealed class WindowStream(FileStream inner, long start, long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var left = length - _position;
            if (left <= 0)
                return 0;
            if (buffer.Length > left)
                buffer = buffer[..(int)left];
            inner.Position = start + _position;
            var read = inner.Read(buffer);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
