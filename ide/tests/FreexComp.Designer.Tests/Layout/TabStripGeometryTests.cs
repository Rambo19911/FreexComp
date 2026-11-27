using FreexComp.Designer.Layout;

namespace FreexComp.Designer.Tests.Layout;

/// <summary>
/// D-147: the C# mirror of the tab-strip geometry (ui-framework <c>phase8/tab-control.md</c> UIF-1497 … 1501 at 96 DPI)
/// and the tab rectangles of DSG-RND-020. The cases are the spec's examples and its native measurements (§4).
/// </summary>
[Trait("Phase", "8")]
public sealed class TabStripGeometryTests
{
    // §4 examples: 289 × 193, TabH 21, TabWidth 60.
    private static TabStripGeometry Strip(int tabs, TabPosition position = TabPosition.Top, TabStyle style = TabStyle.Tabs,
        bool multiLine = false, bool scrollOpposite = false, int selected = -1, int width = 289, int height = 193, int length = 60) =>
        new(width, height, 21, Enumerable.Repeat(length, tabs).ToArray(), position, style, multiLine, scrollOpposite, selected);

    private static Rect Ltrb(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    [Fact]
    [Trait("Req", "DSG-LAY-070")]
    public void DisplayRectForEveryPosition()
    {
        Assert.Equal(Ltrb(4, 25, 285, 189), Strip(1).DisplayRect);
        Assert.Equal(Ltrb(4, 46, 285, 189), Strip(6, multiLine: true).DisplayRect);
        Assert.Equal(Ltrb(4, 4, 285, 168), Strip(1, TabPosition.Bottom).DisplayRect);
        Assert.Equal(Ltrb(88, 4, 285, 189), Strip(12, TabPosition.Left).DisplayRect);
        Assert.Equal(Ltrb(4, 4, 264, 189), Strip(1, TabPosition.Right).DisplayRect);
        Assert.Equal(Ltrb(4, 73, 285, 189), Strip(12, style: TabStyle.Buttons, multiLine: true).DisplayRect);
        Assert.Equal(Ltrb(4, 46, 285, 168), Strip(12, multiLine: true, scrollOpposite: true, selected: 5).DisplayRect);
        Assert.Equal(Ltrb(4, 4, 285, 189), Strip(0).DisplayRect);
        Assert.Equal(0, Strip(0).RowCount); // DSG-LAY-070: without visible tabs R = 0

        // Corpus values (UIF-1520): left / right one column of 3; bottom one row; the clamp of tiny controls.
        Assert.Equal(new Rect(25, 4, 260, 185), Strip(3, TabPosition.Left).DisplayRect);
        Assert.Equal(new Rect(4, 4, 260, 185), Strip(3, TabPosition.Right).DisplayRect);
        Assert.Equal(new Rect(4, 25, 92, 21), Strip(1, width: 100, height: 50).DisplayRect);
        Assert.Equal(new Rect(4, 25, 0, 0), Strip(1, width: 6, height: 20).DisplayRect);

        // TabH: TabHeight if > 0, else Abs(Font.Height) + 9.
        Assert.Equal(21, TabStripGeometry.TabThickness(0, -12));
        Assert.Equal(29, TabStripGeometry.TabThickness(0, -20));
        Assert.Equal(30, TabStripGeometry.TabThickness(30, -12));
        Assert.Equal(new Rect(4, 34, 281, 155), new TabStripGeometry(289, 193, 30, [60]).DisplayRect);
    }

    [Fact]
    [Trait("Req", "DSG-LAY-071")]
    public void RowsPackLikeTheNativeControl()
    {
        // Measured row thresholds (§4): 4 tabs of 60 fit one row from W = 245 (tsTabs), 252 (tsButtons), 273 (tsFlatButtons, top).
        foreach (var (style, threshold) in new[] { (TabStyle.Tabs, 245), (TabStyle.Buttons, 252), (TabStyle.FlatButtons, 273) })
        {
            Assert.Equal(1, Strip(4, style: style, multiLine: true, width: threshold).RowCount);
            Assert.Equal(2, Strip(4, style: style, multiLine: true, width: threshold - 1).RowCount);
        }

        // 2 tabs fit from 125 / 126 / 133.
        foreach (var (style, threshold) in new[] { (TabStyle.Tabs, 125), (TabStyle.Buttons, 126), (TabStyle.FlatButtons, 133) })
        {
            Assert.Equal(1, Strip(2, style: style, multiLine: true, width: threshold).RowCount);
            Assert.Equal(2, Strip(2, style: style, multiLine: true, width: threshold - 1).RowCount);
        }

        // 3 vertical tabs fit one column from H = 185 (tsTabs) and 189 (both button styles); flat buttons use Sa = 3 off the top.
        foreach (var (style, threshold) in new[] { (TabStyle.Tabs, 185), (TabStyle.Buttons, 189), (TabStyle.FlatButtons, 189) })
        {
            foreach (var position in new[] { TabPosition.Left, TabPosition.Right })
            {
                Assert.Equal(1, Strip(3, position, style, height: threshold).RowCount);
                Assert.Equal(2, Strip(3, position, style, height: threshold - 1).RowCount);
            }
        }

        // Greedy packing in order; a single tab longer than the strip still makes its own row; not multi-line → one row.
        var mixed = new TabStripGeometry(289, 193, 21, [100, 100, 100, 300, 50], multiLine: true);
        Assert.Equal([0, 0, 1, 2, 3], mixed.RowOf);
        Assert.Equal(4, mixed.RowCount);
        Assert.Equal(1, Strip(12).RowCount);
        Assert.All(Strip(12).RowOf, r => Assert.Equal(0, r));
        Assert.True(Strip(1, TabPosition.Left).EffectiveMultiLine); // vertical styles exist only with TCS_MULTILINE
        Assert.Equal(4, Strip(12, TabPosition.Right).RowCount);

        // Automatic lengths (UIF-1498): raw text extent + 12, + 22 with a 16-pixel image; minimum 48 unless TabWidth is set.
        Assert.Equal(80, TabStripGeometry.TabLength(0, 68, 0));
        Assert.Equal(48, TabStripGeometry.TabLength(0, 5, 0));
        Assert.Equal(102, TabStripGeometry.TabLength(0, 68, 16 + TabStripGeometry.ImageGap));
        Assert.Equal(30, TabStripGeometry.TabLength(30, 68, 22)); // the fixed width wins over MinW
        // The headless estimate of DSG-LAY-071: Ceiling(0.55 · Abs(FontHeight) · length), raw caption with '&'.
        Assert.Equal(60, TabStripGeometry.EstimateTextExtent("&Mnemonic", -12));
        Assert.Equal(0, TabStripGeometry.EstimateTextExtent("", -12));
    }

    [Fact]
    [Trait("Req", "DSG-LAY-072")]
    public void ScrollOppositeSplitsRows()
    {
        // 12 tabs in 3 rows of 4; the selected tab's row K: rows 0 … K − 1 lie on the opposite side (UIF-1500).
        Assert.Equal(1, Strip(12, multiLine: true, scrollOpposite: true, selected: 5).OppositeRows);
        Assert.Equal(Ltrb(4, 67, 285, 189), Strip(12, multiLine: true, scrollOpposite: true, selected: 0).DisplayRect);
        Assert.Equal(Ltrb(4, 46, 285, 168), Strip(12, multiLine: true, scrollOpposite: true, selected: 4).DisplayRect);
        Assert.Equal(Ltrb(4, 25, 285, 147), Strip(12, multiLine: true, scrollOpposite: true, selected: 11).DisplayRect);

        // Bottom and the vertical sides mirror the split.
        Assert.Equal(Ltrb(4, 25, 285, 147), Strip(12, TabPosition.Bottom, multiLine: true, scrollOpposite: true, selected: 5).DisplayRect);
        Assert.Equal(Ltrb(4 + 21 + 21 + 21, 4, 289 - 25, 189), Strip(12, TabPosition.Left, scrollOpposite: true, selected: 3).DisplayRect);
        Assert.Equal(Ltrb(25, 4, 289 - 4 - 63, 189), Strip(12, TabPosition.Right, scrollOpposite: true, selected: 3).DisplayRect);

        // K = 0: no selected tab, not multi-line, or a button style (TCS_SCROLLOPPOSITE is never sent then, UIF-1492).
        Assert.Equal(0, Strip(12, multiLine: true, scrollOpposite: true).OppositeRows);
        Assert.Equal(0, Strip(12, scrollOpposite: true, selected: 5).OppositeRows);
        Assert.Equal(0, Strip(12, style: TabStyle.Buttons, multiLine: true, scrollOpposite: true, selected: 11).OppositeRows);
        Assert.Equal(0, Strip(12, multiLine: true, selected: 11).OppositeRows);

        // Corpus E49: 6 tabs of 100 in rows 2 + 2 + 2, S3 (index 2) selected → K = 1.
        Assert.Equal(new Rect(4, 46, 281, 122), Strip(6, multiLine: true, scrollOpposite: true, selected: 2, length: 100).DisplayRect);
    }

    [Fact]
    [Trait("Req", "DSG-RND-020")]
    public void TabRectsFollowMeasuredLayout()
    {
        // tsTabs, one top row: rows start 2 inside the strip edge, tabs at 2 along, abutting; the selected one is drawn grown by 2.
        var top = Strip(3, selected: 1);
        Assert.Equal([new Rect(2, 2, 60, 21), new Rect(62, 2, 60, 21), new Rect(122, 2, 60, 21)], top.TabRects);
        Assert.Equal(new Rect(60, 0, 64, 25), top.TabDrawRect(1));
        Assert.Equal(new Rect(2, 2, 60, 21), top.TabDrawRect(0));

        // Bottom, left, right: the same layout turned to the strip side.
        Assert.Equal(new Rect(2, 193 - 2 - 21, 60, 21), Strip(1, TabPosition.Bottom).TabRects[0]);
        Assert.Equal([new Rect(2, 2, 21, 60), new Rect(2, 62, 21, 60)], Strip(2, TabPosition.Left).TabRects);
        Assert.Equal(new Rect(289 - 2 - 21, 2, 21, 60), Strip(1, TabPosition.Right).TabRects[0]);

        // Three rows, tab 0 selected → outer to inner rows 1, 2, 0 (measured); no selection → rows outwards from R − 1.
        var rotated = Strip(12, multiLine: true, selected: 0);
        Assert.Equal(2 + 2 * 21, rotated.TabRects[0].Top);  // row 0 innermost
        Assert.Equal(2, rotated.TabRects[4].Top);           // row 1 outermost
        Assert.Equal(2 + 21, rotated.TabRects[8].Top);      // row 2 between
        var middle = Strip(12, multiLine: true, selected: 5);
        Assert.Equal([2, 2 + 2 * 21, 2 + 21], new[] { middle.TabRects[0].Top, middle.TabRects[4].Top, middle.TabRects[8].Top });
        var none = Strip(12, multiLine: true);
        Assert.Equal([2, 2 + 21, 2 + 2 * 21], new[] { none.TabRects[0].Top, none.TabRects[4].Top, none.TabRects[8].Top });
        Assert.Equal([2, 62, 122, 182], none.TabRects.Take(4).Select(r => r.Left));

        // ScrollOpposite, tab 5 selected (row 1): strip side rows 1, 2 outwards from the display rect, row 0 below it.
        var split = Strip(12, multiLine: true, scrollOpposite: true, selected: 5);
        Assert.Equal(2 + 21, split.TabRects[4].Top);        // row 1 innermost at the top
        Assert.Equal(2, split.TabRects[8].Top);             // row 2 outside it
        Assert.Equal(193 - 2 - 21, split.TabRects[0].Top);  // row 0 on the opposite side, outermost

        // Button styles: packing order, row 0 outermost; rows j·(TabH + 3) from the edge; tabs from 0 along with Sa between.
        var buttons = Strip(12, style: TabStyle.Buttons, multiLine: true, selected: 0);
        Assert.Equal([0, 24, 48], new[] { buttons.TabRects[0].Top, buttons.TabRects[4].Top, buttons.TabRects[8].Top });
        Assert.Equal([0, 63, 126, 189], buttons.TabRects.Take(4).Select(r => r.Left));
        Assert.Equal(buttons.TabRects[0], buttons.TabDrawRect(0)); // only tsTabs grows the selected tab
        Assert.Equal([0, 70, 140], Strip(3, style: TabStyle.FlatButtons, multiLine: true).TabRects.Select(r => r.Left));
        Assert.Equal([2, 65], Strip(2, style: TabStyle.Buttons).TabRects.Select(r => r.Left)); // single-line buttons from 2
        Assert.Equal([0, 63], Strip(2, TabPosition.Left, TabStyle.FlatButtons).TabRects.Select(r => r.Top)); // Sa 3 off the top

        // The display rectangle never overlaps a tab and keeps its distance to the nearest one (UIF-1503).
        foreach (var position in Enum.GetValues<TabPosition>())
        {
            foreach (var style in Enum.GetValues<TabStyle>())
            {
                var g = Strip(12, position, style, multiLine: true, selected: 5);
                Assert.All(g.TabRects, r => Assert.False(Overlaps(r, g.DisplayRect), $"{position} {style} {r} overlaps {g.DisplayRect}"));
            }
        }
    }

    private static bool Overlaps(Rect a, Rect b) => a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;
}
