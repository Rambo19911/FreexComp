using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Rendering;

/// <summary>
/// The tab strip and body frame shared by the renderers of <c>TCustomTabControl</c> descendants (design-renderers.md
/// DSG-RND-020 … 022): the tab rectangles of <see cref="TabStripGeometry"/> for every position, row and style. Images are
/// not drawn at design time; <c>OwnerDraw</c> draws the same (no user code runs).
/// </summary>
public abstract class TabStripRenderer : DesignRenderer
{
    /// <summary>The caption of every visible tab, in order (as stored, with its mnemonic markers).</summary>
    protected abstract IReadOnlyList<string> Captions(RenderContext ctx);

    /// <summary>The selected visible tab when the context carries no laid-out strip; −1 for none.</summary>
    protected abstract int SelectedTab(RenderContext ctx);

    /// <summary>
    /// The strip of <paramref name="ctx"/>: the layout's (<see cref="RenderContext.TabStrip"/>), else one computed from the
    /// stored properties with text extents measured by <paramref name="measurer"/> (raw captions, DSG-LAY-071) and no images.
    /// </summary>
    public TabStripGeometry Strip(RenderContext ctx, IDesignCanvas measurer)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(measurer);
        if (ctx.TabStrip is { } laidOut)
        {
            return laidOut;
        }

        var tabWidth = ctx.Get("TabWidth", 0);
        var tabHeight = TabStripGeometry.TabThickness(ctx.Get("TabHeight", 0), (int)Math.Round(ctx.Font.SizePx));
        var lengths = Captions(ctx)
            .Select(c => TabStripGeometry.TabLength(tabWidth, measurer.MeasureText(c, ctx.Font).Width, 0))
            .ToList();
        return new TabStripGeometry(ctx.Width, ctx.Height, tabHeight, lengths,
            LayoutReader.ParseTabPosition(ctx.Get("TabPosition", "tpTop")) ?? TabPosition.Top,
            LayoutReader.ParseTabStyle(ctx.Get("Style", "tsTabs")) ?? TabStyle.Tabs,
            ctx.Get("MultiLine", false), ctx.Get("ScrollOpposite", false), SelectedTab(ctx));
    }

    /// <summary>
    /// DSG-RND-021: the body frame of <c>tsTabs</c> — the display rectangle grown by 4 on the sides without rows and by 2 on
    /// each side with rows, kept inside the control.
    /// </summary>
    public static Rect BodyFrame(TabStripGeometry strip)
    {
        ArgumentNullException.ThrowIfNull(strip);
        var d = strip.DisplayRect;
        var near = strip.RowCount - strip.OppositeRows > 0 ? 2 : 4; // the strip side
        var far = strip.OppositeRows > 0 ? 2 : 4;                   // the opposite side
        var (l, t, r, b) = (d.Left - 4, d.Top - 4, d.Right + 4, d.Bottom + 4);
        switch (strip.Position)
        {
            case TabPosition.Top: (t, b) = (d.Top - near, d.Bottom + far); break;
            case TabPosition.Bottom: (t, b) = (d.Top - far, d.Bottom + near); break;
            case TabPosition.Left: (l, r) = (d.Left - near, d.Right + far); break;
            default: (l, r) = (d.Left - far, d.Right + near); break;
        }

        (l, t) = (Math.Max(l, 0), Math.Max(t, 0));
        (r, b) = (Math.Min(r, strip.Width), Math.Min(b, strip.Height));
        return new Rect(l, t, Math.Max(r - l, 0), Math.Max(b - t, 0));
    }

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var strip = Strip(ctx, canvas);
        var captions = Captions(ctx);
        canvas.FillRect(new Rect(0, 0, ctx.Width, ctx.Height), ctx.ParentFill);
        if (strip.Style == TabStyle.Tabs && BodyFrame(strip) is { Width: > 0, Height: > 0 } frame)
        {
            canvas.DrawRect(frame, ctx.Palette.Border);
        }

        // The selected tsTabs tab is drawn last: it is grown over its neighbours and opens onto the body.
        var count = Math.Min(captions.Count, strip.TabRects.Count);
        var last = strip.Style == TabStyle.Tabs ? strip.SelectedTab : -1;
        for (var i = 0; i < count; i++)
        {
            if (i != last)
            {
                DrawTab(ctx, canvas, strip, i, captions[i]);
            }
        }

        if (last >= 0 && last < count)
        {
            DrawTab(ctx, canvas, strip, last, captions[last]);
        }
    }

    private static void DrawTab(RenderContext ctx, IDesignCanvas canvas, TabStripGeometry strip, int index, string caption)
    {
        var p = ctx.Palette;
        var r = strip.TabDrawRect(index);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        var selected = index == strip.SelectedTab;
        using (ClipIfNeeded(canvas, ctx, r, ctx.Width, ctx.Height))
        {
            canvas.FillRect(r, selected ? p.Window : p.Face);
            if (strip.Style == TabStyle.Tabs && selected)
            {
                OpenFrame(canvas, r, strip.Position, p.Border);
            }
            else if (strip.Style != TabStyle.FlatButtons || selected)
            {
                canvas.DrawRect(r, p.Border);
            }

            if (strip.Style != TabStyle.Tabs && selected)
            {
                canvas.HLine(r.Left, r.Right, r.Top, p.BorderDark); // sunken: dark top and left edges
                canvas.VLine(r.Left, r.Top, r.Bottom, p.BorderDark);
            }

            var rotation = strip.Position switch
            {
                TabPosition.Left => TextRotation.Up,
                TabPosition.Right => TextRotation.Down,
                _ => TextRotation.None,
            };
            Text(canvas, ctx, caption, r, ctx.Ink, HAlign.Center, VAlign.Center, mnemonic: true, rotation: rotation);
        }
    }

    // The selected tsTabs tab: its outline without the edge that faces the display rectangle.
    private static void OpenFrame(IDesignCanvas canvas, Rect r, TabPosition position, Rgba color)
    {
        if (position != TabPosition.Bottom)
        {
            canvas.HLine(r.Left, r.Right, r.Top, color);
        }

        if (position != TabPosition.Top)
        {
            canvas.HLine(r.Left, r.Right, r.Bottom - 1, color);
        }

        if (position != TabPosition.Right)
        {
            canvas.VLine(r.Left, r.Top, r.Bottom, color);
        }

        if (position != TabPosition.Left)
        {
            canvas.VLine(r.Right - 1, r.Top, r.Bottom, color);
        }
    }
}

/// <summary>
/// §4.16 <c>pagecontrol</c> (DSG-RND-021): one tab per sheet with <c>TabVisible</c>, caption the sheet's first
/// <c>Caption</c> line; the design-active one is the selected tab (DSG-RND-017). The sheets are drawn as its children.
/// </summary>
public sealed class PageControlRenderer : TabStripRenderer
{
    private static readonly PageControlRenderer Shared = new();

    public override string Id => "pagecontrol";

    /// <summary>
    /// DSG-RND-017: the design-active sheet of a page control — the visual child named by the effective <c>ActivePage</c>
    /// (<see cref="PropertyAccess.ReferencedName"/>: a plain name, or the file's header name qualified, RTL-1104) when it is
    /// tab-visible, else the first tab-visible one; null without such a sheet.
    /// </summary>
    public static FormObject? ActiveSheet(FormObject pageControl)
    {
        ArgumentNullException.ThrowIfNull(pageControl);
        return SheetNamedBy(pageControl, pageControl);
    }

    /// <summary>
    /// The sheet that is active when <paramref name="pageControl"/> stores no <c>ActivePage</c> line of its own: the one
    /// its frame definition's effective <c>ActivePage</c> names (DSG-FD-106; D-128), else the first tab-visible sheet.
    /// </summary>
    public static FormObject? DefaultSheet(FormObject pageControl)
    {
        ArgumentNullException.ThrowIfNull(pageControl);
        return SheetNamedBy(pageControl, pageControl.InheritedFrom);
    }

    /// <summary>
    /// DSG-RND-020, DSG-RND-023: every tab-visible sheet with its tab's item rectangle in control coordinates (the hit-test
    /// rectangles of the strip of <see cref="TabStripRenderer.Strip"/>).
    /// </summary>
    public static IReadOnlyList<(FormObject Sheet, Rect Tab)> Tabs(RenderContext ctx, IDesignCanvas measurer)
    {
        var rects = Shared.Strip(ctx, measurer).TabRects;
        return [.. VisibleSheets(ctx.Object).Zip(rects)];
    }

    protected override IReadOnlyList<string> Captions(RenderContext ctx) => [.. VisibleSheets(ctx.Object).Select(s => FirstLine(Caption(s)))];

    protected override int SelectedTab(RenderContext ctx) =>
        ActiveSheet(ctx.Object) is { } active ? VisibleSheets(ctx.Object).ToList().IndexOf(active) : -1;

    // The tab-visible sheet of pageControl named by the effective ActivePage of source, else the first tab-visible one.
    private static FormObject? SheetNamedBy(FormObject pageControl, FormObject? source)
    {
        var sheets = VisibleSheets(pageControl).ToList();
        return source is not null && PropertyAccess.ReferencedName(source, "ActivePage") is { } name
               && sheets.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } named
            ? named
            : sheets.FirstOrDefault();
    }

    private static IEnumerable<FormObject> VisibleSheets(FormObject pageControl) =>
        pageControl.Children.Where(c => c.Info is { Kind: not Catalog.ComponentKind.NonVisual } && TabVisible(c));

    private static bool TabVisible(FormObject sheet) =>
        PropertyAccess.EffectiveLine(sheet, "TabVisible")?.Line.Value is not IdentValue { Name: var v } || !v.Equals("False", StringComparison.OrdinalIgnoreCase);

    private static string Caption(FormObject sheet) => PropertyAccess.EffectiveLine(sheet, "Caption")?.Line.Value is StringValue { Value: var c } ? c : "";
}

/// <summary>
/// §4.16 <c>tabcontrol</c> (DSG-RND-022): the strip of <c>Tabs</c> (selected = the effective <c>TabIndex</c>, none when −1)
/// and the body frame; the children are drawn as for every container. Empty <c>Tabs</c>: only the frame.
/// </summary>
public sealed class TabControlRenderer : TabStripRenderer
{
    private static readonly TabControlRenderer Shared = new();

    public override string Id => "tabcontrol";

    /// <summary>DSG-RND-023: every tab's index with its item rectangle in control coordinates (the hit-test rectangles).</summary>
    public static IReadOnlyList<(int Index, Rect Tab)> Tabs(RenderContext ctx, IDesignCanvas measurer) =>
        [.. Shared.Strip(ctx, measurer).TabRects.Select((r, i) => (i, r))];

    /// <summary>The effective <c>Tabs</c> lines (empty when none are stored).</summary>
    public static IReadOnlyList<string> TabCaptions(RenderContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return ctx.Get<IReadOnlyList<string>>("Tabs", []);
    }

    protected override IReadOnlyList<string> Captions(RenderContext ctx) => TabCaptions(ctx);

    protected override int SelectedTab(RenderContext ctx) =>
        ctx.Get("TabIndex", -1) is var i && i >= 0 && i < TabCaptions(ctx).Count ? i : -1;
}
