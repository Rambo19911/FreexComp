using FreexComp.Designer.Catalog;
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

    // ---- Phase 8: spacing from Margins and Padding (alignment-guidelines.md DSG-SUR-200 … 206; D-114) ----

    /// <summary>
    /// The pair of <see cref="Pair"/> — A (75 × 25 at 100, 100; right edge 175), B (50 × 25 at 20, 160) — with more stored
    /// property lines (separated by ';') on A, on B and on the form, and a third button C (50 × 25 at 20, 186) when
    /// <paramref name="c"/> is given.
    /// </summary>
    private static string Spaced(string a = "", string b = "", string? c = null, string form = "")
    {
        static string Lines(string properties, string indent) =>
            string.Concat(properties.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => indent + p.Trim() + "\n"));
        static string Button(string name, int left, int top, int width, string properties) =>
            $"  object {name}: TButton\n    Left = {left}\n    Top = {top}\n    Width = {width}\n    Height = 25\n{Lines(properties, "    ")}  end\n";
        return "object F: TForm\n  ClientWidth = 400\n  ClientHeight = 300\n" + Lines(form, "  ")
            + Button("A", 100, 100, 75, a) + Button("B", 20, 160, 50, b) + (c is null ? "" : Button("C", 20, 186, 50, c)) + "end\n";
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-200")]
    public void SiblingSpacing_UsesDeclaredMargins()
    {
        // A stores Margins.Right = 8; B's left margin is the default 3: the spacing is 11, the line at A's right + 11.
        using var h = Harness(Spaced(a: "Margins.Right = 8"));
        var (position, guidelines) = Preview(h, "B", 164, 0); // left 184
        Assert.Equal((186, 160), position);
        var line = Assert.Single(guidelines);
        Assert.Equal(new Guideline(SnapAxis.X, GuidelineKind.Margin, 186, h.Object("F"), h.Object("A")), line);
        Assert.Equal(11, line.Spacing);

        // The setting's distance is no candidate for this pair of sides any more: left 181 is 5 from the line and 6 from A's edge.
        (position, guidelines) = Preview(h, "B", 161, 0);
        Assert.Equal((181, 160), position);
        Assert.Empty(guidelines);

        // The other pair of sides (B's right, A's left) has nothing declared: the setting, A's left − 8.
        (position, guidelines) = Preview(h, "B", 24, 0); // right 94
        Assert.Equal((42, 160), position);
        line = Assert.Single(guidelines);
        Assert.Equal((GuidelineKind.Margin, 92, 8), (line.Kind, line.Coordinate, line.Spacing));

        // Declared on the moved control too: 8 + 5. Declared on the moved control only: 3 + 5.
        using var both = Harness(Spaced(a: "Margins.Right = 8", b: "Margins.Left = 5"));
        (position, guidelines) = Preview(both, "B", 166, 0); // left 186
        Assert.Equal((188, 160), position);
        Assert.Equal((GuidelineKind.Margin, 188, 13), (guidelines[0].Kind, guidelines[0].Coordinate, guidelines[0].Spacing));
        using var moved = Harness(Spaced(b: "Margins.Left = 5"));
        (position, guidelines) = Preview(moved, "B", 164, 0); // left 184
        Assert.Equal((183, 160), position);
        Assert.Equal((GuidelineKind.Margin, 183, 8), (guidelines[0].Kind, guidelines[0].Coordinate, guidelines[0].Spacing));

        // The Y axis likewise: B's top faces A's bottom (125), 12 + 3.
        using var below = Harness(Spaced(a: "Margins.Bottom = 12"));
        (position, guidelines) = Preview(below, "B", 0, -18); // top 142
        Assert.Equal((20, 140), position);
        line = Assert.Single(guidelines);
        Assert.Equal(new Guideline(SnapAxis.Y, GuidelineKind.Margin, 140, below.Object("F"), below.Object("A")), line);
        Assert.Equal(15, line.Spacing);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-200")]
    [Trait("Req", "DSG-SUR-202")]
    public void SiblingSpacing_UndeclaredUsesSetting()
    {
        // Nothing stored: the Phase 5 result (MarginGuidelineToSiblingAndParent), the setting as the spacing.
        using var h = Harness(Pair);
        var (position, guidelines) = Preview(h, "B", 165, 0); // left 185
        Assert.Equal((183, 160), position);
        var line = Assert.Single(guidelines);
        Assert.Equal(new Guideline(SnapAxis.X, GuidelineKind.Margin, 183, h.Object("F"), h.Object("A")), line);
        Assert.Equal(8, line.Spacing);
        (position, guidelines) = Preview(h, "B", -10, 0); // left 10: the parent
        Assert.Equal((8, 160), position);
        Assert.Equal(8, Assert.Single(guidelines).Spacing);

        // The setting is read live.
        h.Settings.GuidelineMargin = 6;
        (position, guidelines) = Preview(h, "B", 164, 0); // left 184
        Assert.Equal((181, 160), position);
        Assert.Equal(6, Assert.Single(guidelines).Spacing);

        // A stored value equal to the metadata default is not declared.
        using var same = Harness(Spaced(a: "Margins.Right = 3", b: "Margins.Left = 3"));
        (position, guidelines) = Preview(same, "B", 165, 0);
        Assert.Equal((183, 160), position);
        Assert.Equal(8, Assert.Single(guidelines).Spacing);

        // A guideline of another kind has no spacing.
        Assert.Equal((GuidelineKind.Edge, 0), (Preview(h, "B", 83, 0).Guidelines[0].Kind, Preview(h, "B", 83, 0).Guidelines[0].Spacing));
    }

    // E (100 × 23 at 40, 40) in the panel P (300 × 200, no bevel: its client rectangle is its bounds).
    private static string InPanel(string panel = "", string edit = "") =>
        "object F: TForm\n  ClientWidth = 400\n  ClientHeight = 300\n"
        + "  object P: TPanel\n    Left = 10\n    Top = 10\n    Width = 300\n    Height = 200\n    BevelOuter = bvNone\n"
        + string.Concat(panel.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => "    " + p.Trim() + "\n"))
        + "    object E: TEdit\n      Left = 40\n      Top = 40\n      Width = 100\n      Height = 23\n"
        + string.Concat(edit.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => "      " + p.Trim() + "\n"))
        + "    end\n  end\nend\n";

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-201")]
    public void ParentSpacing_UsesPaddingAndDeclaredMargin()
    {
        // Padding.Left = 12: the left inset is 12; the top has neither padding nor a declared margin: the setting.
        using var h = Harness(InPanel(panel: "Padding.Left = 12"));
        Assert.Equal(new Rect(0, 0, 300, 200), h.Controller.Geometry.GetClientRect(h.Object("P")) with { Left = 0, Top = 0 });
        var (position, guidelines) = Preview(h, "E", -26, 0); // left 14
        Assert.Equal((12, 40), position);
        var line = Assert.Single(guidelines);
        Assert.Equal(new Guideline(SnapAxis.X, GuidelineKind.Margin, 12, h.Object("P"), null), line);
        Assert.Equal(12, line.Spacing);
        (position, guidelines) = Preview(h, "E", 0, -30); // top 10
        Assert.Equal((40, 8), position);
        Assert.Equal((GuidelineKind.Margin, 8, 8), (guidelines[0].Kind, guidelines[0].Coordinate, guidelines[0].Spacing));

        // With Margins.Left = 4 stored on the edit: 12 + 4.
        using var both = Harness(InPanel(panel: "Padding.Left = 12", edit: "Margins.Left = 4"));
        (position, guidelines) = Preview(both, "E", -26, 0); // left 14
        Assert.Equal((16, 40), position);
        Assert.Equal((GuidelineKind.Margin, 16, 16), (guidelines[0].Kind, guidelines[0].Coordinate, guidelines[0].Spacing));

        // A declared margin alone: 0 + 6 from the right client edge (300 − 6); the padding alone: 300 − 20, the
        // undeclared margin is not added.
        using var margin = Harness(InPanel(edit: "Margins.Right = 6"));
        (position, guidelines) = Preview(margin, "E", 152, 0); // right 292
        Assert.Equal((194, 40), position);
        Assert.Equal((GuidelineKind.Margin, 294, 6), (guidelines[0].Kind, guidelines[0].Coordinate, guidelines[0].Spacing));
        using var padding = Harness(InPanel(panel: "Padding.Right = 20; Padding.Bottom = 9"));
        (position, guidelines) = Preview(padding, "E", 138, 0); // right 278
        Assert.Equal((180, 40), position);
        Assert.Equal((GuidelineKind.Margin, 280, 20), (guidelines[0].Kind, guidelines[0].Coordinate, guidelines[0].Spacing));
        (position, guidelines) = Preview(padding, "E", 0, 130); // bottom 193
        Assert.Equal((40, 168), position);
        Assert.Equal((SnapAxis.Y, GuidelineKind.Margin, 191, 9), (guidelines[0].Axis, guidelines[0].Kind, guidelines[0].Coordinate, guidelines[0].Spacing));

        // Neither: the setting (8), as in Phase 5.
        using var plain = Harness(InPanel());
        (position, guidelines) = Preview(plain, "E", -30, 0); // left 10
        Assert.Equal((8, 40), position);
        Assert.Equal((GuidelineKind.Margin, 8, 8), (guidelines[0].Kind, guidelines[0].Coordinate, guidelines[0].Spacing));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-203")]
    [Trait("Req", "UI-DSG-132")]
    public void ZeroSpacingSnapsToTouchingEdges()
    {
        // Both facing margins are stored as 0: the spacing is 0 and B snaps to A's right edge (the edge guideline of the
        // same coordinate has the higher priority, DSG-SUR-074).
        using var h = Harness(Spaced(a: "Margins.Right = 0", b: "Margins.Left = 0; Margins.Top = 0"));
        var (position, guidelines) = Preview(h, "B", 157, 0); // left 177
        Assert.Equal((175, 160), position);
        Assert.Equal(175, Assert.Single(guidelines).Coordinate);

        // The parent's client edge: padding 0 + declared margin 0 — a margin guideline of spacing 0 at the edge itself.
        h.Press("B");
        h.MoveBy(-17, 0); // left 3
        Assert.Equal((0, 160), h.Position("B"));
        var line = Assert.Single(h.Guidelines);
        Assert.Equal(new Guideline(SnapAxis.X, GuidelineKind.Margin, 0, h.Object("F"), null), line);
        Assert.Equal(0, line.Spacing);

        // UI-DSG-132: no line, only the two end ticks (5 DIP) at the shared edge, centred on B's height.
        var g = h.Controller.Geometry;
        var edge = g.GetClientRect(h.Object("F")).Left;
        var b = g.GetBounds(h.Object("B"));
        Assert.Equal(edge, b.Left);
        var mid = (b.Top + b.Bottom - 1) / 2;
        var canvas = new FreexComp.Designer.Rendering.RecordingCanvas();
        h.Controller.Render(canvas, new Rect(0, 0, 2000, 2000));
        var c = h.Controller.Palette.GuidelineMargin.ToString();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal(
            [
                string.Create(inv, $"DrawLine {edge + 0.5},{mid - 2},{edge + 0.5},{mid + 3} {c} 1 Solid"),
                string.Create(inv, $"DrawLine {edge - 0.5},{mid - 2},{edge - 0.5},{mid + 3} {c} 1 Solid"),
            ],
            canvas.Ops.Where(op => op.Kind == "DrawLine" && op.Text.Contains(c, StringComparison.Ordinal)).Select(op => op.Text));
        Assert.True(h.Key(DesignKey.Escape));

        // The top edge likewise.
        (position, guidelines) = Preview(h, "B", 0, -157); // top 3
        Assert.Equal((20, 0), position);
        Assert.Equal((SnapAxis.Y, GuidelineKind.Margin, 0, 0), (guidelines[0].Axis, guidelines[0].Kind, guidelines[0].Coordinate, guidelines[0].Spacing));

        // The setting 0 with nothing declared (Phase 5: MarginGuidelineToSiblingAndParent) is a spacing of 0 too.
        using var plain = Harness(Pair);
        plain.Settings.GuidelineMargin = 0;
        (position, guidelines) = Preview(plain, "B", -17, 0);
        Assert.Equal((0, 160), position);
        Assert.Equal((GuidelineKind.Margin, 0), (guidelines[0].Kind, guidelines[0].Spacing));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-205")]
    public void MultiSelectionUsesAnchorMargins()
    {
        // B and C move together; the margins of the pressed control (the anchor) give the spacing for the whole set.
        using var h = Harness(Spaced(b: "Margins.Left = 6", c: "Margins.Left = 20"));
        h.Select("B", "C");

        h.Press("B"); // anchor B: 6 + 3 → A's right + 9
        h.MoveBy(165, 0); // B's left 185
        Assert.Equal(((184, 160), (184, 186)), (h.Position("B"), h.Position("C")));
        var line = Assert.Single(h.Guidelines);
        Assert.Equal(new Guideline(SnapAxis.X, GuidelineKind.Margin, 184, h.Object("F"), h.Object("A")), line);
        Assert.Equal(9, line.Spacing);
        Assert.True(h.Key(DesignKey.Escape));

        h.Select("B", "C");
        h.Press("C"); // anchor C: 20 + 3 → A's right + 23
        h.MoveBy(177, 0); // C's left 197
        Assert.Equal(((198, 160), (198, 186)), (h.Position("B"), h.Position("C")));
        line = Assert.Single(h.Guidelines);
        Assert.Equal((GuidelineKind.Margin, 198, 23), (line.Kind, line.Coordinate, line.Spacing));
        h.Release();
        Assert.Equal(((198, 160), (198, 186)), (h.Position("B"), h.Position("C")));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-206")]
    public void AlignWithMarginsIsIgnored()
    {
        // AlignWithMargins without a declared margin changes nothing: the setting.
        using var on = Harness(Spaced(a: "AlignWithMargins = True", b: "AlignWithMargins = True"));
        var (position, guidelines) = Preview(on, "B", 165, 0);
        Assert.Equal((183, 160), position);
        Assert.Equal(8, Assert.Single(guidelines).Spacing);

        // Declared margins give the spacing whether AlignWithMargins is on or not.
        foreach (var flag in (string[])["", "AlignWithMargins = True; ", "AlignWithMargins = False; "])
        {
            using var h = Harness(Spaced(a: flag + "Margins.Right = 8"));
            (position, guidelines) = Preview(h, "B", 164, 0);
            Assert.Equal((186, 160), position);
            Assert.Equal(11, Assert.Single(guidelines).Spacing);
        }
    }

    // ---- Phase 8: the baseline rule of the component metadata (alignment-guidelines.md DSG-SUR-208, 209; D-115) ----

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-208")]
    [Trait("Req", "DSG-SUR-209")]
    public void MetadataBaseline_TopAndCenterFormulas()
    {
        // The formulas, with the numbers of the specification: Ascent 11, LineHeight 15, control height 25.
        var metrics = new FreexComp.Designer.Rendering.TextMetrics(11, 4, 15);
        Assert.Equal(3 + 11, new BaselineRule(BaselineAlign.Top, 3).Offset(metrics, 25));
        Assert.Equal(11, new BaselineRule(BaselineAlign.Top).Offset(metrics, 25));
        Assert.Equal(16, new BaselineRule(BaselineAlign.Center).Offset(metrics, 25)); // (25 − 15) div 2 + 11
        Assert.Equal(15, new BaselineRule(BaselineAlign.Center).Offset(metrics, 24)); // div
        Assert.Null(new BaselineRule(BaselineAlign.None).Offset(metrics, 25));

        // Classes of a second metadata root on top of the test metadata: generic-renderer controls with a rule, one
        // without, and an edit whose renderer baseline is suppressed.
        var extra = new FreexComp.Designer.Tests.Catalog.CatalogLoaderTests.MemoryFiles
        {
            [@"C:\pack\index.json"] = """{ "formatVersion": 1, "files": ["Pack.components.json"] }""",
            [@"C:\pack\Pack.components.json"] = """
                { "formatVersion": 1, "unit": "Pack", "components": [
                  { "class": "TGaugeBare", "ancestor": "TControl" },
                  { "class": "TGaugeTop", "ancestor": "TControl", "baseline": { "align": "top", "inset": 3 } },
                  { "class": "TGaugeCenter", "ancestor": "TGaugeTop", "baseline": { "align": "center" } },
                  { "class": "TQuietEdit", "ancestor": "TEdit", "baseline": "none" },
                  { "class": "TShiftedEdit", "ancestor": "TEdit", "baseline": { "align": "top", "inset": 0 } }
                ] }
                """,
        };
        var loaded = CatalogLoader.Load([TestPaths.MetadataIndex, @"C:\pack\index.json"], p => extra.Read(p) ?? (File.Exists(p) ? File.ReadAllBytes(p) : null));
        Assert.Empty(loaded.Problems);
        Assert.All((string[])["TGaugeBare", "TGaugeTop", "TGaugeCenter"], c => Assert.Equal("generic", loaded.Catalog.Find(c)!.RendererId));

        static string Control(string name, string type, int left, int top, int height) =>
            $"  object {name}: {type}\n    Left = {left}\n    Top = {top}\n    Width = 60\n    Height = {height}\n  end\n";
        // 60 wide, 120 apart: the label next to one of them is out of reach (64) of the others.
        var text = "object F: TForm\n  ClientWidth = 1100\n  ClientHeight = 300\n"
            + Control("Bare", "TGaugeBare", 20, 100, 25) + Control("Top", "TGaugeTop", 200, 100, 25)
            + Control("Center", "TGaugeCenter", 380, 100, 25) + Control("Quiet", "TQuietEdit", 560, 100, 23)
            + Control("Shifted", "TShiftedEdit", 740, 100, 23) + Control("E", "TEdit", 920, 100, 23)
            + "  object L: TLabel\n    Left = 100\n    Top = 200\n    Width = 40\n    Height = 15\n    Caption = 'Name'\n  end\n" + "end\n";
        var h = DesignerHarness.FromText(text, catalog: loaded.Catalog);
        using var harness = h;
        h.Settings.SnapToGrid = false;
        var m = FreexComp.Designer.Rendering.RenderContext.FromLayout(h.Object("Top"), h.Layout).Metrics;
        Assert.Equal((10, 15), (m.Ascent, m.LineHeight)); // the deterministic test metrics of the default design font
        int? Offset(string name) => h.Controller.BaselineOffset(h.Object(name));

        // DSG-SUR-208: top → inset + Ascent; center → (H − LineHeight) div 2 + Ascent. A generic control without a rule has none.
        Assert.Null(Offset("Bare"));
        Assert.Equal(3 + 10, Offset("Top"));
        Assert.Equal((25 - 15) / 2 + 10, Offset("Center"));

        // DSG-SUR-209: the metadata value takes precedence over the renderer; "none" suppresses the renderer's baseline.
        Assert.Equal((23 - 15) / 2 + 10, Offset("E"));
        Assert.Null(Offset("Quiet"));
        Assert.Equal(10, Offset("Shifted"));

        // The guidelines use it: the label (baseline at top + 10) dragged next to the generic control snaps to its
        // baseline at 100 + 13; the control without a baseline gives none; the suppressed edit neither.
        var (position, guidelines) = Preview(h, "L", 170, -96); // left 270, top 104: baseline 114 → 113 (the centre line is as near; the baseline has priority)
        Assert.Equal((268, 103), position);
        Assert.Equal([new Guideline(SnapAxis.Y, GuidelineKind.Baseline, 113, h.Object("F"), h.Object("Top"))],
            guidelines.Where(l => l.Axis == SnapAxis.Y));
        Assert.DoesNotContain(Preview(h, "L", -10, -96).Guidelines, l => l.Kind == GuidelineKind.Baseline && ReferenceEquals(l.Reference, h.Object("Bare")));
        Assert.DoesNotContain(Preview(h, "L", 530, -96).Guidelines, l => l.Kind == GuidelineKind.Baseline && ReferenceEquals(l.Reference, h.Object("Quiet")));
    }

    // ---- Phase 8: card acceptance (alignment-guidelines.md DSG-SUR-214; D-116) ----

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-214")]
    [InlineData(23)]
    [InlineData(40)]
    public void BaselineGuide_LabelAndEditOfDifferentHeights(int editHeight)
    {
        // An edit (text centred in its height) and a lower label (text at its top) side by side.
        using var h = Harness($$"""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object E: TEdit
                Left = 100
                Top = 100
                Width = 120
                Height = {{editHeight}}
                AutoSize = False
              end
              object L: TLabel
                Left = 240
                Top = 160
                Width = 40
                Height = 15
                Caption = 'Name'
              end
            end

            """);
        var (edit, label) = (h.Object("E"), h.Object("L"));
        var editOffset = Assert.IsType<int>(h.Controller.BaselineOffset(edit));
        var labelOffset = Assert.IsType<int>(h.Controller.BaselineOffset(label));
        Assert.Equal(((editHeight - 15) / 2 + 10, 10), (editOffset, labelOffset));
        Assert.NotEqual(h.Bounds("E").Height, h.Bounds("L").Height);
        var y = h.Bounds("E").Top + editOffset; // the guideline: the edit's text baseline

        // The label dragged so that its baseline is 1 above that line: it snaps, the guideline is the baseline one.
        var top = y - labelOffset - 1;
        h.Press("L");
        h.MoveBy(0, top - 160);
        Assert.Equal((240, y - labelOffset), h.Position("L"));
        Assert.Equal([new Guideline(SnapAxis.Y, GuidelineKind.Baseline, y, h.Object("F"), edit)], h.Guidelines);
        h.Release();

        // The stored Top of the label is y − its own baseline offset; the edit did not move.
        Assert.Empty(h.Guidelines);
        Assert.Equal(y - labelOffset, FreexComp.Designer.Model.PropertyAccess.GetInt(label, "Top"));
        Assert.Contains($"    Top = {y - labelOffset}\n", h.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Equal(100, FreexComp.Designer.Model.PropertyAccess.GetInt(edit, "Top"));
        Assert.Equal(h.Bounds("E").Top + editOffset, h.Bounds("L").Top + labelOffset);
    }
}
