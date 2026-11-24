namespace FreexComp.Designer.Rendering;

/// <summary>
/// <c>dbgrid</c> (data design-time.md DAT-339): the field frame and the title row of the persistent <c>Columns</c> with the
/// run-time geometry (datasource-and-controls.md DAT-261, 262, 264) — row height = font line height + 4, a 12-wide
/// indicator column, 1 px grid lines inside the cells, 2 px text padding; column width = the stored <c>Width</c>, else
/// title length × the width of <c>0</c> + 4. The body is empty (no live data at design time); without a visible
/// persistent column the grid is empty with the component name centred.
/// </summary>
public sealed class DbGridRenderer : DesignRenderer
{
    private const int IndicatorWidth = 12;

    private static readonly string[] DefaultOptions =
        ["dgEditing", "dgTitles", "dgIndicator", "dgColLines", "dgRowLines", "dgTabs", "dgConfirmDelete", "dgCancelOnExit"];

    public override string Id => "dbgrid";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var content = FieldFrame(ctx, canvas, new Rect(0, 0, ctx.Width, ctx.Height), ctx.Fill(p.Window));
        if (content.Width <= 0 || content.Height <= 0)
        {
            return;
        }

        var columns = CollectionItems.Of(ctx, "Columns").Where(c => !Is(c.Get("Visible", "True"), "False")).ToList();
        if (columns.Count == 0)
        {
            Text(canvas, ctx, ctx.Object.Name, content, p.GrayText, HAlign.Center);
            return;
        }

        var options = ctx.Get<IReadOnlyList<string>>("Options", DefaultOptions);
        bool Has(string option) => options.Any(o => Is(o, option));
        if (!Has("dgTitles"))
        {
            return; // no title row, and no rows without data
        }

        var rowHeight = ctx.Metrics.LineHeight + 4;
        var digit = Math.Max(1, canvas.MeasureText("0", ctx.Font).Width);
        var indicator = Has("dgIndicator") ? IndicatorWidth : 0;
        var cells = columns.Select(c =>
        {
            var title = FirstLine(c.Get("Title", ""));
            title = title.Length > 0 ? title : FirstLine(c.Get("FieldName", ""));
            var width = c.Get("Width", -1);
            return (Title: title, Width: width >= 0 ? width : title.Length * digit + 4);
        }).ToList();
        var total = indicator + cells.Sum(c => (long)c.Width);
        var right = (int)Math.Min(content.Right, content.Left + total);
        var (colLines, rowLines) = (Has("dgColLines"), Has("dgRowLines"));

        using var clip = canvas.PushClip(content);
        var (top, bottom) = (content.Top, content.Top + rowHeight);
        canvas.FillRect(new Rect(content.Left, top, right - content.Left, rowHeight), p.Face);
        var x = content.Left + indicator;
        if (indicator > 0 && colLines)
        {
            canvas.VLine(x - 1, top, bottom, p.Border);
        }

        foreach (var (title, width) in cells)
        {
            if (x >= content.Right)
            {
                break; // a partially visible last column is drawn clipped (DAT-262)
            }

            Text(canvas, ctx, title, new Rect(x + 2, top, width - 4, rowHeight - 1), ctx.Ink);
            x = (int)Math.Min((long)x + width, content.Right + 1);
            if (colLines && width > 0)
            {
                canvas.VLine(x - 1, top, bottom, p.Border);
            }
        }

        if (rowLines)
        {
            canvas.HLine(content.Left, right, bottom - 1, p.Border);
        }
    }
}

/// <summary>
/// <c>dbnavigator</c> (data design-time.md DAT-339, datasource-and-controls.md DAT-256): one button per
/// <c>VisibleButtons</c> member in enumeration order, each <c>Width div count</c> wide (the last takes the remainder) and
/// of the full height, with a simple vector glyph.
/// </summary>
public sealed class DbNavigatorRenderer : DesignRenderer
{
    private const int Glyph = 7;

    private static readonly string[] Buttons =
        ["nbFirst", "nbPrior", "nbNext", "nbLast", "nbInsert", "nbDelete", "nbEdit", "nbPost", "nbCancel", "nbRefresh"];

    public override string Id => "dbnavigator";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        var visible = ctx.Get<IReadOnlyList<string>>("VisibleButtons", Buttons);
        var shown = Buttons.Where(b => visible.Any(v => Is(v, b))).ToList();
        if (shown.Count == 0)
        {
            canvas.FillRect(new Rect(0, 0, w, h), p.Face);
            return;
        }

        var each = w / shown.Count;
        for (var i = 0; i < shown.Count; i++)
        {
            var button = new Rect(i * each, 0, i == shown.Count - 1 ? w - i * each : each, h);
            canvas.FillRect(button, p.ButtonFace);
            canvas.DrawRect(button, p.Border);
            if (button.Width >= Glyph + 4 && h >= Glyph + 4)
            {
                DrawGlyph(canvas, shown[i], button.Left + (button.Width - Glyph) / 2, (h - Glyph) / 2, ctx.Ink);
            }
        }
    }

    /// <summary>The glyph of <paramref name="button"/> in the 7 × 7 box at (<paramref name="l"/>, <paramref name="t"/>).</summary>
    private static void DrawGlyph(IDesignCanvas canvas, string button, int l, int t, Rgba ink)
    {
        const double mid = Glyph / 2.0;
        switch (button)
        {
            case "nbFirst":
                canvas.VLine(l, t, t + Glyph, ink);
                canvas.FillPolygon([new(l + 6, t), new(l + 6, t + Glyph), new(l + 2, t + mid)], ink);
                break;
            case "nbPrior":
                canvas.FillPolygon([new(l + 5, t), new(l + 5, t + Glyph), new(l + 1, t + mid)], ink);
                break;
            case "nbNext":
                canvas.FillPolygon([new(l + 2, t), new(l + 2, t + Glyph), new(l + 6, t + mid)], ink);
                break;
            case "nbLast":
                canvas.FillPolygon([new(l + 1, t), new(l + 1, t + Glyph), new(l + 5, t + mid)], ink);
                canvas.VLine(l + 6, t, t + Glyph, ink);
                break;
            case "nbInsert":
                canvas.HLine(l, l + Glyph, t + 3, ink);
                canvas.VLine(l + 3, t, t + Glyph, ink);
                break;
            case "nbDelete":
                canvas.HLine(l, l + Glyph, t + 3, ink);
                break;
            case "nbEdit":
                canvas.DrawLine(l + 1, t + 6, l + 6, t + 1, ink, 2);
                break;
            case "nbPost":
                canvas.DrawPolyline([new(l, t + 4), new(l + 2.5, t + 6.5), new(l + 7, t + 1)], ink);
                break;
            case "nbCancel":
                canvas.DrawLine(l + 0.5, t + 0.5, l + 6.5, t + 6.5, ink);
                canvas.DrawLine(l + 6.5, t + 0.5, l + 0.5, t + 6.5, ink);
                break;
            default: // nbRefresh
                canvas.DrawEllipse(new Rect(l, t, Glyph, Glyph), ink);
                break;
        }
    }
}
