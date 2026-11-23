using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Undo and redo leave the modes of the surface first (design-surface.md DSG-SUR-170).</summary>
[Trait("Phase", "5")]
public sealed class UndoTests
{
    private const string Form = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object B1: TButton
            Left = 8
            Top = 200
            Width = 75
            Height = 25
            TabOrder = 0
          end
        end

        """;

    [Fact]
    [Trait("Req", "DSG-SUR-170")]
    public void UndoLeavesTabOrderAndDisarms()
    {
        using var h = DesignerHarness.FromText(Form);
        h.Select("B1");
        Assert.True(h.Key(DesignKey.Right, InputModifiers.Ctrl));
        Assert.Equal((9, 200), h.Position("B1"));

        // Undo in tab-order mode with an armed palette item: the mode ends, the item is disarmed, then the unit is undone.
        h.Execute(Commands.TabOrder);
        h.Host.Arm("TButton", sticky: true);
        Assert.Equal(InteractionState.TabOrder, h.Controller.State);
        h.ExecuteUndo();
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        Assert.Null(h.Host.ArmedPaletteClass);
        Assert.Equal(1, h.Host.DisarmCount);
        Assert.Equal((8, 200), h.Position("B1"));
        Assert.Equal(["B1"], h.Selected); // SelectionBefore, not tab-order mode's { form }

        // Redo is symmetric.
        h.Execute(Commands.TabOrder);
        h.Host.Arm("TButton");
        h.ExecuteRedo();
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        Assert.Null(h.Host.ArmedPaletteClass);
        Assert.Equal(2, h.Host.DisarmCount);
        Assert.Equal((9, 200), h.Position("B1"));
        Assert.Equal(["B1"], h.Selected);
    }
}
