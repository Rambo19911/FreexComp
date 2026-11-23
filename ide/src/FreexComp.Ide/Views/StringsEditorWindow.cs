using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using FreexComp.Ide.Core.Dialogs;

namespace FreexComp.Ide.Views;

/// <summary>
/// The strings editor (object-inspector.md §5.5, DSG-OI-055): a resizable dialog, min 480 × 240, with a multi-line
/// <c>type.code</c> text (<c>Dialog.StringsEditor.Text</c>) and the line count (<c>.LineCount</c>) left of the buttons.
/// Enter inserts a line break; Ctrl+Enter is OK.
/// </summary>
public sealed class StringsEditorWindow : DialogWindow
{
    public StringsEditorWindow(StringsEditorViewModel model)
        : this(model, new TextBox(), new TextBlock())
    {
    }

    private StringsEditorWindow(StringsEditorViewModel model, TextBox text, TextBlock count)
        : base(model, Body(model, text, count))
    {
        text.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
            {
                e.Handled = true;
                _ = model.PressDefaultAsync();
            }
        }, RoutingStrategies.Tunnel);
    }

    private static string Id(string field) => AutomationIds.DialogField(StringsEditorViewModel.DialogName, field);

    private static DockPanel Body(StringsEditorViewModel model, TextBox text, TextBlock count)
    {
        text.Text = model.Text;
        text.AcceptsReturn = true;
        text.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
        text.MinHeight = 160;
        text.Classes.Add("code");
        text.Bind(TextBox.FontFamilyProperty, text.GetResourceObservable("FxcFontMono"));
        ScrollViewer.SetVerticalScrollBarVisibility(text, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(text, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        AutomationProperties.SetAutomationId(text, Id("Text"));
        AutomationProperties.SetName(text, model.Title);
        text.TextChanged += (_, _) => model.Text = text.Text ?? "";

        count.Text = model.LineCountText;
        count.Classes.Add("body");
        count.Margin = new Thickness(0, 6, 0, 0);
        count.Bind(TextBlock.ForegroundProperty, count.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(count, Id("LineCount"));
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StringsEditorViewModel.LineCountText))
                count.Text = model.LineCountText;
        };

        var body = new DockPanel { VerticalAlignment = VerticalAlignment.Stretch };
        DockPanel.SetDock(count, Dock.Bottom);
        body.Children.Add(count);
        body.Children.Add(text);
        return body;
    }
}
