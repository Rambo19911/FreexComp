using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Move drags on the design surface (design-surface.md §4, §5, §6, §12; D-038, D-039, D-041).</summary>
public sealed class MoveTests
{
    private const string Hello = "forms/canonical/hello.form"; // NameEdit (16, 16, 200, 23), GreetButton (232, 15, 75, 25)

    // Bar is docked (alTop); Btn1 is a child of Panel1; two tray items.
    private const string Docked = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object Bar: TPanel
            Left = 0
            Top = 0
            Width = 400
            Height = 41
            Align = alTop
          end
          object Panel1: TPanel
            Left = 8
            Top = 50
            Width = 200
            Height = 100
            object Btn1: TButton
              Left = 10
              Top = 10
              Width = 75
              Height = 25
            end
          end
          object Timer1: TTimer
            DesignLeft = 8
            DesignTop = 8
          end
          object Dialog1: TOpenDialog
            DesignLeft = 88
            DesignTop = 8
          end
        end

        """;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-040")]
    public void DragBelowThresholdDoesNothing()
    {
        using var h = DesignerHarness.FromFile(Hello);
        h.Settings.NoSnapping(); // raw deltas (snapping: DragSnapsToGrid, SnapTests)
        var original = h.Text;

        h.Press("GreetButton", at: (10, 10));
        h.MoveBy(3, 0);
        h.MoveBy(0, 3);
        h.MoveBy(-6, 0); // (-3, 3) from the down position: below 4 on both axes
        Assert.Equal(InteractionState.PressPending, h.Controller.State);
        Assert.Equal((232, 15), h.Position("GreetButton"));
        h.Release();
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        Assert.Equal((232, 15), h.Position("GreetButton"));
        Assert.Equal(0, h.Undo.UndoCount);
        Assert.Equal(original, h.Text);
        Assert.Equal(["GreetButton"], h.Selected);

        // Exactly the threshold on one axis starts the drag.
        h.Press("GreetButton");
        h.MoveBy(0, -4);
        Assert.Equal(InteractionState.Moving, h.Controller.State);
        Assert.Equal((232, 11), h.Position("GreetButton"));
        h.Release();
        Assert.Equal((232, 11), h.Position("GreetButton"));
        Assert.Equal(1, h.Undo.UndoCount);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-050")]
    [Trait("Req", "DSG-SUR-053")]
    [Trait("Req", "DSG-SUR-054")]
    [Trait("Req", "DSG-SUR-024")]
    [Trait("Req", "DSG-SUR-170")]
    public void MultiSelectionMovesByAnchorDelta()
    {
        using var h = DesignerHarness.FromFile(Hello);
        h.Settings.NoSnapping(); // raw deltas (snapping: DragSnapsToGrid, SnapTests)
        var original = h.Text;
        h.Select("NameEdit", "GreetButton");

        h.Press("NameEdit"); // selected: the multi-selection stays
        h.MoveBy(10, 20);
        Assert.Equal(InteractionState.Moving, h.Controller.State);
        Assert.Equal((26, 36), h.Position("NameEdit")); // live preview ...
        Assert.Equal((242, 35), h.Position("GreetButton"));
        Assert.Equal(original, h.Text); // ... the model is unchanged until the drop
        h.Release();
        Assert.Equal((26, 36), h.Position("NameEdit"));
        Assert.Equal((242, 35), h.Position("GreetButton"));
        Assert.Equal(["NameEdit", "GreetButton"], h.Selected);
        Assert.Equal(1, h.Undo.UndoCount); // one unit per drag
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
        h.ExecuteRedo();
        Assert.Equal((26, 36), h.Position("NameEdit"));
        Assert.Equal((242, 35), h.Position("GreetButton"));
        Assert.True(h.Controller.CanExecute("edit.undo"));

        // No clamping: negative coordinates are fine.
        h.Select("NameEdit");
        h.Press("NameEdit");
        h.MoveBy(-50, -60);
        h.Release();
        Assert.Equal((-24, -24), h.Position("NameEdit"));

        using var d = DesignerHarness.FromText(Docked);
        d.Settings.NoSnapping();
        // The docked Bar is excluded, the covered Btn1 moves with its parent only.
        d.Select("Bar", "Btn1", "Panel1");
        d.Press("Panel1", at: (150, 80));
        d.MoveBy(5, 6);
        d.Release();
        Assert.Equal((13, 56), d.Position("Panel1"));
        Assert.Equal((10, 10), d.Position("Btn1"));
        Assert.Equal((0, 0), d.Position("Bar"));
        Assert.Equal(1, d.Undo.UndoCount);

        // A drag that starts on a tray item moves only the tray items (DesignLeft/DesignTop, not below 0).
        d.Select("Panel1", "Timer1", "Dialog1");
        d.Press("Timer1");
        d.MoveBy(-20, 5);
        d.Release();
        Assert.Equal((0, 13), Tray(d, "Timer1"));
        Assert.Equal((68, 13), Tray(d, "Dialog1"));
        Assert.Equal((13, 56), d.Position("Panel1"));
        Assert.Equal(2, d.Undo.UndoCount);
        d.ExecuteUndo();
        Assert.Equal((8, 8), Tray(d, "Timer1"));
        Assert.Equal(["Panel1", "Timer1", "Dialog1"], d.Selected);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-058")]
    [Trait("Req", "DSG-SUR-059")]
    [Trait("Req", "DSG-SUR-171")]
    public void EscapeCancelsDrag()
    {
        using var h = DesignerHarness.FromFile(Hello);
        h.Settings.NoSnapping(); // raw deltas (snapping: DragSnapsToGrid, SnapTests)
        var original = h.Text;

        h.Press("GreetButton");
        h.MoveBy(20, 20);
        Assert.Equal((252, 35), h.Position("GreetButton"));
        Assert.True(h.Key(DesignKey.Escape));
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        Assert.Equal((232, 15), h.Position("GreetButton"));
        h.Release();
        Assert.Equal((232, 15), h.Position("GreetButton"));
        Assert.Equal(["GreetButton"], h.Selected); // Escape cancelled the drag, it did not select the parent

        h.Press("GreetButton");
        h.MoveBy(20, 20);
        h.CaptureLost();
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        Assert.Equal((232, 15), h.Position("GreetButton"));

        // A drop with a zero delta records nothing.
        h.Press("GreetButton");
        h.MoveBy(10, 0);
        h.MoveBy(-10, 0);
        h.Release();
        Assert.Equal(0, h.Undo.UndoCount);
        Assert.Equal(original, h.Text);

        // Undo is disabled during a drag.
        h.Press("GreetButton");
        h.MoveBy(8, 0);
        h.Release();
        h.Press("GreetButton");
        h.MoveBy(8, 0);
        Assert.False(h.Controller.CanExecute("edit.undo"));
        h.ExecuteUndo();
        Assert.Equal((248, 15), h.Position("GreetButton"));
        h.Key(DesignKey.Escape);
        Assert.Equal((240, 15), h.Position("GreetButton"));
        Assert.Equal(1, h.Undo.UndoCount);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-052")]
    public void ShiftConstrainsAxis()
    {
        using var h = DesignerHarness.FromFile(Hello);
        h.Settings.NoSnapping(); // raw deltas (snapping: DragSnapsToGrid, SnapTests)
        var original = h.Text;

        h.Press("GreetButton");
        h.MoveBy(10, 3, InputModifiers.Shift); // |dy| < |dx|: dy forced to 0
        Assert.Equal((242, 15), h.Position("GreetButton"));
        h.MoveBy(0, 17, InputModifiers.Shift); // (10, 20): now dx is forced to 0
        Assert.Equal((232, 35), h.Position("GreetButton"));
        h.MoveBy(0, 0, InputModifiers.None); // re-evaluated on every move event
        Assert.Equal((242, 35), h.Position("GreetButton"));
        h.Release(InputModifiers.Shift);
        Assert.Equal((232, 35), h.Position("GreetButton"));
        Assert.Equal(1, h.Undo.UndoCount);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-076")]
    [Trait("Req", "DSG-SUR-079")]
    public void DragSnapsToGrid()
    {
        using var h = DesignerHarness.FromFile(Hello);
        h.Settings.SnapToGrid = true;
        h.Settings.UseGuidelines = false;

        h.Press("GreetButton", at: (10, 10));
        h.MoveBy(13, 6); // raw (245, 21)
        Assert.Equal((248, 24), h.Position("GreetButton")); // R's left/top to the nearest multiple of 8 (live)
        h.Release();
        Assert.Equal((248, 24), h.Position("GreetButton"));
        Assert.Equal((75, 25), h.Size("GreetButton")); // DSG-SUR-078: the size never changes
        Assert.Equal(1, h.Undo.UndoCount);
        h.ExecuteUndo();
        Assert.Equal((232, 15), h.Position("GreetButton"));

        // Halves round up; other grid sizes; negative values floor toward minus infinity.
        h.Settings.GridSizeX = 10;
        h.Settings.GridSizeY = 4;
        h.Press("GreetButton");
        h.MoveBy(3, -16); // raw (235, -1): 235 → 240 (half up), -1 → 0
        h.Release();
        Assert.Equal((240, 0), h.Position("GreetButton"));
        h.Press("GreetButton");
        h.MoveBy(-247, -7); // raw (-7, -7): -7 → -10, -7 → -8
        h.Release();
        Assert.Equal((-10, -8), h.Position("GreetButton"));

        // Tray items snap to the grid in tray coordinates.
        using var d = DesignerHarness.FromText(Docked);
        d.Settings.UseGuidelines = false;
        d.Select("Timer1");
        d.Press("Timer1");
        d.MoveBy(13, 6); // raw (21, 14)
        d.Release();
        Assert.Equal((24, 16), Tray(d, "Timer1"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-070")]
    public void AltDisablesSnapping()
    {
        using var h = DesignerHarness.FromFile(Hello); // grid and guidelines on (defaults)

        h.Press("GreetButton", at: (10, 10)); // Alt on the press would pick the control behind (DSG-SUR-012)
        h.MoveBy(13, 6, InputModifiers.Alt);
        Assert.Equal((245, 21), h.Position("GreetButton")); // raw: no grid (no guideline is within reach here either)
        Assert.Empty(h.Controller.Guidelines);
        h.MoveBy(0, 0, InputModifiers.None); // evaluated on every move event
        Assert.Equal((248, 24), h.Position("GreetButton"));
        h.MoveBy(0, 0, InputModifiers.Alt);
        Assert.Equal((245, 21), h.Position("GreetButton"));
        h.Release(InputModifiers.Alt); // the state at the release counts
        Assert.Equal((245, 21), h.Position("GreetButton"));
        Assert.Equal(1, h.Undo.UndoCount);
    }

    // Panel1 accepts children; Btn and Edit1 are direct children of the form.
    private const string Containers = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object Panel1: TPanel
            Left = 200
            Top = 20
            Width = 180
            Height = 150
            object Inner: TButton
              Left = 10
              Top = 10
              Width = 75
              Height = 25
            end
          end
          object Btn: TButton
            Left = 20
            Top = 20
            Width = 75
            Height = 25
          end
          object Edit1: TEdit
            Left = 20
            Top = 60
            Width = 100
            Height = 23
          end
        end

        """;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-055")]
    [Trait("Req", "DSG-SUR-056")]
    public void DragIntoContainerReparents()
    {
        using var h = DesignerHarness.FromText(Containers);
        h.Settings.NoSnapping();
        var original = h.Text;
        var panel = h.Controller.Geometry.GetClientRect(h.Object("Panel1")); // surface client origin of Panel1
        var form = h.Controller.Geometry.FormClientRect;

        // Into the panel: the drop-target highlight shows while dragging, the drop re-parents (appended = top of z-order)
        // and converts Left/Top so the control stays where it was dropped.
        h.Press("Btn");
        h.MoveBy(220, 30);
        Assert.Same(h.Object("Panel1"), h.Controller.DropTarget);
        h.Release();
        Assert.Null(h.Controller.DropTarget);
        Assert.Equal("Panel1", h.Parent("Btn"));
        Assert.Equal(["Inner", "Btn"], h.ChildNames("Panel1"));
        Assert.Equal((form.Left + 240 - panel.Left, form.Top + 50 - panel.Top), h.Position("Btn"));
        Assert.Equal(["Btn"], h.Selected);
        Assert.Equal(1, h.Undo.UndoCount);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text); // parent, index and position restored
        h.ExecuteRedo();
        Assert.Equal("Panel1", h.Parent("Btn"));
        h.ExecuteUndo();

        // Out of the panel onto the form: the form root as target draws no highlight.
        h.Press("Inner");
        h.MoveBy(-200, 150);
        Assert.Null(h.Controller.DropTarget);
        h.Release();
        Assert.Equal("F", h.Parent("Inner"));
        Assert.Equal(["Panel1", "Btn", "Edit1", "Inner"], h.ChildNames("F"));
        Assert.Equal((panel.Left + 10 - 200 - form.Left, panel.Top + 10 + 150 - form.Top), h.Position("Inner"));
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // A multi-selection follows the anchor's target and keeps its relative creation order.
        h.Select("Edit1", "Btn");
        h.Press("Btn");
        h.MoveBy(220, 30);
        h.Release();
        Assert.Equal(["Inner", "Btn", "Edit1"], h.ChildNames("Panel1"));
        Assert.Equal((form.Left + 240 - panel.Left, form.Top + 90 - panel.Top), h.Position("Edit1"));
        Assert.Equal(1, h.Undo.UndoCount);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // A container never becomes its own target; outside the form client area the target is the current parent.
        h.Select("Panel1");
        h.Press("Panel1", at: (150, 100));
        h.MoveBy(10, 10);
        Assert.Null(h.Controller.DropTarget);
        h.Release();
        Assert.Equal("F", h.Parent("Panel1"));
        h.ExecuteUndo();
        h.Press("Inner");
        h.MoveTo((form.Right + 30, form.Top + 40)); // the backdrop
        Assert.Null(h.Controller.DropTarget);
        h.Release();
        Assert.Equal("Panel1", h.Parent("Inner"));
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // Snapping uses the target's grid origin and siblings: Btn's left edge snaps to Inner's (10) inside the panel.
        h.Settings.UseGuidelines = true;
        var btn = h.SurfacePoint("Btn");
        h.Press("Btn");
        h.MoveTo((btn.X + panel.Left + 12 - (form.Left + 20), btn.Y + panel.Top + 90 - (form.Top + 20)));
        Assert.Contains(h.Guidelines, g => g.Parent == h.Object("Panel1") && g.Reference == h.Object("Inner") && g.Axis == SnapAxis.X);
        h.Release();
        Assert.Equal((10, 90), h.Position("Btn"));
        h.ExecuteUndo();
        h.Settings.NoSnapping();
        h.Settings.SnapToGrid = true;
        h.Press("Btn");
        h.MoveTo((btn.X + panel.Left + 13 - (form.Left + 20), btn.Y + panel.Top + 91 - (form.Top + 20)));
        h.Release();
        Assert.Equal((16, 88), h.Position("Btn")); // the grid of the panel's client area
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-050")]
    public void AlignedControlCannotMove()
    {
        using var d = DesignerHarness.FromText(Docked);
        d.Settings.NoSnapping();
        var original = d.Text;

        d.Press("Bar", at: (100, 20));
        d.MoveBy(10, 10);
        Assert.NotEqual(InteractionState.Moving, d.Controller.State);
        var status = Assert.Single(d.Host.Statuses);
        Assert.Equal("Msg.Designer.AlignedCannotMove", status.Id);
        Assert.Equal(["Bar"], status.Args);
        Assert.Equal((0, 0), d.Position("Bar"));
        d.Release();
        Assert.Equal(["Bar"], d.Selected); // the press selected it; a drag never applies the click rule
        Assert.Equal(0, d.Undo.UndoCount);
        Assert.Equal(original, d.Text);

        // A drag starting on a movable control moves it and silently leaves the aligned one in place.
        d.Select("Bar", "Panel1");
        d.Press("Panel1", at: (150, 80));
        d.MoveBy(5, 6);
        d.Release();
        Assert.Equal((13, 56), d.Position("Panel1"));
        Assert.Equal((0, 0), d.Position("Bar"));
        Assert.Single(d.Host.Statuses);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-051")]
    [Trait("Req", "DSG-SUR-087")]
    public void LockPreventsMoveAndResize()
    {
        using var h = DesignerHarness.FromFile(Hello);
        h.Settings.NoSnapping();
        h.Settings.LockControls = true;
        var original = h.Text;

        // Move: nothing moves, the status says why; selection behaves as usual.
        h.Press("GreetButton");
        Assert.Equal(["GreetButton"], h.Selected);
        h.MoveBy(20, 20);
        Assert.NotEqual(InteractionState.Moving, h.Controller.State);
        Assert.Equal((232, 15), h.Position("GreetButton"));
        h.Release();
        Assert.Equal("Msg.Designer.Locked", Assert.Single(h.Host.Statuses).Id);

        // Resize: the handle drag is ignored too.
        h.PressHandle("GreetButton", HandleKind.SE);
        h.MoveBy(20, 20);
        Assert.NotEqual(InteractionState.Resizing, h.Controller.State);
        h.Release();
        Assert.Equal(new Rect(232, 15, 75, 25), h.Bounds("GreetButton"));
        Assert.Equal(2, h.Host.Statuses.Count);
        Assert.Equal("Msg.Designer.Locked", h.Host.Statuses[1].Id);
        Assert.Equal(0, h.Undo.UndoCount);
        Assert.Equal(original, h.Text);

        // The lasso still selects.
        var client = h.Controller.Geometry.FormClientRect;
        h.PressAt((client.Left + 4, client.Top + 4));
        h.MoveBy(300, 40);
        h.Release();
        Assert.Equal(["NameEdit", "GreetButton"], h.Selected);

        // Unlocked again: the same drag moves.
        h.Settings.LockControls = false;
        h.Press("GreetButton");
        h.MoveBy(20, 20);
        h.Release();
        Assert.Equal((252, 35), h.Position("GreetButton"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-057")]
    [Trait("Req", "DSG-SUR-123")]
    [Trait("Req", "DSG-SUR-124")]
    public void CtrlAtDropCopies()
    {
        using var h = DesignerHarness.FromText(Docked.Replace("      Height = 25\n", "      Height = 25\n      OnClick = Btn1Click\n"));
        h.Settings.NoSnapping();
        h.Host.CodeSync.Methods["Btn1Click"] = "TNotifyEvent";
        var original = h.Text;

        // Ctrl held at the release: the original stays, a clone with the next free name is created where it was dropped.
        h.Press("Btn1");
        h.MoveBy(20, 30);
        h.Release(InputModifiers.Ctrl);
        Assert.Equal((10, 10), h.Position("Btn1"));
        Assert.Equal("Panel1", h.Parent("Btn2"));
        Assert.Equal((30, 40), h.Position("Btn2"));
        Assert.Equal((75, 25), h.Size("Btn2"));
        Assert.Equal(new IdentValue("Btn1Click"), h.Object("Btn2").FindProperty("OnClick")!.Value); // compatible handler kept
        Assert.Equal(["Btn1", "Btn2"], h.ChildNames("Panel1"));
        Assert.Equal(["Btn2"], h.Selected);
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Equal(UndoTitles.Copy, h.Undo.UndoTitleKey);
        Assert.Null(h.Host.Clipboard.GetData(ClipboardFormats.Text)); // the clipboard is not touched
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
        Assert.Equal(["Btn1"], h.Selected);
        h.ExecuteRedo();
        Assert.Equal((30, 40), h.Position("Btn2"));
        h.ExecuteUndo();

        // Ctrl pressed only during the drag: an ordinary move.
        h.Select("Btn1");
        h.Press("Btn1");
        h.MoveBy(10, 0, InputModifiers.Ctrl);
        h.MoveBy(10, 0, InputModifiers.None);
        h.Release(InputModifiers.None);
        Assert.Equal((30, 10), h.Position("Btn1"));
        Assert.Null(h.Document.FindByName("Btn2"));
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // Dropped on another container: the clone goes into the drop target; the original keeps its parent.
        var panel = h.Controller.Geometry.GetClientRect(h.Object("Panel1"));
        var form = h.Controller.Geometry.FormClientRect;
        h.Press("Btn1");
        h.MoveBy(250, 120);
        h.Release(InputModifiers.Ctrl);
        Assert.Equal("Panel1", h.Parent("Btn1"));
        Assert.Equal((10, 10), h.Position("Btn1"));
        Assert.Equal("F", h.Parent("Btn2"));
        Assert.Equal((panel.Left + 10 + 250 - form.Left, panel.Top + 10 + 120 - form.Top), h.Position("Btn2"));
        Assert.Equal(["Bar", "Panel1", "Timer1", "Dialog1", "Btn2"], h.ChildNames("F"));
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // A copied container brings copies of its children; tray items are copied within the tray.
        h.Select("Panel1");
        h.Press("Panel1", at: (150, 60));
        h.MoveBy(0, 120);
        h.Release(InputModifiers.Ctrl);
        Assert.Equal(["Panel2"], h.Selected);
        Assert.Equal((8, 170), h.Position("Panel2"));
        Assert.Equal(["Btn2"], h.ChildNames("Panel2"));
        Assert.Equal((10, 10), h.Position("Btn2"));
        h.Select("Timer1");
        h.Press("Timer1");
        h.MoveBy(0, 64);
        h.Release(InputModifiers.Ctrl);
        Assert.Equal(["Timer2"], h.Selected);
        Assert.Equal((8, 8), Tray(h, "Timer1"));
        Assert.Equal((8, 72), Tray(h, "Timer2"));
    }

    private static (int X, int Y) Tray(DesignerHarness h, string name) =>
        (PropertyAccess.GetInt(h.Object(name), "DesignLeft"), PropertyAccess.GetInt(h.Object(name), "DesignTop"));
}
