using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Tests.Fakes;

/// <summary>
/// <see cref="IFileWatcherFactory"/> whose watchers report only what the test raises; events are delivered
/// synchronously on the calling thread to every undisposed watcher of the change's directory.
/// </summary>
public sealed class ManualFileWatcherFactory : IFileWatcherFactory
{
    private readonly List<ManualFileWatcher> _watchers = [];

    /// <summary>Directories of the watchers that are not disposed, in creation order.</summary>
    public IReadOnlyList<string> ActiveDirectories => [.. _watchers.Select(w => w.Directory)];

    public IFileWatcher Watch(string directory)
    {
        var watcher = new ManualFileWatcher(this, directory);
        _watchers.Add(watcher);
        return watcher;
    }

    public void Raise(FileChange change)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(change.Path));
        foreach (var watcher in _watchers.Where(w => SameDirectory(w.Directory, directory)).ToList())
        {
            watcher.OnChanged(change);
        }
    }

    public void RaiseError(string directory, Exception error)
    {
        foreach (var watcher in _watchers.Where(w => SameDirectory(w.Directory, directory)).ToList())
        {
            watcher.OnError(error);
        }
    }

    internal void Remove(ManualFileWatcher watcher) => _watchers.Remove(watcher);

    private static bool SameDirectory(string a, string? b) =>
        b is not null && StringComparer.OrdinalIgnoreCase.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)));
}

/// <summary>A watcher created by <see cref="ManualFileWatcherFactory"/>.</summary>
public sealed class ManualFileWatcher : IFileWatcher
{
    private readonly ManualFileWatcherFactory _owner;

    internal ManualFileWatcher(ManualFileWatcherFactory owner, string directory)
    {
        _owner = owner;
        Directory = directory;
    }

    public event EventHandler<FileChange>? Changed;

    public event EventHandler<Exception>? Error;

    public string Directory { get; }

    public void Dispose() => _owner.Remove(this);

    internal void OnChanged(FileChange change) => Changed?.Invoke(this, change);

    internal void OnError(Exception error) => Error?.Invoke(this, error);
}
