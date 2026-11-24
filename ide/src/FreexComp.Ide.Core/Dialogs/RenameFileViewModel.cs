using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// Rename of a project file (dialogs.md §7, UI-DLG-070…073), shared by the <c>RenameFile</c> dialog and the in-place editor
/// of the Project Manager (UI-TW-112). The name is validated on every change; <see cref="CommitAsync"/> performs the
/// rename and closes on success (result true); a failure (reported by the rename) keeps it open.
/// </summary>
public sealed class RenameFileViewModel : DialogViewModel<bool>
{
    public const string DialogName = "RenameFile";

    private readonly Func<string, OperationResult> _validate;
    private readonly Func<string, bool, Task<bool>> _rename;
    private string _newName;
    private string? _error;
    private bool _updateReferences = true;
    private bool _committing;

    /// <param name="fileName">The current file name.</param>
    /// <param name="validate">IDE-PM-070 validation of a new name.</param>
    /// <param name="rename">Performs the rename (new name, update the heading); false when it failed (already reported).</param>
    public RenameFileViewModel(string fileName, Func<string, OperationResult> validate, Func<string, bool, Task<bool>> rename)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        OriginalName = fileName;
        _newName = fileName;
        _validate = validate;
        _rename = rename;
        IsUnit = Path.GetExtension(fileName).Equals(".pas", StringComparison.OrdinalIgnoreCase);
        Rename = new DialogButtonViewModel("Ok", Strings.RenameFile_Rename, () => CommitAsync())
        {
            IsDefault = true,
            IsAccent = true,
            IsEnabled = false,
            AccessLabel = "_" + Strings.RenameFile_Rename,
        };
        Buttons = [Rename, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(false)) { IsCancel = true }];
    }

    public override string Name => DialogName;

    public override string Title => Strings.RenameFile_Title;

    public override double Width => 420;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Rename { get; }

    public string OriginalName { get; }

    /// <summary>Length of the base name (without the extension), selected when editing starts (UI-DLG-071, UI-TW-112).</summary>
    public int BaseNameLength => Path.GetFileNameWithoutExtension(OriginalName).Length;

    /// <summary>A Pascal unit: the <c>UpdateReferences</c> check box is shown (UI-DLG-072).</summary>
    public bool IsUnit { get; }

    public bool UpdateReferences
    {
        get => _updateReferences;
        set => SetProperty(ref _updateReferences, value);
    }

    public string NewName
    {
        get => _newName;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _newName, value))
                return;
            var result = _validate(value);
            Error = result.Success ? null : NewProjectValidation.MessageText(result);
            Rename.IsEnabled = Error is null && value != OriginalName;
        }
    }

    /// <summary>The validation message (<c>Dialog.RenameFile.Error.NewName</c>, the in-place popup); null when valid.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>
    /// Enter, focus loss (in place) or <c>Rename</c>: an unchanged name closes without renaming; an invalid one keeps
    /// the editor open; otherwise the rename runs and closes on success. True when closed.
    /// </summary>
    public async Task<bool> CommitAsync()
    {
        if (IsClosed || _committing)
            return IsClosed;
        if (NewName == OriginalName)
        {
            Close(false);
            return true;
        }

        if (Error is not null)
            return false;
        _committing = true;
        try
        {
            if (await _rename(NewName, UpdateReferences))
                Close(true);
        }
        finally
        {
            _committing = false;
        }

        return IsClosed;
    }

    /// <summary>Escape: closes without renaming.</summary>
    public void Cancel() => Close(false);

    /// <summary>
    /// UI-TW-116: in-place naming of a new folder cancels an invalid name on focus loss (the provisional name stays)
    /// instead of keeping the editor open.
    /// </summary>
    public bool CancelInvalidOnFocusLoss { get; init; }

    /// <summary>The in-place editor lost focus: commits (UI-TW-112), or cancels per <see cref="CancelInvalidOnFocusLoss"/>.</summary>
    public Task FocusLostAsync()
    {
        if (CancelInvalidOnFocusLoss && Error is not null)
        {
            Cancel();
            return Task.CompletedTask;
        }

        return CommitAsync();
    }
}
