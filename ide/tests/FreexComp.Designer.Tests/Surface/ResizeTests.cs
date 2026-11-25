using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Resize drags on the design surface (design-surface.md §7, §6, §12; D-042).</summary>
public sealed class ResizeTests
{
    private const string Hello = "forms/canonical/hello.form"; // NameEdit (16, 16, 200, 23), GreetButton (232, 15, 75, 25)

    [Theory]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-081")]
    [InlineData(HandleKind.NW, 242, 21, 65, 19)]
    [InlineData(HandleKind.N, 232, 21, 75, 19)]
    [InlineData(HandleKind.NE, 232, 21, 85, 19)]
    [InlineData(HandleKind.E, 232, 15, 85, 25)]
    [InlineData(HandleKind.SE, 232, 15, 85, 31)]
    [InlineData(HandleKind.S, 232, 15, 75, 31)]
    [InlineData(HandleKind.SW, 242, 15, 65, 31)]
    [InlineData(HandleKind.W, 242, 15, 65, 25)]
    public void EachHandleMovesItsEdges(HandleKind handle, int left, int top, int width, int height)
    {
        using var h = DesignerHarness.FromFile(Hello);
        h.Settings.NoSnapping();
        var original = h.Text;
        h.Select("GreetButton");

        h.PressHandle("GreetButton", handle);
        h.MoveBy(10, 6); // the dragged edge(s) follow the pointer; the others stay
        Assert.Equal(InteractionState.Resizing, h.Controller.State);
        Assert.Equal(new Rect(left, top, width, height), h.Bounds("GreetButton")); // live preview
        Assert.Equal(original, h.Text);
        h.Release();
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        Assert.Equal(new Rect(left, top, width, height), h.Bounds("GreetButton"));
        Assert.Equal(["GreetButton"], h.Selected);
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Equal("Undo_Designer_Resize", h.Undo.UndoTitleKey);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // Escape and CaptureLost cancel; nothing is recorded.
        h.PressHandle("GreetButton", handle);
        h.MoveBy(10, 6);
        Assert.True(h.Key(DesignKey.Escape));
        Assert.Equal(new Rect(232, 15, 75, 25), h.Bounds("GreetButton"));
        h.Release();
        h.PressHandle("GreetButton", handle);
        h.MoveBy(10, 6);
        h.CaptureLost();
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        Assert.Equal(original, h.Text);
        Assert.Equal(0, h.Undo.UndoCount);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-075")]
    [Trait("Req", "DSG-SUR-076")]
    [Trait("Req", "DSG-SUR-078")]
    public void SnapsTheDraggedEdgeOnly()
    {
        using var h = DesignerHarness.FromFile(Hello);
        h.Settings.UseGuidelines = false;
        h.Select("GreetButton");

        // Grid: the dragged edges are rounded (right 317 → 320, bottom 46 → 48); the fixed edges stay where they are.
        h.PressHandle("GreetButton", HandleKind.SE);
        h.MoveBy(10, 6);
        h.Release();
        Assert.Equal(new Rect(232, 15, 88, 33), h.Bounds("GreetButton"));
        h.ExecuteUndo();

        // Guidelines: the left edge dragged to 218 snaps to NameEdit's right edge (216); Alt disables snapping.
        h.Settings.SnapToGrid = false;
        h.Settings.UseGuidelines = true;
        h.PressHandle("GreetButton", HandleKind.W);
        h.MoveBy(-14, 0);
        Assert.Equal(new Rect(216, 15, 91, 25), h.Bounds("GreetButton"));
        Assert.Equal([new Guideline(SnapAxis.X, GuidelineKind.Edge, 216, h.Object("MainForm"), h.Object("NameEdit"))], h.Guidelines);
        h.MoveBy(0, 0, InputModifiers.Alt);
        Assert.Equal(new Rect(218, 15, 89, 25), h.Bounds("GreetButton"));
        Assert.Empty(h.Guidelines);
        h.Release(InputModifiers.Alt); // the state at the release counts
        Assert.Equal(new Rect(218, 15, 89, 25), h.Bounds("GreetButton"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-082")]
    public void RespectsMinimumAndConstraints()
    {
        using var h = DesignerHarness.FromFile(Hello);
        h.Settings.NoSnapping();
        h.Select("GreetButton");

        // Never below 1 × 1; the opposite edge never moves; no flipping.
        h.PressHandle("GreetButton", HandleKind.E);
        h.MoveBy(-100, 0);
        Assert.Equal(new Rect(232, 15, 1, 25), h.Bounds("GreetButton"));
        h.Release();
        Assert.Equal(new Rect(232, 15, 1, 25), h.Bounds("GreetButton"));
        h.ExecuteUndo();
        h.PressHandle("GreetButton", HandleKind.NW);
        h.MoveBy(100, 100);
        h.Release();
        Assert.Equal(new Rect(306, 39, 1, 1), h.Bounds("GreetButton"));
        h.ExecuteUndo();

        // Constraints: MinWidth 50, MaxHeight 30.
        using var c = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object Btn: TButton
                Left = 100
                Top = 100
                Width = 75
                Height = 25
                Constraints.MaxHeight = 30
                Constraints.MinWidth = 50
              end
            end

            """);
        c.Settings.NoSnapping();
        c.Select("Btn");
        c.PressHandle("Btn", HandleKind.E);
        c.MoveBy(-100, 0);
        c.Release();
        Assert.Equal(new Rect(100, 100, 50, 25), c.Bounds("Btn"));
        c.PressHandle("Btn", HandleKind.N);
        c.MoveBy(0, -100);
        c.Release();
        Assert.Equal(new Rect(100, 95, 50, 30), c.Bounds("Btn")); // the bottom edge (125) stays
        c.PressHandle("Btn", HandleKind.SW);
        c.MoveBy(-20, 50);
        c.Release();
        Assert.Equal(new Rect(80, 95, 70, 30), c.Bounds("Btn"));
        Assert.Equal(3, c.Undo.UndoCount);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-083")]
    public void ShiftKeepsAspectRatio()
    {
        using var h = DesignerHarness.FromFile(Hello); // GreetButton 75 × 25: ratio 3
        h.Settings.NoSnapping();
        h.Select("GreetButton");

        // Width changes more (30 / 75 > 4 / 25): it drives, height = round(105 / 3) = 35.
        h.PressHandle("GreetButton", HandleKind.SE, InputModifiers.Shift);
        h.MoveBy(30, 4);
        Assert.Equal(new Rect(232, 15, 105, 35), h.Bounds("GreetButton"));
        h.MoveBy(0, 0, InputModifiers.None); // re-evaluated on every move event
        Assert.Equal(new Rect(232, 15, 105, 29), h.Bounds("GreetButton"));
        h.Release(InputModifiers.Shift);
        Assert.Equal(new Rect(232, 15, 105, 35), h.Bounds("GreetButton"));
        h.ExecuteUndo();

        // Height drives at the NW corner (20 / 25 > 6 / 75): height 45, width 135; the SE corner (307, 40) stays.
        h.PressHandle("GreetButton", HandleKind.NW, InputModifiers.Shift);
        h.MoveBy(-6, -20);
        h.Release();
        Assert.Equal(new Rect(172, -5, 135, 45), h.Bounds("GreetButton"));
        h.ExecuteUndo();

        // Snapping applies to the driving axis only: right 337 → 336 (width 104), height round(104 / 3) = 35 unsnapped.
        h.Settings.SnapToGrid = true;
        h.PressHandle("GreetButton", HandleKind.SE, InputModifiers.Shift);
        h.MoveBy(30, 4);
        h.Release();
        Assert.Equal(new Rect(232, 15, 104, 35), h.Bounds("GreetButton"));
        h.ExecuteUndo();

        // A mid-edge handle ignores Shift.
        h.Settings.SnapToGrid = false;
        h.PressHandle("GreetButton", HandleKind.E, InputModifiers.Shift);
        h.MoveBy(30, 4);
        h.Release();
        Assert.Equal(new Rect(232, 15, 105, 25), h.Bounds("GreetButton"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-080")]
    public void MultiSelectionResizesAll()
    {
        using var h = DesignerHarness.FromFile(Hello);
        h.Settings.NoSnapping();
        var original = h.Text;
        h.Select("NameEdit", "GreetButton"); // GreetButton is primary: only it shows resize handles

        h.PressHandle("GreetButton", HandleKind.SE);
        h.MoveBy(10, 6);
        // The same edge deltas, live; an AutoSize edit keeps its intrinsic height (the layout engine's edit rule), so
        // only its width follows here.
        Assert.Equal(new Rect(16, 16, 210, 23), h.Bounds("NameEdit"));
        h.Release();
        Assert.Equal(new Rect(232, 15, 85, 31), h.Bounds("GreetButton"));
        Assert.Equal(new Rect(16, 16, 210, 23), h.Bounds("NameEdit"));
        Assert.Equal(["NameEdit", "GreetButton"], h.Selected);
        Assert.Equal(1, h.Undo.UndoCount);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // The primary's clamped edge delta (+74) is what the others get; each is clamped on its own.
        h.PressHandle("GreetButton", HandleKind.W);
        h.MoveBy(100, 0);
        h.Release();
        Assert.Equal(new Rect(306, 15, 1, 25), h.Bounds("GreetButton"));
        Assert.Equal(new Rect(90, 16, 126, 23), h.Bounds("NameEdit"));
        h.ExecuteUndo();

        // Tray items are not resized; a second control gets the same deltas on both axes.
        using var d = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object Panel1: TPanel
                Left = 8
                Top = 50
                Width = 200
                Height = 100
              end
              object Btn2: TButton
                Left = 250
                Top = 50
                Width = 75
                Height = 25
              end
              object Timer1: TTimer
                DesignLeft = 8
                DesignTop = 8
              end
            end

            """);
        d.Settings.NoSnapping();
        var before = d.Text;
        d.Select("Timer1", "Btn2", "Panel1");
        d.PressHandle("Panel1", HandleKind.SE);
        d.MoveBy(12, 10);
        d.Release();
        Assert.Equal(new Rect(8, 50, 212, 110), d.Bounds("Panel1"));
        Assert.Equal(new Rect(250, 50, 87, 35), d.Bounds("Btn2"));
        Assert.Equal(before.Replace("Width = 200", "Width = 212").Replace("Height = 100", "Height = 110")
            .Replace("Width = 75", "Width = 87").Replace("Height = 25", "Height = 35"), d.Text);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-084")]
    [Trait("Req", "DSG-SUR-086")]
    public void AlignedControlOnlyFreeEdge()
    {
        using var h = DesignerHarness.FromText("""
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
              object Bottom1: TPanel
                Left = 0
                Top = 260
                Width = 400
                Height = 40
                Align = alBottom
              end
              object Client1: TPanel
                Left = 0
                Top = 40
                Width = 400
                Height = 220
                Align = alClient
              end
            end

            """);
        h.Settings.NoSnapping();
        var original = h.Text;

        // alTop: only S can be dragged; the alClient sibling re-flows and is stored in the same unit.
        h.Select("Top1");
        h.PressHandle("Top1", HandleKind.S);
        h.MoveBy(0, 10);
        Assert.Equal(new Rect(0, 50, 400, 210), h.Bounds("Client1")); // live re-flow
        h.Release();
        Assert.Equal(new Rect(0, 0, 400, 50), h.Bounds("Top1"));
        Assert.Equal(new Rect(0, 50, 400, 210), h.Bounds("Client1"));
        Assert.Equal(1, h.Undo.UndoCount);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // A corner handle drags only its free edge; handles of fixed edges only are inert.
        h.PressHandle("Top1", HandleKind.SE);
        h.MoveBy(10, 10);
        h.Release();
        Assert.Equal(new Rect(0, 0, 400, 50), h.Bounds("Top1"));
        h.ExecuteUndo();
        foreach (var inert in new[] { HandleKind.N, HandleKind.E, HandleKind.W, HandleKind.NE, HandleKind.NW })
        {
            h.PressHandle("Top1", inert);
            h.MoveBy(10, -10);
            Assert.NotEqual(InteractionState.Resizing, h.Controller.State);
            h.Release();
        }

        Assert.Equal(original, h.Text);
        Assert.Equal(0, h.Undo.UndoCount);

        // alBottom: only N; dragging it up grows the panel, the client panel shrinks.
        h.Select("Bottom1");
        h.PressHandle("Bottom1", HandleKind.N);
        h.MoveBy(0, -20);
        h.Release();
        Assert.Equal(new Rect(0, 240, 400, 60), h.Bounds("Bottom1"));
        Assert.Equal(new Rect(0, 40, 400, 200), h.Bounds("Client1"));
        h.ExecuteUndo();
        h.PressHandle("Bottom1", HandleKind.S);
        h.MoveBy(0, 20);
        Assert.NotEqual(InteractionState.Resizing, h.Controller.State);
        h.Release();

        // alClient: no edge can be dragged.
        h.Select("Client1");
        h.PressHandle("Client1", HandleKind.SE);
        h.MoveBy(-20, -20);
        Assert.NotEqual(InteractionState.Resizing, h.Controller.State);
        h.Release();
        Assert.Equal(original, h.Text);
        Assert.Equal(0, h.Undo.UndoCount);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-085")]
    [Trait("Req", "DSG-SUR-086")]
    public void FormResizeReflowsChildrenInOneUnit()
    {
        using var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 240
              object Top1: TPanel
                Left = 0
                Top = 0
                Width = 400
                Height = 30
                Align = alTop
              end
              object Btn: TButton
                Left = 232
                Top = 15
                Width = 75
                Height = 25
                Anchors = [akRight, akBottom]
              end
            end

            """);
        h.Settings.NoSnapping();
        var original = h.Text;
        Assert.Equal(["F"], h.Selected);

        h.PressHandle("F", HandleKind.SE);
        h.MoveBy(100, 60);
        Assert.Equal(InteractionState.Resizing, h.Controller.State);
        Assert.Equal(new Rect(0, 0, 500, 300), h.Bounds("F")); // live: client size and re-flow
        Assert.Equal(new Rect(332, 75, 75, 25), h.Bounds("Btn"));
        Assert.Equal(original, h.Text);
        h.Release();
        Assert.Equal(new Rect(0, 0, 500, 30), h.Bounds("Top1"));
        Assert.Equal(new Rect(332, 75, 75, 25), h.Bounds("Btn"));
        Assert.Contains("ClientWidth = 500", h.Text);
        Assert.Contains("ClientHeight = 300", h.Text);
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Equal("Undo_Designer_Resize", h.Undo.UndoTitleKey);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
        h.ExecuteRedo();
        Assert.Equal(new Rect(332, 75, 75, 25), h.Bounds("Btn"));
        h.ExecuteUndo();

        // E changes only the width, S only the height; minimum client size 16 × 16.
        h.PressHandle("F", HandleKind.E);
        h.MoveBy(-1000, 50);
        h.Release();
        Assert.Equal(new Rect(0, 0, 16, 240), h.Bounds("F"));
        h.ExecuteUndo();
        h.PressHandle("F", HandleKind.S);
        h.MoveBy(50, -1000);
        h.Release();
        Assert.Equal(new Rect(0, 0, 400, 16), h.Bounds("F"));
        h.ExecuteUndo();

        // Grid snapping on the client size (405 → 408, 243 → 240); Alt disables it.
        h.Settings.SnapToGrid = true;
        h.PressHandle("F", HandleKind.SE);
        h.MoveBy(5, 3);
        h.Release();
        Assert.Equal(new Rect(0, 0, 408, 240), h.Bounds("F"));
        h.ExecuteUndo();
        h.PressHandle("F", HandleKind.SE, InputModifiers.Alt);
        h.MoveBy(5, 3);
        h.Release();
        Assert.Equal(new Rect(0, 0, 405, 243), h.Bounds("F"));
        h.ExecuteUndo();

        // Escape cancels; the lock prevents it.
        h.PressHandle("F", HandleKind.SE);
        h.MoveBy(30, 30);
        Assert.True(h.Key(DesignKey.Escape));
        h.Release();
        h.Settings.LockControls = true;
        h.PressHandle("F", HandleKind.SE);
        h.MoveBy(30, 30);
        h.Release();
        Assert.Equal(original, h.Text);
        Assert.Equal(0, h.Undo.UndoCount);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-086")]
    public void ContainerResizeReflowsAnchoredChildren()
    {
        using var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object Panel1: TPanel
                Left = 8
                Top = 8
                Width = 200
                Height = 100
                object Inner: TButton
                  Left = 100
                  Top = 50
                  Width = 75
                  Height = 25
                  Anchors = [akRight, akBottom]
                end
              end
            end

            """);
        h.Settings.NoSnapping();
        var original = h.Text;
        h.Select("Panel1");
        h.PressHandle("Panel1", HandleKind.SE);
        h.MoveBy(20, 10);
        Assert.Equal(new Rect(120, 60, 75, 25), h.Bounds("Inner")); // live
        h.Release();
        Assert.Equal(new Rect(8, 8, 220, 110), h.Bounds("Panel1"));
        Assert.Equal(new Rect(120, 60, 75, 25), h.Bounds("Inner"));
        Assert.Equal(1, h.Undo.UndoCount);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-205")]
    public void DraggedEdgeUsesDeclaredSpacing()
    {
        // A (75 × 25 at 100, 100) stores Margins.Left = 7; B (50 × 25 at 20, 160) is resized.
        using var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object A: TButton
                Left = 100
                Top = 100
                Width = 75
                Height = 25
                Margins.Left = 7
              end
              object B: TButton
                Left = 20
                Top = 160
                Width = 50
                Height = 25
                Margins.Top = 5
              end
            end

            """);
        h.Settings.SnapToGrid = false;
        h.Select("B");

        // The dragged right edge faces A's left edge: 3 (B's default) + 7 = 10 → 90.
        h.PressHandle("B", HandleKind.E);
        h.MoveBy(18, 0); // right 88
        Assert.Equal(new Rect(20, 160, 70, 25), h.Bounds("B"));
        var line = Assert.Single(h.Guidelines);
        Assert.Equal(new Guideline(SnapAxis.X, GuidelineKind.Margin, 90, h.Object("F"), h.Object("A")), line);
        Assert.Equal(10, line.Spacing);
        Assert.True(h.Key(DesignKey.Escape));

        // The dragged top edge faces A's bottom edge (125): 5 (declared on B) + 3 = 8 → 133; the left edge is not
        // dragged and gives no candidate although it stands 12 from the parent's edge.
        h.PressHandle("B", HandleKind.N);
        h.MoveBy(0, -25); // top 135
        Assert.Equal(new Rect(20, 133, 50, 52), h.Bounds("B"));
        line = Assert.Single(h.Guidelines);
        Assert.Equal((SnapAxis.Y, GuidelineKind.Margin, 133, 8), (line.Axis, line.Kind, line.Coordinate, line.Spacing));
        h.Release();
        Assert.Equal(new Rect(20, 133, 50, 52), h.Bounds("B"));
    }
}
