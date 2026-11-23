using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Bring to front / send to back (design-surface.md §11.3; D-052).</summary>
public sealed class ZOrderTests
{
    private const string Form = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object A: TPanel
            Left = 8
            Top = 8
            Width = 185
            Height = 41
            TabOrder = 0
          end
          object B: TButton
            Left = 8
            Top = 64
            Width = 75
            Height = 25
            TabOrder = 1
          end
          object C: TButton
            Left = 8
            Top = 104
            Width = 75
            Height = 25
            TabOrder = 2
          end
          object Timer1: TTimer
            DesignLeft = 8
            DesignTop = 8
          end
        end

        """;

    // Two alTop panels with equal sort keys (both stored at Top = 0): creation order decides the docking (DSG-SUR-152).
    private const string Docked = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object T1: TPanel
            Left = 0
            Top = 0
            Width = 400
            Height = 40
            Align = alTop
            TabOrder = 0
          end
          object T2: TPanel
            Left = 0
            Top = 0
            Width = 400
            Height = 30
            Align = alTop
            TabOrder = 1
          end
        end

        """;

    private static int TabOrder(DesignerHarness h, string name) => PropertyAccess.GetInt(h.Object(name), "TabOrder");

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-150")]
    [Trait("Req", "DSG-SUR-151")]
    [Trait("Req", "DSG-SUR-152")]
    public void BringToFrontSendToBack()
    {
        using var h = DesignerHarness.FromText(Form);
        var original = h.Text;

        // Not for the form alone or a tray item.
        Assert.False(h.Controller.CanExecute("designer.bringToFront"));
        Assert.False(h.Controller.CanExecute("designer.sendToBack"));
        h.Select("Timer1");
        Assert.False(h.Controller.CanExecute("designer.bringToFront"));

        // Bring to front: the selected ones go to the end of their parent's children, keeping their relative order.
        h.Select("B", "A");
        Assert.True(h.Controller.CanExecute("designer.bringToFront"));
        h.Execute("designer.bringToFront");
        Assert.Equal(["C", "Timer1", "A", "B"], h.ChildNames("F"));
        Assert.Equal((0, 1, 2), (TabOrder(h, "A"), TabOrder(h, "B"), TabOrder(h, "C"))); // DSG-SUR-151
        Assert.Equal(["B", "A"], h.Selected);
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Equal("Undo_Designer_Order", h.Undo.UndoTitleKey);
        Assert.False(h.Controller.CanExecute("designer.bringToFront")); // already there
        Assert.True(h.Controller.CanExecute("designer.sendToBack"));
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
        Assert.Equal(["A", "B", "C", "Timer1"], h.ChildNames("F"));

        // Send to back: to the beginning, keeping the relative order.
        h.Select("C", "B");
        h.Execute("designer.sendToBack");
        Assert.Equal(["B", "C", "A", "Timer1"], h.ChildNames("F"));
        Assert.False(h.Controller.CanExecute("designer.sendToBack"));
        h.ExecuteUndo();
        h.Select("A");
        Assert.False(h.Controller.CanExecute("designer.sendToBack"));

        // Ctrl+Shift+Up / Down; allowed while locked.
        h.Settings.LockControls = true;
        h.Select("A");
        Assert.True(h.Key(DesignKey.Up, InputModifiers.Ctrl | InputModifiers.Shift));
        Assert.Equal(["B", "C", "Timer1", "A"], h.ChildNames("F"));
        Assert.True(h.Key(DesignKey.Down, InputModifiers.Ctrl | InputModifiers.Shift));
        Assert.Equal(["A", "B", "C", "Timer1"], h.ChildNames("F"));
        Assert.Equal(new Rect(8, 8, 185, 41), h.Bounds("A")); // z-order never moves a control by itself
        h.ExecuteUndo();
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // Docking: equal sort keys tie in creation order, so the z-order change re-flows and stores in the same unit.
        using var d = DesignerHarness.FromText(Docked);
        Assert.Equal(new Rect(0, 40, 400, 30), d.Bounds("T2"));
        d.Select("T2");
        d.Execute("designer.sendToBack");
        Assert.Equal(["T2", "T1"], d.ChildNames("F"));
        Assert.Equal(new Rect(0, 0, 400, 30), d.Bounds("T2"));
        Assert.Equal(new Rect(0, 30, 400, 40), d.Bounds("T1"));
        Assert.Equal(30, PropertyAccess.GetInt(d.Object("T1"), "Top"));
        Assert.Equal(1, d.Undo.UndoCount);
        d.AssertInvariant();
        d.ExecuteUndo();
        Assert.Equal(["T1", "T2"], d.ChildNames("F"));
        Assert.Equal(0, PropertyAccess.GetInt(d.Object("T1"), "Top"));
        d.SkipFinalChecks = true; // the loaded form violates the invariant (DSG-LAY-041) and undo restores it
    }
}
