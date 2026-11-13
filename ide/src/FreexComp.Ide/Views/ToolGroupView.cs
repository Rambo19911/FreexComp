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
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly Border _content = new();
    private readonly Dictionary<ToolWindowViewModel, ToolWindowFrame> _frames = [];
    private ToolGroupViewModel? _model;

    public ToolGroupView()
    {
        var strip = new Border { Child = _tabs };
        strip.Bind(HeightProperty, strip.GetResourceObservable("FxcMetricToolTabHeight"));
        strip.Bind(Border.BackgroundProperty, strip.GetResourceObservable("FxcBgSunkenBrush"));
        SetDock(strip, Avalonia.Controls.Dock.Bottom);
        Children.Add(strip);
        _content.Bind(Border.BackgroundProperty, _content.GetResourceObservable("FxcBgPanelBrush"));
        Children.Add(_content);
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
        var text = new TextBlock { Text = window.Title, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
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
        AutomationProperties.SetName(tab, window.Title);
        tab.Click += (_, _) => model.Activate(tool);
        return tab;
    }
}
