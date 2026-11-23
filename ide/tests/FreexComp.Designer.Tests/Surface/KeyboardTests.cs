using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Keyboard nudging, resizing and the typing hook of the design surface (design-surface.md §9; D-044).</summary>
public sealed class KeyboardTests
{
    private const string Hello = "forms/canonical/hello.form"; // NameEdit (16, 16, 200, 23), GreetButton (232, 15, 75, 25)

    private const string Panels = """
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
          object Panel1: TPanel
            Left = 20
            Top = 60
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
          object Timer1: TTimer
            DesignLeft = 3
            DesignTop = 0
          end
        end

        """;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-100")]
    [Trait("Req", "DSG-SUR-101")]
    [Trait("Req", "DSG-SUR-106")]
    [Trait("Req", "DSG-SUR-108")]
    [Trait("Req", "DSG-SUR-109")]
    public void ArrowsNudgeByGridCtrlByPixel()
    {
        using var h = DesignerHarness.FromFile(Hello);
        var original = h.Text;
        h.Select("NameEdit", "GreetButton"); // the primary (GreetButton) is the anchor

        // Grid on: the anchor goes to the next grid multiple in that direction; the others move by the same delta.
        Assert.True(h.Key(DesignKey.Right));
        Assert.Equal((240, 15), h.Position("GreetButton"));
        Assert.Equal((24, 16), h.Position("NameEdit"));
        Assert.True(h.Key(DesignKey.Right)); // already on the grid: + grid size
        Assert.Equal((248, 15), h.Position("GreetButton"));
        Assert.True(h.Key(DesignKey.Up));
        Assert.Equal((248, 8), h.Position("GreetButton"));
        Assert.Equal((32, 9), h.Position("NameEdit"));
        Assert.True(h.Key(DesignKey.Down));
        Assert.True(h.Key(DesignKey.Left));
        Assert.Equal((240, 16), h.Position("GreetButton"));

        // Ctrl: exactly 1 px; grid off: 1 px.
        Assert.True(h.Key(DesignKey.Left, InputModifiers.Ctrl));
        Assert.Equal((239, 16), h.Position("GreetButton"));
        h.Settings.SnapToGrid = false;
        Assert.True(h.Key(DesignKey.Down));
        Assert.Equal((239, 17), h.Position("GreetButton"));
        Assert.Equal((23, 18), h.Position("NameEdit"));
        Assert.Equal(["NameEdit", "GreetButton"], h.Selected);
        while (h.Undo.CanUndo)
        {
            h.ExecuteUndo();
        }

        Assert.Equal(original, h.Text);

        // Locked: nothing moves, status Locked.
        h.Settings.LockControls = true;
        Assert.True(h.Key(DesignKey.Right));
        Assert.Equal(("Msg.Designer.Locked", 0), (h.Host.Statuses[^1].Id, h.Undo.UndoCount));
        Assert.Equal(original, h.Text);

        // The form as the sole selection: plain and Ctrl arrows do nothing.
        h.Settings.LockControls = false;
        h.Select("MainForm");
        h.Key(DesignKey.Right);
        h.Key(DesignKey.Down, InputModifiers.Ctrl);
        Assert.Equal(original, h.Text);

        // Aligned controls never move; a re-parenting never happens; anchored children of a moved container stay.
        using var p = DesignerHarness.FromText(Panels);
        p.Select("Top1");
        p.Key(DesignKey.Down);
        Assert.Equal(0, p.Undo.UndoCount);
        p.Select("Top1", "Panel1");
        Assert.True(p.Key(DesignKey.Up));
        Assert.Equal((20, 56), p.Position("Panel1"));
        Assert.Equal((0, 0), p.Position("Top1"));
        Assert.Equal((100, 50), p.Position("Inner"));
        Assert.Equal("F", p.Parent("Panel1"));

        // Tray items move inside the tray, clamped at 0.
        p.Select("Timer1");
        Assert.True(p.Key(DesignKey.Left));
        Assert.Contains("DesignLeft = 0", p.Text);
        p.Key(DesignKey.Up, InputModifiers.Ctrl);
        Assert.Contains("DesignTop = 0", p.Text);
        Assert.True(p.Key(DesignKey.Right));
        Assert.Contains("DesignLeft = 8", p.Text);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-102")]
    [Trait("Req", "DSG-SUR-103")]
    [Trait("Req", "DSG-SUR-106")]
    [Trait("Req", "DSG-SUR-108")]
    public void ShiftArrowsResize()
    {
        using var h = DesignerHarness.FromFile(Hello);
        var original = h.Text;
        h.Select("GreetButton");

        // Grid on: the right / bottom edge goes to the next grid multiple in that direction (right 307 → 312, bottom 40 → 48).
        Assert.True(h.Key(DesignKey.Right, InputModifiers.Shift));
        Assert.Equal(new Rect(232, 15, 80, 25), h.Bounds("GreetButton"));
        Assert.True(h.Key(DesignKey.Down, InputModifiers.Shift));
        Assert.Equal(new Rect(232, 15, 80, 33), h.Bounds("GreetButton"));
        Assert.True(h.Key(DesignKey.Up, InputModifiers.Shift));
        Assert.Equal(new Rect(232, 15, 80, 25), h.Bounds("GreetButton"));
        Assert.True(h.Key(DesignKey.Left, InputModifiers.Shift));
        Assert.Equal(new Rect(232, 15, 72, 25), h.Bounds("GreetButton"));

        // Shift+Alt: exactly 1 px; never below 1 × 1.
        Assert.True(h.Key(DesignKey.Left, InputModifiers.Shift | InputModifiers.Alt));
        Assert.Equal(new Rect(232, 15, 71, 25), h.Bounds("GreetButton"));
        while (h.Undo.CanUndo)
        {
            h.ExecuteUndo();
        }

        Assert.Equal(original, h.Text);
        h.Settings.SnapToGrid = false;
        for (var i = 0; i < 80; i++)
        {
            h.Key(DesignKey.Left, InputModifiers.Shift);
        }

        Assert.Equal(new Rect(232, 15, 1, 25), h.Bounds("GreetButton"));

        // Aligned edges stay fixed; a resized container re-flows its anchored children; the form resizes its client area.
        using var p = DesignerHarness.FromText(Panels);
        p.Select("Top1");
        p.Key(DesignKey.Right, InputModifiers.Shift);
        Assert.Equal(0, p.Undo.UndoCount);
        Assert.True(p.Key(DesignKey.Down, InputModifiers.Shift));
        Assert.Equal(new Rect(0, 0, 400, 48), p.Bounds("Top1"));
        p.Select("Panel1");
        Assert.True(p.Key(DesignKey.Right, InputModifiers.Shift)); // right 220 → 224
        Assert.Equal(new Rect(20, 60, 204, 100), p.Bounds("Panel1"));
        Assert.Equal(new Rect(104, 50, 75, 25), p.Bounds("Inner"));
        p.Select("F");
        Assert.True(p.Key(DesignKey.Down, InputModifiers.Shift | InputModifiers.Alt));
        Assert.Equal(new Rect(0, 0, 400, 301), p.Bounds("F"));
        Assert.True(p.Key(DesignKey.Right, InputModifiers.Shift));
        Assert.Equal(new Rect(0, 0, 408, 301), p.Bounds("F"));
        Assert.Equal(new Rect(0, 0, 408, 48), p.Bounds("Top1"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-107")]
    public void ConsecutiveNudgesMergeIntoOneUndoUnit()
    {
        using var h = DesignerHarness.FromFile(Hello);
        var original = h.Text;
        h.Settings.SnapToGrid = false;
        h.Select("GreetButton");

        // Within 1 s of each other: one unit.
        h.Key(DesignKey.Right);
        h.Time.Advance(TimeSpan.FromMilliseconds(600));
        h.Key(DesignKey.Right);
        h.Time.Advance(TimeSpan.FromMilliseconds(1000));
        h.Key(DesignKey.Down);
        Assert.Equal((234, 16), h.Position("GreetButton"));
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Equal("Undo_Designer_Move", h.Undo.UndoTitleKey);

        // More than 1 s later: a new unit.
        h.Time.Advance(TimeSpan.FromMilliseconds(1001));
        h.Key(DesignKey.Right);
        Assert.Equal(2, h.Undo.UndoCount);

        // A different kind (resize) or a different selection: a new unit.
        h.Key(DesignKey.Right, InputModifiers.Shift);
        Assert.Equal(3, h.Undo.UndoCount);
        Assert.Equal("Undo_Designer_Resize", h.Undo.UndoTitleKey);
        h.Key(DesignKey.Right, InputModifiers.Shift);
        Assert.Equal(3, h.Undo.UndoCount);
        h.Select("NameEdit", "GreetButton");
        h.Key(DesignKey.Right, InputModifiers.Shift);
        Assert.Equal(4, h.Undo.UndoCount);

        // After an undo the next nudge starts a new unit.
        h.ExecuteUndo();
        h.Key(DesignKey.Right, InputModifiers.Shift);
        Assert.Equal(4, h.Undo.UndoCount);
        Assert.Equal(0, h.Undo.RedoCount);

        h.ExecuteUndo();
        h.ExecuteUndo();
        h.ExecuteUndo();
        Assert.Equal((234, 16), h.Position("GreetButton"));
        h.ExecuteUndo(); // the merged unit reverts all three nudges at once
        Assert.Equal(original, h.Text);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-105")]
    public void TypingStartsCaptionEdit()
    {
        using var h = DesignerHarness.FromFile(Hello);

        // Caption when the primary has one, else Text; the typed character replaces the value.
        h.Select("GreetButton");
        Assert.True(h.Controller.OnTextInput("O"));
        Assert.Equal([("Caption", "O")], h.Host.PropertyEdits);
        h.Select("NameEdit");
        Assert.True(h.Controller.OnTextInput("7", InputModifiers.Shift));
        Assert.Equal(("Text", "7"), h.Host.PropertyEdits[^1]);
        h.Select("MainForm");
        Assert.True(h.Controller.OnTextInput("x"));
        Assert.Equal(("Caption", "x"), h.Host.PropertyEdits[^1]);

        // Not a letter or digit, Ctrl/Alt held, or no Caption/Text: no action.
        Assert.False(h.Controller.OnTextInput(" "));
        Assert.False(h.Controller.OnTextInput("+"));
        Assert.False(h.Controller.OnTextInput("ab"));
        Assert.False(h.Controller.OnTextInput("a", InputModifiers.Ctrl));
        Assert.False(h.Controller.OnTextInput("a", InputModifiers.Alt));
        using var p = DesignerHarness.FromText(Panels);
        p.Select("Timer1");
        Assert.False(p.Controller.OnTextInput("a"));
        Assert.Empty(p.Host.PropertyEdits);
        Assert.Equal(3, h.Host.PropertyEdits.Count);
        Assert.Equal(0, h.Undo.UndoCount);
    }
}
