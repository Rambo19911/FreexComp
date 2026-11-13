using Avalonia.Automation;
using Avalonia.Controls;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Fixtures.Views;

/// <summary>Content of the sample tool window: one list, marked as the primary control (UI-TW-007).</summary>
public sealed class SampleToolView : UserControl
{
    public SampleToolView()
    {
        var list = new ListBox { ItemsSource = new[] { "one", "two" } };
        ToolWindowFrame.SetIsPrimary(list, true);
        Content = list;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ToolWindowViewModel vm)
            {
                AutomationProperties.SetAutomationId(list, AutomationIds.Tool(vm.Name) + ".List");
            }
        };
    }
}
