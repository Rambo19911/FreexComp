using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Tests.ViewModels;

[Trait("Phase", "1")]
public sealed class DockSizingTests
{
    [Fact]
    [Trait("Req", "UI-WIN-060")]
    public void SqueezeTakesDocumentThenRightThenLeftThenBottom()
    {
        // Enough room: stored sizes, the document area gets the rest.
        Assert.Equal((260, 300), DockSizing.Widths(1440, 260, 300, true, true, 5));
        // 260 + 300 + 10 + 320 = 890: below that the right dock gives way first, down to 160 ...
        Assert.Equal((260, 250), DockSizing.Widths(840, 260, 300, true, true, 5));
        Assert.Equal((260, 160), DockSizing.Widths(750, 260, 300, true, true, 5));
        // ... then the left dock, down to 160.
        Assert.Equal((200, 160), DockSizing.Widths(690, 260, 300, true, true, 5));
        Assert.Equal((160, 160), DockSizing.Widths(500, 260, 300, true, true, 5));
        // Hidden docks take nothing; a shown left dock alone keeps its size while it fits.
        Assert.Equal((0, 0), DockSizing.Widths(800, 260, 300, false, false, 5));
        Assert.Equal((260, 0), DockSizing.Widths(800, 260, 300, true, false, 5));

        // Bottom: the document area keeps 160 while the bottom dock can shrink to 80.
        Assert.Equal(200, DockSizing.BottomHeight(500, 200, true, 5));
        Assert.Equal(135, DockSizing.BottomHeight(300, 200, true, 5));
        Assert.Equal(80, DockSizing.BottomHeight(150, 200, true, 5));
        Assert.Equal(0, DockSizing.BottomHeight(500, 200, false, 5));
    }

    [Fact]
    [Trait("Req", "UI-WIN-005")]
    public void DraggedSizesStayWithinTheLimits()
    {
        // Side docks: 160 … available − 320 − the other side dock − the splitters.
        Assert.Equal(810, DockSizing.ClampSide(2000, 1440, 300, 10));
        Assert.Equal(160, DockSizing.ClampSide(20, 1440, 300, 10));
        Assert.Equal(400, DockSizing.ClampSide(400, 1440, 300, 10));
        Assert.Equal(160, DockSizing.ClampSide(400, 600, 300, 10)); // no room at all: the minimum wins

        // Bottom dock: 80 … available − 160 − the splitter.
        Assert.Equal(635, DockSizing.ClampBottom(2000, 800, 5));
        Assert.Equal(80, DockSizing.ClampBottom(10, 800, 5));

        // Right-dock split: each group at least 80 high.
        Assert.Equal(80 / 495.0, DockSizing.ClampSplit(0, 500, 5), 9);
        Assert.Equal(1 - (80 / 495.0), DockSizing.ClampSplit(1, 500, 5), 9);
        Assert.Equal(0.3, DockSizing.ClampSplit(0.3, 500, 5), 9);
    }
}
