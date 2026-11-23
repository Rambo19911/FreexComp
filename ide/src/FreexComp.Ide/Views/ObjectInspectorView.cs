using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Designer.Inspector;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// Content of the Object Inspector tool window (ui/designer.md §11, object-inspector.md; D-081): instance selector, page
/// tabs with the arrangement toggles, filter, the property grid (the shared <see cref="PropertyGridView"/> on
/// <see cref="ObjectInspectorViewModel.Grid"/>, D-110; the tool window's primary control) and the description pane. Built
/// in code; the logic is in <see cref="ObjectInspectorViewModel"/>.
/// </summary>
public sealed class ObjectInspectorView : UserControl
{
    private const string Prefix = "Tool.ObjectInspector";
    private const double SplitterHeight = 5; // UI-DSG-100 splitter: 5 hit / 1 visible
    private const double MinDescriptionHeight = 44; // UI-DSG-110
    private const double MinGridHeight = 44;

    private readonly ComboBox _selector = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 26 };
    private readonly Button _tabProperties = new() { Content = Strings.Inspector_Tab_Properties };
    private readonly Button _tabEvents = new() { Content = Strings.Inspector_Tab_Events };
    private readonly ToggleButton _byCategory = new() { Content = "▤", Width = 22, Height = 22 };
    private readonly ToggleButton _byName = new() { Content = "A↓", Width = 22, Height = 22 };
    private readonly TextBox _filter = new() { PlaceholderText = Strings.ToolWindow_FilterPlaceholder, Height = 26 };
    private readonly PropertyGridView _grid = new(Prefix + ".Grid", Prefix + ".Row.", Prefix + ".Category.");
    private readonly TextBlock _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _emptySecondary = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _emptyPanel;
    private readonly TextBlock _descriptionTitle = new();
    private readonly TextBlock _descriptionText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Border _description;
    private readonly Border _splitter;
    private readonly StackPanel _toggles;
    private ObjectInspectorViewModel? _model;
    private bool _building;
    private (double StartY, double StartHeight)? _splitterDrag;

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
        AutomationProperties.SetName(_grid, Strings.Cmd_view_objectInspector);
        AutomationProperties.SetAutomationId(_empty, Prefix + ".Empty");
        ToolWindowFrame.SetIsPrimary(_grid, true);
        _grid.IsBlank = () => _model is not { IsEmpty: false };
        _grid.Escape = () => _model?.ReturnFocusToDocument(); // UI-A11Y-004
        _grid.ContextMenuItems = () => _model?.BuildContextMenu() ?? [];
        _grid.ContextMenuId = Prefix + ".ContextMenu";
        _grid.RatioDragged += ratio => _model?.NameColumnRatio = ratio;

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
        _grid.AddOverlay(_emptyPanel);

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

        _grid.NameColumnRatio = _model?.NameColumnRatio ?? ObjectInspectorViewModel.DefaultNameColumnRatio;
        _grid.Grid = _model?.Grid;
        UpdateNarrow(Bounds.Width);
        Rebuild();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ObjectInspectorViewModel.Rows) or nameof(ObjectInspectorViewModel.Page)
                or nameof(ObjectInspectorViewModel.IsEmpty) or nameof(ObjectInspectorViewModel.IsArrangedByName)
                or nameof(ObjectInspectorViewModel.ShowDescription):
                Rebuild();
                break;
            case nameof(ObjectInspectorViewModel.DescriptionTitle) or nameof(ObjectInspectorViewModel.DescriptionText):
                UpdateDescription();
                break;
            case nameof(ObjectInspectorViewModel.NameColumnRatio):
                _grid.NameColumnRatio = _model?.NameColumnRatio ?? ObjectInspectorViewModel.DefaultNameColumnRatio;
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

    /// <summary>Re-reads the header state and the empty state, then the grid's rows.</summary>
    private void Rebuild()
    {
        RunBuilding(() =>
        {
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
            UpdateDescription();
        });

        _grid.Rebuild();
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
}
