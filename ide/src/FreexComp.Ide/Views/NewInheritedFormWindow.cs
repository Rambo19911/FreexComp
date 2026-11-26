using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// The New Inherited Form dialog (form-inheritance-designer.md §12.2, UI-DLG-316…322), 520 × 380, resizable down to
/// 400 × 280: the <c>Filter</c> box beside the <c>&amp;Inherit from:</c> label, the <c>Ancestors</c> list (rows
/// <c>Dialog.NewInheritedForm.Item.&lt;ClassName&gt;</c>: icon, name, kind, unit path) or its placeholder, the
/// <c>Preview</c> line (a live region). First focus on the list, on <c>Filter</c> when the list is empty; typing in the list
/// goes on in <c>Filter</c>; <c>Down</c> in <c>Filter</c> moves into the list; a double-click on a row is OK.
/// </summary>
public sealed class NewInheritedFormWindow : DialogWindow
{
    public NewInheritedFormWindow(NewInheritedFormViewModel model)
        : this(model, new ListBox(), new TextBox())
    {
    }

    private NewInheritedFormWindow(NewInheritedFormViewModel model, ListBox list, TextBox filter)
        : base(model, Body(model, list, filter))
    {
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (model.VisibleRows.Count == 0)
                filter.Focus(NavigationMethod.Tab);
            else
                FocusSelected(list);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static string Id(string field) => AutomationIds.DialogField(NewInheritedFormViewModel.DialogName, field);

    private static Control Body(NewInheritedFormViewModel model, ListBox list, TextBox filter)
    {
        // Row 0: the label (its mnemonic focuses the list) and the filter at the right.
        filter.Width = 180;
        filter.PlaceholderText = Strings.ToolWindow_FilterPlaceholder;
        filter.Text = model.Filter;
        AutomationProperties.SetAutomationId(filter, Id("Filter"));
        AutomationProperties.SetName(filter, Strings.ToolWindow_FilterPlaceholder);
        filter.TextChanged += (_, _) => model.Filter = filter.Text ?? "";
        filter.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Down && e.KeyModifiers == KeyModifiers.None && model.VisibleRows.Count > 0)
            {
                e.Handled = true;
                FocusSelected(list);
            }
        }, RoutingStrategies.Tunnel);
        var label = DialogFieldBuilder.Label(Strings.Dialog_NewInheritedForm_InheritFrom.Replace('&', '_'), list);
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(filter, Avalonia.Controls.Dock.Right);
        top.Children.Add(filter);
        top.Children.Add(label);

        // Row 1: the list, or its placeholder.
        AutomationProperties.SetAutomationId(list, Id("Ancestors"));
        AutomationProperties.SetName(list, Strings.Dialog_NewInheritedForm_InheritFrom.Replace("&", "", StringComparison.Ordinal));
        list.SelectionMode = SelectionMode.Single;
        list.IsTextSearchEnabled = false; // UI-DLG-317: typed characters go to the filter
        var placeholder = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(16),
        };
        placeholder.Bind(TextBlock.ForegroundProperty, placeholder.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(placeholder, Id("Placeholder"));
        var syncing = false;

        void Fill()
        {
            syncing = true;
            list.Items.Clear();
            foreach (var row in model.VisibleRows)
            {
                var item = CreateRow(row);
                list.Items.Add(item);
                item.IsSelected = ReferenceEquals(row, model.Selected);
            }

            placeholder.Text = model.Placeholder;
            placeholder.IsVisible = model.Placeholder is not null;
            list.IsVisible = model.Placeholder is null;
            syncing = false;
        }

        Fill();
        list.SelectionChanged += (_, _) =>
        {
            if (!syncing)
                model.Selected = (list.SelectedItem as ListBoxItem)?.Tag as NewInheritedFormRow;
        };
        list.AddHandler(InputElement.TextInputEvent, (_, e) =>
        {
            // UI-DLG-317: a printable character moves the focus to the filter and is appended there.
            if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
                return;
            e.Handled = true;
            filter.Focus(NavigationMethod.Tab);
            filter.Text = (filter.Text ?? "") + e.Text;
            filter.CaretIndex = filter.Text.Length;
        }, RoutingStrategies.Tunnel);
        list.DoubleTapped += async (_, e) =>
        {
            if (e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null)
                await model.Ok.PressAsync();
        };
        var stack = new Panel { Children = { list, placeholder } };
        var frame = new Border { BorderThickness = new Thickness(1), Child = stack };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));

        // Row 2: the preview line (a live region, UI-DLG-322).
        var preview = new TextBlock { Text = model.Preview, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        preview.Bind(TextBlock.ForegroundProperty, preview.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(preview, Id("Preview"));
        AutomationProperties.SetLiveSetting(preview, AutomationLiveSetting.Polite);

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NewInheritedFormViewModel.VisibleRows))
                Fill();
            else if (e.PropertyName == nameof(NewInheritedFormViewModel.Selected))
            {
                preview.Text = model.Preview;
                AutomationProperties.SetName(preview, model.Preview);
                if (!syncing)
                {
                    syncing = true;
                    foreach (var item in list.Items.OfType<ListBoxItem>())
                    {
                        item.IsSelected = ReferenceEquals(item.Tag, model.Selected);
                    }

                    syncing = false;
                }
            }
        };
        AutomationProperties.SetName(preview, model.Preview);

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        grid.Children.Add(top);
        Grid.SetRow(frame, 1);
        grid.Children.Add(frame);
        Grid.SetRow(preview, 2);
        grid.Children.Add(preview);
        return grid;
    }

    /// <summary>Focuses the selected row (else the list) so that the arrow keys move the selection.</summary>
    private static void FocusSelected(ListBox list)
    {
        if (list.ContainerFromItem(list.SelectedItem ?? new object()) is ListBoxItem selected)
            selected.Focus(NavigationMethod.Tab);
        else
            list.Focus(NavigationMethod.Tab);
    }

    private static ListBoxItem CreateRow(NewInheritedFormRow row)
    {
        var icon = new IdeIcon { IconName = row.IsFrame ? "frame" : "form", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = row.Name, Classes = { "body" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("FxcTextPrimaryBrush"));
        var kind = new TextBlock { Text = row.Kind, Classes = { "body" }, VerticalAlignment = VerticalAlignment.Center };
        kind.Bind(TextBlock.ForegroundProperty, kind.GetResourceObservable("FxcTextSecondaryBrush"));
        var unit = new TextBlock
        {
            Text = row.UnitPath,
            Classes = { "body" },
            TextTrimming = TextTrimming.PrefixCharacterEllipsis, // the file name stays visible
            VerticalAlignment = VerticalAlignment.Center,
        };
        unit.Bind(TextBlock.ForegroundProperty, unit.GetResourceObservable("FxcTextSecondaryBrush"));
        ToolTip.SetTip(unit, row.FullPath);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("24,160,64,*") };
        columns.Children.Add(icon);
        Grid.SetColumn(name, 1);
        columns.Children.Add(name);
        Grid.SetColumn(kind, 2);
        columns.Children.Add(kind);
        Grid.SetColumn(unit, 3);
        columns.Children.Add(unit);
        var item = new ListBoxItem { Content = columns, Tag = row, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        item.Bind(PaddingProperty, item.GetResourceObservable("FxcSpace2Horizontal"));
        AutomationProperties.SetAutomationId(item, Id("Item." + row.ClassName));
        AutomationProperties.SetName(item, row.AccessibleName);
        return item;
    }
}
