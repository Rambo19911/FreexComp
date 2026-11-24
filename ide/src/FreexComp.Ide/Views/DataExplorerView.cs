using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// Content of the Data Explorer tool window in Phase 7 (tool-windows.md §16, UI-TW-1400): the empty-state block
/// <c>Tool.DataExplorer.Empty</c> (UI-TW-013) — the heading in <c>type.subtitle</c> <c>text.secondary</c> over the text
/// in <c>type.body</c> <c>text.tertiary</c>, centred, at most 220 wide — placed at one third of the height. The panel
/// itself takes the keyboard focus; Escape goes back to the document through the tool-window frame.
/// </summary>
public sealed class DataExplorerView : UserControl
{
    private const double TextMaxWidth = 220;

    public DataExplorerView()
    {
        var heading = new TextBlock
        {
            Text = Strings.Msg_DataExplorer_Heading,
            Classes = { "subtitle" },
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        heading.Bind(TextBlock.ForegroundProperty, heading.GetResourceObservable("FxcTextSecondaryBrush"));
        var text = new TextBlock
        {
            Text = Strings.Msg_DataExplorer_Placeholder,
            Classes = { "body" },
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = TextMaxWidth,
        };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("FxcTextTertiaryBrush"));
        var block = new StackPanel { Spacing = 4, Margin = new Thickness(8, 0), Children = { heading, text } };
        Grid.SetRow(block, 1);
        var empty = new Grid { RowDefinitions = new RowDefinitions("1*,Auto,2*"), Children = { block } };
        AutomationProperties.SetAutomationId(empty, "Tool.DataExplorer.Empty");
        AutomationProperties.SetName(this, Strings.Msg_DataExplorer_Heading);
        Focusable = true; // the panel is the primary control (tool-windows.md §1.2)
        ToolWindowFrame.SetIsPrimary(this, true);
        Content = empty;
    }
}
