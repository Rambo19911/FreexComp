using System.Globalization;
using FreexComp.Compiler.Syntax;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>The file types of the New File dialog (dialogs.md §4); Form joins in P5.</summary>
public enum NewFileKind
{
    Unit,
    TextFile,
}

/// <summary>One row of the <c>Types</c> list: <c>Dialog.NewFile.Types.&lt;Id&gt;</c>.</summary>
public sealed record NewFileType(NewFileKind Kind, string Id, string Label, string Description);

/// <summary>
/// The New File dialog (dialogs.md §4, UI-DLG-040…042): type, name, folder and "Add to project". Fields are validated on
/// every change; <c>Create</c> runs the creation and closes on success (result true); a failure (already reported) keeps
/// the dialog open.
/// </summary>
public sealed class NewFileViewModel : DialogViewModel<bool>
{
    public const string DialogName = "NewFile";

    /// <summary>dialogs.md §24 <c>Msg.NewFile.NameInvalid</c>.</summary>
    private static readonly char[] InvalidFileNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private readonly IFileSystem _fileSystem;
    private readonly Func<string, bool> _isProjectUnit;
    private readonly Func<NewFileViewModel, Task<bool>> _create;
    private readonly Func<string?, Task<string?>> _browse;
    private NewFileType _selectedType;
    private string _name = "";
    private string _folder;
    private bool _nameEdited;
    private bool _addToProject = true;
    private string? _nameError;
    private string? _folderError;
    private bool _creating;

    /// <param name="folder">The initial folder (UI-DLG-040 table).</param>
    /// <param name="projectName">The open project's name; null hides <c>AddToProject</c>.</param>
    /// <param name="isProjectUnit">True when the open project already has a unit of that name (case-insensitive).</param>
    /// <param name="create">Creates <see cref="FilePath"/>; false when it failed (already reported).</param>
    /// <param name="browse">The folder picker for <c>Folder</c> (current value → chosen folder or null).</param>
    public NewFileViewModel(string folder, string? projectName, IFileSystem fileSystem, Func<string, bool> isProjectUnit,
        Func<NewFileViewModel, Task<bool>> create, Func<string?, Task<string?>> browse)
    {
        ArgumentNullException.ThrowIfNull(folder);
        _folder = folder;
        ProjectName = projectName;
        _fileSystem = fileSystem;
        _isProjectUnit = isProjectUnit;
        _create = create;
        _browse = browse;
        Types =
        [
            new NewFileType(NewFileKind.Unit, "Unit", Strings.NewFile_Type_Unit, Strings.NewFile_Type_Unit_Description),
            new NewFileType(NewFileKind.TextFile, "TextFile", Strings.NewFile_Type_TextFile, Strings.NewFile_Type_TextFile_Description),
        ];
        _selectedType = Types[0];
        Create = new DialogButtonViewModel("Ok", Strings.NewProject_Create, () => CommitAsync())
        {
            IsDefault = true,
            IsAccent = true,
            AccessLabel = "_" + Strings.NewProject_Create,
        };
        Buttons = [Create, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(false)) { IsCancel = true }];
        _name = DefaultName();
        Validate();
    }

    public override string Name => DialogName;

    public override string Title => Strings.NewFile_Title;

    public override double Width => 520;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Create { get; }

    public IReadOnlyList<NewFileType> Types { get; }

    /// <summary>UI-DLG-042: a new type recomputes the default name unless the user has edited it.</summary>
    public NewFileType SelectedType
    {
        get => _selectedType;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _selectedType, value))
                return;
            OnPropertyChanged(nameof(Extension));
            if (!_nameEdited)
                SetProperty(ref _name, DefaultName(), nameof(FileBaseName));
            Validate();
        }
    }

    /// <summary>The <c>Name</c> field (the user's text; an edit stops the default-name recomputation).</summary>
    public string FileBaseName
    {
        get => _name;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _name, value))
                return;
            _nameEdited = true;
            Validate();
        }
    }

    public string Folder
    {
        get => _folder;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _folder, value))
                Validate();
        }
    }

    public string? ProjectName { get; }

    public bool ShowAddToProject => ProjectName is not null;

    /// <summary><c>&amp;Add to project &lt;name&gt;</c> with the mnemonic as <c>_A</c>.</summary>
    public string AddToProjectLabel => string.Format(CultureInfo.CurrentCulture, Strings.NewFile_AddToProject, ProjectName);

    public bool AddToProject
    {
        get => _addToProject;
        set => SetProperty(ref _addToProject, value);
    }

    /// <summary>The read-only suffix after <c>Name</c>: <c>.pas</c> for a unit, null (hidden) for a text file.</summary>
    public string? Extension => SelectedType.Kind == NewFileKind.Unit ? ".pas" : null;

    /// <summary>The file name created: a unit's name + <c>.pas</c>; a text file's name, <c>.txt</c> appended when it has no extension.</summary>
    public string FileName => SelectedType.Kind == NewFileKind.Unit ? _name + ".pas"
        : Path.HasExtension(_name) ? _name : _name + ".txt";

    public string FilePath => Path.Combine(_folder, FileName);

    public string? NameError
    {
        get => _nameError;
        private set => SetProperty(ref _nameError, value);
    }

    public string? FolderError
    {
        get => _folderError;
        private set => SetProperty(ref _folderError, value);
    }

    /// <summary>The Browse button of <c>Folder</c>: the folder picker at the field's value.</summary>
    public async Task BrowseAsync()
    {
        if (await _browse(_folder) is { } chosen)
            Folder = chosen;
    }

    /// <summary><c>Create</c> / Enter: only while valid; closes when the creation succeeded.</summary>
    public async Task CommitAsync()
    {
        if (IsClosed || _creating || NameError is not null || FolderError is not null)
            return;
        _creating = true;
        try
        {
            if (await _create(this))
                Close(true);
        }
        finally
        {
            _creating = false;
        }
    }

    /// <summary>UI-DLG-040 table: smallest n ≥ 1 not clashing with a file in the folder nor (unit) a project unit.</summary>
    private string DefaultName()
    {
        for (var n = 1; ; n++)
        {
            var candidate = SelectedType.Kind == NewFileKind.Unit
                ? string.Create(CultureInfo.InvariantCulture, $"Unit{n}")
                : string.Create(CultureInfo.InvariantCulture, $"Untitled{n}.txt");
            var file = SelectedType.Kind == NewFileKind.Unit ? candidate + ".pas" : candidate;
            if ((SelectedType.Kind != NewFileKind.Unit || !_isProjectUnit(candidate)) && !FolderHasFile(file))
                return candidate;
        }
    }

    private bool FolderHasFile(string fileName) =>
        FolderSyntaxValid(_folder) && _fileSystem.FileExists(Path.Combine(_folder, fileName));

    private static bool FolderSyntaxValid(string folder) =>
        folder.Length > 0 && folder.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(folder);

    private void Validate()
    {
        FolderError = !FolderSyntaxValid(_folder) ? Strings.Msg_Project_LocationInvalid
            : !_fileSystem.DirectoryExists(_folder) ? Strings.Msg_Options_PathNotFound
            : null;
        NameError = NameResult() is { Success: false } failed ? NewProjectValidation.MessageText(failed) : null;
        Create.IsEnabled = NameError is null && FolderError is null;
    }

    private OperationResult NameResult()
    {
        if (SelectedType.Kind == NewFileKind.Unit)
        {
            if (!IsIdentifier(_name))
                return OperationResult.Fail("Msg.Project.RenameInvalid");
            if (SyntaxFacts.IsReservedWord(_name))
                return OperationResult.Fail("Msg.Project.NameReserved", _name);
            if (_isProjectUnit(_name))
                return OperationResult.Fail("Msg.Project.DuplicateUnitName", _name);
        }
        else if (_name.Length == 0 || _name.IndexOfAny(InvalidFileNameChars) >= 0)
        {
            return OperationResult.Fail("Msg.NewFile.NameInvalid");
        }

        return FolderError is null && _fileSystem.FileExists(FilePath) ? OperationResult.Fail("Msg.Project.RenameExists", FileName) : OperationResult.Ok;
    }

    private static bool IsIdentifier(string name) =>
        name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
