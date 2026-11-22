using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Guideline snapping of move drags (design-surface.md §6, DSG-SUR-071 … 078; D-040).</summary>
public sealed class SnapTests
{
    // A is the reference; B (50 × 25) is dragged. B's vertical extent 160..185 is within 64 px of A's (100..125).
    private const string Pair = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object A: TButton
            Left = 100
            Top = 100
            Width = 75
            Height = 25
          end
          object B: TButton
            Left = 20
            Top = 160
            Width = 50
            Height = 25
          end
        end

        """;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-072")]
    [Trait("Req", "DSG-SUR-074")]
    public void EdgeGuidelineWithinDistance()
    {
        using var h = Harness(Pair);

        // Left edge 3 px right of A's left edge: snaps; the guideline is shown while dragging.
        var (position, guidelines) = Preview(h, "B", 83, 0);
        Assert.Equal((100, 160), position);
        Assert.Equal([new Guideline(SnapAxis.X, GuidelineKind.Edge, 100, h.Object("F"), h.Object("A"))], guidelines);

        // Exactly the snap distance (4) still snaps; 5 does not.
        Assert.Equal((100, 160), Preview(h, "B", 84, 0).Position);
        Assert.Equal((105, 160), Preview(h, "B", 85, 0).Position);
        Assert.Empty(Preview(h, "B", 85, 0).Guidelines);

        // Right edge to A's left edge (both edges of an axis are candidates in a move).
        (position, guidelines) = Preview(h, "B", 32, 0); // right 102
        Assert.Equal((50, 160), position);
        Assert.Equal([new Guideline(SnapAxis.X, GuidelineKind.Edge, 100, h.Object("F"), h.Object("A"))], guidelines);

        // The drop applies the snapped position; a smaller snap distance setting is read live.
        h.Settings.GuidelineSnapDistance = 2;
        Assert.Equal((103, 160), Preview(h, "B", 83, 0).Position);
        h.Settings.GuidelineSnapDistance = 4;
        h.Press("B");
        h.MoveBy(83, 0);
        h.Release();
        Assert.Equal((100, 160), h.Position("B"));
        Assert.Empty(h.Guidelines); // only during the drag (UI-DSG-050)

        // Center lines: B's horizontal center to A's center ((100 + 175) div 2) and to the parent's client center
        // (400 div 2; B far below A, so A is not a reference).
        h.ExecuteUndo();
        (position, guidelines) = Preview(h, "B", 90, 0); // center 135
        Assert.Equal((112, 160), position);
        Assert.Equal([new Guideline(SnapAxis.X, GuidelineKind.Center, 137, h.Object("F"), h.Object("A"))], guidelines);
        (position, guidelines) = Preview(h, "B", 157, 100); // center 202
        Assert.Equal((175, 260), position);
        Assert.Equal([new Guideline(SnapAxis.X, GuidelineKind.Center, 200, h.Object("F"), null)], guidelines);

        // useGuidelines off: none.
        h.Settings.UseGuidelines = false;
        Assert.Equal((103, 160), Preview(h, "B", 83, 0).Position);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-072")]
    [Trait("Req", "DSG-SUR-074")]
    public void BaselineBeatsEdge()
    {
        // E's baseline: (23 − 15) div 2 + 10 = 114. L (tlTop) has its baseline at top + 10.
        using var h = Harness("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object E: TEdit
                Left = 100
                Top = 100
                Width = 120
                Height = 23
              end
              object L: TLabel
                Left = 240
                Top = 140
                Width = 40
                Height = 15
                Caption = 'Name'
              end
            end

            """);

        // L top 102: top edge d = −2 (priority 1), center d = +2 (priority 2), baseline d = +2 (priority 0) → baseline.
        var (position, guidelines) = Preview(h, "L", 0, -38);
        Assert.Equal((240, 104), position);
        Assert.Equal([new Guideline(SnapAxis.Y, GuidelineKind.Baseline, 114, h.Object("F"), h.Object("E"))], guidelines);

        // A closer edge still wins over a farther baseline (distance first).
        Assert.Equal((240, 100), Preview(h, "L", 0, -39).Position); // top 101: top d = −1, baseline d = +3

        // Controls without text have no baseline: an image only snaps by its edges.
        using var i = Harness("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object E: TEdit
                Left = 100
                Top = 100
                Width = 120
                Height = 23
              end
              object Img: TImage
                Left = 240
                Top = 140
                Width = 40
                Height = 15
              end
            end

            """);
        (position, guidelines) = Preview(i, "Img", 0, -38);
        Assert.Equal((240, 100), position); // top d = −2 (the center d = +2 has the lower priority)
        Assert.Equal([new Guideline(SnapAxis.Y, GuidelineKind.Edge, 100, i.Object("F"), i.Object("E"))], guidelines);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-072")]
    public void MarginGuidelineToSiblingAndParent()
    {
        using var h = Harness(Pair);

        // Left edge to A's right + 8.
        var (position, guidelines) = Preview(h, "B", 165, 0); // left 185
        Assert.Equal((183, 160), position);
        Assert.Equal([new Guideline(SnapAxis.X, GuidelineKind.Margin, 183, h.Object("F"), h.Object("A"))], guidelines);

        // Parent margins: left 0 + 8, right clientW − 8, bottom clientH − 8.
        (position, guidelines) = Preview(h, "B", -10, 0); // left 10
        Assert.Equal((8, 160), position);
        Assert.Equal([new Guideline(SnapAxis.X, GuidelineKind.Margin, 8, h.Object("F"), null)], guidelines);
        Assert.Equal((342, 160), Preview(h, "B", 325, 0).Position); // right 395 → 392
        (position, guidelines) = Preview(h, "B", 0, 106); // bottom 291 → 292
        Assert.Equal((20, 267), position);
        Assert.Equal([new Guideline(SnapAxis.Y, GuidelineKind.Margin, 292, h.Object("F"), null)], guidelines);

        // The margin setting is read live; 0 makes the margin lines coincide with the parent edges.
        h.Settings.GuidelineMargin = 0;
        Assert.Equal((0, 160), Preview(h, "B", -17, 0).Position); // left 3 → 0
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-076")]
    public void GuidelineBeatsGridOtherwiseGrid()
    {
        using var h = Harness(Pair);
        h.Settings.SnapToGrid = true;

        // X: the edge guideline (100) wins over the grid (104); Y has no guideline: grid (161 → 160).
        var (position, guidelines) = Preview(h, "B", 83, 1);
        Assert.Equal((100, 160), position);
        Assert.Equal([new Guideline(SnapAxis.X, GuidelineKind.Edge, 100, h.Object("F"), h.Object("A"))], guidelines);

        // No guideline within reach: the grid on both axes.
        (position, guidelines) = Preview(h, "B", 87, 3); // (107, 163)
        Assert.Equal((104, 160), position);
        Assert.Empty(guidelines);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-073")]
    public void FarSiblingsAreIgnored()
    {
        using var h = Harness(Pair);

        // A's vertical extent 100..125 expanded by 64 is 36..189: B at top 188 overlaps it, at top 189 it does not.
        Assert.Equal((100, 188), Preview(h, "B", 82, 28).Position);
        Assert.Equal((102, 189), Preview(h, "B", 82, 29).Position);
        Assert.Empty(Preview(h, "B", 82, 29).Guidelines);

        // The parent's client rectangle is always considered (left margin 8, far below A).
        Assert.Equal((8, 260), Preview(h, "B", -10, 100).Position);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-074")]
    public void TieBreaking()
    {
        // B (left 103) is 3 px from A1's left edge (100) and from A2's (106): the one nearer on the other axis wins.
        using var near = Harness(Triple(secondTop: 200)); // A1 is 35 px above B, A2 15 px below
        var (position, guidelines) = Preview(near, "B", 83, 0);
        Assert.Equal((106, 160), position);
        Assert.Equal([new Guideline(SnapAxis.X, GuidelineKind.Edge, 106, near.Object("F"), near.Object("A2"))], guidelines);

        // Same distance on the other axis (35 px each): the earlier sibling in creation order wins.
        using var same = Harness(Triple(secondTop: 220));
        (position, guidelines) = Preview(same, "B", 83, 0);
        Assert.Equal((100, 160), position);
        Assert.Equal([new Guideline(SnapAxis.X, GuidelineKind.Edge, 100, same.Object("F"), same.Object("A1"))], guidelines);

        // Same distance, different priority: B's left 179 is 4 px from A's right edge (175, priority 1) and from A's
        // right + 8 (183, margin, priority 3); B's center 204 is 4 px from the parent center (200, priority 2).
        using var pair = Harness(Pair);
        (position, guidelines) = Preview(pair, "B", 159, 0);
        Assert.Equal((175, 160), position);
        Assert.Equal([new Guideline(SnapAxis.X, GuidelineKind.Edge, 175, pair.Object("F"), pair.Object("A"))], guidelines);
    }

    private static string Triple(int secondTop) => $"""
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object A1: TButton
            Left = 100
            Top = 100
            Width = 75
            Height = 25
          end
          object A2: TButton
            Left = 106
            Top = {secondTop}
            Width = 75
            Height = 25
          end
          object B: TButton
            Left = 20
            Top = 160
            Width = 50
            Height = 25
          end
        end

        """;

    private static DesignerHarness Harness(string text)
    {
        var h = DesignerHarness.FromText(text);
        h.Settings.SnapToGrid = false; // guidelines only, unless a test turns the grid on
        return h;
    }

    /// <summary>Drags <paramref name="name"/> by a raw delta, reads the live position and guidelines, then cancels (Escape).</summary>
    private static ((int X, int Y) Position, IReadOnlyList<Guideline> Guidelines) Preview(DesignerHarness h, string name, int dx, int dy)
    {
        h.Press(name);
        var start = h.SurfacePoint(name);
        h.MoveTo((start.X + dx, start.Y + dy));
        Assert.Equal(InteractionState.Moving, h.Controller.State);
        var result = (h.Position(name), h.Guidelines.ToList());
        Assert.True(h.Key(DesignKey.Escape));
        return result;
    }
}
