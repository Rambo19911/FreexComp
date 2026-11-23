using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Delete on the design surface (design-surface.md §10.4; D-047).</summary>
public sealed class DeleteTests
{
    private const string Form = """
        object F: TForm
          ActiveControl = Chk2
          ClientWidth = 400
          ClientHeight = 300
          object Top1: TPanel
            Left = 0
            Top = 0
            Width = 400
            Height = 40
            Align = alTop
            TabOrder = 3
          end
          object Panel1: TPanel
            Left = 8
            Top = 48
            Width = 200
            Height = 150
            TabOrder = 0
            object Chk1: TCheckBox
              Left = 8
              Top = 8
              Width = 97
              Height = 17
              TabOrder = 0
            end
            object Chk2: TCheckBox
              Left = 8
              Top = 40
              Width = 97
              Height = 17
              TabOrder = 1
            end
          end
          object Edit1: TEdit
            Left = 240
            Top = 48
            Width = 121
            Height = 23
            TabOrder = 1
          end
          object Btn1: TButton
            Left = 240
            Top = 88
            Width = 75
            Height = 25
            PopupMenu = Popup1
            TabOrder = 2
          end
          object Client1: TPanel
            Left = 0
            Top = 40
            Width = 400
            Height = 260
            Align = alClient
            TabOrder = 4
          end
          object Popup1: TPopupMenu
            DesignLeft = 8
            DesignTop = 8
          end
        end

        """;

    private static int TabOrder(DesignerHarness h, string name) => PropertyAccess.GetInt(h.Object(name), "TabOrder");

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-130")]
    [Trait("Req", "DSG-SUR-131")]
    [Trait("Req", "DSG-SUR-132")]
    public void DeleteRemovesSubtreeClearsReferencesRenumbersTabs()
    {
        using var h = DesignerHarness.FromText(Form);
        var original = h.Text;

        // Nothing to delete when only the form is selected.
        Assert.False(h.Controller.CanExecute("edit.delete"));
        Assert.False(h.Key(DesignKey.Delete));

        // A container with its subtree: references into the subtree are cleared, the siblings renumbered; one unit.
        h.Select("Panel1");
        Assert.True(h.Controller.CanExecute("edit.delete"));
        Assert.True(h.Key(DesignKey.Delete));
        Assert.Null(h.Document.FindByName("Panel1"));
        Assert.Null(h.Document.FindByName("Chk2"));
        Assert.False(PropertyAccess.IsStored(h.Object("F"), "ActiveControl"));
        Assert.Equal((0, 1, 2, 3), (TabOrder(h, "Edit1"), TabOrder(h, "Btn1"), TabOrder(h, "Top1"), TabOrder(h, "Client1")));
        Assert.Equal(["F"], h.Selected);
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Equal("Undo_Designer_Delete", h.Undo.UndoTitleKey);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
        Assert.Equal(["Panel1"], h.Selected);
        h.ExecuteRedo();
        Assert.Equal(["F"], h.Selected);
        h.ExecuteUndo();

        // A child: the selection becomes its parent; a descendant covered by a selected ancestor goes with it.
        h.Select("Chk1");
        h.Execute("edit.delete");
        Assert.Equal(["Panel1"], h.Selected);
        Assert.Equal(0, TabOrder(h, "Chk2"));
        Assert.Contains("ActiveControl = Chk2", h.Text);
        h.ExecuteUndo();
        h.Select("Chk1", "Panel1", "Chk2");
        h.Execute("edit.delete");
        Assert.Null(h.Document.FindByName("Panel1"));
        Assert.Equal(["F"], h.Selected);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // A tray item: references to it are cleared. Lock Controls does not prevent deletion.
        h.Settings.LockControls = true;
        h.Select("Popup1");
        h.Execute("edit.delete");
        Assert.Null(h.Document.FindByName("Popup1"));
        Assert.False(PropertyAccess.IsStored(h.Object("Btn1"), "PopupMenu"));
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // An aligned control: its siblings re-flow and the result is stored in the same unit.
        h.Select("Top1", "Edit1");
        h.Execute("edit.delete");
        Assert.Equal(new Rect(0, 0, 400, 300), h.Bounds("Client1"));
        Assert.Equal((0, 1, 2), (TabOrder(h, "Panel1"), TabOrder(h, "Btn1"), TabOrder(h, "Client1")));
        Assert.Equal(1, h.Undo.UndoCount);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
        Assert.Equal(["Top1", "Edit1"], h.Selected);

        // Not in tab-order mode.
        h.Execute("designer.tabOrder");
        Assert.False(h.Controller.CanExecute("edit.delete"));
    }
}
