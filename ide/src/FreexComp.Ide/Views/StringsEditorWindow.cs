using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;

namespace FreexComp.Ide.Views;

/// <summary>
/// The strings editor (object-inspector.md §5.5, DSG-OI-055): a resizable dialog, min 480 × 240, with a multi-line
/// <c>type.code</c> text (<c>Dialog.StringsEditor.Text</c>). On the button strip (UI-DLG-344): <c>Load…</c> and
/// <c>Save…</c> left, then the line count (<c>.LineCount</c>), which takes the room that is left and is cut with an
/// ellipsis, <c>OK</c> and <c>Cancel</c> right. Enter inserts a line break; Ctrl+Enter is OK. After a load the text is
/// shown with the caret at the start; after every Load / Save the focus is back in the text (UI-DLG-345).
/// </summary>
public sealed class StringsEditorWindow : DialogWindow
{
    public StringsEditorWindow(StringsEditorViewModel model)
        : this(model, new TextBox(), new TextBlock())
    {
    }

    private StringsEditorWindow(StringsEditorViewModel model, TextBox text, TextBlock count)
        : base(model, Body(model, text))
    {
        text.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
            {
                e.Handled = true;
                _ = model.PressDefaultAsync();
            }
        }, RoutingStrategies.Tunnel);

        count.Text = model.LineCountText;
        count.Classes.Add("body");
        count.VerticalAlignment = VerticalAlignment.Center;
        count.TextTrimming = TextTrimming.CharacterEllipsis;
        count.Bind(TextBlock.ForegroundProperty, count.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(count, Id("LineCount"));
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StringsEditorViewModel.LineCountText))
                count.Text = model.LineCountText;
        };
        SetStripInfo(count);

        model.TextLoaded += (_, _) =>
        {
            text.Text = model.Text; // DSG-OI-130: the whole text, the caret at its start
            text.CaretIndex = 0;
            text.SelectionStart = text.SelectionEnd = 0;
        };
        model.FileActionCompleted += (_, _) => text.Focus(NavigationMethod.Unspecified); // UI-DLG-345
    }

    private static string Id(string field) => AutomationIds.DialogField(StringsEditorViewModel.DialogName, field);

    private static DockPanel Body(StringsEditorViewModel model, TextBox text)
    {
        text.Text = model.Text;
        text.AcceptsReturn = true;
        text.TextWrapping = TextWrapping.NoWrap;
        text.MinHeight = 160;
        text.Classes.Add("code");
        text.Bind(TextBox.FontFamilyProperty, text.GetResourceObservable("FxcFontMono"));
        ScrollViewer.SetVerticalScrollBarVisibility(text, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(text, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        AutomationProperties.SetAutomationId(text, Id("Text"));
        AutomationProperties.SetName(text, model.Title);
        text.TextChanged += (_, _) => model.Text = text.Text ?? "";

        var body = new DockPanel { VerticalAlignment = VerticalAlignment.Stretch };
        body.Children.Add(text);
        return body;
    }
}
