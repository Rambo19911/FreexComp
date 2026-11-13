using System.Text;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler;

/// <summary>All file access of the compiler and build engine (api.md §1). Paths are absolute.</summary>
public interface IFileSystem
{
    bool FileExists(string path);

    /// <summary>UTF-8, BOM stripped; throws <see cref="IOException"/>.</summary>
    string ReadAllText(string path);

    byte[] ReadAllBytes(string path);

    /// <summary>Non-recursive.</summary>
    IEnumerable<string> EnumerateFiles(string directory, string searchPattern);

    IEnumerable<string> EnumerateDirectories(string directory);

    DateTime GetLastWriteTimeUtc(string path);

    long GetLength(string path);

    /// <summary>Creates directories; atomic (CMP-141).</summary>
    void WriteAllBytes(string path, byte[] data);

    void Delete(string path);

    /// <summary>Creates the directory and its parents if missing (toolchain outputs, CMP-627).</summary>
    void CreateDirectory(string path);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    private PhysicalFileSystem()
    {
    }

    public static PhysicalFileSystem Instance { get; } = new();

    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) =>
        Directory.Exists(directory) ? Directory.EnumerateFiles(directory, searchPattern) : [];

    public IEnumerable<string> EnumerateDirectories(string directory) =>
        Directory.Exists(directory) ? Directory.EnumerateDirectories(directory) : [];

    public DateTime GetLastWriteTimeUtc(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : throw new FileNotFoundException("File not found", path);

    public long GetLength(string path) => new FileInfo(path).Length;

    public void WriteAllBytes(string path, byte[] data)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
}

/// <summary>For tests: mutable, thread-safe. Paths are normalised (CMP-205) and compared case-insensitively.</summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private static readonly DateTime Epoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (byte[] Data, DateTime Time)> _files = new(PathUtil.Comparer);
    private long _clock;

    /// <summary>Adds or replaces a file with UTF-8 text (no BOM).</summary>
    public InMemoryFileSystem Add(string path, string text)
    {
        WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        return this;
    }

    public bool FileExists(string path)
    {
        lock (_gate)
        {
            return _files.ContainsKey(PathUtil.Normalize(path));
        }
    }

    public string ReadAllText(string path)
    {
        var bytes = ReadAllBytes(path);
        var preamble = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, preamble, bytes.Length - preamble);
    }

    public byte[] ReadAllBytes(string path) => (byte[])Get(path).Data.Clone();

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern)
    {
        var dir = PathUtil.Normalize(directory);
        var pattern = new Regex(
            "^" + Regex.Escape(searchPattern).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        lock (_gate)
        {
            return _files.Keys
                .Where(f => PathUtil.Comparer.Equals(ParentOf(f), dir) && pattern.IsMatch(f[(f.LastIndexOf('\\') + 1)..]))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        var dir = PathUtil.Normalize(directory);
        var prefix = dir.EndsWith('\\') ? dir : dir + @"\";
        lock (_gate)
        {
            return _files.Keys
                .Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.IndexOf('\\', prefix.Length) is var i and >= 0 ? f[..i] : null)
                .OfType<string>()
                .Distinct(PathUtil.Comparer)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public DateTime GetLastWriteTimeUtc(string path) => Get(path).Time;

    public long GetLength(string path) => Get(path).Data.Length;

    /// <summary>Each write advances a deterministic clock by one second.</summary>
    public void WriteAllBytes(string path, byte[] data)
    {
        var key = PathUtil.Normalize(path);
        lock (_gate)
        {
            _clock++;
            _files[key] = ((byte[])data.Clone(), Epoch.AddSeconds(_clock));
        }
    }

    public void Delete(string path)
    {
        lock (_gate)
        {
            _files.Remove(PathUtil.Normalize(path));
        }
    }

    /// <summary>Directories are implicit (they exist when a file below them exists): nothing to do.</summary>
    public void CreateDirectory(string path) => PathUtil.Normalize(path);

    private (byte[] Data, DateTime Time) Get(string path)
    {
        var key = PathUtil.Normalize(path);
        lock (_gate)
        {
            return _files.TryGetValue(key, out var entry) ? entry : throw new FileNotFoundException("File not found", key);
        }
    }

    private static string ParentOf(string path)
    {
        var i = path.LastIndexOf('\\');
        return i <= 2 ? path[..3] : path[..i];
    }
}
