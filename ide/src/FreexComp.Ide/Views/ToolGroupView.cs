using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Views;

/// <summary>
/// One fixed tool group (UI-TW-001/003): the active tool window above, the dock's tab strip at the bottom, shown even
/// for a single tool window. Each tool window keeps one <see cref="ToolWindowFrame"/> while the group lives.
/// </summary>
public sealed class ToolGroupView : DockPanel
{
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, ClipToBounds = true };
    private readonly Border _content = new();
    private readonly Button _overflow;
    private readonly Dictionary<ToolWindowViewModel, ToolWindowFrame> _frames = [];
    private ToolGroupViewModel? _model;

    public ToolGroupView()
    {
        // UI-TW-003: when the tabs do not fit, a chevron at the right end lists every tool window of the dock.
        _overflow = new Button
        {
            Width = 24,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Focusable = false,
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new Icons.IdeIcon { IconName = "chevron-down", Width = 16, Height = 16 },
        };
        AutomationProperties.SetName(_overflow, Core.Resources.Strings.ToolWindow_TabOverflow);
        ToolTip.SetTip(_overflow, Core.Resources.Strings.ToolWindow_TabOverflow);
        _overflow.Click += (_, _) => OpenOverflowMenu();
        DockPanel.SetDock(_overflow, Avalonia.Controls.Dock.Right);
        var strip = new Border { Child = new DockPanel { Children = { _overflow, _tabs } }, ClipToBounds = true };
        strip.Bind(HeightProperty, strip.GetResourceObservable("FxcMetricToolTabHeight"));
        strip.Bind(Border.BackgroundProperty, strip.GetResourceObservable("FxcBgSunkenBrush"));
        SetDock(strip, Avalonia.Controls.Dock.Bottom);
        Children.Add(strip);
        _content.Bind(Border.BackgroundProperty, _content.GetResourceObservable("FxcBgPanelBrush"));
        Children.Add(_content);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AutomationProperties.AutomationIdProperty)
            AutomationProperties.SetAutomationId(_overflow, AutomationIds.Get(this) + ".TabOverflow");
    }

    /// <summary>The chevron shows when the tabs' natural widths exceed the strip (stable: compared with the full width).</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        var needed = _tabs.Children.Sum(c => c.DesiredSize.Width);
        var overflows = needed > finalSize.Width + 0.5;
        if (_overflow.IsVisible != overflows)
            _overflow.IsVisible = overflows;
        return result;
    }

    private void OpenOverflowMenu()
    {
        if (_model is not { } model)
            return;
        var menu = new ContextMenu();
        foreach (var window in model.Windows)
        {
            var name = window.Name;
            var item = new MenuItem
            {
                Header = window.Title.Replace("_", "__", StringComparison.Ordinal),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = window == model.ActiveWindow,
            };
            AutomationProperties.SetAutomationId(item, AutomationIds.Get(_overflow) + "." + name);
            item.Click += (_, _) => model.Activate(name);
            menu.Items.Add(item);
        }

        _overflow.ContextMenu = menu;
        menu.Open(_overflow);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _model?.PropertyChanged -= OnModelChanged;
        _model = DataContext as ToolGroupViewModel;
        _model?.PropertyChanged += OnModelChanged;
        Rebuild();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        _tabs.Children.Clear();
        if (_model is not { } model)
        {
            _content.Child = null;
            return;
        }

        foreach (var window in model.Windows)
        {
            _tabs.Children.Add(CreateTab(model, window, window == model.ActiveWindow));
        }

        _content.Child = model.ActiveWindow is { } active ? FrameOf(active) : null;
    }

    /// <summary>F6 / Alt+0 into this group: the active tab's primary control (UI-A11Y-003).</summary>
    public void FocusActive()
    {
        if (_model?.ActiveWindow is { } active)
        {
            FrameOf(active).FocusPrimary();
        }
    }

    private ToolWindowFrame FrameOf(ToolWindowViewModel window)
    {
        if (!_frames.TryGetValue(window, out var frame))
        {
            frame = new ToolWindowFrame(window);
            _frames[window] = frame;
        }

        return frame;
    }

    /// <summary>Text-only tab; the active one has <c>bg.panel</c>, <c>text.primary</c> and a 2 px accent line at its bottom.</summary>
    private static Button CreateTab(ToolGroupViewModel model, ToolWindowViewModel window, bool active)
    {
        var tool = window.Name;
        var text = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
        text.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(ToolWindowViewModel.Title)) { Source = window, Mode = Avalonia.Data.BindingMode.OneWay });
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(active ? "FxcTextPrimaryBrush" : "FxcTextSecondaryBrush"));
        var tab = new Button
        {
            Content = text,
            Focusable = false,
            CornerRadius = new CornerRadius(0),
            VerticalAlignment = VerticalAlignment.Stretch,
            BorderThickness = new Thickness(0, 0, 0, active ? 2 : 0),
        };
        tab.Bind(Button.PaddingProperty, tab.GetResourceObservable("FxcSpace4Horizontal"));
        tab.Bind(Button.BackgroundProperty, tab.GetResourceObservable(active ? "FxcBgPanelBrush" : "FxcBgSunkenBrush"));
        tab.Bind(Button.BorderBrushProperty, tab.GetResourceObservable("FxcAccentDefaultBrush"));
        AutomationProperties.SetAutomationId(tab, AutomationIds.ToolTab(tool));
        tab.Bind(AutomationProperties.NameProperty,
            new Avalonia.Data.Binding(nameof(ToolWindowViewModel.TabAutomationName)) { Source = window, Mode = Avalonia.Data.BindingMode.OneWay });
        tab.Click += (_, _) => model.Activate(tool);
        return tab;
    }
}
