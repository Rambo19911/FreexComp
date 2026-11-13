namespace FreexComp.Ide.Core.Platform;

public enum FileChangeKind
{
    Created,
    Changed,
    Deleted,
    Renamed,
}

/// <summary>One change in a watched directory; <paramref name="OldPath"/> is set for <see cref="FileChangeKind.Renamed"/>.</summary>
public sealed record FileChange(FileChangeKind Kind, string Path, string? OldPath = null);

/// <summary>
/// Watches the files directly in one directory (not recursive). Events may arrive on any thread; consumers
/// marshal through <see cref="IUiDispatcher"/>. <see cref="Error"/> means changes may have been lost (IDE-P1-118).
/// </summary>
public interface IFileWatcher : IDisposable
{
    string Directory { get; }

    event EventHandler<FileChange>? Changed;

    event EventHandler<Exception>? Error;
}

/// <summary>Creates file watchers (IDE-P1-118).</summary>
public interface IFileWatcherFactory
{
    IFileWatcher Watch(string directory);
}
