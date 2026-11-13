using Avalonia.Automation;
using Avalonia.Controls;
using FreexComp.Ide.Tests.Fixtures.ViewModels;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Fixtures.Views;

/// <summary>Window of <see cref="SampleDialogViewModel"/>: the fields only; the button strip comes from <see cref="DialogWindow"/>.</summary>
public sealed class SampleDialogWindow(SampleDialogViewModel model) : DialogWindow(model, Body(model))
{
    private static Control Body(SampleDialogViewModel model)
    {
        var name = new TextBox();
        AutomationProperties.SetAutomationId(name, AutomationIds.DialogField(model.Name, "Name"));
        name.TextChanged += (_, _) => model.FieldName = name.Text ?? "";

        var notes = new TextBox { AcceptsReturn = true, Height = 60 };
        AutomationProperties.SetAutomationId(notes, AutomationIds.DialogField(model.Name, "Notes"));
        notes.TextChanged += (_, _) => model.Notes = notes.Text ?? "";

        var option = new CheckBox { Content = "_Option" };
        AutomationProperties.SetAutomationId(option, AutomationIds.DialogField(model.Name, "Option"));
        option.IsCheckedChanged += (_, _) => model.Option = option.IsChecked == true;

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(name);
        panel.Children.Add(notes);
        panel.Children.Add(option);
        return panel;
    }
}
