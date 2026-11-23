namespace FreexComp.Designer.Rendering;

/// <summary>
/// Client and align rectangles of containers by the formulas of ui-framework layout.md UIF-215 … 220
/// (design-renderers.md DSG-RND-004, layout-engine.md DSG-LAY-034), keyed on the metadata renderer id. Both are
/// relative to the control's top-left; widths and heights are clamped to ≥ 0.
/// </summary>
public static class ContainerRects
{
    /// <summary>The client rectangle: form (1, 31, W, H) below the design caption; bordered panel inset 1; else the bounds.</summary>
    public static Rect Client(RenderContext ctx) => ctx.Info.RendererId switch
    {
        "form" => new Rect(RenderContext.FormClientLeft, RenderContext.FormClientTop, Math.Max(0, ctx.Width), Math.Max(0, ctx.Height)),
        "panel" when IsSingle(ctx) => Clamp(1, 1, ctx.Width - 2, ctx.Height - 2),
        "scrollbox" => Inset(Clamp(0, 0, ctx.Width, ctx.Height), ScrollBoxBorder(ctx)),
        "stringgrid" when IsSingle(ctx, "bsSingle") => Inset(Clamp(0, 0, ctx.Width, ctx.Height), 2),
        _ => Clamp(0, 0, ctx.Width, ctx.Height),
    };

    /// <summary>The align rectangle: <c>AdjustClientRect</c> of the class (UIF-218), then inset by <c>Padding</c> (UIF-220).</summary>
    public static Rect Align(RenderContext ctx)
    {
        var client = Client(ctx);
        var adjusted = ctx.Info.RendererId switch
        {
            "panel" => Inset(client, PanelInset(ctx)),
            "groupbox" => Inset(client, GroupBoxInset, CaptionHeight(ctx), GroupBoxInset, GroupBoxInset),
            _ => client,
        };
        return Inset(adjusted, ctx.Get("Padding.Left", 0), ctx.Get("Padding.Top", 0), ctx.Get("Padding.Right", 0),
            ctx.Get("Padding.Bottom", 0));
    }

    /// <summary>Group-box caption height <c>Abs(Font.Height) + 5</c> (UIF-218, controls-standard.md UIF-670).</summary>
    public static int CaptionHeight(RenderContext ctx) => GroupBoxCaptionHeight((int)ctx.Font.SizePx);

    /// <summary>Panel inset of the align rect: <c>BorderWidth</c> + <c>BevelWidth</c> per bevel that is not <c>bvNone</c>.</summary>
    public static int PanelInset(RenderContext ctx) =>
        PanelInset(Math.Max(0, ctx.Get("BorderWidth", 0)), Math.Max(0, ctx.Get("BevelWidth", 1)),
            IsBevel(ctx, "BevelOuter", "bvRaised"), IsBevel(ctx, "BevelInner", "bvNone"));

    /// <summary>
    /// UIF-218 <c>TPanel</c>: <c>I = BorderWidth + (BevelWidth if BevelOuter ≠ bvNone) + (BevelWidth if BevelInner ≠ bvNone)</c>
    /// (shared with the layout engine, DSG-LAY-034).
    /// </summary>
    public static int PanelInset(int borderWidth, int bevelWidth, bool outerBevel, bool innerBevel) =>
        borderWidth + (outerBevel ? bevelWidth : 0) + (innerBevel ? bevelWidth : 0);

    /// <summary>UIF-218 <c>TGroupBox</c>: <c>CapH = Abs(Font.Height) + 5</c> at 96 DPI (shared with the layout engine).</summary>
    public static int GroupBoxCaptionHeight(int fontHeight) => Math.Abs(fontHeight) + 5;

    /// <summary>UIF-218 <c>TGroupBox</c>: the side and bottom inset of the align rect at 96 DPI.</summary>
    public const int GroupBoxInset = 2;

    /// <summary>UIF-215 <c>TScrollBox</c>: the border inset per side, 2 for <c>bsSingle</c> (its default), else 0.</summary>
    public static int ScrollBoxBorder(RenderContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return IsSingle(ctx, "bsSingle") ? 2 : 0;
    }

    internal static bool IsSingle(RenderContext ctx, string fallback = "bsNone") =>
        ctx.Get("BorderStyle", fallback).Equals("bsSingle", StringComparison.OrdinalIgnoreCase);

    private static bool IsBevel(RenderContext ctx, string property, string fallback) =>
        !ctx.Get(property, fallback).Equals("bvNone", StringComparison.OrdinalIgnoreCase);

    internal static Rect Inset(Rect r, int all) => Inset(r, all, all, all, all);

    internal static Rect Inset(Rect r, int left, int top, int right, int bottom) =>
        Clamp(r.Left + left, r.Top + top, r.Width - left - right, r.Height - top - bottom);

    private static Rect Clamp(int left, int top, int width, int height) => new(left, top, Math.Max(0, width), Math.Max(0, height));
}
