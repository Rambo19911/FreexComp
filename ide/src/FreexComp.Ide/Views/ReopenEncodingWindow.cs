using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Views;

/// <summary>
/// Reopen with Encoding (dialogs.md section 18), 440 x auto: <c>Current</c>, the <c>Encoding</c> combo and, for a modified
/// document, the <c>Warning</c> line.
/// </summary>
public sealed class ReopenEncodingWindow(ReopenEncodingViewModel model) : DialogWindow(model, Body(model))
{
    private static string Id(string field) => AutomationIds.DialogField(ReopenEncodingViewModel.DialogName, field);

    private static Control Body(ReopenEncodingViewModel model)
    {
        var current = new TextBlock { Text = model.CurrentText, TextWrapping = TextWrapping.Wrap, Classes = { "body" } };
        current.Bind(TextBlock.ForegroundProperty, current.GetResourceObservable("FxcTextPrimaryBrush"));
        AutomationProperties.SetAutomationId(current, Id("Current"));

        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var encoding in model.Encodings)
        {
            combo.Items.Add(new ComboBoxItem { Content = FileEncodings.DisplayName(encoding) });
        }

        combo.SelectedIndex = IndexOf(model, model.Selected);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
                model.Selected = model.Encodings[combo.SelectedIndex];
        };
        AutomationProperties.SetAutomationId(combo, Id("Encoding"));
        var label = new Label { Content = Strings.ReopenEncoding_Encoding, Target = combo, Padding = new Thickness(0), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        label.Bind(ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        row.Children.Add(label);
        Grid.SetColumn(combo, 1);
        row.Children.Add(combo);

        var panel = new StackPanel { Children = { current, row } };
        panel.Bind(StackPanel.SpacingProperty, panel.GetResourceObservable("FxcSpace4"));
        if (model.ShowWarning)
        {
            var warning = new TextBlock { Text = "⚠ " + Strings.Msg_Encoding_DiscardWarning, TextWrapping = TextWrapping.Wrap, Classes = { "body" } };
            warning.Bind(TextBlock.ForegroundProperty, warning.GetResourceObservable("FxcStatusWarningBrush"));
            AutomationProperties.SetAutomationId(warning, Id("Warning"));
            panel.Children.Add(warning);
        }

        return panel;
    }

    private static int IndexOf(ReopenEncodingViewModel model, FileEncoding encoding)
    {
        for (var i = 0; i < model.Encodings.Count; i++)
        {
            if (model.Encodings[i] == encoding)
                return i;
        }

        return -1;
    }
}
