using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// The toolbar row (toolbars.md §1): the visible toolbars side by side, separated by a vertical line; toolbars that do
/// not fit move, whole and from the right, into the <c>»</c> overflow button's popup (UI-TB-006). The row collapses
/// when no toolbar is visible (UI-TB-004). Generated from the toolbar models; the decisions are in Core.
/// </summary>
public sealed class ToolBarRow : DockPanel
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _overflowPanel = new() { Orientation = Orientation.Vertical };
    private readonly Button _overflow;
    private readonly List<(ToolBarModel Model, StackPanel View)> _toolbars = [];
    private readonly Dictionary<StackPanel, double> _widths = [];
    private int _fitting = -1;

    public ToolBarRow()
    {
        _overflow = new Button
        {
            Content = new TextBlock { Text = Strings.ToolBar_Overflow, VerticalAlignment = VerticalAlignment.Center },
            Flyout = new Flyout { Content = _overflowPanel },
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(_overflow, "ToolBar.Overflow");
        SetDock(_overflow, Avalonia.Controls.Dock.Right);
        Children.Add(_overflow);
        Children.Add(_row);
        SizeChanged += (_, _) => Relayout();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, (_, _) => Dispatcher.UIThread.Post(LeaveIfFocusOutside), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Escape in the toolbar row (UI-TB-008): focus goes back to the document.</summary>
    public event EventHandler? EscapeRequested;

    /// <summary>
    /// UI-TB-008 keyboard entry (from the menu bar with Ctrl+Tab): the buttons become focusable while focus is in the
    /// row, the first enabled one gets focus. A mouse click never focuses them (I-153).
    /// </summary>
    public bool FocusFirstButton()
    {
        var buttons = KeyboardButtons();
        if (buttons.Count == 0)
        {
            return false;
        }

        foreach (var button in AllButtons())
        {
            button.Focusable = true;
        }

        return buttons[0].Focus(NavigationMethod.Tab);
    }

    /// <summary>Enabled buttons of the toolbars in the row, left to right (disabled ones cannot take focus).</summary>
    private List<Button> KeyboardButtons() =>
        [.. InRow.SelectMany(t => ((Panel)t).Children.OfType<Button>()).Where(b => b.IsEffectivelyEnabled && b.IsEffectivelyVisible)];

    private IEnumerable<Button> AllButtons() => _toolbars.SelectMany(t => t.View.Children.OfType<Button>());

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Button current)
        {
            return;
        }

        var buttons = KeyboardButtons();
        var index = buttons.IndexOf(current);
        switch (e.Key)
        {
            case Key.Left when index > 0:
                buttons[index - 1].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
            case Key.Right when index >= 0 && index < buttons.Count - 1:
                buttons[index + 1].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
            case Key.Escape:
                EscapeRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private void LeaveIfFocusOutside()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is null || !this.IsVisualAncestorOf(focused))
        {
            foreach (var button in AllButtons())
            {
                button.Focusable = false;
            }
        }
    }

    /// <summary>Toolbars currently in the row, left to right.</summary>
    public IReadOnlyList<Control> InRow => [.. _row.Children.OfType<StackPanel>()];

    /// <summary>Toolbars currently moved into the overflow popup, in row order.</summary>
    public IReadOnlyList<Control> InOverflow => [.. _overflowPanel.Children.OfType<StackPanel>()];

    public void Populate(IReadOnlyList<ToolBarModel> toolbars)
    {
        foreach (var (model, _) in _toolbars)
        {
            model.PropertyChanged -= OnToolBarChanged;
        }

        _toolbars.Clear();
        _widths.Clear();
        foreach (var model in toolbars)
        {
            _toolbars.Add((model, CreateToolBar(model)));
            model.PropertyChanged += OnToolBarChanged;
        }

        Height = Metric("FxcMetricToolbarHeight");
        _fitting = -1;
        Relayout();
    }

    private void OnToolBarChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolBarModel.IsVisible))
        {
            _fitting = -1;
            Relayout();
        }
    }

    private void Relayout()
    {
        var visible = _toolbars.Where(t => t.Model.IsVisible).Select(t => t.View).ToList();
        IsVisible = visible.Count > 0;
        var available = Bounds.Width > 0 ? Bounds.Width : double.PositiveInfinity;
        var fitting = Core.Menus.ToolBarRowModel.FittingCount([.. visible.Select(MeasuredWidth)], SeparatorWidth, available, OverflowWidth());
        if (fitting == _fitting && visible.Count == InRow.Count + InOverflow.Count)
        {
            return;
        }

        _fitting = fitting;
        _row.Children.Clear();
        _overflowPanel.Children.Clear();
        for (var i = 0; i < visible.Count; i++)
        {
            if (i < fitting)
            {
                if (i > 0)
                {
                    _row.Children.Add(Line(Metric("FxcSpace4")));
                }

                _row.Children.Add(visible[i]);
            }
            else
            {
                _overflowPanel.Children.Add(visible[i]);
            }
        }

        _overflow.IsVisible = fitting < visible.Count;
    }

    /// <summary>Toolbar widths never change with state (UI-TB-010), so each is measured once.</summary>
    private double MeasuredWidth(StackPanel toolbar)
    {
        if (!_widths.TryGetValue(toolbar, out var width))
        {
            toolbar.Measure(Size.Infinity);
            width = toolbar.DesiredSize.Width;
            _widths[toolbar] = width;
        }

        return width;
    }

    private double SeparatorWidth => 1 + (2 * Metric("FxcSpace4"));

    private double OverflowWidth()
    {
        _overflow.Measure(Size.Infinity);
        return Math.Max(_overflow.DesiredSize.Width, Metric("FxcMetricToolbarButtonSize"));
    }

    private StackPanel CreateToolBar(ToolBarModel model)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(bar, "ToolBar." + model.Name);
        foreach (var item in model.Items)
        {
            bar.Children.Add(item switch
            {
                ToolBarButtonModel b => CreateButton(b),
                ToolBarComboModel c => CreateCombo(model.Name, c),
                _ => Line((Metric("FxcMetricToolbarGroupGap") - 1) / 2),
            });
        }

        return bar;
    }

    private Control CreateButton(ToolBarButtonModel model)
    {
        var size = Metric("FxcMetricToolbarButtonSize");
        var iconSize = Metric("FxcMetricToolbarIconSize");
        Button button = model.IsToggle ? new ToggleButton() : new Button();
        button.Width = size;
        button.Height = size;
        button.Padding = new Thickness(0);
        button.Background = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.Focusable = false; // a click leaves focus where it was, so edit.* act on it (I-153); keyboard entry (UI-TB-008) is I-024
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Content = new IdeIcon { IconName = model.IconName, Width = iconSize, Height = iconSize };
        button.Command = model.Command;
        AutomationProperties.SetAutomationId(button, AutomationIds.ToolBarButton(model.Command.Id));
        button.Bind(AutomationProperties.NameProperty, new Binding(nameof(ToolBarButtonModel.Title)) { Source = model, Mode = BindingMode.OneWay });
        button.Bind(ToolTip.TipProperty, new Binding(nameof(ToolBarButtonModel.Tooltip)) { Source = model, Mode = BindingMode.OneWay });
        if (button is ToggleButton toggle)
        {
            toggle.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(model.Command.IsChecked)) { Source = model.Command, Mode = BindingMode.OneWay });
        }

        return button;
    }

    private Control CreateCombo(string toolbar, ToolBarComboModel model)
    {
        var combo = new ComboBox { Width = model.Width, Height = Metric("FxcMetricToolbarComboHeight"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(combo, $"ToolBar.{toolbar}.{model.Name}");
        AutomationProperties.SetName(combo, model.Name);
        var updating = false;
        void Sync()
        {
            updating = true;
            combo.ItemsSource = model.Items;
            combo.SelectedItem = model.Selected;
            combo.IsEnabled = model.IsEnabled;
            updating = false;
        }

        model.PropertyChanged += (_, _) => Sync();
        combo.SelectionChanged += (_, _) =>
        {
            if (!updating && combo.SelectedItem is string item && item != model.Selected)
                model.Choose?.Invoke(item);
        };
        Sync();
        return combo;
    }

    /// <summary>A 1 px vertical line with <paramref name="margin"/> on both sides, full row height minus 8 (UI-TB-001).</summary>
    private Rectangle Line(double margin)
    {
        var line = new Rectangle { Width = 1, Height = Metric("FxcMetricToolbarHeight") - 8, Margin = new Thickness(margin, 0), VerticalAlignment = VerticalAlignment.Center };
        line.Bind(Shape.FillProperty, line.GetResourceObservable("FxcBorderDefaultBrush"));
        return line;
    }

    private double Metric(string key) =>
        this.TryFindResource(key, out var value) && value is double d ? d : Application.Current?.FindResource(key) as double? ?? 0;
}
