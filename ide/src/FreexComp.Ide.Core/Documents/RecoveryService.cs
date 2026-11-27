using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
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
/// Unsaved-file auto-recovery (phase1-shell.md §12): copies of modified documents at every interval and two seconds
/// after the last edit, adoption of copies left by an instance that ended unexpectedly, clean-up. UI thread only; the
/// copies are encoded and written on the thread pool, in order, from a snapshot of the text.
/// </summary>
public interface IRecoveryService
{
    /// <summary><c>L\recovery\&lt;instanceId&gt;</c> of this process (IDE-P1-121); created with the first copy.</summary>
    string InstanceDirectory { get; }

    /// <summary>
    /// IDE-P1-123 (start-up step 9, not with <c>--safe</c>): opens every copy of orphaned instance folders with the
    /// <c>Msg.File.Recovered</c> bar, deletes those folders and writes this instance's copies at once.
    /// </summary>
    Task RecoverAsync();

    /// <summary>IDE-P1-122: on normal exit this instance's folder is deleted (no more copies are written).</summary>
    Task DeleteInstanceDirectoryAsync();

    /// <summary>Completes when every copy and deletion queued so far is on disk (tests, exit).</summary>
    Task WhenIdle();

    /// <summary>
    /// IDE-ARCH-091 fatal errors: writes a copy of every modified document on the calling thread, now, without the queue
    /// or the timer (the process is about to end). Never throws; I/O failures are logged.
    /// </summary>
    void WriteCopiesNow();
}

/// <summary>A running process: id and start time (UTC ticks), the owner of a recovery folder (IDE-P1-121).</summary>
public readonly record struct ProcessIdentity(int ProcessId, long StartTimeTicks)
{
    public static ProcessIdentity Current
    {
        get
        {
            using var self = Process.GetCurrentProcess();
            return new ProcessIdentity(Environment.ProcessId, self.StartTime.ToUniversalTime().Ticks);
        }
    }

    /// <summary>True when a process with this id and start time runs (IDE-P1-123); one that cannot be asked is not ours.</summary>
    public static bool IsRunning(ProcessIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            return process.StartTime.ToUniversalTime().Ticks == identity.StartTimeTicks;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception
                                       or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>The folder name: process id and start ticks in hex (IDE-P1-121).</summary>
    public string InstanceId => string.Create(CultureInfo.InvariantCulture, $"{ProcessId:x}-{StartTimeTicks:x}");
}

/// <inheritdoc />
public sealed class RecoveryService : IRecoveryService
{
    public const string OwnerFileName = "owner.json";
    private const string LogCategory = "Recovery";
    private const string TextExtension = ".txt";
    private const string MetadataExtension = ".json";
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2); // IDE-P1-126

    private readonly IDocumentService _documents;
    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;
    private readonly ISettingsService _settings;
    private readonly ILog _log;
    private readonly bool _safeMode;
    private readonly ProcessIdentity _self;
    private readonly Func<ProcessIdentity, bool> _isRunning;
    private readonly string _root;
    private readonly CompareService? _compare;
    private readonly Dictionary<DocumentViewModel, CopyState> _copied = [];
    private readonly Dictionary<DocumentViewModel, IdleWatch> _idle = [];
    // Adopted documents whose file changed on disk: what their edits were based on, until they are saved or reloaded.
    private readonly Dictionary<DocumentViewModel, CopyBase> _adoptedBases = [];
    private bool _ended; // normal exit or a fatal error: no more copies
    private readonly object _ioGate = new();
    private Task _io = Task.CompletedTask;
    private IDisposable? _timer;
    private bool _ownerWritten; // touched only inside the I/O chain

    /// <param name="safeMode"><c>--safe</c> (IDE-P1-012): no recovery prompt.</param>
    /// <param name="self">This process (default: the current one).</param>
    /// <param name="isRunning">Whether a folder's owner still runs (default: <see cref="ProcessIdentity.IsRunning"/>).</param>
    /// <param name="compare">Opens the diff of <c>Show Differences</c> (IDE-P1-125); without it the bar has no such action.</param>
    public RecoveryService(IDocumentService documents, IFileSystem fileSystem, IUserPaths paths, IClock clock, ISettingsService settings,
        ILog log, bool safeMode, ProcessIdentity? self = null, Func<ProcessIdentity, bool>? isRunning = null, CompareService? compare = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _documents = documents;
        _fileSystem = fileSystem;
        _clock = clock;
        _settings = settings;
        _log = log;
        _safeMode = safeMode;
        _self = self ?? ProcessIdentity.Current;
        _isRunning = isRunning ?? ProcessIdentity.IsRunning;
        _compare = compare;
        _root = Path.Combine(paths.LocalDirectory, "recovery");
        InstanceDirectory = Path.Combine(_root, _self.InstanceId);

        documents.DocumentOpened += (_, e) =>
        {
            e.Document.PropertyChanged += OnDocumentPropertyChanged;
            if (e.Document is TextDocumentViewModel text)
                WatchEdits(text);
        };
        documents.DocumentClosed += (_, e) =>
        {
            e.Document.PropertyChanged -= OnDocumentPropertyChanged;
            if (e.Document is TextDocumentViewModel text && _idle.Remove(text, out var watch))
            {
                text.Buffer.Changed -= watch.Handler;
                watch.Timer?.Dispose();
            }

            DeleteCopy(e.Document); // any answer of the close prompt (IDE-P1-035, IDE-P1-122)
        };
        documents.DocumentSaved += (_, e) => DeleteCopy(e.Document);
        settings.Changed += (_, e) =>
        {
            if (e.Key == SettingsCatalog.EnvironmentRecoveryIntervalSeconds.Name)
                ScheduleNext();
        };
        ScheduleNext();
    }

    public string InstanceDirectory { get; }

    public async Task RecoverAsync()
    {
        if (_safeMode)
            return;
        List<OrphanFolder> orphans;
        try
        {
            orphans = await Task.Run(ReadOrphans); // IDE-ARCH-081
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogCategory, "Cannot look for recovery copies.", ex); // IDE-P1-124
            return;
        }

        foreach (var folder in orphans)
        {
            foreach (var copy in folder.Copies)
            {
                await AdoptAsync(copy);
            }

            if (folder.Complete)
                Enqueue(() => DeleteFolder(folder.Path)); // adopted: the new instance holds the copies now
        }

        if (orphans.Count > 0)
            WriteCopies(); // IDE-P1-123: immediately
    }

    public Task DeleteInstanceDirectoryAsync()
    {
        StopTimers();
        _copied.Clear();
        _adoptedBases.Clear();
        Enqueue(() => DeleteFolder(InstanceDirectory));
        return WhenIdle();
    }

    public Task WhenIdle()
    {
        lock (_ioGate)
        {
            return _io;
        }
    }

    public void WriteCopiesNow()
    {
        StopTimers();
        List<TextDocumentViewModel> modified;
        try
        {
            modified = [.. _documents.Documents.OfType<TextDocumentViewModel>().Where(d => d.IsModified)];
        }
        catch (InvalidOperationException ex) // the collection changed under a non-UI thread
        {
            _log.Warning(LogCategory, "Cannot list the modified documents for the recovery copies.", ex);
            return;
        }

        foreach (var document in modified)
        {
            try
            {
                Write(Snapshot(document));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _log.Warning(LogCategory, "Recovery copy I/O failed.", ex);
            }
        }
    }

    /// <summary>
    /// One copy as the document is now (IDE-P1-121): its file name, a snapshot of its text and its manifest. O(1): the
    /// text is neither read out nor encoded here (IDE-P1-128). The base is what the document knows of its file since it
    /// was read or written (IDE-P1-127), never the file as it is now.
    /// </summary>
    private PendingCopy Snapshot(TextDocumentViewModel document)
    {
        var @base = document.FilePath is null ? default
            : _adoptedBases.TryGetValue(document, out var adopted) ? adopted
            : new CopyBase(document.DiskStamp?.Hash.ToLowerInvariant(), document.DiskStamp?.LastWriteUtc);
        return new PendingCopy(document.Id.Value.ToString("D"), document.Buffer.CreateSnapshot(), new CopyMetadata(1, document.FilePath,
            document.Title, FileEncodings.ToId(document.Encoding), LineEndingId(document.LineEnding.Kind), _clock.Now.UtcDateTime,
            @base.TimestampUtc, @base.Sha256));
    }

    /// <summary>Encodes and writes <paramref name="copy"/> (thread pool; the calling thread for <see cref="WriteCopiesNow"/>).</summary>
    private void Write(PendingCopy copy)
    {
        EnsureInstanceDirectory();
        AtomicFileWriter.Write(_fileSystem, Path.Combine(InstanceDirectory, copy.Name + TextExtension),
            FileEncodings.Encode(copy.Text(), FileEncoding.Utf8Bom));
        AtomicFileWriter.Write(_fileSystem, Path.Combine(InstanceDirectory, copy.Name + MetadataExtension),
            JsonSerializer.SerializeToUtf8Bytes(copy.Metadata, JsonOptions));
    }

    private bool IsEnabled => !_ended && _settings.Get(SettingsCatalog.EnvironmentRecoveryIntervalSeconds) > 0;

    /// <summary>No copies from now on: the interval timer goes, an idle timer still pending finds <see cref="IsEnabled"/> false.</summary>
    private void StopTimers()
    {
        _ended = true;
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// IDE-P1-126: an edited document that is then left alone for <see cref="IdleDelay"/> gets its copy then. One timer
    /// per idle period, not one per edit: when it falls due after later edits it waits for the rest of their two seconds.
    /// </summary>
    private void WatchEdits(TextDocumentViewModel document)
    {
        var watch = new IdleWatch();
        watch.Handler = (_, _) =>
        {
            watch.LastEdit = _clock.Now;
            if (watch.Timer is null && IsEnabled)
                watch.Timer = _clock.Schedule(IdleDelay, () => OnIdle(document, watch));
        };
        document.Buffer.Changed += watch.Handler;
        _idle[document] = watch;
    }

    private void OnIdle(TextDocumentViewModel document, IdleWatch watch)
    {
        watch.Timer = null;
        if (!IsEnabled || !_idle.ContainsKey(document))
            return; // switched off, or closed, meanwhile
        var rest = watch.LastEdit + IdleDelay - _clock.Now;
        if (rest > TimeSpan.Zero)
            watch.Timer = _clock.Schedule(rest, () => OnIdle(document, watch));
        else if (document.IsModified)
            WriteCopy(document);
    }

    private void ScheduleNext()
    {
        _timer?.Dispose();
        _timer = null;
        var seconds = _settings.Get(SettingsCatalog.EnvironmentRecoveryIntervalSeconds);
        if (seconds <= 0)
            return; // 0 disables (IDE-P1-120)
        _timer = _clock.Schedule(TimeSpan.FromSeconds(seconds), () =>
        {
            WriteCopies();
            ScheduleNext();
        });
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Back at the saved/disk version (Revert, a reload, undo to the save point): nothing to recover (IDE-P1-122).
        if (e.PropertyName == nameof(DocumentViewModel.IsModified) && sender is DocumentViewModel { IsModified: false } document)
            DeleteCopy(document);
    }

    /// <summary>IDE-P1-120: a copy of each document that is modified and changed since its last copy, from a snapshot.</summary>
    private void WriteCopies()
    {
        foreach (var document in _documents.Documents.OfType<TextDocumentViewModel>().Where(d => d.IsModified).ToList())
        {
            WriteCopy(document);
        }
    }

    /// <summary>Queues the copy of a modified document unless it is as its last copy.</summary>
    private void WriteCopy(TextDocumentViewModel document)
    {
        var state = new CopyState(document.Buffer.Version, document.Encoding, document.LineEnding.Kind, document.FilePath);
        if (_copied.TryGetValue(document, out var last) && last == state)
            return;
        _copied[document] = state;
        var copy = Snapshot(document);
        Enqueue(() => Write(copy));
    }

    private void DeleteCopy(DocumentViewModel document)
    {
        _adoptedBases.Remove(document); // saved, reloaded or closed: the edits are based on the file as it is now
        if (!_copied.Remove(document))
            return;
        var name = document.Id.Value.ToString("D");
        Enqueue(() =>
        {
            _fileSystem.DeleteFile(Path.Combine(InstanceDirectory, name + TextExtension));
            _fileSystem.DeleteFile(Path.Combine(InstanceDirectory, name + MetadataExtension));
        });
    }

    private void EnsureInstanceDirectory()
    {
        if (_ownerWritten && _fileSystem.DirectoryExists(InstanceDirectory))
            return;
        _fileSystem.CreateDirectory(InstanceDirectory);
        AtomicFileWriter.Write(_fileSystem, Path.Combine(InstanceDirectory, OwnerFileName),
            JsonSerializer.SerializeToUtf8Bytes(new OwnerFile(1, _self.ProcessId, _self.StartTimeTicks), JsonOptions));
        _ownerWritten = true;
    }

    /// <summary>Runs <paramref name="action"/> after the I/O queued before it, on the thread pool; failures are logged (IDE-P1-124).</summary>
    private void Enqueue(Action action)
    {
        lock (_ioGate)
        {
            _io = _io.ContinueWith(_ =>
            {
                try
                {
                    action();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log.Warning(LogCategory, "Recovery copy I/O failed.", ex);
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private void DeleteFolder(string folder)
    {
        if (!_fileSystem.DirectoryExists(folder))
            return;
        foreach (var file in _fileSystem.EnumerateFiles(folder, "*"))
        {
            _fileSystem.DeleteFile(file);
        }

        _fileSystem.DeleteDirectory(folder);
        if (folder == InstanceDirectory)
            _ownerWritten = false;
    }

    /// <summary>The folders whose owner no longer runs, with their copies (thread pool).</summary>
    private List<OrphanFolder> ReadOrphans()
    {
        var orphans = new List<OrphanFolder>();
        if (!_fileSystem.DirectoryExists(_root))
            return orphans;
        foreach (var folder in _fileSystem.EnumerateDirectories(_root))
        {
            if (string.Equals(folder, InstanceDirectory, StringComparison.OrdinalIgnoreCase))
                continue;
            if (ReadOwner(folder) is { } owner && _isRunning(owner))
                continue; // another instance is alive
            var copies = new List<RecoveredCopy>();
            var complete = true;
            foreach (var metadataPath in _fileSystem.EnumerateFiles(folder, "*" + MetadataExtension))
            {
                if (string.Equals(Path.GetFileName(metadataPath), OwnerFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    var metadata = JsonSerializer.Deserialize<CopyMetadata>(_fileSystem.ReadAllBytes(metadataPath), JsonOptions);
                    var textPath = Path.ChangeExtension(metadataPath, TextExtension);
                    if (metadata is null || !_fileSystem.FileExists(textPath))
                        continue; // garbage: nothing to offer
                    var text = FileEncodings.Decode(_fileSystem.ReadAllBytes(textPath), FileEncoding.Utf8Bom);
                    var fileExists = metadata.Path is { } path && Path.IsPathFullyQualified(path) && _fileSystem.FileExists(path);
                    copies.Add(new RecoveredCopy(metadata, text, fileExists, textPath, metadataPath));
                }
                catch (JsonException ex)
                {
                    _log.Warning(LogCategory, $"Ignoring the unreadable recovery copy {metadataPath}.", ex);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log.Warning(LogCategory, $"Cannot read the recovery copy {metadataPath}; it stays for the next start.", ex);
                    complete = false;
                }
            }

            orphans.Add(new OrphanFolder(folder, [.. copies.OrderBy(c => c.Metadata.SavedUtc)], complete));
        }

        return orphans;
    }

    private ProcessIdentity? ReadOwner(string folder)
    {
        try
        {
            var path = Path.Combine(folder, OwnerFileName);
            if (!_fileSystem.FileExists(path))
                return null;
            var owner = JsonSerializer.Deserialize<OwnerFile>(_fileSystem.ReadAllBytes(path), JsonOptions);
            return owner is null ? null : new ProcessIdentity(owner.ProcessId, owner.StartTimeTicks);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null; // no valid owner: orphaned
        }
    }

    /// <summary>
    /// IDE-P1-123: the recovered text as an unsaved modification of its file, or an untitled document when there is no
    /// file (any more), with the <c>Msg.File.Recovered</c> bar, or <c>Msg.File.RecoveredDiskChanged</c> when the file is
    /// not the one the edits were based on (IDE-P1-127).
    /// </summary>
    private async Task AdoptAsync(RecoveredCopy copy)
    {
        var metadata = copy.Metadata;
        if (copy.FileExists)
        {
            var opened = await _documents.OpenAsync(metadata.Path!);
            if (opened.Success && opened.Value is TextDocumentViewModel document)
            {
                if (document.Buffer.Text != copy.Text)
                {
                    var readOnly = document.Buffer.IsReadOnly;
                    document.Buffer.IsReadOnly = false;
                    try
                    {
                        document.Buffer.ApplyEdits([new TextEdit(0, document.Buffer.Length, copy.Text)]); // one undo step back to the disk text
                    }
                    finally
                    {
                        document.Buffer.IsReadOnly = readOnly;
                    }

                    var diskChanged = DiskChanged(metadata, document.DiskStamp);
                    if (diskChanged)
                        _adoptedBases[document] = new CopyBase(metadata.BaseSha256?.ToLowerInvariant(), metadata.BaseTimestampUtc);
                    ShowRecoveredBar(document, copy, diskChanged);
                }

                return;
            }
        }

        var title = string.IsNullOrWhiteSpace(metadata.Title) ? "Unit1.pas" : Path.GetFileName(metadata.Title);
        var untitled = _documents.OpenUntitled(title, copy.Text, FileEncodings.FromId(metadata.Encoding ?? "") ?? FileEncoding.Utf8,
            metadata.LineEnding == "lf" ? LineEndingKind.Lf : LineEndingKind.CrLf);
        ShowRecoveredBar(untitled, copy, diskChanged: false);
    }

    /// <summary>
    /// IDE-P1-127: the file as just read for the document (<paramref name="disk"/>) against the manifest's hash of the
    /// bytes the edits were based on; a manifest without a hash (I-330) has only their time. Nothing known: not changed.
    /// </summary>
    private static bool DiskChanged(CopyMetadata metadata, DiskStamp? disk)
    {
        if (disk is null)
            return false;
        if (metadata.BaseSha256 is { } hash)
            return !string.Equals(hash, disk.Hash, StringComparison.OrdinalIgnoreCase);
        return metadata.BaseTimestampUtc is { } time && time.ToUniversalTime() != disk.LastWriteUtc;
    }

    private void ShowRecoveredBar(TextDocumentViewModel document, RecoveredCopy copy, bool diskChanged)
    {
        var savedUtc = copy.Metadata.SavedUtc;
        Task Keep()
        {
            document.RemoveInfoBar(TextDocumentViewModel.RecoveredInfoBarKind); // stays modified until saved
            return Task.CompletedTask;
        }

        var when = DateTime.SpecifyKind(savedUtc, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        // IDE-P1-125: Keep, Show Differences (only with a file to compare with), Discard.
        List<InfoBarAction> actions = [new InfoBarAction("KeepRecovered", Strings.Button_KeepRecovered, Keep)];
        if (_compare is { } compare && document.FilePath is not null)
        {
            actions.Add(new InfoBarAction("ShowDifferences", Strings.Button_ShowDifferences,
                () => compare.CompareWithDiskAsync(document, Strings.Diff_OnDiskSuffix, Strings.Diff_RecoveredSuffix)));
        }

        actions.Add(new InfoBarAction("DiscardAndReload", Strings.Button_DiscardAndReload, () => DiscardAsync(document, copy)));
        document.ShowInfoBar(new InfoBarViewModel(TextDocumentViewModel.RecoveredInfoBarKind,
            diskChanged ? InfoBarSeverity.Warning : InfoBarSeverity.Info,
            string.Format(CultureInfo.CurrentCulture, diskChanged ? Strings.Msg_File_RecoveredDiskChanged : Strings.Msg_File_Recovered, when),
            actions, _ => Keep()));
    }

    /// <summary>
    /// Discard and Reload from Disk: back to the file's content, or the untitled document closes; this instance's copy
    /// and the adopted one (still there when its folder could not be read completely) go at once (IDE-P1-129).
    /// </summary>
    private async Task DiscardAsync(TextDocumentViewModel document, RecoveredCopy copy)
    {
        if (document.FilePath is not { } path)
        {
            _documents.Close(document); // deletes its copy
            DeleteAdopted(copy);
            return;
        }

        (byte[] Bytes, DiskStamp Stamp) read;
        try
        {
            read = await Task.Run(() =>
            {
                var lastWrite = _fileSystem.GetLastWriteTimeUtc(path);
                var bytes = _fileSystem.ReadAllBytes(path);
                return (bytes, DiskStamp.Of(lastWrite, bytes));
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogCategory, $"Cannot reload {path}.", ex); // IDE-P1-124
            return;
        }

        document.Reload(FileEncodings.Decode(read.Bytes, document.Encoding)); // removes the bar
        document.DiskStamp = read.Stamp;
        DeleteCopy(document); // whatever the document's state is now
        DeleteAdopted(copy);
    }

    private void DeleteAdopted(RecoveredCopy copy) => Enqueue(() =>
    {
        foreach (var file in (string[])[copy.TextPath, copy.MetadataPath])
        {
            if (_fileSystem.FileExists(file))
                _fileSystem.DeleteFile(file);
        }
    });

    private static string LineEndingId(LineEndingKind kind) => kind switch
    {
        LineEndingKind.Lf => "lf",
        LineEndingKind.Mixed => "mixed",
        _ => "crlf",
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly record struct CopyState(long Version, FileEncoding Encoding, LineEndingKind LineEnding, string? Path);

    /// <summary>
    /// IDE-P1-121 <c>&lt;docGuid&gt;.json</c>; <see cref="BaseSha256"/> (IDE-P1-127): lowercase hex SHA-256 of the disk
    /// bytes the edits were based on, absent in a manifest of I-330.
    /// </summary>
    private sealed record CopyMetadata(int FormatVersion, string? Path, string? Title, string? Encoding, string? LineEnding,
        DateTime SavedUtc, DateTime? BaseTimestampUtc, string? BaseSha256 = null);

    private sealed record OwnerFile(int FormatVersion, int ProcessId, long StartTimeTicks);

    /// <summary>A copy found in an orphaned folder, with the two files it was read from.</summary>
    private sealed record RecoveredCopy(CopyMetadata Metadata, string Text, bool FileExists, string TextPath, string MetadataPath);

    /// <summary>What a document's edits are based on: the hash and the time of its file as read or written.</summary>
    private readonly record struct CopyBase(string? Sha256, DateTime? TimestampUtc);

    /// <summary>A copy to write: taken on the UI thread, encoded and written off it.</summary>
    private sealed record PendingCopy(string Name, Func<string> Text, CopyMetadata Metadata);

    /// <summary>The idle timer of one document (IDE-P1-126).</summary>
    private sealed class IdleWatch
    {
        public EventHandler<TextChangedEventArgs>? Handler { get; set; }

        public IDisposable? Timer { get; set; }

        public DateTimeOffset LastEdit { get; set; }
    }

    private sealed record OrphanFolder(string Path, IReadOnlyList<RecoveredCopy> Copies, bool Complete);
}
