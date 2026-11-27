using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Diff;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Documents;

/// <summary>
/// The two commands that open a diff document (diff-viewer.md §2): <c>file.compareWithSaved</c> (IDE-DIFF-027) and
/// <c>tools.compareFiles</c> (IDE-DIFF-028). The <c>diff.*</c> navigation reaches the diff document through the generic
/// Editor-scope route. UI thread only; files are read off it (IDE-ARCH-081).
/// </summary>
public sealed class CompareService
{
    private const string Section = "diff";
    private const string PromptId = "compareFiles";
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly IDocumentService _documents;
    private readonly IFileSystem _fileSystem;
    private readonly ITextBufferFactory _buffers;
    private readonly ILanguageRegistry _languages;
    private readonly ILayoutService _layout;
    private readonly IStatusService _status;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _pickers;
    private readonly FilePrompts _prompts;
    private readonly IClipboardService _clipboard;

    public CompareService(IDocumentService documents, IFileSystem fileSystem, ITextBufferFactory buffers, ILanguageRegistry languages,
        ILayoutService layout, IStatusService status, IDialogService dialogs, IFileDialogService pickers, FilePrompts prompts,
        IClipboardService clipboard)
    {
        _clipboard = clipboard;
        _documents = documents;
        _fileSystem = fileSystem;
        _buffers = buffers;
        _languages = languages;
        _layout = layout;
        _status = status;
        _dialogs = dialogs;
        _pickers = pickers;
        _prompts = prompts;
    }

    /// <summary>Registers the two commands where their phase-8 delivery applies (IDE-P8-003).</summary>
    public void RegisterCommands(ICommandService commands, int implementedPhase)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (Composition.Phase8Features.IsAvailable("file.compareWithSaved", 8, implementedPhase))
            commands.RegisterHandler("file.compareWithSaved", new Handler(() => SavedTarget() is not null, CompareWithSavedAsync));
        if (Composition.Phase8Features.IsAvailable("tools.compareFiles", 8, implementedPhase))
            commands.RegisterHandler("tools.compareFiles", new Handler(() => true, CompareFilesAsync));
    }

    /// <summary>IDE-DIFF-027: the text of the visible view of the active document, when it has a file on disk.</summary>
    private TextDocumentViewModel? SavedTarget() =>
        _documents.ActiveDocument?.VisibleText is { FilePath: { } path } text && _fileSystem.FileExists(path) ? text : null;

    /// <summary>
    /// IDE-DIFF-027: left = the file on disk decoded as the document is (its encoding), right = the editor text; equal
    /// texts → <c>Msg.Diff.Identical</c> and no tab. An open diff of the same document is activated and refreshed.
    /// </summary>
    public async Task CompareWithSavedAsync()
    {
        if (SavedTarget() is not { FilePath: { } path } text)
            return;
        if (await ReadAsync(path) is not { } bytes)
            return;
        if (FileEncodings.Decode(bytes, text.Encoding) == text.Buffer.Text)
        {
            _status.ShowMessage(Strings.Msg_Diff_Identical, StatusDuration);
            return;
        }

        var name = Path.GetFileName(path);
        var left = new DiffSource(name + " " + Strings.Diff_SavedSuffix, path, async () =>
            await ReadAsync(path) is { } current ? new DiffInput(FileEncodings.Decode(current, text.Encoding)) : null);
        var right = new DiffSource(name + " " + Strings.Diff_EditorSuffix, path, () => Task.FromResult<DiffInput?>(new DiffInput(text.Buffer.Text)));
        if (_documents.Documents.OfType<DiffDocumentViewModel>().FirstOrDefault(d => d.Title == left.Label + " ↔ " + right.Label
                && string.Equals(d.LeftSource.Path, path, StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            _documents.Activate(open);
            await open.RefreshAsync();
            return;
        }

        await OpenAsync(new DiffDocumentViewModel(left, right, text.Language, text.Language, _buffers, _layout, _status, _clipboard));
    }

    /// <summary>
    /// IDE-DIFF-028: <c>Select Left File</c>, then <c>Select Right File</c> (last folder: UI state <c>diff.lastFolder</c>);
    /// a binary file → <c>Msg.Diff.Binary</c> and no tab; otherwise a diff tab, also for identical texts (banner).
    /// </summary>
    public async Task CompareFilesAsync()
    {
        if (await PickAsync(Strings.FilePrompt_Title_compareLeft) is not { } leftPath)
            return;
        if (await PickAsync(Strings.FilePrompt_Title_compareRight) is not { } rightPath)
            return;
        var left = new DiffSource(Path.GetFileName(leftPath), leftPath, () => ReadTextAsync(leftPath));
        var right = new DiffSource(Path.GetFileName(rightPath), rightPath, () => ReadTextAsync(rightPath));
        await OpenAsync(new DiffDocumentViewModel(left, right, _languages.ForPath(leftPath), _languages.ForPath(rightPath), _buffers, _layout,
            _status, _clipboard, showIdenticalBanner: true));
    }

    private async Task OpenAsync(DiffDocumentViewModel diff)
    {
        if (await diff.LoadAsync())
            _documents.OpenDocument(diff);
    }

    private async Task<string?> PickAsync(string title)
    {
        var initial = _prompts.InitialFolder(PromptId, _layout.GetUiValue(Section, "lastFolder"));
        var chosen = await _pickers.PickOpenFilesAsync(new FilePickerRequest(PromptId, title,
            [new FileFilter(Strings.FilePrompt_Filter_All, ["*.*"])], initial));
        if (chosen.Count == 0)
            return null;
        if (Path.GetDirectoryName(chosen[0]) is { Length: > 0 } folder)
            _layout.SetUiValue(Section, "lastFolder", folder);
        return chosen[0];
    }

    /// <summary>The file's text as detected on open (IDE-P1-071), with its encoding name for the notes; null after a message.</summary>
    private async Task<DiffInput?> ReadTextAsync(string path)
    {
        if (await ReadAsync(path) is not { } bytes)
            return null;
        var detected = EncodingDetector.Detect(bytes);
        if (detected.IsBinary)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Diff.Binary", [path]));
            return null;
        }

        return new DiffInput(detected.Text, FileEncodings.DisplayName(detected.Encoding));
    }

    /// <summary>The file's bytes, or null after <c>Msg.File.NotFound</c> / <c>Msg.File.OpenFailed</c>.</summary>
    private async Task<byte[]?> ReadAsync(string path)
    {
        try
        {
            return await Task.Run(() => _fileSystem.ReadAllBytes(path));
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

    private sealed class Handler(Func<bool> canExecute, Func<Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => canExecute() ? execute() : Task.CompletedTask;
    }
}
