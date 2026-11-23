using FreexComp.Designer.Surface;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Scrolling a selection made from outside into view (design-surface.md DSG-SUR-030; D-113).</summary>
[Trait("Phase", "5")]
public sealed class SurfaceScrollTests
{
    [Theory]
    [Trait("Req", "DSG-SUR-030")]
    [InlineData(0, 0, 100, 100, 50, 40, 0, 0)] // fully visible with the margin: no scroll
    [InlineData(0, 0, 900, 140, 50, 40, 158, 0)] // right of the view: the right edge + 8 at the view's right edge
    [InlineData(0, 0, 100, 600, 50, 40, 0, 48)] // below: the bottom edge + 8 at the view's bottom
    [InlineData(400, 300, 300, 200, 50, 40, 292, 192)] // left of and above the view: its top-left − 8 at the top-left
    [InlineData(0, 0, 100, 100, 795, 40, 92, 0)] // wider than the view (with margins): its left − 8 at the view's left
    [InlineData(0, 0, 4, 4, 50, 40, 0, 0)] // the margin is clamped at the surface origin
    public void MinimumScrollShowsThePrimaryWithAnEightDipMargin(double offsetX, double offsetY, int left, int top, int width, int height,
        double expectedX, double expectedY)
    {
        var view = new SurfaceViewport(offsetX, offsetY, 800, 600);
        Assert.Equal((expectedX, expectedY), SurfaceScroll.EnsureVisible(view, new Rect(left, top, width, height)));
    }
}
