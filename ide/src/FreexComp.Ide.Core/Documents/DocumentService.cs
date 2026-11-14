using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Documents;

/// <inheritdoc />
public sealed class DocumentService : IDocumentService
{
    private const string MruArgument = "mru";
    private const string VisualArgument = "visual";
    private static readonly TimeSpan StatusMessageDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly IFileSystem _fileSystem;
    private readonly ITextBufferFactory _buffers;
    private readonly ILanguageRegistry _languages;
    private readonly ISettingsService _settings;
    private readonly EditorOptions _editorOptions;
    private readonly IStatusService _status;
    private readonly IFocusTracker _focus;
    private readonly IDialogService _dialogs;
    private readonly FilePrompts _prompts;
    private readonly ObservableCollection<DocumentViewModel> _documents = [];
    private readonly List<DocumentViewModel> _mru = [];
    private readonly Dictionary<string, Task<OperationResult<DocumentViewModel>>> _loading = new(StringComparer.OrdinalIgnoreCase);
    private ICommandService? _commands;
    private TextDocumentViewModel? _statusSource;

    public DocumentService(IFileSystem fileSystem, ITextBufferFactory buffers, ILanguageRegistry languages, ISettingsService settings,
        IStatusService status, IFocusTracker focus, IDialogService dialogs, FilePrompts prompts)
    {
        _prompts = prompts;
        _fileSystem = fileSystem;
        _dialogs = dialogs;
        _buffers = buffers;
        _languages = languages;
        _settings = settings;
        _editorOptions = new EditorOptions(settings);
        _status = status;
        _focus = focus;
        Documents = new ReadOnlyObservableCollection<DocumentViewModel>(_documents);
        _settings.Changed += (_, e) =>
        {
            if (e.Key == SettingsCatalog.EditorTabWidth.Name || e.Key == SettingsCatalog.EditorInsertSpaces.Name)
                PushStatus();
        };
    }

    public ReadOnlyObservableCollection<DocumentViewModel> Documents { get; }

    public IReadOnlyList<DocumentViewModel> MostRecentlyUsed => _mru;

    public DocumentViewModel? ActiveDocument { get; private set; }

    public event EventHandler<DocumentEventArgs>? DocumentOpened;

    public event EventHandler<DocumentClosingEventArgs>? DocumentClosing;

    public event EventHandler<DocumentEventArgs>? DocumentClosed;

    public event EventHandler? ActiveDocumentChanged;

    public event EventHandler<DocumentEventArgs>? DocumentSaved;

    private LineEndingKind DefaultLineEnding =>
        _settings.Get(SettingsCatalog.FilesDefaultLineEnding) == "lf" ? LineEndingKind.Lf : LineEndingKind.CrLf;

    private FileEncoding DefaultEncoding => FileEncodings.FromId(_settings.Get(SettingsCatalog.FilesDefaultEncoding)) ?? FileEncoding.Utf8;

    /// <summary>Handlers of the document commands that need no picker or prompt (commands.md).</summary>
    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        commands.RegisterHandler("window.nextDocument", new Handler(
            arg => _documents.Count > 1 && IsOrderArgument(arg),
            arg => ActivateNext(forward: true, mru: arg == MruArgument)));
        commands.RegisterHandler("window.previousDocument", new Handler(
            arg => _documents.Count > 1 && IsOrderArgument(arg),
            arg => ActivateNext(forward: false, mru: arg == MruArgument)));
        commands.RegisterHandler("window.activateDocument", new Handler(
            arg => FindById(arg) is not null,
            arg => Activate(FindById(arg)!)));
        // Without a project (IDE-P1-033); with one, the project service routes it to project.addNewUnit.
        commands.RegisterHandler("file.newUnit", new Handler(_ => true, _ => NewUntitledUnit()));
        commands.RegisterHandler("file.close", new AsyncHandler(_ => ActiveDocument is not null, _ => CloseAsync(ActiveDocument!)));
        commands.RegisterHandler("file.closeAll", new AsyncHandler(_ => _documents.Count > 0, _ => CloseAllAsync()));
        commands.RegisterHandler("file.open", new AsyncHandler(_ => true, _ => OpenPickedFilesAsync()));
        commands.RegisterHandler("file.save", new AsyncHandler(
            _ => ActiveDocument is TextDocumentViewModel { IsModified: true }, _ => SaveAsync(ActiveDocument!)));
        commands.RegisterHandler("file.saveAs", new AsyncHandler(
            _ => ActiveDocument is TextDocumentViewModel, _ => PickAndSaveAsAsync(ActiveDocument!)));
        commands.RegisterHandler("file.saveAll", new AsyncHandler(_ => _documents.Any(d => d.IsModified), _ => SaveAllAsync()));
        // menus.md: modified and the file exists on disk.
        commands.RegisterHandler("file.revert", new AsyncHandler(
            _ => ActiveDocument is TextDocumentViewModel { IsModified: true, FilePath: { } path } && _fileSystem.FileExists(path),
            _ => RevertAsync(ActiveDocument!)));
        commands.RegisterHandler("file.closeOthers", new AsyncHandler(
            _ => ActiveDocument is not null && _documents.Count > 1, _ => CloseAllAsync(except: ActiveDocument)));
    }

    public async Task<OperationResult<DocumentViewModel>> OpenAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var fullPath = Path.GetFullPath(path);
        if (FindByPath(fullPath) is { } open)
        {
            Activate(open);
            return OperationResult<DocumentViewModel>.Of(open);
        }

        if (_loading.TryGetValue(fullPath, out var pending))
            return await pending;

        var load = LoadAsync(fullPath);
        _loading[fullPath] = load;
        try
        {
            return await load;
        }
        finally
        {
            _loading.Remove(fullPath);
        }
    }

    public TextDocumentViewModel NewUntitledUnit()
    {
        var n = 1;
        while (_documents.OfType<TextDocumentViewModel>().Any(d => string.Equals(d.UntitledName, UntitledUnitName(n), StringComparison.OrdinalIgnoreCase)))
            n++;

        var name = UntitledUnitName(n);
        var lineEnding = DefaultLineEnding;
        var buffer = _buffers.Create(UnitSkeleton(Path.GetFileNameWithoutExtension(name), LineEndingDetector.NewLineText(lineEnding)), lineEnding);
        var document = new TextDocumentViewModel(buffer, null, name, _languages.ForPath(name), DefaultEncoding, lineEnding);
        Add(document);
        return document;
    }

    public void Activate(DocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_documents.Contains(document))
            throw new ArgumentException("The document is not open.", nameof(document));

        _mru.Remove(document);
        _mru.Insert(0, document);
        if (ActiveDocument == document)
            return;
        SetActive(document);
    }

    public void ActivateNext(bool forward, bool mru)
    {
        if (_documents.Count < 2 || ActiveDocument is null)
            return;

        if (mru)
        {
            Activate(forward ? _mru[1] : _mru[^1]);
            return;
        }

        var index = _documents.IndexOf(ActiveDocument);
        var next = (index + (forward ? 1 : -1) + _documents.Count) % _documents.Count;
        Activate(_documents[next]);
    }

    public bool Close(DocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_documents.Contains(document))
            return false;

        var closing = new DocumentClosingEventArgs(document);
        DocumentClosing?.Invoke(this, closing);
        if (closing.Cancel)
            return false;

        _documents.Remove(document);
        _mru.Remove(document);
        UpdateTitleSuffixes();
        DocumentClosed?.Invoke(this, new DocumentEventArgs(document));
        if (ActiveDocument == document)
        {
            if (_mru.Count > 0)
                Activate(_mru[0]);
            else
                SetActive(null);
        }

        _commands?.InvalidateCanExecute();
        return true;
    }

    public async Task<bool> CloseAsync(DocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_documents.Contains(document))
            return false;

        return await PromptSaveChangesAsync([document]) && Close(document);
    }

    public async Task<bool> CloseAllAsync(DocumentViewModel? except = null)
    {
        var closing = _documents.Where(d => d != except).ToList();
        if (!await PromptSaveChangesAsync(closing))
            return false;

        foreach (var document in closing)
        {
            Close(document);
        }

        return true;
    }

    public async Task<bool> PromptSaveChangesAsync(IReadOnlyList<DocumentViewModel> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var modified = _documents.Where(d => d.IsModified && documents.Contains(d)).ToList(); // tab order
        IReadOnlyList<DocumentViewModel>? toSave;
        switch (modified.Count)
        {
            case 0:
                return true;
            case 1:
                toSave = await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.SaveChangesOne", [modified[0].Title])) switch
                {
                    "Save" => modified,
                    "DontSave" => [],
                    _ => null,
                };
                break;
            default:
                toSave = await _dialogs.ShowAsync(new UnsavedChangesViewModel(modified));
                break;
        }

        if (toSave is null)
            return false;

        // UI-DLG-062: a failed save or a canceled Save As picker cancels the whole operation. Discarded documents'
        // recovery copies are deleted by the recovery service on DocumentClosed (I-063).
        foreach (var document in toSave)
        {
            if (!await SaveAsync(document))
                return false;
        }

        return true;
    }

    public async Task<bool> SaveAsync(DocumentViewModel document) => await SaveCoreAsync(document) == SaveOutcome.Saved;

    public async Task<bool> PickAndSaveAsAsync(DocumentViewModel document) =>
        await SaveCoreAsync(document, pickPath: true) == SaveOutcome.Saved;

    public async Task<bool> RevertAsync(DocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document is not TextDocumentViewModel { FilePath: { } path } text)
            return false;
        if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.RevertConfirm", [text.Title])) != "Revert")
            return false;

        byte[] bytes;
        try
        {
            bytes = await Task.Run(() => _fileSystem.ReadAllBytes(path)); // IDE-ARCH-081
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.NotFound", [path]));
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.OpenFailed", [path, ex.Message]));
            return false;
        }

        // Deleting the recovery copy (IDE-P1-122, I-063) and resetting line-change marks (IDE-ED-045) join with
        // their services.
        text.Reload(FileEncodings.Decode(bytes, text.Encoding));
        _commands?.InvalidateCanExecute();
        return true;
    }

    public async Task<bool> SaveAllAsync()
    {
        var written = 0;
        foreach (var document in _documents.Where(d => d.IsModified).ToList()) // tab order (IDE-P1-085)
        {
            switch (await SaveCoreAsync(document))
            {
                case SaveOutcome.Saved:
                    written++;
                    break;
                case SaveOutcome.Canceled:
                    return false;
                case SaveOutcome.PickerCanceled:
                    break; // an untitled document whose picker was canceled is skipped
            }
        }

        // The project file joins with the project service (I-065).
        _status.ShowMessage(written == 0
            ? Strings.Msg_File_NothingToSave
            : string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_SavedAll, written), StatusMessageDuration);
        return true;
    }

    /// <summary>
    /// The interactive save of one document (IDE-P1-082, IDE-P1-083): the lossy-encoding question, the failure dialog
    /// with Retry / Save As… / Cancel.
    /// </summary>
    private async Task<SaveOutcome> SaveCoreAsync(DocumentViewModel document, bool pickPath = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document is not TextDocumentViewModel text)
            return SaveOutcome.Canceled;

        var path = pickPath ? await PickSavePathAsync(text) : text.FilePath ?? await PickSavePathAsync(text);
        if (path is null)
            return SaveOutcome.PickerCanceled;

        while (true)
        {
            var result = await SaveAsAsync(text, path);
            if (result.Success)
                return SaveOutcome.Saved;

            var answer = await _dialogs.ShowMessageAsync(new MessageRequest(result.MessageId!, result.Args));
            switch (result.MessageId, answer)
            {
                case ("Msg.File.EncodingLossy", "SaveAsUTF8"):
                    text.SetEncoding(FileEncoding.Utf8);
                    break;
                case ("Msg.File.SaveFailed", "Retry"):
                    break;
                case ("Msg.File.SaveFailed", "SaveAs"):
                    if (await PickSavePathAsync(text) is not { } chosen)
                        return SaveOutcome.Canceled;
                    path = chosen;
                    break;
                default:
                    return SaveOutcome.Canceled;
            }
        }
    }

    /// <summary>The Save As picker (IDE-P1-034, dialogs.md §5 <c>saveAs</c>); null when canceled.</summary>
    private Task<string?> PickSavePathAsync(TextDocumentViewModel document) => _prompts.SaveAsAsync(document);

    /// <summary>
    /// <c>file.open</c> (IDE-P1-030): every picked file opens in order (one already open is activated, UI-DLG-054), so
    /// the last becomes active; a file that cannot be opened is reported and the others still open.
    /// </summary>
    private async Task OpenPickedFilesAsync()
    {
        // UI-DLG-053 (a picked .pproj opens as the project) joins with the project service (I-065); until then it
        // opens as a JSON document.
        foreach (var path in await _prompts.OpenFileAsync(ActiveDocument?.FilePath))
        {
            var result = await OpenAsync(path);
            if (!result.Success)
                await _dialogs.ShowMessageAsync(new MessageRequest(result.MessageId!, result.Args));
        }
    }

    public async Task<OperationResult> SaveAsAsync(TextDocumentViewModel document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(path);
        var fullPath = Path.GetFullPath(path);
        if (FindByPath(fullPath) is { } other && other != document)
            return OperationResult.Fail("Msg.File.SaveFailed", other.FilePath!, Strings.Reason_OpenInAnotherTab);

        ApplySaveEdits(document); // steps 1-2 (IDE-P1-080)
        var text = document.Buffer.Text;
        if (!FileEncodings.CanEncode(text, document.Encoding))
            return OperationResult.Fail("Msg.File.EncodingLossy", FileEncodings.DisplayName(document.Encoding));

        var bytes = FileEncodings.Encode(text, document.Encoding);
        try
        {
            await Task.Run(() => AtomicFileWriter.Write(_fileSystem, fullPath, bytes)); // IDE-ARCH-081: off the UI thread
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail("Msg.File.SaveFailed", fullPath, ex.Message);
        }

        // Recording the file timestamp/size (I-062) and deleting the recovery copy (I-063) join with their services.
        document.MarkSaved(fullPath, _languages.ForPath(fullPath));
        document.SetReadOnly(false); // a read-only document saved under a new name now has a file we just wrote
        UpdateTitleSuffixes();
        if (document == _statusSource)
            PushStatus();
        DocumentSaved?.Invoke(this, new DocumentEventArgs(document));
        _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_Saved, document.Title), StatusMessageDuration);
        _commands?.InvalidateCanExecute();
        return OperationResult.Ok;
    }

    /// <summary>
    /// IDE-P1-080 steps 1-2 as one undo step: trailing spaces/tabs before each line break and at the end (not on the
    /// caret line while the caret is in that whitespace), then a final line break in the document's ending. Skipped
    /// for Form and JSON documents and for read-only buffers.
    /// </summary>
    private void ApplySaveEdits(TextDocumentViewModel document)
    {
        var buffer = document.Buffer;
        if (buffer.IsReadOnly || document.Language.Id is "form" or "json")
            return;

        var edits = new List<TextEdit>();
        var lastLine = buffer.GetLine(buffer.LineCount);
        var lastContent = lastLine.Length; // what remains of the last line: text there means no final line break
        if (_settings.Get(SettingsCatalog.FilesTrimTrailingWhitespaceOnSave))
        {
            var caret = document.ViewState.Caret;
            for (var line = 1; line <= buffer.LineCount; line++)
            {
                var (offset, length, _) = buffer.GetLine(line);
                var content = length;
                while (content > 0 && buffer.GetText(offset + content - 1, 1) is " " or "\t")
                    content--;
                if (content == length || (line == caret.Line && caret.Column - 1 > content))
                    continue;
                edits.Add(new TextEdit(offset + content, length - content, ""));
                if (line == buffer.LineCount)
                    lastContent = content;
            }
        }

        if (_settings.Get(SettingsCatalog.FilesEnsureFinalNewline) && lastContent > 0)
        {
            var newLine = document.LineEnding.NewLine;
            if (edits.Count > 0 && edits[^1].Offset + edits[^1].Length == buffer.Length)
                edits[^1] = edits[^1] with { NewText = newLine };
            else
                edits.Add(new TextEdit(buffer.Length, 0, newLine));
        }

        if (edits.Count > 0)
            buffer.ApplyEdits(edits);
    }

    private async Task<OperationResult<DocumentViewModel>> LoadAsync(string fullPath)
    {
        EncodingDetection detection;
        bool writable;
        try
        {
            // IDE-ARCH-081: file I/O off the UI thread. IDE-P1-040: the open-for-write probe.
            (detection, writable) = await Task.Run(() =>
                (EncodingDetector.Detect(_fileSystem.ReadAllBytes(fullPath)), _fileSystem.CanOpenForWrite(fullPath)));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return OperationResult<DocumentViewModel>.Fail("Msg.File.NotFound", fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<DocumentViewModel>.Fail("Msg.File.OpenFailed", fullPath, ex.Message);
        }

        if (detection.IsBinary)
            return OperationResult<DocumentViewModel>.Fail("Msg.File.Binary", fullPath);

        var lineEnding = DefaultLineEnding;
        var document = new TextDocumentViewModel(_buffers.Create(detection.Text, lineEnding), fullPath, null,
            _languages.ForPath(fullPath), detection.Encoding, lineEnding);
        document.ShowLoadInfoBars(detection.IsFallback);
        if (!writable)
        {
            document.SetReadOnly(true);
            document.ShowInfoBar(new InfoBarViewModel(TextDocumentViewModel.ReadOnlyInfoBarKind, InfoBarSeverity.Info, Strings.Msg_File_ReadOnly,
                [new InfoBarAction("MakeWritable", Strings.Button_MakeWritable, () => MakeWritableAsync(document))],
                bar => document.RemoveInfoBar(bar.Kind)));
        }

        Add(document);
        return OperationResult<DocumentViewModel>.Of(document);
    }

    /// <summary>The <c>Make Writable</c> action (IDE-P1-040): clears the attribute, then the buffer becomes writable.</summary>
    private async Task MakeWritableAsync(TextDocumentViewModel document)
    {
        var path = document.FilePath!;
        try
        {
            _fileSystem.SetReadOnly(path, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.MakeWritableFailed", [path, ex.Message]));
            return;
        }

        document.SetReadOnly(false);
        _commands?.InvalidateCanExecute();
    }

    /// <summary>A new tab opens right of the active one (UI-ED-077) and becomes active.</summary>
    private void Add(DocumentViewModel document)
    {
        if (document is TextDocumentViewModel textDocument)
            textDocument.Options = _editorOptions;
        var index = ActiveDocument is null ? _documents.Count : _documents.IndexOf(ActiveDocument) + 1;
        _documents.Insert(index, document);
        var modified = document.IsModified;
        document.PropertyChanged += (_, e) =>
        {
            // file.save / file.saveAll follow the modified state (menus.md).
            if (e.PropertyName == nameof(DocumentViewModel.IsModified) && document.IsModified != modified)
            {
                modified = document.IsModified;
                _commands?.InvalidateCanExecute();
            }
        };
        if (document is TextDocumentViewModel text)
        {
            text.ReadOnlyEditRefused += (_, _) => _status.ShowMessage(Strings.Msg_Editor_ReadOnlyEdit, StatusMessageDuration);
            var undoState = (text.Buffer.CanUndo, text.Buffer.CanRedo);
            text.Buffer.Changed += (_, _) =>
            {
                // edit.undo / edit.redo follow the buffer (menus.md, toolbars.md).
                if ((text.Buffer.CanUndo, text.Buffer.CanRedo) != undoState)
                {
                    undoState = (text.Buffer.CanUndo, text.Buffer.CanRedo);
                    _commands?.InvalidateCanExecute();
                }
            };
        }
        UpdateTitleSuffixes();
        DocumentOpened?.Invoke(this, new DocumentEventArgs(document));
        Activate(document);
        _commands?.InvalidateCanExecute();
    }

    private void SetActive(DocumentViewModel? document)
    {
        ActiveDocument = document;
        _focus.SetActiveDocumentTarget(document?.CommandTarget);
        FollowStatus(document as TextDocumentViewModel);
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
        _commands?.InvalidateCanExecute();
    }

    /// <summary>The status bar shows the active text document's fields (IDE-ARCH: <c>IStatusService.SetEditor</c>).</summary>
    private void FollowStatus(TextDocumentViewModel? document)
    {
        if (_statusSource is not null)
        {
            _statusSource.PropertyChanged -= OnStatusSourceChanged;
            _statusSource.ViewState.PropertyChanged -= OnStatusSourceChanged;
        }

        _statusSource = document;
        if (document is not null)
        {
            document.PropertyChanged += OnStatusSourceChanged;
            document.ViewState.PropertyChanged += OnStatusSourceChanged;
        }

        PushStatus();
    }

    private void OnStatusSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(EditorViewState.HorizontalOffset) or nameof(EditorViewState.VerticalOffset)))
            PushStatus(); // scrolling changes no status field
    }

    private void PushStatus() =>
        _status.SetEditor(_statusSource?.GetStatus(_settings.Get(SettingsCatalog.EditorInsertSpaces), _settings.Get(SettingsCatalog.EditorTabWidth)));

    /// <summary>
    /// UI-ED-071 / IDE-P1-037: documents sharing a file name get the shortest run of parent folders that no other of
    /// them has.
    /// </summary>
    private void UpdateTitleSuffixes()
    {
        var files = _documents.Where(d => d.FilePath is not null).ToList();
        foreach (var group in files.GroupBy(d => Path.GetFileName(d.FilePath!), StringComparer.OrdinalIgnoreCase))
        {
            var members = group.Select(d => (Document: d, Folders: ParentFolders(d.FilePath!))).ToList();
            foreach (var (document, folders) in members)
            {
                if (members.Count == 1)
                {
                    document.TitleSuffix = null;
                    continue;
                }

                var depth = 1;
                while (depth < folders.Length && members.Any(m => m.Document != document && SameTail(m.Folders, folders, depth)))
                    depth++;
                document.TitleSuffix = string.Join(Path.DirectorySeparatorChar, folders[^Math.Min(depth, folders.Length)..]);
            }
        }
    }

    private static string[] ParentFolders(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? "";
        var root = Path.GetPathRoot(directory) ?? "";
        var folders = directory[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return folders.Length > 0 ? folders : [root.TrimEnd(Path.DirectorySeparatorChar)];
    }

    private static bool SameTail(string[] a, string[] b, int depth) =>
        a.Length >= depth && b.Length >= depth
        && a[^depth..].SequenceEqual(b[^depth..], StringComparer.OrdinalIgnoreCase);

    private DocumentViewModel? FindByPath(string fullPath) =>
        _documents.FirstOrDefault(d => string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));

    private DocumentViewModel? FindById(string? argument) =>
        DocumentId.TryParse(argument) is { } id ? _documents.FirstOrDefault(d => d.Id == id) : null;

    private enum SaveOutcome
    {
        Saved,
        Canceled,
        PickerCanceled,
    }

    private static bool IsOrderArgument(string? argument) => argument is null or MruArgument or VisualArgument;

    private static string UntitledUnitName(int n) => $"Unit{n}.pas";

    /// <summary>project-manager.md §4.2: the unit skeleton, one empty line between sections.</summary>
    private static string UnitSkeleton(string unitName, string newLine) =>
        string.Join(newLine, $"unit {unitName};", "", "interface", "", "implementation", "", "end.", "");

    private sealed class Handler(Func<string?, bool> canExecute, Action<string?> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument)
        {
            execute(argument);
            return Task.CompletedTask;
        }
    }

    private sealed class AsyncHandler(Func<string?, bool> canExecute, Func<string?, Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument) => execute(argument);
    }
}
