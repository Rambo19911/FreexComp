using System.Globalization;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// One row of the <c>Ancestors</c> list (UI-DLG-316): a class of the project's ancestor view, its kind and its unit —
/// <paramref name="FullPath"/> absolute, <paramref name="UnitPath"/> relative to the project folder.
/// </summary>
public sealed record NewInheritedFormRow(string ClassName, bool IsFrame, string FullPath, string UnitPath)
{
    /// <summary>The class name without the leading <c>T</c> (the list's name column).</summary>
    public string Name => ClassName.Length > 1 && ClassName[0] is 'T' or 't' ? ClassName[1..] : ClassName;

    public string Kind => IsFrame ? Strings.Dialog_NewInheritedForm_KindFrame : Strings.Dialog_NewInheritedForm_KindForm;

    /// <summary>UI-DLG-322: <c>{Name}, {Kind}, {UnitPath}</c>.</summary>
    public string AccessibleName => $"{Name}, {Kind}, {UnitPath}";
}

/// <summary>
/// The New Inherited Form dialog (form-inheritance-designer.md §12.2, UI-DLG-316…322): the project's forms, then its
/// frames, each by class name, with a filter (class name without <c>T</c> or unit path, case-insensitive substring); the
/// active document's class preselected when listed, else the first row; the preview of the names DSG-CS-125 gives. OK
/// (enabled with a selected row) creates the unit and form and closes on success (result true); a failed write (already
/// reported) keeps the dialog open. The list is the catalog's state when the dialog opens (UI-DLG-319). 520 × 380,
/// resizable down to 400 × 280; the size is remembered (<c>dialogs.NewInheritedForm.size</c>).
/// </summary>
public sealed class NewInheritedFormViewModel : DialogViewModel<bool>
{
    public const string DialogName = "NewInheritedForm";

    private readonly Func<string, string> _newName;
    private readonly Func<NewInheritedFormRow, string, Task<bool>> _create;
    private readonly ILayoutService? _layout;
    private string _filter = "";
    private IReadOnlyList<NewInheritedFormRow> _visible;
    private NewInheritedFormRow? _selected;
    private string? _selectedName;
    private bool _creating;

    /// <param name="rows">The classes of the ancestor view (any order).</param>
    /// <param name="activeClass">The class of the active form document, or null.</param>
    /// <param name="newName">The new name (DSG-CS-125) for an ancestor class.</param>
    /// <param name="create">Creates the unit and form for a row and its new name; false when it failed (already reported).</param>
    public NewInheritedFormViewModel(IEnumerable<NewInheritedFormRow> rows, string? activeClass, Func<string, string> newName,
        Func<NewInheritedFormRow, string, Task<bool>> create, ILayoutService? layout = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _newName = newName ?? throw new ArgumentNullException(nameof(newName));
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _layout = layout;
        Rows = [.. rows.OrderBy(r => r.IsFrame).ThenBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase)];
        _visible = Rows;
        Ok = new DialogButtonViewModel("Ok", Strings.Button_OK, CommitAsync) { IsDefault = true, IsAccent = true };
        Buttons = [Ok, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(false)) { IsCancel = true }];
        Selected = (activeClass is null ? null : Rows.FirstOrDefault(r => string.Equals(r.ClassName, activeClass, StringComparison.OrdinalIgnoreCase)))
            ?? Rows.FirstOrDefault();
        Ok.IsEnabled = _selected is not null;
    }

    public override string Name => DialogName;

    public override string Title => Strings.Dialog_NewInheritedForm_Title;

    public override double Width => 520;

    public override double? Height => 380;

    public override bool CanResize => true;

    public override (double Width, double Height)? MinimumSize => (400, 280);

    public override (double Width, double Height)? StoredSize
    {
        get => StoredDialogSize.Read(_layout, DialogName);
        set => StoredDialogSize.Write(_layout, DialogName, value);
    }

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Ok { get; }

    /// <summary>Every row: forms, then frames, each by class name.</summary>
    public IReadOnlyList<NewInheritedFormRow> Rows { get; }

    /// <summary>The rows matching <see cref="Filter"/>, in the order of <see cref="Rows"/>.</summary>
    public IReadOnlyList<NewInheritedFormRow> VisibleRows
    {
        get => _visible;
        private set => SetProperty(ref _visible, value);
    }

    /// <summary>UI-DLG-317: a changed filter moves the selection to the first matching row (none: OK disabled).</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _filter, value))
                return;
            VisibleRows = [.. Rows.Where(Matches)];
            OnPropertyChanged(nameof(Placeholder));
            Selected = VisibleRows.FirstOrDefault();
        }
    }

    public NewInheritedFormRow? Selected
    {
        get => _selected;
        set
        {
            if (value is not null && !VisibleRows.Contains(value))
                value = null;
            if (ReferenceEquals(value, _selected))
                return;
            _selectedName = value is null ? null : _newName(value.ClassName); // before the change is announced
            Ok.IsEnabled = value is not null;
            SetProperty(ref _selected, value);
            OnPropertyChanged(nameof(NewName));
            OnPropertyChanged(nameof(Preview));
        }
    }

    /// <summary>The name of the new class without <c>T</c> and of its unit (DSG-CS-125), or null without a selection.</summary>
    public string? NewName => _selectedName;

    /// <summary><c>New class: T{Name}    Unit: {Unit}.pas</c>; empty when no row is selected.</summary>
    public string Preview => NewName is { } name
        ? string.Format(CultureInfo.CurrentCulture, Strings.Dialog_NewInheritedForm_Preview, name, name)
        : "";

    /// <summary>The list's placeholder: no rows at all, or none matching the filter; null while rows are shown.</summary>
    public string? Placeholder => Rows.Count == 0 ? Strings.Msg_NewInheritedForm_NoAncestors
        : VisibleRows.Count == 0 ? Strings.Msg_NewInheritedForm_NoMatch
        : null;

    private bool Matches(NewInheritedFormRow row) => _filter.Length == 0
        || row.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)
        || row.UnitPath.Contains(_filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>UI-DLG-318: creates the files; closes on success, stays open after a failure (already reported).</summary>
    private async Task CommitAsync()
    {
        if (_creating || _selected is not { } row || NewName is not { } name)
            return;
        _creating = true;
        try
        {
            if (await _create(row, name))
                Close(true);
        }
        finally
        {
            _creating = false;
        }
    }
}
