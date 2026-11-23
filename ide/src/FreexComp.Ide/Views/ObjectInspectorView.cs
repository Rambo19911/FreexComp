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
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// Content of the Object Inspector tool window (ui/designer.md §11, object-inspector.md; D-081): instance selector, page
/// tabs with the arrangement toggles, filter, the property grid (primary control: keyboard navigation and editing of
/// DSG-OI-030 … 037, the inline error of UI-DSG-109) and the description pane. Built in code; the logic is in
/// <see cref="ObjectInspectorViewModel"/>.
/// </summary>
public sealed class ObjectInspectorView : UserControl
{
    private const string Prefix = "Tool.ObjectInspector";
    private const double RowHeight = 22; // UI-DSG-103
    private const double DividerWidth = 5; // the divider's hit area; 1 DIP is drawn
    private const double SplitterHeight = 5; // UI-DSG-100 splitter: 5 hit / 1 visible
    private const double MinDescriptionHeight = 44; // UI-DSG-110
    private const double MinGridHeight = 44;

    private readonly ComboBox _selector = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 26 };
    private readonly Button _tabProperties = new() { Content = Strings.Inspector_Tab_Properties };
    private readonly Button _tabEvents = new() { Content = Strings.Inspector_Tab_Events };
    private readonly ToggleButton _byCategory = new() { Content = "▤", Width = 22, Height = 22 };
    private readonly ToggleButton _byName = new() { Content = "A↓", Width = 22, Height = 22 };
    private readonly TextBox _filter = new() { PlaceholderText = Strings.ToolWindow_FilterPlaceholder, Height = 26 };
    private readonly GridHost _grid = new() { Focusable = true };
    private readonly StackPanel _rows = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _emptySecondary = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _emptyPanel;
    private readonly TextBlock _descriptionTitle = new();
    private readonly TextBlock _descriptionText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Border _description;
    private readonly TextBox _editor = new() { BorderThickness = new Thickness(0), Padding = new Thickness(4, 0), MinHeight = 0, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Border _splitter;
    private readonly StackPanel _toggles;
    private readonly ListBox _list = new() { MaxHeight = 220, MinWidth = 120, Focusable = true };
    private readonly Popup _listPopup;
    private ObjectInspectorViewModel? _model;
    private Control? _currentRow;
    private bool _building;
    private string? _pressedValuePath;
    private bool _draggingDivider;
    private (double StartY, double StartHeight)? _splitterDrag;
    private ContextMenu? _contextMenu;

    public ObjectInspectorView()
    {
        AutomationProperties.SetAutomationId(_selector, Prefix + ".InstanceSelector");
        AutomationProperties.SetName(_selector, Strings.Cmd_view_objectInspector);
        AutomationProperties.SetAutomationId(_tabProperties, Prefix + ".Tab.Properties");
        AutomationProperties.SetAutomationId(_tabEvents, Prefix + ".Tab.Events");
        AutomationProperties.SetAutomationId(_byCategory, Prefix + ".ToolBar.objectInspector.toggleCategorized:category");
        AutomationProperties.SetAutomationId(_byName, Prefix + ".ToolBar.objectInspector.toggleCategorized:name");
        AutomationProperties.SetName(_byCategory, Strings.Inspector_ArrangeByCategory);
        AutomationProperties.SetName(_byName, Strings.Inspector_ArrangeByName);
        ToolTip.SetTip(_byCategory, Strings.Inspector_ArrangeByCategory);
        ToolTip.SetTip(_byName, Strings.Inspector_ArrangeByName);
        AutomationProperties.SetAutomationId(_filter, Prefix + ".Filter");
        AutomationProperties.SetAutomationId(_grid, Prefix + ".Grid");
        AutomationProperties.SetName(_grid, Strings.Cmd_view_objectInspector);
        AutomationProperties.SetAutomationId(_empty, Prefix + ".Empty");
        ToolWindowFrame.SetIsPrimary(_grid, true);

        foreach (var tab in (Button[])[_tabProperties, _tabEvents])
        {
            tab.Classes.Add("flat");
            tab.Padding = new Thickness(8, 0);
            tab.Height = 26;
            tab.Background = Brushes.Transparent;
            tab.BorderThickness = new Thickness(0, 0, 0, 2);
        }

        _tabProperties.Click += (_, _) => _model?.Page = InspectorPage.Properties;
        _tabEvents.Click += (_, _) => _model?.Page = InspectorPage.Events;
        _byCategory.Click += (_, _) => SetArrangement(byName: false);
        _byName.Click += (_, _) => SetArrangement(byName: true);
        _selector.SelectionChanged += (_, _) =>
        {
            if (!_building && _model is { } model && _selector.SelectedIndex >= 0)
            {
                model.SelectComponent(_selector.SelectedIndex);
            }
        };
        _filter.TextChanged += (_, _) =>
        {
            if (!_building && _model is { } model)
            {
                model.FilterText = _filter.Text ?? "";
            }
        };
        _filter.KeyDown += OnFilterKeyDown;

        _empty.Classes.Add("body");
        _emptySecondary.Classes.Add("caption");
        Bind(_empty, TextBlock.ForegroundProperty, "FxcTextTertiaryBrush");
        Bind(_emptySecondary, TextBlock.ForegroundProperty, "FxcTextTertiaryBrush");
        _emptyPanel = new StackPanel
        {
            Spacing = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(8),
            IsHitTestVisible = false, Children = { _empty, _emptySecondary },
        };

        _scroll = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowAutoHide = false }; // the scrollbar takes its own column: it never covers a row's … button
        _grid.Child = new Panel { Children = { _scroll, _emptyPanel } };
        Bind(_grid, Border.BackgroundProperty, "FxcBgPanelBrush");
        _grid.AddHandler(PointerPressedEvent, OnGridPointerPressed, RoutingStrategies.Tunnel);
        _grid.PointerMoved += OnGridPointerMoved;
        _grid.PointerReleased += (_, e) => EndDividerDrag(e.Pointer);
        _grid.PointerCaptureLost += (_, _) => _draggingDivider = false;
        _grid.ContextRequested += OnGridContextRequested;
        _grid.KeyDown += OnGridKeyDown;
        _grid.TextInput += OnGridTextInput;
        _grid.GotFocus += (_, _) => UpdateCurrentBackground();
        _grid.LostFocus += (_, _) => UpdateCurrentBackground();

        _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        _editor.TextChanged += (_, _) =>
        {
            if (!_building && _model is { IsEditing: true } model)
            {
                model.EditText = _editor.Text ?? "";
            }
        };
        _editor.LostFocus += (_, _) =>
        {
            if (!_building && _model is { IsEditing: true } model)
            {
                model.Commit(InspectorCommitCause.FocusLost);
            }
        };

        // DSG-OI-032: Alt+Down opens the row's value list; Enter or a click enters and commits the value, Escape closes it.
        _list.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        _list.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        var listFrame = new Border { BorderThickness = new Thickness(1), Child = _list };
        Bind(listFrame, Border.BorderBrushProperty, "FxcBorderStrongBrush");
        Bind(listFrame, Border.BackgroundProperty, "FxcBgRaisedBrush");
        _listPopup = new Popup
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = true, ShouldUseOverlayLayer = true, Child = listFrame,
        };
        ((Panel)_grid.Child).Children.Add(_listPopup);

        _descriptionTitle.Classes.Add("bodyStrong");
        _descriptionText.Classes.Add("body");
        Bind(_descriptionText, TextBlock.ForegroundProperty, "FxcTextSecondaryBrush");
        _description = new Border
        {
            MinHeight = MinDescriptionHeight, Height = 58, Padding = new Thickness(6, 4),
            Child = new StackPanel { Spacing = 2, Children = { _descriptionTitle, _descriptionText } },
        };
        AutomationProperties.SetAutomationId(_description, Prefix + ".Description");

        // UI-DSG-100: the splitter above the description pane (5 DIP to drag, a 1 DIP line).
        var splitterLine = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center };
        Bind(splitterLine, Border.BackgroundProperty, "FxcBorderSubtleBrush");
        _splitter = new Border { Height = SplitterHeight, Background = Brushes.Transparent, Child = splitterLine, Cursor = new Cursor(StandardCursorType.SizeNorthSouth) };
        AutomationProperties.SetAutomationId(_splitter, Prefix + ".Splitter");
        _splitter.PointerPressed += OnSplitterPressed;
        _splitter.PointerMoved += OnSplitterMoved;
        _splitter.PointerReleased += (_, e) => EndSplitterDrag(e.Pointer);
        _splitter.PointerCaptureLost += (_, _) => _splitterDrag = null;

        var tabs = new DockPanel { Height = 26, LastChildFill = false };
        _toggles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { _byCategory, _byName } };
        DockPanel.SetDock(_toggles, Dock.Right);
        tabs.Children.Add(_toggles);
        tabs.Children.Add(_tabProperties);
        tabs.Children.Add(_tabEvents);

        var top = new StackPanel
        {
            Spacing = 4, Margin = new Thickness(4, 4, 4, 4),
            Children = { _selector, tabs, _filter },
        };
        var layout = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_description, Dock.Bottom);
        DockPanel.SetDock(_splitter, Dock.Bottom);
        layout.Children.Add(top);
        layout.Children.Add(_description);
        layout.Children.Add(_splitter);
        layout.Children.Add(_grid);
        Content = layout;
        SizeChanged += (_, e) => UpdateNarrow(e.NewSize.Width);
    }

    /// <summary>UI-DSG-113: below 260 DIP the arrangement buttons move into the caption's menu.</summary>
    private void UpdateNarrow(double width)
    {
        if (_model is { } model && width > 0)
        {
            model.IsNarrow = width < ObjectInspectorViewModel.NarrowWidth;
        }

        _toggles.IsVisible = _model is not { IsNarrow: true };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelPropertyChanged;
        }

        _model = DataContext as ObjectInspectorViewModel;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelPropertyChanged;
        }

        UpdateNarrow(Bounds.Width);
        Rebuild();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ObjectInspectorViewModel.EditText):
                if (_model is { IsEditing: true } model && !string.Equals(_editor.Text, model.EditText, StringComparison.Ordinal))
                {
                    RunBuilding(() => _editor.Text = model.EditText);
                }

                break;
            case nameof(ObjectInspectorViewModel.IsEditing):
                if (_model is { IsEditing: true } editing)
                {
                    RunBuilding(() => _editor.Text = editing.EditText);
                }

                Rebuild();
                break;
            case nameof(ObjectInspectorViewModel.Rows) or nameof(ObjectInspectorViewModel.EventRows) or nameof(ObjectInspectorViewModel.CurrentPath)
                or nameof(ObjectInspectorViewModel.CurrentEvent) or nameof(ObjectInspectorViewModel.EditError) or nameof(ObjectInspectorViewModel.Page)
                or nameof(ObjectInspectorViewModel.IsEmpty) or nameof(ObjectInspectorViewModel.IsArrangedByName)
                or nameof(ObjectInspectorViewModel.ShowDescription):
                Rebuild();
                break;
            case nameof(ObjectInspectorViewModel.DescriptionTitle) or nameof(ObjectInspectorViewModel.DescriptionText):
                UpdateDescription();
                break;
            case nameof(ObjectInspectorViewModel.NameColumnRatio):
                ApplyRatio();
                break;
            case nameof(ObjectInspectorViewModel.IsNarrow):
                UpdateNarrow(Bounds.Width);
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

    /// <summary>Re-creates the rows and the header state from the view-model (rows are re-read on every change, DSG-OI-094).</summary>
    private void Rebuild()
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
            var model = _model;
            var empty = model is null || model.IsEmpty;
            _selector.IsEnabled = !empty;
            _filter.IsEnabled = !empty;
            var items = model?.ComponentItems ?? [];
            if (_selector.ItemsSource is not IReadOnlyList<string> shown || !shown.SequenceEqual(items))
            {
                _selector.ItemsSource = items;
            }

            if (_selector.SelectedIndex != (model?.SelectedComponentIndex ?? -1))
            {
                _selector.SelectedIndex = model?.SelectedComponentIndex ?? -1;
            }

            _selector.PlaceholderText = model?.MultiSelectionText;
            var events = model?.Page == InspectorPage.Events;
            _tabProperties.Classes.Set("active", !events);
            _tabEvents.Classes.Set("active", events);
            StyleTab(_tabProperties, !events);
            StyleTab(_tabEvents, events);
            _byCategory.IsChecked = model is { IsArrangedByName: false };
            _byName.IsChecked = model is { IsArrangedByName: true };
            _description.IsVisible = model?.ShowDescription ?? true;
            _splitter.IsVisible = _description.IsVisible;

            var emptyText = model?.EmptyStateText ?? (model is null ? Strings.Msg_Inspector_NoSelection : null);
            _emptyPanel.IsVisible = emptyText is not null;
            _empty.Text = emptyText ?? "";
            _emptySecondary.Text = model?.EmptyStateSecondary ?? "";
            _emptySecondary.IsVisible = !string.IsNullOrEmpty(_emptySecondary.Text);
            if (model is not null && !model.IsEmpty)
            {
                if (events)
                {
                    foreach (var row in model.EventRows)
                    {
                        AddRow(model, row.Name, row.Name, 0, row.Value, isBold: row.Value.Length > 0, isReadOnly: model.IsEventsReadOnly,
                            isCurrent: string.Equals(row.Name, model.CurrentEvent, StringComparison.OrdinalIgnoreCase), null);
                    }
                }
                else
                {
                    foreach (var row in model.Rows)
                    {
                        if (row.Kind == PropertyRowKind.Category)
                        {
                            AddCategory(row);
                        }
                        else
                        {
                            AddRow(model, row.Path, row.Name, row.Depth, row.Display, row.IsBold, row.IsReadOnly,
                                string.Equals(row.Path, model.CurrentPath, StringComparison.OrdinalIgnoreCase), row);
                        }
                    }
                }
            }

            UpdateDescription();
        });

        UpdateCurrentBackground();
        if (_model is { IsEditing: true } && _editor.Parent is not null)
        {
            if (editorHadFocus || _grid.IsKeyboardFocusWithin || _grid.IsFocused || _currentRow is not null)
            {
                FocusEditor(selectAll: false);
            }
        }
        else if (editorHadFocus)
        {
            _grid.Focus();
        }

        if (_currentRow is { } current)
        {
            Dispatcher.UIThread.Post(() => current.BringIntoView(), DispatcherPriority.Background);
        }
    }

    private void StyleTab(Button tab, bool active)
    {
        tab.Classes.Set("bodyStrong", active);
        tab.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
        if (active)
        {
            Bind(tab, Button.BorderBrushProperty, "FxcTabActiveIndicatorBrush");
        }
        else
        {
            tab.BorderBrush = Brushes.Transparent;
        }
    }

    private void AddCategory(PropertyRow row)
    {
        var collapsed = !row.IsExpanded;
        var text = new TextBlock { Text = (collapsed ? "▸ " : "▾ ") + row.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
        text.Classes.Add("bodyStrong");
        var border = new Border { Height = RowHeight, Child = text, Tag = new CategoryTag(row.Path) };
        AutomationProperties.SetAutomationId(border, Prefix + ".Category." + row.Path);
        Bind(border, Border.BackgroundProperty, "FxcBgHeaderBrush");
        _rows.Children.Add(border);
    }

    private void AddRow(ObjectInspectorViewModel model, string path, string name, int depth, string display, bool isBold, bool isReadOnly, bool isCurrent, PropertyRow? row)
    {
        var id = Prefix + ".Row." + path;
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
        var editing = isCurrent && model.IsEditing;
        if (editing)
        {
            AutomationProperties.SetAutomationId(_editor, id + ".Editor");
            _editor.Classes.Set("invalid", model.EditError is not null);
            if (model.EditError is not null)
            {
                _editor.BorderThickness = new Thickness(1);
                Bind(_editor, TextBox.BorderBrushProperty, "FxcStatusErrorBrush");
            }
            else
            {
                _editor.BorderThickness = new Thickness(0);
            }

            valueContent = _editor;
        }
        else
        {
            var valueText = new TextBlock { Text = display, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            AutomationProperties.SetAutomationId(valueText, id + ".Value");
            valueText.Classes.Add(isBold ? "bodyStrong" : "body");
            valueText.FontWeight = isBold ? FontWeight.SemiBold : FontWeight.Normal;
            Bind(valueText, TextBlock.ForegroundProperty, isReadOnly ? "FxcTextSecondaryBrush" : "FxcTextPrimaryBrush");
            var cell = new DockPanel { LastChildFill = true };
            if (row is { Info.Type: var type } && type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) || row is { SetMember: not null })
            {
                // UI-DSG-106: a 14 × 14 check box before True/False (the row toggles, the box only shows the state).
                var box = new CheckBox
                {
                    IsChecked = display == "True" ? true : display == "False" ? false : null, IsThreeState = true, IsHitTestVisible = false,
                    Focusable = false, Width = 14, Height = 14, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                AutomationProperties.SetAccessibilityView(box, AccessibilityView.Raw);
                DockPanel.SetDock(box, Dock.Left);
                cell.Children.Add(box);
            }

            if (row is not null && ObjectInspectorViewModel.HasEditorButton(row) && !isReadOnly)
            {
                var button = new Button { Content = "…", Width = 22, MinWidth = 0, Padding = new Thickness(0), Focusable = false, VerticalAlignment = VerticalAlignment.Stretch };
                if (ObjectInspectorViewModel.IsCollectionRow(row))
                {
                    button.IsEnabled = model.Selection is [{ IsPreserved: false }]; // §5.1: not for multi-selection or preserved components
                }

                AutomationProperties.SetAutomationId(button, id + ".Button");
                AutomationProperties.SetName(button, name);
                button.Click += (_, _) =>
                {
                    model.SetCurrent(path);
                    _ = model.EditInDialogAsync();
                };
                DockPanel.SetDock(button, Dock.Right);
                cell.Children.Add(button);
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
        var ratio = model.NameColumnRatio;
        var grid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(ratio, GridUnitType.Star),
                new ColumnDefinition(DividerWidth, GridUnitType.Pixel),
                new ColumnDefinition(1 - ratio, GridUnitType.Star),
            ],
        };
        grid.Children.Add(nameCell);
        Grid.SetColumn(divider, 1);
        grid.Children.Add(divider);
        Grid.SetColumn(valueCell, 2);
        grid.Children.Add(valueCell);

        var border = new Border { Height = RowHeight, Child = grid, Tag = new RowTag(path), BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent };
        Bind(border, Border.BorderBrushProperty, "FxcBorderSubtleBrush");
        AutomationProperties.SetAutomationId(border, id);
        AutomationProperties.SetName(border, name);
        if (isCurrent)
        {
            border.Classes.Add("current");
            _currentRow = border;
        }

        _rows.Children.Add(border);
        if (isCurrent && model.EditError is { } error)
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

    private void UpdateCurrentBackground()
    {
        if (_currentRow is Border border)
        {
            Bind(border, Border.BackgroundProperty, _grid.IsKeyboardFocusWithin ? "FxcBgSelectedBrush" : "FxcBgSelectedInactiveBrush");
        }
    }

    private void UpdateDescription()
    {
        _descriptionTitle.Text = _model?.DescriptionTitle ?? "";
        _descriptionText.Text = _model?.DescriptionText ?? "";
    }

    private void SetArrangement(bool byName)
    {
        if (_model is { } model && model.IsArrangedByName != byName)
        {
            model.ToggleArrangement();
        }
        else
        {
            Rebuild(); // the toggle button keeps its checked state
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
        if (_model is { } model && model.BeginEdit(entry))
        {
            FocusEditor(selectAll: entry is null);
        }
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is not { } model || e.Handled || _editor.IsFocused)
        {
            return;
        }

        var handled = true;
        switch (e.Key, e.KeyModifiers)
        {
            case (Key.Up, KeyModifiers.None):
                model.MoveCurrent(-1);
                break;
            case (Key.Down, KeyModifiers.None):
                model.MoveCurrent(1);
                break;
            case (Key.PageUp, KeyModifiers.None):
                model.MoveCurrent(-PageRows());
                break;
            case (Key.PageDown, KeyModifiers.None):
                model.MoveCurrent(PageRows());
                break;
            case (Key.Home, KeyModifiers.None):
                model.MoveCurrentToEdge(end: false);
                break;
            case (Key.End, KeyModifiers.None):
                model.MoveCurrentToEdge(end: true);
                break;
            case (Key.Left, KeyModifiers.None):
                model.ExpandCurrent(expand: false);
                break;
            case (Key.Right, KeyModifiers.None):
                model.ExpandCurrent(expand: true);
                break;
            case (Key.Enter, KeyModifiers.None):
                _ = ActivateAsync(model);
                break;
            case (Key.F2, KeyModifiers.None):
                BeginEdit();
                break;
            case (Key.Down, KeyModifiers.Alt):
                OpenValueList(model);
                break;
            case (Key.F10, KeyModifiers.Shift):
            case (Key.Apps, KeyModifiers.None):
                OpenContextMenu(null);
                break;
            case (Key.Space, KeyModifiers.None):
                if (model.Page == InspectorPage.Properties)
                {
                    model.ToggleBoolean();
                }

                break;
            case (Key.Escape, KeyModifiers.None):
                model.ReturnFocusToDocument(); // UI-A11Y-004
                break;
            default:
                handled = false;
                break;
        }

        e.Handled = handled;
    }

    private async Task ActivateAsync(ObjectInspectorViewModel model)
    {
        await model.ActivateCurrentAsync();
        if (model.IsEditing)
        {
            FocusEditor(selectAll: true);
        }
    }

    private int PageRows() => Math.Max(1, (int)(_scroll.Viewport.Height / RowHeight) - 1);

    /// <summary>DSG-OI-032: a printable character on a text-editable row replaces the value with that character.</summary>
    private void OnGridTextInput(object? sender, TextInputEventArgs e)
    {
        if (_editor.IsFocused || string.IsNullOrWhiteSpace(e.Text) || e.Text.Any(char.IsControl))
        {
            return;
        }

        e.Handled = true;
        BeginEdit(e.Text);
    }

    /// <summary>DSG-OI-033: Enter, Tab and Up/Down commit (and move); Escape cancels.</summary>
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is not { IsEditing: true } model)
        {
            return;
        }

        switch (e.Key, e.KeyModifiers)
        {
            case (Key.Enter, KeyModifiers.None):
                e.Handled = true;
                if (model.Commit())
                {
                    _grid.Focus();
                }

                break;
            case (Key.Escape, KeyModifiers.None):
                e.Handled = true;
                model.Cancel();
                _grid.Focus();
                break;
            case (Key.Tab, KeyModifiers.None or KeyModifiers.Shift):
            case (Key.Up, KeyModifiers.None):
            case (Key.Down, KeyModifiers.None):
                e.Handled = true;
                var delta = e.Key == Key.Up || e.KeyModifiers == KeyModifiers.Shift ? -1 : 1;
                if (model.Commit())
                {
                    _grid.Focus();
                    model.MoveCurrent(delta);
                }

                break;
        }
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || _model is not { } model)
        {
            return;
        }

        e.Handled = true;
        if (string.IsNullOrEmpty(_filter.Text))
        {
            model.ReturnFocusToDocument(); // DSG-OI-021: the second Escape
            return;
        }

        RunBuilding(() => _filter.Text = "");
        model.ApplyFilterNow("");
    }

    /// <summary>
    /// A press in the grid (tunnel, so it is seen before the editor takes it): a click makes the row current, a click on
    /// the current row's value enters edit mode, a double-click on the value cycles enums/Booleans, opens the strings
    /// editor, or creates/shows an event handler (DSG-OI-032, 083); expanders and category rows toggle.
    /// </summary>
    private void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_model is not { } model)
        {
            return;
        }

        var properties = e.GetCurrentPoint(_grid).Properties;
        if (properties.IsRightButtonPressed)
        {
            RightPress(model, e);
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
            model.Commit(InspectorCommitCause.FocusLost);
            _draggingDivider = true;
            e.Pointer.Capture(_grid);
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
        for (var v = source; v is not null && v != _grid; v = v.GetVisualParent())
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
            model.Cancel();
            model.SetCurrent(value);
            _grid.Focus();
            DoubleClickValue(model);
            return;
        }

        _pressedValuePath = value;
        if (inEditor)
        {
            return; // caret placement in the editor
        }

        e.Handled = true;
        if (model.IsEditing)
        {
            model.Commit(InspectorCommitCause.FocusLost);
        }

        if (category is not null)
        {
            _grid.Focus();
            model.ToggleCategory(category);
            return;
        }

        if (row is null)
        {
            _grid.Focus();
            return;
        }

        var current = model.Page == InspectorPage.Events ? model.CurrentEvent : model.CurrentPath;
        var wasCurrent = string.Equals(current, row, StringComparison.OrdinalIgnoreCase);
        model.SetCurrent(row);
        _grid.Focus();
        if (expander is not null && model.Rows.FirstOrDefault(r => r.Path == expander) is { IsExpandable: true } expandable)
        {
            if (expandable.IsExpanded)
            {
                model.Collapse(expander);
            }
            else
            {
                model.Expand(expander);
            }
        }
        else if (wasCurrent && value is not null && model.Page == InspectorPage.Properties
            && model.Rows.FirstOrDefault(r => r.Path == value) is { } clicked && !ObjectInspectorViewModel.HasEditorButton(clicked))
        {
            BeginEdit();
        }
    }

    private void DoubleClickValue(ObjectInspectorViewModel model)
    {
        if (model.Page == InspectorPage.Events)
        {
            if (model.CurrentEvent is { } name)
            {
                model.ActivateEvent(name);
            }

            return;
        }

        if (model.Rows.FirstOrDefault(r => string.Equals(r.Path, model.CurrentPath, StringComparison.OrdinalIgnoreCase)) is { } row
            && ObjectInspectorViewModel.HasEditorButton(row))
        {
            _ = model.EditInDialogAsync();
            return;
        }

        if (!model.CycleValue())
        {
            BeginEdit();
        }
    }

    /// <summary>The path or event name of the row under a press; null outside rows.</summary>
    private string? RowAt(Visual? source)
    {
        for (var v = source; v is not null && v != _grid; v = v.GetVisualParent())
        {
            if ((v as Control)?.Tag is RowTag t)
            {
                return t.Path;
            }
        }

        return null;
    }

    /// <summary>DSG-OI-039: a right press makes the row under it current (the menu opens on release).</summary>
    private void RightPress(ObjectInspectorViewModel model, PointerPressedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) == _editor)
        {
            return; // the editor's own menu
        }

        if (model.IsEditing)
        {
            model.Commit(InspectorCommitCause.FocusLost);
        }

        if (RowAt(e.Source as Visual) is { } row)
        {
            model.SetCurrent(row);
        }

        _grid.Focus();
    }

    private void OnGridContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) == _editor)
        {
            return;
        }

        e.Handled = true;
        OpenContextMenu(e.TryGetPosition(_grid, out var point) ? point : null);
    }

    /// <summary>DSG-OI-039: the row context menu at the pointer, or under the current row (Shift+F10, Apps).</summary>
    private void OpenContextMenu(Point? at)
    {
        if (_model is not { IsEmpty: false } model || _contextMenu is { IsOpen: true })
        {
            return;
        }

        var menu = MenuBarBuilder.CreateContextMenu(model.BuildContextMenu());
        AutomationProperties.SetAutomationId(menu, Prefix + ".ContextMenu");
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
            menu.PlacementTarget = _currentRow ?? _grid;
        }

        _contextMenu = menu;
        _grid.ContextMenu = menu;
        menu.Open(_grid);
    }

    /// <summary>DSG-OI-032: Alt+Down opens the drop-down list of a row that has one; other text rows enter edit mode.</summary>
    private void OpenValueList(ObjectInspectorViewModel model)
    {
        var values = model.CurrentValueList();
        if (values.Count == 0 || _currentRow is null)
        {
            BeginEdit();
            return;
        }

        var current = model.CurrentValueText;
        _list.ItemsSource = values;
        _list.SelectedIndex = Math.Max(0, values.ToList().FindIndex(v => string.Equals(v, current, StringComparison.OrdinalIgnoreCase)));
        AutomationProperties.SetAutomationId(_list, AutomationProperties.GetAutomationId(_currentRow) + ".List");
        _listPopup.PlacementTarget = _currentRow;
        _listPopup.Width = Math.Max(_list.MinWidth, _currentRow.Bounds.Width);
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
        _grid.Focus();
        if (value is not null && _model is { } model)
        {
            model.ChooseValue(value);
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
                _grid.Focus();
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
    private void OnGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_draggingDivider || _model is not { } model)
        {
            return;
        }

        var width = _rows.Bounds.Width;
        var x = e.GetPosition(_rows).X;
        var min = ObjectInspectorViewModel.MinColumnWidth;
        model.NameColumnRatio = width > 2 * min + DividerWidth ? Math.Clamp(x, min, width - min - DividerWidth) / (width - DividerWidth) : 0.5;
    }

    private void EndDividerDrag(IPointer pointer)
    {
        if (_draggingDivider)
        {
            _draggingDivider = false;
            pointer.Capture(null);
        }
    }

    /// <summary>Applies <see cref="ObjectInspectorViewModel.NameColumnRatio"/> to the shown rows (no rebuild while dragging).</summary>
    private void ApplyRatio()
    {
        var ratio = _model?.NameColumnRatio ?? ObjectInspectorViewModel.DefaultNameColumnRatio;
        foreach (var row in _rows.Children.OfType<Border>())
        {
            if (row.Child is Grid { ColumnDefinitions.Count: 3 } grid)
            {
                grid.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
                grid.ColumnDefinitions[2].Width = new GridLength(1 - ratio, GridUnitType.Star);
            }
        }
    }

    private void OnSplitterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_splitter).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        _splitterDrag = (e.GetPosition(this).Y, _description.Bounds.Height);
        e.Pointer.Capture(_splitter);
    }

    /// <summary>UI-DSG-100: the splitter moves the description pane's top edge (pane at least 44, grid at least 44 DIP).</summary>
    private void OnSplitterMoved(object? sender, PointerEventArgs e)
    {
        if (_splitterDrag is not { } drag)
        {
            return;
        }

        var max = Math.Max(MinDescriptionHeight, _description.Bounds.Height + _grid.Bounds.Height - MinGridHeight);
        _description.Height = Math.Clamp(drag.StartHeight - (e.GetPosition(this).Y - drag.StartY), MinDescriptionHeight, max);
    }

    private void EndSplitterDrag(IPointer pointer)
    {
        if (_splitterDrag is not null)
        {
            _splitterDrag = null;
            pointer.Capture(null);
        }
    }

    private static void Bind(Control control, AvaloniaProperty property, string resource) =>
        control.Bind(property, control.GetResourceObservable(resource));

    private sealed record RowTag(string Path);

    private sealed record ValueTag(string Path);

    private sealed record ExpanderTag(string Path);

    private sealed record CategoryTag(string Name);

    private sealed record DividerTag;

    /// <summary>The focusable grid area (the tool window's primary control, UI-TW-007).</summary>
    private sealed class GridHost : Border
    {
    }
}
