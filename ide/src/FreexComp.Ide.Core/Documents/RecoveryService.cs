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
/// Unsaved-file auto-recovery (phase1-shell.md §12): periodic copies of modified documents, adoption of copies left by
/// an instance that ended unexpectedly, clean-up. UI thread only; the copies are written on the thread pool, in order.
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

    private readonly IDocumentService _documents;
    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;
    private readonly ISettingsService _settings;
    private readonly ILog _log;
    private readonly bool _safeMode;
    private readonly ProcessIdentity _self;
    private readonly Func<ProcessIdentity, bool> _isRunning;
    private readonly string _root;
    private readonly Dictionary<DocumentViewModel, CopyState> _copied = [];
    private readonly object _ioGate = new();
    private Task _io = Task.CompletedTask;
    private IDisposable? _timer;
    private bool _ownerWritten; // touched only inside the I/O chain

    /// <param name="safeMode"><c>--safe</c> (IDE-P1-012): no recovery prompt.</param>
    /// <param name="self">This process (default: the current one).</param>
    /// <param name="isRunning">Whether a folder's owner still runs (default: <see cref="ProcessIdentity.IsRunning"/>).</param>
    public RecoveryService(IDocumentService documents, IFileSystem fileSystem, IUserPaths paths, IClock clock, ISettingsService settings,
        ILog log, bool safeMode, ProcessIdentity? self = null, Func<ProcessIdentity, bool>? isRunning = null)
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
        _root = Path.Combine(paths.LocalDirectory, "recovery");
        InstanceDirectory = Path.Combine(_root, _self.InstanceId);

        documents.DocumentOpened += (_, e) => e.Document.PropertyChanged += OnDocumentPropertyChanged;
        documents.DocumentClosed += (_, e) =>
        {
            e.Document.PropertyChanged -= OnDocumentPropertyChanged;
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
        _timer?.Dispose();
        _timer = null;
        _copied.Clear();
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
            var state = new CopyState(document.Buffer.Version, document.Encoding, document.LineEnding.Kind, document.FilePath);
            if (_copied.TryGetValue(document, out var last) && last == state)
                continue;
            _copied[document] = state;
            var name = document.Id.Value.ToString("D");
            var text = FileEncodings.Encode(document.Buffer.Text, FileEncoding.Utf8Bom);
            var metadata = JsonSerializer.SerializeToUtf8Bytes(new CopyMetadata(1, document.FilePath, document.Title,
                FileEncodings.ToId(document.Encoding), LineEndingId(document.LineEnding.Kind), _clock.Now.UtcDateTime,
                document.FilePath is null ? null : document.DiskStamp?.LastWriteUtc), JsonOptions);
            Enqueue(() =>
            {
                EnsureInstanceDirectory();
                AtomicFileWriter.Write(_fileSystem, Path.Combine(InstanceDirectory, name + TextExtension), text);
                AtomicFileWriter.Write(_fileSystem, Path.Combine(InstanceDirectory, name + MetadataExtension), metadata);
            });
        }
    }

    private void DeleteCopy(DocumentViewModel document)
    {
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
                    copies.Add(new RecoveredCopy(metadata, text, fileExists));
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
    /// file (any more), with the <c>Msg.File.Recovered</c> bar.
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

                    ShowRecoveredBar(document, metadata.SavedUtc);
                }

                return;
            }
        }

        var title = string.IsNullOrWhiteSpace(metadata.Title) ? "Unit1.pas" : Path.GetFileName(metadata.Title);
        var untitled = _documents.OpenUntitled(title, copy.Text, FileEncodings.FromId(metadata.Encoding ?? "") ?? FileEncoding.Utf8,
            metadata.LineEnding == "lf" ? LineEndingKind.Lf : LineEndingKind.CrLf);
        ShowRecoveredBar(untitled, metadata.SavedUtc);
    }

    private void ShowRecoveredBar(TextDocumentViewModel document, DateTime savedUtc)
    {
        Task Keep()
        {
            document.RemoveInfoBar(TextDocumentViewModel.RecoveredInfoBarKind); // stays modified until saved
            return Task.CompletedTask;
        }

        var when = DateTime.SpecifyKind(savedUtc, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        document.ShowInfoBar(new InfoBarViewModel(TextDocumentViewModel.RecoveredInfoBarKind, InfoBarSeverity.Info,
            string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_Recovered, when),
            [
                new InfoBarAction("KeepRecovered", Strings.Button_KeepRecovered, Keep),
                new InfoBarAction("DiscardAndReload", Strings.Button_DiscardAndReload, () => DiscardAsync(document)),
            ],
            _ => Keep()));
    }

    /// <summary>Discard and Reload from Disk: back to the file's content, or the untitled document closes.</summary>
    private async Task DiscardAsync(TextDocumentViewModel document)
    {
        if (document.FilePath is not { } path)
        {
            _documents.Close(document);
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

        document.Reload(FileEncodings.Decode(read.Bytes, document.Encoding)); // removes the bar; unmodified deletes the copy
        document.DiskStamp = read.Stamp;
    }

    private static string LineEndingId(LineEndingKind kind) => kind switch
    {
        LineEndingKind.Lf => "lf",
        LineEndingKind.Mixed => "mixed",
        _ => "crlf",
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly record struct CopyState(long Version, FileEncoding Encoding, LineEndingKind LineEnding, string? Path);

    /// <summary>IDE-P1-121 <c>&lt;docGuid&gt;.json</c>.</summary>
    private sealed record CopyMetadata(int FormatVersion, string? Path, string? Title, string? Encoding, string? LineEnding,
        DateTime SavedUtc, DateTime? BaseTimestampUtc);

    private sealed record OwnerFile(int FormatVersion, int ProcessId, long StartTimeTicks);

    private sealed record RecoveredCopy(CopyMetadata Metadata, string Text, bool FileExists);

    private sealed record OrphanFolder(string Path, IReadOnlyList<RecoveredCopy> Copies, bool Complete);
}
