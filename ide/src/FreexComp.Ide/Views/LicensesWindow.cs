using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Third-Party Licenses dialog (dialogs.md section 16, UI-DLG-160), 760 x 520, resizable: the component list
/// (<c>Dialog.Licenses.Components</c>, 240 wide; rows <c>Dialog.Licenses.Component.&lt;name&gt;</c> with the license
/// identifier right-aligned) and the license text (<c>Dialog.Licenses.Text</c>, read-only selectable code text).
/// </summary>
public sealed class LicensesWindow(LicensesViewModel model) : DialogWindow(model, Body(model))
{
    private static string Id(string field) => AutomationIds.DialogField(LicensesViewModel.DialogName, field);

    private static Control Body(LicensesViewModel model)
    {
        var list = new ListBox { Width = 240, SelectionMode = SelectionMode.Single };
        AutomationProperties.SetAutomationId(list, Id("Components"));
        AutomationProperties.SetName(list, Strings.Licenses_Components);
        foreach (var notice in model.Components)
        {
            list.Items.Add(CreateRow(notice));
        }

        list.SelectedIndex = model.Selected is { } selected ? IndexOf(model, selected) : -1;
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex >= 0)
                model.Selected = model.Components[list.SelectedIndex];
        };

        var text = AboutWindow.Value(Id("Text"), model.Text, "CodeSmall");
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LicensesViewModel.Text))
                text.Text = model.Text;
        };
        var scroller = new ScrollViewer { Content = text, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroller.Bind(PaddingProperty, scroller.GetResourceObservable("FxcSpace4All"));
        var pane = new Border { BorderThickness = new Thickness(1), Child = scroller, Margin = new Thickness(16, 0, 0, 0) };
        pane.Bind(Border.BorderBrushProperty, pane.GetResourceObservable("FxcBorderDefaultBrush"));
        pane.Bind(Border.BackgroundProperty, pane.GetResourceObservable("FxcBgEditorBrush"));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(list);
        Grid.SetColumn(pane, 1);
        grid.Children.Add(pane);
        return grid;
    }

    private static int IndexOf(LicensesViewModel model, ThirdPartyNotice notice)
    {
        for (var i = 0; i < model.Components.Count; i++)
        {
            if (ReferenceEquals(model.Components[i], notice))
                return i;
        }

        return -1;
    }

    private static ListBoxItem CreateRow(ThirdPartyNotice notice)
    {
        var name = new TextBlock { Text = notice.Name, Classes = { "body" }, VerticalAlignment = VerticalAlignment.Center };
        name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("FxcTextPrimaryBrush"));
        var license = new TextBlock
        {
            Text = notice.License,
            Classes = { "body" },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 0, 0),
        };
        license.Bind(TextBlock.ForegroundProperty, license.GetResourceObservable("FxcTextTertiaryBrush"));
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        columns.Children.Add(name);
        Grid.SetColumn(license, 1);
        columns.Children.Add(license);
        var item = new ListBoxItem { Content = columns, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(item, Id("Component." + notice.Name));
        AutomationProperties.SetName(item, notice.Name);
        return item;
    }
}
