using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The NewConfiguration sub-dialog of Project Options and of the Configuration Manager (UI-DLG-083, UI-DLG-270), 360 × auto:
/// <c>Name</c>, <c>CopyFrom</c> ("Based on") and the check box <c>Inherit</c> in the field column. Initial focus on <c>Name</c>.
/// </summary>
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

    private static Grid Body(NewConfigurationViewModel model, TextBox name)
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
        copyFrom.ItemTemplate = ConfigurationSubDialog.NameTemplate();
        AutomationProperties.SetAutomationId(copyFrom, f.Id("CopyFrom"));
        copyFrom.SelectionChanged += (_, _) => model.CopyFrom = copyFrom.SelectedItem as string ?? model.CopyFrom;
        // UI-DLG-270: in the field column; the text wraps inside the dialog width.
        var inherit = new CheckBox
        {
            Content = new Avalonia.Controls.Primitives.AccessText { Text = Strings.NewConfiguration_Inherit, TextWrapping = TextWrapping.Wrap },
            IsChecked = model.Inherit,
        };
        AutomationProperties.SetAutomationId(inherit, f.Id("Inherit"));
        AutomationProperties.SetName(inherit, Strings.NewConfiguration_Inherit.Replace("_", "", StringComparison.Ordinal));
        inherit.IsCheckedChanged += (_, _) => model.Inherit = inherit.IsChecked == true;
        return ConfigurationSubDialog.Rows(
            (DialogFieldBuilder.Label(Strings.NewConfiguration_Name, name), name),
            (null, f.Message("Name", model, () => typed ? model.Error : null, () => true, name)),
            (DialogFieldBuilder.Label(Strings.NewConfiguration_BasedOn, copyFrom), copyFrom),
            (null, inherit));
    }
}

/// <summary>
/// The layout shared by the two 360-wide configuration sub-dialogs (UI-DLG-270, 275): a label column as wide as its
/// longest label and the field column taking the rest, so that a 32-character name has room.
/// </summary>
internal static class ConfigurationSubDialog
{
    /// <summary>Rows of (label or null, field); a text block is the message under the field above it and stands closer to it.</summary>
    public static Grid Rows(params (Control? Label, Control Field)[] rows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var (label, field) = rows[i];
            var isMessage = field is TextBlock;
            var top = i == 0 || isMessage ? 0 : 8;
            if (label is not null)
            {
                label.Margin = new Thickness(0, top, 16, 0);
                Grid.SetRow(label, i);
                grid.Children.Add(label);
            }

            field.Margin = new Thickness(0, isMessage ? 2 : top, 0, 0);
            Grid.SetRow(field, i);
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
        }

        return grid;
    }

    /// <summary>A configuration name in a combo: one line, an ellipsis when it does not fit, the whole name as tooltip.</summary>
    public static Avalonia.Controls.Templates.FuncDataTemplate<string> NameTemplate() => new((name, _) =>
    {
        var text = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(text, name);
        return text;
    }, supportsRecycling: false);
}
