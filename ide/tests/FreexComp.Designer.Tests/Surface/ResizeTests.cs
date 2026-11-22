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
}
