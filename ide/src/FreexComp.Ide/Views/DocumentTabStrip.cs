using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// The document tab strip (editor.md §8, UI-ED-070, UI-ED-076, UI-ED-078): tabs in a horizontally scrolling row that
/// keeps the active tab visible, and the overflow button listing every open document. DataContext: the
/// <see cref="DocumentAreaViewModel"/>.
/// </summary>
public sealed class DocumentTabStrip : Border
{
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer _scroll;
    private readonly Button _overflow;
    private readonly Dictionary<DocumentTabViewModel, DocumentTabView> _views = [];
    private DocumentAreaViewModel? _model;

    /// <summary>UI-ED-076: the drop position line while a tab is dragged.</summary>
    private readonly Avalonia.Controls.Shapes.Rectangle _dropIndicator = new() { Name = "TabDropIndicator", Width = 2, IsVisible = false, IsHitTestVisible = false };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private DocumentTabView? _dragTab;
    private Point _dragStart;
    private bool _dragging;
    private int _dropAt;

    /// <summary>Pointer travel (DIP) before a press on a tab becomes a drag.</summary>
    private const double DragThreshold = 4;

    public DocumentTabStrip()
    {
        this.Bind(BackgroundProperty, this.GetResourceObservable("FxcBgSunkenBrush"));
        this.Bind(BorderBrushProperty, this.GetResourceObservable("FxcBorderDefaultBrush"));
        BorderThickness = new Thickness(0, 0, 0, 1);

        _overflow = new Button
        {
            Width = 28,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new IdeIcon { IconName = "chevron-down", Width = 16, Height = 16 },
            Focusable = false,
            IsVisible = false,
        };
        _overflow.Bind(HeightProperty, this.GetResourceObservable("FxcMetricDocumentTabHeight"));
        AutomationProperties.SetAutomationId(_overflow, AutomationIds.DocumentAreaTabOverflow);
        AutomationProperties.SetName(_overflow, Strings.DocumentArea_TabOverflow);
        ToolTip.SetTip(_overflow, Strings.DocumentArea_TabOverflow);
        _overflow.Click += (_, _) => OpenOverflowMenu();
        DockPanel.SetDock(_overflow, Dock.Right);

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Panel { Children = { _tabs, _overlay } },
            Background = Brushes.Transparent,
        };
        _scroll.Bind(MinHeightProperty, this.GetResourceObservable("FxcMetricDocumentTabHeight"));
        _scroll.PointerWheelChanged += (_, e) =>
        {
            _scroll.Offset = new Vector(Math.Max(0, _scroll.Offset.X - (e.Delta.Y + e.Delta.X) * 40), 0); // UI-ED-078
            e.Handled = true;
        };
        _scroll.DoubleTapped += (_, e) =>
        {
            if (_model is not null && (e.Source as Visual)?.FindAncestorOfType<DocumentTabView>(includeSelf: true) is null)
                _ = _model.NewFileAsync(); // UI-ED-076
        };

        Child = new DockPanel { Children = { _overflow, _scroll } };

        _dropIndicator.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, this.GetResourceObservable("FxcAccentDefaultBrush"));
        _dropIndicator.Bind(HeightProperty, this.GetResourceObservable("FxcMetricDocumentTabHeight"));
        _overlay.Children.Add(_dropIndicator);
        _tabs.AddHandler(PointerPressedEvent, OnTabPressed);
        _tabs.AddHandler(PointerMovedEvent, OnTabMoved, handledEventsToo: true);
        _tabs.AddHandler(PointerReleasedEvent, OnTabReleased, handledEventsToo: true);
        _tabs.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag());
    }

    private void OnTabPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(_tabs).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;
        _dragTab = (e.Source as Visual)?.FindAncestorOfType<DocumentTabView>(includeSelf: true);
        _dragStart = e.GetPosition(_tabs);
        _dragging = false;
    }

    private void OnTabMoved(object? sender, PointerEventArgs e)
    {
        if (_dragTab is null)
            return;
        var position = e.GetPosition(_tabs);
        if (!_dragging && Math.Abs(position.X - _dragStart.X) < DragThreshold)
            return;

        _dragging = true;
        var views = _tabs.Children.OfType<DocumentTabView>().ToList();
        _dropAt = views.Count(v => v.Bounds.Center.X < position.X);
        var x = _dropAt < views.Count ? views[_dropAt].Bounds.Left : views[^1].Bounds.Right;
        Canvas.SetLeft(_dropIndicator, x - _dropIndicator.Width / 2);
        _dropIndicator.IsVisible = true;
    }

    private void OnTabReleased(object? sender, PointerReleasedEventArgs e)
    {
        var tab = _dragging ? _dragTab?.DataContext as DocumentTabViewModel : null;
        var at = _dropAt;
        EndDrag();
        if (tab is not null)
            _model?.MoveTab(tab, at); // UI-ED-076: dragging out of the strip is not supported (the X position counts)
    }

    private void EndDrag()
    {
        _dragTab = null;
        _dragging = false;
        _dropIndicator.IsVisible = false;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.Tabs.CollectionChanged -= OnTabsChanged;
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = DataContext as DocumentAreaViewModel;
        if (_model is not null)
        {
            _model.Tabs.CollectionChanged += OnTabsChanged;
            _model.PropertyChanged += OnModelChanged;
        }

        SyncTabs();
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncTabs();

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentAreaViewModel.ActiveDocument))
            Dispatcher.UIThread.Post(BringActiveIntoView, DispatcherPriority.Background);
    }

    private void SyncTabs()
    {
        var tabs = _model?.Tabs.ToList() ?? [];
        foreach (var gone in _views.Keys.Except(tabs).ToList())
        {
            _views.Remove(gone);
        }

        _tabs.Children.Clear();
        foreach (var tab in tabs)
        {
            if (!_views.TryGetValue(tab, out var view))
            {
                view = new DocumentTabView { Area = _model };
                view.DataContext = tab;
                _views[tab] = view;
            }

            _tabs.Children.Add(view);
        }

        _overflow.IsVisible = tabs.Count > 0;
        Dispatcher.UIThread.Post(BringActiveIntoView, DispatcherPriority.Background);
    }

    private void BringActiveIntoView()
    {
        if (_views.FirstOrDefault(p => p.Key.IsActive).Value is { } active)
            active.BringIntoView();
    }

    /// <summary>UI-ED-078: every open document in tab order; check mark on the active one; choosing runs window.activateDocument.</summary>
    private void OpenOverflowMenu()
    {
        if (_model is null)
            return;

        var menu = new ContextMenu();
        foreach (var (tab, label) in _model.OverflowEntries)
        {
            var item = new MenuItem
            {
                Header = label.Replace("_", "__", StringComparison.Ordinal),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = tab.IsActive,
                Command = _model.ActivateDocumentCommand,
                CommandParameter = tab.Document.Id.ToString(),
            };
            AutomationProperties.SetAutomationId(item, AutomationIds.DocumentAreaTabOverflowItem(tab.TabName));
            menu.Items.Add(item);
        }

        _overflow.ContextMenu = menu;
        menu.Open(_overflow);
    }
}
