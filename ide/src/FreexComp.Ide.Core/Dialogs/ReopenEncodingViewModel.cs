using System.Globalization;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// Reopen with Encoding (dialogs.md section 18): the current encoding, the choice of the five encodings and, for a modified
/// document, the discard warning (the default button then reads "Discard Changes and Reopen"). Result = the chosen
/// encoding, null when canceled. 440 x auto.
/// </summary>
public sealed class ReopenEncodingViewModel : DialogViewModel<FileEncoding?>
{
    public const string DialogName = "ReopenEncoding";

    private FileEncoding _selected;

    /// <param name="proposed">The encoding auto-detection would choose for the file on disk.</param>
    public ReopenEncodingViewModel(string fileName, FileEncoding current, FileEncoding proposed, bool isModified)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        CurrentText = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Encoding_CurrentlyReadAs, fileName, FileEncodings.DisplayName(current));
        _selected = proposed != current ? proposed : FileEncoding.Utf8;
        ShowWarning = isModified;
        var label = isModified ? Strings.ReopenEncoding_DiscardAndReopen : Strings.ReopenEncoding_Reopen;
        Buttons =
        [
            new DialogButtonViewModel("Ok", label, () => Close(_selected)) { IsDefault = true, IsAccent = true, AccessLabel = "_" + label },
            new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(null)) { IsCancel = true },
        ];
    }

    public override string Name => DialogName;

    public override string Title => Strings.ReopenEncoding_Title;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    /// <summary><c>Msg.Encoding.CurrentlyReadAs</c>.</summary>
    public string CurrentText { get; }

    /// <summary><c>Msg.Encoding.DiscardWarning</c> is shown (the document is modified).</summary>
    public bool ShowWarning { get; }

    /// <summary>The combo's entries in order (dialogs.md section 18).</summary>
    public IReadOnlyList<FileEncoding> Encodings { get; } = Enum.GetValues<FileEncoding>();

    public FileEncoding Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }
}
