using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommandScope = FreexComp.Ide.Core.Commands.CommandScope;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Input;
using KeyGesture = FreexComp.Ide.Core.Commands.KeyGesture;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace FreexComp.Ide.Views;

/// <summary>
/// Content of the Tool Palette tool window (ui/designer.md §12, tool-palette.md; D-083): the search box
/// (<c>Tool.ToolPalette.Filter</c>, the primary control, UI-TW-351), then the pages with their headers
/// (<c>.Page.&lt;PageName&gt;</c>) and items (<c>.Item.&lt;ClassName&gt;</c>) as a list or as icon cells, the search
/// highlight, the armed and sticky visuals, the keyboard of §6, the palette drag onto the design surface (DSG-TP-060) and
/// the empty states. Built in code; the logic is in <see cref="ToolPaletteViewModel"/>.
/// </summary>
public sealed class ToolPaletteView : UserControl
{
    private const string Prefix = "Tool.ToolPalette";
    private const double DragThreshold = 4; // DSG-TP-060

    /// <summary>The keys the list handles itself although they are bound to commands (search focus, Escape order of §6, context menu).</summary>
    private static readonly IReadOnlyList<KeyGesture> LocalKeys =
        [.. ((string[])["Ctrl+F", "Escape", "Shift+F10"]).Select(KeyGesture.Parse)];

    private readonly TextBox _search = new()
    {
        Height = 26, MinHeight = 26, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(4),
    };

    private readonly Button _clear;
    private readonly ListHost _list = new() { Focusable = true };
    private readonly StackPanel _rows = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 24, 8, 0) };
    private readonly TextBlock _filterEmpty = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 24, 8, 0) };
    private readonly Dictionary<string, Control> _rowControls = new(StringComparer.Ordinal);
    private ToolPaletteViewModel? _model;
    private bool _building;
    private bool _rebuildPending;
    private bool _rebuildAfterRelease;

    // The pressed item (palette drag, DSG-TP-060).
    private PaletteItem? _pressed;
    private Point _pressPoint;
    private bool _dragging;
    private bool _pressWasDouble;
    private KeyModifiers _pressModifiers;

    public ToolPaletteView()
    {
        AutomationProperties.SetAutomationId(_search, Prefix + ".Filter");
        AutomationProperties.SetName(_search, Strings.ToolPalette_SearchPlaceholder);
        _search.PlaceholderText = Strings.ToolPalette_SearchPlaceholder;
        ToolWindowFrame.SetIsPrimary(_search, true); // UI-TW-351
        _search.InnerLeftContent = new IdeIcon { IconName = "find", Width = 16, Height = 16, Margin = new Thickness(6, 0, 0, 0), IsHitTestVisible = false };
        _clear = new Button
        {
            Width = 20, Height = 20, Padding = new Thickness(0), Focusable = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Content = new IdeIcon { IconName = "close", Width = 16, Height = 16 }, IsVisible = false,
        };
        AutomationProperties.SetAutomationId(_clear, Prefix + ".Filter.Clear");
        AutomationProperties.SetName(_clear, Strings.ToolWindow_ClearFilter);
        ToolTip.SetTip(_clear, Strings.ToolWindow_ClearFilter);
        _clear.Click += (_, _) =>
        {
            _model?.SearchText = "";
            _search.Focus();
        };
        _search.InnerRightContent = _clear;
        _search.TextChanged += (_, _) =>
        {
            if (!_building && _model is { } model)
            {
                model.SearchText = _search.Text ?? "";
            }

            _clear.IsVisible = !string.IsNullOrEmpty(_search.Text);
        };
        _search.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);

        AutomationProperties.SetAutomationId(_list, Prefix + ".List");
        AutomationProperties.SetName(_list, Strings.Cmd_view_toolPalette);
        CommandRouting.SetScope(_list, CommandScope.ToolList);
        CommandRouting.SetToolWindow(_list, ToolPaletteViewModel.ToolName);
        CommandRouting.SetLocalKeys(_list, LocalKeys);
        _scroll = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _list.Child = _scroll;
        _list.Background = Brushes.Transparent;
        _list.KeyDown += OnListKeyDown;
        _list.TextInput += OnListTextInput;
        _list.GotFocus += (_, _) =>
        {
            if (_model is { Current: null } model)
            {
                model.MoveCurrent(0);
            }

            UpdateStates();
        };
        _list.LostFocus += (_, _) => UpdateStates();

        AutomationProperties.SetAutomationId(_empty, Prefix + ".Empty");
        AutomationProperties.SetAutomationId(_filterEmpty, Prefix + ".FilterEmpty");
        foreach (var text in (TextBlock[])[_empty, _filterEmpty])
        {
            text.Classes.Add("body");
            Bind(text, TextBlock.ForegroundProperty, "FxcTextTertiaryBrush");
            text.IsHitTestVisible = false;
        }

        var layout = new DockPanel();
        DockPanel.SetDock(_search, Avalonia.Controls.Dock.Top);
        layout.Children.Add(_search);
        layout.Children.Add(new Panel { Children = { _list, _empty, _filterEmpty } });
        Content = layout;
    }

    /// <summary>The control of a row (<see cref="PaletteRow.Key"/>), for tests.</summary>
    internal Control? RowControl(string key) => _rowControls.GetValueOrDefault(key);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelPropertyChanged;
            _model.SearchFocusRequested -= OnSearchFocusRequested;
        }

        _model = DataContext as ToolPaletteViewModel;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelPropertyChanged;
            _model.SearchFocusRequested += OnSearchFocusRequested;
        }

        Rebuild();
    }

    private void OnSearchFocusRequested(object? sender, EventArgs e) => FocusSearch();

    /// <summary>DSG-TP-030: the search box gets focus with its text selected.</summary>
    private void FocusSearch()
    {
        _search.Focus(NavigationMethod.Tab);
        _search.SelectAll();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_rebuildPending)
        {
            return;
        }

        // A change raises several properties at once: one rebuild for all of them.
        _rebuildPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildPending = false;
            if (_pressed is not null)
            {
                // Replacing the pressed row would lose its pointer capture: rebuild once the press ends.
                _rebuildAfterRelease = true;
                UpdateStates();
                return;
            }

            Rebuild();
        });
    }

    private void Rebuild()
    {
        _building = true;
        try
        {
            var model = _model;
            var hasForm = model?.HasForm == true;
            if (model is not null && !string.Equals(_search.Text ?? "", model.SearchText, StringComparison.Ordinal))
            {
                _search.Text = model.SearchText;
                _search.CaretIndex = model.SearchText.Length;
            }

            _clear.IsVisible = !string.IsNullOrEmpty(_search.Text);

            // UI-DSG-125: without a Design view the list is replaced by the empty state and the search box is disabled.
            _search.IsEnabled = hasForm;
            _empty.Text = model?.NoFormText ?? Strings.Msg_Palette_NoForm;
            _empty.IsVisible = !hasForm;
            _filterEmpty.IsVisible = hasForm && model!.IsFilterEmpty;
            _filterEmpty.Text = model?.FilterEmptyText ?? "";
            _list.IsVisible = hasForm;
            _rows.Children.Clear();
            _rowControls.Clear();
            if (model is null || !hasForm)
            {
                return;
            }

            foreach (var page in model.Pages)
            {
                _rows.Children.Add(Header(page));
                if (!page.IsExpanded)
                {
                    continue;
                }

                if (model.IsIconView)
                {
                    var cells = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2) };
                    foreach (var item in page.Items)
                    {
                        cells.Children.Add(IconCell(model, item));
                    }

                    _rows.Children.Add(cells);
                }
                else
                {
                    foreach (var item in page.Items)
                    {
                        _rows.Children.Add(ListItem(model, item));
                    }
                }
            }

            UpdateStates();
        }
        finally
        {
            _building = false;
        }
    }

    /// <summary>The current row's focus ring while the list has focus, and the current row scrolled into view.</summary>
    private void UpdateStates()
    {
        if (_model is not { } model)
        {
            return;
        }

        var current = model.Current?.Key;
        foreach (var (key, control) in _rowControls)
        {
            var isCurrent = key == current;
            control.Classes.Set("current", isCurrent);
            if (control is Border border)
            {
                border.BorderBrush = isCurrent && _list.IsKeyboardFocusWithin ? Resource("FxcBorderFocusBrush") : border.Tag as IBrush;
            }
        }

        if (current is not null && _rowControls.TryGetValue(current, out var row))
        {
            row.BringIntoView();
        }
    }

    // ---- rows ----

    /// <summary>UI-DSG-121: chevron, page name (bodyStrong), item count right-aligned (caption, text.tertiary); bg.header.</summary>
    private Border Header(PalettePage page)
    {
        var chevron = new IdeIcon { IconName = page.IsExpanded ? "chevron-down" : "chevron-right", Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        var name = new TextBlock { Text = page.Name, VerticalAlignment = VerticalAlignment.Center, Classes = { "bodyStrong" } };
        var count = new TextBlock { Text = page.Items.Count.ToString(System.Globalization.CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Classes = { "caption" } };
        Bind(count, TextBlock.ForegroundProperty, "FxcTextTertiaryBrush");
        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(chevron, Avalonia.Controls.Dock.Left);
        DockPanel.SetDock(count, Avalonia.Controls.Dock.Right);
        panel.Children.Add(chevron);
        panel.Children.Add(count);
        panel.Children.Add(name);
        var key = new PaletteRow(page, null).Key;
        var row = new Border { Height = 22, Child = panel, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Tag = Brushes.Transparent };
        Bind(row, Border.BackgroundProperty, "FxcBgHeaderBrush");
        AutomationProperties.SetAutomationId(row, Prefix + "." + key);
        AutomationProperties.SetName(row, page.Name);
        row.PointerPressed += (_, e) =>
        {
            if (_model is not { } model || !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                return;
            }

            model.SetCurrent(key);
            model.TogglePage(page.Name); // DSG-TP-022: a click on the header
            _list.Focus(NavigationMethod.Pointer);
            e.Handled = true;
        };
        _rowControls[key] = row;
        return row;
    }

    /// <summary>UI-DSG-122, 123: row 28, icon 24 at left padding 8, gap 6, class name; armed: bg.selected + 2 px accent bar; sticky: pin.</summary>
    private Border ListItem(ToolPaletteViewModel model, PaletteItem item)
    {
        var armed = string.Equals(model.ArmedClass, item.ClassName, StringComparison.OrdinalIgnoreCase);
        var icon = Icon(model, item);
        icon.Margin = new Thickness(0, 0, 6, 0);
        var name = ClassText(model, item);
        var panel = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 0, 4, 0) };
        DockPanel.SetDock(icon, Avalonia.Controls.Dock.Left);
        panel.Children.Add(icon);
        if (armed && model.IsSticky)
        {
            var pin = Pin();
            DockPanel.SetDock(pin, Avalonia.Controls.Dock.Right);
            panel.Children.Add(pin);
        }

        panel.Children.Add(name);
        var bar = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = armed, IsHitTestVisible = false };
        Bind(bar, Border.BackgroundProperty, "FxcAccentDefaultBrush");
        var row = new Border { Height = 28, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Tag = Brushes.Transparent, Child = new Panel { Children = { panel, bar } } };
        return Item(model, item, row, armed);
    }

    /// <summary>UI-DSG-122, 123: a 32 × 32 cell with the 24 icon centred, tooltip; armed: bg.selected + 2 px accent outline; sticky: pin top-right.</summary>
    private Border IconCell(ToolPaletteViewModel model, PaletteItem item)
    {
        var armed = string.Equals(model.ArmedClass, item.ClassName, StringComparison.OrdinalIgnoreCase);
        var icon = Icon(model, item);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var content = new Panel { Children = { icon } };
        if (armed && model.IsSticky)
        {
            var pin = Pin();
            pin.HorizontalAlignment = HorizontalAlignment.Right;
            pin.VerticalAlignment = VerticalAlignment.Top;
            content.Children.Add(pin);
        }

        var accent = Resource("FxcAccentDefaultBrush");
        var outline = armed ? accent : Brushes.Transparent;
        var cell = new Border { Width = 32, Height = 32, BorderThickness = new Thickness(armed ? 2 : 1), BorderBrush = outline, Tag = outline, Child = content };
        return Item(model, item, cell, armed);
    }

    /// <summary>The common part of a list row and an icon cell: id, states, tooltip and the pointer.</summary>
    private Border Item(ToolPaletteViewModel model, PaletteItem item, Border row, bool armed)
    {
        var key = new PaletteRow(new PalettePage("", true, []), item).Key;
        AutomationProperties.SetAutomationId(row, Prefix + "." + key);
        AutomationProperties.SetName(row, item.ClassName);
        if (armed)
        {
            row.Classes.Add("armed");
            Bind(row, Border.BackgroundProperty, "FxcBgSelectedBrush");
        }
        else
        {
            row.Background = Brushes.Transparent;
            row.PointerEntered += (_, _) => row.Background = model.AreItemsEnabled ? Resource("FxcBgHoverBrush") : Brushes.Transparent;
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        }

        if (!model.AreItemsEnabled)
        {
            row.Classes.Add("disabled");
        }

        // UI-DSG-126: TClassName (bodyStrong), then "unit UnitName" (caption, text.secondary), after 600 ms.
        var unit = new TextBlock { Text = ToolPaletteViewModel.UnitText(item), Classes = { "caption" } };
        Bind(unit, TextBlock.ForegroundProperty, "FxcTextSecondaryBrush");
        ToolTip.SetTip(row, new StackPanel { Children = { new TextBlock { Text = item.ClassName, Classes = { "bodyStrong" } }, unit } });
        ToolTip.SetShowDelay(row, 600);

        row.PointerPressed += (_, e) => OnItemPressed(row, item, key, e);
        row.PointerMoved += (_, e) => OnItemMoved(row, item, e);
        row.PointerReleased += (_, e) => OnItemReleased(row, item, e);
        row.PointerCaptureLost += (_, _) =>
        {
            if (_dragging)
            {
                _model?.DragLeave();
            }

            _pressed = null;
            _dragging = false;
            RebuildIfDeferred();
        };
        _rowControls[key] = row;
        return row;
    }

    private PaletteIcon Icon(ToolPaletteViewModel model, PaletteItem item)
    {
        var bytes = model.ReadIcon(item);
        return new PaletteIcon(bytes, () => model.ReportIconFailure(item.Icon ?? item.ClassName))
        {
            Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center, IsEnabled = model.AreItemsEnabled, IsHitTestVisible = false,
        };
    }

    /// <summary>The class name; UI-DSG-124: the matched parts in bodyStrong; disabled items in text.disabled.</summary>
    private TextBlock ClassText(ToolPaletteViewModel model, PaletteItem item)
    {
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "body" } };
        Bind(text, TextBlock.ForegroundProperty, model.AreItemsEnabled ? "FxcTextPrimaryBrush" : "FxcTextDisabledBrush");
        var name = item.ClassName;
        var at = 0;
        foreach (var (start, length) in model.Highlights(name))
        {
            if (start > at)
            {
                text.Inlines!.Add(new Run(name[at..start]));
            }

            text.Inlines!.Add(new Run(name.Substring(start, length)) { FontWeight = FontWeight.SemiBold });
            at = start + length;
        }

        if (at < name.Length)
        {
            text.Inlines!.Add(new Run(name[at..]));
        }

        return text;
    }

    private static IdeIcon Pin() =>
        new() { IconName = "pin", Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };

    // ---- pointer ----

    /// <summary>
    /// DSG-TP-040 … 042: a click (press and release without a drag) arms (Shift: sticky); a double-click places at the
    /// default position; a right-click opens the context menu. The press starts a possible palette drag.
    /// </summary>
    private void OnItemPressed(Border row, PaletteItem item, string key, PointerPressedEventArgs e)
    {
        if (_model is not { } model)
        {
            return;
        }

        model.SetCurrent(key);
        _list.Focus(NavigationMethod.Pointer);
        var point = e.GetCurrentPoint(row);
        e.Handled = true;
        if (point.Properties.IsRightButtonPressed)
        {
            OpenContextMenu(row);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            _pressWasDouble = true;
            _pressed = null;
            model.Place(item.ClassName);
            return;
        }

        _pressWasDouble = false;
        _pressed = item;
        _dragging = false;
        _pressModifiers = e.KeyModifiers;
        _pressPoint = e.GetPosition(TopLevel.GetTopLevel(this));
        e.Pointer.Capture(row);
    }

    private void RebuildIfDeferred()
    {
        if (_rebuildAfterRelease && _pressed is null)
        {
            _rebuildAfterRelease = false;
            OnModelPropertyChanged(this, new PropertyChangedEventArgs(null));
        }
    }

    private void OnItemMoved(Border row, PaletteItem item, PointerEventArgs e)
    {
        if (_model is not { } model || !ReferenceEquals(_pressed, item))
        {
            return;
        }

        var at = e.GetPosition(TopLevel.GetTopLevel(this));
        if (!_dragging && (Math.Abs(at.X - _pressPoint.X) >= DragThreshold || Math.Abs(at.Y - _pressPoint.Y) >= DragThreshold))
        {
            _dragging = true;
        }

        if (!_dragging)
        {
            return;
        }

        if (SurfaceAt(at) is { } hit)
        {
            model.DragOver(item.ClassName, hit.Point.X, hit.Point.Y);
        }
        else
        {
            model.DragLeave();
        }
    }

    private void OnItemReleased(Border row, PaletteItem item, PointerReleasedEventArgs e)
    {
        if (_model is not { } model || e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        e.Handled = true;
        var pressed = _pressed;
        var dragging = _dragging;
        _pressed = null;
        _dragging = false;
        if (e.Pointer.Captured == row)
        {
            e.Pointer.Capture(null);
        }

        RebuildIfDeferred();

        if (_pressWasDouble || !ReferenceEquals(pressed, item))
        {
            _pressWasDouble = false;
            return;
        }

        if (!dragging)
        {
            model.Click(item.ClassName, _pressModifiers.HasFlag(KeyModifiers.Shift));
            return;
        }

        // DSG-TP-060: released over the surface = armed and clicked there; elsewhere nothing.
        if (SurfaceAt(e.GetPosition(TopLevel.GetTopLevel(this))) is { } hit)
        {
            model.Drop(item.ClassName, hit.Point.X, hit.Point.Y);
        }
        else
        {
            model.DragLeave();
        }
    }

    /// <summary>The visible design surface under a point of the window, and the point in its coordinates.</summary>
    private (DesignSurfaceControl Surface, Point Point)? SurfaceAt(Point windowPoint)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return null;
        }

        foreach (var surface in top.GetVisualDescendants().OfType<DesignSurfaceControl>().Where(s => s.IsEffectivelyVisible))
        {
            if (top.TranslatePoint(windowPoint, surface) is not { } p || p.X < 0 || p.Y < 0 || p.X >= surface.Bounds.Width || p.Y >= surface.Bounds.Height)
            {
                continue;
            }

            // Only the part the surrounding scroll viewer shows.
            if (surface.FindAncestorOfType<ScrollViewer>() is { } viewer && top.TranslatePoint(windowPoint, viewer) is { } v
                && (v.X < 0 || v.Y < 0 || v.X >= viewer.Bounds.Width || v.Y >= viewer.Bounds.Height))
            {
                continue;
            }

            return (surface, p);
        }

        return null;
    }

    // ---- keyboard ----

    /// <summary>DSG-TP-033: Down → the list; Enter → place the first match and clear the search; Escape → §6 order.</summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is not { } model || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                if (model.Current is null)
                {
                    model.MoveCurrent(0);
                }

                _list.Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
            case Key.Enter:
                model.PlaceFirstMatch();
                e.Handled = true;
                break;
            case Key.Escape:
                model.EscapeKey();
                e.Handled = true;
                break;
        }
    }

    /// <summary>§6: arrows move the current row (icon view: across the cells), Left/Right collapse/expand a header, Space arms.</summary>
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is not { } model)
        {
            return;
        }

        var current = model.Current;
        var shift = e.KeyModifiers == KeyModifiers.Shift;
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F)
        {
            FocusSearch(); // toolPalette.focusSearch
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.F10 && shift) || (e.Key == Key.Apps && e.KeyModifiers == KeyModifiers.None))
        {
            OpenContextMenu(current is { } c && _rowControls.TryGetValue(c.Key, out var row) ? row : _list);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && (e.KeyModifiers == KeyModifiers.None || shift))
        {
            model.ArmCurrent(sticky: shift);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                model.MoveCurrent(-1);
                break;
            case Key.Down:
                model.MoveCurrent(1);
                break;
            case Key.Left when current is { IsPage: true } page:
                if (page.Page.IsExpanded)
                {
                    model.TogglePage(page.Page.Name);
                }

                break;
            case Key.Right when current is { IsPage: true } page:
                if (!page.Page.IsExpanded)
                {
                    model.TogglePage(page.Page.Name);
                }

                break;
            case Key.Left when model.IsIconView:
                model.MoveCurrent(-1);
                break;
            case Key.Right when model.IsIconView:
                model.MoveCurrent(1);
                break;
            case Key.Escape:
                model.EscapeKey();
                break;
            default:
                return;
        }

        e.Handled = true;
        UpdateStates();
    }

    /// <summary>§6: typing in the list searches — focus moves to the search box and the character is appended.</summary>
    private void OnListTextInput(object? sender, TextInputEventArgs e)
    {
        if (_model is not { HasForm: true } model || string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl) || e.Text == " ")
        {
            return;
        }

        model.SearchText += e.Text;
        _search.Focus(NavigationMethod.Tab);
        Rebuild();
        _search.CaretIndex = _search.Text?.Length ?? 0;
        e.Handled = true;
    }

    private void OpenContextMenu(Control anchor)
    {
        if (_model?.ContextMenu?.Invoke() is not { } items)
        {
            return;
        }

        var menu = MenuBarBuilder.CreateContextMenu(items);
        AutomationProperties.SetAutomationId(menu, Prefix + ".ContextMenu");
        _list.ContextMenu = menu;
        menu.Open(anchor);
    }

    private IBrush Resource(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Transparent;

    private static void Bind(Control control, AvaloniaProperty property, string resource) =>
        control.Bind(property, control.GetResourceObservable(resource));

    /// <summary>The focusable list area (the keyboard target of §6).</summary>
    private sealed class ListHost : Border;
}
