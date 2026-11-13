using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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

    public ToolWindowFrame(ToolWindowViewModel model)
    {
        _model = model;
        DataContext = model;
        AutomationProperties.SetAutomationId(this, AutomationIds.Tool(model.Name));
        CommandRouting.SetScope(this, model.Scope);
        CommandRouting.SetToolWindow(this, model.Name);

        Add(CreateCaption(), Avalonia.Controls.Dock.Top);
        if (model.ToolBar.Count > 0 || model.HasFilter)
        {
            Add(CreateToolBar(), Avalonia.Controls.Dock.Top);
        }

        _content.Content = model;
        _content.Bind(ContentControl.BackgroundProperty, _content.GetResourceObservable("FxcBgPanelBrush"));
        Children.Add(_content);
        model.PropertyChanged += OnModelChanged;
        model.FocusRequested += (_, _) => Dispatcher.UIThread.Post(FocusPrimary, DispatcherPriority.Loaded);
        _content.Focusable = true; // focus target when the content has no focusable control
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
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

        var grid = new Grid { Children = { _title, hide, _focusLine } };
        var caption = new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) };
        caption.Bind(HeightProperty, caption.GetResourceObservable("FxcMetricToolCaptionHeight"));
        caption.Bind(Border.BackgroundProperty, caption.GetResourceObservable("FxcBgHeaderBrush"));
        caption.Bind(Border.BorderBrushProperty, caption.GetResourceObservable("FxcBorderDefaultBrush"));
        return caption;
    }

    private Control CreateToolBar()
    {
        var row = new DockPanel();
        if (_model.HasFilter)
        {
            var filter = CreateFilter();
            SetDock(filter, Avalonia.Controls.Dock.Right);
            row.Children.Add(filter);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in _model.ToolBar)
        {
            var button = FlatButton($"{AutomationIds.Tool(_model.Name)}.ToolBar.{item.Command.Id}", item.IconName, 24);
            button.Command = item.Command;
            button.Bind(AutomationProperties.NameProperty, new Binding(nameof(item.Title)) { Source = item, Mode = BindingMode.OneWay });
            button.Bind(ToolTip.TipProperty, new Binding(nameof(item.Tooltip)) { Source = item, Mode = BindingMode.OneWay });
            buttons.Children.Add(button);
        }

        row.Children.Add(buttons);
        var bar = new Border { Child = row, Height = 28, BorderThickness = new Thickness(0, 0, 0, 1) };
        bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable("FxcBgPanelBrush"));
        bar.Bind(Border.BorderBrushProperty, bar.GetResourceObservable("FxcBorderSubtleBrush"));
        return bar;
    }

    /// <summary>Right-aligned, 26 high, 120…240 wide, placeholder "Filter", clear button when non-empty (UI-TW-008).</summary>
    private Control CreateFilter()
    {
        _filter = new TextBox
        {
            Height = 26,
            MinHeight = 26,
            MinWidth = 120,
            MaxWidth = 240,
            Width = 180,
            PlaceholderText = Strings.ToolWindow_FilterPlaceholder,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(_filter, AutomationIds.Tool(_model.Name) + ".Filter");
        _filter.Bind(TextBox.TextProperty, new Binding(nameof(ToolWindowViewModel.FilterText)) { Source = _model, Mode = BindingMode.TwoWay });
        _filter.AddHandler(KeyDownEvent, OnFilterKeyDown, RoutingStrategies.Tunnel);

        _clearFilter = FlatButton(AutomationIds.Tool(_model.Name) + ".Filter.Clear", "close", 20);
        AutomationProperties.SetName(_clearFilter, Strings.ToolWindow_ClearFilter);
        ToolTip.SetTip(_clearFilter, Strings.ToolWindow_ClearFilter);
        _clearFilter.Click += (_, _) => _model.FilterText = "";
        _filter.InnerRightContent = _clearFilter;
        _filter.Bind(MarginProperty, _filter.GetResourceObservable("FxcSpace2Horizontal"));
        return _filter;
    }

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

    private static Button FlatButton(string automationId, string icon, double size)
    {
        var button = new Button
        {
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            Focusable = false,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new IdeIcon { IconName = icon, Width = 16, Height = 16 },
        };
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }
}
