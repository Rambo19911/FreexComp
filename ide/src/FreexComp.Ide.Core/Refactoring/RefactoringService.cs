using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.CodeModel;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Refactoring;

/// <inheritdoc cref="IRefactoringService"/>
public sealed class RefactoringService : IRefactoringService
{
    private const string LogCategory = "Refactoring";
    private const int MaxBlockedLines = 10; // IDE-REFAC-006
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"
    private static readonly TimeSpan WorkingDelay = TimeSpan.FromMilliseconds(400); // IDE-REFAC-012

    private readonly IDocumentService _documents;
    private readonly IProjectService _projects;
    private readonly ILibrarySourceService _library;
    private readonly IFileSystem _fileSystem;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly IClock _clock;
    private readonly ILog _log;
    private readonly CodeModelService _codeModel;

    public RefactoringService(IDocumentService documents, IProjectService projects, ILibrarySourceService library, IFileSystem fileSystem,
        IDialogService dialogs, IStatusService status, IClock clock, ILog log, CodeModelService codeModel)
    {
        _documents = documents;
        _projects = projects;
        _library = library;
        _fileSystem = fileSystem;
        _dialogs = dialogs;
        _status = status;
        _clock = clock;
        _log = log;
        _codeModel = codeModel;
    }

    public bool IsBusy { get; private set; }

    public event EventHandler? Changed;

    /// <summary>
    /// The preview of IDE-REFAC-007: shows the changes of a checked edit and returns the edit reduced to the chosen ones,
    /// or null when the user cancelled. Null until the Preview dialog exists: <see cref="PreviewMode.Auto"/> and
    /// <see cref="PreviewMode.Always"/> then apply without a dialog.
    /// </summary>
    internal Func<WorkspaceEdit, Task<WorkspaceEdit?>>? Preview { get; set; }

    public Task<RefactoringResult> ApplyAsync(WorkspaceEdit edit, PreviewMode preview = PreviewMode.Auto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (IsBusy)
            throw new InvalidOperationException("A refactoring operation is running.");
        return RunAsync(edit, preview, ct);
    }

    /// <summary>One operation from its start to its result: busy state and the "working" status text around it.</summary>
    private async Task<RefactoringResult> RunAsync(WorkspaceEdit edit, PreviewMode preview, CancellationToken ct)
    {
        var working = new WorkingStatus(_clock, _status, string.Format(CultureInfo.CurrentCulture, Strings.Msg_Refactoring_Working, edit.Title));
        SetBusy(true);
        try
        {
            return await ApplyCoreAsync(edit, preview, working, ct);
        }
        finally
        {
            working.Stop();
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<RefactoringResult> ApplyCoreAsync(WorkspaceEdit edit, PreviewMode preview, WorkingStatus working, CancellationToken ct)
    {
        working.Start();
        var (prepared, stop) = await PrepareAsync(edit, working);
        if (stop is not null)
            return stop;

        // IDE-REFAC-006: the checks run before a preview is shown — a blocked operation never shows one.
        var plan = await CheckAsync(prepared);
        if (plan.Blocked.Count > 0)
            return await BlockedAsync(prepared.Title, plan.Blocked, working);

        if (Preview is { } show && NeedsPreview(preview, plan))
        {
            working.Stop();
            var chosen = await show(prepared);
            if (chosen is null)
                return Result(RefactoringOutcome.Canceled);
            working.Start();
            (prepared, stop) = await PrepareAsync(chosen, working);
            if (stop is not null)
                return stop;
        }

        if (ct.IsCancellationRequested)
            return Result(RefactoringOutcome.Canceled);

        // … and again immediately before the first write; that check records the version of every buffer it judged:
        // (1) of IDE-REFAC-008.
        plan = await CheckAsync(prepared);
        if (plan.Blocked.Count > 0)
            return await BlockedAsync(prepared.Title, plan.Blocked, working);
        var buffers = plan.Targets.Where(t => t.Document is not null).ToList();
        var files = plan.Targets.Where(t => t.Document is null).ToList();

        // (2) The closed files, on the thread pool, in path order; a failed write puts back what was written.
        var written = await Task.Run(() => WriteFiles(files));
        if (written.FailedPath is { } failedPath)
        {
            working.Stop();
            await ShowWriteFailureAsync(prepared.Title, "Msg.Refactoring.ApplyFailed", [failedPath, written.Error!], written.Unrestored);
            return Result(RefactoringOutcome.Failed, error: written.Error);
        }

        // (3) One block on the UI thread: every buffer is still open and has its recorded version, then the edits.
        var changed = buffers.Where(b => !_documents.Documents.Contains(b.Document!) || b.Document!.Buffer.Version != b.Version)
            .Select(b => new BlockedFile(b.Edit.Path, BlockReason.ChangedSinceComputed)).ToList();
        if (changed.Count > 0)
        {
            var unrestored = await Task.Run(() => Restore(written.Written));
            if (unrestored.Count == 0)
                return await BlockedAsync(prepared.Title, changed, working);
            working.Stop();
            await ShowWriteFailureAsync(prepared.Title, null, [], unrestored);
            return Result(RefactoringOutcome.Blocked, blocked: changed, error: unrestored[0].Error);
        }

        var edited = new List<Target>();
        try
        {
            foreach (var buffer in buffers)
            {
                edited.Add(buffer);
                buffer.Document!.Buffer.ApplyEdits(buffer.Edit.Edits, prepared.Title); // IDE-REFAC-010: one undo step, not saved
            }
        }
        catch (Exception e)
        {
            // Not expected (the edit is valid and the buffers were checked): the undo steps made so far are undone and
            // the files put back; reported as an edit that cannot be applied (IDE-REFAC-002).
            _log.Error(LogCategory, $"{prepared.Title}: editing {edited[^1].Edit.Path} failed", e);
            foreach (var target in Enumerable.Reverse(edited))
                UndoQuietly(target.Document!.Buffer, target.Version);
            var unrestored = await Task.Run(() => Restore(written.Written));
            working.Stop();
            await ShowWriteFailureAsync(prepared.Title, "Msg.Refactoring.Failed", [], unrestored);
            return Result(RefactoringOutcome.Failed, error: e.Message);
        }

        // (4) The code model reads the written files again; status.
        foreach (var file in written.Written)
            _codeModel.Workspace.NotifyFileChangedOnDisk(file.Path);
        var editCount = prepared.Files.Sum(f => f.Edits.Length);
        var fileCount = prepared.Files.Length;
        working.Stop();
        _status.ShowMessage(
            editCount == 1 ? Format(Strings.Msg_Refactoring_AppliedOne, prepared.Title)
            : fileCount == 1 ? Format(Strings.Msg_Refactoring_AppliedOneFile, prepared.Title, editCount)
            : Format(Strings.Msg_Refactoring_AppliedMany, prepared.Title, editCount, fileCount),
            StatusDuration);
        return new RefactoringResult(RefactoringOutcome.Applied, editCount, fileCount, [], null);
    }

    private static RefactoringResult Result(RefactoringOutcome outcome, IReadOnlyList<BlockedFile>? blocked = null, string? error = null) =>
        new(outcome, 0, 0, blocked ?? [], error);

    private static string Format(string resource, params object[] args) => string.Format(CultureInfo.CurrentCulture, resource, args);

    /// <summary>
    /// IDE-REFAC-001, 002: the normalised edit. <c>Stop</c> ends the operation: nothing is left to change (said in the
    /// status bar), or the edit is invalid (logged once and reported).
    /// </summary>
    private async Task<(WorkspaceEdit Edit, RefactoringResult? Stop)> PrepareAsync(WorkspaceEdit edit, WorkingStatus working)
    {
        var normalized = edit.Normalize();
        if (normalized.Files.IsEmpty)
        {
            working.Stop();
            _status.ShowMessage(Format(Strings.Msg_Refactoring_NoChanges, edit.Title), StatusDuration);
            return (normalized, Result(RefactoringOutcome.NoChanges));
        }

        if (normalized.FindDefect() is not { } defect)
            return (normalized, null);
        var error = $"{edit.Title}: invalid workspace edit, {defect.Path}: {defect.Problem}";
        _log.Error(LogCategory, error);
        working.Stop();
        await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Refactoring.Failed", [edit.Title]));
        return (normalized, Result(RefactoringOutcome.Failed, error: error));
    }

    /// <summary>What a file edit changes at apply time (IDE-REFAC-004): the buffer of an open text document, else the file.</summary>
    private sealed record Target(FileEdit Edit, TextDocumentViewModel? Document)
    {
        /// <summary>A file target as read by the checks: null when the file does not exist.</summary>
        public FileState? File { get; init; }

        /// <summary>A buffer target: the buffer's version when the checks judged it (IDE-REFAC-008 (1)).</summary>
        public long Version { get; init; }
    }

    /// <summary>A closed file as found on disk: its bytes (read once, IDE-REFAC-009) and whether it can be written.</summary>
    private sealed record FileState(byte[]? Bytes, EncodingDetection? Detection, bool Writable);

    private sealed record Plan(IReadOnlyList<Target> Targets, IReadOnlyList<BlockedFile> Blocked);

    private TextDocumentViewModel? DocumentOf(string path) => _documents.Documents.OfType<TextDocumentViewModel>()
        .FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// IDE-REFAC-004, 006: the target of every file edit, in path order, and the files that block with the first reason
    /// that applies. The closed files are read on the thread pool; the targets are decided, and everything is judged,
    /// on the UI thread after that read, in one block (a document opened or closed during the read repeats it).
    /// </summary>
    private async Task<Plan> CheckAsync(WorkspaceEdit edit)
    {
        var ordered = edit.Files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
        while (true)
        {
            var targets = ordered.Select(f => new Target(f, DocumentOf(f.Path))).ToList();
            var closed = targets.Where(t => t.Document is null).Select(t => t.Edit.Path).ToList();
            var states = closed.Count == 0 ? [] : await Task.Run(() => closed.Select(ReadState).ToList()); // IDE-ARCH-081
            if (!targets.Select(t => t.Document).SequenceEqual(ordered.Select(f => DocumentOf(f.Path))))
                continue;
            var blocked = new List<BlockedFile>();
            for (int i = 0, read = 0; i < targets.Count; i++)
            {
                targets[i] = targets[i].Document is { } document
                    ? targets[i] with { Version = document.Buffer.Version }
                    : targets[i] with { File = states[read++] };
                if (ReasonFor(targets[i]) is { } reason)
                    blocked.Add(new BlockedFile(targets[i].Edit.Path, reason));
            }

            return new Plan(targets, blocked);
        }
    }

    private FileState? ReadState(string path)
    {
        try
        {
            if (!_fileSystem.FileExists(path))
                return null;
            var writable = !_fileSystem.IsReadOnly(path) && _fileSystem.CanOpenForWrite(path);
            var bytes = _fileSystem.ReadAllBytes(path);
            return new FileState(bytes, EncodingDetector.Detect(bytes), writable);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FileState(null, null, false); // it exists and cannot be read: in use
        }
    }

    /// <summary>IDE-REFAC-006: the first reason that applies to the file, in the order of the rule; null when it may be edited.</summary>
    private BlockReason? ReasonFor(Target target)
    {
        var (path, document, file) = (target.Edit.Path, target.Document, target.File);
        if (document is null && file is null)
            return BlockReason.NotFound;
        if (_library.IsLibrarySource(path) && !_projects.IsMember(path))
            return BlockReason.LibrarySource;
        if (!EditableFiles.MayEdit(path, _projects, _library.IsLibrarySource, _documents))
            return BlockReason.OutsideProject;
        if (document is not null)
        {
            if (document.Buffer.IsReadOnly)
                return BlockReason.NotWritable;
            if (document.InfoBars.Any(bar => bar.Kind is TextDocumentViewModel.ExternalChangeInfoBarKind or TextDocumentViewModel.ExternalDeleteInfoBarKind))
                return BlockReason.ChangedOnDisk;
            return document.Buffer.Text == target.Edit.BaseText ? null : BlockReason.ChangedSinceComputed;
        }

        if (!file!.Writable)
            return BlockReason.NotWritable;
        return file.Detection is { IsBinary: false } detection && detection.Text == target.Edit.BaseText ? null : BlockReason.ChangedOnDisk;
    }

    /// <summary>
    /// IDE-REFAC-006: nothing changed; <c>Msg.Refactoring.Blocked</c> with one line per file in path order, at most ten,
    /// then the number of the others.
    /// </summary>
    private async Task<RefactoringResult> BlockedAsync(string title, IReadOnlyList<BlockedFile> blocked, WorkingStatus working)
    {
        working.Stop();
        var lines = blocked.Take(MaxBlockedLines).Select(b => $"{b.Path} — {ReasonText(b.Reason)}").ToList();
        if (blocked.Count > MaxBlockedLines)
            lines.Add(Format(Strings.Reason_Refactoring_More, blocked.Count - MaxBlockedLines));
        await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Refactoring.Blocked", [title, string.Join('\n', lines)]));
        return Result(RefactoringOutcome.Blocked, blocked: blocked);
    }

    private static string ReasonText(BlockReason reason) => reason switch
    {
        BlockReason.NotFound => Strings.Reason_Refactoring_NotFound,
        BlockReason.LibrarySource => Strings.Reason_Refactoring_LibrarySource,
        BlockReason.OutsideProject => Strings.Reason_Refactoring_OutsideProject,
        BlockReason.NotWritable => Strings.Reason_Refactoring_NotWritable,
        BlockReason.ChangedOnDisk => Strings.Reason_Refactoring_ChangedOnDisk,
        BlockReason.ChangedSinceComputed => Strings.Reason_Refactoring_ChangedSinceComputed,
        BlockReason.ChangedAfterwards => Strings.Reason_Refactoring_ChangedAfterwards,
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>A written file with the bytes it had before (kept for the rollback).</summary>
    private sealed record WrittenFile(string Path, byte[] Original);

    /// <summary>What step (2) did: the files written; after a failure the file that failed, the system's reason and the files that could not be put back.</summary>
    private sealed record WriteResult(IReadOnlyList<WrittenFile> Written, string? FailedPath, string? Error,
        IReadOnlyList<(string Path, string Error)> Unrestored);

    /// <summary>
    /// IDE-REFAC-008 (2), IDE-REFAC-009: the new bytes of every file — the edits applied to the decoded text, encoded as
    /// the file was — then the writes through the atomic writer. The first failure stops and restores. Thread pool.
    /// </summary>
    private WriteResult WriteFiles(IReadOnlyList<Target> files)
    {
        var writes = new List<(string Path, byte[] Original, byte[] New)>();
        foreach (var target in files)
        {
            var detection = target.File!.Detection!;
            try
            {
                writes.Add((target.Edit.Path, target.File.Bytes!, FileEncodings.Encode(Apply(detection.Text, target.Edit.Edits), detection.Encoding)));
            }
            catch (EncoderFallbackException e)
            {
                return new WriteResult([], target.Edit.Path, e.Message, []); // an inserted character the file's encoding cannot store
            }
        }

        var written = new List<WrittenFile>();
        foreach (var (path, original, bytes) in writes)
        {
            try
            {
                AtomicFileWriter.Write(_fileSystem, path, bytes);
                written.Add(new WrittenFile(path, original));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new WriteResult([], path, e.Message, Restore(written));
            }
        }

        return new WriteResult(written, null, null, []);
    }

    /// <summary>The text with the edits applied by descending offset (they do not overlap, IDE-REFAC-002).</summary>
    private static string Apply(string text, IEnumerable<TextEdit> edits)
    {
        var builder = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e => e.Offset).ThenByDescending(e => e.Length))
            builder.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.NewText);
        return builder.ToString();
    }

    /// <summary>
    /// IDE-REFAC-008: the written files get their original bytes back, in reverse order, through the same writer.
    /// Returns the files that could not be restored (logged). Thread pool.
    /// </summary>
    private List<(string Path, string Error)> Restore(IReadOnlyList<WrittenFile> written)
    {
        var failed = new List<(string Path, string Error)>();
        foreach (var file in written.Reverse())
        {
            try
            {
                AtomicFileWriter.Write(_fileSystem, file.Path, file.Original);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log.Warning(LogCategory, $"{file.Path}: the previous content cannot be put back", e);
                failed.Add((file.Path, e.Message));
            }
        }

        return failed;
    }

    /// <summary>
    /// The message of a failed apply: <paramref name="messageId"/> with the title and <paramref name="args"/> when every
    /// written file was put back (none when it is null: the caller reports), else <c>Msg.Refactoring.RestoreFailed</c>
    /// naming the files that were not, with the system's reason of the first of them.
    /// </summary>
    private async Task ShowWriteFailureAsync(string title, string? messageId, IReadOnlyList<string> args, IReadOnlyList<(string Path, string Error)> unrestored)
    {
        if (unrestored.Count > 0)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Refactoring.RestoreFailed",
                [title, string.Join('\n', unrestored.Select(u => u.Path)), unrestored[0].Error]));
        }
        else if (messageId is not null)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest(messageId, [title, .. args]));
        }
    }

    /// <summary>Takes back the undo step an operation made in <paramref name="buffer"/>, if it made one; never throws.</summary>
    private void UndoQuietly(ITextBuffer buffer, long versionBefore)
    {
        try
        {
            if (buffer.Version != versionBefore && buffer.CanUndo)
                buffer.Undo();
        }
        catch (Exception e)
        {
            _log.Warning(LogCategory, "A buffer edit cannot be undone", e);
        }
    }

    /// <summary>
    /// IDE-REFAC-007: <see cref="PreviewMode.Auto"/> shows the preview unless the edit changes exactly one file and that
    /// file is the buffer of the active document's visible text view.
    /// </summary>
    private bool NeedsPreview(PreviewMode mode, Plan plan) => mode switch
    {
        PreviewMode.Always => true,
        PreviewMode.Never => false,
        _ => !(plan.Targets is [{ Document: { } document }] && ReferenceEquals(document, _documents.ActiveDocument?.VisibleText)),
    };

    /// <summary>
    /// IDE-REFAC-012: after 400 ms of work the status bar names the operation (sticky) until it stops — at the result, and
    /// while a dialog of the operation is open.
    /// </summary>
    private sealed class WorkingStatus(IClock clock, IStatusService status, string text)
    {
        private IDisposable? _timer;
        private IDisposable? _sticky;

        public void Start()
        {
            Stop();
            _timer = clock.Schedule(WorkingDelay, () => _sticky = status.ShowStickyMessage(text));
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            _sticky?.Dispose();
            _sticky = null;
        }
    }
}
