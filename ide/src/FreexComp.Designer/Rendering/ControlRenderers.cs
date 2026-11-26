using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Rendering;

/// <summary>§4.16 <c>scrollbar</c>: end cells with arrows along the longer side (<c>Kind</c> when present), no thumb.</summary>
public sealed class ScrollBarRenderer : DesignRenderer
{
    public override string Id => "scrollbar";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        canvas.FillRect(new Rect(0, 0, w, h), p.Face);
        canvas.DrawRect(new Rect(0, 0, w, h), p.Border);
        var vertical = ctx.Has("Kind") ? Is(ctx.Get("Kind", "sbHorizontal"), "sbVertical") : h > w;
        var ink = ctx.Enabled ? p.GrayText : p.Border;
        if (vertical)
        {
            var cell = Math.Min(w, h / 2);
            Arrow(canvas, new Rect(0, 0, w, cell), 'u', ink);
            Arrow(canvas, new Rect(0, h - cell, w, cell), 'd', ink);
        }
        else
        {
            var cell = Math.Min(h, w / 2);
            Arrow(canvas, new Rect(0, 0, cell, h), 'l', ink);
            Arrow(canvas, new Rect(w - cell, 0, cell, h), 'r', ink);
        }
    }
}

/// <summary>§4.16 <c>statictext</c>: an always opaque label with an optional border; measures for auto-size (DSG-RND-016).</summary>
public sealed class StaticTextRenderer : DesignRenderer, IAutoSizeRenderer
{
    public override string Id => "statictext";

    public override int? GetBaseline(RenderContext ctx) => Inset(ctx) + ctx.Metrics.Ascent;

    /// <summary>DSG-RND-016: the label measurement plus 2 px per side for <c>sbsSingle</c>, 4 px for <c>sbsSunken</c>.</summary>
    public Size GetPreferredSize(RenderContext ctx, IDesignCanvas canvas)
    {
        var caption = Caption(ctx);
        var inset = Inset(ctx);
        var size = caption.Length == 0 ? new Size(0, ctx.Metrics.LineHeight) : canvas.MeasureText(caption, ctx.Font);
        return new Size(size.Width + 2 * inset, size.Height + 2 * inset);
    }

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var bounds = new Rect(0, 0, ctx.Width, ctx.Height);
        canvas.FillRect(bounds, ctx.Fill(p.Face));
        var border = ctx.Get("BorderStyle", "sbsNone");
        if (Is(border, "sbsSingle"))
        {
            canvas.DrawRect(bounds, p.BorderDark);
        }
        else if (Is(border, "sbsSunken"))
        {
            canvas.DrawRect(bounds, p.Border);
        }

        Text(canvas, ctx, FirstLine(ctx.Get("Caption", "")), ContainerRects.Inset(bounds, Inset(ctx)), ctx.Ink,
            HorizontalOf(ctx.Get("Alignment", "taLeftJustify")), VAlign.Top, mnemonic: ctx.Get("ShowAccelChar", true));
    }

    /// <summary>The text inset of the border style (layout.md UIF-255): 2 for <c>sbsSingle</c>, 4 for <c>sbsSunken</c>.</summary>
    private static int Inset(RenderContext ctx)
    {
        var border = ctx.Get("BorderStyle", "sbsNone");
        return Is(border, "sbsSingle") ? 2 : Is(border, "sbsSunken") ? 4 : 0;
    }

    private static string Caption(RenderContext ctx)
    {
        var caption = FirstLine(ctx.Get("Caption", ""));
        return ctx.Get("ShowAccelChar", true) ? StripMnemonic(caption) : caption;
    }
}

/// <summary>§4.16 <c>shape</c>: a filled rectangle or ellipse by <c>Shape</c>; square variants use the centred largest square.</summary>
public sealed class ShapeRenderer : DesignRenderer
{
    public override string Id => "shape";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        var shape = ctx.Get("Shape", "stRectangle");
        var r = new Rect(0, 0, w, h);
        if (shape.Contains("Square", StringComparison.OrdinalIgnoreCase) || shape.Contains("Circle", StringComparison.OrdinalIgnoreCase))
        {
            var side = Math.Min(w, h);
            r = new Rect((w - side) / 2, (h - side) / 2, side, side);
        }

        var fill = ctx.ResolveColor(ctx.Get<FormValue?>("Brush.Color", null), p.Window);
        var pen = ctx.ResolveColor(ctx.Get<FormValue?>("Pen.Color", null), p.Text);
        if (shape.Contains("Circle", StringComparison.OrdinalIgnoreCase) || shape.Contains("Ellipse", StringComparison.OrdinalIgnoreCase))
        {
            canvas.FillEllipse(r, fill);
            canvas.DrawEllipse(r, pen);
        }
        else
        {
            canvas.FillRect(r, fill);
            canvas.DrawRect(r, pen);
        }
    }
}

/// <summary>§4.16 <c>bevel</c>: a <c>form.border</c> rectangle, or the single edge of a line shape.</summary>
public sealed class BevelRenderer : DesignRenderer
{
    public override string Id => "bevel";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var color = ctx.Palette.Border;
        var (w, h) = (ctx.Width, ctx.Height);
        switch (ctx.Get("Shape", "bsBox"))
        {
            case var s when Is(s, "bsTopLine"):
                canvas.HLine(0, w, 0, color);
                break;
            case var s when Is(s, "bsBottomLine"):
                canvas.HLine(0, w, h - 1, color);
                break;
            case var s when Is(s, "bsLeftLine"):
                canvas.VLine(0, 0, h, color);
                break;
            case var s when Is(s, "bsRightLine"):
                canvas.VLine(w - 1, 0, h, color);
                break;
            case var s when Is(s, "bsSpacer"):
                break;
            default:
                canvas.DrawRect(new Rect(0, 0, w, h), color);
                break;
        }
    }
}

/// <summary>§4.16 <c>splitter</c>: the fill and, with <c>Beveled</c>, a centred line along the longer side.</summary>
public sealed class SplitterRenderer : DesignRenderer
{
    public override string Id => "splitter";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        canvas.FillRect(new Rect(0, 0, w, h), ctx.Fill(p.Face));
        if (ctx.Get("Beveled", false))
        {
            if (h >= w)
            {
                canvas.VLine(w / 2, 0, h, p.Border);
            }
            else
            {
                canvas.HLine(0, w, h / 2, p.Border);
            }
        }
    }
}

/// <summary>§4.16 <c>scrollbox</c>: fill, double border for <c>bsSingle</c>, stubs where the layout shows a scroll bar (UIF-291).</summary>
public sealed class ScrollBoxRenderer : DesignRenderer
{
    private const int Bar = 17;

    public override string Id => "scrollbox";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        canvas.FillRect(new Rect(0, 0, w, h), ctx.Fill(ctx.ParentFill));
        var border = ContainerRects.ScrollBoxBorder(ctx);
        if (border > 0)
        {
            canvas.DrawRect(new Rect(0, 0, w, h), p.BorderDark);
            if (w > 2 && h > 2)
            {
                canvas.DrawRect(new Rect(1, 1, w - 2, h - 2), p.Border);
            }
        }

        // The layout's client size is the inner size minus the visible bars (layout-engine.md DSG-LAY-034).
        var client = ctx.ClientRect;
        var vertical = w - 2 * border - client.Width >= Bar;
        var horizontal = h - 2 * border - client.Height >= Bar;
        if (vertical)
        {
            VerticalScrollStub(ctx, canvas, new Rect(w - border - Bar, border, Bar, Math.Max(0, h - 2 * border - (horizontal ? Bar : 0))));
        }

        if (horizontal)
        {
            HorizontalScrollStub(ctx, canvas, new Rect(border, h - border - Bar, Math.Max(0, w - 2 * border - (vertical ? Bar : 0)), Bar));
        }
    }
}

/// <summary>
/// §4.16 <c>pagecontrol</c>: the tab strip (one tab per sheet with <c>TabVisible</c>; the design-active one filled
/// <c>form.window</c>, DSG-RND-017) and the body frame. The sheets are drawn as its children.
/// </summary>
public sealed class PageControlRenderer : DesignRenderer
{
    public override string Id => "pagecontrol";

    /// <summary>The tab-strip height: <c>TabHeight</c>, or <c>Abs(Font.Height) + 9</c> (21 for the default font; layout.md UIF-219).</summary>
    public static int TabHeight(RenderContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var stored = ctx.Get("TabHeight", 0);
        return stored > 0 ? stored : (int)Math.Abs(ctx.Font.SizePx) + 9;
    }

    /// <summary>
    /// DSG-RND-017: the design-active sheet of a page control — the visual child named by the effective <c>ActivePage</c>
    /// (<see cref="PropertyAccess.ReferencedName"/>: a plain name, or the file's header name qualified, RTL-1104) when it is
    /// tab-visible, else the first tab-visible one; null without such a sheet.
    /// </summary>
    public static FormObject? ActiveSheet(FormObject pageControl)
    {
        ArgumentNullException.ThrowIfNull(pageControl);
        var sheets = Sheets(pageControl).Where(TabVisible).ToList();
        return PropertyAccess.ReferencedName(pageControl, "ActivePage") is { } name
               && sheets.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } named
            ? named
            : sheets.FirstOrDefault();
    }

    /// <summary>The first tab-visible sheet: the one that is active without a stored <c>ActivePage</c> (its default).</summary>
    public static FormObject? DefaultSheet(FormObject pageControl) => Sheets(pageControl).FirstOrDefault(TabVisible);

    /// <summary>
    /// The tabs in control coordinates, left to right from x = 0, each <c>TabWidth</c> wide (or its caption's width
    /// + 16), <see cref="TabHeight"/> high. The surface hit-tests them with the same canvas measurement.
    /// </summary>
    public static IReadOnlyList<(FormObject Sheet, Rect Tab)> Tabs(RenderContext ctx, IDesignCanvas measurer)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(measurer);
        var tabHeight = TabHeight(ctx);
        var fixedWidth = ctx.Get("TabWidth", 0);
        var tabs = new List<(FormObject, Rect)>();
        var x = 0;
        foreach (var sheet in Sheets(ctx.Object).Where(TabVisible))
        {
            var width = fixedWidth > 0 ? fixedWidth : measurer.MeasureText(StripMnemonic(FirstLine(Caption(sheet))), ctx.Font).Width + 16;
            tabs.Add((sheet, new Rect(x, 0, width, tabHeight)));
            x += width;
        }

        return tabs;
    }

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        var tabHeight = Math.Min(TabHeight(ctx), h);
        canvas.FillRect(new Rect(0, 0, w, h), ctx.ParentFill);
        var active = ActiveSheet(ctx.Object);
        foreach (var (sheet, tab) in Tabs(ctx, canvas))
        {
            if (tab.Left >= w)
            {
                break;
            }

            var r = new Rect(tab.Left, 0, Math.Min(tab.Width, w - tab.Left), tabHeight);
            canvas.FillRect(r, ReferenceEquals(sheet, active) ? p.Window : p.Face);
            canvas.DrawRect(r, p.Border);
            Text(canvas, ctx, FirstLine(Caption(sheet)), r, ctx.Ink, HAlign.Center, VAlign.Center, mnemonic: true);
        }

        if (h - tabHeight > 0)
        {
            canvas.DrawRect(new Rect(0, tabHeight, w, h - tabHeight), p.Border);
        }
    }

    private static IEnumerable<FormObject> Sheets(FormObject pageControl) =>
        pageControl.Children.Where(c => c.Info is { Kind: not ComponentKind.NonVisual });

    private static bool TabVisible(FormObject sheet) =>
        PropertyAccess.EffectiveLine(sheet, "TabVisible")?.Line.Value is not IdentValue { Name: var v } || !v.Equals("False", StringComparison.OrdinalIgnoreCase);

    private static string Caption(FormObject sheet) => PropertyAccess.EffectiveLine(sheet, "Caption")?.Line.Value is StringValue { Value: var c } ? c : "";
}

/// <summary>§4.16 <c>tabsheet</c>: the effective fill; its geometry is the page rectangle (never stored).</summary>
public sealed class TabSheetRenderer : DesignRenderer
{
    public override string Id => "tabsheet";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas) =>
        canvas.FillRect(new Rect(0, 0, ctx.Width, ctx.Height), ctx.Fill(ctx.ParentFill));
}

/// <summary>§4.16 <c>stringgrid</c>: field frame, fixed rows/columns and grid lines from the default cell size, clipped.</summary>
public sealed class StringGridRenderer : DesignRenderer
{
    public override string Id => "stringgrid";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var content = FieldFrame(ctx, canvas, new Rect(0, 0, ctx.Width, ctx.Height), ctx.Fill(p.Window));
        if (content.Width <= 0 || content.Height <= 0)
        {
            return;
        }

        var colWidth = Math.Max(1, ctx.Get("DefaultColWidth", 64));
        var rowHeight = Math.Max(1, ctx.Get("DefaultRowHeight", 24));
        var line = Math.Max(0, ctx.Get("GridLineWidth", 1));
        var cols = Math.Clamp(ctx.Get("ColCount", 5), 0, 1000);
        var rows = Math.Clamp(ctx.Get("RowCount", 5), 0, 1000);
        var fixedCols = Math.Clamp(ctx.Get("FixedCols", 1), 0, cols);
        var fixedRows = Math.Clamp(ctx.Get("FixedRows", 1), 0, rows);
        var fixedFill = ctx.ResolveColor(ctx.Get<FormValue?>("FixedColor", null), p.Face);
        var gridWidth = Math.Min(content.Width, cols * (colWidth + line));
        var gridHeight = Math.Min(content.Height, rows * (rowHeight + line));
        using var clip = canvas.PushClip(content);
        for (var r = 0; r < fixedRows; r++)
        {
            canvas.FillRect(new Rect(content.Left, content.Top + r * (rowHeight + line), gridWidth, rowHeight), fixedFill);
        }

        for (var c = 0; c < fixedCols; c++)
        {
            canvas.FillRect(new Rect(content.Left + c * (colWidth + line), content.Top, colWidth, gridHeight), fixedFill);
        }

        if (line == 0)
        {
            return;
        }

        for (var c = 1; c <= cols && c * (colWidth + line) - line < content.Width; c++)
        {
            canvas.FillRect(new Rect(content.Left + c * (colWidth + line) - line, content.Top, line, gridHeight), p.Border);
        }

        for (var r = 1; r <= rows && r * (rowHeight + line) - line < content.Height; r++)
        {
            canvas.FillRect(new Rect(content.Left, content.Top + r * (rowHeight + line) - line, gridWidth, line), p.Border);
        }
    }
}

/// <summary>§4.16 <c>trackbar</c>: a 4 px channel along the longer side and an 11 × 21 thumb at the position fraction.</summary>
public sealed class TrackBarRenderer : DesignRenderer
{
    public override string Id => "trackbar";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        var vertical = ctx.Has("Orientation") ? Is(ctx.Get("Orientation", "trHorizontal"), "trVertical") : h > w;
        var fraction = Fraction(ctx);
        using var clip = canvas.PushClip(new Rect(0, 0, w, h));
        if (vertical)
        {
            canvas.FillRect(new Rect((w - 4) / 2, 5, 4, Math.Max(0, h - 10)), p.Border);
            var thumb = new Rect((w - 21) / 2, (int)Math.Round(fraction * Math.Max(0, h - 11), MidpointRounding.AwayFromZero), 21, 11);
            canvas.FillRect(thumb, p.ButtonFace);
            canvas.DrawRect(thumb, p.BorderDark);
        }
        else
        {
            canvas.FillRect(new Rect(5, (h - 4) / 2, Math.Max(0, w - 10), 4), p.Border);
            var thumb = new Rect((int)Math.Round(fraction * Math.Max(0, w - 11), MidpointRounding.AwayFromZero), (h - 21) / 2, 11, 21);
            canvas.FillRect(thumb, p.ButtonFace);
            canvas.DrawRect(thumb, p.BorderDark);
        }
    }

    /// <summary><c>(Position − Min) / (Max − Min)</c> clamped to 0 … 1; 0 when undefined.</summary>
    internal static double Fraction(RenderContext ctx)
    {
        var (min, max, position) = (ctx.Get("Min", 0L), ctx.Get("Max", 10L), ctx.Get("Position", 0L));
        return max <= min ? 0 : Math.Clamp((double)(position - min) / (max - min), 0, 1);
    }
}

/// <summary>§4.16 <c>progressbar</c>: window fill, border and the highlight bar over the position fraction.</summary>
public sealed class ProgressBarRenderer : DesignRenderer
{
    public override string Id => "progressbar";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        canvas.FillRect(new Rect(0, 0, w, h), p.Window);
        canvas.DrawRect(new Rect(0, 0, w, h), p.Border);
        var inner = ContainerRects.Inset(new Rect(0, 0, w, h), 1);
        var fraction = TrackBarRenderer.Fraction(ctx);
        if (Is(ctx.Get("Orientation", "pbHorizontal"), "pbVertical"))
        {
            var filled = (int)Math.Round(fraction * inner.Height, MidpointRounding.AwayFromZero);
            if (filled > 0)
            {
                canvas.FillRect(new Rect(inner.Left, inner.Bottom - filled, inner.Width, filled), p.Highlight);
            }
        }
        else
        {
            var filled = (int)Math.Round(fraction * inner.Width, MidpointRounding.AwayFromZero);
            if (filled > 0)
            {
                canvas.FillRect(new Rect(inner.Left, inner.Top, filled, inner.Height), p.Highlight);
            }
        }
    }
}

/// <summary>§4.16 <c>treeview</c> / <c>listview</c>: field frame and the component name; a report header for list views.</summary>
public class TreeViewRenderer : DesignRenderer
{
    public override string Id => "treeview";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var content = FieldFrame(ctx, canvas, new Rect(0, 0, ctx.Width, ctx.Height), ctx.Fill(p.Window));
        var top = DrawHeader(ctx, canvas, content);
        Text(canvas, ctx, ctx.Object.Name, new Rect(4, top + 3, Math.Max(0, ctx.Width - 8), Math.Max(0, Math.Min(ctx.Metrics.LineHeight, ctx.Height - top - 3))),
            p.GrayText, HAlign.Left, VAlign.Top);
    }

    /// <summary>Draws a header row inside <paramref name="content"/>; returns the y below it (the content top when there is none).</summary>
    protected virtual int DrawHeader(RenderContext ctx, IDesignCanvas canvas, Rect content) => content.Top;
}

/// <summary>§4.16 <c>listview</c>: as <c>treeview</c>; <c>vsReport</c> with <c>ShowColumnHeaders</c> adds the header of the <c>Columns</c> items.</summary>
public sealed class ListViewRenderer : TreeViewRenderer
{
    public override string Id => "listview";

    protected override int DrawHeader(RenderContext ctx, IDesignCanvas canvas, Rect content)
    {
        if (!Is(ctx.Get("ViewStyle", "vsIcon"), "vsReport") || !ctx.Get("ShowColumnHeaders", true) || content.Width <= 0)
        {
            return content.Top;
        }

        var p = ctx.Palette;
        var height = Math.Min(ctx.Metrics.LineHeight + 6, content.Height);
        canvas.FillRect(new Rect(content.Left, content.Top, content.Width, height), p.Face);
        var x = content.Left;
        foreach (var item in CollectionItems.Of(ctx, "Columns"))
        {
            if (x >= content.Right)
            {
                break;
            }

            var width = Math.Max(0, Math.Min(item.Get("Width", 50), content.Right - x));
            Text(canvas, ctx, FirstLine(item.Get("Caption", "")), new Rect(x + 4, content.Top, Math.Max(0, width - 8), height), ctx.Ink,
                HorizontalOf(item.Get("Alignment", "taLeftJustify")));
            x += width;
            if (x < content.Right)
            {
                canvas.VLine(x - 1, content.Top, content.Top + height, p.Border);
            }
        }

        return content.Top + height;
    }
}

/// <summary>§4.16 <c>statusbar</c>: face fill, top edge, simple text or the <c>Panels</c> cells, size grip.</summary>
public sealed class StatusBarRenderer : DesignRenderer
{
    public override string Id => "statusbar";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        canvas.FillRect(new Rect(0, 0, w, h), p.Face);
        canvas.HLine(0, w, 0, p.Border);
        if (ctx.Get("SimplePanel", false))
        {
            Text(canvas, ctx, FirstLine(ctx.Get("SimpleText", "")), new Rect(4, 1, Math.Max(0, w - 4), Math.Max(0, h - 1)), ctx.Ink);
        }
        else
        {
            var x = 0;
            foreach (var panel in CollectionItems.Of(ctx, "Panels"))
            {
                if (x >= w)
                {
                    break;
                }

                var width = Math.Max(0, Math.Min(panel.Get("Width", 50), w - x));
                Text(canvas, ctx, FirstLine(panel.Get("Text", "")), new Rect(x + 2, 1, Math.Max(0, width - 4), Math.Max(0, h - 1)), ctx.Ink,
                    HorizontalOf(panel.Get("Alignment", "taLeftJustify")));
                x += width;
                if (x < w)
                {
                    canvas.VLine(x - 1, 1, h, p.Border);
                }
            }
        }

        if (ctx.Get("SizeGrip", true) && w >= 13 && h >= 13)
        {
            for (var k = 1; k <= 3; k++)
            {
                canvas.DrawLine(w - 4 * k, h - 0.5, w - 0.5, h - 4 * k, p.GrayText, 1, LineStyle.Dot);
            }
        }
    }
}

/// <summary>§4.16 <c>toolbar</c>: the effective fill and a bottom edge; its children are flowed by the layout (UIF-800).</summary>
public sealed class ToolBarRenderer : DesignRenderer
{
    public override string Id => "toolbar";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        canvas.FillRect(new Rect(0, 0, ctx.Width, ctx.Height), ctx.Fill(ctx.ParentFill));
        canvas.HLine(0, ctx.Width, ctx.Height - 1, ctx.Palette.Border);
    }
}

/// <summary>§4.16 <c>toolbutton</c>: an image placeholder (and the caption when the bar shows captions), separators, dividers.</summary>
public sealed class ToolButtonRenderer : DesignRenderer
{
    private const int Image = 16;

    public override string Id => "toolbutton";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        var style = ctx.Get("Style", "tbsButton");
        if (Is(style, "tbsSeparator"))
        {
            return;
        }

        if (Is(style, "tbsDivider"))
        {
            canvas.VLine(w / 2, 0, h, p.Border);
            return;
        }

        // Smaller than the image and its margins: everything is clipped to the bounds (§5).
        using var clip = w < Image + 6 || h < Image + 4 ? canvas.PushClip(new Rect(0, 0, w, h)) : null;
        var bar = ctx.Object.Parent;
        var showCaptions = bar is not null && ctx.ParentValue("ShowCaptions", false);
        var list = bar is not null && ctx.ParentValue("List", false);
        var caption = FirstLine(ctx.Get("Caption", ""));
        if (showCaptions && caption.Length > 0 && list)
        {
            var image = new Rect(3, (h - Image) / 2, Image, Image);
            canvas.DrawRect(image, p.Border);
            Text(canvas, ctx, caption, new Rect(image.Right + 3, 0, Math.Max(0, w - image.Right - 6), h), ctx.Ink, mnemonic: true);
        }
        else if (showCaptions && caption.Length > 0)
        {
            var image = new Rect((w - Image) / 2, 2, Image, Image);
            canvas.DrawRect(image, p.Border);
            Text(canvas, ctx, caption, new Rect(0, image.Bottom, w, Math.Max(0, h - image.Bottom)), ctx.Ink, HAlign.Center, mnemonic: true);
        }
        else
        {
            var image = new Rect((w - Image) / 2, (h - Image) / 2, Image, Image);
            canvas.DrawRect(image, p.Border);
        }

        if (ctx.Get("Down", false))
        {
            canvas.DrawRect(new Rect(0, 0, w, h), p.BorderDark);
        }
    }
}

/// <summary>The items of a <c>"kind": "collection"</c> property with their effective values (stored, else the item class default).</summary>
internal static class CollectionItems
{
    public static IEnumerable<Item> Of(RenderContext ctx, string property)
    {
        var info = ctx.Info.FindProperty(property);
        if (info is not { Kind: PropertyKind.Collection } || ctx.Object.FindProperty(property)?.Value is not CollectionValue collection)
        {
            yield break;
        }

        var type = info.ItemClass is { } itemClass ? ctx.Object.Catalog.FindType(itemClass) : null;
        foreach (var item in collection.Items)
        {
            yield return new Item(ctx.Object.Catalog, type, item);
        }
    }

    internal readonly record struct Item(ComponentCatalog Catalog, TypeInfo? Type, CollectionItem Value)
    {
        public T Get<T>(string path, T fallback)
        {
            FormValue? value = Value.Properties.FirstOrDefault(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).Value;
            if (value is null or RawValue && Type?.Properties.FirstOrDefault(p => p.Name.Equals(path, StringComparison.OrdinalIgnoreCase)) is { } info)
            {
                try
                {
                    value = PropertyAccess.DefaultValue(Catalog, info);
                }
                catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
                {
                    value = null;
                }
            }

            object? result = (value, fallback) switch
            {
                (IntegerValue i, int) => (int)Math.Clamp(i.Value, int.MinValue, int.MaxValue),
                (StringValue s, string) => s.Value,
                (IdentValue ident, string) => ident.Name,
                _ => null,
            };
            return result is T typed ? typed : fallback;
        }
    }
}
