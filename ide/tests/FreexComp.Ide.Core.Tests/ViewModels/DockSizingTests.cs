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
}
