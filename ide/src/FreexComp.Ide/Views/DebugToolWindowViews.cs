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
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// Content of a debug tool window (tool-windows.md §10–§15): the empty-state block <c>Tool.&lt;Name&gt;.Empty</c> of
/// UI-TW-013 — heading in <c>text.secondary</c>, optional secondary line in <c>type.caption</c> <c>text.tertiary</c>,
/// centred horizontally at one third of the height — shown instead of the rows while the view-model says the window is
/// empty, and the rows themselves: a header and a list drawn from the view-model's columns and rows
/// (<see cref="DebugGridViewModel"/>) with the AutomationIds <c>Tool.&lt;Name&gt;.List</c>, <c>.Column.&lt;id&gt;</c>,
/// <c>.Row.&lt;key&gt;</c> and <c>.Row.&lt;key&gt;.&lt;Column&gt;</c>. Double-click is the default action; a tree column
/// shows expanders; an in-place editor (<c>.Row.&lt;key&gt;.Editor</c>) commits with Enter and cancels with Escape.
/// </summary>
public abstract class DebugToolWindowView : UserControl
{
    private const double RowHeight = 22;
    private const double Indent = 16; // space.6 per tree level

    private readonly string _prefix;
    private readonly TextBlock _heading = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _secondary = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _empty = new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 0), IsHitTestVisible = false };
    private readonly ListBox _list = new();
    private readonly Grid _header = new() { Height = 24 };
    private readonly Panel _root;
    private readonly Dictionary<string, (TextBlock Label, string Text)> _headerLabels = new(StringComparer.Ordinal);
    private DebugToolWindowViewModel? _model;
    private DebugGridViewModel? _grid;
    private bool _syncingSelection;
    private ScrollViewer? _scroller;
    private bool _followsEnd = true; // UI-TW-504 (Event Log, UI-TW-1300)

    protected DebugToolWindowView(string toolName)
    {
        _prefix = AutomationIds.Tool(toolName);
        AutomationProperties.SetAutomationId(_empty, _prefix + ".Empty");
        _heading.Bind(TextBlock.ForegroundProperty, _heading.GetResourceObservable("FxcTextSecondaryBrush"));
        _secondary.Classes.Add("caption");
        _secondary.Bind(TextBlock.ForegroundProperty, _secondary.GetResourceObservable("FxcTextTertiaryBrush"));
        _empty.Children.Add(_heading);
        _empty.Children.Add(_secondary);

        AutomationProperties.SetAutomationId(_list, _prefix + ".List");
        // UI-TW-007 (I-307): the list is the primary control; a list box is not focusable by itself, and the window's
        // panel (focusable for the empty state) would take the focus instead.
        _list.Focusable = true;
        ToolWindowFrame.SetIsPrimary(_list, true);
        _list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(MinHeightProperty, RowHeight) },
        });
        _list.ItemTemplate = new FuncDataTemplate<DebugGridRow>((row, _) => row is null ? new Border() : CreateRow(row), supportsRecycling: false);
        _list.SelectionChanged += OnSelectionChanged;
        _list.DoubleTapped += OnDoubleTapped;
        _list.Tapped += OnTapped;
        _list.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _list.ContextRequested += OnContextRequested;
        _list.TemplateApplied += (_, e) =>
        {
            _scroller?.ScrollChanged -= OnScrollChanged;
            _scroller = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
            _scroller?.ScrollChanged += OnScrollChanged;
        };
        Input.ListBehaviors.FocusOnRightPress(_list); // UI-TW-014 (I-283)

        var rows = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        rows.Children.Add(_header);
        rows.Children.Add(_list);
        ContentHost.Children.Add(rows);

        // One third of the height above the block, two thirds below.
        var placed = new Grid { RowDefinitions = new RowDefinitions("*,Auto,2*"), IsHitTestVisible = false };
        Grid.SetRow(_empty, 1);
        placed.Children.Add(_empty);
        _root = new Panel { Focusable = true, Background = Brushes.Transparent, Children = { ContentHost, placed } };
        Content = _root;
        DataContextChanged += (_, _) => Attach(DataContext as DebugToolWindowViewModel);
        // UI-TW-504: Ctrl+End resumes following the end, wherever the focus is in the window (the list moves its focus to
        // the last row as well when it has it).
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e is { Key: Key.End, KeyModifiers: KeyModifiers.Control } && e.Source is not TextBox)
            {
                _followsEnd = true;
                ScrollToEnd();
            }
        }, RoutingStrategies.Tunnel);
        Update();
    }

    /// <summary>Where the window's rows go; hidden while the empty state is shown.</summary>
    protected Panel ContentHost { get; } = new();

    private void Attach(DebugToolWindowViewModel? model)
    {
        if (_model is not null)
            _model.PropertyChanged -= OnModelChanged;
        if (_grid is not null)
        {
            _grid.SelectionRequested -= OnSelectionRequested;
            _grid.ScrollToEndRequested -= OnScrollToEndRequested;
            _grid.ColumnWidthsChanged -= OnColumnWidthsChanged;
        }

        _model = model;
        _grid = model as DebugGridViewModel;
        if (model is not null)
            model.PropertyChanged += OnModelChanged;
        if (_grid is not null)
        {
            _grid.SelectionRequested += OnSelectionRequested;
            _grid.ScrollToEndRequested += OnScrollToEndRequested;
            _grid.ColumnWidthsChanged += OnColumnWidthsChanged;
            _list.SelectionMode = _grid.MultiSelect ? SelectionMode.Multiple : SelectionMode.Single;
            AutomationProperties.SetName(_list, _grid.Title);
            BuildHeader();
        }

        Update();
        UpdateRows();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DebugGridViewModel.Rows) or nameof(DebugGridViewModel.Edit):
                UpdateRows();
                break;
            case nameof(DebugGridViewModel.SortColumn):
                UpdateHeader();
                break;
            case nameof(DebugToolWindowViewModel.EmptyHeading) or nameof(DebugToolWindowViewModel.EmptySecondary) or nameof(DebugToolWindowViewModel.IsEmpty):
                Update();
                break;
        }
    }

    private void Update()
    {
        _heading.Text = _model?.EmptyHeading;
        _secondary.Text = _model?.EmptySecondary;
        _secondary.IsVisible = _model?.EmptySecondary is not null;
        _empty.IsVisible = _model?.IsEmpty == true;
        ContentHost.IsVisible = _model is { IsEmpty: false } or { ShowsContentWhenEmpty: true };
        // The panel takes keyboard focus (view.*, F6) while there is no list to take it.
        ToolWindowFrame.SetIsPrimary(_root, !ContentHost.IsVisible);
    }

    private Grid Cells()
    {
        var grid = new Grid();
        foreach (var column in _grid?.Columns ?? [])
            grid.ColumnDefinitions.Add(Definition(column));
        return grid;
    }

    /// <summary>
    /// A column at its specified width while the window is wide enough. In a narrow dock (the right dock is 300 wide, the
    /// columns of Locals ask for 320 plus the value) the columns share the width in proportion, so that the remaining-width
    /// column never disappears; glyph and check-box columns keep their fixed width.
    /// </summary>
    private ColumnDefinition Definition(DebugGridColumn column)
    {
        var definition = new ColumnDefinition();
        SizeColumn(definition, column);
        return definition;
    }

    /// <summary>UI-TW-011/012: a resizable column at the user's width (<see cref="DebugGridViewModel.WidthOf"/>).</summary>
    private void SizeColumn(ColumnDefinition definition, DebugGridColumn column)
    {
        var width = _grid?.WidthOf(column) ?? column.Width;
        (definition.Width, definition.MaxWidth) = column.Width <= 0 ? (new GridLength(RemainingWeight, GridUnitType.Star), double.PositiveInfinity)
            : !DebugGridViewModel.IsResizable(column) ? (new GridLength(width, GridUnitType.Pixel), double.PositiveInfinity)
            : (new GridLength(width, GridUnitType.Star), width);
    }

    private const double RemainingWeight = 200;
    private const double DividerWidth = 6;

    /// <summary>A column got another width: the header and the rows shown take it at once (no new rows are made).</summary>
    private void OnColumnWidthsChanged(object? sender, EventArgs e)
    {
        var columns = _grid?.Columns ?? [];
        IEnumerable<Grid> grids = [_header, .. _list.GetRealizedContainers().SelectMany(c => c.GetVisualDescendants().OfType<Grid>().Where(g => g.Tag is DebugGridRow).Take(1))];
        foreach (var grid in grids)
        {
            for (var i = 0; i < columns.Count && i < grid.ColumnDefinitions.Count; i++)
                SizeColumn(grid.ColumnDefinitions[i], columns[i]);
        }
    }

    /// <summary>
    /// UI-TW-011: the divider at the right edge of a resizable header cell — dragging it resizes the column (min 32),
    /// double-clicking it fits the column to its widest content (header and the rows shown).
    /// </summary>
    private Border CreateDivider(DebugGridColumn column, int index)
    {
        var divider = new Border
        {
            Width = DividerWidth,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
        };
        AutomationProperties.SetAutomationId(divider, $"{_prefix}.Column.{column.Id}.Divider");
        (double X, double Width)? drag = null;
        divider.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(divider).Properties.IsLeftButtonPressed)
                return;
            e.Handled = true;
            if (e.ClickCount >= 2)
            {
                drag = null;
                AutoFit(column, index);
                return;
            }

            drag = (e.GetPosition(_header).X, _header.ColumnDefinitions[index].ActualWidth);
            e.Pointer.Capture(divider);
        };
        divider.PointerMoved += (_, e) =>
        {
            if (drag is { } start)
                _grid?.ResizeColumn(column.Id, start.Width + e.GetPosition(_header).X - start.X);
        };
        divider.PointerReleased += (_, e) =>
        {
            e.Handled = true; // not a click on the header cell: the sort stays
            drag = null;
            e.Pointer.Capture(null);
        };
        divider.PointerCaptureLost += (_, _) => drag = null;
        return divider;
    }

    /// <summary>UI-TW-011: the widest of the header text and the column's cells of the rows shown, measured without limit.</summary>
    private void AutoFit(DebugGridColumn column, int index)
    {
        var header = _headerLabels[column.Id].Label;
        header.Measure(Size.Infinity);
        var width = header.DesiredSize.Width + DividerWidth;
        foreach (var grid in _list.GetRealizedContainers().SelectMany(c => c.GetVisualDescendants().OfType<Grid>().Where(g => g.Tag is DebugGridRow).Take(1)))
        {
            foreach (var cell in grid.Children.Where(c => Grid.GetColumn(c) == index))
            {
                cell.Measure(Size.Infinity);
                width = Math.Max(width, cell.DesiredSize.Width);
            }
        }

        _grid?.ResizeColumn(column.Id, Math.Ceiling(width));
        InvalidateMeasure();
    }

    private void BuildHeader()
    {
        _header.Children.Clear();
        _header.ColumnDefinitions.Clear();
        _headerLabels.Clear();
        var columns = _grid!.Columns;
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            _header.ColumnDefinitions.Add(Definition(column));
            var label = new TextBlock
            {
                Text = column.Header,
                HorizontalAlignment = column.AlignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("FxcTextSecondaryBrush"));
            // UI-TW-011: the whole header cell is the click target; sortable columns cycle the sort.
            var cell = new Border { Background = Brushes.Transparent, Child = label };
            AutomationProperties.SetAutomationId(cell, $"{_prefix}.Column.{column.Id}");
            cell.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left)
                    _grid?.CycleSort(column.Id);
            };
            _headerLabels[column.Id] = (label, column.Header);
            Grid.SetColumn(cell, i);
            _header.Children.Add(cell);
            if (DebugGridViewModel.IsResizable(column))
            {
                var divider = CreateDivider(column, i);
                Grid.SetColumn(divider, i);
                _header.Children.Add(divider);
            }
        }
    }

    /// <summary>UI-TW-011: the sort column shows ▲ / ▼ after its text.</summary>
    private void UpdateHeader()
    {
        foreach (var (id, (label, text)) in _headerLabels)
            label.Text = _grid?.SortColumn == id ? text + (_grid.SortDescending ? " ▼" : " ▲") : text;
    }

    private void UpdateRows()
    {
        // A new list every time: the rows are drawn again even when only the in-place editor changed.
        DebugGridRow[] rows = [.. _grid?.Rows ?? []];
        _syncingSelection = true;
        _list.ItemsSource = rows;
        ApplySelection();
        _syncingSelection = false;
        UpdateHeader();
    }

    private void ApplySelection()
    {
        var keys = _grid?.SelectedKeys ?? [];
        var selected = (_grid?.Rows ?? []).Where(r => keys.Contains(r.Key)).ToList();
        _list.SelectedItems?.Clear();
        foreach (var row in selected)
        {
            if (_list.SelectionMode.HasFlag(SelectionMode.Multiple))
                _list.SelectedItems?.Add(row);
            else
                _list.SelectedItem = row;
        }
    }

    private void OnSelectionRequested(object? sender, EventArgs e)
    {
        _syncingSelection = true;
        ApplySelection();
        _syncingSelection = false;
        if (_list.SelectedItem is { } first)
            _list.ScrollIntoView(first);
    }

    /// <summary>UI-TW-504 / UI-TW-1300: appended rows keep the list at its end only while it follows the end.</summary>
    private void OnScrollToEndRequested(object? sender, EventArgs e)
    {
        if (_followsEnd)
            ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (_grid is { Rows: [.., var last] })
            _list.ScrollIntoView(last);
    }

    /// <summary>
    /// UI-TW-504: the user's scrolling decides whether the list follows the end — scrolled up it stops, scrolled back to
    /// the end it resumes. A change of the content alone (rows appended, the offset unchanged) decides nothing.
    /// </summary>
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta.Y == 0 || _scroller is not { } scroller)
            return;
        _followsEnd = scroller.Offset.Y + scroller.Viewport.Height >= scroller.Extent.Height - 1;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_grid is not null && !_syncingSelection)
            _grid.SelectedKeys = [.. _list.SelectedItems?.OfType<DebugGridRow>().Select(r => r.Key) ?? []];
    }

    private Control CreateRow(DebugGridRow row)
    {
        var grid = Cells();
        grid.MinHeight = RowHeight;
        grid.Tag = row;
        var id = $"{_prefix}.Row.{row.Key}";
        AutomationProperties.SetAutomationId(grid, id);
        AutomationProperties.SetName(grid, row.AutomationName);
        if (row.IsHighlighted)
            grid.Bind(Panel.BackgroundProperty, grid.GetResourceObservable("FxcStatusWarningBgBrush")); // UI-TW-803
        else
            grid.Background = Brushes.Transparent;

        var columns = _grid!.Columns;
        var edit = _grid.Edit is { } active && active.RowKey == row.Key ? active : null;
        for (var i = 0; i < columns.Count && i < row.Cells.Count; i++)
        {
            var cell = edit is not null && edit.Column == columns[i].Id
                ? CreateEditor(edit, id)
                : CreateCell(row, row.Cells[i], columns[i], id + "." + columns[i].Id, tree: i == 0 && _grid.IsTree);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private Control CreateCell(DebugGridRow row, DebugGridCell cell, DebugGridColumn column, string id, bool tree)
    {
        Control control;
        if (cell.Checked is { } isChecked)
        {
            var box = new CheckBox { IsChecked = isChecked, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, Padding = new Thickness(0), Focusable = false };
            box.Click += (_, e) =>
            {
                _grid?.Toggle(row);
                e.Handled = true;
            };
            AutomationProperties.SetName(box, Core.Resources.Strings.DebugView_Column_Enabled);
            control = box;
        }
        else if (cell.Glyph is { } glyph)
        {
            control = new DebugGlyph { Kind = glyph, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(control, glyph.ToString());
        }
        else
        {
            var text = new TextBlock
            {
                Text = cell.Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = column.AlignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0),
            };
            var brush = cell.Style switch
            {
                DebugCellStyle.Tertiary => "FxcTextTertiaryBrush",
                DebugCellStyle.Error or DebugCellStyle.Changed => "FxcStatusErrorBrush",
                DebugCellStyle.Warning => "FxcStatusWarningBrush",
                _ => "FxcTextPrimaryBrush",
            };
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(brush));
            if (cell.Style is DebugCellStyle.Code or DebugCellStyle.Changed)
                text.Classes.Add("codeSmall");
            if (cell.Style == DebugCellStyle.Changed || row.IsHighlighted)
                text.FontWeight = FontWeight.SemiBold; // type.bodyStrong
            if (cell.Secondary is { Length: > 0 } secondary)
            {
                var suffix = new Avalonia.Controls.Documents.Run(secondary);
                suffix.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, text.GetResourceObservable("FxcTextTertiaryBrush"));
                text.Inlines = [new Avalonia.Controls.Documents.Run(cell.Text), suffix];
            }

            control = text;
        }

        AutomationProperties.SetAutomationId(control, id);
        if (cell.ToolTip is { Length: > 0 } tip)
            ToolTip.SetTip(control, tip);
        if (!tree)
            return control;

        // The tree column: indent per level, then the expander (or its space), then the name.
        var panel = new DockPanel { Margin = new Thickness(row.Level * Indent, 0, 0, 0), Background = Brushes.Transparent };
        var expander = new Border { Width = 16, Height = RowHeight, Background = Brushes.Transparent };
        if (row.IsExpanded is { } expanded)
        {
            expander.Child = new IdeIcon { IconName = expanded ? "chevron-down" : "chevron-right", Width = 8, Height = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            expander.Tag = ExpanderTag;
            AutomationProperties.SetAutomationId(expander, $"{_prefix}.Row.{row.Key}.Expander");
        }

        DockPanel.SetDock(expander, Dock.Left);
        panel.Children.Add(expander);
        panel.Children.Add(control);
        return panel;
    }

    private static readonly object ExpanderTag = new();

    /// <summary>
    /// UI-TW-1004 / UI-TW-1103 / UI-TW-1504: the input replaces the cell; a failed commit shows its message under it; an
    /// edit that commits on focus loss does so and gives the list the focus back after Enter.
    /// </summary>
    private Control CreateEditor(DebugGridEdit edit, string rowId)
    {
        var box = new TextBox { Text = edit.Text, MinHeight = RowHeight, Padding = new Thickness(3, 1), VerticalContentAlignment = VerticalAlignment.Center };
        if (edit.IsCode)
            box.Classes.Add("codeSmall");
        AutomationProperties.SetAutomationId(box, rowId + edit.IdSuffix);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(4, 0) };
        error.Classes.Add("caption");
        error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable("FxcStatusErrorBrush"));
        AutomationProperties.SetAutomationId(error, rowId + ".Error");
        void ShowError()
        {
            error.Text = edit.Error;
            error.IsVisible = edit.Error is not null;
        }

        edit.PropertyChanged += (_, _) => ShowError();
        ShowError();
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.None)
                return;
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = edit.CommitAsync(box.Text ?? ""); // never faults; a view model registers what it starts with the debug service (I-221)
                if (edit.CommitsOnFocusLoss && _grid?.Edit != edit)
                    _list.Focus();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                edit.Cancel();
                _list.Focus();
            }
        }, RoutingStrategies.Tunnel);
        if (edit.CommitsOnFocusLoss)
        {
            // Posted: a click on another row selects that row first, then the commit redraws the rows.
            box.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (_grid?.Edit == edit)
                    _ = edit.CommitAsync(box.Text ?? "");
            });
        }

        box.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (box.IsEffectivelyVisible && _grid?.Edit == edit)
            {
                box.Focus();
                box.SelectAll();
            }
        });
        return new StackPanel { Children = { box, error } };
    }

    /// <summary>
    /// The row a tap is on, the column under the pointer (by the row's column bounds: a cell is hit wherever it is
    /// tapped, not only on its glyphs) and whether the tap is on the row's expander.
    /// </summary>
    private (DebugGridRow? Row, string? Column, bool Expander) Hit(TappedEventArgs e)
    {
        var expander = false;
        for (var element = e.Source as StyledElement; element is not null; element = element.Parent)
        {
            if (element is not Control { Tag: var tag } control)
                continue;
            if (ReferenceEquals(tag, ExpanderTag))
            {
                expander = true;
            }
            else if (tag is DebugGridRow row && control is Grid grid)
            {
                var x = e.GetPosition(grid).X;
                string? column = null;
                var columns = _grid?.Columns ?? [];
                for (var i = 0; i < columns.Count && i < grid.ColumnDefinitions.Count; i++)
                {
                    x -= grid.ColumnDefinitions[i].ActualWidth;
                    if (x < 0)
                    {
                        column = columns[i].Id;
                        break;
                    }
                }

                return (row, column, expander);
            }
        }

        return (null, null, false);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_grid is null || Hit(e) is not { Row: { } row } hit)
            return;
        if (hit.Expander)
        {
            _grid.Toggle(row);
            e.Handled = true;
        }
        else if (row.Key == WatchesViewModel.NewKey && _grid is WatchesViewModel)
        {
            _ = _grid.ActivateAsync(row, hit.Column); // UI-TW-1104: clicking the placeholder row starts watches.add
            e.Handled = true;
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_grid is null || e.Source is TextBox || Hit(e) is not { Row: { } row } hit || hit.Expander)
            return;
        e.Handled = true;
        _ = _grid.ActivateAsync(row, hit.Column);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_grid is null || e.KeyModifiers != KeyModifiers.None || e.Source is TextBox || _list.SelectedItem is not DebugGridRow row)
            return;
        switch (e.Key)
        {
            case Key.Space when row.Cells.Any(c => c.Checked is not null):
                _grid.Toggle(row); // tool-windows.md §10: Space toggles the check box of the row
                e.Handled = true;
                break;
            case Key.Right when row.IsExpanded == false:
            case Key.Left when row.IsExpanded == true:
                _grid.Toggle(row);
                e.Handled = true;
                break;
            // Locals, Watches and the Event Log have no Enter command: Enter is the default action of the row.
            case Key.Enter when _grid is VariableTreeViewModel or EventLogViewModel:
                _ = _grid.ActivateAsync(row, null);
                e.Handled = true;
                break;
        }
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_grid is null || _grid.ContextCommands.Count == 0)
            return;
        MenuBarBuilder.CreateContextMenu(_grid.BuildContextMenu()).Open(_list);
        e.Handled = true;
    }
}

/// <summary>A glyph of the editor's glyph margin (editor.md §3.1) in a 16 × 16 cell of a debug tool window.</summary>
public sealed class DebugGlyph : Control
{
    public static readonly StyledProperty<LineGlyphKind> KindProperty = AvaloniaProperty.Register<DebugGlyph, LineGlyphKind>(nameof(Kind));

    static DebugGlyph() => AffectsRender<DebugGlyph>(KindProperty);

    public DebugGlyph()
    {
        Width = 16;
        Height = 16;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public LineGlyphKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public override void Render(DrawingContext context) =>
        GlyphMargin.DrawGlyph(context, new LineGlyph(Kind), token => ThemeBrushes.Find(this, token));
}

/// <summary>Breakpoints (tool-windows.md §10).</summary>
public sealed class BreakpointsView() : DebugToolWindowView(BreakpointsViewModel.ToolName);

/// <summary>Call Stack (tool-windows.md §11).</summary>
public sealed class CallStackView() : DebugToolWindowView(CallStackViewModel.ToolName);

/// <summary>Locals (tool-windows.md §12).</summary>
public sealed class LocalsView() : DebugToolWindowView(LocalsViewModel.ToolName);

/// <summary>Watches (tool-windows.md §13).</summary>
public sealed class WatchesView() : DebugToolWindowView(WatchesViewModel.ToolName);

/// <summary>Threads (tool-windows.md §14).</summary>
public sealed class ThreadsView() : DebugToolWindowView(ThreadsViewModel.ToolName);

/// <summary>Event Log (tool-windows.md §15).</summary>
public sealed class EventLogView() : DebugToolWindowView(EventLogViewModel.ToolName);

/// <summary>Bookmarks (tool-windows.md §18, F11-045).</summary>
public sealed class BookmarksView() : DebugToolWindowView(BookmarksViewModel.ToolName);

/// <summary>Templates (code-templates.md §5, F11-052).</summary>
public sealed class TemplatesView() : DebugToolWindowView(TemplatesViewModel.ToolName);

/// <summary>History (local-history.md §2, F11-060).</summary>
public sealed class HistoryView() : DebugToolWindowView(HistoryViewModel.ToolName);
