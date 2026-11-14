using System.Globalization;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>The accepted Go to Line input: a line in range and an optional column (null = first non-blank).</summary>
public sealed record GoToLineTarget(int Line, int? Column);

/// <summary>
/// The Go to Line dialog (dialogs.md §12), 320 × auto: <c>Line</c> starts with the caret line; every change is validated
/// (<see cref="GoToLineInput"/>), an invalid entry shows <c>Msg.GoToLine.OutOfRange</c> and disables Go (UI-DLG-006).
/// </summary>
public sealed class GoToLineViewModel : DialogViewModel<GoToLineTarget>
{
    public const string DialogName = "GoToLine";

    private string _line;
    private string? _error;

    public GoToLineViewModel(int lineCount, int caretLine)
    {
        LineCount = lineCount;
        _line = caretLine.ToString(CultureInfo.InvariantCulture);
        Go = new DialogButtonViewModel("Ok", Strings.GoToLine_Go, Commit) { IsDefault = true, IsAccent = true, AccessLabel = "_" + Strings.GoToLine_Go };
        Buttons = [Go, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(null)) { IsCancel = true }];
    }

    public override string Name => DialogName;

    public override string Title => Strings.GoToLine_Title;

    public override double Width => 320;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Go { get; }

    public int LineCount { get; }

    /// <summary>The label <c>&amp;Line (1–{n}):</c> with its mnemonic as <c>_L</c>.</summary>
    public string Label => string.Format(CultureInfo.CurrentCulture, Strings.GoToLine_Line, LineCount);

    public string Line
    {
        get => _line;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _line, value))
                return;
            Error = GoToLineInput.TryParse(value, LineCount, out _, out _) ? null : GoToLineInput.OutOfRangeMessage(LineCount);
            Go.IsEnabled = Error is null;
        }
    }

    /// <summary>The validation message (<c>Dialog.GoToLine.Error.Line</c>); null when valid.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    private void Commit()
    {
        if (GoToLineInput.TryParse(Line, LineCount, out var line, out var column))
            Close(new GoToLineTarget(line, column));
    }
}
