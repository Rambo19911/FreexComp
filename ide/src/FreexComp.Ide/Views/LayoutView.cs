using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Views;

/// <summary>
/// Regions 4–7 of the main window (main-window.md §1): left dock, document area over the bottom dock, right dock with
/// an upper and a lower group, splitters between them (UI-WIN-001). Collapsed docks take no space and hide their
/// splitter (UI-WIN-004); dock sizes follow the profile and are squeezed per UI-WIN-060 when space is short.
/// The docks are fixed in Phases 1–7 (UI-WIN-003), so the structure is drawn here directly; no docking library.
/// </summary>
public sealed class LayoutView : Grid
{
    private const int LeftColumn = 0;
    private const int RightColumn = 4;
    private const int BottomRow = 2;

    private readonly Grid _center = new() { RowDefinitions = new RowDefinitions("*,Auto,Auto") };
    private readonly Grid _right = new() { RowDefinitions = new RowDefinitions("*,Auto,*") };
    private readonly ToolGroupView _left = Group(AutomationIds.DockLeft);
    private readonly ToolGroupView _bottom = Group(AutomationIds.DockBottom);
    private readonly ToolGroupView _rightUpper = Group(AutomationIds.DockRightUpper);
    private readonly ToolGroupView _rightLower = Group(AutomationIds.DockRightLower);
    private readonly ContentControl _documents = new();
    private readonly GridSplitter _leftSplitter;
    private readonly GridSplitter _rightSplitter;
    private readonly GridSplitter _bottomSplitter;
    private readonly GridSplitter _rightSplit;
    private LayoutViewModel? _model;

    public LayoutView()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto");
        _leftSplitter = Splitter(GridResizeDirection.Columns, AutomationIds.DockLeft, DockSplitter.Left);
        _rightSplitter = Splitter(GridResizeDirection.Columns, AutomationIds.DockRight, DockSplitter.Right);
        _bottomSplitter = Splitter(GridResizeDirection.Rows, AutomationIds.DockBottom, DockSplitter.Bottom);
        _rightSplit = Splitter(GridResizeDirection.Rows, AutomationIds.DockRightUpper, DockSplitter.RightSplit);

        Place(this, _left, column: LeftColumn);
        Place(this, _leftSplitter, column: 1);
        Place(this, _center, column: 2);
        Place(this, _rightSplitter, column: 3);
        Place(this, _right, column: RightColumn);
        Place(_center, _documents, row: 0);
        Place(_center, _bottomSplitter, row: 1);
        Place(_center, _bottom, row: BottomRow);
        Place(_right, _rightUpper, row: 0);
        Place(_right, _rightSplit, row: 1);
        Place(_right, _rightLower, row: 2);
        AutomationProperties.SetAutomationId(_right, AutomationIds.DockRight);

        SizeChanged += (_, _) => ApplySizes();
    }


    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _model?.PropertyChanged -= OnModelChanged;
        _model?.PaneCycleRequested -= OnPaneCycleRequested;
        _model?.PaneFocusRequested -= OnPaneFocusRequested;
        foreach (var group in _model?.Groups ?? [])
        {
            group.PropertyChanged -= OnModelChanged;
        }

        _model = DataContext as LayoutViewModel;
        if (_model is not { } model)
        {
            return;
        }

        model.PropertyChanged += OnModelChanged;
        model.PaneCycleRequested += OnPaneCycleRequested;
        model.PaneFocusRequested += OnPaneFocusRequested;
        foreach (var group in model.Groups)
        {
            group.PropertyChanged += OnModelChanged;
        }

        _left.DataContext = model.Left;
        _bottom.DataContext = model.Bottom;
        _rightUpper.DataContext = model.RightUpper;
        _rightLower.DataContext = model.RightLower;
        _documents.Content = model.DocumentArea;
        ApplySizes();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LayoutViewModel.DocumentArea))
        {
            _documents.Content = _model?.DocumentArea;
        }

        ApplySizes();
    }

    private void OnPaneCycleRequested(object? sender, int step)
    {
        if (_model is { } model)
        {
            FocusPane(model.NextPane(FocusedPane(), step));
        }
    }

    private void OnPaneFocusRequested(object? sender, Pane pane) => FocusPane(pane);

    /// <summary>The pane holding keyboard focus, or null when focus is outside the docks and the document area.</summary>
    private Pane? FocusedPane()
    {
        for (var v = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual; v is not null; v = v.GetVisualParent())
        {
            if (v == _documents)
            {
                return Pane.Documents;
            }

            if (v is ToolGroupView group && PaneOf(group) is { } pane)
            {
                return pane;
            }

            if (v is StatusBarView)
            {
                return Pane.StatusBar;
            }
        }

        return null;
    }

    private Pane? PaneOf(ToolGroupView group) =>
        group == _left ? Pane.Left : group == _bottom ? Pane.Bottom : group == _rightUpper ? Pane.RightUpper : group == _rightLower ? Pane.RightLower : null;

    private void FocusPane(Pane pane)
    {
        switch (pane)
        {
            case Pane.Documents:
                // The active document's text (or its design surface in the Design view, UI-A11Y-004); another document
                // view; while none is open, the document area itself.
                if (_documents.GetVisualDescendants().OfType<Editor.CodeEditorView>().FirstOrDefault(v => v.IsEffectivelyVisible)?.FocusText() != true
                    && _documents.GetVisualDescendants().OfType<Designer.DesignSurfaceControl>().FirstOrDefault(c => c.IsEffectivelyVisible)?.Focus(NavigationMethod.Tab) != true)
                    _documents.GetVisualDescendants().OfType<InputElement>()
                        .FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible && c.FindAncestorOfType<DocumentTabStrip>() is null)
                        ?.Focus(NavigationMethod.Tab); // the tabs are reached by keyboard from the document, not instead of it
                break;
            case Pane.Left:
                _left.FocusActive();
                break;
            case Pane.Bottom:
                _bottom.FocusActive();
                break;
            case Pane.RightUpper:
                _rightUpper.FocusActive();
                break;
            case Pane.StatusBar:
                // UI-WIN-042: the status bar lives outside the docks, in the same window.
                TopLevel.GetTopLevel(this)?.GetVisualDescendants().OfType<StatusBarView>().FirstOrDefault()?.FocusFirstField();
                break;
            default:
                _rightLower.FocusActive();
                break;
        }
    }

    private void ApplySizes()
    {
        if (_model is not { } model)
        {
            return;
        }

        var splitter = Metric("FxcMetricSplitterHitArea");
        var leftShown = !model.Left.IsCollapsed;
        var rightShown = !model.IsRightCollapsed;
        var bottomShown = !model.Bottom.IsCollapsed;
        var width = Bounds.Width > 0 ? Bounds.Width : double.PositiveInfinity;
        var height = Bounds.Height > 0 ? Bounds.Height : double.PositiveInfinity;
        var (leftMin, rightMin) = SideMinimums(model);
        var (left, right) = DockSizing.Widths(width, model.LeftSize, model.RightSize, leftShown, rightShown, splitter, leftMin, rightMin);
        var bottom = DockSizing.BottomHeight(height, model.BottomSize, bottomShown, splitter);

        ColumnDefinitions[LeftColumn].Width = new GridLength(left);
        ColumnDefinitions[RightColumn].Width = new GridLength(right);
        _center.RowDefinitions[BottomRow].Height = new GridLength(bottom);

        // UI-WIN-005: a splitter drag stays within the limits (the grid enforces them while dragging).
        var splitters = (leftShown ? splitter : 0) + (rightShown ? splitter : 0);
        SetLimits(ColumnDefinitions[LeftColumn], leftShown, leftMin, DockSizing.ClampSide(double.PositiveInfinity, width, right, splitters, leftMin));
        SetLimits(ColumnDefinitions[RightColumn], rightShown, rightMin, DockSizing.ClampSide(double.PositiveInfinity, width, left, splitters, rightMin));
        SetLimits(_center.RowDefinitions[BottomRow], bottomShown, DockSizing.BottomDockMinHeight, DockSizing.ClampBottom(double.PositiveInfinity, height, splitter));
        _left.IsVisible = _leftSplitter.IsVisible = leftShown;
        _right.IsVisible = _rightSplitter.IsVisible = rightShown;
        _bottom.IsVisible = _bottomSplitter.IsVisible = bottomShown;

        // Each half of the right dock collapses on its own; the other then takes the whole height.
        var upperShown = !model.RightUpper.IsCollapsed;
        var lowerShown = !model.RightLower.IsCollapsed;
        _rightUpper.IsVisible = upperShown;
        _rightLower.IsVisible = lowerShown;
        _rightSplit.IsVisible = upperShown && lowerShown;
        _right.RowDefinitions[0].Height = upperShown ? new GridLength(lowerShown ? model.RightSplit : 1, GridUnitType.Star) : new GridLength(0);
        _right.RowDefinitions[2].Height = lowerShown ? new GridLength(upperShown ? 1 - model.RightSplit : 1, GridUnitType.Star) : new GridLength(0);
        _right.RowDefinitions[0].MinHeight = _right.RowDefinitions[2].MinHeight = upperShown && lowerShown ? DockSizing.RightGroupMinHeight : 0;
    }

    /// <summary>I-215: each side dock is at least as wide as the widest tool bar of its windows.</summary>
    private static (double Left, double Right) SideMinimums(LayoutViewModel model) =>
        (DockSizing.SideMinWidth(model.Left.Windows), DockSizing.SideMinWidth(model.RightUpper.Windows.Concat(model.RightLower.Windows)));

    private static void SetLimits(ColumnDefinition column, bool shown, double min, double max)
    {
        column.MinWidth = shown ? min : 0;
        column.MaxWidth = shown ? max : double.PositiveInfinity;
    }

    private static void SetLimits(RowDefinition row, bool shown, double min, double max)
    {
        row.MinHeight = shown ? min : 0;
        row.MaxHeight = shown ? max : double.PositiveInfinity;
    }

    /// <summary>A drag ended: the dock keeps the new size, within the limits of UI-WIN-005.</summary>
    private void Store(DockSplitter which)
    {
        if (_model is not { } model)
        {
            return;
        }

        var splitter = Metric("FxcMetricSplitterHitArea");
        var splitters = (model.Left.IsCollapsed ? 0 : splitter) + (model.IsRightCollapsed ? 0 : splitter);
        var left = ColumnDefinitions[LeftColumn].ActualWidth;
        var right = ColumnDefinitions[RightColumn].ActualWidth;
        switch (which)
        {
            case DockSplitter.Left:
                model.LeftSize = DockSizing.ClampSide(left, Bounds.Width, right, splitters, SideMinimums(model).Left);
                break;
            case DockSplitter.Right:
                model.RightSize = DockSizing.ClampSide(right, Bounds.Width, left, splitters, SideMinimums(model).Right);
                break;
            case DockSplitter.Bottom:
                model.BottomSize = DockSizing.ClampBottom(_center.RowDefinitions[BottomRow].ActualHeight, _center.Bounds.Height, splitter);
                break;
            default:
                var upper = _right.RowDefinitions[0].ActualHeight;
                var lower = _right.RowDefinitions[2].ActualHeight;
                if (upper + lower > 0)
                {
                    model.RightSplit = DockSizing.ClampSplit(upper / (upper + lower), upper + lower + splitter, splitter);
                }

                break;
        }
    }

    private static ToolGroupView Group(string automationId)
    {
        var view = new ToolGroupView();
        AutomationProperties.SetAutomationId(view, automationId);
        return view;
    }

    private GridSplitter Splitter(GridResizeDirection direction, string dockId, DockSplitter which)
    {
        var splitter = new GridSplitter { ResizeDirection = direction, MinWidth = 0, MinHeight = 0, Cursor = new Cursor(direction == GridResizeDirection.Columns ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth) };
        AutomationProperties.SetAutomationId(splitter, AutomationIds.Splitter(dockId));
        splitter.DragCompleted += (_, e) =>
        {
            if (e.Vector != default)
            {
                Store(which); // a click without movement (e.g. the halves of a double-click) changes nothing
            }
        };
        splitter.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                _model?.ResetSize(which); // UI-WIN-005: double-click resets the dock to its default size
                e.Handled = true;         // and starts no drag (the reset moves the splitter under the pointer)
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        var thickness = Metric("FxcMetricSplitterHitArea");
        if (direction == GridResizeDirection.Columns)
        {
            splitter.Width = thickness;
        }
        else
        {
            splitter.Height = thickness;
        }

        splitter.Bind(GridSplitter.BackgroundProperty, splitter.GetResourceObservable("FxcBgWindowBrush"));
        return splitter;
    }

    private static void Place(Grid grid, Control child, int row = 0, int column = 0)
    {
        SetRow(child, row);
        SetColumn(child, column);
        grid.Children.Add(child);
    }

    private static double Metric(string key) => Application.Current?.FindResource(key) as double? ?? 0;
}
