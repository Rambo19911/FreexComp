using FreexComp.Designer.Catalog;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;

namespace FreexComp.Designer.Surface;

/// <summary>
/// Where everything is on the design surface, and the conversions between its coordinate spaces (design-surface.md
/// DSG-SUR-001): <b>surface</b> (backdrop, origin top-left of the scroll content), <b>form client</b>, <b>parent
/// client</b> (as stored in the file) and <b>tray</b> (inside the tray border). Built from one
/// <see cref="LayoutResult"/>; client origins of containers come from their renderers (DSG-RND-004, which take them from
/// the layout, D-102). Frame and tray placement: ui/designer.md UI-DSG-003, UI-DSG-010, UI-DSG-060, UI-DSG-062.
/// </summary>
public sealed class SurfaceGeometry
{
    /// <summary>UI-DSG-003: the form frame's top-left outer corner in surface coordinates.</summary>
    public const int FrameOrigin = 16;

    /// <summary>UI-DSG-010: the caption bar height.</summary>
    public const int CaptionHeight = 30;

    /// <summary>UI-DSG-060: the gap between the form frame and the tray.</summary>
    public const int TrayGap = 8;

    /// <summary>UI-DSG-060: the minimum tray height.</summary>
    public const int MinTrayHeight = 64;

    /// <summary>UI-DSG-062: the width of a tray cell.</summary>
    public const int TrayCellWidth = 72;

    /// <summary>UI-DSG-062: the height of a tray cell with the name below the tile (32 + 2 + 16).</summary>
    public const int TrayCellHeight = 50;

    /// <summary>UI-DSG-062: the tile edge (<see cref="NonVisualRenderer.TileSize"/>).</summary>
    public const int TileSize = NonVisualRenderer.TileSize;

    private readonly RendererRegistry _renderers;
    private readonly Dictionary<FormObject, Rect> _clientRects = new(ReferenceEqualityComparer.Instance);

    /// <param name="document">The form.</param>
    /// <param name="layout">The layout of the current model version (DSG-SUR-181).</param>
    /// <param name="renderers">The renderers whose <c>GetClientRect</c> gives each container's client origin.</param>
    /// <param name="showComponentCaptions"><c>designer.showComponentCaptions</c>: false = tray cells are just the tile.</param>
    public SurfaceGeometry(FormDocument document, LayoutResult layout, RendererRegistry renderers, bool showComponentCaptions = true)
    {
        Document = document;
        Layout = layout;
        _renderers = renderers;
        ShowComponentCaptions = showComponentCaptions;

        var root = document.Root;
        var client = layout.HasBounds(root) ? layout.GetBounds(root) : default;
        FrameRect = new Rect(FrameOrigin, FrameOrigin, client.Width + 2, client.Height + CaptionHeight + 2);
        var rootClient = ClientOf(root, FrameRect.Left, FrameRect.Top);
        FormClientRect = rootClient;
        _clientRects[root] = rootClient;

        TrayItems = root.Children.Where(c => c.Info?.Kind == ComponentKind.NonVisual).ToList();
        var right = 0;
        var bottom = 0;
        foreach (var item in TrayItems)
        {
            var (x, y) = TrayPosition(item);
            right = Math.Max(right, x + TrayCellWidth);
            bottom = Math.Max(bottom, y + (showComponentCaptions ? TrayCellHeight : TileSize));
        }

        TrayRect = new Rect(FrameRect.Left, FrameRect.Bottom + TrayGap, Math.Max(FrameRect.Width, right + 8),
            Math.Max(MinTrayHeight, bottom + 8));
    }

    public FormDocument Document { get; }

    public LayoutResult Layout { get; }

    public bool ShowComponentCaptions { get; }

    /// <summary>The form frame (border, caption, client area) in surface coordinates: (ClientWidth + 2) × (ClientHeight + 32).</summary>
    public Rect FrameRect { get; }

    /// <summary>The caption bar inside the frame border.</summary>
    public Rect CaptionRect => new(FrameRect.Left + 1, FrameRect.Top + 1, Math.Max(0, FrameRect.Width - 2), CaptionHeight);

    /// <summary>The form's client area in surface coordinates (origin (17, 47)).</summary>
    public Rect FormClientRect { get; }

    /// <summary>The tray including its 1 px border, in surface coordinates.</summary>
    public Rect TrayRect { get; }

    /// <summary>The tray items (non-visual direct children of the form) in creation order.</summary>
    public IReadOnlyList<FormObject> TrayItems { get; }

    /// <summary>DSG-SUR-002: a pointer coordinate rounded half away from zero.</summary>
    public static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    public static (int X, int Y) Round(double x, double y) => (Round(x), Round(y));

    /// <summary>True for a component that is drawn on the form (has layout bounds) and is not the form itself.</summary>
    public bool IsControl(FormObject obj) => obj.Parent is not null && Layout.HasBounds(obj) && !IsTrayItem(obj);

    public bool IsTrayItem(FormObject obj) => obj.Parent is { Parent: null } && obj.Info?.Kind == ComponentKind.NonVisual;

    /// <summary>Outer bounds in surface coordinates: the frame for the form, the layout bounds for a control, the cell for a tray item.</summary>
    public Rect GetBounds(FormObject obj)
    {
        if (obj.Parent is null)
        {
            return FrameRect;
        }

        if (IsTrayItem(obj))
        {
            return GetTrayCell(obj);
        }

        var s = Layout.GetSurfaceBounds(obj);
        return s with { Left = s.Left + FormClientRect.Left, Top = s.Top + FormClientRect.Top };
    }

    /// <summary>The client area of the form or a control in surface coordinates (the children's coordinate origin).</summary>
    public Rect GetClientRect(FormObject obj)
    {
        if (!_clientRects.TryGetValue(obj, out var rect))
        {
            var bounds = GetBounds(obj);
            rect = ClientOf(obj, bounds.Left, bounds.Top);
            _clientRects[obj] = rect;
        }

        return rect;
    }

    /// <summary>A tray item's cell (72 × 50, or the 32 × 32 tile at +20 without captions) in surface coordinates.</summary>
    public Rect GetTrayCell(FormObject item)
    {
        var (x, y) = TrayToSurface(TrayPosition(item));
        return ShowComponentCaptions ? new Rect(x, y, TrayCellWidth, TrayCellHeight) : new Rect(x + (TrayCellWidth - TileSize) / 2, y, TileSize, TileSize);
    }

    /// <summary>The 32 × 32 tile of a tray item (centred at the top of its cell) in surface coordinates.</summary>
    public Rect GetTrayTile(FormObject item)
    {
        var (x, y) = TrayToSurface(TrayPosition(item));
        return new Rect(x + (TrayCellWidth - TileSize) / 2, y, TileSize, TileSize);
    }

    /// <summary>The stored <c>DesignLeft</c>/<c>DesignTop</c> of a tray item (0 when absent or malformed).</summary>
    public static (int X, int Y) TrayPosition(FormObject item)
    {
        static int Get(FormObject o, string path) => o.FindProperty(path) is { Value: IntegerValue i } ? (int)Math.Clamp(i.Value, 0, int.MaxValue) : 0;
        return (Get(item, "DesignLeft"), Get(item, "DesignTop"));
    }

    public (int X, int Y) FormClientToSurface((int X, int Y) p) => (p.X + FormClientRect.Left, p.Y + FormClientRect.Top);

    public (int X, int Y) SurfaceToFormClient((int X, int Y) p) => (p.X - FormClientRect.Left, p.Y - FormClientRect.Top);

    /// <summary>A point in the client coordinates of <paramref name="parent"/> (as stored in the file) → surface.</summary>
    public (int X, int Y) ParentClientToSurface(FormObject parent, (int X, int Y) p)
    {
        var c = GetClientRect(parent);
        return (p.X + c.Left, p.Y + c.Top);
    }

    /// <summary>A surface point → the client coordinates of <paramref name="parent"/>.</summary>
    public (int X, int Y) SurfaceToParentClient(FormObject parent, (int X, int Y) p)
    {
        var c = GetClientRect(parent);
        return (p.X - c.Left, p.Y - c.Top);
    }

    public (int X, int Y) TrayToSurface((int X, int Y) p) => (p.X + TrayRect.Left + 1, p.Y + TrayRect.Top + 1);

    public (int X, int Y) SurfaceToTray((int X, int Y) p) => (p.X - TrayRect.Left - 1, p.Y - TrayRect.Top - 1);

    private Rect ClientOf(FormObject obj, int left, int top)
    {
        if (!Layout.HasBounds(obj))
        {
            return new Rect(left, top, 0, 0);
        }

        var ctx = RenderContext.FromLayout(obj, Layout);
        var c = _renderers.Resolve(ctx.Info.RendererId).GetClientRect(ctx);
        return c with { Left = c.Left + left, Top = c.Top + top };
    }
}
