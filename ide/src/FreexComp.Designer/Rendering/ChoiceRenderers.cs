namespace FreexComp.Designer.Rendering;

/// <summary>§4.6 / §4.7: a glyph (check or radio) beside a caption.</summary>
public abstract class GlyphRenderer : DesignRenderer
{
    protected const int GlyphSize = 13;

    public override int? GetBaseline(RenderContext ctx) => CenteredBaseline(ctx, 0, ctx.Height);

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var (w, h) = (ctx.Width, ctx.Height);
        if (ctx.Has("ParentColor") && ctx.Has("Color") && !ctx.Get("ParentColor", true))
        {
            canvas.FillRect(new Rect(0, 0, w, h), ctx.Fill(ctx.ParentFill));
        }

        var glyphLeft = Is(ctx.Get("Alignment", "taRightJustify"), "taLeftJustify");
        var box = new Rect(glyphLeft ? w - GlyphSize : 0, (h - GlyphSize) / 2, GlyphSize, GlyphSize);
        using (ClipIfNeeded(canvas, ctx, box, w, h))
        {
            DrawGlyph(ctx, canvas, box, ctx.Enabled ? ctx.Palette.Text : ctx.Palette.GrayText);
        }

        var captionRect = glyphLeft ? new Rect(0, 0, Math.Max(0, w - 17), h) : new Rect(17, 0, Math.Max(0, w - 17), h);
        Text(canvas, ctx, FirstLine(ctx.Get("Caption", "")), captionRect, ctx.Ink, HAlign.Left, VAlign.Center, mnemonic: true);
    }

    protected abstract void DrawGlyph(RenderContext ctx, IDesignCanvas canvas, Rect box, Rgba ink);
}

/// <summary>§4.6 <c>checkbox</c>: <c>State</c> wins over <c>Checked</c> when stored.</summary>
public sealed class CheckBoxRenderer : GlyphRenderer
{
    public override string Id => "checkbox";

    protected override void DrawGlyph(RenderContext ctx, IDesignCanvas canvas, Rect box, Rgba ink)
    {
        var p = ctx.Palette;
        canvas.FillRect(box, p.Window);
        canvas.DrawRect(box, p.BorderDark);
        var state = ctx.IsStored("State") ? ctx.Get("State", "cbUnchecked") : ctx.Get("Checked", false) ? "cbChecked" : "cbUnchecked";
        if (Is(state, "cbChecked"))
        {
            canvas.DrawPolyline([new(box.Left + 3, box.Top + 6), new(box.Left + 5, box.Top + 9), new(box.Left + 10, box.Top + 3)], ink, 2);
        }
        else if (Is(state, "cbGrayed"))
        {
            canvas.FillRect(new Rect(box.Left + 3, box.Top + 3, 7, 7), p.GrayText);
        }
    }
}

/// <summary>§4.7 <c>radiobutton</c>.</summary>
public sealed class RadioButtonRenderer : GlyphRenderer
{
    public override string Id => "radiobutton";

    protected override void DrawGlyph(RenderContext ctx, IDesignCanvas canvas, Rect box, Rgba ink)
    {
        canvas.FillEllipse(box, ctx.Palette.Window);
        canvas.DrawEllipse(box, ctx.Palette.BorderDark);
        if (ctx.Get("Checked", false))
        {
            canvas.FillEllipse(new Rect(box.Left + 4, box.Top + 4, 5, 5), ink);
        }
    }
}

/// <summary>§4.8 <c>listbox</c>.</summary>
public sealed class ListBoxRenderer : DesignRenderer
{
    public override string Id => "listbox";

    public override int? GetBaseline(RenderContext ctx) => 1 + ctx.Metrics.Ascent; // 1 + (rowHeight − LineHeight) div 2 + Ascent

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var content = FieldFrame(ctx, canvas, new Rect(0, 0, ctx.Width, ctx.Height), ctx.Fill(ctx.Palette.Window));
        DrawItems(ctx, canvas, content, ctx.Get<IReadOnlyList<string>>("Items", []), ctx.Get("ItemIndex", -1), showNameWhenEmpty: true);
    }

    /// <summary>Rows of <c>LineHeight</c> top-down in <paramref name="content"/>, a scroll stub when they overflow.</summary>
    internal static void DrawItems(RenderContext ctx, IDesignCanvas canvas, Rect content, IReadOnlyList<string> items, int itemIndex,
        bool showNameWhenEmpty)
    {
        var p = ctx.Palette;
        var rowHeight = Math.Max(1, ctx.Metrics.LineHeight);
        var textWidth = content.Width;
        if ((long)items.Count * rowHeight > content.Height && content.Width >= 17)
        {
            VerticalScrollStub(ctx, canvas, new Rect(content.Right - 17, content.Top, 17, content.Height));
            textWidth -= 17;
        }

        if (items.Count == 0)
        {
            if (showNameWhenEmpty)
            {
                Text(canvas, ctx, ctx.Object.Name, Row(content, 0, rowHeight, textWidth, 2), p.GrayText);
            }

            return;
        }

        for (var i = 0; i < items.Count && content.Top + i * rowHeight < content.Bottom; i++)
        {
            var color = ctx.Ink;
            if (i == itemIndex)
            {
                var row = Row(content, i, rowHeight, textWidth, 0);
                if (row.Width > 0 && row.Height > 0)
                {
                    canvas.FillRect(row, p.Highlight);
                }

                color = p.HighlightText;
            }

            Text(canvas, ctx, FirstLine(items[i]), Row(content, i, rowHeight, textWidth, 2), color);
        }
    }

    private static Rect Row(Rect content, int index, int rowHeight, int width, int textInset)
    {
        var top = content.Top + index * rowHeight;
        return new Rect(content.Left + textInset, top, Math.Max(0, width - textInset), Math.Max(0, Math.Min(rowHeight, content.Bottom - top)));
    }
}

/// <summary>§4.9 <c>combobox</c>.</summary>
public sealed class ComboBoxRenderer : DesignRenderer
{
    private const int ButtonWidth = 17;

    public override string Id => "combobox";

    public override int? GetBaseline(RenderContext ctx) =>
        IsSimple(ctx) ? CenteredBaseline(ctx, 0, Math.Min(ctx.Height, EditRowHeight(ctx))) : CenteredBaseline(ctx, 0, ctx.Height);

    protected override void RenderCore(RenderContext ctx, IDesignCanvas canvas)
    {
        var p = ctx.Palette;
        var (w, h) = (ctx.Width, ctx.Height);
        var style = ctx.Get("Style", "csDropDown");
        var items = ctx.Get<IReadOnlyList<string>>("Items", []);
        var itemIndex = ctx.Get("ItemIndex", -1);
        if (IsSimple(ctx))
        {
            var editHeight = Math.Min(h, EditRowHeight(ctx));
            var edit = FieldFrame(ctx, canvas, new Rect(0, 0, w, editHeight), ctx.Fill(p.Window));
            Text(canvas, ctx, FirstLine(ctx.Get("Text", "")), ContainerRects.Inset(edit, 3, 0, 3, 0), ctx.Ink);
            if (h > editHeight)
            {
                var list = FieldFrame(ctx, canvas, new Rect(0, editHeight, w, h - editHeight), ctx.Fill(p.Window));
                ListBoxRenderer.DrawItems(ctx, canvas, list, items, itemIndex, showNameWhenEmpty: false);
            }

            return;
        }

        var dropDown = Is(style, "csDropDown");
        var content = FieldFrame(ctx, canvas, new Rect(0, 0, w, h), dropDown ? ctx.Fill(p.Window) : p.ButtonFace);
        var textArea = content;
        if (content.Width >= ButtonWidth)
        {
            var cell = new Rect(content.Right - ButtonWidth, content.Top, ButtonWidth, content.Height);
            if (dropDown)
            {
                canvas.VLine(cell.Left, cell.Top, cell.Bottom, p.Border);
            }

            Arrow(canvas, cell, 'd', ctx.Enabled ? p.Text : p.GrayText);
            textArea = textArea with { Width = textArea.Width - ButtonWidth };
        }

        var text = dropDown ? ctx.Get("Text", "") : itemIndex >= 0 && itemIndex < items.Count ? items[itemIndex] : "";
        Text(canvas, ctx, FirstLine(text), ContainerRects.Inset(textArea, 3, 0, 3, 0), ctx.Ink);
    }

    private static bool IsSimple(RenderContext ctx) => Is(ctx.Get("Style", "csDropDown"), "csSimple");

    /// <summary><c>Abs(Font.Height) + 11</c>: the edit row of <c>csSimple</c> (and the intrinsic height of the others).</summary>
    private static int EditRowHeight(RenderContext ctx) => (int)ctx.Font.SizePx + 11;
}
