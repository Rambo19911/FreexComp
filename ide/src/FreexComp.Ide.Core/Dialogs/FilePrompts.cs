using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Core.Layout;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// The picker prompts of dialogs.md §5 (<c>openFile</c>, <c>openProject</c>, <c>saveAs</c>, <c>addExisting</c>,
/// <c>folder.&lt;fieldId&gt;</c>): titles, filters and the initial-folder rule UI-DLG-051 — the first existing of
/// (1) the prompt's stated folder, (2) its last folder (<c>dialogs.lastFolder.&lt;promptId&gt;</c>), (3) the open
/// project's directory, (4) <c>environment.defaultProjectsDir</c> (empty = <c>Documents\FreexComp Projects</c>),
/// (5) the Documents folder. A successful pick stores its folder for (2). UI thread only.
/// </summary>
public sealed class FilePrompts
{
    private static readonly FileFilter AllFiles = new(Strings.FilePrompt_Filter_All, ["*.*"]);

    private readonly IFileDialogService _pickers;
    private readonly IFileSystem _fileSystem;
    private readonly ILayoutService _layout;
    private readonly ISettingsService _settings;
    private readonly IUserPaths _paths;
    private readonly Func<string?> _projectDirectory;
    private readonly Func<string?, bool> _isProjectMember;

    /// <param name="projectDirectory">The open project's directory, null without a project (rule 3).</param>
    /// <param name="isProjectMember">True for a file of the open project (Save As is "Save Copy As", IDE-PM-080).</param>
    public FilePrompts(IFileDialogService pickers, IFileSystem fileSystem, ILayoutService layout, ISettingsService settings,
        IUserPaths paths, Func<string?> projectDirectory, Func<string?, bool> isProjectMember)
    {
        _isProjectMember = isProjectMember;
        _pickers = pickers;
        _fileSystem = fileSystem;
        _layout = layout;
        _settings = settings;
        _paths = paths;
        _projectDirectory = projectDirectory;
    }

    /// <summary><c>file.open</c>: multi-select; stated folder = the active document's folder when it is saved.</summary>
    public Task<IReadOnlyList<string>> OpenFileAsync(string? activeDocumentPath) =>
        OpenAsync("openFile", Strings.FilePrompt_Title_openFile, multiple: true,
            [
                new FileFilter(Strings.FilePrompt_Filter_Source, ["*.pas", "*.inc", "*.form"]),
                new FileFilter(Strings.FilePrompt_Filter_Project, ["*.pproj"]),
                new FileFilter(Strings.FilePrompt_Filter_Text, ["*.txt", "*.md", "*.json"]),
                AllFiles,
            ],
            activeDocumentPath is null ? null : Path.GetDirectoryName(activeDocumentPath));

    /// <summary><c>file.openProject</c>: single file, no stated folder.</summary>
    public async Task<string?> OpenProjectAsync() =>
        (await OpenAsync("openProject", Strings.FilePrompt_Title_openProject, multiple: false,
            [new FileFilter(Strings.FilePrompt_Filter_Projects, ["*.pproj"]), AllFiles], stated: null)).FirstOrDefault();

    /// <summary><c>project.addExistingFile</c>: multi-select; stated folder = the project directory.</summary>
    public Task<IReadOnlyList<string>> AddExistingAsync() =>
        OpenAsync("addExisting", Strings.FilePrompt_Title_addExisting, multiple: true,
            [new FileFilter(Strings.FilePrompt_Filter_PascalUnits, ["*.pas"]), AllFiles], _projectDirectory());

    /// <summary>
    /// <c>file.saveAs</c> and the first save of an untitled document: filters by document type (default extension =
    /// first pattern), suggested name = title, stated folder = the document's folder (untitled: project directory).
    /// For a project member the title is "Save Copy As" (IDE-PM-080, UI-DLG-055).
    /// </summary>
    public async Task<string?> SaveAsAsync(TextDocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        IReadOnlyList<FileFilter> filters = document.Language.Id switch
        {
            "pascal" => [new FileFilter(Strings.FilePrompt_Filter_PascalUnit, ["*.pas"]), new FileFilter(Strings.FilePrompt_Filter_Include, ["*.inc"]), AllFiles],
            "form" => [new FileFilter(Strings.FilePrompt_Filter_Form, ["*.form"])],
            _ => [AllFiles],
        };
        var first = filters[0].Patterns[0];
        var title = _isProjectMember(document.FilePath) ? Strings.Picker_SaveCopyAs : Strings.FilePrompt_Title_saveAs;
        var request = new FilePickerRequest("saveAs", title, filters,
            InitialFolder("saveAs", document.FilePath is { } path ? Path.GetDirectoryName(path) : _projectDirectory()),
            SuggestedName: document.Title,
            DefaultExtension: first == "*.*" ? null : first[2..]);
        var chosen = await _pickers.PickSaveFileAsync(request);
        Remember("saveAs", chosen is null ? null : Path.GetDirectoryName(chosen));
        return chosen;
    }

    /// <summary>The folder picker of a path field (<c>folder.&lt;fieldId&gt;</c>); stated folder = the field's value.</summary>
    public async Task<string?> PickFolderAsync(string fieldId, string? currentValue)
    {
        ArgumentNullException.ThrowIfNull(fieldId);
        var promptId = "folder." + fieldId;
        var stated = string.IsNullOrWhiteSpace(currentValue) ? null : currentValue;
        var chosen = await _pickers.PickFolderAsync(new FilePickerRequest(promptId, Strings.FilePrompt_Title_folder, [], InitialFolder(promptId, stated)));
        Remember(promptId, chosen);
        return chosen;
    }

    /// <summary><c>environment.defaultProjectsDir</c>; empty = <see cref="BuiltInProjectsDirectory"/>.</summary>
    public string DefaultProjectsDirectory =>
        _settings.Get(SettingsCatalog.EnvironmentDefaultProjectsDir) is { Length: > 0 } configured
            ? configured
            : BuiltInProjectsDirectory;

    /// <summary><c>Documents\FreexComp Projects</c>: the default project folder while the setting is empty.</summary>
    public string BuiltInProjectsDirectory => Path.Combine(_paths.DocumentsDirectory, "FreexComp Projects");

    /// <summary>UI-DLG-051: the first existing candidate folder, or null (the OS default).</summary>
    public string? InitialFolder(string promptId, string? stated)
    {
        string?[] candidates =
        [
            stated,
            _layout.GetLastFolder(promptId),
            _projectDirectory(),
            DefaultProjectsDirectory,
            _paths.DocumentsDirectory,
        ];
        return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && Path.IsPathFullyQualified(c) && _fileSystem.DirectoryExists(c));
    }

    private async Task<IReadOnlyList<string>> OpenAsync(string promptId, string title, bool multiple, IReadOnlyList<FileFilter> filters, string? stated)
    {
        var chosen = await _pickers.PickOpenFilesAsync(new FilePickerRequest(promptId, title, filters, InitialFolder(promptId, stated), multiple));
        Remember(promptId, chosen.Count == 0 ? null : Path.GetDirectoryName(chosen[^1]));
        return chosen;
    }

    private void Remember(string promptId, string? folder)
    {
        if (!string.IsNullOrEmpty(folder))
            _layout.SetLastFolder(promptId, folder);
    }
}
