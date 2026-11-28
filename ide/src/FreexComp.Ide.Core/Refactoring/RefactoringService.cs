using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.CodeModel;
using FreexComp.Ide.Core.Commands;
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
public sealed partial class RefactoringService : IRefactoringService
{
    /// <summary>The command that takes the newest operation back (IDE-REFAC-021).</summary>
    public const string UndoCommandId = "refactor.undo";

    /// <summary>IDE-REFAC-020: the history keeps at most this many operations.</summary>
    internal const int MaxHistoryOperations = 20;

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
    private readonly List<Operation> _history = []; // IDE-REFAC-020: the applied operations, oldest first
    private string? _historyProject; // the project file the history belongs to; null without a project

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
        _historyProject = projects.Project?.FilePath;
        projects.ProjectChanged += (_, _) => OnProjectChanged();
        // IDE-REFAC-024: edit.undo of a text document (a form document's unit included) asks here first.
        // IDE-REFAC-035: its caret line shows the quick-action glyph.
        documents.DocumentOpened += (_, e) =>
        {
            if (e.Document is not TextDocumentViewModel text)
                return;
            text.OperationUndo = UndoFromEditor;
            AttachGlyph(text);
        };
        // IDE-REFAC-032: closing a document cancels the collection of its quick actions.
        documents.DocumentClosed += (_, e) =>
        {
            if (e.Document is not TextDocumentViewModel text)
                return;
            DetachGlyph(text);
            if (_collecting.Remove(text, out var request))
                request.Cancel();
        };
        documents.ActiveDocumentChanged += (_, _) => OnActiveDocumentChanged();
    }

    public bool IsBusy { get; private set; }

    public event EventHandler? Changed;

    /// <summary>IDE-REFAC-020: the history keeps at most this many characters of before and after texts (the newest operation always stays).</summary>
    internal long MaxHistoryCharacters { get; set; } = 64_000_000;

    /// <summary>The number of operations on the history.</summary>
    internal int HistoryCount => _history.Count;

    public bool CanUndo => _history.Count > 0;

    public string? UndoTitle => _history.Count > 0 ? _history[^1].Title : null;

    /// <summary>
    /// Gives the engine the command service, while the IDE is composed. Where they are available in a composition of
    /// <paramref name="implementedPhase"/> it registers <c>refactor.undo</c> (IDE-REFAC-021: enabled while the history is
    /// not empty and no operation runs; its main-menu label names the operation it takes back) and
    /// <c>edit.quickActions</c> (IDE-REFAC-034); a refactoring registered afterwards gets its command here (IDE-REFAC-030).
    /// </summary>
    public void RegisterCommands(ICommandService commands, int implementedPhase)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        _implementedPhase = implementedPhase;
        if (IsAvailable(UndoCommandId))
            commands.RegisterHandler(UndoCommandId, new UndoHandler(this));
        if (IsAvailable(QuickActionsCommandId))
            commands.RegisterHandler(QuickActionsCommandId, new QuickActionsHandler(this));
        Changed += (_, _) => commands.InvalidateCanExecute();
    }

    private sealed class UndoHandler(RefactoringService owner) : ICommandHandler
    {
        public bool CanExecute(string? argument) => owner.CanUndo && !owner.IsBusy;

        public Task ExecuteAsync(string? argument) => CanExecute(argument) ? owner.UndoAsync() : Task.CompletedTask;

        /// <summary><c>&amp;Undo &lt;Title&gt;</c>: the mnemonic stays U; an ampersand of the title would be read as a second mnemonic and is left out.</summary>
        public string? MenuLabelOverride =>
            owner.UndoTitle is { } title ? "&" + UndoTitleOf(title.Replace("&", "", StringComparison.Ordinal)) : null;
    }

    /// <summary>The name of the undo of the operation <paramref name="title"/>: the menu label and the title of the undo step it makes.</summary>
    private static string UndoTitleOf(string title) => Format(Strings.Undo_Refactoring, title);

    /// <summary>IDE-REFAC-020: the history belongs to the open project; it is emptied when that project closes or another one is loaded.</summary>
    private void OnProjectChanged()
    {
        var project = _projects.Project?.FilePath;
        if (string.Equals(project, _historyProject, StringComparison.OrdinalIgnoreCase))
            return;
        _historyProject = project;
        if (_history.Count == 0)
            return;
        _history.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The preview of IDE-REFAC-007: shows the changes of a checked edit and returns the edit reduced to the chosen ones,
    /// or null when the user cancelled. The service graph sets it to the dialog <c>RefactoringPreview</c> (UI-DLG-360…367).
    /// Null: <see cref="PreviewMode.Auto"/> and <see cref="PreviewMode.Always"/> apply without a preview.
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
        var project = _historyProject;
        working.Start();
        var (prepared, stop) = await PrepareAsync(edit, working);
        if (stop is not null)
            return stop;

        // IDE-REFAC-006: the checks run before a preview is shown — a blocked operation never shows one.
        var plan = await JudgeAsync(prepared);
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
        plan = await JudgeAsync(prepared);
        if (plan.Blocked.Count > 0)
            return await BlockedAsync(prepared.Title, plan.Blocked, working);
        var open = plan.Targets.Where(t => !t.IsFile).ToList();
        var files = plan.Targets.Where(t => t.IsFile).ToList();

        // (2) The closed files, on the thread pool, in path order; a failed write puts back what was written.
        var written = await Task.Run(() => WriteFiles(files));
        if (written.FailedPath is { } failedPath)
        {
            working.Stop();
            await ShowWriteFailureAsync(prepared.Title, "Msg.Refactoring.ApplyFailed", [failedPath, written.Error!], written.Unrestored);
            return Result(RefactoringOutcome.Failed, error: written.Error);
        }

        // (3) One block on the UI thread: every document is still open, every buffer has its recorded version and every
        // form its recorded text; then the edits, in path order.
        var changed = open.Where(IsChanged).Select(t => new BlockedFile(t.Edit.Path, BlockReason.ChangedSinceComputed)).ToList();
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
        var records = new List<FileRecord>(); // IDE-REFAC-020: what the operation did, file by file
        try
        {
            foreach (var target in open)
            {
                edited.Add(target);
                if (target.Form is { } form)
                {
                    form.Designer.RevertFormText(Apply(target.Edit.BaseText, target.Edit.Edits), prepared.Title); // IDE-REFAC-011: through the designer, not saved
                    // The after text is the designer's current form text: what a later undo compares with (IDE-REFAC-004).
                    records.Add(new FileRecord(target.Edit.Path, TargetKind.FormText, target.FormText!, form.Designer.CurrentFormText()));
                }
                else
                {
                    var buffer = target.Document!.Buffer;
                    buffer.ApplyEdits(target.Edit.Edits, prepared.Title); // IDE-REFAC-010: one undo step, not saved
                    records.Add(new FileRecord(target.Edit.Path, TargetKind.Buffer, target.Edit.BaseText, buffer.Text) { Buffer = buffer, Version = buffer.Version });
                }
            }
        }
        catch (Exception e)
        {
            // Not expected (the edit is valid and the documents were checked): the undo steps made so far are undone, a
            // form gets back the text it had, and the files are put back; reported as an edit that cannot be applied
            // (IDE-REFAC-002).
            _log.Error(LogCategory, $"{prepared.Title}: editing {edited[^1].Edit.Path} failed", e);
            foreach (var target in Enumerable.Reverse(edited))
            {
                if (target.Form is { } form)
                    RevertQuietly(form, target.FormText!, prepared.Title);
                else
                    UndoQuietly(target.Document!.Buffer, target.Version);
            }
            var unrestored = await Task.Run(() => Restore(written.Written));
            working.Stop();
            await ShowWriteFailureAsync(prepared.Title, "Msg.Refactoring.Failed", [], unrestored);
            return Result(RefactoringOutcome.Failed, error: e.Message);
        }

        // (4) The code model reads the written files again; the operation goes on the history; status.
        foreach (var file in written.Written)
            _codeModel.Workspace.NotifyFileChangedOnDisk(file.Path);
        records.AddRange(files.Zip(written.Written, (target, file) => new FileRecord(file.Path, TargetKind.File, target.File!.Detection!.Text, file.Text)
        {
            OriginalBytes = file.Original,
            WrittenHash = SHA256.HashData(file.New),
        }));
        if (string.Equals(project, _historyProject, StringComparison.OrdinalIgnoreCase)) // not when the project was closed meanwhile
            Record(new Operation(prepared.Title, _clock.Now, [.. records.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase)]));
        var editCount = prepared.Files.Sum(f => f.Edits.Length);
        var fileCount = prepared.Files.Length;
        working.Stop();
        _status.ShowMessage(
            editCount == 1 ? Format(Strings.Msg_Refactoring_AppliedOne, prepared.Title)
            : fileCount == 1 ? Format(Strings.Msg_Refactoring_AppliedOneFile, prepared.Title, editCount)
            : Format(Strings.Msg_Refactoring_AppliedMany, prepared.Title, editCount, fileCount),
            StatusDuration);
        var result = new RefactoringResult(RefactoringOutcome.Applied, editCount, fileCount, [], null);
        try
        {
            prepared.OnApplied?.Invoke(result); // IDE-REFAC-047 b: after step (4), for this outcome only
        }
        catch (Exception e)
        {
            _log.Warning(LogCategory, $"{prepared.Title}: the callback of the applied edit failed: {e.Message}", e);
        }

        return result;
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
        var error = LogDefect(edit.Title, defect);
        working.Stop();
        await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Refactoring.Failed", [edit.Title]));
        return (normalized, Result(RefactoringOutcome.Failed, error: error));
    }

    /// <summary>IDE-REFAC-002: an invalid edit is a defect of its producer — logged once, as an error; returns the text.</summary>
    private string LogDefect(string title, (string Path, string Problem) defect)
    {
        var error = $"{title}: invalid workspace edit, {defect.Path}: {defect.Problem}";
        _log.Error(LogCategory, error);
        return error;
    }

    public async Task<IReadOnlyList<BlockedFile>> CheckAsync(WorkspaceEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return (await JudgeAsync(edit.Normalize())).Blocked;
    }

    /// <summary>
    /// What a file edit changes at apply time (IDE-REFAC-004): the form text of an open form document
    /// (<paramref name="Form"/>), the buffer of an open text document (<paramref name="Document"/>), else the file.
    /// </summary>
    private sealed record Target(FileEdit Edit, TextDocumentViewModel? Document, FormDocumentViewModel? Form)
    {
        /// <summary>Neither a buffer nor a form text: the file on disk is rewritten.</summary>
        public bool IsFile => Document is null && Form is null;

        /// <summary>A file target as read by the checks: null when the file does not exist.</summary>
        public FileState? File { get; init; }

        /// <summary>A buffer target: the buffer's version when the checks judged it (IDE-REFAC-008 (1)).</summary>
        public long Version { get; init; }

        /// <summary>A form-text target: the designer's current form text when the checks judged it (IDE-REFAC-008 (1)).</summary>
        public string? FormText { get; init; }
    }

    /// <summary>A closed file as found on disk: its bytes (read once, IDE-REFAC-009) and whether it can be written.</summary>
    private sealed record FileState(byte[]? Bytes, EncodingDetection? Detection, bool Writable);

    private sealed record Plan(IReadOnlyList<Target> Targets, IReadOnlyList<BlockedFile> Blocked);

    /// <summary>
    /// IDE-REFAC-004: the open document a path belongs to. The <c>.form</c> of an open form document is looked for first:
    /// a form opened without its unit is a document with the form's own path and an empty placeholder buffer.
    /// </summary>
    private (TextDocumentViewModel? Document, FormDocumentViewModel? Form) DocumentOf(string path)
    {
        if (_documents.Documents.OfType<FormDocumentViewModel>()
            .FirstOrDefault(d => string.Equals(d.FormText.FilePath, path, StringComparison.OrdinalIgnoreCase)) is { } form)
            return (null, form);
        return (_documents.Documents.OfType<TextDocumentViewModel>()
            .FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase)), null);
    }

    private Target TargetOf(FileEdit edit)
    {
        var (document, form) = DocumentOf(edit.Path);
        return new Target(edit, document, form);
    }

    /// <summary>
    /// IDE-REFAC-008 (3): the document was closed, its buffer has another version, or the form another text, than when the
    /// checks judged it.
    /// </summary>
    private bool IsChanged(Target target) => target.Form is { } form
        ? !_documents.Documents.Contains(form) || !string.Equals(form.Designer.CurrentFormText(), target.FormText, StringComparison.Ordinal)
        : !_documents.Documents.Contains(target.Document!) || target.Document!.Buffer.Version != target.Version;

    /// <summary>
    /// IDE-REFAC-004, 006: the target of every file edit, in path order, and the files that block with the first reason
    /// that applies. The closed files are read on the thread pool; the targets are decided, and everything is judged,
    /// on the UI thread after that read, in one block (a document opened or closed during the read repeats it).
    /// </summary>
    private async Task<Plan> JudgeAsync(WorkspaceEdit edit)
    {
        var ordered = edit.Files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
        while (true)
        {
            var targets = ordered.Select(TargetOf).ToList();
            var closed = targets.Where(t => t.IsFile).Select(t => t.Edit.Path).ToList();
            var states = closed.Count == 0 ? [] : await Task.Run(() => closed.Select(ReadState).ToList()); // IDE-ARCH-081
            if (!targets.Select(t => (t.Document, t.Form)).SequenceEqual(ordered.Select(f => DocumentOf(f.Path))))
                continue;
            var blocked = new List<BlockedFile>();
            for (int i = 0, read = 0; i < targets.Count; i++)
            {
                targets[i] = targets[i] switch
                {
                    { Form: { } form } => targets[i] with { FormText = form.Designer.CurrentFormText() },
                    { Document: { } document } => targets[i] with { Version = document.Buffer.Version },
                    _ => targets[i] with { File = states[read++] },
                };
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
        if (target.IsFile && file is null)
            return BlockReason.NotFound;
        if (_library.IsLibrarySource(path) && !_projects.IsMember(path))
            return BlockReason.LibrarySource;
        if (!EditableFiles.MayEdit(path, _projects, _library.IsLibrarySource, _documents))
            return BlockReason.OutsideProject;
        if (target.Form is { } form)
        {
            // The form document is read only as a whole (its unit cannot be written, a library source), or its form buffer is.
            if (form.IsReadOnly || form.FormText.IsReadOnly)
                return BlockReason.NotWritable;
            if (ShowsExternalChange(form)) // one bar per document, whichever of its two files changed
                return BlockReason.ChangedOnDisk;
            return target.FormText == target.Edit.BaseText ? null : BlockReason.ChangedSinceComputed;
        }

        if (document is not null)
        {
            if (document.Buffer.IsReadOnly)
                return BlockReason.NotWritable;
            if (ShowsExternalChange(document))
                return BlockReason.ChangedOnDisk;
            return document.Buffer.Text == target.Edit.BaseText ? null : BlockReason.ChangedSinceComputed;
        }

        if (!file!.Writable)
            return BlockReason.NotWritable;
        return file.Detection is { IsBinary: false } detection && detection.Text == target.Edit.BaseText ? null : BlockReason.ChangedOnDisk;
    }

    private static bool ShowsExternalChange(TextDocumentViewModel document) => document.InfoBars
        .Any(bar => bar.Kind is TextDocumentViewModel.ExternalChangeInfoBarKind or TextDocumentViewModel.ExternalDeleteInfoBarKind);

    /// <summary>
    /// IDE-REFAC-006: nothing changed; <c>Msg.Refactoring.Blocked</c> with one line per file in path order, at most ten,
    /// then the number of the others.
    /// </summary>
    private async Task<RefactoringResult> BlockedAsync(string title, IReadOnlyList<BlockedFile> blocked, WorkingStatus working)
    {
        working.Stop();
        await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Refactoring.Blocked", [title, BlockedLines(blocked)]));
        return Result(RefactoringOutcome.Blocked, blocked: blocked);
    }

    /// <summary>IDE-REFAC-006: one line <c>&lt;full path&gt; — &lt;reason&gt;</c> per file, at most ten, then the number of the others.</summary>
    private static string BlockedLines(IReadOnlyList<BlockedFile> blocked)
    {
        var lines = blocked.Take(MaxBlockedLines).Select(b => $"{b.Path} — {ReasonText(b.Reason)}").ToList();
        if (blocked.Count > MaxBlockedLines)
            lines.Add(Format(Strings.Reason_Refactoring_More, blocked.Count - MaxBlockedLines));
        return string.Join('\n', lines);
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

    /// <summary>
    /// One file to write: the bytes it has (kept for the rollback, and for the undo), the bytes it gets and the text those hold.
    /// </summary>
    private sealed record FileWrite(string Path, byte[] Original, byte[] New, string Text);

    /// <summary>What a write step did: the files written; after a failure the file that failed, the system's reason and the files that could not be put back.</summary>
    private sealed record WriteResult(IReadOnlyList<FileWrite> Written, string? FailedPath, string? Error,
        IReadOnlyList<(string Path, string Error)> Unrestored);

    /// <summary>
    /// IDE-REFAC-008 (2), IDE-REFAC-009: the new bytes of every file — the edits applied to the decoded text, encoded as
    /// the file was — then the writes through the atomic writer. The first failure stops and restores. Thread pool.
    /// </summary>
    private WriteResult WriteFiles(IReadOnlyList<Target> files)
    {
        var writes = new List<FileWrite>();
        foreach (var target in files)
        {
            var detection = target.File!.Detection!;
            var text = Apply(detection.Text, target.Edit.Edits);
            try
            {
                writes.Add(new FileWrite(target.Edit.Path, target.File.Bytes!, FileEncodings.Encode(text, detection.Encoding), text));
            }
            catch (EncoderFallbackException e)
            {
                return new WriteResult([], target.Edit.Path, e.Message, []); // an inserted character the file's encoding cannot store
            }
        }

        return Write(writes);
    }

    /// <summary>The writes, in order, through the atomic writer. The first failure stops and puts back what was written. Thread pool.</summary>
    private WriteResult Write(IReadOnlyList<FileWrite> writes)
    {
        var written = new List<FileWrite>();
        foreach (var write in writes)
        {
            try
            {
                AtomicFileWriter.Write(_fileSystem, write.Path, write.New);
                written.Add(write);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new WriteResult([], write.Path, e.Message, Restore(written));
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
    private List<(string Path, string Error)> Restore(IReadOnlyList<FileWrite> written)
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

    /// <summary>
    /// Takes back the undo step an operation made in <paramref name="buffer"/>, if it made one, and leaves nothing of it
    /// to redo; never throws.
    /// </summary>
    private void UndoQuietly(ITextBuffer buffer, long versionBefore)
    {
        try
        {
            if (buffer.Version != versionBefore && buffer.CanUndo)
            {
                buffer.Undo();
                buffer.ClearRedo();
            }
        }
        catch (Exception e)
        {
            _log.Warning(LogCategory, "A buffer edit cannot be undone", e);
        }
    }

    /// <summary>
    /// Gives a form back the text it had before the operation edited it (the designer's own undo stack went with that
    /// edit, IDE-REFAC-011); never throws.
    /// </summary>
    private void RevertQuietly(FormDocumentViewModel form, string textBefore, string title)
    {
        try
        {
            if (!string.Equals(form.Designer.CurrentFormText(), textBefore, StringComparison.Ordinal))
                form.Designer.RevertFormText(textBefore, title);
        }
        catch (Exception e)
        {
            _log.Warning(LogCategory, "A form edit cannot be taken back", e);
        }
    }

    /// <summary>What a file edit changed at apply time (IDE-REFAC-004).</summary>
    private enum TargetKind
    {
        Buffer,
        FormText,
        File,
    }

    /// <summary>
    /// What an applied operation did to one file (IDE-REFAC-020): the text before and after; for a file target the bytes
    /// it had and the SHA-256 of the bytes written; for a buffer target the buffer and its version right after the edit.
    /// </summary>
    private sealed record FileRecord(string Path, TargetKind Kind, string Before, string After)
    {
        public byte[]? OriginalBytes { get; init; }

        public byte[]? WrittenHash { get; init; }

        public ITextBuffer? Buffer { get; init; }

        public long Version { get; init; }
    }

    /// <summary>An applied operation (IDE-REFAC-020): its files in path order.</summary>
    private sealed record Operation(string Title, DateTimeOffset Time, IReadOnlyList<FileRecord> Files)
    {
        /// <summary>The characters of the before and after texts: what the operation costs on the history.</summary>
        public long Characters { get; } = Files.Sum(f => (long)f.Before.Length + f.After.Length);
    }

    /// <summary>IDE-REFAC-020: the operation is the newest of the history; the oldest go while there are too many or they are too large.</summary>
    private void Record(Operation operation)
    {
        _history.Add(operation);
        while (_history.Count > 1 && (_history.Count > MaxHistoryOperations || _history.Sum(o => o.Characters) > MaxHistoryCharacters))
            _history.RemoveAt(0);
    }

    public Task<RefactoringResult> UndoAsync()
    {
        if (IsBusy)
            throw new InvalidOperationException("A refactoring operation is running.");
        if (_history.Count == 0)
            throw new InvalidOperationException("There is no refactoring operation to undo.");
        return UndoNewestAsync(_history[^1]);
    }

    private async Task<RefactoringResult> UndoNewestAsync(Operation operation) => (await RunUndoAsync(operation, fromEditor: false)).Result;

    /// <summary>
    /// IDE-REFAC-024: <c>edit.undo</c> in <paramref name="document"/>. Null — the document's own undo is the answer —
    /// unless its buffer is a buffer target of the newest operation in which nothing happened since; then the whole
    /// operation is undone, and the result says whether the document's own undo is to run instead (the undo was blocked
    /// or failed, nothing changed, and the user chose <c>Undo in This File</c>).
    /// </summary>
    private Task<bool>? UndoFromEditor(TextDocumentViewModel document)
    {
        if (IsBusy || _history.Count == 0)
            return null;
        var operation = _history[^1];
        var buffer = document.Buffer;
        return operation.Files.Any(f => ReferenceEquals(f.Buffer, buffer) && f.Version == buffer.Version)
            ? UndoThisFileOnlyAsync(operation)
            : null;
    }

    private async Task<bool> UndoThisFileOnlyAsync(Operation operation) => (await RunUndoAsync(operation, fromEditor: true)).ThisFileOnly;

    /// <summary>One undo from its start to its result: busy state and the "working" status text around it.</summary>
    private async Task<(RefactoringResult Result, bool ThisFileOnly)> RunUndoAsync(Operation operation, bool fromEditor)
    {
        var working = new WorkingStatus(_clock, _status, Format(Strings.Msg_Refactoring_Working, UndoTitleOf(operation.Title)));
        SetBusy(true);
        try
        {
            working.Start();
            return await UndoCoreAsync(operation, fromEditor, working);
        }
        finally
        {
            working.Stop();
            SetBusy(false);
        }
    }

    /// <summary>IDE-REFAC-021: the sequence of an undo. <c>ThisFileOnly</c>: asked from the editor, it did not go through and the user wants the document's own undo.</summary>
    private async Task<(RefactoringResult Result, bool ThisFileOnly)> UndoCoreAsync(Operation operation, bool fromEditor, WorkingStatus working)
    {
        // (1) Where every file of the operation is now and what text it has; (2) a conflict: nothing changes.
        var states = await ClassifyAsync(operation);
        var conflicts = states.Where(s => s.Conflict is not null).Select(s => new BlockedFile(s.Record.Path, s.Conflict!.Value)).ToList();
        if (conflicts.Count > 0)
            return await UndoBlockedAsync(operation.Title, conflicts, fromEditor, working);
        var restore = states.Where(s => s.Restore).ToList();
        var open = restore.Where(s => !s.IsFile).ToList();

        // (3) The closed files, on the thread pool, in path order; a failed write puts back what was written.
        var written = await Task.Run(() => RestoreFiles(restore.Where(s => s.IsFile).ToList()));
        if (written.FailedPath is { } failedPath)
        {
            working.Stop();
            var thisFileOnly = false;
            if (fromEditor)
                thisFileOnly = await AskThisFileOnlyAsync(operation.Title, $"{failedPath} — {written.Error}");
            else
                await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Refactoring.UndoFailed", [operation.Title, failedPath, written.Error!]));
            return (Result(RefactoringOutcome.Failed, error: written.Error), thisFileOnly);
        }

        // (4) One block on the UI thread: every document is as step 1 found it; then the form texts and buffers, in path order.
        var changed = open.Select(s => (State: s, Reason: ChangeSince(s))).Where(c => c.Reason is not null)
            .Select(c => new BlockedFile(c.State.Record.Path, c.Reason!.Value)).ToList();
        if (changed.Count > 0)
        {
            await Task.Run(() => Restore(written.Written)); // a file that cannot be put back keeps its earlier text: a later undo skips it
            return await UndoBlockedAsync(operation.Title, changed, fromEditor, working);
        }

        foreach (var state in open)
            RestoreDocument(state, UndoTitleOf(operation.Title));

        // (5) The operation leaves the history; the code model reads the written files again; status.
        _history.Remove(operation);
        foreach (var file in written.Written)
            _codeModel.Workspace.NotifyFileChangedOnDisk(file.Path);
        working.Stop();
        _status.ShowMessage(
            restore.Count == 0 ? Format(Strings.Msg_Refactoring_AlreadyUndone, operation.Title)
            : restore.Count == 1 ? Format(Strings.Msg_Refactoring_UndoneOne, operation.Title)
            : Format(Strings.Msg_Refactoring_UndoneMany, operation.Title, restore.Count),
            StatusDuration);
        return (restore.Count == 0 ? Result(RefactoringOutcome.NoChanges) : new RefactoringResult(RefactoringOutcome.Applied, 0, restore.Count, [], null), false);
    }

    /// <summary>
    /// A file of the operation to undo as it is now (IDE-REFAC-021 (1)): the document it belongs to, or the file on disk
    /// (<paramref name="File"/>, null when it does not exist); <see cref="Restore"/> — it has the operation's text and gets
    /// its earlier text back; <see cref="Conflict"/> — it blocks the undo; neither — it has its earlier text already.
    /// </summary>
    private sealed record UndoState(FileRecord Record, TextDocumentViewModel? Document, FormDocumentViewModel? Form)
    {
        public bool IsFile => Document is null && Form is null;

        public FileState? File { get; init; }

        /// <summary>A buffer: its version when it was judged.</summary>
        public long Version { get; init; }

        /// <summary>A form: the designer's current form text when it was judged.</summary>
        public string? FormText { get; init; }

        public bool Restore { get; init; }

        public BlockReason? Conflict { get; init; }
    }

    /// <summary>
    /// IDE-REFAC-021 (1): the current target and text of every file of <paramref name="operation"/>, judged. The closed
    /// files are read on the thread pool; everything is judged on the UI thread after that read, in one block (a
    /// document opened or closed during the read repeats it).
    /// </summary>
    private async Task<List<UndoState>> ClassifyAsync(Operation operation)
    {
        while (true)
        {
            var located = operation.Files.Select(r => DocumentOf(r.Path)).ToList();
            var closed = operation.Files.Where((_, i) => located[i] is (null, null)).Select(r => r.Path).ToList();
            var files = closed.Count == 0 ? [] : await Task.Run(() => closed.Select(ReadState).ToList()); // IDE-ARCH-081
            if (!located.SequenceEqual(operation.Files.Select(r => DocumentOf(r.Path))))
                continue;
            var states = new List<UndoState>();
            var read = 0;
            foreach (var (record, (document, form)) in operation.Files.Zip(located))
            {
                var state = new UndoState(record, document, form);
                state = state switch
                {
                    { Form: not null } => state with { FormText = form!.Designer.CurrentFormText() },
                    { Document: not null } => state with { Version = document!.Buffer.Version },
                    _ => state with { File = files[read++] },
                };
                states.Add(Judge(state));
            }

            return states;
        }
    }

    /// <summary>
    /// IDE-REFAC-021 (1): the operation's text → restore (not writable: a conflict); the earlier text → nothing to do; a
    /// missing file, a file that cannot be read, or any other text → a conflict.
    /// </summary>
    private static UndoState Judge(UndoState state)
    {
        string? text;
        bool writable;
        if (state.Form is { } form)
            (text, writable) = (state.FormText, !form.IsReadOnly && !form.FormText.IsReadOnly);
        else if (state.Document is { } document)
            (text, writable) = (document.Buffer.Text, !document.Buffer.IsReadOnly);
        else if (state.File is not { } file)
            return state with { Conflict = BlockReason.NotFound };
        else if (file.Bytes is null)
            return state with { Conflict = BlockReason.NotWritable }; // it exists and cannot be read: in use
        else
            (text, writable) = (file.Detection is { IsBinary: false } detection ? detection.Text : null, file.Writable);

        if (string.Equals(text, state.Record.After, StringComparison.Ordinal))
            return writable ? state with { Restore = true } : state with { Conflict = BlockReason.NotWritable };
        return string.Equals(text, state.Record.Before, StringComparison.Ordinal) ? state : state with { Conflict = BlockReason.ChangedAfterwards };
    }

    /// <summary>
    /// IDE-REFAC-021 (4): why a document judged in step 1 cannot be restored any more — it was closed, something happened
    /// in its buffer or to its form text, or it became read only — or null when it is as it was.
    /// </summary>
    private BlockReason? ChangeSince(UndoState state)
    {
        if (state.Form is { } form)
        {
            if (!_documents.Documents.Contains(form) || !string.Equals(form.Designer.CurrentFormText(), state.FormText, StringComparison.Ordinal))
                return BlockReason.ChangedAfterwards;
            return form.IsReadOnly || form.FormText.IsReadOnly ? BlockReason.NotWritable : null;
        }

        var document = state.Document!;
        if (!_documents.Documents.Contains(document) || document.Buffer.Version != state.Version)
            return BlockReason.ChangedAfterwards;
        return document.Buffer.IsReadOnly ? BlockReason.NotWritable : null;
    }

    /// <summary>
    /// IDE-REFAC-021 (2): nothing changed and the operation stays; <c>Msg.Refactoring.UndoBlocked</c> names the files —
    /// from the editor <c>Msg.Refactoring.UndoBlockedLocal</c>, which offers the document's own undo (IDE-REFAC-024).
    /// </summary>
    private async Task<(RefactoringResult Result, bool ThisFileOnly)> UndoBlockedAsync(string title, IReadOnlyList<BlockedFile> conflicts, bool fromEditor,
        WorkingStatus working)
    {
        working.Stop();
        var thisFileOnly = false;
        if (fromEditor)
            thisFileOnly = await AskThisFileOnlyAsync(title, BlockedLines(conflicts));
        else
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Refactoring.UndoBlocked", [title, BlockedLines(conflicts)]));
        return (Result(RefactoringOutcome.Blocked, blocked: conflicts), thisFileOnly);
    }

    /// <summary>IDE-REFAC-024: <c>Undo in This File</c> or <c>Cancel</c>.</summary>
    private async Task<bool> AskThisFileOnlyAsync(string title, string lines) =>
        await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Refactoring.UndoBlockedLocal", [title, lines])) == "UndoInThisFile";

    /// <summary>
    /// IDE-REFAC-023: the closed files get their earlier content back — the bytes they had when nothing rewrote them since
    /// the operation (the SHA-256 of their bytes is the recorded one), else the earlier text in the encoding the file has
    /// now. All new bytes are computed before the first write. Thread pool.
    /// </summary>
    private WriteResult RestoreFiles(IReadOnlyList<UndoState> files)
    {
        var writes = new List<FileWrite>();
        foreach (var state in files)
        {
            var (record, current) = (state.Record, state.File!);
            try
            {
                var untouched = record is { OriginalBytes: not null, WrittenHash: { } hash } && SHA256.HashData(current.Bytes!).AsSpan().SequenceEqual(hash);
                writes.Add(new FileWrite(record.Path, current.Bytes!,
                    untouched ? record.OriginalBytes! : FileEncodings.Encode(record.Before, current.Detection!.Encoding), record.Before));
            }
            catch (EncoderFallbackException e)
            {
                return new WriteResult([], record.Path, e.Message, []); // the file's encoding of today cannot store the earlier text
            }
        }

        return Write(writes);
    }

    /// <summary>
    /// IDE-REFAC-022: a form gets its earlier text through the designer; a buffer the operation edited and in which
    /// nothing happened since takes its step back and keeps nothing of it to redo — text, undo history and save point are
    /// as before the operation; any other buffer gets one undo step that replaces the differing middle of its text.
    /// </summary>
    private static void RestoreDocument(UndoState state, string undoTitle)
    {
        var record = state.Record;
        if (state.Form is { } form)
        {
            form.Designer.RevertFormText(record.Before, undoTitle);
            return;
        }

        var buffer = state.Document!.Buffer;
        // The version does not move when a history is reset around an unchanged text (a reload): then there is no step to take back.
        if (ReferenceEquals(buffer, record.Buffer) && buffer.Version == record.Version && buffer.CanUndo)
        {
            buffer.Undo();
            buffer.ClearRedo();
            return;
        }

        var (current, earlier) = (record.After, record.Before); // the buffer has the operation's text (step 1)
        var shorter = Math.Min(current.Length, earlier.Length);
        var prefix = 0;
        while (prefix < shorter && current[prefix] == earlier[prefix])
            prefix++;
        var suffix = 0;
        while (suffix < shorter - prefix && current[current.Length - 1 - suffix] == earlier[earlier.Length - 1 - suffix])
            suffix++;
        buffer.ApplyEdits([new TextEdit(prefix, current.Length - prefix - suffix, earlier[prefix..(earlier.Length - suffix)])], undoTitle);
    }

    /// <summary>
    /// IDE-REFAC-007: <see cref="PreviewMode.Auto"/> shows the preview unless the edit changes exactly one file and that
    /// file is the buffer of the active document's visible text view. A form-text target is not a buffer: it is previewed.
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
