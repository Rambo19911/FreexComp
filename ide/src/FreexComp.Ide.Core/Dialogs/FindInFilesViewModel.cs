using System.Globalization;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// The Find in Files dialog (dialogs.md §11), 560 × auto: <c>Find</c> stores the options as the last used ones
/// (UI state <c>findInFiles.*</c> and <c>dialogs.FindInFiles.*</c>), adds the texts to their histories and closes with
/// the request; <c>Cancel</c> remembers nothing (UI-DLG-111). Validation: empty text, an invalid regular expression and,
/// for scope Folder, a missing folder block <c>Find</c>.
/// </summary>
public sealed class FindInFilesViewModel : DialogViewModel<FindInFilesRequest>
{
    public const string DialogName = "FindInFiles";
    private const string Section = "findInFiles";

    private readonly ILayoutService _layout;
    private readonly IFileSystem _fileSystem;
    private readonly IFileDialogService _pickers;
    private string _searchText;
    private bool _matchCase;
    private bool _wholeWord;
    private bool _regex;
    private FindInFilesScope _scope;
    private string _folder;
    private bool _subfolders;
    private string _fileTypes;

    /// <param name="initialText">The selection (single line) or the word at the caret of the active editor; null = none.</param>
    /// <param name="projectDirectory">The open project's directory; null = no project (scope Project disabled).</param>
    public FindInFilesViewModel(ILayoutService layout, IFileSystem fileSystem, IFileDialogService pickers, string? initialText, string? projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        _fileSystem = fileSystem;
        _pickers = pickers;
        HasProject = projectDirectory is not null;
        SearchHistory = layout.GetDialogHistory(DialogName, "SearchText");
        FileTypesHistory = layout.GetDialogHistory(DialogName, "FileTypes");
        _searchText = initialText is { Length: > 0 } ? initialText : SearchHistory.FirstOrDefault() ?? "";
        _matchCase = layout.GetUiFlag(Section, "matchCase") ?? false;
        _wholeWord = layout.GetUiFlag(Section, "wholeWord") ?? false;
        _regex = layout.GetUiFlag(Section, "regex") ?? false;
        _subfolders = layout.GetUiFlag(Section, "subfolders") ?? true;
        _scope = Enum.TryParse<FindInFilesScope>(layout.GetDialogValue(DialogName, "scope"), out var scope) ? scope : FindInFilesScope.Project;
        if (_scope == FindInFilesScope.Project && !HasProject)
            _scope = FindInFilesScope.OpenFiles;
        _folder = layout.GetDialogValue(DialogName, "folder") ?? projectDirectory ?? "";
        _fileTypes = FileTypesHistory.FirstOrDefault() ?? FindInFilesRequest.DefaultMasks;
        Find = new DialogButtonViewModel("Find", Strings.Button_Find, Commit) { IsDefault = true, IsAccent = true };
        Buttons = [Find, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(null)) { IsCancel = true }];
        Validate();
    }

    public override string Name => DialogName;

    public override string Title => Strings.FindInFiles_Title;

    public override double Width => 560;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Find { get; }

    public bool HasProject { get; }

    public IReadOnlyList<string> SearchHistory { get; }

    public IReadOnlyList<string> FileTypesHistory { get; }

    public string SearchText
    {
        get => _searchText;
        set => Set(ref _searchText, value ?? "");
    }

    public bool MatchCase
    {
        get => _matchCase;
        set => Set(ref _matchCase, value);
    }

    public bool WholeWord
    {
        get => _wholeWord;
        set => Set(ref _wholeWord, value);
    }

    public bool Regex
    {
        get => _regex;
        set => Set(ref _regex, value);
    }

    public FindInFilesScope Scope
    {
        get => _scope;
        set
        {
            if (value == FindInFilesScope.Project && !HasProject)
                return;
            if (Set(ref _scope, value))
                OnPropertyChanged(nameof(IsFolderScope));
        }
    }

    /// <summary>The folder fields are enabled.</summary>
    public bool IsFolderScope => Scope == FindInFilesScope.Folder;

    public string Folder
    {
        get => _folder;
        set => Set(ref _folder, value ?? "");
    }

    public bool Subfolders
    {
        get => _subfolders;
        set => Set(ref _subfolders, value);
    }

    public string FileTypes
    {
        get => _fileTypes;
        set => Set(ref _fileTypes, value ?? "");
    }

    /// <summary><c>Error.SearchText</c>: <c>Msg.FindInFiles.TextEmpty</c> or <c>Msg.Find.RegexInvalid</c>; null when valid.</summary>
    public string? SearchTextMessage { get; private set; }

    /// <summary><c>Error.Folder</c>: <c>Msg.FindInFiles.FolderMissing</c> (scope Folder only); null when valid.</summary>
    public string? FolderMessage { get; private set; }

    public bool IsValid => SearchTextMessage is null && FolderMessage is null;

    /// <summary>The <c>Folder.Browse</c> button: the folder picker.</summary>
    public async Task BrowseAsync()
    {
        var initial = _fileSystem.DirectoryExists(Folder) ? Folder : null;
        var picked = await _pickers.PickFolderAsync(new FilePickerRequest(DialogName + ".Folder", Strings.FindInFiles_FolderPicker, [], initial));
        if (picked is not null)
            Folder = picked;
    }

    /// <summary>The request of the current fields (masks: empty = <c>*.*</c>).</summary>
    public FindInFilesRequest ToRequest() => new(new FindQuery(SearchText, MatchCase, WholeWord, Regex), Scope,
        Scope == FindInFilesScope.Folder ? Folder.Trim() : null, Subfolders, FileTypes.Trim().Length == 0 ? "*.*" : FileTypes.Trim());

    private bool Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name))
            return false;
        Validate();
        return true;
    }

    private void Validate()
    {
        SearchTextMessage = null;
        if (SearchText.Length == 0)
        {
            SearchTextMessage = Strings.Msg_FindInFiles_TextEmpty;
        }
        else if (Regex)
        {
            try
            {
                TextFinder.CreateRegex(new FindQuery(SearchText, MatchCase, WholeWord, true));
            }
            catch (ArgumentException e)
            {
                SearchTextMessage = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Find_RegexInvalid, e.Message);
            }
        }

        FolderMessage = IsFolderScope && (Folder.Trim().Length == 0 || !IsValidPath(Folder.Trim()) || !_fileSystem.DirectoryExists(Folder.Trim()))
            ? Strings.Msg_FindInFiles_FolderMissing
            : null;
        OnPropertyChanged(nameof(SearchTextMessage));
        OnPropertyChanged(nameof(FolderMessage));
        OnPropertyChanged(nameof(IsValid));
        Find.IsEnabled = IsValid;
    }

    private static bool IsValidPath(string path) => path.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(path);

    private void Commit()
    {
        if (!IsValid)
            return;
        _layout.SetUiFlag(Section, "matchCase", MatchCase);
        _layout.SetUiFlag(Section, "wholeWord", WholeWord);
        _layout.SetUiFlag(Section, "regex", Regex);
        _layout.SetUiFlag(Section, "subfolders", Subfolders);
        _layout.SetDialogValue(DialogName, "scope", Scope.ToString());
        if (Scope == FindInFilesScope.Folder)
            _layout.SetDialogValue(DialogName, "folder", Folder.Trim());
        _layout.AddDialogHistory(DialogName, "SearchText", SearchText);
        if (FileTypes.Trim().Length > 0)
            _layout.AddDialogHistory(DialogName, "FileTypes", FileTypes.Trim());
        Close(ToRequest());
    }
}
