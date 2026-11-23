using FreexComp.Designer.Layout;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Tests.Layout;
using static FreexComp.Designer.Tests.Layout.LayoutHarness;

namespace FreexComp.Designer.Tests.Rendering;

/// <summary>D-102: renderers take their size, client and align rects from the <see cref="LayoutResult"/> (DSG-RND-004, DSG-LAY-034).</summary>
public sealed partial class RendererTests
{
    /// <summary>
    /// The run-time state after the layout (scroll-box inset and scroll bars, page-control sheets, scaling) reaches the
    /// renderer, not the stored properties: a scroll box's client origin is (2, 2) and its size excludes the visible bar;
    /// a tab sheet is sized by the page rectangle; a panel scaled ×2 has twice its width and its scaled BorderWidth
    /// insets the align rect. Rects are in control coordinates (the align rect is moved by the client origin).
    /// </summary>
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-RND-004")]
    [Trait("Req", "DSG-LAY-034")]
    public void ClientRectsComeFromLayoutResult()
    {
        var form = Form(600, 400,
            Obj("Box", "TScrollBox", 0, 0, 200, 100, "", Obj("N", "TPanel", 0, 0, 300, 50))
            + Obj("Book", "TPageControl", 0, 110, 289, 193, "", "object Sheet: TTabSheet\nend\n")
            + Obj("Framed", "TPanel", 300, 0, 100, 60, "BorderStyle = bsSingle\nBorderWidth = 2"));
        var layout = Engine.Compute(form);

        var box = form.FindByName("Box")!;
        var boxCtx = RenderContext.FromLayout(box, layout);
        Assert.Equal((200, 100), (boxCtx.Width, boxCtx.Height));
        Assert.Equal(new Rect(2, 2, 196, 79), boxCtx.ClientRect);
        Assert.Equal(new Rect(2, 2, 196, 79), boxCtx.AlignRect);
        Assert.Equal(new Rect(2, 2, 196, 79), RenderHarness.Registry.Resolve(boxCtx.Info.RendererId).GetClientRect(boxCtx));

        var sheet = form.FindByName("Sheet")!;
        var sheetCtx = RenderContext.FromLayout(sheet, layout);
        Assert.Equal((281, 162), (sheetCtx.Width, sheetCtx.Height));
        Assert.Equal(new Rect(0, 0, 281, 162), sheetCtx.ClientRect);

        // The form keeps its design caption: client (1, 31) below it, of the computed client size.
        var root = RenderContext.FromLayout(form.Root, layout);
        Assert.Equal((600, 400), (root.Width, root.Height));
        Assert.Equal(new Rect(1, 31, 600, 400), root.ClientRect);

        var scaled = Engine.ComputeWithSteps(form, "scale 2 1").Single();
        var panel = form.FindByName("Framed")!;
        var panelCtx = RenderContext.FromLayout(panel, scaled);
        Assert.Equal((200, 120), (panelCtx.Width, panelCtx.Height));
        Assert.Equal(new Rect(1, 1, 198, 118), panelCtx.ClientRect);
        Assert.Equal(new Rect(1 + 5, 1 + 5, 198 - 10, 118 - 10), panelCtx.AlignRect); // BorderWidth 2 × 2 + outer bevel 1
        Assert.NotEqual(new RenderContext(panel, 200, 120).AlignRect, panelCtx.AlignRect); // the stored BorderWidth (2) would inset 3
    }

    /// <summary>
    /// D-104: after scaling, the panel's inner bevel sits just outside the layout's align rect (before its scaled
    /// <c>Padding</c>), not at the stored <c>BorderWidth</c>: ×2 makes BorderWidth 4 and Padding 6, the bevels stay 1.
    /// </summary>
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-RND-004")]
    public void ScaledPanelBevelMatchesAlignRect()
    {
        var form = Form(600, 400, Obj("Framed", "TPanel", 0, 0, 100, 60,
            "BorderStyle = bsSingle\nBorderWidth = 2\nBevelInner = bvLowered\nPadding.Left = 3\nPadding.Top = 3\nPadding.Right = 3\nPadding.Bottom = 3"));
        var scaled = Engine.ComputeWithSteps(form, "scale 2 1").Single();
        var ctx = RenderContext.FromLayout(form.FindByName("Framed")!, scaled);
        Assert.Equal(new Rect(1, 1, 198, 118), ctx.ClientRect);
        Assert.Equal(new Rect(1 + 12, 1 + 12, 198 - 24, 118 - 24), ctx.AlignRect); // BorderWidth 4 + two bevels + Padding 6

        var bevels = RenderHarness.Render(ctx).Ops.Where(op => op.Kind == "DrawRect").Select(op => op.Text).ToList();
        var border = ctx.Palette.Border;
        Assert.Equal(
        [
            $"DrawRect 0,0,200,120 {ctx.Palette.BorderDark} 1 Solid", // bsSingle frame
            $"DrawRect 1,1,198,118 {border} 1 Solid", // outer bevel on the client edge
            $"DrawRect 6,6,188,108 {border} 1 Solid", // inner bevel: the align rect outset by Padding 6 and the bevel 1
        ], bevels);
    }
}
