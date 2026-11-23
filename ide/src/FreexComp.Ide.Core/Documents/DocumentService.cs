using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Designer;
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
using FreexComp.Ide.Core.Layout;

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
    private readonly BlockClipboard _blockClipboard = new();
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

    /// <summary>UI-DLG-053: opens a <c>.pproj</c> picked in <c>file.open</c> as the project (set by the service graph).</summary>
    public Func<string, Task>? ProjectOpener { get; set; }

    /// <summary>
    /// <c>file.newUnit</c> while a project is open (IDE-PM-040): returns false when there is no project, so an untitled
    /// unit is made instead (set by the service graph).
    /// </summary>
    public Func<Task<bool>>? ProjectNewUnit { get; set; }

    /// <summary>
    /// The component metadata form documents are read with (designer README §3); an empty catalog until the metadata is
    /// loaded (architecture.md IDE-ARCH-015 step 9, P5). Set by the service graph (tests set it directly).
    /// </summary>
    public FreexComp.Designer.Catalog.ComponentCatalog DesignCatalog { get; set; } = FreexComp.Designer.Catalog.ComponentCatalog.Empty;

    /// <summary>
    /// The companion form of a unit named by the open project's <c>form</c> entry (designer README §3; absolute path),
    /// or null; the sibling <c>.form</c> of the same base name is used otherwise (set by the service graph).
    /// </summary>
    public Func<string, string?>? FormPathOf { get; set; }

    /// <summary>The UI state holding the Documents dialog size (UI-DLG-005; set by the service graph).</summary>
    public ILayoutService? Layout { get; set; }

    /// <summary>Typing pauses for undo grouping (IDE-P1-091; set by the service graph).</summary>
    public IClock? Clock { get; set; }

    /// <summary>The OS clipboard of the documents' clipboard commands (IDE-ED-038; set by the service graph).</summary>
    public IClipboardService? Clipboard { get; set; }

    /// <summary>
    /// IDE-P1-086: called before a document's file is written, disposed on the UI thread after the new stamp is recorded
    /// (the file-change service; set by the service graph).
    /// </summary>
    public Func<string, IDisposable>? WriteGuard { get; set; }

    /// <summary>Creates the Welcome page (welcome-page.md; set by the service graph).</summary>
    public Func<WelcomeDocumentViewModel>? WelcomeFactory { get; set; }

    /// <summary>The OS shell of <c>file.showInExplorer</c> (set by the service graph).</summary>
    public IShellService? Shell { get; set; }

    /// <summary>
    /// tool-windows.md §2: the full paths of the selected Project Manager nodes while the Project Manager has focus
    /// (<c>file.save</c>, <c>file.copyPath</c>, <c>file.showInExplorer</c> then act on them); null elsewhere, so the
    /// commands act on the active document (set by the service graph).
    /// </summary>
    public Func<IReadOnlyList<string>?>? SelectedNodePaths { get; set; }

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
        // I-069: with a project open the project service adds a project unit instead (IDE-PM-040).
        commands.RegisterHandler("file.newUnit", new AsyncHandler(_ => true, async _ =>
        {
            if (ProjectNewUnit is null || !await ProjectNewUnit())
                NewUntitledUnit();
        }));
        commands.RegisterHandler("view.welcomePage", new Handler(_ => WelcomeFactory is not null, _ => ShowWelcomePage()));
        // I-129: the views of a form document (designer README §3; menus.md: "doc has a companion form").
        commands.RegisterHandler("view.toggleFormCode", new Handler(_ => ActiveDocument is FormDocumentViewModel,
            _ => ((FormDocumentViewModel)ActiveDocument!).ToggleFormCode()));
        commands.RegisterHandler("view.formAsText", new Handler(_ => ActiveDocument is FormDocumentViewModel,
            _ => ((FormDocumentViewModel)ActiveDocument!).ShowFormAsText()));
        commands.RegisterHandler("file.close", new AsyncHandler(_ => ActiveDocument is not null, _ => CloseAsync(ActiveDocument!)));
        commands.RegisterHandler("file.closeAll", new AsyncHandler(_ => _documents.Count > 0, _ => CloseAllAsync()));
        commands.RegisterHandler("file.open", new AsyncHandler(_ => true, _ => OpenPickedFilesAsync()));
        commands.RegisterHandler("file.save", new AsyncHandler(_ => SaveTargets().Count > 0, async _ =>
        {
            foreach (var document in SaveTargets())
            {
                if (!await SaveAsync(document))
                    break;
            }
        }));
        commands.RegisterHandler("file.copyPath", new AsyncHandler(_ => PathTarget() is not null, _ => CopyPathAsync(PathTarget()!)));
        // IDE-WEL-008: the Welcome row menu passes the path as the argument.
        commands.RegisterHandler("file.showInExplorer", new Handler(
            arg => (arg ?? PathTarget()) is { } path && (_fileSystem.FileExists(path) || _fileSystem.DirectoryExists(path)),
            arg => RevealInExplorer(arg ?? PathTarget()!)));
        commands.RegisterHandler("file.saveAs", new AsyncHandler(
            _ => ActiveDocument is TextDocumentViewModel, _ => PickAndSaveAsAsync(ActiveDocument!)));
        commands.RegisterHandler("file.saveAll", new AsyncHandler(_ => _documents.Any(d => d.IsModified), _ => SaveAllAsync()));
        // menus.md: modified and the file exists on disk.
        commands.RegisterHandler("file.revert", new AsyncHandler(
            _ => ActiveDocument is TextDocumentViewModel { IsModified: true, FilePath: { } path } && _fileSystem.FileExists(path),
            _ => RevertAsync(ActiveDocument!)));
        commands.RegisterHandler("file.closeOthers", new AsyncHandler(
            _ => ActiveDocument is not null && _documents.Count > 1, _ => CloseAllAsync(except: ActiveDocument)));
        commands.RegisterHandler("window.documents", new AsyncHandler(
            _ => _documents.Count > 0, _ => _dialogs.ShowAsync(new DocumentsViewModel(this, Layout))));
        // Status-bar R7 "Save with Encoding" (IDE-P1-074): only the encoding of the next save changes.
        commands.RegisterHandler("edit.setEncoding", new Handler(
            arg => ActiveDocument is TextDocumentViewModel && arg is not null && FileEncodings.FromId(arg) is not null,
            arg => ((TextDocumentViewModel)ActiveDocument!).SetEncoding(FileEncodings.FromId(arg!)!.Value)));
        // The fold commands, edit.convertLineEndings and the editor operations reach the active document through the
        // generic Editor-scope route (EditorCommandRoute, registered by ServiceGraph).

        // UI-ED-058: Show Whitespace is the setting, for every editor.
        commands.RegisterHandler("view.showWhitespace", new Handler(_ => true,
            _ => _settings.Set(SettingsCatalog.EditorShowWhitespace, !_settings.Get(SettingsCatalog.EditorShowWhitespace)),
            () => _settings.Get(SettingsCatalog.EditorShowWhitespace)));
        _settings.Changed += (_, e) =>
        {
            // The check mark of Show Whitespace; zoom limits and Reset Zoom follow editor.fontSize.
            if (e.Key == SettingsCatalog.EditorShowWhitespace.Name || e.Key == SettingsCatalog.EditorFontSize.Name)
                commands.InvalidateCanExecute();
        };
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

        // DSG-CS-080: a .form opens its unit (same folder and base name) in the Design view.
        if (string.Equals(Path.GetExtension(fullPath), ".form", StringComparison.OrdinalIgnoreCase))
        {
            var owner = _documents.OfType<FormDocumentViewModel>()
                .FirstOrDefault(d => string.Equals(d.FormText.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
            var unitPath = Path.ChangeExtension(fullPath, ".pas");
            if (owner is null && FindByPath(unitPath) is null && _fileSystem.FileExists(unitPath))
                owner = (await OpenAsync(unitPath)).Value as FormDocumentViewModel;
            if (owner is not null && string.Equals(owner.FormText.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                Activate(owner);
                owner.ShowView(DocumentView.Design);
                return OperationResult<DocumentViewModel>.Of(owner);
            }

            // DSG-CS-080: without its unit the form opens in the NoUnit mode (editable Design view, no code sync).
            if (owner is null && FindByPath(unitPath) is null && !_fileSystem.FileExists(unitPath) && _fileSystem.FileExists(fullPath))
                return await LoadFormOnlyAsync(fullPath);
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

    public TextDocumentViewModel OpenUntitled(string name, string text, FileEncoding encoding, LineEndingKind lineEnding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(text);
        var document = new TextDocumentViewModel(_buffers.Create(text, lineEnding), null, name, _languages.ForPath(name), encoding, lineEnding);
        Add(document);
        return document;
    }

    public WelcomeDocumentViewModel ShowWelcomePage()
    {
        if (_documents.OfType<WelcomeDocumentViewModel>().FirstOrDefault() is { } open)
        {
            Activate(open);
            return open;
        }

        var page = (WelcomeFactory ?? throw new InvalidOperationException("No Welcome page factory."))();
        Add(page);
        return page;
    }

    public GeneratedTextDocumentViewModel ShowGenerated(string kind, string title, string text)
    {
        if (_documents.OfType<GeneratedTextDocumentViewModel>().FirstOrDefault(d => d.Kind == kind) is { } open)
        {
            open.Text = text;
            Activate(open);
            return open;
        }

        var document = new GeneratedTextDocumentViewModel(kind, title, text, _buffers);
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

    public void Move(DocumentViewModel document, int index)
    {
        ArgumentNullException.ThrowIfNull(document);
        var from = _documents.IndexOf(document);
        if (from < 0)
            throw new ArgumentException("The document is not open.", nameof(document));
        index = Math.Clamp(index, 0, _documents.Count - 1);
        if (index != from)
            _documents.Move(from, index);
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
        (document as WelcomeDocumentViewModel)?.Detach();
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

    public async Task<bool> PromptSaveChangesAsync(IReadOnlyList<DocumentViewModel> documents, Action? saveFailed = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        // The caller's order: tab order, the project file first (UI-DLG-063).
        var modified = documents.Distinct().Where(d => d.IsModified && _documents.Contains(d)).ToList();
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
            {
                saveFailed?.Invoke();
                return false;
            }
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

        if (await ReadForReopenAsync(path) is not { } read)
            return false;

        // The recovery copy is deleted by the recovery service when the document becomes unmodified (IDE-P1-122).
        text.Reload(FileEncodings.Decode(read.Bytes, text.Encoding));
        text.DiskStamp = read.Stamp;
        _commands?.InvalidateCanExecute();
        return true;
    }

    public async Task<bool> ReopenWithEncodingAsync(DocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document is not TextDocumentViewModel { FilePath: { } path } text)
            return false;
        if (await ReadForReopenAsync(path) is not { Bytes: var bytes })
            return false;

        var detected = EncodingDetector.Detect(bytes);
        var proposed = detected.IsBinary ? FileEncoding.Utf8 : detected.Encoding;
        if (await _dialogs.ShowAsync(new ReopenEncodingViewModel(text.Title, text.Encoding, proposed, text.IsModified)) is not { } chosen)
            return false;

        // dialogs.md section 18: re-read at Reopen; invalid byte sequences become U+FFFD.
        if (await ReadForReopenAsync(path) is not { } current)
            return false;
        text.ReloadWithEncoding(FileEncodings.Decode(current.Bytes, chosen), chosen);
        text.DiskStamp = current.Stamp;
        _commands?.InvalidateCanExecute();
        return true;
    }

    /// <summary>
    /// The file's bytes and stamp (time taken before reading, IDE-P1-110), or null after <c>Msg.File.NotFound</c> /
    /// <c>Msg.File.OpenFailed</c>.
    /// </summary>
    private async Task<(byte[] Bytes, DiskStamp Stamp)?> ReadForReopenAsync(string path)
    {
        try
        {
            return await Task.Run(() => // IDE-ARCH-081
            {
                var lastWrite = _fileSystem.GetLastWriteTimeUtc(path);
                var bytes = _fileSystem.ReadAllBytes(path);
                return ((byte[], DiskStamp)?)(bytes, DiskStamp.Of(lastWrite, bytes));
            });
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.NotFound", [path]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.OpenFailed", [path, ex.Message]));
        }

        return null;
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

        // A modified project model (IDE-PM-004) is written last once project.setConfiguration exists (P3).
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
        // UI-DLG-053: a picked .pproj opens as the project first, the remaining files open as documents afterwards.
        var picked = await _prompts.OpenFileAsync(ActiveDocument?.FilePath);
        var project = ProjectOpener is null ? null
            : picked.FirstOrDefault(p => string.Equals(Path.GetExtension(p), ".pproj", StringComparison.OrdinalIgnoreCase));
        if (project is not null)
            await ProjectOpener!(project);
        foreach (var path in picked.Where(p => !ReferenceEquals(p, project)))
        {
            var result = await OpenAsync(path);
            if (result.NeedsMessage)
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

        // A form document: repair, then the removal of empty handlers, before the unit text is taken (DSG-CS-091 b, 072).
        var form = document as FormDocumentViewModel;
        var movedForm = form is { HasUnit: true } ? FollowingFormPath(form, fullPath) : null;
        if (movedForm is not null)
            form!.Designer.RetargetForm(Path.GetFileName(movedForm)); // DSG-CS-022: the {$FORM} directive follows
        var plan = form?.Designer.PrepareSave();
        ApplySaveEdits(document); // steps 1-2 (IDE-P1-080)
        var text = document.Buffer.Text;
        if (!FileEncodings.CanEncode(text, document.Encoding))
            return OperationResult.Fail("Msg.File.EncodingLossy", FileEncodings.DisplayName(document.Encoding));

        // designer README §3: only the modified buffers are written, the .pas first; a form without unit has no .pas.
        if (form is null || (form.HasUnit && (form.IsUnitModified || !string.Equals(form.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))))
        {
            var written = await WriteAsync(fullPath, FileEncodings.Encode(text, document.Encoding));
            if (!written.Success)
                return written.Failure!;
            document.DiskStamp = written.Stamp;
        }

        // The form: written when modified; a form that follows the unit to a new path (Save As, rename) is written there
        // unless that file already holds exactly its text (a rename moved it).
        if (form is not null && (plan?.FormText ?? (movedForm is null ? null : form.Designer.CurrentFormText())) is { } formText)
        {
            var formPath = movedForm ?? (form.HasUnit ? form.FormText.FilePath! : fullPath);
            var bytes = FileEncodings.Encode(formText, form.FormText.Encoding);
            if (plan?.FormText is null && _fileSystem.FileExists(formPath) && _fileSystem.ReadAllBytes(formPath).AsSpan().SequenceEqual(bytes))
            {
                form.FormText.DiskStamp = StampAfterWrite(formPath, bytes);
            }
            else
            {
                var written = await WriteAsync(formPath, bytes);
                if (!written.Success)
                    return written.Failure!;
                form.FormText.DiskStamp = written.Stamp;
            }

            form.FormText.MarkSaved(formPath, form.FormText.Language);
            form.Designer.MarkFormSaved(formText);
        }

        // The recovery copy is deleted by the recovery service on DocumentSaved (IDE-P1-080 step 5).
        document.MarkSaved(fullPath, document.IsLargeFile ? _languages.PlainText : _languages.ForPath(fullPath)); // IDE-P1-054
        document.SetReadOnly(false); // a read-only document saved under a new name now has a file we just wrote
        UpdateTitleSuffixes();
        if (document == _statusSource)
            PushStatus();
        DocumentSaved?.Invoke(this, new DocumentEventArgs(document));
        _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_Saved, document.Title), StatusMessageDuration);
        if (plan is { RemovedHandlers: > 0 } removed) // DSG-CS-074
        {
            _status.ShowMessage(removed.RemovedHandlers == 1
                ? Strings.Msg_Designer_HandlersRemovedOne
                : string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_HandlersRemovedMany, removed.RemovedHandlers), StatusMessageDuration);
        }

        _commands?.InvalidateCanExecute();
        return OperationResult.Ok;
    }

    /// <summary>
    /// One file of a save, off the UI thread (IDE-ARCH-081) and marked as our own write (IDE-P1-086); the new stamp
    /// (IDE-P1-080 step 5) or the <c>Msg.File.SaveFailed</c> result.
    /// </summary>
    private async Task<(bool Success, DiskStamp? Stamp, OperationResult? Failure)> WriteAsync(string path, byte[] bytes)
    {
        using var ownWrite = WriteGuard?.Invoke(path);
        try
        {
            var stamp = await Task.Run(() =>
            {
                AtomicFileWriter.Write(_fileSystem, path, bytes);
                return StampAfterWrite(path, bytes);
            });
            return (true, stamp, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, null, OperationResult.Fail("Msg.File.SaveFailed", path, ex.Message));
        }
    }

    /// <summary>The stamp of the file just written; null when it cannot be examined (the next check records it).</summary>
    /// <summary>
    /// DSG-CS-022, IDE-PM-071: the form path of a form document saved as <paramref name="unitPath"/>, or null when the
    /// unit keeps its path. The form gets the unit's new base name; a form beside the unit moves to the unit's new folder,
    /// one elsewhere (a project form entry) stays in its folder.
    /// </summary>
    private static string? FollowingFormPath(FormDocumentViewModel form, string unitPath)
    {
        if (form.FilePath is not { } oldUnit || string.Equals(oldUnit, unitPath, StringComparison.OrdinalIgnoreCase)
            || form.FormText.FilePath is not { } oldForm)
            return null;
        var besideUnit = string.Equals(Path.GetDirectoryName(oldForm), Path.GetDirectoryName(oldUnit), StringComparison.OrdinalIgnoreCase);
        var folder = besideUnit ? Path.GetDirectoryName(unitPath)! : Path.GetDirectoryName(oldForm)!;
        var moved = Path.Combine(folder, Path.GetFileNameWithoutExtension(unitPath) + ".form");
        return string.Equals(moved, oldForm, StringComparison.Ordinal) ? null : moved;
    }

    private DiskStamp? StampAfterWrite(string path, byte[] bytes)
    {
        try
        {
            return DiskStamp.Of(_fileSystem.GetLastWriteTimeUtc(path), bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
        DiskStamp stamp;
        (string Path, EncodingDetection Text, DiskStamp Stamp)? form;
        var large = false;
        try
        {
            // IDE-P1-054: above files.largeFileThresholdMb the file opens only after Msg.File.TooLarge is confirmed.
            var length = await Task.Run(() => _fileSystem.GetLength(fullPath)); // IDE-ARCH-081: file I/O off the UI thread
            if (length > _settings.Get(SettingsCatalog.FilesLargeFileThresholdMb) * 1024L * 1024L)
            {
                var size = (length / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.CurrentCulture);
                if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.TooLarge", [size])) != "Open")
                    return OperationResult<DocumentViewModel>.Canceled();
                large = true;
            }

            // IDE-P1-040: the open-for-write probe. The time is taken before reading, so a write in between is seen
            // as a change later rather than missed (IDE-P1-110).
            var formPath = FormPathOf?.Invoke(fullPath);
            (detection, writable, stamp, form) = await Task.Run(() =>
            {
                var lastWrite = _fileSystem.GetLastWriteTimeUtc(fullPath);
                var bytes = _fileSystem.ReadAllBytes(fullPath);
                return (EncodingDetector.Detect(bytes), _fileSystem.CanOpenForWrite(fullPath), DiskStamp.Of(lastWrite, bytes), ReadCompanionForm(fullPath, formPath));
            });
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
        // IDE-P1-054: a large file is Plain Text (no highlighting, folding or bracket matching), word wrap off.
        var language = large ? _languages.PlainText : _languages.ForPath(fullPath);
        TextDocumentViewModel document;
        if (form is { } companion && !large)
        {
            // UI-WIN-020: a unit with a companion form is one document with three views (I-129, D-072).
            var formText = new TextDocumentViewModel(_buffers.Create(companion.Text.Text, lineEnding), companion.Path, null,
                _languages.ForPath(companion.Path), companion.Text.Encoding, lineEnding) { DiskStamp = companion.Stamp };
            Configure(formText);
            var formDocument = new FormDocumentViewModel(_buffers.Create(detection.Text, lineEnding), fullPath, language, detection.Encoding,
                lineEnding, formText, DesignCatalog) { DiskStamp = stamp };
            formDocument.Designer.Status = _status;
            BindDesignerClipboard(formDocument);
            formDocument.ActiveViewChanged += (_, _) => _commands?.InvalidateCanExecute();
            document = formDocument;
        }
        else
        {
            document = new TextDocumentViewModel(_buffers.Create(detection.Text, lineEnding), fullPath, null, language, detection.Encoding,
                lineEnding) { IsLargeFile = large, DiskStamp = stamp };
        }

        document.ReopenWithEncodingHandler = () => ReopenWithEncodingAsync(document);
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

    /// <summary>
    /// DSG-CS-080, code-sync.md §8 <c>NoUnit</c>: a <c>.form</c> whose unit does not exist is one form document without a
    /// unit — its own buffer is an empty placeholder, its path is the form's, it shows the Design view.
    /// </summary>
    private async Task<OperationResult<DocumentViewModel>> LoadFormOnlyAsync(string fullPath)
    {
        EncodingDetection detection;
        DiskStamp stamp;
        try
        {
            (detection, stamp) = await Task.Run(() =>
            {
                var lastWrite = _fileSystem.GetLastWriteTimeUtc(fullPath);
                var bytes = _fileSystem.ReadAllBytes(fullPath);
                return (EncodingDetector.Detect(bytes), DiskStamp.Of(lastWrite, bytes));
            });
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
        if (FindByPath(fullPath) is { } open)
            return OperationResult<DocumentViewModel>.Of(open);

        var lineEnding = DefaultLineEnding;
        var language = _languages.ForPath(fullPath);
        var formText = new TextDocumentViewModel(_buffers.Create(detection.Text, lineEnding), fullPath, null, language, detection.Encoding,
            lineEnding) { DiskStamp = stamp };
        Configure(formText);
        var document = new FormDocumentViewModel(_buffers.Create("", lineEnding), fullPath, language, detection.Encoding, lineEnding,
            formText, DesignCatalog, hasUnit: false) { DiskStamp = stamp };
        document.Designer.Status = _status;
        BindDesignerClipboard(document);
        document.ActiveViewChanged += (_, _) => _commands?.InvalidateCanExecute();
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

    /// <summary>
    /// The companion <c>.form</c> of a <c>.pas</c> unit (same folder, same base name; designer README §3) and its text, or
    /// null when there is none or it cannot be read as text. Called off the UI thread.
    /// </summary>
    private (string Path, EncodingDetection Text, DiskStamp Stamp)? ReadCompanionForm(string unitPath, string? projectForm)
    {
        if (!string.Equals(Path.GetExtension(unitPath), ".pas", StringComparison.OrdinalIgnoreCase))
            return null;
        var formPath = projectForm is not null && _fileSystem.FileExists(projectForm) ? projectForm : Path.ChangeExtension(unitPath, ".form");
        if (!_fileSystem.FileExists(formPath))
            return null;
        try
        {
            var lastWrite = _fileSystem.GetLastWriteTimeUtc(formPath);
            var bytes = _fileSystem.ReadAllBytes(formPath);
            var detection = EncodingDetector.Detect(bytes);
            return detection.IsBinary ? null : (formPath, detection, DiskStamp.Of(lastWrite, bytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The editor services of a text document (also of the form text inside a form document, which is not listed).</summary>
    private void Configure(TextDocumentViewModel textDocument)
    {
        textDocument.Options = _editorOptions;
        textDocument.BlockClipboard = _blockClipboard;
        textDocument.Clock = Clock;
        textDocument.Clipboard = Clipboard;
        textDocument.GutterMenu = line => _commands is null ? [] : Menus.GutterMenu.Build(textDocument.Decorations.GlyphsAt(line), _commands, _settings);
        textDocument.ExecuteCommand = id => _commands is { } commands && commands.CanExecute(id) ? commands.ExecuteAsync(id) : Task.CompletedTask;
        textDocument.TextAreaMenu = () => _commands is null ? []
            : Menus.EditorContextMenu.Build(_commands, Composition.ServiceGraph.ImplementedPhase, hasForm: textDocument is FormDocumentViewModel);
        textDocument.ViewState.WordWrap = _editorOptions.WordWrap; // IDE-ED-040; a restored session replaces it
        textDocument.ViewState.PropertyChanged += (_, e) =>
        {
            // Zoom limits, Reset Zoom and the Word Wrap check mark follow the view state.
            if (e.PropertyName is nameof(EditorViewState.FontSize) or nameof(EditorViewState.WordWrap))
                _commands?.InvalidateCanExecute();
        };
    }

    /// <summary>A new tab opens right of the active one (UI-ED-077) and becomes active.</summary>
    private void Add(DocumentViewModel document)
    {
        if (document is TextDocumentViewModel textDocument)
            Configure(textDocument);
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
            text.StatusMessage += (_, message) => _status.ShowMessage(message, StatusMessageDuration);
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

    /// <summary>
    /// The status bar shows the active text document's fields (IDE-ARCH: <c>IStatusService.SetEditor</c>); in the Design
    /// view of a form document the designer selection instead (UI-DSG-130).
    /// </summary>
    private void FollowStatus(TextDocumentViewModel? document)
    {
        if (_statusSource is not null)
        {
            _statusSource.PropertyChanged -= OnStatusSourceChanged;
            _statusSource.ViewState.PropertyChanged -= OnStatusSourceChanged;
            if (_statusSource is FormDocumentViewModel old)
            {
                old.Designer.SelectionChanged -= OnDesignerStatusChanged;
                old.Designer.ModelChanged -= OnDesignerStatusChanged;
            }
        }

        _statusSource = document;
        if (document is not null)
        {
            document.PropertyChanged += OnStatusSourceChanged;
            document.ViewState.PropertyChanged += OnStatusSourceChanged;
            if (document is FormDocumentViewModel form)
            {
                form.Designer.SelectionChanged += OnDesignerStatusChanged;
                form.Designer.ModelChanged += OnDesignerStatusChanged;
            }
        }

        PushStatus();
    }

    private void OnDesignerStatusChanged(object? sender, EventArgs e) => PushStatus();

    private void OnStatusSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(EditorViewState.HorizontalOffset) or nameof(EditorViewState.VerticalOffset)))
            PushStatus(); // scrolling changes no status field
    }

    private void PushStatus()
    {
        if (_statusSource is FormDocumentViewModel { ActiveView: DocumentView.Design } form)
        {
            _status.SetEditor(null);
            _status.SetDesignerSelection(form.Designer.SelectionStatusText);
            return;
        }

        _status.SetDesignerSelection(null);
        _status.SetEditor(_statusSource?.GetStatus(_settings.Get(SettingsCatalog.EditorInsertSpaces), _settings.Get(SettingsCatalog.EditorTabWidth)));
    }

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

    /// <summary>
    /// <c>file.save</c>: the modified open documents of the selected Project Manager files while it has focus, else
    /// the active document when it is a modified text document.
    /// </summary>
    private List<DocumentViewModel> SaveTargets()
    {
        if (SelectedNodePaths?.Invoke() is { } selected)
            return [.. selected.Select(FindByPath).OfType<TextDocumentViewModel>().Where(d => d.IsModified)];
        return ActiveDocument is TextDocumentViewModel { IsModified: true } active ? [active] : [];
    }

    /// <summary><c>file.copyPath</c> / <c>file.showInExplorer</c>: the one selected node while the Project Manager has focus, else the active document's file.</summary>
    private string? PathTarget() =>
        SelectedNodePaths?.Invoke() is { } selected ? (selected is [var one] ? one : null) : ActiveDocument?.FilePath;

    private async Task CopyPathAsync(string path)
    {
        if (Clipboard is not { } clipboard)
            return;
        try
        {
            await clipboard.SetTextAsync(path);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or IOException or UnauthorizedAccessException
                                       or TimeoutException or InvalidOperationException)
        {
            _status.ShowMessage(Strings.Status_ClipboardBusy, StatusMessageDuration);
        }
    }

    /// <summary>D-078: the designer copies to and pastes from the OS clipboard (design-surface.md DSG-SUR-110, DSG-SUR-120).</summary>
    private void BindDesignerClipboard(FormDocumentViewModel document)
    {
        if (Clipboard is { } clipboard)
            document.Designer.Host.Clipboard = new SystemDesignerClipboard(clipboard);
    }

    private void RevealInExplorer(string path) => Shell?.RevealFile(path); // a refusal of the shell has no message (P1)

    private static bool IsOrderArgument(string? argument) => argument is null or MruArgument or VisualArgument;

    private static string UntitledUnitName(int n) => $"Unit{n}.pas";

    /// <summary>project-manager.md §4.2: the unit skeleton, one empty line between sections.</summary>
    private static string UnitSkeleton(string unitName, string newLine) =>
        string.Join(newLine, $"unit {unitName};", "", "interface", "", "implementation", "", "end.", "");

    private sealed class Handler(Func<string?, bool> canExecute, Action<string?> execute, Func<bool>? isChecked = null) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public bool IsChecked(string? argument) => isChecked?.Invoke() ?? false;

        public Task ExecuteAsync(string? argument)
        {
            execute(argument);
            return Task.CompletedTask;
        }
    }

    private sealed class AsyncHandler(Func<string?, bool> canExecute, Func<string?, Task> execute, Func<bool>? isChecked = null) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public bool IsChecked(string? argument) => isChecked?.Invoke() ?? false;

        public Task ExecuteAsync(string? argument) => execute(argument);
    }
}
