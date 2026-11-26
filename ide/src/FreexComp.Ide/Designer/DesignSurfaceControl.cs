using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Input;
using DesignRect = FreexComp.Designer.Rect;
using KeyModifiers = Avalonia.Input.KeyModifiers;
using Point = Avalonia.Point;

namespace FreexComp.Ide.Designer;

/// <summary>
/// The design surface of a form (ui/designer.md §2 … §7, design-surface.md; D-078): draws the
/// <see cref="DesignSurfaceController"/> through an <see cref="AvaloniaDesignCanvas"/>, adapts pointer, key and text input
/// to it, sets the cursor (UI-DSG-036, with the copy badge of a Ctrl-drag, DSG-SUR-057), measures to the surface extent so
/// a surrounding scroll viewer scrolls the backdrop (UI-DSG-003), brings the selection into view, follows the theme and
/// exposes the form, controls, tray and handles to automation. Its <c>DataContext</c> is the <see cref="FormDesignerViewModel"/>;
/// it is the command target of the <c>Designer</c> scope (edit and designer commands).
/// </summary>
public sealed class DesignSurfaceControl : Control, ICommandTarget
{
    /// <summary>UI-DSG-003: the margin right of and below the frame and tray.</summary>
    public const int SurfaceMargin = 16;

    private readonly Dictionary<StandardCursorType, Cursor> _cursors = [];

    private FormDesignerViewModel? _designer;
    private DesignSurfaceController? _surface;
    private DesignSurfaceCommands? _commands;
    private DesignSurfaceAutomationPeer? _peer;
    private string _guidelines = "";
    private Point _pointer;

    /// <summary>
    /// The keys the surface handles itself while focused (design-surface.md §9): the IDE's key routing leaves them to it
    /// although they are bound to <c>designer.*</c> commands (Escape cancels a drag before it selects the parent, Tab
    /// numbers in tab-order mode, Enter runs the default action).
    /// </summary>
    public static readonly IReadOnlyList<Core.Commands.KeyGesture> LocalKeys =
        [.. ((string[])["Escape", "Tab", "Shift+Tab", "Enter", "Ctrl+Shift+Up", "Ctrl+Shift+Down"]).Select(Core.Commands.KeyGesture.Parse)];

    public DesignSurfaceControl()
    {
        Focusable = true;
        ClipToBounds = true;
        CommandRouting.SetScope(this, CommandScope.Designer);
        CommandRouting.SetLocalKeys(this, LocalKeys);
        ActualThemeVariantChanged += (_, _) =>
        {
            UpdatePalette();
            InvalidateVisual();
        };
        // The surface opens its menu itself (UI-DSG-080, at the press); the toolkit's own request is not used.
        AddHandler(ContextRequestedEvent, (_, e) => e.Handled = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>The designer shown (the <c>DataContext</c>).</summary>
    public FormDesignerViewModel? Designer => _designer;

    /// <summary>The current surface controller (replaced with the model, DSG-FD-030).</summary>
    public DesignSurfaceController? Surface => _surface;

    /// <summary>Where the context menu was last opened, in control coordinates (null: never).</summary>
    public Point? ContextMenuPoint { get; private set; }

    /// <summary>The cursor kind last asked of the surface (UI-DSG-036).</summary>
    public CursorKind CursorKind { get; private set; }

    /// <summary>The cursor shows the copy badge: a move drag with Ctrl held (DSG-SUR-057).</summary>
    public bool ShowsCopyBadge { get; private set; }

    /// <summary>The name of the designed form (the root component), or "" without a model.</summary>
    public string FormName => _designer?.Model?.Root.Name ?? "";

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_designer is not null)
        {
            _designer.ModelReplaced -= OnModelReplaced;
            _designer.ModelChanged -= OnModelChanged;
            _designer.SelectionChanged -= OnSelectionChanged;
            _designer.DisplayChanged -= OnDisplayChanged;
            _designer.Host.ContextMenuRequested -= OnContextMenuRequested;
        }

        _designer = DataContext as FormDesignerViewModel;
        _commands = _designer is null ? null : new DesignSurfaceCommands(_designer);
        if (_designer is not null)
        {
            _designer.ModelReplaced += OnModelReplaced;
            _designer.ModelChanged += OnModelChanged;
            _designer.SelectionChanged += OnSelectionChanged;
            _designer.DisplayChanged += OnDisplayChanged;
            _designer.Host.ContextMenuRequested += OnContextMenuRequested;
        }

        Rebind();
    }

    private void OnModelReplaced(object? sender, EventArgs e) => Rebind();

    private void OnDisplayChanged(object? sender, EventArgs e) => Invalidate();

    /// <summary>designer.md §8: the context menu at a surface point (the press, or the primary's top-left + (8, 8) by keyboard).</summary>
    private void OnContextMenuRequested(object? sender, (double X, double Y) at)
    {
        if (_designer?.ContextMenu?.Invoke() is not { } items)
        {
            return;
        }

        ContextMenu?.Close();
        var menu = Views.MenuBarBuilder.CreateContextMenu(items);
        menu.Placement = PlacementMode.AnchorAndGravity;
        menu.PlacementRect = new Rect(at.X, at.Y, 1, 1);
        menu.PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft;
        menu.PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight;
        ContextMenu = menu;
        ContextMenuPoint = new Point(at.X, at.Y);
        menu.Open(this);
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        AutomationProperties.SetAutomationId(this, AutomationIds.Designer(FormName));
        _peer?.Refresh();
        Invalidate();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        _peer?.Refresh();
        BringSelectionIntoView();
    }

    /// <summary>Attaches to the designer's current surface controller.</summary>
    private void Rebind()
    {
        _surface?.Invalidated -= OnInvalidated;
        _surface = _designer?.Surface;
        _surface?.Invalidated += OnInvalidated;
        UpdatePalette();
        AutomationProperties.SetAutomationId(this, AutomationIds.Designer(FormName));
        _peer?.Refresh();
        Invalidate();
    }

    private void OnInvalidated(object? sender, EventArgs e)
    {
        // The guidelines of a drag are automation children (UI-DSG-131): they change with the drag, not with the model.
        var guidelines = _surface is null ? "" : string.Join(';', _surface.Guidelines.Select(l => $"{l.Kind}{l.Axis}{l.Coordinate}/{l.Spacing}"));
        if (guidelines != _guidelines)
        {
            _guidelines = guidelines;
            _peer?.Refresh();
        }

        Invalidate();
    }

    private void Invalidate()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }


    /// <summary>The IDE colours of the current theme; the form colours do not depend on it (design-tokens.md §3.3).</summary>
    private void UpdatePalette()
    {
        if (_surface is not null)
        {
            var variant = ActualThemeVariant;
            var dark = variant == ThemeVariant.Dark || variant?.InheritVariant == ThemeVariant.Dark;
            _surface.Palette = dark ? SurfacePalette.Dark : SurfacePalette.Light;
        }
    }

    /// <summary>UI-DSG-003: frame and tray plus the margin; the view stretches it to fill the backdrop.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_surface is null)
        {
            return default;
        }

        var g = _surface.Geometry;
        return new Size(Math.Max(g.FrameRect.Right, g.TrayRect.Right) + SurfaceMargin, Math.Max(g.FrameRect.Bottom, g.TrayRect.Bottom) + SurfaceMargin);
    }

    public override void Render(DrawingContext context)
    {
        if (_surface is null)
        {
            return;
        }

        var size = Bounds.Size;
        var viewport = new DesignRect(0, 0, (int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height));
        _surface.Render(new AvaloniaDesignCanvas(context, BrushFor), viewport);
    }

    private IBrush? BrushFor(string token) =>
        this.TryFindResource(IconCatalog.BrushKey(token), ActualThemeVariant, out var value) ? value as IBrush : null;

    // ---- pointer ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_surface is null)
        {
            return;
        }

        Focus(NavigationMethod.Pointer);
        var point = e.GetCurrentPoint(this);
        var button = point.Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.RightButtonPressed => PointerButton.Right,
            PointerUpdateKind.MiddleButtonPressed => PointerButton.Middle,
            _ => PointerButton.Left,
        };
        e.Pointer.Capture(this);
        Send(PointerKind.Down, point.Position, button, e.KeyModifiers, e.ClickCount);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_surface is null)
        {
            return;
        }

        Send(PointerKind.Move, e.GetPosition(this), PointerButton.None, e.KeyModifiers, 1);
        UpdateToolTip();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _surface?.PointerLeft(); // UI-DSG-084: no hovered tab
        ToolTip.SetTip(this, null);
        _toolTipFor = null;
    }

    private object? _toolTipFor;

    /// <summary>
    /// UI-DSG-083: over a tray item its class name (bodyStrong), plus "Double-click to edit" with a componentEditor.
    /// UI-DSG-142: over an inherited component of a descendant (tray item or control) a last line "Inherited from …".
    /// </summary>
    private void UpdateToolTip()
    {
        object? tip = _surface is { State: InteractionState.Idle } surface
            ? (object?)surface.GetTrayToolTip(_pointer.X, _pointer.Y) ?? surface.GetInheritedControlToolTip(_pointer.X, _pointer.Y)
            : null;
        if (Equals(tip, _toolTipFor))
        {
            return;
        }

        _toolTipFor = tip;
        ToolTip.SetTip(this, tip is null ? null : BuildToolTip(tip));
    }

    /// <summary>The tooltip content for a <see cref="TrayToolTip"/> or an <see cref="InheritedControlToolTip"/>.</summary>
    internal static StackPanel BuildToolTip(object tip)
    {
        var panel = new StackPanel();
        string? inheritedFrom = null;
        if (tip is TrayToolTip tray)
        {
            panel.Children.Add(new TextBlock { Text = tray.ClassName, Classes = { "bodyStrong" } });
            if (tray.OpensEditor)
            {
                panel.Children.Add(new TextBlock { Text = FreexComp.Ide.Core.Resources.Strings.Designer_TrayEditHint, Classes = { "body" } });
            }

            inheritedFrom = tray.InheritedFrom;
        }
        else if (tip is InheritedControlToolTip control)
        {
            panel.Children.Add(new TextBlock { Text = control.Name + ": " + control.ClassName, Classes = { "bodyStrong" } });
            inheritedFrom = control.InheritedFrom;
        }

        if (inheritedFrom is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, FreexComp.Ide.Core.Resources.Strings.Designer_InheritedFromTooltip, inheritedFrom),
                Classes = { "body" },
            });
        }

        return panel;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_surface is null)
        {
            return;
        }

        var button = e.InitialPressMouseButton switch
        {
            MouseButton.Right => PointerButton.Right,
            MouseButton.Middle => PointerButton.Middle,
            _ => PointerButton.Left,
        };
        Send(PointerKind.Up, e.GetPosition(this), button, e.KeyModifiers, 1);
        if (e.Pointer.Captured == this)
        {
            e.Pointer.Capture(null);
        }

        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_surface is { State: InteractionState.PressPending or InteractionState.Moving or InteractionState.Resizing or InteractionState.Lasso or InteractionState.Placing })
        {
            Send(PointerKind.CaptureLost, _pointer, PointerButton.None, KeyModifiers.None, 1);
        }
    }

    private void Send(PointerKind kind, Point position, PointerButton button, KeyModifiers modifiers, int clickCount)
    {
        _pointer = position;
        _surface!.OnPointer(new PointerInput(kind, position.X, position.Y, button, ToDesign(modifiers), clickCount));
        UpdateCursor();
    }

    /// <summary>UI-DSG-036 cursors; the copy badge (DSG-SUR-057) as the copy drag cursor.</summary>
    private void UpdateCursor()
    {
        if (_surface is null)
        {
            return;
        }

        CursorKind = _surface.GetCursor(_pointer.X, _pointer.Y);
        ShowsCopyBadge = _surface.State == InteractionState.Moving && _surface.CopyBadge;
        Cursor = CursorOf(ShowsCopyBadge ? StandardCursorType.DragCopy : CursorKind switch
        {
            CursorKind.SizeAll => StandardCursorType.SizeAll,
            CursorKind.SizeNS => StandardCursorType.SizeNorthSouth,
            CursorKind.SizeWE => StandardCursorType.SizeWestEast,
            CursorKind.SizeNWSE => StandardCursorType.TopLeftCorner,
            CursorKind.SizeNESW => StandardCursorType.TopRightCorner,
            CursorKind.Cross => StandardCursorType.Cross,
            _ => StandardCursorType.Arrow,
        });
    }

    private Cursor CursorOf(StandardCursorType type)
    {
        if (!_cursors.TryGetValue(type, out var cursor))
        {
            _cursors[type] = cursor = new Cursor(type);
        }

        return cursor;
    }

    // ---- keyboard ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_surface is null || e.Handled)
        {
            return;
        }
        if (e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift))
        {
            // designer.md §8: by keyboard at the primary selection's top-left + (8, 8).
            var b = _surface.Geometry.GetBounds(_surface.Selection.Primary);
            _designer!.Host.ShowContextMenu(b.Left + 8, b.Top + 8);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.LeftCtrl or Key.RightCtrl && _surface.State == InteractionState.Moving)
        {
            Send(PointerKind.Move, _pointer, PointerButton.None, e.KeyModifiers, 1); // the copy badge follows Ctrl at once
            return;
        }

        DesignKey? key = e.Key switch
        {
            Key.Left => DesignKey.Left,
            Key.Right => DesignKey.Right,
            Key.Up => DesignKey.Up,
            Key.Down => DesignKey.Down,
            Key.Tab => DesignKey.Tab,
            Key.Enter => DesignKey.Enter,
            Key.Escape => DesignKey.Escape,
            Key.Delete => DesignKey.Delete,
            Key.Space => DesignKey.Space,
            _ => null,
        };
        if (key is { } k && _surface.OnKey(new KeyInput(k, ToDesign(e.KeyModifiers))))
        {
            e.Handled = true;
            UpdateCursor();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl && _surface is { State: InteractionState.Moving })
        {
            Send(PointerKind.Move, _pointer, PointerButton.None, e.KeyModifiers, 1);
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_surface is not null && !e.Handled && e.Text is { Length: > 0 } text && _surface.OnTextInput(text))
        {
            e.Handled = true;
        }
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (_designer?.Host.Clipboard is SystemDesignerClipboard clipboard)
        {
            _ = clipboard.RefreshAsync(); // another program may have copied form text (DSG-SUR-120)
        }
    }

    private static InputModifiers ToDesign(KeyModifiers m) =>
        (m.HasFlag(KeyModifiers.Control) ? InputModifiers.Ctrl : 0)
        | (m.HasFlag(KeyModifiers.Shift) ? InputModifiers.Shift : 0)
        | (m.HasFlag(KeyModifiers.Alt) ? InputModifiers.Alt : 0);

    /// <summary>
    /// DSG-SUR-030: the primary selection scrolls into view by the minimum distance, with an 8 DIP margin (aligned at its
    /// top-left when larger than the view) — for a selection from outside (Object Inspector, Structure, Messages) and
    /// equally for one made on the surface (keyboard selection of a control outside the view).
    /// </summary>
    private void BringSelectionIntoView()
    {
        if (_surface is not { } surface || surface.Selection.Items.Count == 0 || this.FindAncestorOfType<ScrollViewer>() is not { } scroller)
        {
            return;
        }

        var view = new SurfaceViewport(scroller.Offset.X, scroller.Offset.Y, scroller.Viewport.Width, scroller.Viewport.Height);
        var (x, y) = SurfaceScroll.EnsureVisible(view, surface.Geometry.GetBounds(surface.Selection.Primary));
        if ((x, y) != (view.OffsetX, view.OffsetY))
        {
            scroller.Offset = new Vector(x, y);
        }
    }

    // ---- commands ----

    public bool CanExecute(string commandId, string? argument) => _commands?.CanExecute(commandId, argument) ?? false;

    public Task<bool> ExecuteAsync(string commandId, string? argument) => _commands?.ExecuteAsync(commandId, argument) ?? Task.FromResult(false);

    // ---- automation ----

    protected override AutomationPeer OnCreateAutomationPeer() => _peer = new DesignSurfaceAutomationPeer(this);
}
