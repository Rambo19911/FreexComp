using FreexComp.Designer.Layout;
using static FreexComp.Designer.Tests.Layout.LayoutHarness;

namespace FreexComp.Designer.Tests.Layout;

/// <summary>Anchors, resize and recursion (D-032).</summary>
public sealed partial class LayoutEngineTests
{
    /// <summary>Every combination of the two horizontal and the two vertical anchors.</summary>
    public static TheoryData<string> AnchorSets()
    {
        var data = new TheoryData<string>();
        foreach (var h in new[] { "", "akLeft", "akRight", "akLeft, akRight" })
        {
            foreach (var v in new[] { "", "akTop", "akBottom", "akTop, akBottom" })
            {
                data.Add(string.Join(", ", new[] { h, v }.Where(s => s.Length > 0)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AnchorSets))]
    [Trait("Phase", "5")]
    [Trait("Req", "UIF-231")]
    [Trait("Req", "DSG-LAY-020")]
    public void AnchorsOnResize(string anchors)
    {
        const int bl = 30, bt = 250, bw = 81, bh = 31, pw0 = 400, ph0 = 300, pw = 557, ph = 200;
        var form = Form(pw0, ph0, Obj("C", "TPanel", bl, bt, bw, bh, $"Anchors = [{anchors}]"));
        var set = anchors.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        // Independent statement of the UIF-231 table with the integer MulDiv of layout.md §1.
        static (int Pos, int Size) Axis(bool near, bool far, int b, int s, int p0, int p)
        {
            var d = p - p0;
            return (near, far) switch
            {
                (true, false) => (b, s),
                (true, true) => (b, Math.Max(0, s + d)),
                (false, true) => (b + d, s),
                _ => (MulDiv(b + s / 2, p, p0) - s / 2, s),
            };
        }

        var (l, w) = Axis(set.Contains("akLeft"), set.Contains("akRight"), bl, bw, pw0, pw);
        var (t, h) = Axis(set.Contains("akTop"), set.Contains("akBottom"), bt, bh, ph0, ph);
        var r = Engine.ComputeWithResize(form, "LayoutForm", pw, ph);
        Assert.Equal(new Rect(l, t, w, h), Bounds(r, form, "C"));

        // The same through the steps grammar (run-time mode, two explicit changes: width, then height).
        var stepped = Engine.ComputeWithSteps(form, $"client {pw} {ph}\ndump\n");
        Assert.Equal(new Rect(l, t, w, h), Bounds(Assert.Single(stepped), form, "C"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-LAY-021")]
    [Trait("Req", "UIF-232")]
    public void AnchorsAreRelativeToStoredBounds()
    {
        // E21: shrinking clamps the width to 0, growing back restores the stored bounds exactly (no drift).
        var form = Form(400, 300, Obj("X", "TPanel", 16, 16, 200, 23, "Anchors = [akLeft, akTop, akRight]")
            + Obj("Y", "TPanel", 150, 100, 101, 51, "Anchors = []"));
        var small = Engine.ComputeWithResize(form, "LayoutForm", 150, 300);
        Assert.Equal(new Rect(16, 16, 0, 23), Bounds(small, form, "X"));
        foreach (var width in new[] { 150, 173, 611, 399 })
        {
            Engine.ComputeWithResize(form, "LayoutForm", width, 177);
        }

        var back = Engine.ComputeWithResize(form, "LayoutForm", 400, 300);
        Assert.Equal(new Rect(16, 16, 200, 23), Bounds(back, form, "X"));
        Assert.Equal(new Rect(150, 100, 101, 51), Bounds(back, form, "Y"));
        Assert.Empty(back.Differences());

        // The steps run the same way: E21 and E23 (an explicit change re-captures the baseline).
        var dumps = Engine.ComputeWithSteps(form, "client 150 300\ndump\nclient 400 300\ndump\nset X Left 30\nclient 500 300\ndump\n");
        Assert.Equal(new Rect(16, 16, 0, 23), Bounds(dumps[0], form, "X"));
        Assert.Equal(new Rect(16, 16, 200, 23), Bounds(dumps[1], form, "X"));
        Assert.Equal(new Rect(30, 16, 300, 23), Bounds(dumps[2], form, "X"));

        // A hand-edited file whose container is stored smaller than its aligned size: the child's baseline is the
        // stored panel client size, so the load-time pass moves the anchored child (UIF-301, A-LAY-11).
        var inconsistent = Form(400, 300, Obj("P", "TPanel", 0, 0, 300, 200, "Align = alClient\nBevelOuter = bvNone",
            Obj("Btn", "TButton", 200, 160, 75, 25, "Anchors = [akRight, akBottom]")));
        var loaded = Engine.Compute(inconsistent);
        Assert.Equal(new Rect(300, 260, 75, 25), Bounds(loaded, inconsistent, "Btn"));
        Assert.Equal(2, loaded.Differences().Count);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-LAY-023")]
    [Trait("Req", "UIF-227")]
    public void NestedContainersRecurse()
    {
        // E24 one level deeper: Outer (alClient) > Inner (alBottom, anchored children) > Btn.
        var form = Form(400, 300,
            Obj("Outer", "TPanel", 0, 0, 400, 300, "Align = alClient\nBevelOuter = bvNone",
                Obj("Inner", "TPanel", 0, 200, 400, 100, "Align = alBottom\nBevelOuter = bvNone",
                    Obj("Btn", "TButton", 300, 60, 75, 25, "Anchors = [akRight, akBottom]")
                    + Obj("Fill", "TPanel", 10, 10, 50, 50, "Align = alLeft"))
                + Obj("Side", "TPanel", 350, 20, 30, 30, "Anchors = [akTop, akRight]")
                + Obj("Fixed", "TPanel", 10, 10, 100, 100, "BevelOuter = bvNone",
                    Obj("Deep", "TPanel", 50, 50, 40, 40, "Anchors = [akRight, akBottom]"))));
        var r = Engine.ComputeWithResize(form, "LayoutForm", 640, 480);
        Assert.Equal(new Rect(0, 0, 640, 480), Bounds(r, form, "Outer"));
        Assert.Equal(new Rect(0, 380, 640, 100), Bounds(r, form, "Inner"));
        Assert.Equal(new Rect(540, 60, 75, 25), Bounds(r, form, "Btn"));
        Assert.Equal(new Rect(0, 0, 50, 100), Bounds(r, form, "Fill"));
        Assert.Equal(new Rect(590, 20, 30, 30), Bounds(r, form, "Side"));
        Assert.Equal(new Rect(10, 10, 100, 100), Bounds(r, form, "Fixed"));
        Assert.Equal(new Rect(50, 50, 40, 40), Bounds(r, form, "Deep")); // its container did not change size
        Assert.Equal(new Rect(540, 380 + 60, 75, 25), r.GetSurfaceBounds(form.FindByName("Btn")!));

        // Resizing an inner container directly: only its subtree and its parent's pass are involved.
        var inner = Engine.ComputeWithResize(form, "Fixed", 160, 130);
        Assert.Equal(new Rect(10, 10, 160, 130), Bounds(inner, form, "Fixed"));
        Assert.Equal(new Rect(110, 80, 40, 40), Bounds(inner, form, "Deep"));

        // A drag preview that moves Btn into Fixed (it becomes Fixed's last child) and changes an alignment.
        var preview = Engine.ComputeWithOverrides(form, new Dictionary<string, LayoutOverride>
        {
            ["Btn"] = new(Bounds: new Rect(5, 5, 75, 25), ParentName: "Fixed"),
            ["Side"] = new(Align: "alRight"),
        });
        Assert.Equal(new Rect(5, 5, 75, 25), Bounds(preview, form, "Btn"));
        Assert.Equal(new Rect(10 + 5, 10 + 5, 75, 25), preview.GetSurfaceBounds(form.FindByName("Btn")!));
        Assert.Equal(new Rect(370, 0, 30, 200), Bounds(preview, form, "Side")); // alRight below the alBottom Inner
        var outer = Assert.Single(preview.ToDump().Controls);
        Assert.Equal(["Inner", "Side", "Fixed"], outer.Controls.Select(c => c.Name));
        Assert.Equal(["Fill"], outer.Controls[0].Controls.Select(c => c.Name));
        Assert.Equal(["Deep", "Btn"], outer.Controls[2].Controls.Select(c => c.Name));
    }

    private static int MulDiv(int a, int b, int c)
    {
        var p = (long)a * b;
        return (int)(p >= 0 ? (p + c / 2) / c : (p - c / 2) / c);
    }
}
