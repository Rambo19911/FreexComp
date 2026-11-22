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
        "form" => new Rect(1, 31, Math.Max(0, ctx.Width), Math.Max(0, ctx.Height)),
        "panel" when IsSingle(ctx) => Clamp(1, 1, ctx.Width - 2, ctx.Height - 2),
        _ => Clamp(0, 0, ctx.Width, ctx.Height),
    };

    /// <summary>The align rectangle: <c>AdjustClientRect</c> of the class (UIF-218), then inset by <c>Padding</c> (UIF-220).</summary>
    public static Rect Align(RenderContext ctx)
    {
        var client = Client(ctx);
        var adjusted = ctx.Info.RendererId switch
        {
            "panel" => Inset(client, PanelInset(ctx)),
            "groupbox" => Inset(client, 2, CaptionHeight(ctx), 2, 2),
            _ => client,
        };
        return Inset(adjusted, ctx.Get("Padding.Left", 0), ctx.Get("Padding.Top", 0), ctx.Get("Padding.Right", 0),
            ctx.Get("Padding.Bottom", 0));
    }

    /// <summary>Group-box caption height <c>Abs(Font.Height) + 5</c> (UIF-218, controls-standard.md UIF-670).</summary>
    public static int CaptionHeight(RenderContext ctx) => (int)ctx.Font.SizePx + 5;

    /// <summary>Panel inset of the align rect: <c>BorderWidth</c> + <c>BevelWidth</c> per bevel that is not <c>bvNone</c>.</summary>
    public static int PanelInset(RenderContext ctx)
    {
        var bevel = Math.Max(0, ctx.Get("BevelWidth", 1));
        return Math.Max(0, ctx.Get("BorderWidth", 0)) + (IsBevel(ctx, "BevelOuter", "bvRaised") ? bevel : 0)
            + (IsBevel(ctx, "BevelInner", "bvNone") ? bevel : 0);
    }

    internal static bool IsSingle(RenderContext ctx) =>
        ctx.Get("BorderStyle", "bsNone").Equals("bsSingle", StringComparison.OrdinalIgnoreCase);

    private static bool IsBevel(RenderContext ctx, string property, string fallback) =>
        !ctx.Get(property, fallback).Equals("bvNone", StringComparison.OrdinalIgnoreCase);

    internal static Rect Inset(Rect r, int all) => Inset(r, all, all, all, all);

    internal static Rect Inset(Rect r, int left, int top, int right, int bottom) =>
        Clamp(r.Left + left, r.Top + top, r.Width - left - right, r.Height - top - bottom);

    private static Rect Clamp(int left, int top, int width, int height) => new(left, top, Math.Max(0, width), Math.Max(0, height));
}
