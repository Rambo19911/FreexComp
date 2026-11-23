using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>The align, size, spacing and grid commands of the design surface (design-surface.md §11.1; D-051).</summary>
public sealed class AlignCommandTests
{
    // A is the reference in most cases; B is narrower (odd width) with a maximum width; D sits in another parent (P);
    // Top1 is docked and never moves.
    private const string Form = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object Top1: TPanel
            Left = 0
            Top = 0
            Width = 400
            Height = 40
            Align = alTop
          end
          object A: TButton
            Left = 10
            Top = 50
            Width = 100
            Height = 30
          end
          object B: TButton
            Left = 50
            Top = 100
            Width = 61
            Height = 20
            Constraints.MaxWidth = 80
          end
          object P: TPanel
            Left = 150
            Top = 100
            Width = 200
            Height = 150
            object D: TButton
              Left = 10
              Top = 10
              Width = 50
              Height = 25
            end
          end
          object T1: TTimer
            DesignLeft = 8
            DesignTop = 8
          end
        end

        """;

    private static readonly string[] AlignAndSize =
    [
        "designer.alignLeft", "designer.alignRight", "designer.alignTop", "designer.alignBottom", "designer.alignHCenter",
        "designer.alignVCenter", "designer.sameWidth", "designer.sameHeight", "designer.sameSize",
    ];

    private static readonly string[] All =
    [
        .. AlignAndSize, "designer.centerHInParent", "designer.centerVInParent", "designer.spaceHEqually",
        "designer.spaceVEqually", "designer.alignToGrid", "designer.sizeToGrid",
    ];

    /// <summary>Bounds in form client coordinates (so controls of different parents compare visually).</summary>
    private static Rect Visual(DesignerHarness h, string name)
    {
        var b = h.Controller.Geometry.GetBounds(h.Object(name));
        var client = h.Controller.Geometry.FormClientRect;
        return b with { Left = b.Left - client.Left, Top = b.Top - client.Top };
    }

    [Theory]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-140")]
    [InlineData("designer.alignLeft", 10, 100, 61, 20, 10, -1, 50, 25)]
    [InlineData("designer.alignRight", 49, 100, 61, 20, 60, -1, 50, 25)]
    [InlineData("designer.alignTop", 50, 50, 61, 20, -1, 50, 50, 25)]
    [InlineData("designer.alignBottom", 50, 60, 61, 20, -1, 55, 50, 25)]
    [InlineData("designer.alignHCenter", 29, 100, 61, 20, 35, -1, 50, 25)] // 10 + 39 div 2; 10 + 50 div 2
    [InlineData("designer.alignVCenter", 50, 55, 61, 20, -1, 52, 50, 25)] // 50 + 10 div 2; 50 + 5 div 2
    [InlineData("designer.sameWidth", 50, 100, 80, 20, -1, -1, 100, 25)] // B is clamped by Constraints.MaxWidth
    [InlineData("designer.sameHeight", 50, 100, 61, 30, -1, -1, 50, 30)]
    [InlineData("designer.sameSize", 50, 100, 80, 30, -1, -1, 100, 30)]
    public void EachCommandMatchesFormula(string command, int bLeft, int bTop, int bWidth, int bHeight,
        int dLeft, int dTop, int dWidth, int dHeight)
    {
        using var h = DesignerHarness.FromText(Form);
        var original = h.Text;
        var d = Visual(h, "D");
        var a = Visual(h, "A");
        var top1 = Visual(h, "Top1");
        h.Select("Top1", "B", "D", "A"); // reference A; Top1 is docked and not an "other"
        Assert.True(h.Controller.CanExecute(command));
        h.Execute(command);

        Assert.Equal(new Rect(bLeft, bTop, bWidth, bHeight), Visual(h, "B"));
        Assert.Equal(new Rect(dLeft < 0 ? d.Left : dLeft, dTop < 0 ? d.Top : dTop, dWidth, dHeight), Visual(h, "D"));
        Assert.Equal(a, Visual(h, "A"));
        Assert.Equal(top1, Visual(h, "Top1"));
        Assert.Equal("P", h.Parent("D")); // stored back in its own parent's coordinates
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Equal(command.StartsWith("designer.same", StringComparison.Ordinal) ? UndoTitles.Size : UndoTitles.Align, h.Undo.UndoTitleKey);
        Assert.Equal(["Top1", "B", "D", "A"], h.Selected);

        // A command that changes nothing records nothing.
        h.Execute(command);
        Assert.Equal(1, h.Undo.UndoCount);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-140")]
    [Trait("Req", "DSG-SUR-142")]
    public void CenterAndGridCommandsMatchFormula()
    {
        using var h = DesignerHarness.FromText(Form);
        var original = h.Text;
        var d = Visual(h, "D");

        // Only the movable controls sharing the primary's parent: B = (10, 50) … (111, 120) in the 400 × 300 client area.
        h.Select("B", "D", "A");
        h.Execute("designer.centerHInParent");
        Assert.Equal((149, 50), h.Position("A")); // dx = (400 − 101) div 2 − 10 = 139
        Assert.Equal((189, 100), h.Position("B"));
        Assert.Equal(d, Visual(h, "D"));
        h.ExecuteUndo();
        h.Execute("designer.centerVInParent");
        Assert.Equal((10, 115), h.Position("A")); // dy = (300 − 70) div 2 − 50 = 65
        Assert.Equal((50, 165), h.Position("B"));
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // The grid commands use the grid even when snapping is off; positions in each parent's client coordinates.
        h.Settings.SnapToGrid = false;
        h.Execute("designer.alignToGrid");
        Assert.Equal((8, 48), h.Position("A"));
        Assert.Equal((48, 104), h.Position("B"));
        Assert.Equal((8, 8), h.Position("D"));
        Assert.Equal(UndoTitles.Align, h.Undo.UndoTitleKey);
        h.ExecuteUndo();
        h.Execute("designer.sizeToGrid");
        Assert.Equal((104, 32), h.Size("A"));
        Assert.Equal((64, 24), h.Size("B"));
        Assert.Equal((48, 24), h.Size("D"));
        Assert.Equal(UndoTitles.Size, h.Undo.UndoTitleKey);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // At least one grid step; a resized container re-flows its children (stored in the same unit).
        using var small = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object Q: TPanel
                Left = 0
                Top = 0
                Width = 101
                Height = 3
                object Fill: TPanel
                  Left = 0
                  Top = 0
                  Width = 101
                  Height = 3
                  Align = alClient
                end
              end
            end

            """);
        small.Select("Q");
        small.Execute("designer.sizeToGrid");
        Assert.Equal((104, 8), small.Size("Q"));
        Assert.Equal(PropertyAccess.GetInt(small.Object("Fill"), "Width"), small.Size("Fill").Width);
        Assert.Equal(1, small.Undo.UndoCount);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-140")]
    public void SpaceEquallyDistributesGaps()
    {
        using var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object First: TButton
                Left = 0
                Top = 0
                Width = 10
                Height = 10
              end
              object InnerA: TButton
                Left = 40
                Top = 50
                Width = 20
                Height = 10
              end
              object InnerB: TButton
                Left = 25
                Top = 50
                Width = 5
                Height = 10
              end
              object Last: TButton
                Left = 100
                Top = 90
                Width = 10
                Height = 10
              end
            end

            """);
        var original = h.Text;
        h.Select("Last", "InnerA", "First", "InnerB");

        // Sorted by left: First, InnerB, InnerA, Last; G = 100 − 10 − 25 = 65; lefts 10 + round(65/3) and 10 + round(130/3) + 5.
        h.Execute("designer.spaceHEqually");
        Assert.Equal((0, 0), h.Position("First"));
        Assert.Equal((32, 50), h.Position("InnerB"));
        Assert.Equal((58, 50), h.Position("InnerA"));
        Assert.Equal((100, 90), h.Position("Last"));
        Assert.Equal(1, h.Undo.UndoCount);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // Vertically: InnerA and InnerB tie at 50 (creation order); G = 90 − 10 − 20 = 60; tops 10 + 20, 10 + 40 + 10.
        h.Execute("designer.spaceVEqually");
        Assert.Equal((40, 30), h.Position("InnerA"));
        Assert.Equal((25, 60), h.Position("InnerB"));
        Assert.Equal((100, 90), h.Position("Last"));
        h.ExecuteUndo();

        // One inner control: G = 100 − 10 − 20 = 70 → 10 + 35. Halves round up.
        h.Select("First", "InnerA", "Last");
        h.Execute("designer.spaceHEqually");
        Assert.Equal((45, 50), h.Position("InnerA"));
        h.ExecuteUndo();
        h.Select("First", "InnerB", "InnerA");
        h.Execute("designer.spaceHEqually"); // First 0…10, InnerB, InnerA 40…60: G = 40 − 10 − 5 = 25 → 10 + round(12.5)
        Assert.Equal((23, 50), h.Position("InnerB"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-141")]
    public void EnabledWhenRules()
    {
        using var h = DesignerHarness.FromText(Form);
        bool Can(string command) => h.Controller.CanExecute(command);

        // The form alone: nothing.
        Assert.All(All, c => Assert.False(Can(c), c));

        // One movable control: the parent and grid commands only.
        h.Select("A");
        Assert.All(AlignAndSize, c => Assert.False(Can(c), c));
        Assert.True(Can("designer.centerHInParent"));
        Assert.True(Can("designer.centerVInParent"));
        Assert.True(Can("designer.alignToGrid"));
        Assert.True(Can("designer.sizeToGrid"));
        Assert.False(Can("designer.spaceHEqually"));

        // A docked reference aligns others; a docked "other" does not count.
        h.Select("A", "Top1");
        Assert.All(AlignAndSize, c => Assert.True(Can(c), c));
        h.Select("Top1", "A");
        Assert.All(AlignAndSize, c => Assert.False(Can(c), c));
        h.Select("Top1");
        Assert.False(Can("designer.centerHInParent"));
        Assert.False(Can("designer.alignToGrid"));
        Assert.False(Can("designer.sizeToGrid"));

        // Tray items are not controls.
        h.Select("A", "T1");
        Assert.All(AlignAndSize, c => Assert.False(Can(c), c));
        h.Select("A", "B", "T1");
        Assert.All(AlignAndSize, c => Assert.False(Can(c), c)); // the reference is not a control
        Assert.True(Can("designer.centerHInParent"));

        // Spacing needs three movable controls.
        h.Select("A", "B");
        Assert.False(Can("designer.spaceHEqually"));
        Assert.All(AlignAndSize, c => Assert.True(Can(c), c));
        h.Select("A", "B", "D");
        Assert.True(Can("designer.spaceHEqually"));
        Assert.True(Can("designer.spaceVEqually"));

        // Lock Controls disables every command of §11.1.
        h.Settings.LockControls = true;
        Assert.All(All, c => Assert.False(Can(c), c));
        h.Execute("designer.alignLeft");
        Assert.Equal(0, h.Undo.UndoCount);
    }
}
