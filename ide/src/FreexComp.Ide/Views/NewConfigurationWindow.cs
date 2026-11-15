using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>The NewConfiguration sub-dialog of Project Options (UI-DLG-083), 360 × auto. Initial focus on <c>Name</c>.</summary>
public sealed class NewConfigurationWindow : DialogWindow
{
    public NewConfigurationWindow(NewConfigurationViewModel model)
        : this(model, new TextBox())
    {
    }

    private NewConfigurationWindow(NewConfigurationViewModel model, TextBox name)
        : base(model, Body(model, name))
    {
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(
            () => name.Focus(Avalonia.Input.NavigationMethod.Tab), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static StackPanel Body(NewConfigurationViewModel model, TextBox name)
    {
        var f = new DialogFieldBuilder(NewConfigurationViewModel.DialogName);
        AutomationProperties.SetAutomationId(name, f.Id("Name"));
        var typed = false;
        name.TextChanged += (_, _) =>
        {
            typed = true;
            model.ConfigName = name.Text ?? "";
        };
        var copyFrom = new ComboBox { ItemsSource = model.Existing, SelectedItem = model.CopyFrom, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(copyFrom, f.Id("CopyFrom"));
        copyFrom.SelectionChanged += (_, _) => model.CopyFrom = copyFrom.SelectedItem as string ?? model.CopyFrom;
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.NewConfiguration_Name, name), name),
                f.Message("Name", model, () => typed ? model.Error : null, () => true, name),
                DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.NewConfiguration_CopyFrom, copyFrom), copyFrom),
            },
        };
    }
}
