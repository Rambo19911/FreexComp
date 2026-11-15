using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// Content of the Messages tool window (tool-windows.md §6): column header, the virtualized multi-selection list of
/// rows (UI-TW-411 AutomationIds <c>Tool.Messages.Row.&lt;key&gt;</c> and cells), the empty states (UI-TW-407) and the
/// default action on double-click (UI-TW-404); Right/Left expand and collapse related rows (UI-TW-403).
/// </summary>
public sealed class MessagesView : UserControl
{
    private const string Prefix = "Tool.Messages";

    private static readonly (string Id, string Header, GridLength Width, HorizontalAlignment Align)[] Columns =
    [
        ("Severity", "", new GridLength(28), HorizontalAlignment.Center),
        ("Code", Strings.MessagesView_Column_Code, new GridLength(64), HorizontalAlignment.Left),
        ("Description", Strings.MessagesView_Column_Description, new GridLength(1, GridUnitType.Star), HorizontalAlignment.Left),
        ("File", Strings.MessagesView_Column_File, new GridLength(160), HorizontalAlignment.Left),
        ("Line", Strings.MessagesView_Column_Line, new GridLength(56), HorizontalAlignment.Right),
        ("Col", Strings.MessagesView_Column_Col, new GridLength(48), HorizontalAlignment.Right),
    ];

    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple };
    private readonly TextBlock _empty = new() { Text = Strings.Msg_Messages_Empty, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
    private readonly StackPanel _filteredEmpty = new() { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 4 };
    private MessagesViewModel? _model;

    public MessagesView()
    {
        AutomationProperties.SetAutomationId(_list, Prefix + ".List");
        AutomationProperties.SetName(_list, Strings.Cmd_view_messages);
        ToolWindowFrame.SetIsPrimary(_list, true);
        _list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Avalonia.Styling.Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Avalonia.Styling.Setter(MinHeightProperty, 22.0) },
        });
        _list.ItemTemplate = new FuncDataTemplate<MessageRow>((row, _) => row is null ? new Border() : CreateRow(row), supportsRecycling: false);
        _list.SelectionChanged += (_, _) =>
        {
            if (_model is not null)
                _model.SelectedRows = [.. _list.SelectedItems?.OfType<MessageRow>() ?? []];
        };
        _list.DoubleTapped += OnDoubleTapped;
        _list.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _list.ContextRequested += OnContextRequested;
        AutomationProperties.SetAutomationId(_empty, Prefix + ".Empty");
        Bind(_empty, TextBlock.ForegroundProperty, "FxcTextSecondaryBrush");

        var filteredText = new TextBlock { Text = Strings.Msg_Messages_FilteredEmpty, HorizontalAlignment = HorizontalAlignment.Center };
        Bind(filteredText, TextBlock.ForegroundProperty, "FxcTextSecondaryBrush");
        var clear = new Button { Content = Strings.MessagesView_ClearFilter, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "link" } };
        AutomationProperties.SetAutomationId(clear, Prefix + ".FilteredEmpty.ClearFilter");
        clear.Click += (_, _) => _model?.ClearFilter();
        _filteredEmpty.Children.Add(filteredText);
        _filteredEmpty.Children.Add(clear);
        AutomationProperties.SetAutomationId(_filteredEmpty, Prefix + ".FilteredEmpty");

        var body = new Panel { Children = { _list, _empty, _filteredEmpty } };
        var panel = new DockPanel();
        var header = CreateHeader();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(body);
        Content = panel;
        SizeChanged += (_, _) => PlaceEmpty();
        DataContextChanged += (_, _) => Attach(DataContext as MessagesViewModel);
    }

    private static void Bind(Control control, AvaloniaProperty property, string resource) =>
        control.Bind(property, control.GetResourceObservable(resource));

    private static Grid Cells()
    {
        var grid = new Grid();
        foreach (var column in Columns)
            grid.ColumnDefinitions.Add(new ColumnDefinition(column.Width));
        return grid;
    }

    private static Grid CreateHeader()
    {
        var header = Cells();
        header.Height = 24;
        for (var i = 0; i < Columns.Length; i++)
        {
            var (id, text, _, align) = Columns[i];
            var cell = new TextBlock { Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
            if (id == "Severity")
                ToolTip.SetTip(cell, Strings.MessagesView_Column_Severity);
            AutomationProperties.SetAutomationId(cell, $"{Prefix}.Column.{id}");
            Bind(cell, TextBlock.ForegroundProperty, "FxcTextSecondaryBrush");
            Grid.SetColumn(cell, i);
            header.Children.Add(cell);
        }

        return header;
    }

    private static Control CreateRow(MessageRow row)
    {
        var grid = Cells();
        grid.MinHeight = 22;
        grid.Tag = row;
        var id = $"{Prefix}.Row.{row.Key}";
        AutomationProperties.SetAutomationId(grid, id);
        AutomationProperties.SetName(grid, row.Description);

        var severity = new Panel { Width = 28, Height = 22 };
        if (row.Icon.Length > 0)
            severity.Children.Add(new IdeIcon { IconName = row.Icon, Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Center });
        if (row.HasChildren)
            severity.Children.Add(new IdeIcon { IconName = row.IsExpanded ? "chevron-down" : "chevron-right", Width = 8, Height = 8, HorizontalAlignment = HorizontalAlignment.Left });
        AutomationProperties.SetAutomationId(severity, id + ".Severity");
        AutomationProperties.SetName(severity, row.SeverityText);
        if (row.SeverityText.Length > 0)
            ToolTip.SetTip(severity, row.SeverityText);
        grid.Children.Add(severity);

        AddCell(grid, 1, id + ".Code", row.Code, "FxcTextPrimaryBrush", HorizontalAlignment.Left);
        var description = AddCell(grid, 2, id + ".Description", row.Description, row.IsChild ? "FxcTextSecondaryBrush" : "FxcTextPrimaryBrush",
            HorizontalAlignment.Left);
        if (row.Item.IsSummary)
            description.FontWeight = FontWeight.SemiBold; // type.bodyStrong (UI-TW-402)
        var file = AddCell(grid, 3, id + ".File", row.File, row.File.Length > 0 ? "FxcTextLinkBrush" : "FxcTextPrimaryBrush", HorizontalAlignment.Left);
        if (row.FullPath.Length > 0)
            ToolTip.SetTip(file, row.FullPath);
        AddCell(grid, 4, id + ".Line", row.Line, "FxcTextPrimaryBrush", HorizontalAlignment.Right);
        AddCell(grid, 5, id + ".Col", row.Col, "FxcTextPrimaryBrush", HorizontalAlignment.Right);
        return grid;
    }

    private static TextBlock AddCell(Grid grid, int column, string id, string text, string brush, HorizontalAlignment align)
    {
        var cell = new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
        };
        AutomationProperties.SetAutomationId(cell, id);
        Bind(cell, TextBlock.ForegroundProperty, brush);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
        return cell;
    }

    private void Attach(MessagesViewModel? model)
    {
        if (_model is not null)
            _model.PropertyChanged -= OnModelChanged;
        _model = model;
        if (model is not null)
            model.PropertyChanged += OnModelChanged;
        Update();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MessagesViewModel.Rows) or nameof(MessagesViewModel.IsEmpty) or nameof(MessagesViewModel.IsFilteredEmpty))
            Update();
        else if (e.PropertyName == nameof(MessagesViewModel.CurrentRow) && _model?.CurrentRow is { } current)
        {
            _list.SelectedItem = current; // UI-TW-405: F8 selects the row it navigates to
            _list.ScrollIntoView(current);
        }
    }

    private void Update()
    {
        var rows = _model?.Rows ?? [];
        var selected = _model?.SelectedRows.Select(r => r.Key).ToHashSet(StringComparer.Ordinal) ?? [];
        _list.ItemsSource = rows;
        foreach (var row in rows.Where(r => selected.Contains(r.Key)))
            _list.SelectedItems?.Add(row);
        _empty.IsVisible = _model?.IsEmpty ?? true;
        _filteredEmpty.IsVisible = _model?.IsFilteredEmpty ?? false;
    }

    private MessageRow? RowAt(object? source)
    {
        for (var element = source as StyledElement; element is not null; element = element.Parent)
        {
            if (element is Grid { Tag: MessageRow row })
                return row;
        }

        return _list.SelectedItem as MessageRow;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_model is null || RowAt(e.Source) is not { } row)
            return;
        e.Handled = true;
        _ = _model.GoToAsync(row);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is null || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Right or Key.Left))
            return;
        if (_list.SelectedItem is MessageRow { HasChildren: true } row && row.IsExpanded != (e.Key == Key.Right))
        {
            var key = row.Key;
            _model.SetExpanded(row, e.Key == Key.Right);
            _list.SelectedItem = _model.Rows.FirstOrDefault(r => r.Key == key);
            e.Handled = true;
        }
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_model is null)
            return;
        var menu = MenuBarBuilder.CreateContextMenu(_model.BuildContextMenu());
        menu.Open(_list);
        e.Handled = true;
    }

    /// <summary>UI-TW-013: the empty-state text sits at one third of the content height.</summary>
    private void PlaceEmpty()
    {
        var margin = new Thickness(0, Math.Max(0, Bounds.Height / 3), 0, 0);
        _empty.Margin = margin;
        _filteredEmpty.Margin = margin;
    }
}
