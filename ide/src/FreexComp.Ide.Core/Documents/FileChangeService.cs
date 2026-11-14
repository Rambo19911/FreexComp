using System.ComponentModel;
using System.Globalization;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Documents;

/// <summary>
/// External changes to open files (phase1-shell.md §11): watchers per directory, the activation re-check, the info
/// bars, the silent reload and the suppression of the IDE's own saves. UI thread only, except where noted.
/// </summary>
public interface IFileChangeService
{
    /// <summary>IDE-P1-110 (b): re-checks every open file now (the main window was activated).</summary>
    void CheckAll();

    /// <summary>Completes when every check started so far has finished (tests, exit).</summary>
    Task WhenIdle();

    /// <summary>
    /// IDE-P1-117: watches the open project's file (its folder keeps a watcher while the project is open). A watcher event
    /// for it (debounced like a document, own writes ignored) and every <see cref="CheckAll"/> run
    /// <paramref name="check"/> on the UI thread. A null <paramref name="path"/> stops watching.
    /// </summary>
    void WatchProjectFile(string? path, Func<Task>? check);
}

/// <inheritdoc />
public sealed class FileChangeService : IFileChangeService
{
    /// <summary>IDE-P1-111: a file is checked this long after its last watcher event.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    /// <summary>IDE-P1-086: events for a file the IDE wrote are ignored until this long after the write.</summary>
    public static readonly TimeSpan OwnWriteQuiet = TimeSpan.FromMilliseconds(500);

    private const string LogCategory = "FileChanges";
    private static readonly TimeSpan StatusMessageDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    private readonly IDocumentService _documents;
    private readonly IFileSystem _fileSystem;
    private readonly IFileWatcherFactory _watcherFactory;
    private readonly IClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly ISettingsService _settings;
    private readonly IStatusService _status;
    private readonly ILog _log;
    private readonly ICommandService _commands;
    private readonly Dictionary<string, IFileWatcher> _watchers = new(PathComparer);
    private readonly Dictionary<string, IDisposable> _pending = new(PathComparer);
    private readonly Dictionary<string, int> _writing = new(PathComparer);
    private readonly Dictionary<string, DateTimeOffset> _quietUntil = new(PathComparer);
    private readonly List<Task> _running = [];
    private string? _projectFile;
    private Func<Task>? _projectCheck;
    private IDisposable? _projectPending;

    public FileChangeService(IDocumentService documents, IFileSystem fileSystem, IFileWatcherFactory watchers, IClock clock,
        IUiDispatcher dispatcher, ISettingsService settings, IStatusService status, ILog log, ICommandService commands)
    {
        _documents = documents;
        _fileSystem = fileSystem;
        _watcherFactory = watchers;
        _clock = clock;
        _dispatcher = dispatcher;
        _settings = settings;
        _status = status;
        _log = log;
        _commands = commands;
        documents.DocumentOpened += (_, e) =>
        {
            e.Document.PropertyChanged += OnDocumentPropertyChanged;
            SyncWatchers();
        };
        documents.DocumentClosed += (_, e) =>
        {
            e.Document.PropertyChanged -= OnDocumentPropertyChanged;
            SyncWatchers();
        };
        documents.DocumentSaved += (_, e) =>
        {
            // The saved text is the disk version (IDE-P1-115: saving recreates a deleted file).
            e.Document.IsDeletedOnDisk = false;
            e.Document.RemoveInfoBar(TextDocumentViewModel.ExternalChangeInfoBarKind);
            e.Document.RemoveInfoBar(TextDocumentViewModel.ExternalDeleteInfoBarKind);
        };
        settings.Changed += (_, e) =>
        {
            if (e.Key == SettingsCatalog.FilesDetectExternalChanges.Name)
                SyncWatchers();
        };
    }

    private bool Enabled => _settings.Get(SettingsCatalog.FilesDetectExternalChanges);

    /// <summary>
    /// IDE-P1-086: the document service writes <paramref name="path"/> until the result is disposed; events for it are
    /// ignored meanwhile and for <see cref="OwnWriteQuiet"/> afterwards. Dispose on the UI thread.
    /// </summary>
    public IDisposable BeginOwnWrite(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _writing[path] = _writing.GetValueOrDefault(path) + 1;
        if (_pending.Remove(path, out var timer))
            timer.Dispose();
        if (PathComparer.Equals(path, _projectFile))
        {
            _projectPending?.Dispose();
            _projectPending = null;
        }

        return new OwnWrite(this, path);
    }

    public void CheckAll()
    {
        if (!Enabled)
            return;
        foreach (var document in _documents.Documents.OfType<TextDocumentViewModel>().Where(d => d.FilePath is not null).ToList())
        {
            Check(document);
        }

        CheckProjectFile();
    }

    public void WatchProjectFile(string? path, Func<Task>? check)
    {
        _projectPending?.Dispose();
        _projectPending = null;
        _projectFile = path is null || check is null ? null : Path.GetFullPath(path);
        _projectCheck = _projectFile is null ? null : check;
        SyncWatchers();
    }

    private void CheckProjectFile()
    {
        if (Enabled && _projectFile is { } path && _projectCheck is { } check && !IsQuiet(path))
            Track(check());
    }

    public async Task WhenIdle()
    {
        while (true)
        {
            Task[] running;
            lock (_running)
            {
                running = [.. _running];
            }

            if (running.Length == 0)
                return;
            await Task.WhenAll(running);
        }
    }

    private void EndOwnWrite(string path)
    {
        if (--_writing[path] == 0)
            _writing.Remove(path);
        _quietUntil[path] = _clock.Now + OwnWriteQuiet;
    }

    private bool IsQuiet(string path) =>
        _writing.ContainsKey(path) || (_quietUntil.TryGetValue(path, out var until) && _clock.Now < until);

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.FilePath)) // Save As moved it to another folder
            SyncWatchers();
    }

    /// <summary>IDE-P1-118: one watcher per directory with an open file; disposed when its last file closes.</summary>
    private void SyncWatchers()
    {
        var wanted = Enabled
            ? _documents.Documents.Select(d => d.FilePath).Append(_projectFile).OfType<string>().Select(Path.GetDirectoryName)
                .OfType<string>().ToHashSet(PathComparer)
            : [];
        foreach (var directory in _watchers.Keys.Where(d => !wanted.Contains(d)).ToList())
        {
            _watchers.Remove(directory, out var watcher);
            watcher!.Dispose();
        }

        foreach (var directory in wanted.Where(d => !_watchers.ContainsKey(d)))
        {
            try
            {
                var watcher = _watcherFactory.Watch(directory);
                watcher.Changed += (_, change) => _dispatcher.Post(() => OnChanged(change));
                watcher.Error += (_, error) => _dispatcher.Post(() =>
                {
                    _log.Warning(LogCategory, $"Watcher error in {directory}; checking every open file.", error);
                    CheckAll();
                });
                _watchers[directory] = watcher;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                _log.Warning(LogCategory, $"Cannot watch {directory}; only the activation re-check applies.", ex);
            }
        }
    }

    /// <summary>A watcher event (UI thread): the affected open file is checked after <see cref="Debounce"/>.</summary>
    private void OnChanged(FileChange change)
    {
        if (!Enabled)
            return;
        foreach (var path in (string?[])[change.Path, change.OldPath])
        {
            if (path is not null && _projectFile is { } projectFile && PathComparer.Equals(Path.GetFullPath(path), projectFile)
                && !IsQuiet(projectFile))
            {
                _projectPending?.Dispose();
                _projectPending = _clock.Schedule(Debounce, () =>
                {
                    _projectPending = null;
                    CheckProjectFile();
                });
            }

            if (path is null || FindDocument(Path.GetFullPath(path)) is not { FilePath: { } documentPath } || IsQuiet(documentPath))
                continue;
            if (_pending.Remove(documentPath, out var timer))
                timer.Dispose();
            _pending[documentPath] = _clock.Schedule(Debounce, () =>
            {
                _pending.Remove(documentPath);
                if (FindDocument(documentPath) is { } document)
                    Check(document);
            });
        }
    }

    private TextDocumentViewModel? FindDocument(string path) =>
        _documents.Documents.OfType<TextDocumentViewModel>().FirstOrDefault(d => PathComparer.Equals(d.FilePath, path));

    private void Check(TextDocumentViewModel document)
    {
        if (document.FilePath is { } path && !IsQuiet(path))
            Track(CheckAsync(document, path));
    }

    private void Track(Task task)
    {
        lock (_running)
        {
            _running.Add(task);
        }

        _ = ForgetAsync(task);
    }

    private async Task ForgetAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            _log.Error(LogCategory, "External-change check failed.", ex);
        }
        finally
        {
            lock (_running)
            {
                _running.Remove(task);
            }
        }
    }

    /// <summary>IDE-P1-110: compares (existence, last-write time, length) with the recorded stamp; reads only on a difference.</summary>
    private async Task CheckAsync(TextDocumentViewModel document, string path)
    {
        var recorded = document.DiskStamp;
        Probe probe;
        try
        {
            probe = await Task.Run(() => Examine(path, recorded)); // IDE-ARCH-081
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogCategory, $"Cannot check {path}.", ex); // e.g. locked while another program writes it
            return;
        }

        if (!_documents.Documents.Contains(document) || !PathComparer.Equals(document.FilePath, path) || IsQuiet(path)
            || !ReferenceEquals(document.DiskStamp, recorded))
            return; // closed, saved or reloaded meanwhile

        if (probe.Missing)
        {
            if (!document.IsDeletedOnDisk)
                ShowDeletedBar(document);
            return;
        }

        if (document.IsDeletedOnDisk)
        {
            // IDE-P1-115: the file came back before the user answered.
            document.IsDeletedOnDisk = false;
            document.RemoveInfoBar(TextDocumentViewModel.ExternalDeleteInfoBarKind);
        }

        if (probe.Stamp is not { } stamp)
            return; // unchanged time and length
        if (recorded is null || recorded.Hash == stamp.Hash)
        {
            // IDE-P1-112: identical content only updates the recorded stamp (unknown before: start from here).
            document.DiskStamp = stamp;
            document.RemoveInfoBar(TextDocumentViewModel.ExternalChangeInfoBarKind);
            return;
        }

        if (!document.IsModified && _settings.Get(SettingsCatalog.EnvironmentAutoReloadUnmodified))
        {
            Reload(document, stamp, probe.Bytes!); // IDE-P1-113
            _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_ReloadedSilently, document.Title), StatusMessageDuration);
            return;
        }

        ShowChangedBar(document, stamp, dirty: document.IsModified);
    }

    private Probe Examine(string path, DiskStamp? recorded)
    {
        try
        {
            if (!_fileSystem.FileExists(path))
                return new Probe(Missing: true, null, null);
            var lastWrite = _fileSystem.GetLastWriteTimeUtc(path);
            if (recorded is not null && recorded.Matches(lastWrite, _fileSystem.GetLength(path)))
                return new Probe(Missing: false, null, null);
            var bytes = _fileSystem.ReadAllBytes(path);
            return new Probe(Missing: false, DiskStamp.Of(lastWrite, bytes), bytes);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Probe(Missing: true, null, null); // deleted while we looked
        }
    }

    /// <summary>IDE-P1-113: the disk text replaces the buffer (caret kept, clamped; undo cleared; folds kept).</summary>
    private void Reload(TextDocumentViewModel document, DiskStamp stamp, byte[] bytes)
    {
        document.Reload(FileEncodings.Decode(bytes, document.Encoding));
        document.DiskStamp = stamp;
        _commands.InvalidateCanExecute();
    }

    /// <summary>Reload / Reload and Discard My Changes: the file as it is now.</summary>
    private async Task ReloadFromDiskAsync(TextDocumentViewModel document)
    {
        if (document.FilePath is not { } path || !_documents.Documents.Contains(document))
            return;
        Probe probe;
        try
        {
            probe = await Task.Run(() => Examine(path, recorded: null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogCategory, $"Cannot reload {path}.", ex);
            return;
        }

        if (probe.Missing)
        {
            ShowDeletedBar(document);
            return;
        }

        Reload(document, probe.Stamp!, probe.Bytes!);
    }

    /// <summary>IDE-P1-113 / IDE-P1-114: one external-change bar per document (IDE-P1-116).</summary>
    private void ShowChangedBar(TextDocumentViewModel document, DiskStamp stamp, bool dirty)
    {
        Task Keep(InfoBarViewModel? _)
        {
            document.DiskStamp = stamp; // the same change is not reported again
            document.RemoveInfoBar(TextDocumentViewModel.ExternalChangeInfoBarKind);
            return Task.CompletedTask;
        }

        Task ReloadNow()
        {
            var task = ReloadFromDiskAsync(document);
            Track(task);
            return task;
        }

        document.RemoveInfoBar(TextDocumentViewModel.ExternalDeleteInfoBarKind);
        document.ShowInfoBar(new InfoBarViewModel(TextDocumentViewModel.ExternalChangeInfoBarKind, InfoBarSeverity.Warning,
            dirty ? Strings.Msg_File_ExternalChangeDirty : Strings.Msg_File_ExternalChange,
            [
                dirty
                    ? new InfoBarAction("ReloadAndDiscard", Strings.Button_ReloadAndDiscard, ReloadNow)
                    : new InfoBarAction("Reload", Strings.Button_Reload, ReloadNow),
                new InfoBarAction("KeepMyVersion", Strings.Button_KeepMyVersion, () => Keep(null)),
            ],
            bar => Keep(bar)));
    }

    /// <summary>IDE-P1-115: Keep in Editor / Close; the tab title is struck through (UI-ED-074).</summary>
    private void ShowDeletedBar(TextDocumentViewModel document)
    {
        Task KeepInEditor()
        {
            document.MarkModified(); // saving recreates the file
            document.RemoveInfoBar(TextDocumentViewModel.ExternalDeleteInfoBarKind);
            return Task.CompletedTask;
        }

        async Task Close()
        {
            if (document.IsModified)
                await _documents.CloseAsync(document); // the normal prompt
            else
                _documents.Close(document);
        }

        document.IsDeletedOnDisk = true;
        document.RemoveInfoBar(TextDocumentViewModel.ExternalChangeInfoBarKind);
        document.ShowInfoBar(new InfoBarViewModel(TextDocumentViewModel.ExternalDeleteInfoBarKind, InfoBarSeverity.Warning,
            Strings.Msg_File_ExternalDelete,
            [
                new InfoBarAction("KeepInEditor", Strings.Button_KeepInEditor, KeepInEditor),
                new InfoBarAction("Close", Strings.Button_Close, Close),
            ],
            _ => KeepInEditor()));
    }

    /// <summary>What a check found: missing, unchanged (no stamp), or changed (new stamp and the bytes read).</summary>
    private sealed record Probe(bool Missing, DiskStamp? Stamp, byte[]? Bytes);

    private sealed class OwnWrite(FileChangeService owner, string path) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            owner.EndOwnWrite(path);
        }
    }
}
