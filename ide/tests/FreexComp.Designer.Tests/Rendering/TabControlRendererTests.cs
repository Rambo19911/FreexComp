using FreexComp.Designer.Layout;
using FreexComp.Designer.Rendering;
using static FreexComp.Designer.Tests.Layout.LayoutHarness;

namespace FreexComp.Designer.Tests.Rendering;

/// <summary>
/// The <c>pagecontrol</c> and <c>tabcontrol</c> renderers (design-renderers.md DSG-RND-021, DSG-RND-022; D-148) with the
/// framework metadata: tabs at the laid-out rectangles of DSG-RND-020 for every position and style.
/// </summary>
public sealed class TabControlRendererTests
{
    private const string PageSheets = "object S1: TTabSheet\n  Caption = '&One'\nend\nobject S2: TTabSheet\n  Caption = 'Two'\nend\n"
        + "object S3: TTabSheet\n  Caption = 'Three'\nend\n";

    private const string StripTabs = "Tabs = (\n'&One'\n'Two'\n'Three'\n)\n";

    private static readonly string[] Captions = ["&One", "Two", "Three"];

    public static TheoryData<string, string, string> Configurations()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var className in (string[])["TPageControl", "TTabControl"])
        {
            foreach (var position in (string[])["tpTop", "tpBottom", "tpLeft", "tpRight"])
            {
                foreach (var style in (string[])["tsTabs", "tsButtons", "tsFlatButtons"])
                {
                    data.Add(className, position, style);
                }
            }
        }

        return data;
    }

    /// <summary>
    /// Every tab is filled at its drawing rectangle (<c>form.window</c> for the selected one, <c>form.face</c> otherwise),
    /// captioned with its mnemonic — rotated to read upwards on the left and downwards on the right; <c>tsTabs</c> draws
    /// the body frame (the display rectangle grown by 4, by 2 on the strip side) and the selected tab last, open towards
    /// the body; the button styles draw no body frame and sink the selected button (dark top and left edges); flat
    /// buttons other than the selected one have no frame.
    /// </summary>
    [Theory]
    [MemberData(nameof(Configurations))]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-RND-021")]
    [Trait("Req", "DSG-RND-022")]
    public void DrawsTabsAtEveryPosition(string className, string position, string style)
    {
        var page = className == "TPageControl";
        var properties = $"TabPosition = {position}\nStyle = {style}\nTabWidth = 56\n" + (page ? "ActivePage = S2\n" : StripTabs + "TabIndex = 1\n");
        var form = Form(400, 300, Obj("X", className, 8, 8, 289, 193, "ParentFont = False\nFont.Height = -12\n" + properties, page ? PageSheets : ""));
        var ctx = RenderContext.FromLayout(form.FindByName("X")!, Engine.Compute(form));
        var canvas = new RecordingCanvas();
        RenderHarness.Registry.Resolve(page ? "pagecontrol" : "tabcontrol").Render(ctx, canvas);
        var ops = canvas.Ops.Select(o => o.Text).ToList();
        var p = ctx.Palette;

        var strip = Assert.IsType<TabStripGeometry>(ctx.TabStrip);
        Assert.Equal((3, 1, 1), (strip.TabRects.Count, strip.RowCount, strip.SelectedTab));
        Assert.Equal($"FillRect 0,0,289,193 {ctx.ParentFill}", ops[0]);

        // The body frame of tsTabs, one row on the strip side: 2 there, 4 elsewhere (one column of 56-pixel tabs fits 193 in every style: flat buttons 3 · 56 + 2 · 10 + 3 = 191).
        var frame = $"DrawRect {R(TabStripRenderer.BodyFrame(strip))} {p.Border} 1 Solid";
        if (style == "tsTabs")
        {
            Assert.Equal(frame, ops[1]);
            Assert.Equal(position switch
            {
                "tpTop" => new Rect(0, 23, 289, 170),
                "tpBottom" => new Rect(0, 0, 289, 170),
                "tpLeft" => new Rect(23, 0, 266, 193),
                _ => new Rect(0, 0, 266, 193),
            }, TabStripRenderer.BodyFrame(strip));
        }
        else
        {
            Assert.DoesNotContain(ops, o => o.StartsWith("DrawRect 0,", StringComparison.Ordinal) && o.Contains(",193 ", StringComparison.Ordinal));
        }

        var rotation = position switch { "tpLeft" => " up", "tpRight" => " down", _ => "" };
        var fills = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            var r = strip.TabDrawRect(i);
            var fill = $"FillRect {R(r)} {(i == 1 ? p.Window : p.Face)}";
            Assert.Contains(fill, ops);
            fills.Add(ops.IndexOf(fill));
            Assert.Contains($"DrawText \"{Captions[i]}\" {R(r)} \"Segoe UI\" 12 {ctx.Ink} Center Center mnemonic{rotation}", ops);
            var outline = $"DrawRect {R(r)} {p.Border} 1 Solid";
            Assert.Equal(style == "tsButtons" || (style == "tsFlatButtons" && i == 1) || (style == "tsTabs" && i != 1), ops.Contains(outline));
        }

        var selected = strip.TabDrawRect(1);
        var sunken = FormattableString.Invariant($"DrawLine {selected.Left},{selected.Top + 0.5},{selected.Right},{selected.Top + 0.5} {p.BorderDark} 1 Solid");
        if (style == "tsTabs")
        {
            // The selected tab is grown by 2 and drawn after the others.
            Assert.Equal(strip.TabRects[1] with { Left = strip.TabRects[1].Left - 2, Top = strip.TabRects[1].Top - 2,
                Width = strip.TabRects[1].Width + 4, Height = strip.TabRects[1].Height + 4 }, strip.TabDrawRect(1));
            Assert.True(fills[1] > fills[0] && fills[1] > fills[2], string.Join("\n", ops));
            Assert.DoesNotContain(sunken, ops);
        }
        else
        {
            Assert.Equal(strip.TabRects[1], strip.TabDrawRect(1));
            Assert.Contains(sunken, ops);
        }
    }

    /// <summary>DSG-RND-022: a tab control without tabs draws only its fill and the body frame; without a TabIndex no tab is selected.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-RND-022")]
    public void TabControlWithoutTabsOrIndex()
    {
        var empty = Form(400, 300, Obj("X", "TTabControl", 8, 8, 289, 193));
        var ctx = RenderContext.FromLayout(empty.FindByName("X")!, Engine.Compute(empty));
        var canvas = new RecordingCanvas();
        new TabControlRenderer().Render(ctx, canvas);
        Assert.Equal([$"FillRect 0,0,289,193 {ctx.ParentFill}", $"DrawRect 0,0,289,193 {ctx.Palette.Border} 1 Solid"], canvas.Ops.Select(o => o.Text));

        var unselected = Form(400, 300, Obj("X", "TTabControl", 8, 8, 289, 193, StripTabs));
        ctx = RenderContext.FromLayout(unselected.FindByName("X")!, Engine.Compute(unselected));
        canvas = new RecordingCanvas();
        new TabControlRenderer().Render(ctx, canvas);
        Assert.Equal(-1, ctx.TabStrip!.SelectedTab);
        Assert.DoesNotContain(canvas.Ops, o => o.Text.StartsWith("FillRect", StringComparison.Ordinal) && o.Text.EndsWith(ctx.Palette.Window.ToString(), StringComparison.Ordinal));
        Assert.Equal(3, canvas.Ops.Count(o => o.Kind == "DrawText"));
    }

    private static string R(Rect r) => $"{r.Left},{r.Top},{r.Width},{r.Height}";
}
