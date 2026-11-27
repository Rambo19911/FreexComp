using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Platform;

/// <summary><see cref="IFileWatcherFactory"/> over <see cref="System.IO.FileSystemWatcher"/> (IDE-P1-118).</summary>
public sealed class FileSystemWatcherFactory : IFileWatcherFactory
{
    public IFileWatcher Watch(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return new Watcher(directory);
    }

    private sealed class Watcher : IFileWatcher
    {
        private readonly FileSystemWatcher _watcher;

        public Watcher(string directory)
        {
            Directory = directory;
            _watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes,
            };
            _watcher.Created += (_, e) => Changed?.Invoke(this, new FileChange(FileChangeKind.Created, e.FullPath));
            _watcher.Changed += (_, e) => Changed?.Invoke(this, new FileChange(FileChangeKind.Changed, e.FullPath));
            _watcher.Deleted += (_, e) => Changed?.Invoke(this, new FileChange(FileChangeKind.Deleted, e.FullPath));
            _watcher.Renamed += (_, e) => Changed?.Invoke(this, new FileChange(FileChangeKind.Renamed, e.FullPath, e.OldFullPath));
            _watcher.Error += (_, e) => Error?.Invoke(this, e.GetException());
            _watcher.EnableRaisingEvents = true;
        }

        public event EventHandler<FileChange>? Changed;

        public event EventHandler<Exception>? Error;

        public string Directory { get; }

        public void Dispose() => _watcher.Dispose();
    }
}
