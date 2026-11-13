using Avalonia.Automation;
using Avalonia.Controls;
using FreexComp.Ide.Tests.Fixtures.ViewModels;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Fixtures.Views;

/// <summary>
/// Content of the grid fixture: a header row with one cell per column, each followed by a divider that resizes it
/// (<c>Tool.&lt;Name&gt;.Column.&lt;Id&gt;</c> and <c>…Divider</c>), and a focusable body marked as the primary control.
/// </summary>
public sealed class SampleGridToolView : UserControl
{
    private const double DividerWidth = 5;

    public SampleGridToolView()
    {
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SampleGridToolViewModel vm)
            {
                Content = Build(vm);
            }
        };
    }

    private static DockPanel Build(SampleGridToolViewModel vm)
    {
        var header = new Grid { Height = 24 };
        foreach (var (id, width) in vm.Columns)
        {
            var column = header.ColumnDefinitions.Count;
            header.ColumnDefinitions.Add(new ColumnDefinition(vm.ColumnWidth(id) ?? width, GridUnitType.Pixel) { MinWidth = 32 });
            header.ColumnDefinitions.Add(new ColumnDefinition(DividerWidth, GridUnitType.Pixel));

            var cell = new TextBlock { Text = id };
            AutomationProperties.SetAutomationId(cell, $"Tool.{vm.Name}.Column.{id}");
            Grid.SetColumn(cell, column);
            header.Children.Add(cell);

            var divider = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Width = DividerWidth };
            AutomationProperties.SetAutomationId(divider, $"Tool.{vm.Name}.Column.{id}.Divider");
            Grid.SetColumn(divider, column + 1);
            divider.DragCompleted += (_, _) => vm.SetColumnWidth(id, header.ColumnDefinitions[column].ActualWidth);
            header.Children.Add(divider);
        }

        header.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        var body = new Border { Focusable = true };
        AutomationProperties.SetAutomationId(body, $"Tool.{vm.Name}.List");
        ToolWindowFrame.SetIsPrimary(body, true);

        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(body);
        return panel;
    }
}
