using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using FreexComp.Designer.Rendering;
using static FreexComp.Designer.Tests.Layout.LayoutHarness;

namespace FreexComp.Designer.Tests.Layout;

/// <summary>The layout engine (layout-engine.md; D-031 … D-033) against the runtime rules of ui-framework layout.md.</summary>
public sealed partial class LayoutEngineTests
{
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-LAY-001")]
    [Trait("Req", "DSG-LAY-021")]
    public void WorkedExampleFromSpec()
    {
        var form = Form(400, 240,
            Obj("Top1", "TPanel", 0, 0, 400, 30, "Align = alTop")
            + Obj("Left1", "TPanel", 0, 30, 100, 184, "Align = alLeft")
            + Obj("Bottom1", "TPanel", 3, 217, 394, 20, "Align = alBottom\nAlignWithMargins = True")
            + Obj("Client1", "TPanel", 100, 30, 300, 184, "Align = alClient")
            + Obj("Btn", "TButton", 232, 15, 75, 25, "Anchors = [akRight, akBottom]"));

        var result = Engine.Compute(form);
        Assert.Equal(new Rect(0, 0, 400, 30), Bounds(result, form, "Top1"));
        Assert.Equal(new Rect(3, 217, 394, 20), Bounds(result, form, "Bottom1"));
        Assert.Equal(new Rect(0, 30, 100, 184), Bounds(result, form, "Left1"));
        Assert.Equal(new Rect(100, 30, 300, 184), Bounds(result, form, "Client1"));
        Assert.Equal(new Rect(232, 15, 75, 25), Bounds(result, form, "Btn"));
        Assert.Empty(result.Differences());

        var resized = Engine.ComputeWithResize(form, "LayoutForm", 500, 300);
        Assert.Equal(new Rect(0, 0, 500, 300), resized.GetBounds(form.Root));
        Assert.Equal(new Rect(0, 0, 500, 30), Bounds(resized, form, "Top1"));
        Assert.Equal(new Rect(3, 277, 494, 20), Bounds(resized, form, "Bottom1"));
        Assert.Equal(new Rect(0, 30, 100, 244), Bounds(resized, form, "Left1"));
        Assert.Equal(new Rect(100, 30, 400, 244), Bounds(resized, form, "Client1"));
        Assert.Equal(new Rect(332, 75, 75, 25), Bounds(resized, form, "Btn"));
        Assert.Empty(form.Diagnostics); // Compute is pure (DSG-LAY-004): the stored values are untouched
        Assert.Equal(new Rect(232, 15, 75, 25), PropertyAccess.GetBounds(form.FindByName("Btn")!));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-LAY-031")]
    [Trait("Req", "UIF-222")]
    public void AlignPhaseOrderAndPositionSortKeys()
    {
        // E05/E06: the phase order is fixed and independent of creation order.
        string[] five = ["T:alTop:0:0:10:30", "B:alBottom:0:0:10:20", "L:alLeft:0:0:50:10", "R:alRight:0:0:60:10", "C:alClient:0:0:10:10"];
        foreach (var order in new[] { five, five.Reverse().ToArray() })
        {
            var form = Form(400, 300, string.Concat(order.Select(Aligned)));
            var result = Engine.Compute(form);
            Assert.Equal(new Rect(0, 0, 400, 30), Bounds(result, form, "T"));
            Assert.Equal(new Rect(0, 280, 400, 20), Bounds(result, form, "B"));
            Assert.Equal(new Rect(0, 30, 50, 250), Bounds(result, form, "L"));
            Assert.Equal(new Rect(340, 30, 60, 250), Bounds(result, form, "R"));
            Assert.Equal(new Rect(50, 30, 290, 250), Bounds(result, form, "C"));
        }

        // E02: alTop by Top; E03: ties keep creation order.
        var byTop = Form(400, 300, Aligned("A:alTop:0:50:100:30") + Aligned("B:alTop:0:10:100:20"));
        var r = Engine.Compute(byTop);
        Assert.Equal(new Rect(0, 0, 400, 20), Bounds(r, byTop, "B"));
        Assert.Equal(new Rect(0, 20, 400, 30), Bounds(r, byTop, "A"));
        var tie = Form(400, 300, Aligned("A:alTop:0:0:50:30") + Aligned("B:alTop:0:0:50:40"));
        r = Engine.Compute(tie);
        Assert.Equal(new Rect(0, 0, 400, 30), Bounds(r, tie, "A"));
        Assert.Equal(new Rect(0, 30, 400, 40), Bounds(r, tie, "B"));

        // E04: alBottom by largest bottom edge first; alRight by largest right edge first.
        var bottoms = Form(400, 300, Aligned("A:alBottom:0:100:10:20") + Aligned("B:alBottom:0:250:10:30"));
        r = Engine.Compute(bottoms);
        Assert.Equal(new Rect(0, 270, 400, 30), Bounds(r, bottoms, "B"));
        Assert.Equal(new Rect(0, 250, 400, 20), Bounds(r, bottoms, "A"));
        var rights = Form(400, 300, Aligned("A:alRight:10:0:20:10") + Aligned("B:alRight:300:0:30:10"));
        r = Engine.Compute(rights);
        Assert.Equal(new Rect(370, 0, 30, 300), Bounds(r, rights, "B"));
        Assert.Equal(new Rect(350, 0, 20, 300), Bounds(r, rights, "A"));

        // E40 as a drag preview: dropping B left of A reorders the alLeft phase.
        var lefts = Form(400, 300, Aligned("A:alLeft:0:0:100:300") + Aligned("B:alLeft:100:0:50:300"));
        r = Engine.Compute(lefts);
        Assert.Equal(new Rect(0, 0, 100, 300), Bounds(r, lefts, "A"));
        Assert.Equal(new Rect(100, 0, 50, 300), Bounds(r, lefts, "B"));
        var preview = Engine.ComputeWithOverrides(lefts,
            new Dictionary<string, LayoutOverride> { ["B"] = new(Bounds: new Rect(-1, 0, 50, 300)) });
        Assert.Equal(new Rect(0, 0, 50, 300), Bounds(preview, lefts, "B"));
        Assert.Equal(new Rect(50, 0, 100, 300), Bounds(preview, lefts, "A"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "UIF-235")]
    [Trait("Req", "UIF-222")]
    public void AlignWithMargins()
    {
        // E07
        var e07 = Form(400, 300, Aligned("A:alTop:0:0:10:30", "AlignWithMargins = True") + Aligned("C:alClient:0:0:10:10"));
        var r = Engine.Compute(e07);
        Assert.Equal(new Rect(3, 3, 394, 30), Bounds(r, e07, "A"));
        Assert.Equal(new Rect(0, 36, 400, 264), Bounds(r, e07, "C"));

        // E08: custom margins.
        var e08 = Form(400, 300,
            Aligned("A:alLeft:0:0:100:10", "AlignWithMargins = True\nMargins.Left = 10\nMargins.Top = 5\nMargins.Right = 20\nMargins.Bottom = 15")
            + Aligned("C:alClient:0:0:10:10", "AlignWithMargins = True"));
        r = Engine.Compute(e08);
        Assert.Equal(new Rect(10, 5, 100, 280), Bounds(r, e08, "A"));
        Assert.Equal(new Rect(133, 3, 264, 294), Bounds(r, e08, "C"));

        // E36: bottom and right edges.
        var e36 = Form(400, 300, Aligned("B:alBottom:0:0:10:20", "AlignWithMargins = True")
            + Aligned("R:alRight:0:0:60:10", "AlignWithMargins = True") + Aligned("C:alClient:0:0:10:10"));
        r = Engine.Compute(e36);
        Assert.Equal(new Rect(3, 277, 394, 20), Bounds(r, e36, "B"));
        Assert.Equal(new Rect(337, 3, 60, 268), Bounds(r, e36, "R"));
        Assert.Equal(new Rect(0, 0, 334, 274), Bounds(r, e36, "C"));

        // Margins never affect alNone controls (UIF-235); E10: padding insets aligned children only.
        var padded = Form(400, 300, Obj("P", "TPanel", 0, 0, 400, 300,
            "BevelOuter = bvNone\nPadding.Left = 8\nPadding.Top = 8\nPadding.Right = 8\nPadding.Bottom = 8",
            Aligned("T:alTop:0:0:10:24") + Obj("N", "TPanel", 5, 5, 50, 20, "AlignWithMargins = True")));
        r = Engine.Compute(padded);
        Assert.Equal(new Rect(8, 8, 384, 24), Bounds(r, padded, "T"));
        Assert.Equal(new Rect(5, 5, 50, 20), Bounds(r, padded, "N"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "UIF-240")]
    [Trait("Req", "UIF-250")]
    public void ConstraintsClampSizes()
    {
        // E12 … E15: constrained sizes stay anchored at the phase's fixed edge.
        var form = Form(400, 300,
            Aligned("C:alClient:0:0:10:10", "Constraints.MaxWidth = 300\nConstraints.MaxHeight = 200"));
        var r = Engine.Compute(form);
        Assert.Equal(new Rect(0, 0, 300, 200), Bounds(r, form, "C"));
        form = Form(400, 300, Aligned("A:alTop:0:0:10:10", "Constraints.MinHeight = 25") + Aligned("B:alTop:0:50:100:20"));
        r = Engine.Compute(form);
        Assert.Equal(new Rect(0, 0, 400, 25), Bounds(r, form, "A"));
        Assert.Equal(new Rect(0, 25, 400, 20), Bounds(r, form, "B"));
        form = Form(400, 300, Aligned("R:alRight:0:0:80:10", "Constraints.MaxHeight = 100") + Aligned("C:alClient:0:0:10:10"));
        r = Engine.Compute(form);
        Assert.Equal(new Rect(320, 0, 80, 100), Bounds(r, form, "R"));
        Assert.Equal(new Rect(0, 0, 320, 300), Bounds(r, form, "C"));
        form = Form(400, 300, Aligned("B:alBottom:0:0:10:40", "Constraints.MaxWidth = 250"));
        r = Engine.Compute(form);
        Assert.Equal(new Rect(0, 260, 250, 40), Bounds(r, form, "B"));

        // Stored bounds pass through ConstrainSize while reading; the minimum wins over a contradictory maximum.
        form = Form(400, 300, Obj("N", "TPanel", 10, 10, 500, 5,
            "Constraints.MaxWidth = 200\nConstraints.MinHeight = 30\nConstraints.MaxHeight = 20"));
        r = Engine.Compute(form);
        Assert.Equal(new Rect(10, 10, 200, 30), Bounds(r, form, "N"));
        Assert.Single(r.Differences());

        // I1: combo boxes (E33a/b); csSimple keeps its stored height.
        form = Form(400, 300, Obj("A", "TComboBox", 10, 10, 145, 40) + Obj("B", "TComboBox", 0, 0, 145, 40, "Align = alLeft")
            + Obj("S", "TComboBox", 10, 60, 145, 90, "Style = csSimple"));
        r = Engine.Compute(form);
        Assert.Equal(new Rect(10, 10, 145, 23), Bounds(r, form, "A"));
        Assert.Equal(new Rect(0, 0, 145, 23), Bounds(r, form, "B"));
        Assert.Equal(new Rect(10, 60, 145, 90), Bounds(r, form, "S"));

        // I2 / I3: auto-size edits by effective font (E37); memos and AutoSize = False keep their height.
        form = Form(400, 300, Obj("E", "TEdit", 10, 10, 121, 40, "Font.Height = -16\nParentFont = False")
            + Obj("N", "TEdit", 10, 60, 121, 40, "BorderStyle = bsNone")
            + Obj("F", "TEdit", 10, 110, 121, 40, "AutoSize = False")
            + Obj("M", "TMemo", 10, 160, 121, 40)
            + Obj("G", "TGroupBox", 200, 0, 185, 105, "Font.Height = -20\nParentFont = False",
                Obj("Inner", "TEdit", 8, 20, 100, 10)));
        r = Engine.Compute(form);
        Assert.Equal(new Rect(10, 10, 121, 27), Bounds(r, form, "E"));
        Assert.Equal(new Rect(10, 60, 121, 15), Bounds(r, form, "N"));
        Assert.Equal(new Rect(10, 110, 121, 40), Bounds(r, form, "F"));
        Assert.Equal(new Rect(10, 160, 121, 40), Bounds(r, form, "M"));
        Assert.Equal(new Rect(8, 20, 100, 31), Bounds(r, form, "Inner")); // inherits the group box font (UIF-251)

        // I4: a tab sheet always has the display rectangle (UIF-1501: top TabH + 4); its stored values are ignored.
        form = Form(400, 300, Obj("Book", "TPageControl", 0, 0, 289, 193, "TabHeight = 30",
            "object Sheet: TTabSheet\nend\n"));
        r = Engine.Compute(form);
        Assert.Equal(new Rect(4, 34, 281, 155), Bounds(r, form, "Sheet"));
        Assert.Empty(r.Differences()); // tab-sheet geometry is not stored (DSG-LAY-040)
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-LAY-034")]
    [Trait("Req", "UIF-215")]
    [Trait("Req", "UIF-218")]
    public void ClientAndAlignRectsPerClass()
    {
        var form = Form(600, 400,
            Obj("Raised", "TPanel", 0, 0, 200, 100, "", Aligned("C1:alClient:0:0:1:1"))
            + Obj("Framed", "TPanel", 0, 100, 200, 100,
                "BorderStyle = bsSingle\nBevelInner = bvLowered\nBorderWidth = 3\nPadding.Left = 1\nPadding.Top = 2\nPadding.Right = 3\nPadding.Bottom = 4",
                Aligned("C2:alClient:0:0:1:1"))
            + Obj("Group", "TGroupBox", 200, 0, 185, 105, "", Aligned("C3:alClient:0:0:1:1"))
            + Obj("Box", "TScrollBox", 200, 110, 200, 100, "",
                Obj("N", "TPanel", 0, 0, 300, 50) + Aligned("B:alBottom:0:0:10:20"))
            + Obj("Book", "TPageControl", 0, 210, 289, 193, "", "object Sheet: TTabSheet\nend\n"));
        var r = Engine.Compute(form);
        FormObjectRects(r, form, "LayoutForm", new Rect(0, 0, 600, 400), new Rect(0, 0, 600, 400));

        // TPanel: client = full bounds; bevels/BorderWidth inset only the align rect (E25).
        FormObjectRects(r, form, "Raised", new Rect(0, 0, 200, 100), new Rect(1, 1, 198, 98));
        Assert.Equal(new Rect(1, 1, 198, 98), Bounds(r, form, "C1"));

        // bsSingle: client origin (1, 1), size W − 2; align rect = inset 3 + 1 + 1 then padding, in client coordinates.
        FormObjectRects(r, form, "Framed", new Rect(1, 1, 198, 98), new Rect(6, 7, 198 - 10 - 4, 98 - 10 - 6));
        Assert.Equal(new Rect(6, 7, 184, 82), Bounds(r, form, "C2"));
        Assert.Equal(new Rect(1 + 6, 100 + 1 + 7, 184, 82), r.GetSurfaceBounds(form.FindByName("C2")!));

        // TGroupBox (E26): align rect (2, 17, W − 2, H − 2).
        FormObjectRects(r, form, "Group", new Rect(0, 0, 185, 105), new Rect(2, 17, 181, 86));

        // TScrollBox (E32): inset 2, minus the visible horizontal bar.
        FormObjectRects(r, form, "Box", new Rect(2, 2, 196, 79), new Rect(0, 0, 196, 79));
        Assert.Equal(new Rect(0, 59, 196, 20), Bounds(r, form, "B"));
        Assert.Equal(new Rect(0, 0, 300, 50), Bounds(r, form, "N"));

        // TPageControl (E27): sheets at the display rectangle (UIF-1501); the sheet's client is its full bounds.
        Assert.Equal(new Rect(4, 25, 281, 164), Bounds(r, form, "Sheet"));
        FormObjectRects(r, form, "Sheet", new Rect(0, 0, 281, 164), new Rect(0, 0, 281, 164));

        // The renderers' rectangles (control coordinates) come from the same formulas (ContainerRects).
        foreach (var name in new[] { "Raised", "Framed", "Group" })
        {
            var obj = form.FindByName(name)!;
            var bounds = r.GetBounds(obj);
            var ctx = new RenderContext(obj, bounds.Width, bounds.Height);
            var client = r.GetClientRect(obj);
            var align = r.GetAlignRect(obj);
            Assert.Equal(ctx.ClientRect, client);
            Assert.Equal(ctx.AlignRect, align with { Left = align.Left + client.Left, Top = align.Top + client.Top });
        }
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-LAY-030")]
    public void IgnoresNonVisualAndUnplaceable()
    {
        const string text = """
            object LayoutForm: TForm
              ClientWidth = 400
              ClientHeight = 300
              object Clock: TTimer
                Interval = 500
              end
              object Top1: TPanel
                Left = 0
                Top = 0
                Width = 10
                Height = 30
                Align = alTop
              end
              object Odd: TMysteryControl
                Left = 7
                Top = 9
                Width = 50
                Height = 60
                Align = alClient
              end
              object Ghost: TMysteryControl
                Caption = 'x'
              end
            end
            """;
        var result = FormParser.Parse(text.Replace("\r\n", "\n"), "odd.form", LayoutHarness.Catalog);
        var form = result.Document!;
        var r = Engine.Compute(form, LayoutMode.RunTime);
        Assert.False(r.HasBounds(form.FindByName("Clock")!));
        Assert.False(r.HasBounds(form.FindByName("Ghost")!));
        Assert.True(r.HasBounds(form.FindByName("Odd")!));
        Assert.Equal(new Rect(0, 0, 400, 30), Bounds(r, form, "Top1"));
        Assert.Equal(new Rect(7, 9, 50, 60), Bounds(r, form, "Odd")); // preserved: alNone, never aligned
        Assert.Throws<ArgumentException>(() => r.GetBounds(form.FindByName("Clock")!));

        var dump = r.ToDump();
        Assert.Equal(["Top1", "Odd"], dump.Controls.Select(c => c.Name));
    }

    /// <summary>A visual child line <c>Name:Align:L:T:W:H</c> as a <c>TPanel</c> object block.</summary>
    private static string Aligned(string spec) => Aligned(spec, "");

    private static string Aligned(string spec, string properties)
    {
        var p = spec.Split(':');
        return Obj(p[0], "TPanel", int.Parse(p[2]), int.Parse(p[3]), int.Parse(p[4]), int.Parse(p[5]),
            $"Align = {p[1]}\n" + properties);
    }

    private static void FormObjectRects(LayoutResult r, FormDocument form, string name, Rect client, Rect align)
    {
        var obj = form.FindByName(name)!;
        Assert.Equal(client, r.GetClientRect(obj));
        Assert.Equal(align, r.GetAlignRect(obj));
    }
}
