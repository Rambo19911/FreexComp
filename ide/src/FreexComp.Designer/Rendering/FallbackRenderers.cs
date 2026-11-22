namespace FreexComp.Designer.Rendering;

/// <summary>§4.14: a known class without a specific renderer — a face box with <c>Name: TClassName</c>.</summary>
public sealed class GenericRenderer : DesignRenderer
{
    public override string Id => "generic";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        canvas.FillRect(new Rect(0, 0, w, h), p.Face);
        canvas.DrawRect(new Rect(0, 0, w, h), p.Border);

        var title = $"{ctx.Object.Name}: {ctx.Object.TypeName}";
        var value = FirstLine(ctx.Has("Caption") ? ctx.Get("Caption", "") : ctx.Get("Text", ""));
        var lineHeight = ctx.Metrics.LineHeight;
        if (value.Length > 0 && h >= 2 * lineHeight + 4)
        {
            var top = (h - 2 * lineHeight) / 2;
            Text(canvas, ctx, title, new Rect(0, top, w, lineHeight), p.Text, HAlign.Center, ellipsis: true);
            Text(canvas, ctx, value, new Rect(0, top + lineHeight, w, lineHeight), p.GrayText, HAlign.Center, ellipsis: true);
        }
        else
        {
            Text(canvas, ctx, title, new Rect(0, 0, w, h), p.Text, HAlign.Center, ellipsis: true);
        }
    }
}

/// <summary>§4.15: a preserved object of an unknown class — white box, dashed error border, warning triangle.</summary>
public sealed class UnknownRenderer : DesignRenderer
{
    public override string Id => "unknown";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        canvas.FillRect(new Rect(0, 0, w, h), new Rgba(255, 255, 255));
        canvas.DrawRect(new Rect(0, 0, w, h), p.Error, style: LineStyle.Dash);
        var triangle = new Rect(2, 2, 12, 12);
        using (ClipIfNeeded(canvas, ctx, triangle, w, h))
        {
            canvas.FillPolygon([new(2, 14), new(14, 14), new(8, 2)], p.Error);
        }

        Text(canvas, ctx, $"{ctx.Object.Name}: {ctx.Object.TypeName}", new Rect(0, 0, w, h), p.GrayText, HAlign.Center);
    }
}
