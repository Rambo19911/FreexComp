using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Move drags on the design surface (design-surface.md §4, §5, §12; D-038). No snapping yet (D-039).</summary>
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

    private static (int X, int Y) Tray(DesignerHarness h, string name) =>
        (PropertyAccess.GetInt(h.Object(name), "DesignLeft"), PropertyAccess.GetInt(h.Object(name), "DesignTop"));
}
