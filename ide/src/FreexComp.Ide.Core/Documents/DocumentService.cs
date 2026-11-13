using System.Collections.ObjectModel;
using System.ComponentModel;
using FreexComp.Ide.Core.Commands;
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

    private readonly IFileSystem _fileSystem;
    private readonly ITextBufferFactory _buffers;
    private readonly ILanguageRegistry _languages;
    private readonly ISettingsService _settings;
    private readonly IStatusService _status;
    private readonly IFocusTracker _focus;
    private readonly ObservableCollection<DocumentViewModel> _documents = [];
    private readonly List<DocumentViewModel> _mru = [];
    private readonly Dictionary<string, Task<OperationResult<DocumentViewModel>>> _loading = new(StringComparer.OrdinalIgnoreCase);
    private ICommandService? _commands;
    private TextDocumentViewModel? _statusSource;

    public DocumentService(IFileSystem fileSystem, ITextBufferFactory buffers, ILanguageRegistry languages, ISettingsService settings,
        IStatusService status, IFocusTracker focus)
    {
        _fileSystem = fileSystem;
        _buffers = buffers;
        _languages = languages;
        _settings = settings;
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

    public async Task<OperationResult> SaveAsAsync(TextDocumentViewModel document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(path);
        var fullPath = Path.GetFullPath(path);
        if (FindByPath(fullPath) is { } other && other != document)
            return OperationResult.Fail("Msg.File.SaveFailed", other.FilePath!, Strings.Reason_OpenInAnotherTab);

        var text = document.Buffer.Text;
        if (!FileEncodings.CanEncode(text, document.Encoding))
            return OperationResult.Fail("Msg.File.EncodingLossy"); // the Save-as-UTF-8 question is the save pipeline's (IDE-P1-083)

        var bytes = FileEncodings.Encode(text, document.Encoding);
        try
        {
            await Task.Run(() => _fileSystem.WriteAllBytes(fullPath, bytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail("Msg.File.SaveFailed", fullPath, ex.Message);
        }

        document.MarkSaved(fullPath, _languages.ForPath(fullPath));
        UpdateTitleSuffixes();
        if (document == _statusSource)
            PushStatus();
        DocumentSaved?.Invoke(this, new DocumentEventArgs(document));
        return OperationResult.Ok;
    }

    private async Task<OperationResult<DocumentViewModel>> LoadAsync(string fullPath)
    {
        EncodingDetection detection;
        try
        {
            // IDE-ARCH-081: file I/O off the UI thread.
            detection = await Task.Run(() => EncodingDetector.Detect(_fileSystem.ReadAllBytes(fullPath)));
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
        Add(document);
        return OperationResult<DocumentViewModel>.Of(document);
    }

    /// <summary>A new tab opens right of the active one (UI-ED-077) and becomes active.</summary>
    private void Add(DocumentViewModel document)
    {
        var index = ActiveDocument is null ? _documents.Count : _documents.IndexOf(ActiveDocument) + 1;
        _documents.Insert(index, document);
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

    private void OnStatusSourceChanged(object? sender, PropertyChangedEventArgs e) => PushStatus();

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
}
