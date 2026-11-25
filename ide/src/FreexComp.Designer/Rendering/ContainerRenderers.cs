namespace FreexComp.Designer.Rendering;

/// <summary>§4.10 <c>groupbox</c>: client = bounds, align rect below the caption (UIF-218).</summary>
public sealed class GroupBoxRenderer : DesignRenderer
{
    public override string Id => "groupbox";

    public override int? GetBaseline(RenderContext ctx) => ctx.Metrics.Ascent;

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        var fill = ctx.Fill(ctx.ParentFill);
        canvas.FillRect(new Rect(0, 0, w, h), fill);
        var captionHeight = ContainerRects.CaptionHeight(ctx);
        var t = Math.Min(captionHeight / 2, h);
        if (h - t > 0)
        {
            canvas.DrawRect(new Rect(0, t, w, h - t), p.Border);
        }

        var caption = FirstLine(ctx.Get("Caption", ""));
        if (caption.Length == 0 || w <= 6)
        {
            return;
        }

        var textWidth = canvas.MeasureText(StripMnemonic(caption), ctx.Font).Width;
        var band = Math.Min(captionHeight, h);
        canvas.FillRect(new Rect(6, 0, Math.Min(textWidth + 4, w - 6), band), fill);
        Text(canvas, ctx, caption, new Rect(8, 0, Math.Max(0, Math.Min(textWidth, w - 8)), band), ctx.Ink, HAlign.Left, VAlign.Top,
            mnemonic: true);
    }
}

/// <summary>§4.11 <c>panel</c>: bevels inset the align rect, not the client origin (UIF-215, UIF-218).</summary>
public sealed class PanelRenderer : DesignRenderer
{
    public override string Id => "panel";

    public override int? GetBaseline(RenderContext ctx)
    {
        var align = ctx.AlignRect;
        var m = ctx.Metrics;
        var v = ctx.Get("VerticalAlignment", "taVerticalCenter");
        return Is(v, "taAlignTop") ? align.Top + m.Ascent
            : Is(v, "taAlignBottom") ? align.Bottom - m.Descent
            : align.Top + (align.Height - m.LineHeight) / 2 + m.Ascent;
    }

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        canvas.FillRect(new Rect(0, 0, w, h), ctx.Fill(ctx.ParentFill));
        if (ContainerRects.IsSingle(ctx))
        {
            canvas.DrawRect(new Rect(0, 0, w, h), p.BorderDark);
        }

        var client = ctx.ClientRect;
        var bevelWidth = Math.Max(0, ctx.Get("BevelWidth", 1));
        var outer = ctx.Get("BevelOuter", "bvRaised");
        var inner = ctx.Get("BevelInner", "bvNone");
        Bevel(canvas, client, outer, bevelWidth, p.Border);
        // D-104: the inner bevel lies just outside the align rect before Padding, so it follows the run-time (scaled)
        // BorderWidth of the layout instead of the stored one.
        // The inset is symmetric; its left side is never clamped away.
        var innerInset = ctx.AlignRect.Left - client.Left - ctx.Padding.Left - (Is(inner, "bvNone") ? 0 : bevelWidth);
        var innerRect = ContainerRects.Inset(client, innerInset);
        Bevel(canvas, innerRect, inner, bevelWidth, p.Border);

        if (ctx.Get("ShowCaption", true))
        {
            var v = ctx.Get("VerticalAlignment", "taVerticalCenter");
            var valign = Is(v, "taAlignTop") ? VAlign.Top : Is(v, "taAlignBottom") ? VAlign.Bottom : VAlign.Center;
            Text(canvas, ctx, FirstLine(ctx.Get("Caption", "")), ctx.AlignRect, ctx.Ink, HorizontalOf(ctx.Get("Alignment", "taCenter")),
                valign, mnemonic: true);
        }
    }

    private static void Bevel(IDesignCanvas canvas, Rect r, string cut, int width, Rgba color)
    {
        if ((Is(cut, "bvRaised") || Is(cut, "bvLowered")) && width > 0 && r.Width > 0 && r.Height > 0)
        {
            canvas.DrawRect(r, color, width); // raised and lowered look the same in the neutral style
        }
    }
}

/// <summary>§4.12 <c>image</c>: a placeholder (the picture is preserved, not decoded, in Phase 5).</summary>
public sealed class ImageRenderer : DesignRenderer
{
    public override string Id => "image";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        canvas.DrawRect(new Rect(0, 0, w, h), p.Border, style: LineStyle.Dash);
        canvas.DrawLine(0.5, 0.5, w - 0.5, h - 0.5, p.Border);
        canvas.DrawLine(w - 0.5, 0.5, 0.5, h - 0.5, p.Border);
        if (w < 48 || h < 20)
        {
            return;
        }

        var name = ctx.Object.Name;
        var size = canvas.MeasureText(name, ctx.Font);
        var lineHeight = ctx.Metrics.LineHeight;
        var boxWidth = Math.Min(size.Width + 4, w);
        var boxHeight = Math.Min(lineHeight + 4, h);
        var box = new Rect((w - boxWidth) / 2, (h - boxHeight) / 2, boxWidth, boxHeight);
        canvas.FillRect(box, p.Face);
        Text(canvas, ctx, name, ContainerRects.Inset(box, 2), p.GrayText, HAlign.Center);
    }
}

/// <summary>§4.13 <c>nonvisual</c>: the 32 × 32 tray tile (UI-DSG-062); the surface draws the name below it.</summary>
public sealed class NonVisualRenderer : DesignRenderer
{
    /// <summary>The tile edge length.</summary>
    public const int TileSize = 32;

    public override string Id => "nonvisual";

    // The tile has a fixed size whatever the context says.
    public override void Render(RenderContext ctx, IDesignCanvas canvas) => RenderCore(ctx, canvas);

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var tile = new Rect(0, 0, TileSize, TileSize);
        canvas.FillRect(tile, ctx.Palette.TileBackground);
        canvas.DrawRect(tile, ctx.Palette.TileBorder);
        canvas.DrawIcon(ctx.Info.Palette?.Icon ?? "builtin:component", new Rect(4, 4, 24, 24));
    }
}

/// <summary>
/// DSG-RND-019 <c>frame</c>: the effective <c>Color</c> over the bounds, no border and no caption; the children are drawn
/// by the surface as for any container. As the root of a frame document the surface shows this rectangle instead of the
/// form frame (<see cref="Surface.SurfaceGeometry.FrameRect"/>).
/// </summary>
public sealed class FrameRenderer : DesignRenderer
{
    public override string Id => "frame";

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas) =>
        canvas.FillRect(new Rect(0, 0, ctx.Width, ctx.Height), ctx.Fill(ctx.ParentFill));
}
