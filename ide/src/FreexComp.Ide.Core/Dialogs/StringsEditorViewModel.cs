using System.Globalization;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// The strings editor (object-inspector.md §5.5, DSG-OI-055): a multi-line text of a <c>TStrings</c> property, resizable,
/// min 480 × 240. The result is the list of lines (CR LF, LF or CR separate them; a single trailing line break adds no
/// line); Cancel, Escape or × give null. Enter inserts a line break, Ctrl+Enter is OK (the window's).
/// </summary>
public sealed class StringsEditorViewModel : DialogViewModel<IReadOnlyList<string>>
{
    public const string DialogName = "StringsEditor";

    private string _text;

    /// <param name="property">The property name (<c>Lines</c>).</param>
    /// <param name="component">The component name, or <c>{n} components</c> for a multi-selection.</param>
    /// <param name="lines">The initial lines (of the primary component).</param>
    public StringsEditorViewModel(string property, string component, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(lines);
        Title = string.Format(CultureInfo.CurrentCulture, Strings.StringsEditor_Title, property, component);
        _text = string.Join("\r\n", lines);
        Ok = new DialogButtonViewModel("Ok", Strings.Button_OK, () => Close(Split(Text))) { IsDefault = true, IsAccent = true };
        Buttons = [Ok, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(null)) { IsCancel = true }];
    }

    public override string Name => DialogName;

    public override string Title { get; }

    public override double Width => 480;

    public override double? Height => 240;

    public override bool CanResize => true;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Ok { get; }

    /// <summary>The edited text (<c>Dialog.StringsEditor.Text</c>).</summary>
    public string Text
    {
        get => _text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _text, value))
            {
                OnPropertyChanged(nameof(LineCountText));
            }
        }
    }

    /// <summary><c>{n} lines</c> / <c>1 line</c> (<c>Dialog.StringsEditor.LineCount</c>).</summary>
    public string LineCountText => Split(Text).Count is var n && n == 1
        ? Strings.StringsEditor_LineOne
        : string.Format(CultureInfo.CurrentCulture, Strings.StringsEditor_LinesMany, n);

    /// <summary>DSG-OI-055: the lines of <paramref name="text"/>; an empty text has none.</summary>
    public static IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n'))
            {
                continue;
            }

            lines.Add(text[start..i]);
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }
}
