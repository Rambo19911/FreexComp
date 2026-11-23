using System.Globalization;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;

namespace FreexComp.Designer.Surface;

/// <summary>Drawing the design surface (design-surface.md §14, DSG-SUR-180 … 182; ui/designer.md §2 … §7, §10).</summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>UI-DSG-061: the text of an empty tray.</summary>
    public const string EmptyTrayText = "Non-visual components appear here";

    /// <summary><c>type.caption</c> (11 px, Segoe UI): tray names, the empty-tray text, tab-order badges (bold).</summary>
    private static readonly TextStyle CaptionFont = new("Segoe UI", 11, false, false, false, false);

    /// <summary><c>type.codeSmall</c> (12 px, <c>font.mono</c>): the drag tip (UI-DSG-037).</summary>
    private static readonly TextStyle TipFont = new("Cascadia Mono", 12, false, false, false, false);

    /// <summary>The order the handles are drawn in (UI-DSG-030).</summary>
    private static readonly HandleKind[] HandleDrawOrder =
        [HandleKind.NW, HandleKind.N, HandleKind.NE, HandleKind.E, HandleKind.SE, HandleKind.S, HandleKind.SW, HandleKind.W];

    // The last pointer position of a move or resize drag (the drag tip, UI-DSG-037).
    private (int X, int Y) _pointerAt;

    /// <summary>The IDE colours of the current theme (design-tokens.md §3.3); the IDE sets it on a theme switch.</summary>
    public SurfacePalette Palette { get; set; } = SurfacePalette.Light;

    /// <summary>During a move drag: Ctrl is held, so the drop copies (DSG-SUR-057); the view adds the copy badge to the cursor.</summary>
    public bool CopyBadge { get; private set; }

    /// <summary>
    /// DSG-SUR-180: draws the surface in <paramref name="viewport"/> (surface coordinates): backdrop; the form frame; per
    /// container its background, grid dots and children back to front (each clipped to the parent's client rect) with the
    /// state overlays; the tray; then the overlay layer. DSG-SUR-181: side-effect free, from the layout of the current model
    /// version (or the drag preview).
    /// </summary>
    public void Render(IDesignCanvas canvas, Rect viewport)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var (_, layout, g, hitTester) = Current;
        var p = Palette;
        canvas.FillRect(viewport, p.Backdrop);
        using var clip = canvas.PushClip(viewport);
        var root = Document.Root;
        var grid = _settings.ShowGrid && State != InteractionState.TabOrder && !IsReadOnly;

        if (layout.HasBounds(root))
        {
            using (canvas.PushTranslate(g.FrameRect.Left, g.FrameRect.Top))
            {
                DrawComponent(canvas, RenderContext.FromLayout(root, layout, p.Renderers), root);
            }

            DrawContainer(canvas, g, root, viewport, grid);
        }

        DrawTray(canvas, g, p);

        if (DropTarget is { } target)
        {
            canvas.DrawRect(g.GetBounds(target), p.DropTarget, 2);
        }

        if (State != InteractionState.TabOrder && Selection.Items.Count > 0)
        {
            var items = Selection.Items;
            for (var i = 0; i < items.Count - 1; i++)
            {
                DrawSelection(canvas, g, hitTester, items[i], primary: false, sole: false);
            }

            DrawSelection(canvas, g, hitTester, items[^1], primary: true, sole: items.Count == 1);
        }

        DrawGuidelines(canvas, g, p);

        if (LassoRect is { } lasso)
        {
            using (canvas.PushClip(_lassoContainer is { } container ? g.GetClientRect(container) : Inner(g.TrayRect)))
            {
                canvas.FillRect(lasso, p.RubberBandFill);
                canvas.DrawRect(lasso, p.RubberBandBorder, 1, LineStyle.Dash);
            }
        }

        if (PlacementRect is { } placement)
        {
            canvas.DrawRect(placement, p.RubberBandBorder, 1, LineStyle.Dash); // DSG-TP-045, 060: placement drag and palette ghost
        }

        if (State == InteractionState.TabOrder)
        {
            DrawTabOrder(canvas, g, p);
        }

        DrawTip(canvas, g, p);
    }

    /// <summary>Calls the component's renderer (the unknown-class renderer for a class without metadata); a renderer that throws is replaced by the generic one.</summary>
    private void DrawComponent(IDesignCanvas canvas, RenderContext ctx, FormObject obj)
    {
        var renderer = _layout.Renderers.Resolve(obj.Info is null ? "unknown" : ctx.Info.RendererId);
        try
        {
            renderer.Render(ctx, canvas);
        }
        catch (Exception e) when (e is not OutOfMemoryException && !ReferenceEquals(renderer, RendererRegistry.Generic))
        {
            RendererRegistry.Generic.Render(ctx, canvas); // DSG-RND-008: renderers never break the surface
        }
    }

    /// <summary>The grid of <paramref name="container"/>'s client area, then its children back to front with their overlays.</summary>
    private void DrawContainer(IDesignCanvas canvas, SurfaceGeometry g, FormObject container, Rect viewport, bool grid)
    {
        var client = g.GetClientRect(container);
        if (!Intersects(client, viewport))
        {
            return;
        }

        using var clip = canvas.PushClip(client);
        if (grid)
        {
            DrawGrid(canvas, client, viewport);
        }

        foreach (var child in container.Children)
        {
            if (!g.IsControl(child) || IsHiddenPage(g, child))
            {
                continue;
            }

            var b = g.GetBounds(child);
            if (b.Width <= 0 || b.Height <= 0 || !Intersects(b, viewport))
            {
                continue;
            }

            using (canvas.PushClip(b))
            {
                using (canvas.PushTranslate(b.Left, b.Top))
                {
                    DrawComponent(canvas, RenderContext.FromLayout(child, g.Layout, Palette.Renderers), child);
                }

                if (child.Info is { AcceptsChildren: true } && !child.IsPreserved)
                {
                    DrawContainer(canvas, g, child, viewport, grid);
                }

                DrawStateOverlays(canvas, child, b);
            }
        }
    }

    /// <summary>UI-DSG-020 … 022: 1 × 1 dots at the grid positions of the client origin, every second one below 4 DIP, only in the viewport.</summary>
    private void DrawGrid(IDesignCanvas canvas, Rect client, Rect viewport)
    {
        var stepX = _settings.GridSizeX < 4 ? 2 * Math.Max(1, _settings.GridSizeX) : _settings.GridSizeX;
        var stepY = _settings.GridSizeY < 4 ? 2 * Math.Max(1, _settings.GridSizeY) : _settings.GridSizeY;
        var (left, top) = (Math.Max(client.Left, viewport.Left), Math.Max(client.Top, viewport.Top));
        var (right, bottom) = (Math.Min(client.Right, viewport.Right), Math.Min(client.Bottom, viewport.Bottom));
        var x0 = FirstMultiple(left - client.Left, stepX);
        var y0 = FirstMultiple(top - client.Top, stepY);
        var dot = Palette.GridDot;
        for (var y = client.Top + y0; y < bottom; y += stepY)
        {
            for (var x = client.Left + x0; x < right; x += stepX)
            {
                canvas.FillRect(new Rect(x, y, 1, 1), dot);
            }
        }
    }

    private static int FirstMultiple(int value, int step) => value <= 0 ? 0 : (value + step - 1) / step * step;

    /// <summary>ui/designer.md §10: the hatch of <c>Visible = False</c>; the warning triangle of an unknown property or bad value (F0102, F0103).</summary>
    private void DrawStateOverlays(IDesignCanvas canvas, FormObject obj, Rect b)
    {
        if (obj.FindProperty("Visible") is { Value: IdentValue { Name: var visible } } && visible.Equals("False", StringComparison.OrdinalIgnoreCase))
        {
            var gray = Palette.Renderers.GrayText;
            canvas.FillHatch(b, gray with { A = 89 }, 6); // form.grayText at 35 %
        }

        if (!obj.IsPreserved && obj.Properties.Any(pr => pr.IsPreserved))
        {
            var (x, y) = (b.Left + 2, b.Top + 2);
            canvas.FillPolygon([new(x, y + 12), new(x + 12, y + 12), new(x + 6, y)], Palette.Warning);
        }
    }

    /// <summary>UI-DSG-060 … 062: background, border, the tiles with their names, or the empty-tray text.</summary>
    private void DrawTray(IDesignCanvas canvas, SurfaceGeometry g, SurfacePalette p)
    {
        var tray = g.TrayRect;
        canvas.FillRect(tray, p.Panel);
        canvas.DrawRect(tray, p.BorderDefault);
        if (g.TrayItems.Count == 0)
        {
            canvas.DrawText(EmptyTrayText, tray, CaptionFont, p.TextTertiary, HAlign.Center, VAlign.Center);
            return;
        }

        using var clip = canvas.PushClip(Inner(tray));
        foreach (var item in g.TrayItems)
        {
            var tile = g.GetTrayTile(item);
            using (canvas.PushTranslate(tile.Left, tile.Top))
            {
                DrawComponent(canvas, new RenderContext(item, SurfaceGeometry.TileSize, SurfaceGeometry.TileSize, palette: p.Renderers), item);
            }

            if (g.ShowComponentCaptions)
            {
                var cell = g.GetTrayCell(item);
                canvas.DrawText(item.Name, new Rect(cell.Left, tile.Bottom + 2, cell.Width, cell.Bottom - tile.Bottom - 2), CaptionFont, p.TextPrimary,
                    HAlign.Center, VAlign.Top, ellipsis: true);
            }
        }
    }

    /// <summary>
    /// UI-DSG-014, UI-DSG-030 … 034, UI-DSG-038, UI-DSG-063, UI-DSG-092: the outline and handles of one selected component.
    /// </summary>
    private void DrawSelection(IDesignCanvas canvas, SurfaceGeometry g, HitTester hitTester, FormObject obj, bool primary, bool sole)
    {
        var p = Palette;
        var size = _settings.HandleSize;
        if (obj.Parent is null)
        {
            if (primary && sole && !IsReadOnly)
            {
                DrawHandles(canvas, Ordered(hitTester.GetHandleCenters(obj, true, size)), size, filled: true, HandleColor());
            }

            return;
        }

        var tray = g.IsTrayItem(obj);
        if (!tray && (!g.IsControl(obj) || IsHiddenPage(g, obj)))
        {
            return;
        }

        var rect = tray ? g.GetTrayTile(obj) : g.GetBounds(obj);
        if (obj.IsPreserved || IsReadOnly)
        {
            canvas.DrawRect(rect, p.HandleSecondary);
            return;
        }

        var centers = tray ? Corners(rect) : Ordered(hitTester.GetHandleCenters(obj, true, size));
        if (primary)
        {
            canvas.DrawRect(rect, HandleColor());
            DrawHandles(canvas, centers, size, filled: true, HandleColor());
        }
        else
        {
            canvas.DrawRect(rect, p.HandleSecondary, 1, LineStyle.Dash);
            DrawHandles(canvas, [.. centers.Where(c => c.Kind is HandleKind.NW or HandleKind.NE or HandleKind.SE or HandleKind.SW)], size,
                filled: false, p.HandleSecondary);
        }
    }

    private Rgba HandleColor() => _settings.LockControls ? Palette.HandleLocked : Palette.HandleBorder;

    private void DrawHandles(IDesignCanvas canvas, IEnumerable<(HandleKind Kind, int X, int Y)> centers, int size, bool filled, Rgba border)
    {
        foreach (var (_, x, y) in centers)
        {
            var square = new Rect(x - size / 2, y - size / 2, size, size);
            if (filled)
            {
                canvas.FillRect(square, Palette.HandleFill);
            }

            canvas.DrawRect(square, border);
        }
    }

    private static List<(HandleKind Kind, int X, int Y)> Ordered(IEnumerable<(HandleKind Kind, int X, int Y)> centers) =>
        [.. centers.OrderBy(c => Array.IndexOf(HandleDrawOrder, c.Kind))];

    private static List<(HandleKind Kind, int X, int Y)> Corners(Rect r) =>
        [(HandleKind.NW, r.Left, r.Top), (HandleKind.NE, r.Right - 1, r.Top), (HandleKind.SE, r.Right - 1, r.Bottom - 1), (HandleKind.SW, r.Left, r.Bottom - 1)];

    /// <summary>UI-DSG-050 … 054: the guidelines of the snaps applied to the dragged control (or the placement rectangle).</summary>
    private void DrawGuidelines(IDesignCanvas canvas, SurfaceGeometry g, SurfacePalette p)
    {
        Rect? dragged = State switch
        {
            InteractionState.Moving when _moveAnchor is { } a && !_moveTray => g.GetBounds(a),
            InteractionState.Resizing when _resizePrimary is { Parent: not null } r => g.GetBounds(r),
            InteractionState.Placing => PlacementRect,
            _ => null,
        };
        if (dragged is not { } d || !_settings.UseGuidelines)
        {
            return;
        }

        foreach (var line in Guidelines)
        {
            var origin = g.GetClientRect(line.Parent);
            var r = line.Reference is { } reference ? g.GetBounds(reference) : origin;
            var x = line.Axis == SnapAxis.X;
            var at = (x ? origin.Left : origin.Top) + line.Coordinate;
            switch (line.Kind)
            {
                case GuidelineKind.Edge or GuidelineKind.Center:
                {
                    var color = p.GuidelineEdge;
                    var pixel = at == (x ? d.Right : d.Bottom) || at == (x ? r.Right : r.Bottom) ? at - 1 : at; // a right / bottom edge: its last pixel
                    if (x)
                    {
                        canvas.VLine(pixel, Math.Min(d.Top, r.Top) - 4, Math.Max(d.Bottom, r.Bottom) + 4, color);
                    }
                    else
                    {
                        canvas.HLine(Math.Min(d.Left, r.Left) - 4, Math.Max(d.Right, r.Right) + 4, pixel, color);
                    }

                    break;
                }

                case GuidelineKind.Baseline:
                    canvas.HLine(Math.Min(d.Left, r.Left) - 4, Math.Max(d.Right, r.Right) + 4, origin.Top + line.Coordinate, p.GuidelineBaseline);
                    break;
                default:
                    DrawMarginLine(canvas, d, r, line.Reference is null, x, p.GuidelineMargin);
                    break;
            }
        }
    }

    /// <summary>UI-DSG-053: a line in the gap between the dragged rect and the reference (or the parent's client edge), centred on the overlap, with 5 DIP ticks.</summary>
    private static void DrawMarginLine(IDesignCanvas canvas, Rect d, Rect r, bool parent, bool x, Rgba color)
    {
        var (dLo, dHi, rLo, rHi) = x ? (d.Left, d.Right, r.Left, r.Right) : (d.Top, d.Bottom, r.Top, r.Bottom);
        var (a, b) = parent
            ? (dLo - rLo <= rHi - dHi ? (rLo, dLo) : (dHi, rHi))
            : (dLo >= rHi ? (rHi, dLo) : (dHi, rLo));
        if (b <= a)
        {
            return;
        }

        var (oLo, oHi) = x ? (Math.Max(d.Top, r.Top), Math.Min(d.Bottom, r.Bottom)) : (Math.Max(d.Left, r.Left), Math.Min(d.Right, r.Right));
        var mid = oHi > oLo ? (oLo + oHi - 1) / 2 : (x ? d.Top + (d.Height - 1) / 2 : d.Left + (d.Width - 1) / 2);
        if (x)
        {
            canvas.HLine(a, b, mid, color);
            canvas.VLine(a, mid - 2, mid + 3, color);
            canvas.VLine(b - 1, mid - 2, mid + 3, color);
        }
        else
        {
            canvas.VLine(mid, a, b, color);
            canvas.HLine(mid - 2, mid + 3, a, color);
            canvas.HLine(mid - 2, mid + 3, b - 1, color);
        }
    }

    /// <summary>UI-DSG-070 … 072, DSG-SUR-095: a badge with the position in its parent's tab sequence on every focusable control; the focus rectangle.</summary>
    private void DrawTabOrder(IDesignCanvas canvas, SurfaceGeometry g, SurfacePalette p)
    {
        var bold = CaptionFont with { Bold = true };
        foreach (var obj in Document.AllObjects())
        {
            if (!TabOrderRules.IsFocusable(obj) || !g.IsControl(obj) || IsHiddenPage(g, obj))
            {
                continue;
            }

            var b = g.GetBounds(obj);
            var number = TabOrderRules.Sequence(obj.Parent!).ToList().IndexOf(obj).ToString(CultureInfo.InvariantCulture);
            var width = Math.Max(18, canvas.MeasureText(number, bold).Width + 8);
            var badge = new Rect(b.Left, b.Top, width, 16);
            var clicked = _tabClicked.Contains(obj);
            var tabStop = !(obj.FindProperty("TabStop") is { Value: IdentValue { Name: var stop } } && stop.Equals("False", StringComparison.OrdinalIgnoreCase));
            using (tabStop ? null : canvas.PushOpacity(0.5))
            {
                canvas.FillRect(badge, clicked ? p.Accent : p.TabBadgeBackground);
                canvas.DrawText(number, badge, bold with { StrikeOut = !tabStop }, clicked ? p.OnAccent : p.TabBadgeText, HAlign.Center, VAlign.Center);
            }
        }

        if (TabOrderFocus is { } focus && g.IsControl(focus))
        {
            canvas.DrawRect(g.GetBounds(focus), p.TextPrimary, 1, LineStyle.Dot);
        }
    }

    /// <summary>UI-DSG-037: <c>Left, Top</c> (move) or <c>Width × Height</c> (resize) 12 DIP right of and below the pointer.</summary>
    private void DrawTip(IDesignCanvas canvas, SurfaceGeometry g, SurfacePalette p)
    {
        string? text = null;
        if (State == InteractionState.Moving && _moveAnchor is { } anchor)
        {
            var (left, top) = _moveTray
                ? (SurfaceGeometry.TrayPosition(anchor).X + _moveDelta.X, SurfaceGeometry.TrayPosition(anchor).Y + _moveDelta.Y)
                : (g.Layout.GetBounds(anchor).Left, g.Layout.GetBounds(anchor).Top);
            text = string.Create(CultureInfo.InvariantCulture, $"{left}, {top}");
        }
        else if (State == InteractionState.Resizing && _resizePrimary is { } primary
                 && _resizeBounds.Find(m => ReferenceEquals(m.Object, primary)) is { Object: not null } resized)
        {
            text = string.Create(CultureInfo.InvariantCulture, $"{resized.Bounds.Width} × {resized.Bounds.Height}");
        }
        else if (State == InteractionState.Placing && PlacementRect is { } placement)
        {
            text = string.Create(CultureInfo.InvariantCulture, $"{placement.Width} × {placement.Height}"); // DSG-TP-045
        }

        if (text is null)
        {
            return;
        }

        var width = canvas.MeasureText(text, TipFont).Width + 8;
        var box = new Rect(_pointerAt.X + 12, _pointerAt.Y + 12, width, 20);
        canvas.FillRect(box, p.Raised);
        canvas.DrawRect(box, p.BorderDefault);
        canvas.DrawText(text, box, TipFont, p.TextPrimary, HAlign.Center, VAlign.Center);
    }

    /// <summary>A tab sheet that is not the active page is not drawn (DSG-RND-017).</summary>
    private static bool IsHiddenPage(SurfaceGeometry g, FormObject obj) => HitTester.IsInactiveSheet(obj, g.Layout);

    private static Rect Inner(Rect r) => new(r.Left + 1, r.Top + 1, Math.Max(0, r.Width - 2), Math.Max(0, r.Height - 2));
}
