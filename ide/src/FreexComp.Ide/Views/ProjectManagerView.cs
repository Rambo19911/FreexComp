using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Views;

/// <summary>
/// Content of the Project Manager tool window (tool-windows.md §2): missing-files info bar, the tree (primary control,
/// <c>Tool.ProjectManager.Tree</c>) and the empty state. All state lives in <see cref="ProjectManagerViewModel"/>; the view
/// mirrors the selection both ways and builds the context menu from the model when it opens.
/// </summary>
public sealed class ProjectManagerView : UserControl
{
    internal const string RowPrefix = "Tool.ProjectManager.Row.";

    private static readonly FuncTemplate<Panel?> VirtualizingRows = new(() => new VirtualizingStackPanel());

    private readonly TreeView _tree;
    private readonly StackPanel _barHost = new(); // the missing-files bar above the unlisted-files bar (UI-TW-123)
    private readonly Control _empty;
    private readonly TextBlock _emptyHeading = new() { Text = Strings.Msg_Project_NoProject, Classes = { "body" }, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _emptySecondary = new() { Text = Strings.ProjectManager_NoProject_Secondary, Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Canvas _dragLayer = new() { IsHitTestVisible = false };
    private Border? _dragAdorner;
    private Border? _insertionLine;
    private ProjectNodeViewModel? _pressed;
    private Point _pressedAt;
    private bool _dragging;
    private Avalonia.Threading.DispatcherTimer? _edgeScroll;
    private Point _lastDragPoint;
    // I-219: the links wrap in a narrow dock instead of running past its edge.
    private readonly WrapPanel _links = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, ItemSpacing = 8, LineSpacing = 4 };
    private ProjectManagerViewModel? _model;
    private Window? _window;
    private bool _syncing;
    private bool _fromTree;

    public ProjectManagerView()
    {
        _tree = new TreeView
        {
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncTreeDataTemplate<ProjectNodeViewModel>((node, _) => new ProjectNodeView { DataContext = node }, node => node.Children),
            ContextMenu = new ContextMenu(),
            ItemsPanel = VirtualizingRows, // UI-TW-017: a project can have far more than 200 rows
        };
        AutomationProperties.SetAutomationId(_tree, "Tool.ProjectManager.Tree");
        ToolWindowFrame.SetIsPrimary(_tree, true);
        _tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters =
            {
                new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(ProjectNodeViewModel.IsExpanded)) { Mode = BindingMode.TwoWay }),
                new Setter(IsVisibleProperty, new Binding(nameof(ProjectNodeViewModel.IsVisibleInTree))),
                new Setter(ToolTip.TipProperty, new Binding(nameof(ProjectNodeViewModel.ToolTipText))),
                new Setter(AutomationProperties.NameProperty, new Binding(nameof(ProjectNodeViewModel.AutomationName))),
                new Setter(AutomationProperties.AutomationIdProperty, new Binding(nameof(ProjectNodeViewModel.Key)) { StringFormat = RowPrefix + "{0}" }),
                new Setter(MinHeightProperty, 22d),
                new Setter(ItemsControl.ItemsPanelProperty, VirtualizingRows),
            },
        });
        // UI-TW-121: the drop-target row is filled with accent.subtle and outlined with 1 px accent.
        var dropTarget = new Style(x => x.OfType<TreeViewItem>().Class(ProjectNodeView.DropTargetClass).Template().OfType<Border>().Name("PART_LayoutRoot"));
        dropTarget.Setters.Add(new Setter(Border.BackgroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("FxcAccentSubtleBrush")));
        dropTarget.Setters.Add(new Setter(Border.BorderBrushProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("FxcAccentDefaultBrush")));
        dropTarget.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));
        _tree.Styles.Add(dropTarget);
        _tree.SelectionChanged += OnTreeSelectionChanged;
        // UI-TW-119…122: moving rows by pointer inside the tree (no OS drag-and-drop loop).
        _tree.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        _tree.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        _tree.AddHandler(PointerCaptureLostEvent, (_, _) => CancelDrag(), RoutingStrategies.Direct | RoutingStrategies.Bubble);
        _tree.AddHandler(KeyDownEvent, OnKeyDownWhileDragging, RoutingStrategies.Tunnel);
        _tree.AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
        _tree.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        // UI-TW-113: files dragged from the OS over the tree get the drop-target highlight (design-tokens §2.3
        // accent.subtle); the main window adds them on drop (MainWindowViewModel.DropFilesAsync).
        _tree.AddHandler(DragDrop.DragEnterEvent, OnFilesDraggedOver);
        _tree.AddHandler(DragDrop.DragOverEvent, OnFilesDraggedOver);
        _tree.AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropTarget(false), handledEventsToo: true);
        _tree.AddHandler(DragDrop.DropEvent, (_, _) => SetDropTarget(false), handledEventsToo: true);

        _empty = CreateEmptyState();
        var body = new Panel { Children = { _tree, _empty, _dragLayer } };
        DockPanel.SetDock(_barHost, Avalonia.Controls.Dock.Top);
        Content = new DockPanel { Children = { _barHost, body } };
    }

    private IDisposable? _dropHighlight;

    private void OnFilesDraggedOver(object? sender, DragEventArgs e) =>
        SetDropTarget(e.DataTransfer.TryGetFiles() is { Length: > 0 });

    private void SetDropTarget(bool on)
    {
        if (on && _dropHighlight is null)
            _dropHighlight = _tree.Bind(BackgroundProperty, _tree.GetResourceObservable("FxcAccentSubtleBrush"));
        else if (!on)
        {
            _dropHighlight?.Dispose();
            _dropHighlight = null;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
            _model.SelectionChanged -= OnModelSelectionChanged;
            _model.RenameEnded -= OnRenameEnded;
        }

        _model?.Roots.CollectionChanged -= OnRootsChanged;
        _model = DataContext as ProjectManagerViewModel;
        _model?.Roots.CollectionChanged += OnRootsChanged; // before the tree's own handler: focus is still inside
        _tree.ItemsSource = _model?.Roots;
        AutomationProperties.SetName(_tree, _model?.Title); // UI-A11Y-020
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
            _model.SelectionChanged += OnModelSelectionChanged;
            _model.RenameEnded += OnRenameEnded;
            BuildEmptyLinks();
        }

        UpdateState();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        _window?.Activated += OnWindowActivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _window?.Activated -= OnWindowActivated;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowActivated(object? sender, EventArgs e) => _model?.OnWindowActivated();

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectManagerViewModel.HasProject) or nameof(ProjectManagerViewModel.MissingFilesBar)
            or nameof(ProjectManagerViewModel.UnlistedFilesBar) or nameof(ProjectManagerViewModel.IsEmptyGroup))
            UpdateState();
    }

    private void UpdateState()
    {
        var hasProject = _model?.HasProject == true;
        _tree.IsVisible = hasProject;
        _empty.IsVisible = !hasProject;
        // IDE-PM-154: a project group without projects says so, with its own links.
        var emptyGroup = _model?.IsEmptyGroup == true;
        if (_emptySecondary.IsVisible == emptyGroup)
        {
            _emptyHeading.Text = emptyGroup ? Strings.Msg_Group_Empty : Strings.Msg_Project_NoProject;
            _emptySecondary.IsVisible = !emptyGroup;
            if (_model is not null)
                BuildEmptyLinks();
        }

        InfoBarViewModel?[] bars = [_model?.MissingFilesBar, _model?.UnlistedFilesBar];
        var shown = bars.OfType<InfoBarViewModel>().ToList();
        if (!shown.SequenceEqual(_barHost.Children.Select(c => c.DataContext)))
        {
            _barHost.Children.Clear();
            foreach (var bar in shown)
                _barHost.Children.Add(new InfoBarView { AutomationIdBase = "Tool.ProjectManager.InfoBar", DataContext = bar });
        }
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _model is null)
            return;
        _fromTree = true;
        try
        {
            _model.SetSelection([.. _tree.SelectedItems.OfType<ProjectNodeViewModel>()]);
        }
        finally
        {
            _fromTree = false;
        }
    }

    /// <summary>
    /// The model's selection (sync, Show, UI-TW-109 corrections) goes to the tree; the last row is brought into view —
    /// also when the selection is unchanged but was set by the model (sync after Collapse All, I-180).
    /// </summary>
    private void OnModelSelectionChanged(object? sender, EventArgs e)
    {
        if (_model is null)
            return;
        var differs = !_model.Selection.SequenceEqual(_tree.SelectedItems.OfType<ProjectNodeViewModel>());
        if (differs)
        {
            _syncing = true;
            try
            {
                _tree.SelectedItems.Clear();
                foreach (var node in _model.Selection)
                    _tree.SelectedItems.Add(node);
            }
            finally
            {
                _syncing = false;
            }
        }

        if (_model.Selection.Count > 0 && (differs || !_fromTree))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_model is { Selection.Count: > 0 } m)
                    ScrollIntoView(m.Selection[^1]);
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// UI-TW-017 (I-180): the rows are virtualized, so the row of <paramref name="node"/> may have no container yet.
    /// From the root down, each level scrolls its child into view by item, which realizes that child's container
    /// (the last one, the node's row, ends up with its header in view).
    /// </summary>
    private void ScrollIntoView(ProjectNodeViewModel node)
    {
        var path = new Stack<ProjectNodeViewModel>();
        for (var n = node; n is not null; n = n.Parent)
            path.Push(n);
        ItemsControl owner = _tree;
        while (path.TryPop(out var item))
        {
            owner.ScrollIntoView(item);
            if (owner.ContainerFromItem(item) is not TreeViewItem row)
                return;
            owner = row;
        }
    }

    /// <summary>
    /// IDE-PM-100: a rebuild after a file operation replaces every row; when focus was in the tree it stays there (on the
    /// selected row, else the tree) instead of falling back to the document.
    /// </summary>
    private void OnRootsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // IsActive covers a confirmation box that holds focus meanwhile (focus is tracked in the main window only).
        if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Reset || !(_tree.IsKeyboardFocusWithin || _model?.IsActive == true))
            return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var row = _model is { Selection.Count: > 0 } m ? _tree.TreeContainerFromItem(m.Selection[^1]) : _tree.ContainerFromIndex(0);
            row?.Focus(NavigationMethod.Tab);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>UI-TW-112: after the in-place rename, focus returns to the (renamed) selected row.</summary>
    private void OnRenameEnded(object? sender, EventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_model is { Selection.Count: > 0 } m && _tree.TreeContainerFromItem(m.Selection[^1]) is Control container)
                container.Focus(NavigationMethod.Tab);
        }, Avalonia.Threading.DispatcherPriority.Loaded);

    /// <summary>
    /// UI-TW-014: a right-click on an unselected row selects it first; on the empty area the selection is cleared.
    /// A double-click runs the default action (UI-TW-018) instead of the tree's own expand toggle.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_model is null)
            return;
        var node = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as ProjectNodeViewModel;
        var point = e.GetCurrentPoint(_tree);
        if (point.Properties.IsRightButtonPressed)
        {
            if (node is null)
                _model.ClearSelection();
            else if (!_model.Selection.Contains(node))
                _model.Select(node);
            // I-281: the commands of the menu act on the selection of the focused Project Manager, and the toolkit moves
            // the focus on a left press only. The right press moves it the same way (to the nearest focusable element
            // around the pressed one: the row, or the tool window's content for the empty area), so the menu belongs to
            // the right-clicked row whatever had the focus before.
            for (var v = e.Source as Visual; v is not null; v = v.GetVisualParent())
            {
                if (v is InputElement { Focusable: true, IsEffectivelyEnabled: true, IsEffectivelyVisible: true } target)
                {
                    target.Focus(NavigationMethod.Pointer);
                    break;
                }
            }

            e.Handled = true;
        }
        else if (point.Properties.IsLeftButtonPressed && e.ClickCount == 2 && node is not null)
        {
            _model.Select(node);
            _ = _model.OpenSelectionAsync();
            e.Handled = true;
        }
        else if (point.Properties.IsLeftButtonPressed && e.ClickCount == 1 && e.KeyModifiers == KeyModifiers.None && node is not null
                 && _model.CanStartDrag(node) && (e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is null)
        {
            // UI-TW-119: a drag may start here once the pointer has moved more than 4 DIP.
            _pressed = node;
            _pressedAt = point.Position;
            if (_model.Selection.Count > 1 && _model.Selection.Contains(node))
                e.Handled = true; // the multi-selection stays: its rows are dragged together (a plain click selects on release)
        }
    }

    /// <summary>UI-TW-119: beyond this distance from the press the drag starts.</summary>
    internal const double DragThreshold = 4;

    /// <summary>UI-TW-121: this close to the top or bottom edge the tree scrolls while dragging.</summary>
    private const double EdgeScrollZone = 24;

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_model is null || _pressed is null)
            return;
        var point = e.GetPosition(_tree);
        if (!_dragging)
        {
            if (!e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed)
            {
                _pressed = null; // the button went up somewhere else: no drag from a later hover
                return;
            }

            if (Math.Abs(point.X - _pressedAt.X) <= DragThreshold && Math.Abs(point.Y - _pressedAt.Y) <= DragThreshold)
                return;
            if (!_model.BeginDrag(_pressed))
            {
                _pressed = null;
                return;
            }

            _dragging = true;
            // I-245: no row tooltip while rows are dragged (it would cover the drop target); an open one closes.
            foreach (var open in _tree.GetVisualDescendants().OfType<Control>().Where(ToolTip.GetIsOpen).ToList())
                ToolTip.SetIsOpen(open, false);
            ToolTip.SetServiceEnabled(_tree, false);
            e.Pointer.Capture(_tree);
            ShowDragAdorner(_pressed, _model.DraggedRows.Count);
            _edgeScroll = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(50), Avalonia.Threading.DispatcherPriority.Input, (_, _) => ScrollAtEdge());
            _edgeScroll.Start();
        }

        _lastDragPoint = point;
        UpdateDrag(point);
        e.Handled = true;
    }

    /// <summary>UI-TW-120 / UI-TW-121: the adorner follows the pointer; the row under it decides target and cursor.</summary>
    private void UpdateDrag(Point point)
    {
        if (_model is null || _dragAdorner is null)
            return;
        var onLayer = _tree.TranslatePoint(point, _dragLayer) ?? point;
        Canvas.SetLeft(_dragAdorner, onLayer.X + 12);
        Canvas.SetTop(_dragAdorner, onLayer.Y + 16);
        var inside = new Rect(_tree.Bounds.Size).Contains(point);
        if (_model.IsRootDrag)
        {
            UpdateInsertionLine(inside ? point : null);
            return;
        }

        var over = inside ? (_tree.InputHitTest(point) as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as ProjectNodeViewModel : null;
        var target = _model.DragOver(over);
        _tree.Cursor = new Cursor(target is null ? StandardCursorType.No : StandardCursorType.DragMove);
    }

    /// <summary>
    /// UI-TW-127: while a root row is dragged the 2 px <c>accent</c> insertion line lies across the tree at the gap between
    /// root rows nearest to the pointer; the "not allowed" cursor outside the tree.
    /// </summary>
    private void UpdateInsertionLine(Point? point)
    {
        if (_model is null)
            return;
        // The gaps: above each root row and below the last one, in tree coordinates.
        var gaps = new List<double>();
        for (var i = 0; i < _model.Roots.Count; i++)
        {
            if (_tree.ContainerFromIndex(i) is not { } row || row.TranslatePoint(default, _tree) is not { } top)
                continue;
            gaps.Add(top.Y);
            if (i == _model.Roots.Count - 1)
                gaps.Add(top.Y + row.Bounds.Height);
        }

        int? gap = point is { } p && gaps.Count == _model.Roots.Count + 1
            ? Enumerable.Range(0, gaps.Count).MinBy(i => Math.Abs(gaps[i] - p.Y))
            : null;
        _model.DragOverGap(gap);
        _tree.Cursor = new Cursor(_model.InsertionGap is null ? StandardCursorType.No : StandardCursorType.DragMove);
        if (_model.InsertionGap is not { } at)
        {
            if (_insertionLine is not null)
                _dragLayer.Children.Remove(_insertionLine);
            _insertionLine = null;
            return;
        }

        if (_insertionLine is null)
        {
            _insertionLine = new Border { Height = 2 };
            _insertionLine.Bind(Border.BackgroundProperty, _insertionLine.GetResourceObservable("FxcAccentDefaultBrush"));
            AutomationProperties.SetAutomationId(_insertionLine, "Tool.ProjectManager.InsertionLine");
            _dragLayer.Children.Add(_insertionLine);
        }

        AutomationProperties.SetName(_insertionLine, _model.InsertionName);
        _insertionLine.Width = _tree.Bounds.Width;
        var onLayer = _tree.TranslatePoint(new Point(0, gaps[at]), _dragLayer) ?? new Point(0, gaps[at]);
        Canvas.SetLeft(_insertionLine, onLayer.X);
        Canvas.SetTop(_insertionLine, Math.Clamp(onLayer.Y - 1, 0, Math.Max(0, _dragLayer.Bounds.Height - 2)));
    }

    private void ShowDragAdorner(ProjectNodeViewModel pressed, int count)
    {
        var text = new TextBlock
        {
            Text = count > 1 ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Drag_FileCount, count) : pressed.Name,
            Classes = { "body" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (count == 1)
            content.Children.Add(new Icons.IdeIcon { IconName = pressed.IconName, Width = 16, Height = 16 });
        content.Children.Add(text);
        var adorner = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(6, 2), Child = content };
        adorner.Bind(Border.BackgroundProperty, adorner.GetResourceObservable("FxcBgRaisedBrush"));
        adorner.Bind(Border.BorderBrushProperty, adorner.GetResourceObservable("FxcBorderStrongBrush"));
        adorner.Bind(Border.CornerRadiusProperty, adorner.GetResourceObservable("FxcRadiusControl"));
        AutomationProperties.SetAutomationId(adorner, "Tool.ProjectManager.DragAdorner");
        AutomationProperties.SetName(adorner, text.Text);
        _dragAdorner = adorner;
        _dragLayer.Children.Add(adorner);
    }

    /// <summary>UI-TW-121: within 24 DIP of the top or bottom edge the tree scrolls by one row every 50 ms.</summary>
    private void ScrollAtEdge()
    {
        if (!_dragging || _tree.FindDescendantOfType<ScrollViewer>() is not { } scroll)
            return;
        var step = _lastDragPoint.Y < EdgeScrollZone ? -22d : _lastDragPoint.Y > _tree.Bounds.Height - EdgeScrollZone ? 22d : 0d;
        if (step == 0)
            return;
        var y = Math.Clamp(scroll.Offset.Y + step, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
        if (y == scroll.Offset.Y)
            return;
        scroll.Offset = scroll.Offset.WithY(y);
        UpdateDrag(_lastDragPoint);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pressed = _pressed;
        _pressed = null;
        if (_model is null)
            return;
        if (!_dragging)
        {
            // A press on a row of a multi-selection that did not become a drag is a plain click: the row alone.
            if (pressed is not null && e.InitialPressMouseButton == MouseButton.Left && _model.Selection.Count > 1 && _model.Selection.Contains(pressed))
                _model.Select(pressed);
            return;
        }

        UpdateDrag(e.GetPosition(_tree));
        StopDragVisuals();
        e.Pointer.Capture(null);
        e.Handled = true;
        _ = _model.DropAsync(); // releasing outside a valid target cancels (UI-TW-119)
    }

    /// <summary>UI-TW-119: Escape during the drag cancels it.</summary>
    private void OnKeyDownWhileDragging(object? sender, KeyEventArgs e)
    {
        if (!_dragging || e.Key != Key.Escape)
            return;
        e.Handled = true;
        CancelDrag();
    }

    private void CancelDrag()
    {
        if (!_dragging)
            return; // a row container that gives up the pointer before the drag starts (it scrolled) cancels nothing
        _pressed = null;
        StopDragVisuals();
        _model?.EndDrag();
    }

    private void StopDragVisuals()
    {
        _dragging = false;
        _edgeScroll?.Stop();
        _edgeScroll = null;
        _dragLayer.Children.Clear();
        _dragAdorner = null;
        _insertionLine = null;
        _tree.ClearValue(CursorProperty);
        _tree.ClearValue(ToolTip.ServiceEnabledProperty);
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_model is null)
            return;
        var menu = MenuBarBuilder.CreateContextMenu(_model.ContextMenu());
        _tree.ContextMenu!.Items.Clear();
        foreach (var item in menu.Items.Cast<Control>().ToList())
        {
            menu.Items.Remove(item);
            _tree.ContextMenu.Items.Add(item);
        }
    }

    /// <summary>UI-TW-013 empty state: <c>Msg.Project.NoProject</c>, its secondary line and the two links.</summary>
    private Control CreateEmptyState()
    {
        var heading = _emptyHeading;
        heading.Bind(TextBlock.ForegroundProperty, heading.GetResourceObservable("FxcTextSecondaryBrush"));
        var secondary = _emptySecondary;
        secondary.Bind(TextBlock.ForegroundProperty, secondary.GetResourceObservable("FxcTextTertiaryBrush"));
        var block = new StackPanel { Spacing = 4, Children = { heading, secondary, _links } };
        Grid.SetRow(block, 1);
        var grid = new Grid { RowDefinitions = new RowDefinitions("1*,Auto,2*"), Children = { block } };
        AutomationProperties.SetAutomationId(grid, "Tool.ProjectManager.Empty");
        return grid;
    }

    private void BuildEmptyLinks()
    {
        _links.Children.Clear();
        foreach (var command in _model!.EmptyLinks)
        {
            var link = new Button
            {
                Content = command.Title,
                Command = command,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
            };
            link.Bind(ForegroundProperty, link.GetResourceObservable("FxcTextLinkBrush"));
            AutomationProperties.SetAutomationId(link, "Tool.ProjectManager.Empty.Link." + command.Id);
            _links.Children.Add(link);
        }
    }
}
