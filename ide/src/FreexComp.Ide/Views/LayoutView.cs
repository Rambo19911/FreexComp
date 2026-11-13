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
        _leftSplitter = Splitter(GridResizeDirection.Columns);
        _rightSplitter = Splitter(GridResizeDirection.Columns);
        _bottomSplitter = Splitter(GridResizeDirection.Rows);
        _rightSplit = Splitter(GridResizeDirection.Rows);

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

        _leftSplitter.DragCompleted += (_, _) => Store(m => m.LeftSize = ColumnDefinitions[LeftColumn].ActualWidth);
        _rightSplitter.DragCompleted += (_, _) => Store(m => m.RightSize = ColumnDefinitions[RightColumn].ActualWidth);
        _bottomSplitter.DragCompleted += (_, _) => Store(m => m.BottomSize = _center.RowDefinitions[BottomRow].ActualHeight);
        _rightSplit.DragCompleted += (_, _) => Store(m =>
        {
            var upper = _right.RowDefinitions[0].ActualHeight;
            var lower = _right.RowDefinitions[2].ActualHeight;
            m.RightSplit = upper + lower > 0 ? upper / (upper + lower) : m.RightSplit;
        });
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
                // The active document view; while none is open, the document area itself.
                _documents.GetVisualDescendants().OfType<InputElement>()
                    .FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible)?.Focus(NavigationMethod.Tab);
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
        var (left, right) = DockSizing.Widths(width, model.LeftSize, model.RightSize, leftShown, rightShown, splitter);
        var bottom = DockSizing.BottomHeight(height, model.BottomSize, bottomShown, splitter);

        ColumnDefinitions[LeftColumn].Width = new GridLength(left);
        ColumnDefinitions[RightColumn].Width = new GridLength(right);
        _center.RowDefinitions[BottomRow].Height = new GridLength(bottom);
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
    }

    private void Store(Action<LayoutViewModel> store)
    {
        if (_model is { } model)
        {
            store(model);
        }
    }

    private static ToolGroupView Group(string automationId)
    {
        var view = new ToolGroupView();
        AutomationProperties.SetAutomationId(view, automationId);
        return view;
    }

    private GridSplitter Splitter(GridResizeDirection direction)
    {
        var splitter = new GridSplitter { ResizeDirection = direction, MinWidth = 0, MinHeight = 0, Cursor = new Cursor(direction == GridResizeDirection.Columns ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth) };
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
