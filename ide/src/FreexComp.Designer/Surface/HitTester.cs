using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

/// <summary>What a surface point is over (design-surface.md DSG-SUR-010).</summary>
public enum HitKind
{
    /// <summary>A resize handle of the primary selection (a control).</summary>
    Handle,

    /// <summary>A resize handle of the form frame (E, S, SE) when the form is the sole selection.</summary>
    FormHandle,

    TrayItem,

    Control,

    /// <summary>The form's client area where no control is hit.</summary>
    FormClient,

    /// <summary>The form caption or border.</summary>
    FormFrame,

    /// <summary>The tray background.</summary>
    Tray,

    Backdrop,
}

/// <summary>The eight resize handles (UI-DSG-030).</summary>
public enum HandleKind { NW, N, NE, E, SE, S, SW, W }

/// <summary>A hit: <paramref name="Object"/> is the component (the form for the form kinds), null for tray background and backdrop.</summary>
public readonly record struct HitResult(HitKind Kind, FormObject? Object, HandleKind? Handle = null);

/// <summary>Hit testing of the design surface (design-surface.md §2) on one <see cref="SurfaceGeometry"/>.</summary>
public sealed class HitTester
{
    /// <summary>UI-DSG-032 / UI-A11Y-043: the hit square of a handle.</summary>
    public const int HandleHitSize = 11;

    /// <summary><c>designer.handleSize</c> default (UI-DSG-032).</summary>
    public const int DefaultHandleSize = 7;

    /// <summary>DSG-SUR-010: when hit squares overlap, this order decides.</summary>
    private static readonly HandleKind[] HandlePriority =
        [HandleKind.SE, HandleKind.NE, HandleKind.SW, HandleKind.NW, HandleKind.E, HandleKind.S, HandleKind.W, HandleKind.N];

    public HitTester(SurfaceGeometry geometry) => Geometry = geometry;

    public SurfaceGeometry Geometry { get; }

    /// <summary>
    /// DSG-SUR-010 … 012. <paramref name="selection"/>: the current selection, last = primary. <paramref name="alt"/>: Alt
    /// held on pointer down — the front-most control at the point is skipped; <paramref name="altPrevious"/> is the
    /// component picked by the previous Alt+click at this point (null if none), so repeated Alt+clicks cycle deeper and wrap
    /// after the form to the front-most. Handles keep their priority with Alt (Alt only disables snapping while dragging them).
    /// </summary>
    public HitResult HitTest(double x, double y, IReadOnlyList<FormObject> selection, bool alt = false, FormObject? altPrevious = null,
        int handleSize = DefaultHandleSize)
    {
        var p = SurfaceGeometry.Round(x, y);
        var g = Geometry;
        var root = g.Document.Root;

        if (selection.Count > 0)
        {
            var primary = selection[^1];
            foreach (var (kind, square) in GetHandleSquares(primary, selection.Count == 1, handleSize))
            {
                if (Contains(square, p))
                {
                    return new HitResult(primary.Parent is null ? HitKind.FormHandle : HitKind.Handle, primary, kind);
                }
            }
        }

        for (var i = g.TrayItems.Count - 1; i >= 0; i--)
        {
            if (Contains(g.GetTrayCell(g.TrayItems[i]), p))
            {
                return new HitResult(HitKind.TrayItem, g.TrayItems[i]);
            }
        }

        if (Contains(g.FormClientRect, p))
        {
            var stack = ControlsAt(p);
            stack.Add(root);
            var index = alt ? AltIndex(stack, altPrevious) : 0;
            return new HitResult(index == stack.Count - 1 ? HitKind.FormClient : HitKind.Control, stack[index]);
        }

        if (Contains(g.FrameRect, p))
        {
            return new HitResult(HitKind.FormFrame, root);
        }

        return Contains(g.TrayRect, p) ? new HitResult(HitKind.Tray, null) : new HitResult(HitKind.Backdrop, null);
    }

    /// <summary>The controls whose bounds contain the surface point, front to back (DSG-SUR-010 (4), DSG-SUR-011).</summary>
    public List<FormObject> ControlsAt((int X, int Y) p)
    {
        var hits = new List<FormObject>();
        Collect(Geometry.Document.Root, p, hits);
        hits.Reverse(); // collected back to front (pre-order): parents before children, earlier siblings first
        return hits;
    }

    /// <summary>
    /// The hit squares of the handles <paramref name="obj"/> shows as primary selection, in hit priority order: the form's
    /// E, S, SE when it is the sole selection (UI-DSG-014); a control's eight handles, without the mid-edge handles of an
    /// axis shorter than 3 × <paramref name="handleSize"/> (UI-DSG-033). None for tray items (decoration only), preserved
    /// components (UI-DSG-038) and controls whose geometry is computed (<c>"stored": false</c>, e.g. tab sheets, UI-DSG-084).
    /// </summary>
    public IReadOnlyList<(HandleKind Kind, Rect Square)> GetHandleSquares(FormObject obj, bool soleSelection = true, int handleSize = DefaultHandleSize) =>
        GetHandleCenters(obj, soleSelection, handleSize)
            .Select(h => (h.Kind, new Rect(h.X - HandleHitSize / 2, h.Y - HandleHitSize / 2, HandleHitSize, HandleHitSize)))
            .ToList();

    /// <summary>The centres of the handles of <see cref="GetHandleSquares"/> (on the outline pixels), in hit priority order.</summary>
    public IReadOnlyList<(HandleKind Kind, int X, int Y)> GetHandleCenters(FormObject obj, bool soleSelection = true, int handleSize = DefaultHandleSize)
    {
        var g = Geometry;
        var list = new List<(HandleKind, int, int)>();
        if (obj.Parent is null)
        {
            if (soleSelection)
            {
                var f = g.FrameRect;
                var (r, b) = (f.Right - 1, f.Bottom - 1);
                list.Add((HandleKind.SE, r, b));
                list.Add((HandleKind.E, r, f.Top + (f.Height - 1) / 2));
                list.Add((HandleKind.S, f.Left + (f.Width - 1) / 2, b));
            }

            return list;
        }

        if (!g.IsControl(obj) || obj.IsPreserved || obj.Info?.FindProperty("Left") is { Stored: false })
        {
            return list;
        }

        var bounds = g.GetBounds(obj);
        var (left, top, right, bottom) = (bounds.Left, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
        var (midX, midY) = (bounds.Left + (bounds.Width - 1) / 2, bounds.Top + (bounds.Height - 1) / 2);
        var wide = bounds.Width >= 3 * handleSize;
        var high = bounds.Height >= 3 * handleSize;
        foreach (var kind in HandlePriority)
        {
            switch (kind)
            {
                case HandleKind.SE: list.Add((kind, right, bottom)); break;
                case HandleKind.NE: list.Add((kind, right, top)); break;
                case HandleKind.SW: list.Add((kind, left, bottom)); break;
                case HandleKind.NW: list.Add((kind, left, top)); break;
                case HandleKind.E when high: list.Add((kind, right, midY)); break;
                case HandleKind.S when wide: list.Add((kind, midX, bottom)); break;
                case HandleKind.W when high: list.Add((kind, left, midY)); break;
                case HandleKind.N when wide: list.Add((kind, midX, top)); break;
            }
        }

        return list;
    }

    private void Collect(FormObject parent, (int X, int Y) p, List<FormObject> hits)
    {
        var g = Geometry;
        if (!Contains(g.GetClientRect(parent), p))
        {
            return; // children are clipped to every ancestor's client rect
        }

        foreach (var child in parent.Children)
        {
            if (!g.IsControl(child) || IsHiddenPage(child))
            {
                continue;
            }

            var bounds = g.GetBounds(child);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                continue; // DSG-SUR-011: zero-size controls are not hit-testable
            }

            if (Contains(bounds, p))
            {
                hits.Add(child);
                Collect(child, p, hits);
            }
        }
    }

    /// <summary>A tab sheet that is not the active page is not drawn, so it is not hit (its geometry is computed: <c>"stored": false</c>).</summary>
    private bool IsHiddenPage(FormObject obj) =>
        obj.Info?.FindProperty("Left") is { Stored: false } && !Geometry.Layout.GetVisible(obj);

    private static int AltIndex(List<FormObject> stack, FormObject? previous)
    {
        var at = previous is null ? -1 : stack.FindIndex(o => ReferenceEquals(o, previous));
        return at >= 0 ? (at + 1) % stack.Count : Math.Min(1, stack.Count - 1);
    }

    private static bool Contains(Rect r, (int X, int Y) p) => p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
}
