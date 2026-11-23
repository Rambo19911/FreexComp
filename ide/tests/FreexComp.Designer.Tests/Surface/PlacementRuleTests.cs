using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Placement into aligned parents (tool-palette.md DSG-TP-056) and the harness's final checks (testing.md DSG-TEST-020); D-087.</summary>
[Trait("Phase", "5")]
public sealed class PlacementRuleTests
{
    private const string AlignedParent = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object Host: TPanel
            Left = 0
            Top = 0
            Width = 400
            Height = 300
            Align = alClient
            TabOrder = 0
          end
        end

        """;

    [Fact]
    [Trait("Req", "DSG-TP-056")]
    public void ContainerPlacedIntoAnAlignedParentGetsTheDefaultAnchors()
    {
        using var h = DesignerHarness.FromText(AlignedParent);
        var client = h.Controller.Geometry.GetClientRect(h.Object("Host"));

        // A panel (container) and an edit (wincontrol) placed into the alClient panel: nothing special, default Anchors.
        h.Host.Arm("TPanel");
        h.ClickAt((client.Left + 40, client.Top + 40));
        h.Host.Arm("TEdit");
        h.ClickAt((client.Left + 40, client.Top + 200));
        foreach (var name in new[] { "Panel1", "Edit1" })
        {
            var placed = h.Object(name);
            Assert.Equal("Host", placed.Parent!.Name);
            Assert.Null(placed.FindProperty("Anchors")); // the metadata default [akLeft, akTop] is not stored
            Assert.Null(placed.FindProperty("Align"));
        }

        Assert.DoesNotContain("Anchors", h.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "DSG-TEST-020")]
    public void HarnessDisposeChecksTheInvariantAndTheReparse()
    {
        // A consistent form passes the final checks.
        var ok = DesignerHarness.FromText(AlignedParent);
        ok.Dispose();

        // Stored bounds that disagree with Align: Dispose fails with the invariant.
        var broken = DesignerHarness.FromText(AlignedParent);
        broken.Editor.Apply(new SetProperty("Host", "Width", new IntegerValue(123, false)));
        var error = Assert.ThrowsAny<Exception>(broken.Dispose);
        Assert.Contains("Host: stored", error.Message, StringComparison.Ordinal);

        // Opting out skips both checks.
        broken.SkipFinalChecks = true;
        broken.Dispose();
    }
}
