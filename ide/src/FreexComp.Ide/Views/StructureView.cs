using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using FreexComp.Ide.Core.CodeModel;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Structure tool window (tool-windows.md §3): a flat list of the visible tree rows (<c>Tool.Structure.Row.&lt;key&gt;</c>,
/// error rows with the cells <c>.Message</c> and <c>.Position</c>), indented by depth with a chevron for expandable rows;
/// Left/Right collapse/expand, Enter and double-click run <c>structure.goToSource</c>; the tracked row is selected without
/// taking the focus. Empty state <c>Tool.Structure.Empty</c> (<c>Msg.Structure.Empty</c>). In the Design view
/// (UI-TW-207) the list allows several selected rows, kept equal to the surface selection; non-visual components carry
/// a gear overlay, unknown or read-only ones are drawn in <c>text.disabled</c> with a warning overlay; the rows show their
/// palette icons (UI-TW-1610) and can be dragged to re-parent or reorder the components (UI-TW-1611…1613): a press and a
/// 4 px move start the drag, a 2 px <c>accent</c> insertion line or a highlighted row (<c>bg.selected.inactive</c>,
/// <c>accent</c> border) shows the drop position, the "no" cursor an invalid one; Escape or a lost capture cancels.
/// </summary>
public sealed class StructureView : UserControl
{
    private const string Prefix = "Tool.Structure";
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Single };
    private readonly TextBlock _empty = new() { Text = Strings.Msg_Structure_Empty, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
    /// <summary>UI-TW-1611: the pointer distance (DIP) that turns a press on a row into a drag.</summary>
    private const double DragThreshold = 4;

    private Cursor? _noCursor;

    private readonly Canvas _dropLayer = new() { IsHitTestVisible = false };
    private readonly Border _dropLine = new() { Height = 2, IsVisible = false };
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<StructureRow, Border> _rowBorders = [];
    private StructureViewModel? _model;
    private bool _syncing;
    private (Point At, StructureRow Row, bool SelectOnRelease)? _press;
    private bool _dragging;
    private Border? _highlighted;
    private IDisposable[] _highlight = [];

    public StructureView()
    {
        AutomationProperties.SetAutomationId(_list, Prefix + ".List");
        AutomationProperties.SetName(_list, Strings.Cmd_view_structure);
        ToolWindowFrame.SetIsPrimary(_list, true);
        _list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(MinHeightProperty, 22.0) },
        });
        _list.ItemTemplate = new FuncDataTemplate<StructureRow>((row, _) => row is null ? new Border() : CreateRow(row), supportsRecycling: false);
        _list.SelectionChanged += (_, e) =>
        {
            if (_model is null || _syncing)
                return;
            if (_model.IsDesign)
            {
                // The row selected last becomes the primary selection on the surface.
                var added = e.AddedItems.OfType<StructureRow>().ToList();
                var rows = (_list.SelectedItems?.OfType<StructureRow>() ?? []).Except(added).Concat(added).ToList();
                _model.SelectRows(rows);
            }
            else
            {
                _model.SelectedRow = _list.SelectedItem as StructureRow;
            }
        };
        _list.DoubleTapped += async (_, _) =>
        {
            if (_model?.SelectedRow is { } row)
                await _model.GoToSourceAsync(row);
        };
        _list.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _list.ContextRequested += OnContextRequested;
        _list.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _list.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        _list.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        _list.PointerCaptureLost += (_, _) =>
        {
            if (_dragging)
                CancelDrag(); // UI-TW-1612
        };
        AutomationProperties.SetAutomationId(_empty, Prefix + ".Empty");
        _empty.Bind(TextBlock.ForegroundProperty, _empty.GetResourceObservable("FxcTextSecondaryBrush"));
        _dropLine.Bind(Border.BackgroundProperty, _dropLine.GetResourceObservable("FxcAccentDefaultBrush"));
        _dropLayer.Children.Add(_dropLine);
        Content = new Panel { Children = { _list, _dropLayer, _empty } };
        DataContextChanged += (_, _) => Attach(DataContext as StructureViewModel);
    }

    private void Attach(StructureViewModel? model)
    {
        if (_model is not null)
            _model.PropertyChanged -= OnModelChanged;
        _model = model;
        _list.ItemsSource = model?.Rows;
        if (model is not null)
            model.PropertyChanged += OnModelChanged;
        Sync();
    }

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StructureViewModel.SelectedRow) or nameof(StructureViewModel.SelectedRows)
            or nameof(StructureViewModel.IsEmpty) or nameof(StructureViewModel.IsDesign))
            Sync();
    }

    private void Sync()
    {
        _empty.IsVisible = _model?.IsEmpty != false;
        _list.IsVisible = !_empty.IsVisible;
        _syncing = true;
        if (_model is { IsDesign: true } design)
        {
            _list.SelectionMode = SelectionMode.Multiple;
            if (_list.SelectedItems is { } items)
            {
                items.Clear();
                foreach (var selected in design.SelectedRows)
                    items.Add(selected);
            }
        }
        else
        {
            _list.SelectionMode = SelectionMode.Single;
            _list.SelectedItem = _model?.SelectedRow;
        }

        _syncing = false;
        if (_model?.SelectedRow is { } row)
            _list.ScrollIntoView(row);
    }

    /// <summary>Design view rows use the designer context menu, verb group first (tool-windows.md §3, DSG-SUR-191).</summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_model?.BuildDesignContextMenu() is not { } items)
            return;
        _list.ContextMenu?.Close();
        var menu = MenuBarBuilder.CreateContextMenu(items);
        _list.ContextMenu = menu;
        menu.Open(_list);
        e.Handled = true;
    }

    /// <summary>
    /// UI-TW-1611: a left press on a row of the Design view may become a drag. A press on a row of a multi-selection keeps
    /// the selection (the drag moves all of it); without a drag the release then selects only that row.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _press = null;
        if (_model is not { IsDesign: true } model || !e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed || e.KeyModifiers != KeyModifiers.None)
            return;
        var at = e.GetPosition(_list);
        if (RowAt(at) is not { } hit)
            return;
        var keep = model.SelectedRows.Count > 1 && model.SelectedRows.Contains(hit.Row);
        _press = (at, hit.Row, keep);
        if (keep)
        {
            e.Handled = true; // the list would reduce the selection to this row
            _list.Focus();
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_model is not { } model || _press is not { } press)
            return;
        var at = e.GetPosition(_list);
        if (!_dragging)
        {
            if (Math.Abs(at.X - press.At.X) < DragThreshold && Math.Abs(at.Y - press.At.Y) < DragThreshold)
                return;
            if (!model.BeginDrag(press.Row))
            {
                _press = null;
                return;
            }

            _dragging = true;
            e.Pointer.Capture(_list);
        }

        e.Handled = true;
        var hit = RowAt(at);
        ShowDropTarget(model.DragOver(hit?.Row, hit?.Fraction ?? 0));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_model is not { } model || _press is not { } press)
            return;
        _press = null;
        if (!_dragging)
        {
            if (press.SelectOnRelease)
                model.SelectRows([press.Row]);
            return;
        }

        _dragging = false;
        e.Handled = true;
        e.Pointer.Capture(null);
        ShowDropTarget(null);
        model.Drop();
    }

    private void CancelDrag()
    {
        _dragging = false;
        _press = null;
        ShowDropTarget(null);
        _model?.CancelDrag();
    }

    /// <summary>The row under <paramref name="at"/> (list coordinates) and where in it (0 = top edge, 1 = bottom edge).</summary>
    private (StructureRow Row, double Fraction)? RowAt(Point at)
    {
        foreach (var container in _list.GetRealizedContainers())
        {
            if (!container.IsVisible || _list.ItemFromContainer(container) is not StructureRow row
                || container.TranslatePoint(default, _list) is not { } origin)
            {
                continue;
            }

            var height = container.Bounds.Height;
            if (height > 0 && at.Y >= origin.Y && at.Y < origin.Y + height)
                return (row, (at.Y - origin.Y) / height);
        }

        return null;
    }

    /// <summary>UI-TW-1611, 1612: the insertion line or the row highlight of <paramref name="target"/>, the "no" cursor when it is invalid.</summary>
    private void ShowDropTarget(StructureDropTarget? target)
    {
        foreach (var binding in _highlight)
            binding.Dispose();
        _highlight = [];
        _highlighted?.ClearValue(Border.BackgroundProperty);
        _highlighted?.SetValue(Border.BorderBrushProperty, Avalonia.Media.Brushes.Transparent);
        _highlighted = null;
        _dropLine.IsVisible = false;
        _list.Cursor = _dragging && target is not { IsValid: true } ? _noCursor ??= new Cursor(StandardCursorType.No) : null;
        if (!_dragging || target is not { IsValid: true } || _list.ContainerFromItem(target.Row) is not { } container
            || container.TranslatePoint(default, _dropLayer) is not { } origin)
        {
            return;
        }

        if (target.Position == FreexComp.Designer.Surface.StructureDropPosition.Into)
        {
            if (_rowBorders.TryGetValue(target.Row, out var border))
            {
                _highlighted = border;
                _highlight =
                [
                    border.Bind(Border.BackgroundProperty, border.GetResourceObservable("FxcBgSelectedInactiveBrush")),
                    border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("FxcAccentDefaultBrush")),
                ];
            }

            return;
        }

        var indent = 4 + 16 * target.Row.Depth;
        var y = target.Position == FreexComp.Designer.Surface.StructureDropPosition.Before ? origin.Y : origin.Y + container.Bounds.Height;
        Canvas.SetLeft(_dropLine, origin.X + indent);
        Canvas.SetTop(_dropLine, Math.Max(0, y - 1));
        _dropLine.Width = Math.Max(0, container.Bounds.Width - indent - 4);
        _dropLine.IsVisible = true;
    }

    /// <summary>The row frame highlighted as the "into" drop target (UI-TW-1611); for tests.</summary>
    internal Border? HighlightedRow => _highlighted;

    /// <summary>The insertion line (UI-TW-1611); for tests.</summary>
    internal Border DropLine => _dropLine;

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dragging && e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelDrag(); // UI-TW-1612
            return;
        }

        if (_model?.SelectedRow is not { } row || e.KeyModifiers != KeyModifiers.None)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await _model.GoToSourceAsync(row);
                break;
            case Key.Delete:
                e.Handled = _model.DeleteKey(); // DSG-SUR-238: reached only when edit.delete is disabled (UI-KEY-007)
                break;
            case Key.Right when row.HasChildren && !row.IsExpanded:
            case Key.Left when row.HasChildren && row.IsExpanded:
                e.Handled = true;
                _model.SetExpanded(row, e.Key == Key.Right);
                _list.Focus();
                break;
        }
    }

    /// <summary>UI-DSG-144: the root row's suffix, two spaces after the name (the resource itself has no leading gap).</summary>
    private static string InheritsSuffix(string ancestor) =>
        "  " + string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Designer_InheritsSuffix, ancestor);

    private Control CreateRow(StructureRow row)
    {
        var id = $"{Prefix}.Row.{row.Node.Key}";
        // 1 px less on every side: the frame around the row (UI-TW-1611) keeps the row at 22 px and the indent unchanged.
        var panel = new DockPanel { MinHeight = 20, Margin = new Thickness(3 + 16 * row.Depth, 0, 3, 0), Background = Avalonia.Media.Brushes.Transparent };
        AutomationProperties.SetAutomationId(panel, id);
        // UI-DSG-136, UI-DSG-143: an inherited object's screen-reader name names the frame or ancestor it comes from;
        // UI-DSG-144: the root row of a descendant names its ancestor.
        AutomationProperties.SetName(panel, row.Node.InheritedFrom is { } frame
            ? row.Node.Text + string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Designer_InheritedSuffix, frame)
            : row.Node.InheritsFrom is { } inherits
                ? row.Node.Text + InheritsSuffix(inherits)
                : row.Node.Text);
        var chevron = new Panel { Width = 14 };
        if (row.HasChildren)
        {
            chevron.Children.Add(new IdeIcon { IconName = row.IsExpanded ? "chevron-down" : "chevron-right", Width = 8, Height = 8 });
            chevron.PointerPressed += (_, e) =>
            {
                _model?.SetExpanded(row, !row.IsExpanded);
                e.Handled = true;
            };
        }

        DockPanel.SetDock(chevron, Avalonia.Controls.Dock.Left);
        panel.Children.Add(chevron);
        if (row.Node.Kind == StructureKind.Error)
        {
            var position = new TextBlock { Text = row.Node.Position, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            position.Bind(TextBlock.ForegroundProperty, position.GetResourceObservable("FxcTextTertiaryBrush"));
            AutomationProperties.SetAutomationId(position, id + ".Position");
            DockPanel.SetDock(position, Avalonia.Controls.Dock.Right);
            panel.Children.Add(position);
        }

        // UI-TW-203: the symbol-kind icon (16); UI-TW-1610: a component's palette icon (16 × 16, the generic glyph when it has
        // none or it cannot be read), the form root's form glyph.
        Control kind = row.Node.Kind == StructureKind.Component && !row.Node.IsDesignRoot
            ? PaletteIcon.FromFile(row.Node.PaletteIcon)
            : new IdeIcon { IconName = row.Node.IsDesignRoot ? "form" : SymbolIcons.For(row.Node.Kind), Classes = { "symbol" } };
        kind.Width = 16;
        kind.Height = 16;
        kind.VerticalAlignment = VerticalAlignment.Center;
        kind.Margin = new Thickness(0, 0, 4, 0);
        kind.IsHitTestVisible = false;
        DockPanel.SetDock(kind, Avalonia.Controls.Dock.Left);
        panel.Children.Add(kind);

        // UI-TW-201: the visibility overlay of a member; UI-TW-207: the gear overlay of a non-visual component, the warning
        // overlay of an unknown or read-only one.
        var lockIcon = SymbolIcons.VisibilityOverlay(row.Node.Visibility);
        foreach (var overlay in (ReadOnlySpan<(bool Shown, string? Icon, string Class)>)[(lockIcon is not null, lockIcon, "visibility"),
                     (row.Node.IsNonVisual, "settings", "overlay"), (row.Node.IsUnavailable, "warning", "overlay")])
        {
            if (!overlay.Shown)
                continue;
            var glyph = new IdeIcon { IconName = overlay.Icon, Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            glyph.Classes.Add(overlay.Class);
            DockPanel.SetDock(glyph, Avalonia.Controls.Dock.Left);
            panel.Children.Add(glyph);
        }

        var text = new TextBlock { Text = row.Node.Text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(row.Node.Kind == StructureKind.Error ? "FxcStatusErrorBrush"
            : row.Node.IsUnavailable ? "FxcTextDisabledBrush" : row.Node.InheritedFrom is not null ? "FxcTextSecondaryBrush" : "FxcTextPrimaryBrush"));
        // UI-DSG-144: the root row of a resolved descendant ends with "  (inherits TAncestor)" in text.secondary.
        if (row.Node.InheritsFrom is { } ancestor)
        {
            var suffix = new Avalonia.Controls.Documents.Run(InheritsSuffix(ancestor));
            suffix.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, text.GetResourceObservable("FxcTextSecondaryBrush"));
            text.Text = null;
            text.Inlines = [new Avalonia.Controls.Documents.Run(row.Node.Text), suffix];
        }

        if (row.Node.Kind == StructureKind.Error)
            AutomationProperties.SetAutomationId(text, id + ".Message");
        if (row.Node.Visibility is { } visibility)
            ToolTip.SetTip(text, visibility);
        panel.Children.Add(text);

        // UI-TW-1611: the frame of the "into" drop highlight.
        var rowFrame = new Border { Child = panel, BorderThickness = new Thickness(1), BorderBrush = Avalonia.Media.Brushes.Transparent };
        _rowBorders.AddOrUpdate(row, rowFrame);
        return rowFrame;
    }
}
