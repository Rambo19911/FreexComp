using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// <see cref="IFileSystem"/> in memory with the disk's semantics (checked by
/// <c>FakesTests.InMemoryFileSystemBehavesLikeDisk</c>). Drive roots exist implicitly. Thread-safe.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    private readonly object _gate = new();
    private readonly Dictionary<string, FileEntry> _files = new(Cmp);
    private readonly HashSet<string> _directories = new(Cmp);
    private readonly Func<DateTime> _utcNow;

    public InMemoryFileSystem(Func<DateTime>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    public bool FileExists(string path)
    {
        lock (_gate)
        {
            return _files.ContainsKey(Normalize(path));
        }
    }

    public bool DirectoryExists(string path)
    {
        lock (_gate)
        {
            return DirectoryExistsCore(Normalize(path));
        }
    }

    /// <summary>
    /// Runs on the reading thread before every <see cref="ReadAllBytes"/>, outside the lock: a deterministic gate for
    /// tests of background readers (I-189), e.g. advancing a <see cref="ManualClock"/> per file read.
    /// </summary>
    public Action<string>? BeforeRead { get; set; }

    public byte[] ReadAllBytes(string path)
    {
        BeforeRead?.Invoke(path);
        lock (_gate)
        {
            return [.. GetFile(Normalize(path)).Data];
        }
    }

    public void WriteAllBytes(string path, byte[] data)
    {
        lock (_gate)
        {
            var p = Normalize(path);
            RequireParent(p);
            if (_files.TryGetValue(p, out var existing) && existing.ReadOnly)
            {
                throw new UnauthorizedAccessException($"Access to the path '{p}' is denied.");
            }

            _files[p] = new FileEntry([.. data], _utcNow(), ReadOnly: false);
        }
    }

    /// <summary>Memory has no disk cache: the same as <see cref="WriteAllBytes"/>.</summary>
    public void WriteAllBytesDurable(string path, byte[] data) => WriteAllBytes(path, data);

    public void CreateDirectory(string path)
    {
        lock (_gate)
        {
            for (var d = Normalize(path); d is not null && !IsRoot(d); d = Path.GetDirectoryName(d))
            {
                if (_files.ContainsKey(d))
                {
                    throw new IOException($"A file with the name '{d}' already exists.");
                }

                _directories.Add(d);
            }
        }
    }

    public void DeleteFile(string path)
    {
        lock (_gate)
        {
            var p = Normalize(path);
            if (_files.TryGetValue(p, out var entry) && entry.ReadOnly)
            {
                throw new UnauthorizedAccessException($"Access to the path '{p}' is denied.");
            }

            _files.Remove(p);
        }
    }

    public void DeleteDirectory(string path)
    {
        lock (_gate)
        {
            var d = Normalize(path);
            if (!_directories.Contains(d))
            {
                return;
            }

            if (_files.Keys.Any(f => IsChild(d, f)) || _directories.Any(x => IsChild(d, x)))
            {
                throw new IOException($"The directory is not empty. : '{d}'");
            }

            _directories.Remove(d);
        }
    }

    public void MoveDirectory(string sourcePath, string destinationPath)
    {
        lock (_gate)
        {
            var source = Normalize(sourcePath);
            var destination = Normalize(destinationPath);
            if (!_directories.Contains(source))
            {
                throw new DirectoryNotFoundException($"Could not find a part of the path '{source}'.");
            }

            RequireParent(destination);
            if (_files.ContainsKey(destination) || (_directories.Contains(destination) && !Cmp.Equals(source, destination)))
            {
                throw new IOException($"Cannot create '{destination}' because a file or directory with the same name already exists.");
            }

            string Moved(string path) => destination + path[source.Length..];
            bool Under(string path) => path.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            foreach (var file in _files.Keys.Where(Under).ToList())
            {
                _files.Remove(file, out var entry);
                _files[Moved(file)] = entry!;
            }

            foreach (var directory in _directories.Where(d => Under(d) || Cmp.Equals(d, source)).ToList())
            {
                _directories.Remove(directory);
                _directories.Add(Moved(directory));
            }
        }
    }

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
    {
        lock (_gate)
        {
            var source = Normalize(sourcePath);
            var destination = Normalize(destinationPath);
            var entry = GetFile(source);
            RequireParent(destination);
            if (_files.TryGetValue(destination, out var target))
            {
                if (!overwrite)
                {
                    throw new IOException($"The file '{destination}' already exists.");
                }

                if (target.ReadOnly)
                {
                    throw new UnauthorizedAccessException($"Access to the path '{destination}' is denied.");
                }
            }

            _files.Remove(source);
            _files[destination] = entry;
        }
    }

    public void ReplaceFile(string sourcePath, string destinationPath)
    {
        lock (_gate)
        {
            var source = Normalize(sourcePath);
            var destination = Normalize(destinationPath);
            var entry = GetFile(source);
            var target = GetFile(destination);
            if (target.ReadOnly)
            {
                throw new UnauthorizedAccessException($"Access to the path '{destination}' is denied.");
            }

            _files.Remove(source);
            _files[destination] = target with { Data = entry.Data, LastWriteUtc = entry.LastWriteUtc };
        }
    }

    public void OverwriteFile(string path, byte[] data)
    {
        lock (_gate)
        {
            var p = Normalize(path);
            var entry = GetFile(p);
            if (entry.ReadOnly)
            {
                throw new UnauthorizedAccessException($"Access to the path '{p}' is denied.");
            }

            _files[p] = entry with { Data = [.. data], LastWriteUtc = _utcNow() };
        }
    }

    /// <summary>Every file, in path order: what a test checks when it asserts where state was written.</summary>
    public IReadOnlyList<string> AllFiles
    {
        get
        {
            lock (_gate)
            {
                return [.. _files.Keys.Order(Cmp)];
            }
        }
    }

    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern)
    {
        lock (_gate)
        {
            var d = RequireDirectory(directory);
            return [.. _files.Keys.Where(f => IsChild(d, f) && Wildcard.Matches(searchPattern, Path.GetFileName(f)))
                                  .Order(Cmp)];
        }
    }

    public IReadOnlyList<string> EnumerateDirectories(string directory)
    {
        lock (_gate)
        {
            var d = RequireDirectory(directory);
            return [.. _directories.Where(x => IsChild(d, x)).Order(Cmp)];
        }
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        lock (_gate)
        {
            return GetFile(Normalize(path)).LastWriteUtc;
        }
    }

    public long GetLength(string path)
    {
        lock (_gate)
        {
            return GetFile(Normalize(path)).Data.Length;
        }
    }

    public bool IsReadOnly(string path)
    {
        lock (_gate)
        {
            return _files.TryGetValue(Normalize(path), out var entry) && entry.ReadOnly;
        }
    }

    public bool CanOpenForWrite(string path)
    {
        lock (_gate)
        {
            return _files.TryGetValue(Normalize(path), out var entry) && !entry.ReadOnly;
        }
    }

    /// <summary><see cref="FileAttributes.Normal"/>, or the read-only flag and those set by <see cref="SetAttributes"/>.</summary>
    public FileAttributes GetAttributes(string path)
    {
        lock (_gate)
        {
            var entry = GetFile(Normalize(path));
            var attributes = entry.Other | (entry.ReadOnly ? FileAttributes.ReadOnly : 0);
            return attributes == 0 ? FileAttributes.Normal : attributes;
        }
    }

    /// <summary>Test helper: sets the attributes of an existing file (read-only included).</summary>
    public void SetAttributes(string path, FileAttributes attributes)
    {
        lock (_gate)
        {
            var p = Normalize(path);
            _files[p] = GetFile(p) with { ReadOnly = attributes.HasFlag(FileAttributes.ReadOnly), Other = attributes & ~(FileAttributes.ReadOnly | FileAttributes.Normal) };
        }
    }

    public void SetReadOnly(string path, bool readOnly)
    {
        lock (_gate)
        {
            var p = Normalize(path);
            _files[p] = GetFile(p) with { ReadOnly = readOnly };
        }
    }

    /// <summary>Test helper: sets the modification time of an existing file.</summary>
    public void SetLastWriteTimeUtc(string path, DateTime utc)
    {
        lock (_gate)
        {
            var p = Normalize(path);
            _files[p] = GetFile(p) with { LastWriteUtc = utc };
        }
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsRoot(string path) => Path.GetDirectoryName(path) is null;

    private static bool IsChild(string directory, string path) =>
        Cmp.Equals(Path.GetDirectoryName(path), directory);

    private bool DirectoryExistsCore(string path) => IsRoot(path) || _directories.Contains(path);

    private FileEntry GetFile(string path) =>
        _files.TryGetValue(path, out var entry)
            ? entry
            : throw new FileNotFoundException($"Could not find file '{path}'.", path);

    private void RequireParent(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent is null || !DirectoryExistsCore(parent))
        {
            throw new DirectoryNotFoundException($"Could not find a part of the path '{path}'.");
        }
    }

    private string RequireDirectory(string directory)
    {
        var d = Normalize(directory);
        return DirectoryExistsCore(d)
            ? d
            : throw new DirectoryNotFoundException($"Could not find a part of the path '{d}'.");
    }

    /// <param name="Other">The attributes besides read-only (hidden, system, ...; I-187).</param>
    private sealed record FileEntry(byte[] Data, DateTime LastWriteUtc, bool ReadOnly, FileAttributes Other = 0);

    private static class Wildcard
    {
        public static bool Matches(string pattern, string name) => Match(pattern, 0, name, 0);

        private static bool Match(string p, int i, string s, int j)
        {
            while (i < p.Length)
            {
                var c = p[i];
                if (c == '*')
                {
                    for (var k = j; k <= s.Length; k++)
                    {
                        if (Match(p, i + 1, s, k))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                if (j >= s.Length || (c != '?' && char.ToUpperInvariant(c) != char.ToUpperInvariant(s[j])))
                {
                    return false;
                }

                i++;
                j++;
            }

            return j == s.Length;
        }
    }
}
