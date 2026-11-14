using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The About dialog (dialogs.md section 15), 520 x auto, not resizable: application icon (48), product name, version,
/// license lines, then Runtime / Toolchain / Settings rows. Every value is read-only selectable text (UI-DLG-150).
/// </summary>
public sealed class AboutWindow(AboutViewModel model) : DialogWindow(model, Body(model))
{
    private static string Id(string field) => AutomationIds.DialogField(AboutViewModel.DialogName, field);

    /// <summary>Read-only selectable text in one of the type styles of design-tokens.md section 4.</summary>
    internal static SelectableTextBlock Value(string automationId, string text, string type, string brush = "FxcTextPrimaryBrush")
    {
        var block = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        var mono = type.StartsWith("Code", StringComparison.Ordinal);
        block.Bind(TextBlock.FontFamilyProperty, block.GetResourceObservable(mono ? "FxcFontMono" : "FxcFontUi"));
        block.Bind(TextBlock.FontSizeProperty, block.GetResourceObservable($"FxcType{type}Size"));
        block.Bind(TextBlock.LineHeightProperty, block.GetResourceObservable($"FxcType{type}LineHeight"));
        if (type == "Title")
            block.FontWeight = FontWeight.SemiBold;
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable(brush));
        AutomationProperties.SetAutomationId(block, automationId);
        return block;
    }

    private static Control Body(AboutViewModel model)
    {
        using var stream = AssetLoader.Open(new Uri("avares://FreexComp.Ide/Assets/app.ico"));
        var icon = new Image { Source = new Bitmap(stream), Width = 48, Height = 48, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 16, 0) };
        AutomationProperties.SetAutomationId(icon, Id("Icon"));
        AutomationProperties.SetName(icon, Strings.App_ProductName);
        var head = new StackPanel { Children = { Value(Id("ProductName"), model.ProductName, "Title"), Value(Id("Version"), model.Version, "Body", "FxcTextSecondaryBrush") } };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, head } };

        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
        AddRow(rows, 0, Strings.About_Label_Runtime, Value(Id("Runtime"), model.Runtime, "Body"));
        AddRow(rows, 1, Strings.About_Label_Toolchain, Value(Id("Toolchain"), model.Toolchain, "Body"));
        AddRow(rows, 2, Strings.About_Label_Settings, Value(Id("Settings"), model.Settings, "CodeSmall"));

        var panel = new StackPanel { Children = { header, Value(Id("License"), model.License, "Body"), rows } };
        panel.Bind(StackPanel.SpacingProperty, panel.GetResourceObservable("FxcSpace6"));
        return panel;
    }

    private static void AddRow(Grid grid, int row, string label, SelectableTextBlock value)
    {
        var caption = new TextBlock { Text = label + ":", Margin = new Thickness(0, 0, 16, 4), Classes = { "body" } };
        caption.Bind(TextBlock.ForegroundProperty, caption.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetLabeledBy(value, caption);
        Grid.SetRow(caption, row);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(caption);
        grid.Children.Add(value);
    }
}
