using FreexComp.Designer.Layout;
using static FreexComp.Designer.Tests.Layout.LayoutHarness;

namespace FreexComp.Designer.Tests.Layout;

/// <summary>Run-time and design mode (layout-engine.md DSG-LAY-005, DSG-LAY-032; D-092).</summary>
public sealed partial class LayoutEngineTests
{
    /// <summary>
    /// Invisible aligned controls (top, left, nested client), an invisible tool button in a flowed bar and an invisible
    /// sheet child: design mode equals run-time mode of the same form with every control visible, except for the
    /// <c>visible</c> values, which stay the controls' own; run-time mode leaves the invisible controls out.
    /// </summary>
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-LAY-005")]
    [Trait("Req", "DSG-LAY-032")]
    [Trait("Req", "UIF-221")]
    public void RunTimeAndDesignModesDifferOnlyInVisibility()
    {
        const string Hidden = "Visible = False";
        var body = Obj("Top1", "TPanel", 0, 0, 100, 30, "Align = alTop\n" + Hidden)
            + Obj("Top2", "TPanel", 0, 40, 100, 20, "Align = alTop")
            + Obj("Left1", "TPanel", 0, 60, 50, 10, "Align = alLeft\n" + Hidden)
            + Obj("Host", "TPanel", 5, 5, 10, 10, "Align = alClient",
                Obj("Inner", "TPanel", 0, 0, 10, 10, "Align = alRight\n" + Hidden)
                + Obj("Fill", "TPanel", 0, 0, 10, 10, "Align = alClient"))
            + "object Bar: TToolBar\n  Left = 0\n  Top = 0\n  Width = 100\n  Height = 29\n"
            + "  object B1: TToolButton\n  end\n  object B2: TToolButton\n    " + Hidden + "\n  end\n  object B3: TToolButton\n  end\nend\n";
        var form = Form(400, 300, body);
        var visibleForm = Form(400, 300, body.Replace(Hidden, "Visible = True", StringComparison.Ordinal));

        var design = Engine.Compute(form, LayoutMode.Design).ToDump();
        var runTime = Engine.Compute(form, LayoutMode.RunTime).ToDump();
        var allVisible = Engine.Compute(visibleForm, LayoutMode.RunTime).ToDump();

        // Design mode = run-time layout of the all-visible form; only the visible flags differ.
        Assert.Equal(Flatten(allVisible.Controls).Select(c => c with { Visible = true }),
            Flatten(design.Controls).Select(c => c with { Visible = true }));
        Assert.Equal(10, Flatten(design.Controls).Count());
        Assert.Equal(["Top1", "Left1", "Inner", "B2"], Flatten(design.Controls).Where(c => !c.Visible).Select(c => c.Name));
        Assert.Equal(Flatten(runTime.Controls).Select(c => (c.Name, c.Visible)), Flatten(design.Controls).Select(c => (c.Name, c.Visible)));

        // Run time: the invisible controls do not take part (UIF-221) and keep their bounds; their siblings move up.
        static Node Named(LayoutDumpForm dump, string name) => Flatten(dump.Controls).Single(c => c.Name == name);
        Assert.Equal((0, 0, 100, 30), Box(Named(runTime, "Top1")));
        Assert.Equal((0, 0, 400, 30), Box(Named(design, "Top1")));
        Assert.Equal((0, 29, 400, 20), Box(Named(runTime, "Top2")));
        Assert.Equal((0, 59, 400, 20), Box(Named(design, "Top2")));
        Assert.Equal((23, 2, 23, 22), Box(Named(runTime, "B3")));
        Assert.Equal((46, 2, 23, 22), Box(Named(design, "B3")));

        // Without invisible controls both modes give the same dump.
        Assert.Equal(Flatten(allVisible.Controls), Flatten(Engine.Compute(visibleForm, LayoutMode.Design).ToDump().Controls));
    }

    private static (int, int, int, int) Box(Node c) => (c.Left, c.Top, c.Width, c.Height);

    /// <summary>One dump element without its children (value equality).</summary>
    private sealed record Node(string Class, string Name, int Left, int Top, int Width, int Height, bool Visible);

    /// <summary>The controls of a dump in pre-order.</summary>
    private static IEnumerable<Node> Flatten(IReadOnlyList<LayoutDumpControl> controls) =>
        controls.SelectMany(c => (IEnumerable<Node>)[new Node(c.Class, c.Name, c.Left, c.Top, c.Width, c.Height, c.Visible), .. Flatten(c.Controls)]);
}
