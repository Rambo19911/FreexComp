using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Documents dialog (dialogs.md section 17), 600 x 380, resizable: a two-column multi-selection list (Name 200,
/// Folder; headers sort) with <c>Activate</c>, <c>Save</c> and <c>CloseDocs</c> beside it and Done in the strip. In the
/// list, Enter and double-click activate the row, Delete closes the selection.
/// </summary>
public sealed class DocumentsWindow(DocumentsViewModel model) : DialogWindow(model, Body(model))
{
    private static string Id(string field) => AutomationIds.DialogField(DocumentsViewModel.DialogName, field);

    private static Control Body(DocumentsViewModel model)
    {
        var list = new ListBox { SelectionMode = SelectionMode.Multiple, IsTextSearchEnabled = true };
        AutomationProperties.SetAutomationId(list, Id("List"));
        DialogWindow.SetKeepsEnter(list, true); // Enter activates the row (dialogs.md Documents)
        AutomationProperties.SetName(list, Strings.Documents_Title);
        var syncing = false;

        void Fill()
        {
            syncing = true;
            list.Items.Clear();
            foreach (var row in model.Rows)
            {
                var item = CreateRow(row);
                list.Items.Add(item);
                item.IsSelected = model.Selection.Contains(row);
            }

            syncing = false;
        }

        Fill();
        model.Rows.CollectionChanged += (_, e) =>
        {
            if (e.Action is NotifyCollectionChangedAction.Remove && e.OldItems is { } old)
            {
                foreach (var item in list.Items.OfType<ListBoxItem>().Where(i => old.Contains(i.Tag)).ToList())
                {
                    list.Items.Remove(item);
                }
            }
            else
            {
                Fill();
            }
        };
        list.SelectionChanged += (_, _) =>
        {
            if (!syncing)
                model.Selection = [.. list.Items.OfType<ListBoxItem>().Where(i => i.IsSelected).Select(i => (DocumentRow)i.Tag!)];
        };
        list.AddHandler(InputElement.KeyDownEvent, async (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.None)
                return;
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await model.Activate.PressAsync();
            }
            else if (e.Key == Key.Delete)
            {
                e.Handled = true;
                await model.CloseDocs.PressAsync();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        list.DoubleTapped += async (_, _) => await model.Activate.PressAsync();

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*") };
        header.Children.Add(HeaderButton(Strings.Documents_ColumnName, "Name", () => model.SortBy(DocumentsSort.Name)));
        var folderHeader = HeaderButton(Strings.Documents_ColumnFolder, "Folder", () => model.SortBy(DocumentsSort.Folder));
        Grid.SetColumn(folderHeader, 1);
        header.Children.Add(folderHeader);
        header.Bind(Panel.BackgroundProperty, header.GetResourceObservable("FxcBgHeaderBrush"));

        var table = new DockPanel();
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        table.Children.Add(header);
        table.Children.Add(list);
        var frame = new Border { BorderThickness = new Thickness(1), Child = table };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));

        var side = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        side.Bind(StackPanel.SpacingProperty, side.GetResourceObservable("FxcSpace4"));
        foreach (var button in new[] { model.Activate, model.Save, model.CloseDocs })
        {
            side.Children.Add(SideButton(button));
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(frame);
        Grid.SetColumn(side, 1);
        grid.Children.Add(side);
        return grid;
    }

    private static Button HeaderButton(string text, string field, Action sort)
    {
        var label = new TextBlock { Text = text, Classes = { "bodyStrong" } };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("FxcTextSecondaryBrush"));
        var button = new Button
        {
            Content = label,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Bind(PaddingProperty, button.GetResourceObservable("FxcSpace2Horizontal"));
        AutomationProperties.SetAutomationId(button, Id("Column." + field));
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => sort();
        return button;
    }

    private static Button SideButton(DialogButtonViewModel model)
    {
        var button = new Button { Content = model.AccessLabel, IsEnabled = model.IsEnabled, HorizontalContentAlignment = HorizontalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Bind(MinWidthProperty, button.GetResourceObservable("FxcMetricButtonMinWidth"));
        button.Bind(HeightProperty, button.GetResourceObservable("FxcMetricButtonHeight"));
        AutomationProperties.SetAutomationId(button, Id(model.Id));
        AutomationProperties.SetName(button, model.Label);
        button.Click += async (_, _) => await model.PressAsync();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DialogButtonViewModel.IsEnabled))
                button.IsEnabled = model.IsEnabled;
        };
        return button;
    }

    private static ListBoxItem CreateRow(DocumentRow row)
    {
        var name = new TextBlock { Text = row.Name, Classes = { "body" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("FxcTextPrimaryBrush"));
        var folder = new TextBlock
        {
            Text = row.Folder,
            Classes = { "body" },
            TextTrimming = TextTrimming.PrefixCharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        folder.Bind(TextBlock.ForegroundProperty, folder.GetResourceObservable("FxcTextSecondaryBrush"));
        ToolTip.SetTip(folder, row.Folder);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*") };
        columns.Children.Add(name);
        Grid.SetColumn(folder, 1);
        columns.Children.Add(folder);
        row.PropertyChanged += (_, _) => name.Text = row.Name;
        var item = new ListBoxItem { Content = columns, Tag = row, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        item.Bind(PaddingProperty, item.GetResourceObservable("FxcSpace2Horizontal"));
        AutomationProperties.SetAutomationId(item, Id("Row." + row.Key));
        AutomationProperties.SetName(item, row.Document.Title);
        TextSearch.SetText(item, row.Document.Title);
        return item;
    }
}
