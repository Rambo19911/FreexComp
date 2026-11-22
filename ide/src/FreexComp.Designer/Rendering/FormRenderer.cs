namespace FreexComp.Designer.Rendering;

/// <summary>
/// §4.1: the form with its design-time frame. <see cref="RenderContext.Width"/>/<see cref="RenderContext.Height"/> are
/// the client size; the drawing covers the outer size (W + 2) × (H + 32): 1 px border and a 30 px caption.
/// </summary>
public sealed class FormRenderer : DesignRenderer
{
    private static readonly TextStyle CaptionFont = new("Segoe UI", 12, false, false, false, false);

    public override string Id => "form";

    /// <summary>The outer (decorated) size of a form with the given client size.</summary>
    public static Size OuterSize(int clientWidth, int clientHeight) => new(Math.Max(0, clientWidth) + 2, Math.Max(0, clientHeight) + 32);

    // A form with an empty client area still has its frame.
    public override void Render(RenderContext ctx, IDesignCanvas canvas) => RenderCore(ctx, canvas);

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (clientWidth, clientHeight) = (Math.Max(0, ctx.Width), Math.Max(0, ctx.Height));
        var (outerW, outerH) = OuterSize(clientWidth, clientHeight);
        canvas.DrawRect(new Rect(0, 0, outerW, outerH), p.BorderDark);
        canvas.FillRect(new Rect(1, 1, clientWidth, 30), p.CaptionBackground);

        var glyph = new Rect(9, 8, 16, 16);
        using (ClipIfNeeded(canvas, ctx, glyph, outerW, outerH))
        {
            canvas.DrawRect(glyph, p.GrayText);
            canvas.FillRect(new Rect(9, 8, 16, 3), p.GrayText);
        }

        var captionRight = outerW - 1 - 3 * 46 - 8;
        var caption = FirstLine(ctx.Get("Caption", ""));
        if (caption.Length > 0 && captionRight > 33)
        {
            canvas.DrawText(caption, new Rect(33, 1, captionRight - 33, 30), CaptionFont, p.CaptionText, HAlign.Left, VAlign.Center,
                ellipsis: true);
        }

        for (var k = 3; k >= 1; k--)
        {
            var cellLeft = outerW - 1 - 46 * k;
            if (cellLeft < 1)
            {
                continue; // a very narrow form shows only the glyphs that fit
            }

            var (gx, gy) = (cellLeft + 18, 1 + 10);
            switch (k)
            {
                case 3: // minimize
                    canvas.HLine(gx, gx + 10, 1 + 15, p.CaptionText);
                    break;
                case 2: // maximize
                    canvas.DrawRect(new Rect(gx, gy, 10, 10), p.CaptionText);
                    break;
                default: // close
                    canvas.DrawLine(gx + 0.5, gy + 0.5, gx + 9.5, gy + 9.5, p.CaptionText);
                    canvas.DrawLine(gx + 9.5, gy + 0.5, gx + 0.5, gy + 9.5, p.CaptionText);
                    break;
            }
        }

        canvas.FillRect(new Rect(1, 31, clientWidth, clientHeight), ctx.Fill(p.Face));
    }
}
