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

    /// <summary><c>file.openProject</c>: single file, no stated folder; projects and project groups (IDE-PM-154).</summary>
    public async Task<string?> OpenProjectAsync() =>
        (await OpenAsync("openProject", Strings.FilePrompt_Title_openProject, multiple: false,
            [new FileFilter(Strings.Filter_ProjectsAndGroups, ["*.pproj", "*.pgroup"]), AllFiles], stated: null)).FirstOrDefault();

    /// <summary><c>project.addExistingProject</c> (IDE-PM-164): several <c>*.pproj</c> files; stated folder = <paramref name="folder"/>.</summary>
    public Task<IReadOnlyList<string>> AddExistingProjectAsync(string? folder) =>
        OpenAsync("addExistingProject", Strings.FilePrompt_Title_addExistingProject, multiple: true,
            [new FileFilter(Strings.FilePrompt_Filter_Projects, ["*.pproj"]), AllFiles], folder);

    /// <summary>
    /// <c>file.saveGroupAs</c> (IDE-PM-167): the save picker for <c>*.pgroup</c> in <paramref name="folder"/> with
    /// <paramref name="suggestedName"/>; null when canceled.
    /// </summary>
    public async Task<string?> SaveGroupAsAsync(string? folder, string suggestedName)
    {
        var request = new FilePickerRequest("saveGroupAs", Strings.FilePrompt_Title_saveGroupAs,
            [new FileFilter(Strings.FilePrompt_Filter_ProjectGroups, ["*.pgroup"])], InitialFolder("saveGroupAs", folder),
            SuggestedName: suggestedName, DefaultExtension: "pgroup");
        var chosen = await _pickers.PickSaveFileAsync(request);
        Remember("saveGroupAs", chosen is null ? null : Path.GetDirectoryName(chosen));
        return chosen;
    }

    /// <summary>The strings editor's <c>Load…</c> (<c>stringsLoad</c>, DSG-OI-130): one file, text files first, no stated folder; null when canceled.</summary>
    public async Task<string?> StringsLoadAsync() =>
        (await OpenAsync("stringsLoad", Strings.FilePrompt_Title_stringsLoad, multiple: false, TextFileFilters, stated: null)).FirstOrDefault();

    /// <summary>
    /// The strings editor's <c>Save…</c> (<c>stringsSave</c>, DSG-OI-132): the save picker for <c>*.txt</c> with
    /// <paramref name="suggestedName"/>; null when canceled.
    /// </summary>
    public async Task<string?> StringsSaveAsync(string suggestedName)
    {
        var request = new FilePickerRequest("stringsSave", Strings.FilePrompt_Title_stringsSave, TextFileFilters, InitialFolder("stringsSave", null),
            SuggestedName: suggestedName, DefaultExtension: "txt");
        var chosen = await _pickers.PickSaveFileAsync(request);
        Remember("stringsSave", chosen is null ? null : Path.GetDirectoryName(chosen));
        return chosen;
    }

    private static IReadOnlyList<FileFilter> TextFileFilters => [new FileFilter(Strings.FilePrompt_Filter_Text, ["*.txt"]), AllFiles];

    /// <summary>The file system the prompts look into: where a dialog reads or writes the file it asked for.</summary>
    internal IFileSystem FileSystem => _fileSystem;

    /// <summary>The settings the prompts read: the new-file encoding and line ending of a file a dialog writes.</summary>
    internal ISettingsService Settings => _settings;

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

    /// <summary>The open project's directory; null without a project.</summary>
    public string? ProjectDirectory => _projectDirectory();

    /// <summary>
    /// The open-file picker of an <c>editor: "filename"</c> property (<c>propertyFile</c>, object-inspector.md §5.1): single
    /// file; <paramref name="filter"/> in the metadata form <c>Description|pattern|Description|pattern</c> (patterns
    /// separated by <c>;</c>; null or empty = all files); stated folder = <paramref name="folder"/>, else the project directory.
    /// </summary>
    public async Task<string?> PropertyFileAsync(string? filter, string? folder) =>
        (await OpenAsync("propertyFile", Strings.FilePrompt_Title_propertyFile, multiple: false, ParseFilter(filter), folder ?? _projectDirectory()))
        .FirstOrDefault();

    /// <summary>The filters of a metadata filter text; a description's trailing <c>(patterns)</c> is dropped (the label adds it).</summary>
    public static IReadOnlyList<FileFilter> ParseFilter(string? filter)
    {
        var parts = (filter ?? "").Split('|');
        var filters = new List<FileFilter>();
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (patterns.Length == 0)
                continue;
            var name = parts[i].Trim();
            if (name.EndsWith(')') && name.LastIndexOf('(') is > 0 and var open)
                name = name[..open].TrimEnd();
            filters.Add(new FileFilter(name, patterns));
        }

        return filters.Count > 0 ? filters : [AllFiles];
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
