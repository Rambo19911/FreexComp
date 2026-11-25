using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;
using static FreexComp.Designer.Tests.Support.FrameFixtures;

namespace FreexComp.Designer.Tests.Rendering;

/// <summary>The <c>frame</c> renderer as a frame document's root and as an instance (design-renderers.md DSG-RND-019; D-118).</summary>
public sealed class FrameRendererTests
{
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-RND-019")]
    [Trait("Req", "DSG-FD-121")]
    public void RootAndInstanceRecording()
    {
        var registry = RendererRegistry.CreateDefault();
        var engine = new LayoutEngine(Components, registry);
        var frames = AddressFrames();

        // The root of a frame document: its effective Color over Width × Height, no border, no caption, no glyphs.
        var frame = frames.Find("TAddressFrame")!.Document;
        var frameLayout = engine.Compute(frame);
        var root = RenderContext.FromLayout(frame.Root, frameLayout);
        Assert.Equal("frame", root.Info.RendererId);
        Assert.Equal(new Rect(0, 0, 320, 180), root.ClientRect);
        Assert.Equal("FillRect 0,0,320,180 #FFFFFFFF\n", Record(registry, root));

        // On the surface the frame's rectangle replaces the form frame (no caption bar).
        var geometry = new SurfaceGeometry(frame, frameLayout, registry);
        Assert.Equal(new Rect(SurfaceGeometry.FrameOrigin, SurfaceGeometry.FrameOrigin, 320, 180), geometry.FrameRect);
        Assert.Equal(geometry.FrameRect, geometry.FormClientRect);
        Assert.Equal(geometry.FrameRect, geometry.GetBounds(frame.Root));

        // Resizing the frame on the surface stores its Width / Height (DSG-FD-117), not a client size.
        using (var h = DesignerHarness.FromText("object PlainFrame: TFrame\n  Left = 0\n  Top = 0\n  Width = 200\n  Height = 100\nend\n",
            catalog: Components))
        {
            h.Settings.NoSnapping();
            h.PressHandle("PlainFrame", HandleKind.SE);
            h.MoveBy(40, 20);
            h.Release();
            Assert.Equal("object PlainFrame: TFrame\n  Left = 0\n  Top = 0\n  Width = 240\n  Height = 120\nend\n", h.Text);
        }

        // An instance: the effective (inherited or own) Color; children are drawn by the surface.
        var form = ParseForm(CustomerForm, frames);
        new FormEditor(form, Components).Apply(new SetProperty("ShippingAddress", "Color", new IntegerValue(0x000000FF, true)));
        var layout = engine.Compute(form);
        var billing = RenderContext.FromLayout(form.FindByName("BillingAddress")!, layout);
        var shipping = RenderContext.FromLayout(form.FindByName("ShippingAddress")!, layout);
        Assert.Equal("FillRect 0,0,320,180 #FFFFFFFF\n", Record(registry, billing));
        Assert.Equal("FillRect 0,0,320,180 #FF0000FF\n", Record(registry, shipping));
        Assert.Equal(new Rect(0, 0, 320, 180), registry.Resolve("frame").GetClientRect(billing));
        Assert.Null(registry.Resolve("frame").GetBaseline(billing));

        // An inherited child resolves its effective values (the override wins) when drawn.
        var label = RenderContext.FromLayout(form.FindByQualifiedName("BillingAddress.StreetLabel")!, layout);
        Assert.Contains("\"Billing street\"", Record(registry, label), StringComparison.Ordinal);
        var shippingLabel = RenderContext.FromLayout(form.FindByQualifiedName("ShippingAddress.StreetLabel")!, layout);
        Assert.Contains("\"Street\"", Record(registry, shippingLabel), StringComparison.Ordinal);
    }

    private static string Record(RendererRegistry registry, RenderContext ctx)
    {
        var canvas = new RecordingCanvas();
        registry.Resolve(ctx.Info.RendererId).Render(ctx, canvas);
        return canvas.ToText();
    }
}
