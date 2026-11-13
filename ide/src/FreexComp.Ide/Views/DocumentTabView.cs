using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// One document tab (editor.md §8, UI-ED-070…076, UI-ED-079): lock glyph, title, folder suffix, and the close slot
/// that shows the modified dot or the × button. <see cref="Area"/> is set by <see cref="DocumentTabStrip"/>.
/// </summary>
public sealed class DocumentTabView : Border
{
    private readonly TextBlock _title = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _suffix = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
    private readonly IdeIcon _lock = new() { IconName = "lock", Width = 16, Height = 16, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _close = new()
    {
        Content = new IdeIcon { IconName = "close", Width = 16, Height = 16 },
        Padding = new Thickness(0),
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Focusable = false,
    };

    private readonly Ellipse _dot = new() { Width = 8, Height = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Panel _slot = new() { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }; // space.3
    private readonly Border _padding = new();
    private DocumentTabViewModel? _tab;
    private IDisposable? _background;
    private IDisposable? _indicator;
    private IDisposable? _foreground;

    public DocumentTabView()
    {
        this.Bind(HeightProperty, this.GetResourceObservable("FxcMetricDocumentTabHeight"));
        this.Bind(MinWidthProperty, this.GetResourceObservable("FxcMetricDocumentTabMinWidth"));
        this.Bind(MaxWidthProperty, this.GetResourceObservable("FxcMetricDocumentTabMaxWidth"));
        BorderThickness = new Thickness(0, 2, 0, 0); // the active tab's top indicator (UI-ED-070)
        _slot.Bind(WidthProperty, this.GetResourceObservable("FxcMetricDocumentTabCloseSize"));
        _slot.Bind(HeightProperty, this.GetResourceObservable("FxcMetricDocumentTabCloseSize"));
        _suffix.Classes.Add("body");
        _suffix.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("FxcTextTertiaryBrush"));
        _dot.Bind(Shape.FillProperty, this.GetResourceObservable("FxcTabModifiedDotBrush"));
        AutomationProperties.SetName(_dot, Strings.DocumentTab_Modified);
        AutomationProperties.SetName(_close, Strings.DocumentTab_Close);
        ToolTip.SetTip(_close, Strings.DocumentTab_Close);
        _close.Click += (_, e) =>
        {
            e.Handled = true;
            if (_tab is not null && Area is not null)
                _ = Area.CloseAsync(_tab);
        };
        _close.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsFocusedProperty)
                Update();
        };
        _slot.Children.Add(_close);

        var separator = new Rectangle { Width = 1 };
        separator.Bind(Shape.FillProperty, this.GetResourceObservable("FxcBorderSubtleBrush"));
        DockPanel.SetDock(separator, Dock.Right);
        var padding = _padding;
        var row = new DockPanel();
        DockPanel.SetDock(_slot, Dock.Right);
        DockPanel.SetDock(_lock, Dock.Left);
        DockPanel.SetDock(_title, Dock.Left);
        row.Children.Add(_slot);
        row.Children.Add(_lock);
        row.Children.Add(_title);
        row.Children.Add(_suffix);
        padding.Child = row;
        Child = new DockPanel { Children = { separator, padding } };
    }

    /// <summary>The document area whose commands the tab uses.</summary>
    public DocumentAreaViewModel? Area { get; set; }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_tab is not null)
            _tab.PropertyChanged -= OnTabChanged;
        _tab = DataContext as DocumentTabViewModel;
        if (_tab is not null)
            _tab.PropertyChanged += OnTabChanged;
        ContextMenu = Area is null ? null : MenuBarBuilder.CreateContextMenu(Area.TabContextMenu);
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty)
            Update();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_tab is null || Area is null)
            return;

        var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (kind == PointerUpdateKind.MiddleButtonPressed)
        {
            e.Handled = true;
            _ = Area.CloseAsync(_tab); // UI-ED-076
        }
        else if (kind is PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed)
        {
            Area.Activate(_tab); // a right click activates before the context menu opens (UI-ED-079)
        }
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        if (_tab is not { } tab)
            return;

        AutomationProperties.SetAutomationId(this, AutomationIds.DocumentTab(tab.Title, tab.Occurrence));
        AutomationProperties.SetName(this, tab.Title);
        AutomationProperties.SetAutomationId(_close, AutomationIds.DocumentTabClose(tab.TabName));
        AutomationProperties.SetAutomationId(_dot, AutomationIds.DocumentTabModified(tab.TabName));
        ToolTip.SetTip(this, tab.ToolTip);
        if (this.TryFindResource("FxcMetricDocumentTabPadding", out var padding) && padding is double horizontal)
            _padding.Padding = new Thickness(horizontal, 0);
        _title.Text = tab.Title;
        _suffix.Text = tab.Suffix;
        _suffix.IsVisible = tab.Suffix is not null;
        _lock.IsVisible = tab.IsReadOnly;
        _title.Classes.Set("body", !tab.IsActive);
        _title.Classes.Set("bodyStrong", tab.IsActive);

        // UI-ED-072/073: the dot replaces the × while modified, except on hover / keyboard focus of the slot.
        var hovered = IsPointerOver || _close.IsFocused;
        if (tab.IsModified)
        {
            if (!_slot.Children.Contains(_dot))
                _slot.Children.Add(_dot);
            _dot.IsVisible = !hovered;
            _close.IsVisible = hovered;
        }
        else
        {
            _slot.Children.Remove(_dot);
            _close.IsVisible = tab.IsActive || hovered;
        }

        Rebind(ref _background, BackgroundProperty, tab.IsActive ? "FxcBgEditorBrush" : hovered ? "FxcBgHoverBrush" : null);
        Rebind(ref _indicator, BorderBrushProperty, tab.IsActive ? "FxcTabActiveIndicatorBrush" : null);
        _foreground?.Dispose();
        _foreground = _title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(tab.IsActive ? "FxcTextPrimaryBrush" : "FxcTextSecondaryBrush"));
    }

    private void Rebind(ref IDisposable? binding, AvaloniaProperty<IBrush?> property, string? resourceKey)
    {
        binding?.Dispose();
        binding = null;
        if (resourceKey is null)
            SetValue(property, Brushes.Transparent);
        else
            binding = this.Bind(property, this.GetResourceObservable(resourceKey));
    }
}
