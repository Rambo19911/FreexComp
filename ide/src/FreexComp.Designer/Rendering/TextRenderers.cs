namespace FreexComp.Designer.Rendering;

/// <summary>§4.2 <c>label</c>.</summary>
public sealed class LabelRenderer : DesignRenderer, IAutoSizeRenderer
{
    public override string Id => "label";

    public override int? GetBaseline(RenderContext ctx)
    {
        var m = ctx.Metrics;
        var layout = ctx.Get("Layout", "tlTop");
        return Is(layout, "tlCenter") ? (ctx.Height - m.LineHeight) / 2 + m.Ascent
            : Is(layout, "tlBottom") ? ctx.Height - m.Descent
            : m.Ascent;
    }

    /// <summary>DSG-RND-016: the measured caption; with <c>WordWrap</c> measured at the current width.</summary>
    public Size GetPreferredSize(RenderContext ctx, IDesignCanvas canvas)
    {
        var caption = Caption(ctx);
        if (caption.Length == 0)
        {
            return new Size(0, ctx.Metrics.LineHeight);
        }

        var wrap = ctx.Get("WordWrap", false);
        var measured = canvas.MeasureText(caption, ctx.Font, wrap ? Math.Max(1, ctx.Width) : double.PositiveInfinity);
        return wrap ? new Size(ctx.Width, measured.Height) : measured;
    }

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var bounds = new Rect(0, 0, ctx.Width, ctx.Height);
        if (!ctx.Get("Transparent", true))
        {
            canvas.FillRect(bounds, ctx.Fill(ctx.ParentFill));
        }

        var wrap = ctx.Get("WordWrap", false);
        var caption = ctx.Get("Caption", "");
        if (!wrap)
        {
            caption = FirstLine(caption);
        }

        if (caption.Length == 0)
        {
            canvas.DrawRect(bounds, p.Border, style: LineStyle.Dot); // design-time aid: an empty label stays visible
            return;
        }

        var layout = ctx.Get("Layout", "tlTop");
        var v = Is(layout, "tlCenter") ? VAlign.Center : Is(layout, "tlBottom") ? VAlign.Bottom : VAlign.Top;
        Text(canvas, ctx, caption, bounds, ctx.Ink, HorizontalOf(ctx.Get("Alignment", "taLeftJustify")), v, wrap,
            mnemonic: ctx.Get("ShowAccelChar", true));
    }

    private static string Caption(RenderContext ctx)
    {
        var caption = ctx.Get("Caption", "");
        if (!ctx.Get("WordWrap", false))
        {
            caption = FirstLine(caption);
        }

        return ctx.Get("ShowAccelChar", true) ? StripMnemonic(caption) : caption;
    }
}

/// <summary>§4.5 <c>button</c>.</summary>
public sealed class ButtonRenderer : DesignRenderer
{
    public override string Id => "button";

    public override int? GetBaseline(RenderContext ctx) => CenteredBaseline(ctx, 0, ctx.Height);

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        var isDefault = ctx.Get("Default", false);
        canvas.FillRect(new Rect(0, 0, w, h), p.ButtonFace);
        canvas.DrawRect(new Rect(0, 0, w, h), isDefault ? p.Highlight : p.Border);
        if (isDefault && w > 2 && h > 2)
        {
            canvas.DrawRect(new Rect(1, 1, w - 2, h - 2), p.Highlight);
        }

        var wrap = ctx.Get("WordWrap", false);
        var caption = ctx.Get("Caption", "");
        Text(canvas, ctx, wrap ? caption : FirstLine(caption), ContainerRects.Inset(new Rect(0, 0, w, h), 4), ctx.Ink,
            HAlign.Center, VAlign.Center, wrap, mnemonic: true);
    }
}

/// <summary>§4.3 <c>edit</c>.</summary>
public sealed class EditRenderer : DesignRenderer
{
    public override string Id => "edit";

    public override int? GetBaseline(RenderContext ctx) => CenteredBaseline(ctx, 0, ctx.Height);

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var content = FieldFrame(ctx, canvas, new Rect(0, 0, ctx.Width, ctx.Height), ctx.Fill(ctx.Palette.Window));
        var text = FirstLine(ctx.Get("Text", ""));
        var passwordChar = ctx.Get("PasswordChar", "\0");
        if (passwordChar.Length > 0 && passwordChar[0] != '\0')
        {
            text = new string('●', text.Length);
        }

        Text(canvas, ctx, text, ContainerRects.Inset(content, 3, 0, 3, 0), ctx.Ink, HorizontalOf(ctx.Get("Alignment", "taLeftJustify")));
    }
}

/// <summary>§4.4 <c>memo</c>.</summary>
public sealed class MemoRenderer : DesignRenderer
{
    private const int Bar = 17;

    public override string Id => "memo";

    public override int? GetBaseline(RenderContext ctx) => 1 + 1 + ctx.Metrics.Ascent;

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var content = FieldFrame(ctx, canvas, new Rect(0, 0, ctx.Width, ctx.Height), ctx.Fill(ctx.Palette.Window));
        var bars = ctx.Get("ScrollBars", "ssNone");
        var vertical = (Is(bars, "ssVertical") || Is(bars, "ssBoth")) && content.Width >= Bar;
        var horizontal = (Is(bars, "ssHorizontal") || Is(bars, "ssBoth")) && content.Height >= Bar;
        var textArea = content;
        if (vertical)
        {
            VerticalScrollStub(ctx, canvas, new Rect(content.Right - Bar, content.Top, Bar, content.Height));
            textArea = textArea with { Width = textArea.Width - Bar };
        }

        if (horizontal)
        {
            HorizontalScrollStub(ctx, canvas, new Rect(content.Left, content.Bottom - Bar, textArea.Width, Bar));
            textArea = textArea with { Height = textArea.Height - Bar };
        }

        var rect = ContainerRects.Inset(textArea, 3, 1, 3, 1);
        var lines = ctx.Get<IReadOnlyList<string>>("Lines", []);
        var h = HorizontalOf(ctx.Get("Alignment", "taLeftJustify"));
        if (ctx.Get("WordWrap", true))
        {
            Text(canvas, ctx, string.Join("\n", lines), rect, ctx.Ink, h, VAlign.Top, wordWrap: true);
            return;
        }

        var lineHeight = Math.Max(1, ctx.Metrics.LineHeight);
        for (var i = 0; i < lines.Count; i++)
        {
            var top = rect.Top + i * lineHeight;
            if (top >= rect.Bottom)
            {
                break; // lines beyond the rect are clipped
            }

            Text(canvas, ctx, lines[i], new Rect(rect.Left, top, rect.Width, Math.Min(lineHeight, rect.Bottom - top)), ctx.Ink, h, VAlign.Top);
        }
    }
}
