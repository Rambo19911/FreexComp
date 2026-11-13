using FreexComp.Ide.Core.Dialogs;

namespace FreexComp.Ide.Tests.Fixtures.ViewModels;

/// <summary>
/// A dialog that exists only in tests: a required single-line field, a multi-line field and a check box; OK returns
/// the name, Cancel null. Its window, <c>FreexComp.Ide.Tests.Fixtures.Views.SampleDialogWindow</c>, is found by the
/// dialog service's naming convention.
/// </summary>
public sealed class SampleDialogViewModel : DialogViewModel<string>
{
    private string _name = "";
    private string _notes = "";
    private bool _option;

    public SampleDialogViewModel()
    {
        Ok = new DialogButtonViewModel("Ok", "OK", () => Close(FieldName)) { IsDefault = true, IsAccent = true, IsEnabled = false };
        Cancel = new DialogButtonViewModel("Cancel", "Cancel", () => Close(null)) { IsCancel = true };
        Buttons = [Ok, Cancel];
    }

    public override string Name => "Sample";

    public override string Title => "Sample";

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Ok { get; }

    public DialogButtonViewModel Cancel { get; }

    public string FieldName
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                Ok.IsEnabled = value.Length > 0; // UI-DLG-006: the default button is disabled while invalid
            }
        }
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public bool Option
    {
        get => _option;
        set => SetProperty(ref _option, value);
    }
}
