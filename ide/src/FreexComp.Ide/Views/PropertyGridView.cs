using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Designer.Inspector;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Menus;

namespace FreexComp.Ide.Views;

/// <summary>
/// The one property grid control (D-110): the rows of a <see cref="PropertyGridViewModel"/> (Properties or Events page)
/// with keyboard navigation and editing (DSG-OI-030 … 037), the inline error (UI-DSG-109), the value list (Alt+Down), the
/// … buttons, the name/value divider (UI-DSG-103) and the row context menu hook. The Object Inspector and the embedded
/// grids of the collection editor, the Menu Designer and the Child List Editor (dialogs.md §25 … §27) all use it; only the
/// AutomationIds and a few host behaviours (<see cref="IsInDialog"/>, <see cref="Escape"/>, the context menu) differ.
/// </summary>
public sealed class PropertyGridView : Border
{
    private const double RowHeight = 22; // UI-DSG-103
    private const double DividerWidth = 5; // the divider's hit area; 1 DIP is drawn

    private readonly string _rowPrefix;
    private readonly string? _categoryPrefix;
    private readonly StackPanel _rows = new();
    private readonly ScrollViewer _scroll;
    private readonly Panel _layers;
    private readonly TextBox _editor = new() { BorderThickness = new Thickness(0), Padding = new Thickness(4, 0), MinHeight = 0, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ListBox _list = new() { MaxHeight = 220, MinWidth = 120, Focusable = true };
    private readonly Popup _listPopup;
    private PropertyGridViewModel? _grid;
    private Control? _currentRow;
    private bool _building;
    private string? _pressedValuePath;
    private bool _draggingDivider;
    private double _ratio = ObjectInspectorViewModel.DefaultNameColumnRatio;
    private ContextMenu? _contextMenu;

    /// <param name="hostId">AutomationId of the focusable grid area.</param>
    /// <param name="rowPrefix">AutomationId prefix of the rows (<c>…Row.</c>); a row is <c>rowPrefix + path</c>.</param>
    /// <param name="categoryPrefix">AutomationId prefix of category rows; null when the host shows none.</param>
    public PropertyGridView(string hostId, string rowPrefix, string? categoryPrefix = null)
    {
        _rowPrefix = rowPrefix;
        _categoryPrefix = categoryPrefix;
        Focusable = true;
        AutomationProperties.SetAutomationId(this, hostId);
        AutomationProperties.SetAutomationId(_list, hostId + ".List"); // the row's own id is set when the list opens
        _scroll = new ScrollViewer
        {
            Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowAutoHide = false, // the scrollbar takes its own column: it never covers a row's … button
        };
        _layers = new Panel { Children = { _scroll } };
        Child = _layers;
        Bind(this, BackgroundProperty, "FxcBgPanelBrush");
        AddHandler(PointerPressedEvent, OnPointerPressedTunnel, RoutingStrategies.Tunnel);
        PointerMoved += OnPointerMovedDivider;
        PointerReleased += (_, e) => EndDividerDrag(e.Pointer);
        PointerCaptureLost += (_, _) => _draggingDivider = false;
        ContextRequested += OnContextRequestedHere;
        KeyDown += OnKeyDownHere;
        TextInput += OnTextInputHere;
        GotFocus += (_, _) => UpdateCurrentBackground();
        LostFocus += (_, _) => UpdateCurrentBackground();

        _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        _editor.TextChanged += (_, _) =>
        {
            if (!_building && _grid is { IsEditing: true } grid)
            {
                grid.EditText = _editor.Text ?? "";
            }
        };
        _editor.LostFocus += (_, _) =>
        {
            if (!_building && _grid is { IsEditing: true } grid && !_editor.IsKeyboardFocusWithin)
            {
                grid.Commit(InspectorCommitCause.FocusLost);
            }
        };

        // DSG-OI-032: Alt+Down opens the row's value list; Enter or a click enters and commits the value, Escape closes it.
        _list.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        DialogWindow.SetKeepsEnter(_list, true); // in a dialog the value list takes its own Enter
        _list.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        var listFrame = new Border { BorderThickness = new Thickness(1), Child = _list };
        Bind(listFrame, Border.BorderBrushProperty, "FxcBorderStrongBrush");
        Bind(listFrame, Border.BackgroundProperty, "FxcBgRaisedBrush");
        _listPopup = new Popup
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = true, ShouldUseOverlayLayer = true, Child = listFrame,
        };
        _layers.Children.Add(_listPopup);
    }

    /// <summary>The grid shown; rows are re-read on every change of it (DSG-OI-094).</summary>
    public PropertyGridViewModel? Grid
    {
        get => _grid;
        set
        {
            if (ReferenceEquals(_grid, value))
            {
                return;
            }

            _grid?.PropertyChanged -= OnGridPropertyChanged;
            _grid = value;
            _grid?.PropertyChanged += OnGridPropertyChanged;
            Rebuild();
        }
    }

    /// <summary>
    /// In a dialog (dialogs.md §25 … §27): Enter outside an editor is left to the dialog's default button and Escape to its
    /// Cancel; Enter and Escape inside an editor commit / cancel that edit only (UI-DLG-240).
    /// </summary>
    public bool IsInDialog { get; init; }

    /// <summary>The host shows no rows (the Object Inspector's empty states); keys and clicks do nothing.</summary>
    public Func<bool>? IsBlank { get; set; }

    /// <summary>Escape outside an editor (the Object Inspector: back to the document, UI-A11Y-004).</summary>
    public Action? Escape { get; set; }

    /// <summary>The row context menu (DSG-OI-039) and its AutomationId; none when null.</summary>
    public Func<IReadOnlyList<MenuItemModel>>? ContextMenuItems { get; set; }

    public string? ContextMenuId { get; set; }

    /// <summary>The name column's share of the row width (UI-DSG-103).</summary>
    public double NameColumnRatio
    {
        get => _ratio;
        set
        {
            _ratio = value;
            ApplyRatio();
        }
    }

    /// <summary>The user dragged the divider to a new ratio.</summary>
    public event Action<double>? RatioDragged;

    /// <summary>A layer drawn over the rows (the Object Inspector's empty-state text).</summary>
    public void AddOverlay(Control overlay) => _layers.Children.Insert(1, overlay);

    public bool IsEditorFocused => _editor.IsFocused;

    private bool Blank => _grid is null || (IsBlank?.Invoke() ?? false);

    private void OnGridPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PropertyGridViewModel.EditText):
                if (_grid is { IsEditing: true } grid && !string.Equals(_editor.Text, grid.EditText, StringComparison.Ordinal))
                {
                    RunBuilding(() => _editor.Text = grid.EditText);
                }

                break;
            case nameof(PropertyGridViewModel.IsEditing):
                if (_grid is { IsEditing: true } editing)
                {
                    RunBuilding(() => _editor.Text = editing.EditText);
                }

                Rebuild();
                break;
            case nameof(PropertyGridViewModel.Rows) or nameof(PropertyGridViewModel.EventRows) or nameof(PropertyGridViewModel.CurrentPath)
                or nameof(PropertyGridViewModel.CurrentEvent) or nameof(PropertyGridViewModel.EditError) or nameof(PropertyGridViewModel.Page):
                Rebuild();
                break;
        }
    }

    private void RunBuilding(Action action)
    {
        var was = _building;
        _building = true;
        try
        {
            action();
        }
        finally
        {
            _building = was;
        }
    }

    /// <summary>Re-creates the rows from the view-model.</summary>
    public void Rebuild()
    {
        var editorHadFocus = _editor.IsFocused;
        RunBuilding(() =>
        {
            if (_editor.Parent is Panel oldParent)
            {
                oldParent.Children.Remove(_editor);
            }
            else if (_editor.Parent is Decorator decorator)
            {
                decorator.Child = null;
            }

            _rows.Children.Clear();
            _currentRow = null;
            if (Blank || _grid is not { } grid)
            {
                return;
            }

            if (grid.Page == InspectorPage.Events)
            {
                foreach (var row in grid.EventRows)
                {
                    AddRow(grid, row.Name, row.Name, 0, row.Value, isBold: row.Value.Length > 0 && !row.IsInherited, isReadOnly: grid.IsEventsReadOnly,
                        isCurrent: string.Equals(row.Name, grid.CurrentEvent, StringComparison.OrdinalIgnoreCase), null, secondaryValue: row.IsInherited);
                }
            }
            else
            {
                foreach (var row in grid.Rows)
                {
                    if (row.Kind == PropertyRowKind.Category)
                    {
                        AddCategory(row);
                    }
                    else
                    {
                        AddRow(grid, row.Path, row.Name, row.Depth, row.Display, row.IsBold, row.IsReadOnly,
                            string.Equals(row.Path, grid.CurrentPath, StringComparison.OrdinalIgnoreCase), row);
                    }
                }
            }
        });

        UpdateCurrentBackground();
        if (_grid is { IsEditing: true } && _editor.Parent is not null)
        {
            if (editorHadFocus || IsKeyboardFocusWithin || IsFocused || _currentRow is not null)
            {
                // D-127: lay the new rows out first; a focus on the unarranged editor brings (0, 0) into view, which
                // scrolled the grid to the top whenever edit mode began.
                UpdateLayout();
                FocusEditor(selectAll: false);
            }
        }
        else if (editorHadFocus)
        {
            Focus();
        }

        if (_currentRow is { } current)
        {
            Dispatcher.UIThread.Post(() => current.BringIntoView(), DispatcherPriority.Background);
        }
    }

    private void AddCategory(PropertyRow row)
    {
        var collapsed = !row.IsExpanded;
        var text = new TextBlock { Text = (collapsed ? "▸ " : "▾ ") + row.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
        text.Classes.Add("bodyStrong");
        var border = new Border { Height = RowHeight, Child = text, Tag = new CategoryTag(row.Path) };
        if (_categoryPrefix is not null)
        {
            AutomationProperties.SetAutomationId(border, _categoryPrefix + row.Path);
        }

        Bind(border, Border.BackgroundProperty, "FxcBgHeaderBrush");
        _rows.Children.Add(border);
    }

    /// <param name="secondaryValue">DSG-OI-120: the value is a handler the frame assigns (<c>text.secondary</c>).</param>
    private void AddRow(PropertyGridViewModel grid, string path, string name, int depth, string display, bool isBold, bool isReadOnly, bool isCurrent,
        PropertyRow? row, bool secondaryValue = false)
    {
        var id = _rowPrefix + path;
        var nameCell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4 + 16 * depth, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        var expander = new TextBlock { Width = 16, Text = row is { IsExpandable: true } ? (row.IsExpanded ? "▾" : "▸") : "", VerticalAlignment = VerticalAlignment.Center };
        if (row is { IsExpandable: true })
        {
            AutomationProperties.SetAutomationId(expander, id + ".Expander");
            expander.Tag = new ExpanderTag(path);
            expander.Background = Brushes.Transparent;
        }

        nameCell.Children.Add(expander);
        var nameText = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        nameText.Classes.Add("body");
        Bind(nameText, TextBlock.ForegroundProperty, isReadOnly ? "FxcTextSecondaryBrush" : "FxcTextPrimaryBrush");
        nameCell.Children.Add(nameText);

        Control valueContent;
        var editing = isCurrent && grid.IsEditing;
        // UI-DSG-107 (D-127): the current row of a combo (a row with a value list) shows the chevron button at the right.
        var dropDown = isCurrent && !isReadOnly && grid.CurrentValueList().Count > 0 ? DropDownButton(grid, id, name) : null;
        if (editing)
        {
            AutomationProperties.SetAutomationId(_editor, id + ".Editor");
            AutomationProperties.SetName(_editor, name);
            _editor.Classes.Set("invalid", grid.EditError is not null);
            if (grid.EditError is not null)
            {
                _editor.BorderThickness = new Thickness(1);
                Bind(_editor, TextBox.BorderBrushProperty, "FxcStatusErrorBrush");
            }
            else
            {
                _editor.BorderThickness = new Thickness(0);
            }

            if (dropDown is null)
            {
                valueContent = _editor;
            }
            else
            {
                var combo = new DockPanel { LastChildFill = true };
                combo.Children.Add(dropDown);
                combo.Children.Add(_editor);
                valueContent = combo;
            }
        }
        else
        {
            var valueText = new TextBlock { Text = display, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            AutomationProperties.SetAutomationId(valueText, id + ".Value");
            valueText.Classes.Add(isBold ? "bodyStrong" : "body");
            valueText.FontWeight = isBold ? FontWeight.SemiBold : FontWeight.Normal;
            Bind(valueText, TextBlock.ForegroundProperty, isReadOnly || secondaryValue ? "FxcTextSecondaryBrush" : "FxcTextPrimaryBrush");
            if (row is { Source: PropertyRowSource.Name, IsReadOnly: true } && grid.FixedNameTip is { } tip)
            {
                ToolTip.SetTip(valueText, tip); // DSG-OI-119
            }
            var cell = new DockPanel { LastChildFill = true };
            if (row is { Info.Type: var type } && type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) || row is { SetMember: not null })
            {
                // UI-DSG-106: a 14 × 14 check box before True/False (the row toggles, the box only shows the state).
                var box = new CheckBox
                {
                    IsChecked = display == "True" ? true : display == "False" ? false : null, IsThreeState = true, IsHitTestVisible = false,
                    Focusable = false, Width = 14, Height = 14, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center, Template = CheckTemplate,
                };
                AutomationProperties.SetAccessibilityView(box, AccessibilityView.Raw);
                AutomationProperties.SetAutomationId(box, id + ".Check"); // UI-A11Y-030: every interactive element has an id
                DockPanel.SetDock(box, Dock.Left);
                cell.Children.Add(box);
            }

            if (row is not null && grid.ShowsButton(row) && !isReadOnly)
            {
                var button = new Button { Content = "…", Width = 22, MinWidth = 0, Padding = new Thickness(0), Focusable = false, VerticalAlignment = VerticalAlignment.Stretch };
                if (PropertyGridViewModel.IsCollectionRow(row))
                {
                    button.IsEnabled = grid.IsSingleEditableSelection; // §5.1: not for multi-selection or preserved components
                }

                AutomationProperties.SetAutomationId(button, id + ".Button");
                AutomationProperties.SetName(button, name);
                button.Click += (_, _) =>
                {
                    grid.SetCurrent(path);
                    _ = grid.EditInDialogAsync();
                };
                DockPanel.SetDock(button, Dock.Right);
                cell.Children.Add(button);
            }

            if (dropDown is not null)
            {
                cell.Children.Add(dropDown);
            }

            cell.Children.Add(valueText);
            valueContent = cell;
        }

        var valueCell = new Border { Padding = new Thickness(editing ? 0 : 4, 0, 0, 0), Child = valueContent, Background = Brushes.Transparent, Tag = new ValueTag(path) };
        // UI-DSG-103: the 1 DIP divider inside a 5 DIP drag area.
        var line = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        Bind(line, Border.BackgroundProperty, "FxcBorderSubtleBrush");
        var divider = new Border { Width = DividerWidth, Background = Brushes.Transparent, Child = line, Tag = new DividerTag(), Cursor = new Cursor(StandardCursorType.SizeWestEast) };
        AutomationProperties.SetAutomationId(divider, id + ".Divider");
        var cells = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(_ratio, GridUnitType.Star),
                new ColumnDefinition(DividerWidth, GridUnitType.Pixel),
                new ColumnDefinition(1 - _ratio, GridUnitType.Star),
            ],
        };
        cells.Children.Add(nameCell);
        Avalonia.Controls.Grid.SetColumn(divider, 1);
        cells.Children.Add(divider);
        Avalonia.Controls.Grid.SetColumn(valueCell, 2);
        cells.Children.Add(valueCell);

        var border = new Border { Height = RowHeight, Child = cells, Tag = new RowTag(path), BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent };
        Bind(border, Border.BorderBrushProperty, "FxcBorderSubtleBrush");
        AutomationProperties.SetAutomationId(border, id);
        AutomationProperties.SetName(border, name);
        if (isCurrent)
        {
            border.Classes.Add("current");
            _currentRow = border;
        }

        _rows.Children.Add(border);
        if (isCurrent && grid.EditError is { } error)
        {
            // UI-DSG-109: the message under the row (bg.raised, status.error, type.caption).
            var message = new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap };
            message.Classes.Add("caption");
            Bind(message, TextBlock.ForegroundProperty, "FxcStatusErrorBrush");
            AutomationProperties.SetAutomationId(message, id + ".Error");
            var popup = new Border { Padding = new Thickness(6, 2), Child = message, BorderThickness = new Thickness(1) };
            Bind(popup, Border.BackgroundProperty, "FxcBgRaisedBrush");
            Bind(popup, Border.BorderBrushProperty, "FxcStatusErrorBrush");
            _rows.Children.Add(popup);
        }
    }

    /// <summary>UI-DSG-107: the 16 DIP chevron button of a combo row; a click opens the value list (DSG-OI-032).</summary>
    private Button DropDownButton(PropertyGridViewModel grid, string id, string name)
    {
        var chevron = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M2.5,4.5 L6,8 L9.5,4.5"), Width = 12, Height = 12, StrokeThickness = 1.5, StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        Bind(chevron, Avalonia.Controls.Shapes.Shape.StrokeProperty, "FxcTextPrimaryBrush");
        var button = new Button
        {
            Content = chevron, Width = 16, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Focusable = false,
            VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(button, id + ".DropDown");
        AutomationProperties.SetName(button, name);
        button.Tag = new DropDownTag();
        button.Click += (_, _) => OpenValueList(grid); // keyboard / automation invoke; a press opens it in OnPointerPressedTunnel
        DockPanel.SetDock(button, Dock.Right);
        return button;
    }

    private void UpdateCurrentBackground()
    {
        if (_currentRow is Border border)
        {
            Bind(border, Border.BackgroundProperty, IsKeyboardFocusWithin ? "FxcBgSelectedBrush" : "FxcBgSelectedInactiveBrush");
        }
    }

    private void FocusEditor(bool selectAll)
    {
        _editor.Focus(NavigationMethod.Tab);
        if (selectAll)
        {
            _editor.SelectAll();
        }
        else
        {
            _editor.CaretIndex = _editor.Text?.Length ?? 0;
            _editor.SelectionStart = _editor.SelectionEnd = _editor.CaretIndex;
        }
    }

    /// <summary>Enters edit mode (Enter, F2, a click on the current row's value) with all text selected.</summary>
    private void BeginEdit(string? entry = null)
    {
        if (!Blank && _grid is { } grid && grid.BeginEdit(entry))
        {
            FocusEditor(selectAll: entry is null);
        }
    }

    /// <summary>
    /// D-127: input from the value list. Its popup's events route through the popup to this grid, so the grid's own
    /// handlers must leave them to the list (a press on an item would otherwise move focus and swallow the selection).
    /// </summary>
    private bool FromList(RoutedEventArgs e) => (e.Source as Visual)?.FindAncestorOfType<ListBox>(includeSelf: true) == _list;

    private void OnKeyDownHere(object? sender, KeyEventArgs e)
    {
        if (Blank || _grid is not { } grid || e.Handled || _editor.IsFocused || FromList(e))
        {
            return;
        }

        var handled = true;
        switch (e.Key, e.KeyModifiers)
        {
            case (Key.Up, KeyModifiers.None):
                grid.MoveCurrent(-1);
                break;
            case (Key.Down, KeyModifiers.None):
                grid.MoveCurrent(1);
                break;
            case (Key.PageUp, KeyModifiers.None):
                grid.MoveCurrent(-PageRows());
                break;
            case (Key.PageDown, KeyModifiers.None):
                grid.MoveCurrent(PageRows());
                break;
            case (Key.Home, KeyModifiers.None):
                grid.MoveCurrentToEdge(end: false);
                break;
            case (Key.End, KeyModifiers.None):
                grid.MoveCurrentToEdge(end: true);
                break;
            case (Key.Left, KeyModifiers.None):
                grid.ExpandCurrent(expand: false);
                break;
            case (Key.Right, KeyModifiers.None):
                grid.ExpandCurrent(expand: true);
                break;
            case (Key.Enter, KeyModifiers.None) when !IsInDialog:
                _ = ActivateAsync(grid);
                break;
            case (Key.F2, KeyModifiers.None):
                if (IsInDialog)
                {
                    _ = ActivateAsync(grid); // strings rows open their editor, others edit in place
                }
                else
                {
                    BeginEdit();
                }

                break;
            case (Key.Down, KeyModifiers.Alt):
                OpenValueList(grid);
                break;
            case (Key.F10, KeyModifiers.Shift):
            case (Key.Apps, KeyModifiers.None):
                if (ContextMenuItems is null)
                {
                    handled = false;
                    break;
                }

                OpenContextMenu(null);
                break;
            case (Key.Space, KeyModifiers.None):
                if (grid.Page == InspectorPage.Properties)
                {
                    grid.ToggleBoolean();
                }

                break;
            case (Key.Escape, KeyModifiers.None) when Escape is { } escape:
                escape();
                break;
            default:
                handled = false;
                break;
        }

        e.Handled = handled;
    }

    private async Task ActivateAsync(PropertyGridViewModel grid)
    {
        await grid.ActivateCurrentAsync();
        if (grid.IsEditing)
        {
            FocusEditor(selectAll: true);
        }
    }

    private int PageRows() => Math.Max(1, (int)(_scroll.Viewport.Height / RowHeight) - 1);

    /// <summary>DSG-OI-032: a printable character on a text-editable row replaces the value with that character.</summary>
    private void OnTextInputHere(object? sender, TextInputEventArgs e)
    {
        if (_editor.IsFocused || FromList(e) || string.IsNullOrWhiteSpace(e.Text) || e.Text.Any(char.IsControl))
        {
            return;
        }

        e.Handled = true;
        BeginEdit(e.Text);
    }

    /// <summary>DSG-OI-033: Enter, Tab and Up/Down commit (and move); Escape cancels (in a dialog: that edit only).</summary>
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_grid is not { IsEditing: true } grid)
        {
            return;
        }

        switch (e.Key, e.KeyModifiers)
        {
            case (Key.Enter, KeyModifiers.None):
                e.Handled = true; // UI-DLG-240: never the dialog's default button
                if (grid.Commit())
                {
                    Focus();
                }

                break;
            case (Key.Escape, KeyModifiers.None):
                e.Handled = true;
                grid.Cancel();
                Focus();
                break;
            case (Key.Down, KeyModifiers.Alt): // DSG-OI-032 (D-127): also while editing
                e.Handled = true;
                OpenValueList(grid);
                break;
            case (Key.Tab, KeyModifiers.None or KeyModifiers.Shift):
            case (Key.Up, KeyModifiers.None):
            case (Key.Down, KeyModifiers.None):
                e.Handled = true;
                var delta = e.Key == Key.Up || e.KeyModifiers == KeyModifiers.Shift ? -1 : 1;
                if (grid.Commit())
                {
                    Focus();
                    grid.MoveCurrent(delta);
                }

                break;
        }
    }

    /// <summary>
    /// A press in the grid (tunnel, so it is seen before the editor takes it): a click makes the row current, a click on
    /// the current row's value enters edit mode, a double-click on the value cycles enums/Booleans, opens the strings
    /// editor, or creates/shows an event handler (DSG-OI-032, 083); expanders and category rows toggle.
    /// </summary>
    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (Blank || _grid is not { } grid || FromList(e))
        {
            return;
        }

        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            RightPress(grid, e);
            return;
        }

        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        var source = e.Source as Visual;
        if (source is Control { Tag: DividerTag })
        {
            // UI-DSG-103: dragging the name/value divider.
            e.Handled = true;
            grid.Commit(InspectorCommitCause.FocusLost);
            _draggingDivider = true;
            e.Pointer.Capture(this);
            return;
        }

        if (source?.FindAncestorOfType<Button>(includeSelf: true) is { Tag: DropDownTag })
        {
            // D-127: the chevron opens the list on the press, before the press moves focus from the editor to the grid
            // (which ends the edit and rebuilds the row, so a click would never reach the button).
            e.Handled = true;
            OpenValueList(grid);
            return;
        }

        if (source?.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return; // the … button
        }

        string? row = null;
        string? value = null;
        string? expander = null;
        string? category = null;
        for (var v = source; v is not null && v != this; v = v.GetVisualParent())
        {
            switch ((v as Control)?.Tag)
            {
                case ValueTag t:
                    value ??= t.Path;
                    break;
                case ExpanderTag t:
                    expander ??= t.Path;
                    break;
                case RowTag t:
                    row ??= t.Path;
                    break;
                case CategoryTag t:
                    category ??= t.Name;
                    break;
            }
        }

        var inEditor = source?.FindAncestorOfType<TextBox>(includeSelf: true) == _editor;
        if (e.ClickCount % 2 == 0 && value is not null && value == _pressedValuePath) // every second press of a series is a double-click
        {
            e.Handled = true;
            grid.Cancel();
            grid.SetCurrent(value);
            Focus();
            DoubleClickValue(grid);
            return;
        }

        _pressedValuePath = value;
        if (inEditor)
        {
            return; // caret placement in the editor
        }

        e.Handled = true;
        if (grid.IsEditing)
        {
            grid.Commit(InspectorCommitCause.FocusLost);
        }

        if (category is not null)
        {
            Focus();
            grid.ToggleCategory(category);
            return;
        }

        if (row is null)
        {
            Focus();
            return;
        }

        var current = grid.Page == InspectorPage.Events ? grid.CurrentEvent : grid.CurrentPath;
        var wasCurrent = string.Equals(current, row, StringComparison.OrdinalIgnoreCase);
        grid.SetCurrent(row);
        Focus();
        if (expander is not null && grid.Rows.FirstOrDefault(r => r.Path == expander) is { IsExpandable: true } expandable)
        {
            if (expandable.IsExpanded)
            {
                grid.Collapse(expander);
            }
            else
            {
                grid.Expand(expander);
            }
        }
        else if (wasCurrent && value is not null && grid.Page == InspectorPage.Properties
            && grid.Rows.FirstOrDefault(r => r.Path == value) is { } clicked && !PropertyGridViewModel.HasEditorButton(clicked))
        {
            BeginEdit();
        }
    }

    private void DoubleClickValue(PropertyGridViewModel grid)
    {
        if (grid.Page == InspectorPage.Events)
        {
            if (grid.CurrentEvent is { } name)
            {
                grid.ActivateEvent(name);
            }

            return;
        }

        if (grid.Rows.FirstOrDefault(r => string.Equals(r.Path, grid.CurrentPath, StringComparison.OrdinalIgnoreCase)) is { } row
            && PropertyGridViewModel.HasEditorButton(row))
        {
            _ = grid.EditInDialogAsync();
            return;
        }

        if (!grid.CycleValue())
        {
            BeginEdit();
        }
    }

    /// <summary>The path or event name of the row under a press; null outside rows.</summary>
    private string? RowAt(Visual? source)
    {
        for (var v = source; v is not null && v != this; v = v.GetVisualParent())
        {
            if ((v as Control)?.Tag is RowTag t)
            {
                return t.Path;
            }
        }

        return null;
    }

    /// <summary>DSG-OI-039: a right press makes the row under it current (the menu opens on release).</summary>
    private void RightPress(PropertyGridViewModel grid, PointerPressedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) == _editor)
        {
            return; // the editor's own menu
        }

        if (grid.IsEditing)
        {
            grid.Commit(InspectorCommitCause.FocusLost);
        }

        if (RowAt(e.Source as Visual) is { } row)
        {
            grid.SetCurrent(row);
        }

        Focus();
    }

    private void OnContextRequestedHere(object? sender, ContextRequestedEventArgs e)
    {
        if (ContextMenuItems is null || FromList(e) || (e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) == _editor)
        {
            return;
        }

        e.Handled = true;
        OpenContextMenu(e.TryGetPosition(this, out var point) ? point : null);
    }

    /// <summary>DSG-OI-039: the row context menu at the pointer, or under the current row (Shift+F10, Apps).</summary>
    private void OpenContextMenu(Point? at)
    {
        if (Blank || ContextMenuItems is not { } items || _contextMenu is { IsOpen: true })
        {
            return;
        }

        var menu = MenuBarBuilder.CreateContextMenu(items());
        if (ContextMenuId is { } id)
        {
            AutomationProperties.SetAutomationId(menu, id);
        }

        if (at is { } point)
        {
            menu.Placement = PlacementMode.AnchorAndGravity;
            menu.PlacementRect = new Rect(point.X, point.Y, 1, 1);
            menu.PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft;
            menu.PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight;
        }
        else
        {
            menu.Placement = PlacementMode.Bottom;
            menu.PlacementTarget = _currentRow ?? this;
        }

        _contextMenu = menu;
        ContextMenu = menu;
        menu.Open(this);
    }

    /// <summary>
    /// DSG-OI-032: Alt+Down or the chevron button opens the drop-down list of a row that has one; other text rows enter
    /// edit mode. From edit mode the edit ends first (the list enters and commits its choice); the typed text, when it
    /// names an entry, is the one selected.
    /// </summary>
    private void OpenValueList(PropertyGridViewModel grid)
    {
        var values = grid.CurrentValueList();
        if (values.Count == 0 || _currentRow is null)
        {
            if (!grid.IsEditing)
            {
                BeginEdit();
            }

            return;
        }

        var current = grid.CurrentValueText;
        var width = _currentRow.Bounds.Width;
        if (grid.IsEditing)
        {
            if (values.FirstOrDefault(v => string.Equals(v, grid.EditText, StringComparison.OrdinalIgnoreCase)) is { } typed)
            {
                current = typed;
            }

            grid.Cancel(); // rebuilds the rows: the new current row is laid out before the list is placed under it
            UpdateLayout();
            if (_currentRow is null)
            {
                return;
            }
        }

        _list.ItemsSource = values;
        _list.SelectedIndex = Math.Max(0, values.ToList().FindIndex(v => string.Equals(v, current, StringComparison.OrdinalIgnoreCase)));
        AutomationProperties.SetAutomationId(_list, AutomationProperties.GetAutomationId(_currentRow) + ".List");
        _listPopup.PlacementTarget = _currentRow;
        _listPopup.Width = Math.Max(_list.MinWidth, Math.Max(width, _currentRow.Bounds.Width));
        _listPopup.IsOpen = true;
        if (!_list.Focus(NavigationMethod.Tab))
        {
            Dispatcher.UIThread.Post(() => _list.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded); // the popup is laid out first
        }
    }

    private void ChooseListValue()
    {
        var value = _list.SelectedItem as string;
        _listPopup.IsOpen = false;
        Focus();
        if (value is not null && _grid is { } grid)
        {
            grid.ChooseValue(value);
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key, e.KeyModifiers)
        {
            case (Key.Enter, KeyModifiers.None):
            case (Key.Up, KeyModifiers.Alt):
                e.Handled = true;
                ChooseListValue();
                break;
            case (Key.Escape, KeyModifiers.None):
                e.Handled = true;
                _listPopup.IsOpen = false;
                Focus();
                break;
            case (Key.Up or Key.Down, KeyModifiers.None):
                e.Handled = true;
                _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + (e.Key == Key.Up ? -1 : 1), 0, _list.ItemCount - 1);
                break;
        }
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null)
        {
            ChooseListValue();
        }
    }

    /// <summary>UI-DSG-103: the divider follows the pointer; both columns keep at least 60 DIP.</summary>
    private void OnPointerMovedDivider(object? sender, PointerEventArgs e)
    {
        if (!_draggingDivider)
        {
            return;
        }

        var width = _rows.Bounds.Width;
        var x = e.GetPosition(_rows).X;
        var min = ObjectInspectorViewModel.MinColumnWidth;
        var ratio = width > 2 * min + DividerWidth ? Math.Clamp(x, min, width - min - DividerWidth) / (width - DividerWidth) : 0.5;
        if (RatioDragged is { } dragged)
        {
            dragged(ratio);
        }
        else
        {
            NameColumnRatio = ratio;
        }
    }

    private void EndDividerDrag(IPointer pointer)
    {
        if (_draggingDivider)
        {
            _draggingDivider = false;
            pointer.Capture(null);
        }
    }

    /// <summary>Applies <see cref="NameColumnRatio"/> to the shown rows (no rebuild while dragging).</summary>
    private void ApplyRatio()
    {
        foreach (var row in _rows.Children.OfType<Border>())
        {
            if (row.Child is Grid { ColumnDefinitions.Count: 3 } cells)
            {
                cells.ColumnDefinitions[0].Width = new GridLength(_ratio, GridUnitType.Star);
                cells.ColumnDefinitions[2].Width = new GridLength(1 - _ratio, GridUnitType.Star);
            }
        }
    }

    /// <summary>
    /// UI-DSG-106 (I-219): the toolkit's check box template draws a 20 DIP box, which a 14 × 14 cell clips to a corner;
    /// this one fits: a 1 DIP <c>border.strong</c> square, checked = <c>accent.default</c> fill with a
    /// <c>text.onAccent</c> tick, undetermined (mixed set members) = a <c>text.primary</c> dash.
    /// </summary>
    private static readonly Avalonia.Controls.Templates.FuncControlTemplate<CheckBox> CheckTemplate = new((box, _) =>
    {
        var mark = new Avalonia.Controls.Shapes.Path
        {
            StrokeThickness = 1.5, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, IsHitTestVisible = false,
            Data = box.IsChecked switch
            {
                true => Geometry.Parse("M3,6.5 L5.5,9 L10,3.5"),
                null => Geometry.Parse("M3.5,6 L8.5,6"),
                _ => null,
            },
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        var square = new Border { Width = 14, Height = 14, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Child = mark };
        Bind(square, Border.BorderBrushProperty, box.IsChecked == true ? "FxcAccentDefaultBrush" : "FxcBorderStrongBrush");
        Bind(square, Border.BackgroundProperty, box.IsChecked == true ? "FxcAccentDefaultBrush" : "FxcBgEditorBrush");
        Bind(mark, Avalonia.Controls.Shapes.Shape.StrokeProperty, box.IsChecked == true ? "FxcTextOnAccentBrush" : "FxcTextPrimaryBrush");
        return square;
    });

    private static void Bind(Control control, AvaloniaProperty property, string resource) =>
        control.Bind(property, control.GetResourceObservable(resource));

    private sealed record RowTag(string Path);

    private sealed record ValueTag(string Path);

    private sealed record ExpanderTag(string Path);

    private sealed record CategoryTag(string Name);

    private sealed record DividerTag;

    private sealed record DropDownTag;
}
