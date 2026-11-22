namespace FreexComp.Designer.Rendering;

/// <summary>
/// Common base of the built-in renderers: nothing is drawn for a zero or negative size (§5), and the shared pieces of
/// §3.3 (field frame, glyphs, arrows, scroll-bar stubs) are drawn here.
/// </summary>
public abstract class DesignRenderer : IDesignRenderer
{
    public abstract string Id { get; }

    public virtual void Render(RenderContext ctx, IDesignCanvas canvas)
    {
        if (ctx.Width > 0 && ctx.Height > 0)
        {
            RenderCore(ctx, canvas);
        }
    }

    public virtual Rect GetClientRect(RenderContext ctx) => ctx.ClientRect;

    public virtual int? GetBaseline(RenderContext ctx) => null;

    protected abstract void RenderCore(RenderContext ctx, IDesignCanvas canvas);

    /// <summary><c>(H − LineHeight) div 2 + Ascent</c>: the baseline of one vertically centred line.</summary>
    protected static int CenteredBaseline(RenderContext ctx, int top, int height) =>
        top + (height - ctx.Metrics.LineHeight) / 2 + ctx.Metrics.Ascent;

    /// <summary>The first line of a caption (§5: line breaks are not drawn unless stated).</summary>
    protected static string FirstLine(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text : text[..end];
    }

    /// <summary>A caption with the mnemonic markers of DSG-RND-002 removed (<c>&amp;&amp;</c> → <c>&amp;</c>).</summary>
    protected static string StripMnemonic(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '&')
            {
                if (i + 1 < text.Length && text[i + 1] == '&')
                {
                    builder.Append('&');
                    i++;
                }

                continue;
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
    }

    protected static bool Is(string value, string member) => value.Equals(member, StringComparison.OrdinalIgnoreCase);

    protected static HAlign HorizontalOf(string alignment) =>
        Is(alignment, "taRightJustify") ? HAlign.Right : Is(alignment, "taCenter") ? HAlign.Center : HAlign.Left;

    /// <summary>Draws text only when its rectangle and the text are non-empty.</summary>
    protected static void Text(IDesignCanvas canvas, RenderContext ctx, string text, Rect r, Rgba color, HAlign h = HAlign.Left,
        VAlign v = VAlign.Center, bool wordWrap = false, bool ellipsis = false, bool mnemonic = false)
    {
        if (text.Length > 0 && r.Width > 0 && r.Height > 0)
        {
            canvas.DrawText(text, r, ctx.Font, color, h, v, wordWrap, ellipsis, mnemonic);
        }
    }

    /// <summary>Pushes a clip to the control bounds only when <paramref name="piece"/> does not fit inside them.</summary>
    protected static IDisposable? ClipIfNeeded(IDesignCanvas canvas, RenderContext ctx, Rect piece, int width, int height) =>
        piece.Left >= 0 && piece.Top >= 0 && piece.Right <= width && piece.Bottom <= height
            ? null
            : canvas.PushClip(new Rect(0, 0, width, height));

    /// <summary>
    /// The field frame of §3.3: fill (<c>form.face</c> when disabled) and, unless <c>BorderStyle = bsNone</c>, a
    /// <c>form.borderDark</c> border. Returns the content rect.
    /// </summary>
    protected static Rect FieldFrame(RenderContext ctx, IDesignCanvas canvas, Rect r, Rgba fill)
    {
        var p = ctx.Palette;
        canvas.FillRect(r, ctx.Enabled ? fill : p.Face);
        if (Is(ctx.Get("BorderStyle", "bsSingle"), "bsNone"))
        {
            return r;
        }

        canvas.DrawRect(r, p.BorderDark);
        return ContainerRects.Inset(r, 1);
    }

    /// <summary>A filled triangle of the given size centred in <paramref name="cell"/>, pointing up/down/left/right.</summary>
    protected static void Arrow(IDesignCanvas canvas, Rect cell, char direction, Rgba color)
    {
        var (w, h) = direction is 'u' or 'd' ? (7, 4) : (4, 7);
        if (cell.Width < w || cell.Height < h)
        {
            return; // no room: an arrow never leaves its cell
        }

        var x = cell.Left + (cell.Width - w) / 2;
        var y = cell.Top + (cell.Height - h) / 2;
        Point[] points = direction switch
        {
            'u' => [new(x, y + h), new(x + w, y + h), new(x + w / 2.0, y)],
            'd' => [new(x, y), new(x + w, y), new(x + w / 2.0, y + h)],
            'l' => [new(x + w, y), new(x + w, y + h), new(x, y + h / 2.0)],
            _ => [new(x, y), new(x, y + h), new(x + w, y + h / 2.0)],
        };
        canvas.FillPolygon(points, color);
    }

    /// <summary>The vertical scroll-bar stub of §3.3 in <paramref name="strip"/> (17 wide).</summary>
    protected static void VerticalScrollStub(RenderContext ctx, IDesignCanvas canvas, Rect strip)
    {
        var p = ctx.Palette;
        canvas.FillRect(strip, p.Face);
        canvas.VLine(strip.Left, strip.Top, strip.Bottom, p.Border);
        var cell = Math.Min(17, strip.Height);
        Arrow(canvas, new Rect(strip.Left, strip.Top, strip.Width, cell), 'u', p.GrayText);
        Arrow(canvas, new Rect(strip.Left, strip.Bottom - cell, strip.Width, cell), 'd', p.GrayText);
    }

    /// <summary>The horizontal counterpart (memo §4.4): top edge line, left/right triangles.</summary>
    protected static void HorizontalScrollStub(RenderContext ctx, IDesignCanvas canvas, Rect strip)
    {
        var p = ctx.Palette;
        canvas.FillRect(strip, p.Face);
        canvas.HLine(strip.Left, strip.Right, strip.Top, p.Border);
        var cell = Math.Min(17, strip.Width);
        Arrow(canvas, new Rect(strip.Left, strip.Top, cell, strip.Height), 'l', p.GrayText);
        Arrow(canvas, new Rect(strip.Right - cell, strip.Top, cell, strip.Height), 'r', p.GrayText);
    }
}
