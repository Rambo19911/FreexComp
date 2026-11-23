using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>
/// Surface rules of design-surface.md and ui/designer.md that had no test of their own (D-087): coordinates, selection
/// events, snapping per axis, anchors after a move, paste in tab-order mode, and the drawing of the frame, the overlay,
/// the drag tip, the lasso, the guidelines, the tray and the tab-order badges on a <see cref="RecordingCanvas"/>.
/// </summary>
[Trait("Phase", "5")]
public sealed class SurfaceRuleTests
{
    private static readonly Rect Viewport = new(0, 0, 2000, 2000);

    // A is the reference; B (50 × 25) is dragged (the pair of SnapTests).
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

    private static RecordingCanvas Render(DesignerHarness h, Rect? viewport = null)
    {
        var canvas = new RecordingCanvas();
        h.Controller.Render(canvas, viewport ?? Viewport);
        return canvas;
    }

    private static string Hex(Rgba c) => c.ToString();

    private static IEnumerable<DrawOp> Colored(RecordingCanvas canvas, string kind, Rgba color) =>
        canvas.Ops.Where(op => op.Kind == kind && op.Text.Contains(Hex(color), StringComparison.Ordinal));

    private static DesignerHarness Guides(string text)
    {
        var h = DesignerHarness.FromText(text);
        h.Settings.SnapToGrid = false;
        return h;
    }

    [Fact]
    [Trait("Req", "DSG-SUR-002")]
    public void PointerCoordinatesAreRoundedHalfAwayFromZero()
    {
        using var h = DesignerHarness.FromText(Pair);
        h.Settings.NoSnapping();
        h.Press("B");
        var (sx, sy) = h.SurfacePoint("B");

        // x = 2k + 0.5 rounds up to 2k + 1 (banker's rounding would give 2k); y = …4 rounds down.
        var x = (sx + 20) / 2 * 2 + 0.5;
        var y = sy + 30.4;
        h.Controller.OnPointer(new PointerInput(PointerKind.Move, x, y, PointerButton.None, InputModifiers.None));
        h.Controller.OnPointer(new PointerInput(PointerKind.Up, x, y, PointerButton.Left, InputModifiers.None));
        var expected = (20 + (int)(x + 0.5) - sx, 160 + 30);
        Assert.Equal(expected, h.Position("B"));
        Assert.Equal(((sx + 20) / 2 * 2) + 1, (int)Math.Round(x, MidpointRounding.AwayFromZero));
    }

    [Fact]
    [Trait("Req", "DSG-SUR-029")]
    public void SelectionChangesRaiseChangedAndAreNotUndoUnits()
    {
        using var h = DesignerHarness.FromText(Pair);
        var raised = 0;
        h.Selection.Changed += (_, _) => raised++;
        var units = h.Undo.UndoCount;

        h.Select("A");
        h.Click("B");
        h.Click("A", modifiers: InputModifiers.Shift);
        h.Execute(Commands.SelectAll);

        Assert.True(raised >= 4, $"{raised} Changed events");
        Assert.Equal(units, h.Undo.UndoCount);
        Assert.False(h.Undo.CanUndo);
    }

    [Fact]
    [Trait("Req", "DSG-SUR-071")]
    public void SnappingIsPerAxisInTheParentsClientCoordinates()
    {
        // The panel's client origin is not on the form grid: snapping uses the candidate rectangle in the panel's own
        // client coordinates. X snaps to A's left edge (guideline), Y independently to the grid.
        using var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object P: TPanel
                Left = 13
                Top = 13
                Width = 250
                Height = 180
                TabOrder = 0
                object A: TButton
                  Left = 40
                  Top = 8
                  Width = 75
                  Height = 25
                  TabOrder = 0
                end
                object B: TButton
                  Left = 3
                  Top = 60
                  Width = 50
                  Height = 25
                  TabOrder = 1
                end
              end
            end

            """);
        h.Press("B");
        h.MoveBy(35, 6); // candidate (38, 66) in P's client
        Assert.Equal((40, 64), h.Position("B"));
        var line = Assert.Single(h.Guidelines);
        Assert.Equal((SnapAxis.X, GuidelineKind.Edge, 40), (line.Axis, line.Kind, line.Coordinate));
        Assert.Same(h.Object("P"), line.Parent);
        h.Release();
        Assert.Equal((40, 64), h.Position("B"));
    }

    [Fact]
    [Trait("Req", "DSG-SUR-077")]
    public void WithoutGuidelineOrGridTheRawValueIsUsed()
    {
        using var h = Guides(Pair);

        // Far from every reference and with the grid off: the raw integer delta.
        h.Press("B");
        h.MoveBy(57, 23);
        Assert.Empty(h.Guidelines);
        h.Release();
        Assert.Equal((77, 183), h.Position("B"));

        // Grid on but Alt held during the move (snapping suspended, DSG-SUR-070): raw as well; without Alt the grid applies.
        h.Settings.SnapToGrid = true;
        h.Press("B");
        h.MoveBy(5, 5, InputModifiers.Alt);
        h.Release(InputModifiers.Alt);
        Assert.Equal((82, 188), h.Position("B"));
        h.Press("B");
        h.MoveBy(5, 5);
        h.Release();
        Assert.Equal((88, 192), h.Position("B"));
    }

    [Fact]
    [Trait("Req", "DSG-SUR-060")]
    public void AnchorBaselineAfterAMoveIsTheNewStoredBounds()
    {
        using var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object Ok: TButton
                Left = 300
                Top = 250
                Width = 75
                Height = 25
                Anchors = [akRight, akBottom]
                TabOrder = 0
              end
            end

            """);
        h.Settings.NoSnapping();
        h.Press("Ok");
        h.MoveBy(-100, -50);
        h.Release();
        Assert.Equal((200, 200), h.Position("Ok"));
        Assert.Contains("    Left = 200\n    Top = 200\n", h.Text, StringComparison.Ordinal);

        // Growing the form by 100 × 40 moves the button by that much from its new position (not from the old one).
        var grown = h.Engine.ComputeWithResize(h.Document, "F", 500, 340);
        Assert.Equal(new Rect(300, 240, 75, 25), grown.GetBounds(h.Object("Ok")));
    }

    [Fact]
    [Trait("Req", "DSG-SUR-128")]
    public void PasteIsDisabledInTabOrderMode()
    {
        using var h = DesignerHarness.FromText(Pair);
        h.Host.Clipboard.SetText("object C: TButton\n  Left = 8\n  Top = 8\n  Width = 75\n  Height = 25\nend\n");
        Assert.True(h.Controller.CanExecute(Commands.Paste));

        h.Execute(Commands.TabOrder);
        Assert.False(h.Controller.CanExecute(Commands.Paste));
        var text = h.Text;
        h.Execute(Commands.Paste);
        Assert.Equal(text, h.Text);
        Assert.Null(h.Document.FindByName("C"));

        h.Controller.ExitTabOrderMode();
        Assert.True(h.Controller.CanExecute(Commands.Paste));
    }

    [Fact]
    [Trait("Req", "DSG-SUR-044")]
    public void PressOutsideTheFormInTabOrderModeOnlyClearsTheFocusRectangle()
    {
        using var h = DesignerHarness.FromText(Pair.Replace("    Height = 25\n  end\n  object B", "    Height = 25\n    TabOrder = 0\n  end\n  object B", StringComparison.Ordinal)
            .Replace("    Height = 25\n  end\nend", "    Height = 25\n    TabOrder = 1\n  end\nend", StringComparison.Ordinal));
        h.Execute(Commands.TabOrder);
        h.Click("B"); // counter[F] = 1: B is 0
        var text = h.Text;
        var undo = h.Undo.UndoCount;
        Assert.True(h.Key(DesignKey.Tab));
        Assert.NotNull(h.Controller.TabOrderFocus);

        // A press inside the form on empty client area does nothing (DSG-SUR-093): the focus rectangle stays.
        var frame = h.Controller.Geometry.GetBounds(h.Document.Root);
        h.ClickAt((frame.Left + 300, frame.Top + 250));
        Assert.NotNull(h.Controller.TabOrderFocus);

        // Outside the form (the backdrop): the focus rectangle goes; mode, selection, counters and the text stay.
        h.ClickAt((frame.Right + 100, frame.Bottom + 100));
        Assert.Null(h.Controller.TabOrderFocus);
        Assert.Equal(InteractionState.TabOrder, h.Controller.State);
        Assert.Equal(["F"], h.Selected);
        Assert.Equal(text, h.Text);
        Assert.Equal(undo, h.Undo.UndoCount);
        h.Click("A"); // the counter was kept: A gets position 1 (already 1, nothing changes)
        Assert.Equal(text, h.Text);
        h.Controller.ExitTabOrderMode();
    }

    [Fact]
    [Trait("Req", "UI-DSG-083")]
    public void TrayTooltipNamesTheClassAndOffersTheComponentEditor()
    {
        using var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 300
              ClientHeight = 200
              object Timer1: TTimer
                DesignLeft = 8
                DesignTop = 8
              end
              object MainMenu1: TMainMenu
                DesignLeft = 88
                DesignTop = 8
              end
            end

            """);
        var g = h.Controller.Geometry;
        (double, double) Center(Rect r) => (r.Left + r.Width / 2.0, r.Top + r.Height / 2.0);

        // The tile (and the name below it) of a class without componentEditor: the class name only.
        var (tx, ty) = Center(g.GetTrayTile(h.Object("Timer1")));
        Assert.Equal(new TrayToolTip("TTimer", false), h.Controller.GetTrayToolTip(tx, ty));
        var cell = g.GetTrayCell(h.Object("Timer1"));
        Assert.Equal(new TrayToolTip("TTimer", false), h.Controller.GetTrayToolTip(cell.Left + 36, cell.Bottom - 2));

        // With a componentEditor (the Menu Designer): a second line offering the double-click.
        var (mx, my) = Center(g.GetTrayTile(h.Object("MainMenu1")));
        Assert.Equal(new TrayToolTip("TMainMenu", true), h.Controller.GetTrayToolTip(mx, my));

        // Not over a tray item (the tray background, the form, the backdrop): no tooltip.
        Assert.Null(h.Controller.GetTrayToolTip(g.TrayRect.Right - 2, g.TrayRect.Bottom - 2));
        Assert.Null(h.Controller.GetTrayToolTip(g.FrameRect.Left + 50, g.FrameRect.Top + 80));
        Assert.Null(h.Controller.GetTrayToolTip(1990, 1990));

        // No extra adornment: both tiles draw the same frame (fill and border) and nothing else outside their cell.
        var ops = Render(h).Ops.Select(o => o.Text).ToList();
        var r = h.Controller.Palette.Renderers;
        Assert.Equal(2, ops.Count(o => o == $"FillRect 0,0,32,32 {Hex(r.TileBackground)}"));
        Assert.Equal(2, ops.Count(o => o == $"DrawRect 0,0,32,32 {Hex(r.TileBorder)} 1 Solid"));
    }

    [Fact]
    [Trait("Req", "UI-DSG-005")]
    public void DesignViewHasNoZoom()
    {
        using var h = DesignerHarness.FromFile("forms/canonical/hello.form");
        var g = h.Controller.Geometry;

        // 100 %: one form pixel is one surface DIP; a pointer delta of n moves a control by n.
        Assert.Equal((400, 240), (g.FormClientRect.Width, g.FormClientRect.Height));
        Assert.Equal((75, 25), (g.GetBounds(h.Object("GreetButton")).Width, g.GetBounds(h.Object("GreetButton")).Height));
        Assert.DoesNotContain(typeof(Commands).GetFields().Select(f => f.GetValue(null) as string), id => id?.Contains("zoom", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(typeof(DesignSurfaceController).GetProperties(), p => p.Name.Contains("Zoom", StringComparison.Ordinal) || p.Name.Contains("Scale", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "UI-DSG-010")]
    [Trait("Req", "UI-DSG-011")]
    [Trait("Req", "UI-DSG-013")]
    public void FrameAndCaption()
    {
        using var h = DesignerHarness.FromFile("forms/canonical/hello.form");
        var p = h.Controller.Palette.Renderers;
        var g = h.Controller.Geometry;

        // Outer frame (400 + 2) × (240 + 30 + 2) at the frame origin; caption bar 30 high directly above the client area.
        Assert.Equal(new Rect(16, 16, 402, 272), g.FrameRect);
        Assert.Equal(new Rect(17, 47, 400, 240), g.FormClientRect);
        var ops = Render(h).Ops.Select(o => o.Text).ToList();
        Assert.Contains($"DrawRect 0,0,402,272 {Hex(p.BorderDark)} 1 Solid", ops);
        Assert.Contains($"FillRect 1,1,400,30 {Hex(p.CaptionBackground)}", ops);
        Assert.Contains($"FillRect 1,31,400,240 {Hex(p.Face)}", ops);

        // 8 px gap, the 16 × 16 window glyph (outline with a thicker top edge), 8 px gap, the caption text (12 px, centred
        // vertically, ellipsis) up to the three 46 × 30 caption cells; 10 × 10 glyphs in caption.fg.
        Assert.Contains($"DrawRect 9,8,16,16 {Hex(p.GrayText)} 1 Solid", ops);
        Assert.Contains($"FillRect 9,8,16,3 {Hex(p.GrayText)}", ops);
        var captionWidth = 402 - 1 - 3 * 46 - 8 - 33;
        Assert.Contains($"DrawText \"Hello\" 33,1,{captionWidth},30 \"Segoe UI\" 12 {Hex(p.CaptionText)} Left Center ellipsis", ops);
        var cells = new[] { 402 - 1 - 46 * 3, 402 - 1 - 46 * 2, 402 - 1 - 46 };
        Assert.Contains($"DrawLine {cells[0] + 18},16.5,{cells[0] + 28},16.5 {Hex(p.CaptionText)} 1 Solid", ops); // minimize
        Assert.Contains($"DrawRect {cells[1] + 18},11,10,10 {Hex(p.CaptionText)} 1 Solid", ops);              // maximize
        Assert.Contains($"DrawLine {cells[2] + 18}.5,11.5,{cells[2] + 27}.5,20.5 {Hex(p.CaptionText)} 1 Solid", ops); // close

        // The form's Left / Top do not move the frame.
        h.Editor.Apply([new SetProperty("MainForm", "Left", new IntegerValue(900, false)), new SetProperty("MainForm", "Top", new IntegerValue(700, false))]);
        Assert.Equal(new Rect(16, 16, 402, 272), h.Controller.Geometry.FrameRect);
        Assert.Contains($"DrawRect 0,0,402,272 {Hex(p.BorderDark)} 1 Solid", Render(h).Ops.Select(o => o.Text));
    }

    [Fact]
    [Trait("Req", "UI-DSG-035")]
    public void HandlesAreAnOverlayNotClippedByTheParent()
    {
        using var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object P: TPanel
                Left = 100
                Top = 50
                Width = 150
                Height = 100
                TabOrder = 0
                object Inner: TButton
                  Left = 0
                  Top = 0
                  Width = 75
                  Height = 25
                  TabOrder = 0
                end
              end
            end

            """);
        var p = h.Controller.Palette;
        h.Select("Inner");
        var g = h.Controller.Geometry;
        var inner = g.GetBounds(h.Object("Inner"));
        var client = g.GetClientRect(h.Object("P"));
        Assert.True(inner.Left <= client.Left && inner.Top <= client.Top);

        // The NW handle (7 × 7 centred on the corner pixel) reaches 3 DIP outside the panel's client area and is not cut.
        var nw = Colored(Render(h), "FillRect", p.HandleFill).First(op => op.Text.EndsWith($",7,7 {Hex(p.HandleFill)}", StringComparison.Ordinal));
        Assert.Equal((inner.Left - 3.0, inner.Top - 3.0, inner.Left + 4.0, inner.Top + 4.0), nw.Extent);

        // It is clipped to the viewport.
        var cut = new Rect(inner.Left - 1, inner.Top - 1, 500, 500);
        nw = Colored(Render(h, cut), "FillRect", p.HandleFill).First(op => op.Text.EndsWith($",7,7 {Hex(p.HandleFill)}", StringComparison.Ordinal));
        Assert.Equal((inner.Left - 1.0, inner.Top - 1.0, inner.Left + 4.0, inner.Top + 4.0), nw.Extent);
    }

    [Fact]
    [Trait("Req", "UI-DSG-037")]
    public void ResizeTipShowsWidthByHeight()
    {
        using var h = DesignerHarness.FromText(Pair);
        h.Settings.NoSnapping();
        var p = h.Controller.Palette;
        h.Select("B");
        var start = h.HandlePoint("B", HandleKind.SE);
        h.PressHandle("B", HandleKind.SE);
        h.MoveBy(10, 5);
        var pointer = (X: start.X + 10, Y: start.Y + 5);
        var canvas = Render(h);

        // The control is redrawn at its new size; the tip box (bg.raised, border.default, 20 high) sits 12 DIP right of and
        // below the pointer and shows "Width × Height" in type.codeSmall.
        Assert.Equal((60, 30), h.Size("B"));
        var text = Assert.Single(canvas.Ops, op => op.Kind == "DrawText" && op.Text.Contains("\"60 × 30\"", StringComparison.Ordinal));
        Assert.Contains("\"Cascadia Mono\" 12", text.Text, StringComparison.Ordinal);
        var box = Assert.Single(Colored(canvas, "FillRect", p.Raised), op => op.Extent is { } e && e.Left == pointer.X + 12 && e.Top == pointer.Y + 12);
        Assert.Equal(20.0, box.Extent!.Value.Bottom - box.Extent!.Value.Top);
        Assert.Contains(canvas.Ops, op => op.Kind == "DrawRect" && op.Extent == box.Extent && op.Text.Contains(Hex(p.BorderDefault), StringComparison.Ordinal));
        h.Release();
        Assert.DoesNotContain(Render(h).Ops, op => op.Text.Contains("\"60 × 30\"", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "UI-DSG-040")]
    public void LassoInAContainerIsClippedToItsClientArea()
    {
        using var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object P: TPanel
                Left = 100
                Top = 50
                Width = 150
                Height = 100
                TabOrder = 0
              end
            end

            """);
        var p = h.Controller.Palette;
        var client = h.Controller.Geometry.GetClientRect(h.Object("P"));

        // Ctrl + drag on the panel's empty client area starts a lasso in the panel; dragged far beyond it.
        h.PressAt((client.Left + 20, client.Top + 20), InputModifiers.Ctrl);
        h.MoveBy(300, 300);
        var canvas = Render(h);
        var borderAt = canvas.Ops.ToList().FindIndex(op => op.Kind == "DrawRect" && op.Text.EndsWith($"{Hex(p.RubberBandBorder)} 1 Dash", StringComparison.Ordinal));
        Assert.True(borderAt > 0, canvas.ToText());
        var (fill, border) = (canvas.Ops[borderAt - 1], canvas.Ops[borderAt]);
        Assert.Equal("FillRect", fill.Kind);
        Assert.EndsWith(Hex(p.RubberBandFill), fill.Text, StringComparison.Ordinal);
        var clipped = ((double)client.Left + 20, (double)client.Top + 20, (double)client.Right, (double)client.Bottom);
        Assert.Equal(clipped, fill.Extent);
        Assert.Equal(clipped, border.Extent);
        h.Key(DesignKey.Escape);
    }

    [Fact]
    [Trait("Req", "UI-DSG-050")]
    [Trait("Req", "UI-DSG-051")]
    public void EdgeGuidelineOnlyWhileASnapIsApplied()
    {
        using var h = Guides(Pair);
        var p = h.Controller.Palette;
        bool AnyGuide(RecordingCanvas c) => c.Ops.Any(op => op.Kind == "DrawLine"
            && (op.Text.Contains(Hex(p.GuidelineEdge), StringComparison.Ordinal) || op.Text.Contains(Hex(p.GuidelineBaseline), StringComparison.Ordinal)
                || op.Text.Contains(Hex(p.GuidelineMargin), StringComparison.Ordinal)));

        Assert.False(AnyGuide(Render(h))); // idle

        // B's left edge snaps to A's left edge (form client x 100 = surface 117): a vertical line from the outermost top
        // (A, 147) to the outermost bottom (B, 232), extended by 4 at both ends.
        h.Press("B");
        h.MoveBy(83, 0);
        var line = Assert.Single(Colored(Render(h), "DrawLine", p.GuidelineEdge));
        Assert.Equal($"DrawLine 117.5,143,117.5,236 {Hex(p.GuidelineEdge)} 1 Solid", line.Text);

        // No snap applied (5 px away): no line, though the drag goes on.
        h.MoveBy(2, 0);
        Assert.Equal(InteractionState.Moving, h.Controller.State);
        Assert.False(AnyGuide(Render(h)));
        h.Key(DesignKey.Escape);

        // designer.useGuidelines off: nothing is drawn.
        h.Settings.UseGuidelines = false;
        h.Press("B");
        h.MoveBy(83, 0);
        Assert.False(AnyGuide(Render(h)));
        h.Release();
        Assert.False(AnyGuide(Render(h))); // after the drop
    }

    [Fact]
    [Trait("Req", "UI-DSG-052")]
    public void BaselineGuidelineSpansBothControls()
    {
        using var h = Guides("""
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
        var p = h.Controller.Palette;
        h.Press("L");
        h.MoveBy(0, -38);
        Assert.Equal(GuidelineKind.Baseline, Assert.Single(h.Guidelines).Kind);

        // Baseline 114 (surface 161), from E's left (117) − 4 to L's right (297) + 4.
        var line = Assert.Single(Colored(Render(h), "DrawLine", p.GuidelineBaseline));
        Assert.Equal($"DrawLine 113,161.5,301,161.5 {Hex(p.GuidelineBaseline)} 1 Solid", line.Text);
        h.Key(DesignKey.Escape);
    }

    [Fact]
    [Trait("Req", "UI-DSG-053")]
    public void MarginGuidelineInTheGapWithTicks()
    {
        using var h = Guides(Pair);
        var p = h.Controller.Palette;
        h.Press("B");
        h.MoveBy(165, 0); // left 185 → 183 = A's right + 8
        Assert.Equal(GuidelineKind.Margin, Assert.Single(h.Guidelines).Kind);

        // The gap is A's right (surface 192) … B's left (200); no vertical overlap, so it is centred on B (207 + 12);
        // perpendicular ticks of 5 DIP at both ends.
        var lines = Colored(Render(h), "DrawLine", p.GuidelineMargin).Select(o => o.Text).ToList();
        var c = Hex(p.GuidelineMargin);
        Assert.Equal([$"DrawLine 192,219.5,200,219.5 {c} 1 Solid", $"DrawLine 192.5,217,192.5,222 {c} 1 Solid", $"DrawLine 199.5,217,199.5,222 {c} 1 Solid"], lines);
        h.Key(DesignKey.Escape);
    }

    [Fact]
    [Trait("Req", "UI-DSG-054")]
    public void AtMostOneGuidelinePerAxisAndKind()
    {
        using var h = Guides(Pair);
        var p = h.Controller.Palette;

        // B moved next to A on both axes: several edges and centres coincide; one line per axis is drawn.
        h.Press("B");
        h.MoveBy(82, -58); // (102, 102): left, right … top, bottom all within reach of A
        Assert.Equal((100, 100), h.Position("B"));
        var groups = h.Guidelines.GroupBy(l => (l.Axis, l.Kind)).ToList();
        Assert.All(groups, grp => Assert.Single(grp));
        Assert.Equal([SnapAxis.X, SnapAxis.Y], h.Guidelines.Select(l => l.Axis).Order());
        var canvas = Render(h);
        Assert.Equal(h.Guidelines.Count, canvas.Ops.Count(op => op.Kind == "DrawLine"
            && (op.Text.Contains(Hex(p.GuidelineEdge), StringComparison.Ordinal) || op.Text.Contains(Hex(p.GuidelineBaseline), StringComparison.Ordinal))));
        h.Key(DesignKey.Escape);
    }

    [Fact]
    [Trait("Req", "UI-DSG-061")]
    public void EmptyTrayShowsItsText()
    {
        using var h = DesignerHarness.FromFile("forms/canonical/hello.form");
        var p = h.Controller.Palette;
        var tray = h.Controller.Geometry.TrayRect;
        Assert.Empty(h.Controller.Geometry.TrayItems);
        Assert.Contains($"DrawText \"Non-visual components appear here\" {tray.Left},{tray.Top},{tray.Width},{tray.Height} \"Segoe UI\" 11 {Hex(p.TextTertiary)} Center Center",
            Render(h).Ops.Select(o => o.Text));
    }

    [Fact]
    [Trait("Req", "UI-DSG-062")]
    [Trait("Req", "UI-DSG-064")]
    public void TrayCellTileAndName()
    {
        using var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 300
              ClientHeight = 200
              object Timer1: TTimer
                DesignLeft = 8
                DesignTop = 8
              end
              object AVeryLongComponentName1: TTimer
                DesignLeft = 88
                DesignTop = 8
              end
            end

            """);
        var p = h.Controller.Palette;
        var g = h.Controller.Geometry;
        var timer = h.Object("Timer1");

        // The cell's top-left is (DesignLeft, DesignTop) in tray coordinates; 72 × 50; the 32 × 32 tile centred at its top.
        var origin = g.TrayToSurface((8, 8));
        Assert.Equal(new Rect(origin.X, origin.Y, 72, 50), g.GetTrayCell(timer));
        Assert.Equal(new Rect(origin.X + 20, origin.Y, 32, 32), g.GetTrayTile(timer));

        var ops = Render(h).Ops.Select(o => o.Text).ToList();
        var r = p.Renderers;
        Assert.Contains($"FillRect 0,0,32,32 {Hex(r.TileBackground)}", ops);
        Assert.Contains($"DrawRect 0,0,32,32 {Hex(r.TileBorder)} 1 Solid", ops);

        // No palette icon in the metadata: the generic component glyph at icon.palette (24).
        Assert.Contains("DrawIcon \"builtin:component\" 4,4,24,24", ops);

        // The name 2 DIP under the tile, type.caption, text.primary, centred, clipped with an ellipsis to the cell width.
        Assert.Contains($"DrawText \"Timer1\" {origin.X},{origin.Y + 34},72,16 \"Segoe UI\" 11 {Hex(p.TextPrimary)} Center Top ellipsis", ops);
        Assert.Contains(ops, o => o.StartsWith("DrawText \"AVeryLongComponentName1\" ", StringComparison.Ordinal) && o.Contains(",72,16 ", StringComparison.Ordinal)
            && o.EndsWith(" ellipsis", StringComparison.Ordinal));

        // designer.showComponentCaptions off: no names.
        using var bare = DesignerHarness.FromText(h.Text);
        bare.Settings.ShowComponentCaptions = false;
        Assert.DoesNotContain(Render(bare).Ops, o => o.Text.StartsWith("DrawText \"Timer1\"", StringComparison.Ordinal));
    }

    private const string TabForm = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object B0: TButton
            Left = 8
            Top = 8
            Width = 75
            Height = 25
            TabOrder = 0
          end
          object B1: TButton
            Left = 8
            Top = 48
            Width = 75
            Height = 25
            TabOrder = 1
          end
          object NoStop: TEdit
            Left = 8
            Top = 88
            Width = 121
            Height = 23
            TabOrder = 2
            TabStop = False
          end
          object Lbl: TLabel
            Left = 200
            Top = 8
            Width = 30
            Height = 15
            Caption = 'L'
          end
        end

        """;

    [Fact]
    [Trait("Req", "UI-DSG-070")]
    [Trait("Req", "UI-DSG-071")]
    [Trait("Req", "UI-DSG-072")]
    [Trait("Req", "UI-DSG-073")]
    public void TabOrderBadges()
    {
        using var h = DesignerHarness.FromText(TabForm);
        var p = h.Controller.Palette;
        var g = h.Controller.Geometry;
        h.Select("B1");
        h.Execute(Commands.TabOrder);
        var canvas = Render(h);
        var ops = canvas.Ops.Select(o => o.Text).ToList();

        // A badge (16 high, at least 18 wide) at the top-left of every focusable control, with its TabOrder in bold
        // type.caption; none on the label.
        string Badge(string name, int width = 18) { var b = g.GetBounds(h.Object(name)); return $"{b.Left},{b.Top},{width},16"; }
        Assert.Contains($"FillRect {Badge("B0")} {Hex(p.TabBadgeBackground)}", ops);
        Assert.Contains($"DrawText \"0\" {Badge("B0")} \"Segoe UI\" 11 {Hex(p.TabBadgeText)} Center Center bold", ops);
        Assert.Contains($"DrawText \"1\" {Badge("B1")} \"Segoe UI\" 11 {Hex(p.TabBadgeText)} Center Center bold", ops);
        Assert.Equal(3, Colored(canvas, "FillRect", p.TabBadgeBackground).Count());
        var lbl = g.GetBounds(h.Object("Lbl"));
        Assert.DoesNotContain(canvas.Ops, op => op.Text.StartsWith($"FillRect {lbl.Left},{lbl.Top},", StringComparison.Ordinal)
            && (op.Text.Contains(Hex(p.TabBadgeBackground), StringComparison.Ordinal) || op.Text.Contains(Hex(p.Accent), StringComparison.Ordinal)));

        // TabStop = False: 50 % opacity and a struck-through number.
        var noStop = ops.IndexOf($"DrawText \"2\" {Badge("NoStop")} \"Segoe UI\" 11 {Hex(p.TabBadgeText)} Center Center bold strikeout");
        Assert.True(noStop > 0, canvas.ToText());
        Assert.Equal("PushOpacity 0.5", ops[noStop - 2]);
        Assert.Equal($"FillRect {Badge("NoStop")} {Hex(p.TabBadgeBackground)}", ops[noStop - 1]);

        // Handles, grid and guidelines are hidden.
        Assert.DoesNotContain(canvas.Ops, op => op.Text.Contains(Hex(p.HandleBorder), StringComparison.Ordinal));
        Assert.Empty(Colored(canvas, "FillRect", p.GridDot));

        // A control clicked in the current pass uses accent.default / text.onAccent.
        h.Click("B1");
        ops = Render(h).Ops.Select(o => o.Text).ToList();
        Assert.Contains($"FillRect {Badge("B1")} {Hex(p.Accent)}", ops);
        Assert.Contains($"DrawText \"0\" {Badge("B1")} \"Segoe UI\" 11 {Hex(p.OnAccent)} Center Center bold", ops);
        Assert.Contains($"FillRect {Badge("B0")} {Hex(p.TabBadgeBackground)}", ops);
        h.Controller.ExitTabOrderMode();
    }

    [Fact]
    [Trait("Req", "DSG-RND-003")]
    public void RecordingCanvasMeasuresTextDeterministically()
    {
        var canvas = new RecordingCanvas();
        var regular = new TextStyle("Segoe UI", 12, false, false, false, false);

        // 6 px per character at 12 px, scaled linearly with the size, bold × 1.1, rounded up.
        Assert.Equal(30, canvas.MeasureText("Hello", regular).Width);
        Assert.Equal(45, canvas.MeasureText("Hello", regular with { SizePx = 18 }).Width);
        Assert.Equal(33, canvas.MeasureText("Hello", regular with { Bold = true }).Width);
        Assert.Equal(19, canvas.MeasureText("abc", regular with { SizePx = 11, Bold = true }).Width); // 18.15 → 19
        Assert.Equal(28, canvas.MeasureText("abcde", regular with { SizePx = 11 }).Width);             // 27.5 → 28
        Assert.Equal(RecordingCanvas.Metrics(regular), canvas.GetMetrics(regular));

        // Every call is recorded in order with its golden text; measuring records nothing.
        canvas.FillRect(new Rect(1, 2, 3, 4), Rgba.Parse("#102030"));
        canvas.DrawText("x", new Rect(0, 0, 10, 10), regular, Rgba.Parse("#000000"));
        Assert.Equal(["FillRect", "DrawText"], canvas.Ops.Select(o => o.Kind));
        Assert.StartsWith("FillRect 1,2,3,4 ", canvas.Ops[0].Text, StringComparison.Ordinal);
    }
}
