using FreexComp.Designer.Layout;
using static FreexComp.Designer.Tests.Layout.LayoutHarness;

namespace FreexComp.Designer.Tests.Layout;

/// <summary>D-147: the layout engine feeds <see cref="TabStripGeometry"/> from the stored tab-strip inputs (DSG-LAY-069 … 072).</summary>
[Trait("Phase", "8")]
public sealed class LayoutEngineTabStripTests
{
    private static string Sheets(params string[] lines) =>
        string.Concat(lines.Select((l, i) => $"object S{i + 1}: TTabSheet\n" + Indent(l, 2) + "end\n"));

    private static string Book(string properties, string sheets) =>
        Obj("Book", "TPageControl", 0, 0, 289, 193, "Font.Height = -12\nParentFont = False\n" + properties, sheets);

    [Fact]
    [Trait("Req", "DSG-LAY-069")]
    [Trait("Req", "DSG-LAY-071")]
    public void AutomaticLengthsUseTheCanvasMeasurementOrTheEstimate()
    {
        // Estimate: 'Settings and more' (17 characters) → Ceiling(0.55 · 12 · 17) = 113, tab 125; two fit a row of 289.
        const string caption = "Caption = 'Settings and more'";
        var form = Form(400, 300, Book("MultiLine = True", Sheets(caption, caption, caption, caption)));
        Assert.Equal(new Rect(4, 46, 281, 143), Bounds(Engine.Compute(form), form, "S1"));

        // The surface's canvas measurement replaces the estimate: 200 + 12 per tab → one tab per row, 4 rows.
        var measured = Engine;
        var calls = new List<string>();
        measured.TabTextWidth = (obj, text) =>
        {
            Assert.Equal("Book", obj.Name);
            calls.Add(text);
            return 200;
        };
        Assert.Equal(new Rect(4, 88, 281, 101), Bounds(measured.Compute(form), form, "S1"));
        Assert.Contains("Settings and more", calls);

        // A sheet without TabVisible has no tab; the caption is measured with its mnemonic prefix processed (UIF-1498, U-176).
        form = Form(400, 300, Book("MultiLine = True",
            Sheets(caption, "Caption = '&Hidden one'\nTabVisible = False", caption, "Caption = '&Wide'")));
        calls.Clear();
        _ = measured.Compute(form);
        Assert.DoesNotContain("&Hidden one", calls);
        Assert.Contains("Wide", calls);
        Assert.DoesNotContain("&Wide", calls);
        Assert.Equal(new Rect(4, 46, 281, 143), Bounds(Engine.Compute(form), form, "S2")); // estimate: rows 125 + 125 | 48
    }

    [Fact]
    [Trait("Req", "DSG-LAY-069")]
    [Trait("Req", "DSG-LAY-071")]
    public void ImagesWidenTabsWithAnImageIndex()
    {
        // Two tabs of 'Alpha' (estimate 33 → 48 by MinW); with a 64-pixel image list and image index 0: 33 + 12 + 70 = 115 each.
        var plain = Form(400, 300, Book("MultiLine = True", Sheets("Caption = 'Alpha'", "Caption = 'Alpha'")));
        Assert.Equal(new Rect(4, 25, 281, 164), Bounds(Engine.Compute(plain), plain, "S1"));
        var narrow = Obj("Narrow", "TPageControl", 0, 0, 200, 193, "Font.Height = -12\nParentFont = False\nMultiLine = True\nImages = Icons",
            Sheets("Caption = 'Alpha'\nImageIndex = 0", "Caption = 'Alpha'\nImageIndex = 0"));
        var form = Form(400, 300, "object Icons: TImageList\n  Width = 64\nend\n" + narrow);
        Assert.Equal(new Rect(4, 46, 192, 143), Bounds(Engine.Compute(form), form, "S1")); // 115 + 115 + 5 > 200
        form = Form(400, 300, "object Icons: TImageList\n  Width = 64\nend\n"
            + narrow.Replace("ImageIndex = 0", "ImageIndex = -1"));
        Assert.Equal(new Rect(4, 25, 192, 164), Bounds(Engine.Compute(form), form, "S1")); // no image: 48 + 48 + 5
        form = Form(400, 300, "object Icons: TImageList\nend\n" + narrow);
        Assert.Equal(new Rect(4, 25, 192, 164), Bounds(Engine.Compute(form), form, "S1")); // default Width 16: 67 + 67 + 5
    }

    [Fact]
    [Trait("Req", "DSG-LAY-069")]
    [Trait("Req", "DSG-LAY-072")]
    public void StepsSetTabStripInputs()
    {
        var form = Form(400, 300, Book("", Sheets("", "", "")) + Obj("P", "TPanel", 300, 0, 50, 50));
        var results = Engine.ComputeWithSteps(form,
            "set Book TabPosition tpLeft\ndump\nset Book TabWidth 70\ndump\nset Book TabHeight 30\ndump\nset Book MultiLine True\n"
            + "set Book TabPosition tpTop\ndump\nset Book Style tsButtons\ndump\n");
        Assert.Equal(new Rect(25, 4, 260, 185), Bounds(results[0], form, "S1"));  // 3 automatic tabs of 48 in one column
        Assert.Equal(new Rect(46, 4, 239, 185), Bounds(results[1], form, "S1"));  // 3 × 70 + 5 > 193: two columns
        Assert.Equal(new Rect(64, 4, 221, 185), Bounds(results[2], form, "S1"));  // two columns of TabH 30: 2 · 30 + 4
        Assert.Equal(new Rect(4, 34, 281, 155), Bounds(results[3], form, "S1"));  // 3 × 70 + 5 ≤ 289: one row
        Assert.Equal(new Rect(4, 34, 281, 155), Bounds(results[4], form, "S1"));  // 3 × 70 + 2 · 3 + 3 ≤ 289

        // UIF-334: bad values and tab-strip properties on other controls are errors.
        Assert.Throws<FormatException>(() => Engine.ComputeWithSteps(form, "set Book TabPosition tpMiddle\n"));
        Assert.Throws<FormatException>(() => Engine.ComputeWithSteps(form, "set Book Style csSimple\n"));
        Assert.Throws<FormatException>(() => Engine.ComputeWithSteps(form, "set Book MultiLine Yes\n"));
        Assert.Throws<FormatException>(() => Engine.ComputeWithSteps(form, "set P TabPosition tpLeft\n"));
    }
}
