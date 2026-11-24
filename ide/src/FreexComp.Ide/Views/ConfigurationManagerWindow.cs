using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Configuration Manager (UI-DLG-271…276), 560 × 420, resizable: the list <c>Configurations</c> (header row; columns
/// Name — indented per inheritance level, the active one with a bullet in a fixed gutter — and Inherits from) with the
/// buttons <c>New</c>, <c>Rename</c>, <c>Delete</c>, <c>SetActive</c> at its right; below it the <c>Parent</c> combo, its
/// hint and the <c>OwnValues</c> line. Only the list grows with the window. In the list Insert, F2 and Delete are the
/// buttons; Enter is the default button (UI-DLG-273).
/// </summary>
public sealed class ConfigurationManagerWindow : DialogWindow
{
    /// <summary>UI-DLG-272: the fixed gutter left of the indent that holds the bullet of the active configuration.</summary>
    public const double GutterWidth = 16;

    /// <summary>UI-DLG-272: width of the <c>Inherits from</c> column.</summary>
    public const double ParentColumnWidth = 180;

    /// <summary>Horizontal padding of a row, and of the header row above the rows.</summary>
    private const double RowPadding = 4;

    /// <summary>UI-DLG-271: width of the buttons beside the list.</summary>
    public const double SideButtonWidth = 96;

    public ConfigurationManagerWindow(ConfigurationManagerViewModel model)
        : this(model, new ListBox { SelectionMode = SelectionMode.Single })
    {
    }

    private ConfigurationManagerWindow(ConfigurationManagerViewModel model, ListBox list)
        : base(model, Body(model, list))
    {
        // UI-DLG-273: initial focus on the list — on its selected row, so that the arrow keys move from there.
        Opened += (_, _) => Dispatcher.UIThread.Post(() => FocusSelected(list), DispatcherPriority.Loaded);
    }

    private static string Id(string field) => AutomationIds.DialogField(ConfigurationManagerViewModel.DialogName, field);

    private static void FocusSelected(ListBox list)
    {
        if (list.SelectedItem is ListBoxItem item)
            item.Focus(NavigationMethod.Tab);
        else if (!list.IsKeyboardFocusWithin)
            list.Focus(NavigationMethod.Tab);
    }

    private static Control Body(ConfigurationManagerViewModel model, ListBox list)
    {
        AutomationProperties.SetAutomationId(list, Id("Configurations"));
        AutomationProperties.SetName(list, Strings.ConfigurationManager_Configurations.Replace("_", "", StringComparison.Ordinal).TrimEnd(':'));
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled); // UI-DLG-272: never a horizontal scroll bar
        var syncing = false;

        void Fill()
        {
            var hadFocus = list.IsKeyboardFocusWithin;
            syncing = true;
            list.Items.Clear();
            foreach (var row in model.Rows)
            {
                var item = CreateRow(row);
                list.Items.Add(item);
                if (row.Name == model.SelectedName)
                    list.SelectedItem = item;
            }

            syncing = false;
            if (hadFocus)
                Dispatcher.UIThread.Post(() => FocusSelected(list), DispatcherPriority.Loaded);
        }

        // Alt+C (the label's mnemonic) lands on the list itself: the focus goes on to the selected row.
        list.Focusable = true;
        list.GotFocus += (_, e) =>
        {
            if (ReferenceEquals(e.Source, list))
                FocusSelected(list);
        };
        list.SelectionChanged += (_, _) =>
        {
            if (!syncing && list.SelectedItem is ListBoxItem { Tag: ConfigurationRow row })
                model.SelectedName = row.Name;
            if (list.SelectedItem is { } selected)
                list.ScrollIntoView(selected); // the selected row is never out of view (twenty configurations scroll)
        };

        // The header row (UI-TW-011 look): the name header starts where the names of root configurations start.
        var nameHeader = HeaderCell(Strings.ConfigurationManager_Column_Name, "Name");
        var parentHeader = HeaderCell(Strings.ConfigurationManager_Column_Parent, "Parent");
        var header = new Grid { ColumnDefinitions = Columns(), Height = 24 };
        header.Children.Add(nameHeader);
        Grid.SetColumn(parentHeader, 1);
        header.Children.Add(parentHeader);
        header.Bind(Panel.BackgroundProperty, header.GetResourceObservable("FxcBgHeaderBrush"));
        var headerHost = new Border { Child = header, Padding = new Thickness(RowPadding, 0) };
        headerHost.Bind(Border.BackgroundProperty, headerHost.GetResourceObservable("FxcBgHeaderBrush"));
        // A visible vertical scroll bar narrows the rows: the header follows, so that the columns stay aligned.
        list.LayoutUpdated += (_, _) =>
        {
            var scroller = list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            var bar = scroller is null ? 0 : Math.Max(0, Math.Round(scroller.Bounds.Width - scroller.Viewport.Width));
            if (headerHost.Padding.Right != RowPadding + bar)
                headerHost.Padding = new Thickness(RowPadding, 0, RowPadding + bar, 0);
        };

        var table = new DockPanel();
        DockPanel.SetDock(headerHost, Avalonia.Controls.Dock.Top);
        table.Children.Add(headerHost);
        table.Children.Add(list);
        var frame = new Border { BorderThickness = new Thickness(1), Child = table };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));

        // The buttons beside the list.
        var newButton = SideButton("New", Strings.ConfigurationManager_New, () => model.NewAsync());
        var renameButton = SideButton("Rename", Strings.ConfigurationManager_Rename, () => model.RenameAsync());
        var deleteButton = SideButton("Delete", Strings.ConfigurationManager_Delete, () => model.DeleteAsync());
        var activeButton = SideButton("SetActive", Strings.ConfigurationManager_SetActive, () =>
        {
            model.SetActive();
            return Task.CompletedTask;
        });
        var side = new StackPanel { Width = SideButtonWidth, VerticalAlignment = VerticalAlignment.Top, Children = { newButton, renameButton, deleteButton, activeButton } };
        side.Bind(StackPanel.SpacingProperty, side.GetResourceObservable("FxcSpace4"));
        side.Margin = new Thickness(8, 0, 0, 0); // space.4

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(frame);
        Grid.SetColumn(side, 1);
        top.Children.Add(side);

        // Parent, its hint and the own values share the label column (UI-DLG-274).
        var parent = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(parent, Id("Parent"));
        parent.SelectionChanged += (_, _) =>
        {
            if (!syncing && parent.SelectedItem is string name)
                model.SelectedParent = name;
        };
        var hint = new TextBlock { Text = model.ParentHint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        hint.Classes.Add("caption");
        hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(hint, Id("Parent.Hint"));
        var ownValues = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        ownValues.Classes.Add("body");
        ownValues.Bind(TextBlock.ForegroundProperty, ownValues.GetResourceObservable("FxcTextPrimaryBrush"));
        AutomationProperties.SetAutomationId(ownValues, Id("OwnValues"));
        var parentLabel = FieldLabel(Strings.ConfigurationManager_Parent, parent);
        var ownLabel = FieldLabel(Strings.ConfigurationManager_OwnValues, null);
        AutomationProperties.SetLabeledBy(ownValues, ownLabel);
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
        foreach (var (control, column, row) in (ReadOnlySpan<(Control, int, int)>)[(parentLabel, 0, 0), (parent, 1, 0), (hint, 1, 1), (ownLabel, 0, 2), (ownValues, 1, 2)])
        {
            Grid.SetColumn(control, column);
            Grid.SetRow(control, row);
            fields.Children.Add(control);
        }

        ownValues.Margin = new Thickness(0, 8, 0, 0);
        ownLabel.Margin = new Thickness(0, 8, 16, 0);
        ownLabel.VerticalAlignment = VerticalAlignment.Top;
        ownValues.VerticalAlignment = VerticalAlignment.Top;
        fields.Margin = new Thickness(0, 8, 0, 0); // space.4

        void Sync()
        {
            syncing = true;
            parent.ItemsSource = model.ParentItems;
            parent.SelectedItem = model.SelectedParent;
            syncing = false;
            ownValues.Text = model.OwnValues;
            ToolTip.SetTip(ownValues, model.OwnValues);
            deleteButton.IsEnabled = model.CanDelete;
            // UI-DLG-273: a disabled button tells why, as tooltip and as help text.
            ToolTip.SetTip(deleteButton, model.DeleteToolTip);
            AutomationProperties.SetHelpText(deleteButton, model.DeleteToolTip ?? "");
            activeButton.IsEnabled = model.CanSetActive;
        }

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConfigurationManagerViewModel.Rows))
            {
                Fill();
            }
            else if (e.PropertyName == nameof(ConfigurationManagerViewModel.SelectedName))
            {
                syncing = true;
                list.SelectedItem = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is ConfigurationRow row && row.Name == model.SelectedName);
                syncing = false;
            }

            Sync();
        };
        Fill();
        Sync();

        list.AddHandler(InputElement.KeyDownEvent, async (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.None)
                return;
            Func<Task>? action = e.Key switch
            {
                Key.Insert => model.NewAsync,
                Key.F2 => model.RenameAsync,
                Key.Delete => model.DeleteAsync,
                _ => null,
            };
            if (action is null)
                return;
            e.Handled = true;
            await action();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var listLabel = new Label { Content = Strings.ConfigurationManager_Configurations, Target = list, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) };
        listLabel.Bind(TemplatedControl.ForegroundProperty, listLabel.GetResourceObservable("FxcTextPrimaryBrush"));
        var body = new DockPanel();
        DockPanel.SetDock(listLabel, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(fields, Avalonia.Controls.Dock.Bottom);
        body.Children.Add(listLabel);
        body.Children.Add(fields);
        body.Children.Add(top);
        return body;
    }

    /// <summary>UI-DLG-272: <c>Name</c> star-sized (minimum 160), <c>Inherits from</c> 180 (minimum 120).</summary>
    private static ColumnDefinitions Columns() =>
    [
        new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 160 },
        new ColumnDefinition(ParentColumnWidth, GridUnitType.Pixel) { MinWidth = 120 },
    ];

    private static TextBlock HeaderCell(string text, string field)
    {
        var cell = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        cell.Classes.Add("caption");
        cell.Bind(TextBlock.ForegroundProperty, cell.GetResourceObservable("FxcTextSecondaryBrush"));
        cell.Margin = field == "Name" ? new Thickness(GutterWidth, 0, 0, 0) : new Thickness(0);
        AutomationProperties.SetAutomationId(cell, Id("Column." + field));
        return cell;
    }

    private static Label FieldLabel(string text, Control? target)
    {
        var label = new Label { Content = text, Target = target, Padding = new Thickness(0), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        label.Bind(TemplatedControl.ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        return label;
    }

    private static Button SideButton(string id, string label, Func<Task> action)
    {
        var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Bind(HeightProperty, button.GetResourceObservable("FxcMetricButtonHeight"));
        // I-242: the 26 px button holds its 18 px text line — no vertical padding, the label centred.
        button.Padding = new Thickness(4, 0);
        button.VerticalContentAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(button, Id(id));
        AutomationProperties.SetName(button, label.Replace("_", "", StringComparison.Ordinal));
        ToolTip.SetShowOnDisabled(button, true);
        button.Click += async (_, _) => await action();
        return button;
    }

    private static ListBoxItem CreateRow(ConfigurationRow row)
    {
        // The bullet of the active configuration (UI-MENU-007) in the fixed gutter left of the indent.
        var bullet = new Ellipse { Width = 6, Height = 6, IsVisible = row.IsActive, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        bullet.Bind(Shape.FillProperty, bullet.GetResourceObservable("FxcTextPrimaryBrush"));
        var gutter = new Panel { Width = GutterWidth, Children = { bullet } };
        var name = new TextBlock
        {
            Text = row.Name,
            Classes = { "body" },
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(row.Indent, 0, 8, 0),
        };
        name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("FxcTextPrimaryBrush"));
        var nameCell = new DockPanel();
        DockPanel.SetDock(gutter, Avalonia.Controls.Dock.Left);
        nameCell.Children.Add(gutter);
        nameCell.Children.Add(name);
        var parent = new TextBlock { Text = row.ParentText, Classes = { "body" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        parent.Bind(TextBlock.ForegroundProperty, parent.GetResourceObservable("FxcTextSecondaryBrush"));
        var columns = new Grid { ColumnDefinitions = Columns() };
        columns.Children.Add(nameCell);
        Grid.SetColumn(parent, 1);
        columns.Children.Add(parent);
        var item = new ListBoxItem { Content = columns, Tag = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(RowPadding, 0) };
        item.Bind(MinHeightProperty, item.GetResourceObservable("FxcMetricMenuItemHeight"));
        AutomationProperties.SetAutomationId(item, Id("Configurations.Row." + row.Name));
        AutomationProperties.SetName(item, row.AutomationName);
        ToolTip.SetTip(item, row.Name); // a trimmed name is shown in full
        TextSearch.SetText(item, row.Name);
        return item;
    }
}

/// <summary>
/// The RenameConfiguration sub-dialog (UI-DLG-275), 360 × auto: <c>Name</c>, pre-filled and fully selected, with its
/// message <c>Error.Name</c> under it; buttons Rename (default) and Cancel.
/// </summary>
public sealed class RenameConfigurationWindow : DialogWindow
{
    public RenameConfigurationWindow(RenameConfigurationViewModel model)
        : this(model, new TextBox())
    {
    }

    private RenameConfigurationWindow(RenameConfigurationViewModel model, TextBox name)
        : base(model, Body(model, name))
    {
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            name.Focus(NavigationMethod.Tab);
            name.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private static Grid Body(RenameConfigurationViewModel model, TextBox name)
    {
        var f = new DialogFieldBuilder(RenameConfigurationViewModel.DialogName);
        name.Text = model.ConfigName;
        AutomationProperties.SetAutomationId(name, f.Id("Name"));
        name.TextChanged += (_, _) => model.ConfigName = name.Text ?? "";
        return ConfigurationSubDialog.Rows(
            (DialogFieldBuilder.Label(Strings.NewConfiguration_Name, name), name),
            (null, f.Message("Name", model, () => model.Error, () => true, name)));
    }
}
