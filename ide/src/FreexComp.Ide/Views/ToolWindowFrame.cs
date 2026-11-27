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
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Input;

namespace FreexComp.Ide.Views;

/// <summary>
/// The shell part of one tool window (UI-TW-001): caption bar with title and Hide button (UI-TW-002), the optional
/// tool bar with filter box (UI-TW-008), then the content view of the view-model (view locator). The frame is the
/// tool window's command scope; <see cref="IsPrimaryProperty"/> marks the control that <c>view.*</c> focuses (UI-TW-007).
/// </summary>
public sealed class ToolWindowFrame : DockPanel
{
    public static readonly AttachedProperty<bool> IsPrimaryProperty =
        AvaloniaProperty.RegisterAttached<ToolWindowFrame, Control, bool>("IsPrimary");

    private readonly ToolWindowViewModel _model;
    private readonly Rectangle _focusLine = new() { Height = 2, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _title = new() { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl _content = new();
    private TextBox? _filter;
    private Button? _clearFilter;
    private Button? _menu;

    public ToolWindowFrame(ToolWindowViewModel model)
    {
        _model = model;
        DataContext = model;
        AutomationProperties.SetAutomationId(this, AutomationIds.Tool(model.Name));
        CommandRouting.SetScope(this, model.Scope);
        CommandRouting.SetToolWindow(this, model.Name);

        Add(CreateCaption(), Avalonia.Controls.Dock.Top);
        if (model.ToolBar.Count > 0 || model.HasFilter || model.ToolBarChoice is not null || model.ToolBarOptions is not null)
        {
            Add(CreateToolBar(), Avalonia.Controls.Dock.Top);
        }

        _content.Content = model;
        _content.Bind(ContentControl.BackgroundProperty, _content.GetResourceObservable("FxcBgPanelBrush"));
        Children.Add(_content);
        model.PropertyChanged += OnModelChanged;
        model.FocusRequested += (_, _) => Dispatcher.UIThread.Post(FocusPrimary, DispatcherPriority.Loaded);
        _content.Focusable = true; // focus target when the content has no focusable control
        KeyboardNavigation.SetIsTabStop(_content, false); // UI-A11Y-006: not a stop of its own
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle); // UI-A11Y-006: Tab stays inside; F6 leaves
        UpdateState();
    }

    public static bool GetIsPrimary(Control control) => control.GetValue(IsPrimaryProperty);

    public static void SetIsPrimary(Control control, bool value) => control.SetValue(IsPrimaryProperty, value);

    /// <summary>The marked primary control of the content, else its first focusable control.</summary>
    public void FocusPrimary()
    {
        var candidates = _content.GetVisualDescendants().OfType<InputElement>()
            .Where(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible).ToList();
        var target = candidates.FirstOrDefault(c => c is Control control && GetIsPrimary(control)) ?? candidates.FirstOrDefault() ?? _content;
        target.Focus(NavigationMethod.Tab);
    }

    /// <summary>UI-A11Y-004: an Escape that nothing inside the tool window used moves focus to the active document.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            _model.ReturnFocusToDocument();
            e.Handled = true;
        }
    }

    private void Add(Control child, Avalonia.Controls.Dock dock)
    {
        SetDock(child, dock);
        Children.Add(child);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ToolWindowViewModel.IsActive) or nameof(ToolWindowViewModel.FilterText) or nameof(ToolWindowViewModel.Caption))
        {
            UpdateState();
        }
        else if (e.PropertyName == nameof(ToolWindowViewModel.HasCaptionMenu))
        {
            _menu!.IsVisible = _model.HasCaptionMenu;
        }
    }

    /// <summary>UI-TW-002: focused → <c>text.primary</c> title and the 2 px accent line; else <c>text.secondary</c>.</summary>
    private void UpdateState()
    {
        _title.Text = _model.Caption;
        _title.Bind(TextBlock.ForegroundProperty, _title.GetResourceObservable(_model.IsActive ? "FxcTextPrimaryBrush" : "FxcTextSecondaryBrush"));
        _focusLine.IsVisible = _model.IsActive;
        _clearFilter?.IsVisible = _model.FilterText.Length > 0;
    }

    private Control CreateCaption()
    {
        AutomationProperties.SetAutomationId(_title, AutomationIds.Tool(_model.Name) + ".Caption");
        _title.Bind(MarginProperty, _title.GetResourceObservable("FxcSpace4Horizontal"));
        _focusLine.Bind(Shape.FillProperty, _focusLine.GetResourceObservable("FxcAccentDefaultBrush"));

        var hide = FlatButton(AutomationIds.Tool(_model.Name) + ".Hide", "minus", 20);
        hide.Bind(ToolTip.TipProperty, new Binding(nameof(ToolWindowViewModel.HideTooltip)) { Source = _model, Mode = BindingMode.OneWay });
        AutomationProperties.SetName(hide, Strings.ToolWindow_Hide);
        hide.Click += (_, _) => _model.Hide();
        hide.HorizontalAlignment = HorizontalAlignment.Right;
        hide.VerticalAlignment = VerticalAlignment.Center;

        // UI-DSG-113: the optional ⋯ menu left of Hide (the Object Inspector's arrangement when narrow).
        _menu = new Button
        {
            Content = "⋯", Width = 20, Height = 20, Padding = new Thickness(0), Focusable = false, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 22, 0),
            IsVisible = _model.HasCaptionMenu,
        };
        AutomationProperties.SetAutomationId(_menu, AutomationIds.Tool(_model.Name) + ".Menu");
        AutomationProperties.SetName(_menu, _model.Title);
        _menu.Click += (_, _) =>
        {
            _menu.ContextMenu = MenuBarBuilder.CreateContextMenu(_model.BuildCaptionMenu());
            _menu.ContextMenu.Open(_menu);
        };

        var grid = new Grid { Children = { _title, _menu, hide, _focusLine } };
        var caption = new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) };
        caption.Bind(HeightProperty, caption.GetResourceObservable("FxcMetricToolCaptionHeight"));
        caption.Bind(Border.BackgroundProperty, caption.GetResourceObservable("FxcBgHeaderBrush"));
        caption.Bind(Border.BorderBrushProperty, caption.GetResourceObservable("FxcBorderDefaultBrush"));
        return caption;
    }

    private Control CreateToolBar()
    {
        // UI-A11Y-006: buttons first in tab order, the filter at the right. I-215: the filter shrinks down to its
        // minimum width and never overlaps the buttons (ToolBarRowPanel).
        var row = new ToolBarRowPanel();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (_model.ToolBarChoice is { } choice)
        {
            AddChoice(buttons, choice);
        }

        foreach (var item in _model.ToolBar)
        {
            var button = FlatButton($"{AutomationIds.Tool(_model.Name)}.ToolBar.{item.Command.Id}", item.IconName, ToolWindowViewModel.ToolBarButtonSize, item.IsToggle);
            button.Command = item.Command;
            if (button is ToggleButton toggle)
            {
                // The Selected state (design-tokens.md §9) follows the command; a click runs the command only.
                toggle.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(item.Command.IsChecked)) { Source = item.Command, Mode = BindingMode.OneWay });
            }

            button.Focusable = true; // UI-A11Y-006: the tool bar is one tab stop, arrows move between its buttons
            button.Bind(AutomationProperties.NameProperty, new Binding(nameof(item.Title)) { Source = item, Mode = BindingMode.OneWay });
            button.Bind(ToolTip.TipProperty, new Binding(nameof(item.Tooltip)) { Source = item, Mode = BindingMode.OneWay });
            buttons.Children.Add(button);
        }

        KeyboardNavigation.SetTabNavigation(buttons, KeyboardNavigationMode.Once);
        buttons.AddHandler(KeyDownEvent, OnToolBarKeyDown, RoutingStrategies.Bubble);
        row.Children.Add(buttons);
        if (_model.HasFilter)
        {
            row.Children.Add(CreateFilter());
        }
        else if (_model.ToolBarOptions is { } options)
        {
            row.Children.Add(CreateOptions(options));
        }

        var bar = new Border { Child = row, Height = 28, BorderThickness = new Thickness(0, 0, 0, 1) };
        bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable("FxcBgPanelBrush"));
        bar.Bind(Border.BorderBrushProperty, bar.GetResourceObservable("FxcBorderSubtleBrush"));
        return bar;
    }

    /// <summary>The label and combo box of <see cref="ToolWindowViewModel.ToolBarChoice"/> (UI-TW-501), left of the buttons.</summary>
    private void AddChoice(StackPanel row, Core.ToolWindows.ToolWindowToolBarChoice choice)
    {
        var label = new TextBlock { Text = choice.Label, VerticalAlignment = VerticalAlignment.Center };
        label.Bind(MarginProperty, label.GetResourceObservable("FxcSpace2Horizontal"));
        var combo = new ComboBox
        {
            Width = choice.Width,
            VerticalAlignment = VerticalAlignment.Center,
            ItemsSource = choice.Items,
            Margin = new Thickness(0, 0, ToolWindowViewModel.ChoiceMargin, 0),
        };
        FitComboBox(combo, Metric("FxcMetricToolbarComboHeight")); // I-282: a combo box of a tool bar
        AutomationProperties.SetAutomationId(combo, $"{AutomationIds.Tool(_model.Name)}.{choice.Name}");
        AutomationProperties.SetLabeledBy(combo, label);
        combo.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(choice.SelectedIndex)) { Source = choice, Mode = BindingMode.TwoWay });
        row.Children.Add(label);
        row.Children.Add(combo);
    }

    /// <summary>
    /// <see cref="ToolWindowViewModel.ToolBarOptions"/> at the right end: the label, then one radio button per item
    /// (<c>Tool.&lt;Name&gt;.&lt;Choice&gt;.&lt;ItemId&gt;</c>), shown while the choice is visible (UI-TW-1622).
    /// </summary>
    private Control CreateOptions(Core.ToolWindows.ToolWindowToolBarChoice options)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = options.Width,
        };
        // design-tokens.md: a radio button is a 14 × 14 box in a 22 high row. The theme's glyph is 20 in a 32 high cell,
        // taller than the 28 tool bar; its template sets those sizes, which only local values override.
        var row = Metric("FxcMetricCheckBoxRowHeight");
        var box = Metric("FxcMetricCheckBoxSize");
        void Compact(object? sender, TemplateAppliedEventArgs e)
        {
            foreach (var name in (string[])["OuterEllipse", "CheckOuterEllipse"])
            {
                if (e.NameScope.Find<Ellipse>(name) is not { } ellipse)
                    continue;
                ellipse.Width = box;
                ellipse.Height = box;
                if (ellipse.Parent is Grid cell)
                {
                    cell.Height = row;
                    cell.VerticalAlignment = VerticalAlignment.Center;
                }
            }
        }

        var label = new TextBlock { Text = options.Label, VerticalAlignment = VerticalAlignment.Center };
        label.Bind(MarginProperty, label.GetResourceObservable("FxcSpace2Horizontal"));
        panel.Children.Add(label);
        var group = AutomationIds.Tool(_model.Name) + "." + options.Name;
        var buttons = new List<RadioButton>();
        for (var i = 0; i < options.Items.Count; i++)
        {
            var index = i;
            var button = new RadioButton
            {
                Content = options.Items[i].Label,
                GroupName = group,
                VerticalAlignment = VerticalAlignment.Center,
                Height = row,
                MinHeight = row,
                Margin = new Thickness(0, 0, ToolWindowViewModel.ChoiceMargin, 0),
                IsChecked = options.SelectedIndex == i,
            };
            AutomationProperties.SetAutomationId(button, group + "." + options.Items[i].Id);
            button.TemplateApplied += Compact;
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true)
                    options.SelectedIndex = index;
            };
            buttons.Add(button);
            panel.Children.Add(button);
        }

        void Sync()
        {
            panel.IsVisible = options.IsVisible;
            for (var i = 0; i < buttons.Count; i++)
                buttons[i].IsChecked = options.SelectedIndex == i;
        }

        options.PropertyChanged += (_, _) => Sync();
        Sync();
        return panel;
    }

    /// <summary>UI-A11Y-006: Left / Right move focus between the enabled tool-bar buttons.</summary>
    private static void OnToolBarKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not StackPanel bar || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Left or Key.Right))
            return;
        var buttons = bar.Children.OfType<Button>().Where(b => b.IsEffectivelyEnabled && b.IsVisible).ToList();
        var index = buttons.FindIndex(b => b.IsFocused);
        if (index < 0)
            return;
        var next = index + (e.Key == Key.Right ? 1 : -1);
        if (next >= 0 && next < buttons.Count)
            buttons[next].Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    /// <summary>Right-aligned, 26 high, 120…240 wide, placeholder "Filter" (or the window's own), clear button when non-empty (UI-TW-008).</summary>
    private Control CreateFilter()
    {
        _filter = new TextBox
        {
            MinWidth = ToolWindowViewModel.FilterMinWidth,
            MaxWidth = ToolWindowViewModel.FilterMaxWidth,
            PlaceholderText = _model.FilterPlaceholder,
            VerticalAlignment = VerticalAlignment.Center,
        };
        FitFilterBox(_filter);
        AutomationProperties.SetAutomationId(_filter, AutomationIds.Tool(_model.Name) + ".Filter");
        AutomationProperties.SetName(_filter, _model.FilterPlaceholder); // UI-A11Y-020
        _filter.Bind(TextBox.TextProperty, new Binding(nameof(ToolWindowViewModel.FilterText)) { Source = _model, Mode = BindingMode.TwoWay });
        _filter.AddHandler(KeyDownEvent, OnFilterKeyDown, RoutingStrategies.Tunnel);

        _clearFilter = FlatButton(AutomationIds.Tool(_model.Name) + ".Filter.Clear", "close", 20);
        AutomationProperties.SetName(_clearFilter, Strings.ToolWindow_ClearFilter);
        ToolTip.SetTip(_clearFilter, Strings.ToolWindow_ClearFilter);
        _clearFilter.Click += (_, _) => _model.FilterText = "";
        _filter.InnerRightContent = _clearFilter;
        _filter.Margin = new Thickness(ToolWindowViewModel.FilterMargin, 0);
        return _filter;
    }

    /// <summary>
    /// I-215: a filter / search box of a tool window is 26 high (UI-TW-008) and its text and placeholder fit the font's
    /// line height: no vertical padding (the 1 px border leaves 24 for the text line), text centred vertically.
    /// </summary>
    public static void FitFilterBox(TextBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        box.Height = 26;
        box.MinHeight = 26;
        box.Padding = new Thickness(6, 0, 6, 0);
        box.VerticalContentAlignment = VerticalAlignment.Center;
    }

    /// <summary>
    /// I-282, I-285: a combo box lower than the theme's (32) — one of a tool bar (24) or the 26 of design-tokens.md §8.
    /// The height set alone does nothing: the theme's minimum height wins, so both are set. The theme's vertical padding
    /// (5 + 7) is that of its 32-high combo box and leaves less than a text line here, which cut the selected item: no
    /// vertical padding, text centred vertically, as <see cref="FitFilterBox"/> does for a filter box. The horizontal
    /// padding stays the theme's, which is <c>space.5</c>.
    /// </summary>
    public static void FitComboBox(ComboBox combo, double height)
    {
        ArgumentNullException.ThrowIfNull(combo);
        combo.Height = height;
        combo.MinHeight = height;
        combo.Padding = new Thickness(Metric("FxcSpace5"), 0, 0, 0);
        combo.VerticalContentAlignment = VerticalAlignment.Center;
    }

    private static double Metric(string key) => Application.Current?.FindResource(key) as double? ?? 0;

    /// <summary>Escape: a non-empty filter is cleared, an empty one returns focus to the list (UI-TW-008).</summary>
    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (_model.FilterText.Length > 0)
        {
            _model.FilterText = "";
        }
        else
        {
            FocusPrimary();
        }

        e.Handled = true;
    }

    private static Button FlatButton(string automationId, string icon, double size, bool toggle = false)
    {
        var button = toggle ? new ToggleButton() : new Button();
        button.Width = size;
        button.Height = size;
        button.Padding = new Thickness(0);
        button.Focusable = false;
        button.Background = Brushes.Transparent;
        if (!toggle)
            button.BorderThickness = new Thickness(0); // a toggle keeps its border: the checked state draws it (I-215)
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Content = new IdeIcon { IconName = icon, Width = 16, Height = 16 };
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }
}
