using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Palette placement in the controller (tool-palette.md §5.2, §5.3; D-053).</summary>
public sealed class PlacementTests
{
    private const string Form = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object P1: TPanel
            Left = 200
            Top = 100
            Width = 185
            Height = 105
            TabOrder = 0
          end
          object B1: TButton
            Left = 8
            Top = 200
            Width = 75
            Height = 25
            TabOrder = 1
          end
        end

        """;

    private const string Empty = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
        end

        """;

    private static (int X, int Y) Client(DesignerHarness h, int x, int y) => h.Controller.Geometry.FormClientToSurface((x, y));

    private static (int X, int Y) In(DesignerHarness h, string parent, int x, int y) =>
        h.Controller.Geometry.ParentClientToSurface(h.Object(parent), (x, y));

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-044")]
    [Trait("Req", "DSG-TP-052")]
    [Trait("Req", "DSG-TP-054")]
    public void ClickPlacesAtSnappedPointWithDefaultSize()
    {
        using var h = DesignerHarness.FromText(Form);
        var original = h.Text;

        // A click on the form client area: the grid-snapped point, the default size, one "Add" unit, disarmed.
        h.Host.Arm("TButton");
        h.ClickAt(Client(h, 13, 21));
        Assert.Equal("F", h.Parent("Button1"));
        Assert.Equal(new Rect(16, 24, 75, 25), h.Bounds("Button1"));
        Assert.Equal(["P1", "B1", "Button1"], h.ChildNames("F"));
        Assert.Equal(["Button1"], h.Selected);
        Assert.Null(h.Host.ArmedPaletteClass);
        Assert.Equal(1, h.Host.DisarmCount);
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Equal("Undo_Designer_Add", h.Undo.UndoTitleKey);
        Assert.Equal("TButton", h.Undo.Peek!.TitleArgument);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
        Assert.Equal(["F"], h.Selected);
        h.ExecuteRedo();
        Assert.Equal(["Button1"], h.Selected);

        // Inside a container: its client coordinates.
        h.Host.Arm("TButton");
        h.ClickAt(In(h, "P1", 10, 13));
        Assert.Equal("P1", h.Parent("Button2"));
        Assert.Equal(new Rect(8, 16, 75, 25), h.Bounds("Button2"));

        // Over a control that is not a container: into its parent, with a status naming the control.
        h.Host.Arm("TButton");
        h.ClickAt(h.SurfacePoint("B1"));
        Assert.Equal("F", h.Parent("Button3"));
        Assert.Equal(new Rect(48, 216, 75, 25), h.Bounds("Button3"));
        Assert.Contains(h.Host.Statuses, s => s.Id == "Msg.Designer.CannotDropHere" && s.Args.SequenceEqual(["B1"]));

        // Alt: no snapping.
        h.Host.Arm("TButton");
        h.ClickAt(Client(h, 13, 21), InputModifiers.Alt);
        Assert.Equal(new Rect(13, 21, 75, 25), h.Bounds("Button4"));

        // The backdrop (outside the form): the default position in the form.
        h.Host.Arm("TButton");
        h.ClickAt((2, 2));
        Assert.Equal("F", h.Parent("Button5"));
        Assert.Equal(new Rect(8, 8, 75, 25), h.Bounds("Button5"));
        Assert.Equal(5, PropertyAccess.GetInt(h.Object("Button5"), "TabOrder"));
        Assert.Equal(0, PropertyAccess.GetInt(h.Object("Button2"), "TabOrder"));
        Assert.Equal(5, h.Undo.UndoCount);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-045")]
    public void DragDefinesBounds()
    {
        using var h = DesignerHarness.FromText(Empty);

        // Press, drag, release: the normalized rectangle with both corners snapped.
        h.Host.Arm("TPanel");
        h.PressAt(Client(h, 10, 10));
        h.MoveTo(Client(h, 110, 70));
        Assert.Equal(InteractionState.Placing, h.Controller.State);
        var origin = h.Controller.Geometry.FormClientRect;
        Assert.Equal(new Rect(origin.Left + 8, origin.Top + 8, 104, 64), h.Controller.PlacementRect);
        h.Release();
        Assert.Null(h.Controller.PlacementRect);
        Assert.Equal(new Rect(8, 8, 104, 64), h.Bounds("Panel1"));
        Assert.Equal(["Panel1"], h.Selected);
        Assert.Equal(1, h.Undo.UndoCount);

        // Dragged up and left: normalized.
        h.Host.Arm("TPanel");
        h.PressAt(Client(h, 230, 170));
        h.MoveTo(Client(h, 130, 110));
        h.Release();
        Assert.Equal(new Rect(128, 112, 104, 56), h.Bounds("Panel2"));

        // Smaller than 4 after snapping: a click (default size at the press point).
        h.Host.Arm("TButton");
        h.PressAt(Client(h, 300, 40));
        h.MoveTo(Client(h, 306, 41));
        h.Release();
        Assert.Equal(new Rect(304, 40, 75, 25), h.Bounds("Button1"));

        // The target is the container at the press point; it is highlighted during the drag.
        h.Host.Arm("TButton");
        h.PressAt(In(h, "Panel1", 20, 20));
        h.MoveTo(Client(h, 200, 150));
        Assert.Equal("Panel1", h.Controller.DropTarget?.Name);
        h.Release();
        Assert.Equal("Panel1", h.Parent("Button2"));
        Assert.Null(h.Controller.DropTarget);

        // Escape cancels the drag but keeps the item armed; the next Escape disarms.
        var units = h.Undo.UndoCount;
        var disarms = h.Host.DisarmCount;
        h.Host.Arm("TButton");
        h.PressAt(Client(h, 200, 200));
        h.MoveTo(Client(h, 260, 260));
        Assert.True(h.Key(DesignKey.Escape));
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        Assert.Null(h.Controller.PlacementRect);
        h.Release();
        Assert.Equal(units, h.Undo.UndoCount);
        Assert.Equal("TButton", h.Host.ArmedPaletteClass);
        Assert.True(h.Key(DesignKey.Escape));
        Assert.Null(h.Host.ArmedPaletteClass);
        Assert.Equal(disarms + 1, h.Host.DisarmCount);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-046")]
    public void DefaultPositionCascades()
    {
        // (gridX, gridY), then +2 grid steps while a child is exactly there; stops at the last position inside the client area.
        using (var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 100
              ClientHeight = 60
            end

            """))
        {
            h.Controller.PlaceAtDefaultPosition("TButton");
            h.Controller.PlaceAtDefaultPosition("TButton");
            h.Controller.PlaceAtDefaultPosition("TButton");
            Assert.Equal(new Rect(8, 8, 75, 25), h.Bounds("Button1"));
            Assert.Equal(new Rect(24, 24, 75, 25), h.Bounds("Button2"));
            Assert.Equal(new Rect(24, 24, 75, 25), h.Bounds("Button3"));
            Assert.Equal(["Button3"], h.Selected);
            Assert.Equal(3, h.Undo.UndoCount);
        }

        // The primary when it is a container; else its parent.
        using var g = DesignerHarness.FromText(Form);
        g.Select("P1");
        g.Controller.PlaceAtDefaultPosition("TButton");
        Assert.Equal("P1", g.Parent("Button1"));
        Assert.Equal(new Rect(8, 8, 75, 25), g.Bounds("Button1"));
        g.Controller.PlaceAtDefaultPosition("TButton");
        Assert.Equal("P1", g.Parent("Button2"));
        Assert.Equal(new Rect(24, 24, 75, 25), g.Bounds("Button2"));
        g.Select("B1");
        g.Controller.PlaceAtDefaultPosition("TCheckBox");
        Assert.Equal("F", g.Parent("CheckBox1"));
        Assert.Equal(new Rect(8, 8, 97, 17), g.Bounds("CheckBox1"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-043")]
    public void NonVisualGoesToNextFreeTraySlot()
    {
        using var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object P1: TPanel
                Left = 200
                Top = 100
                Width = 185
                Height = 105
                TabOrder = 0
              end
              object Timer1: TTimer
                DesignLeft = 8
                DesignTop = 8
              end
              object OpenDialog1: TOpenDialog
                DesignLeft = 168
                DesignTop = 8
              end
            end

            """);

        // A click on the form or on a control: a child of the form, in the next free tray slot.
        h.Host.Arm("TTimer");
        h.ClickAt(Client(h, 50, 50));
        Assert.Equal("F", h.Parent("Timer2"));
        Assert.Equal((88, 8), SurfaceGeometry.TrayPosition(h.Object("Timer2")));
        Assert.Equal(["Timer2"], h.Selected);
        h.Host.Arm("TTimer");
        h.ClickAt(h.SurfacePoint("P1"));
        Assert.Equal("F", h.Parent("Timer3"));
        Assert.Equal((248, 8), SurfaceGeometry.TrayPosition(h.Object("Timer3")));

        // A click inside the tray: the click position − (16, 16), grid-snapped, at least 0.
        h.Host.Arm("TTimer");
        h.ClickAt(h.Controller.Geometry.TrayToSurface((100, 40)));
        Assert.Equal((88, 24), SurfaceGeometry.TrayPosition(h.Object("Timer4")));
        h.Host.Arm("TTimer");
        h.ClickAt(h.Controller.Geometry.TrayToSurface((3, 3)));
        Assert.Equal((0, 0), SurfaceGeometry.TrayPosition(h.Object("Timer5")));

        // A drag places like a click at the press point; the default position is the next free slot.
        h.Host.Arm("TOpenDialog");
        h.PressAt(Client(h, 20, 20));
        h.MoveTo(Client(h, 120, 120));
        h.Release();
        Assert.Equal((8, 72), SurfaceGeometry.TrayPosition(h.Object("OpenDialog2")));
        h.Controller.PlaceAtDefaultPosition("TTimer");
        Assert.Equal((168, 72), SurfaceGeometry.TrayPosition(h.Object("Timer6")));
        Assert.Equal(6, h.Undo.UndoCount);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-050")]
    [Trait("Req", "DSG-TP-051")]
    public void NameAndInitialProperties()
    {
        using var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object Button1: TButton
                Left = 8
                Top = 200
                Width = 75
                Height = 25
                TabOrder = 0
              end
            end

            """);
        h.Host.CodeSync.Members["Button2"] = Designer.CodeSync.MemberLookup.Method; // a member of the form class: not free

        // <namePrefix><n>, smallest free n; geometry, then initial values with $(Name), then TabOrder.
        h.Host.Arm("TButton");
        h.ClickAt(Client(h, 16, 24));
        Assert.Contains("""
              object Button3: TButton
                Left = 16
                Top = 24
                Width = 75
                Height = 25
                Caption = 'Button3'
                TabOrder = 1
              end
            """.Replace("\r\n", "\n"), h.Text);

        // A container with a caption; a non-visual component with its tray position only.
        h.Controller.PlaceAtDefaultPosition("TPanel");
        Assert.Contains("""
              object Panel1: TPanel
                Left = 8
                Top = 8
                Width = 185
                Height = 41
                Caption = 'Panel1'
                TabOrder = 2
              end
            """.Replace("\r\n", "\n"), h.Text);
        h.Controller.PlaceAtDefaultPosition("TTimer");
        Assert.Contains("""
              object Timer1: TTimer
                DesignLeft = 8
                DesignTop = 8
              end
            """.Replace("\r\n", "\n"), h.Text);

        // Abstract or unknown classes create nothing.
        var units = h.Undo.UndoCount;
        h.Controller.PlaceAtDefaultPosition("TControl");
        h.Controller.PlaceAtDefaultPosition("TNoSuchClass");
        Assert.Equal(units, h.Undo.UndoCount);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-041")]
    public void StickyArmPlacesRepeatedly()
    {
        using var h = DesignerHarness.FromText(Empty);

        // Sticky: the item stays armed after each placement.
        h.Host.Arm("TButton", sticky: true);
        h.ClickAt(Client(h, 16, 16));
        h.ClickAt(Client(h, 16, 64));
        h.ClickAt(Client(h, 16, 112));
        Assert.Equal(["Button1", "Button2", "Button3"], h.ChildNames("F"));
        Assert.Equal(new Rect(16, 64, 75, 25), h.Bounds("Button2"));
        Assert.Equal(["Button3"], h.Selected);
        Assert.Equal("TButton", h.Host.ArmedPaletteClass);
        Assert.Equal(0, h.Host.DisarmCount);
        Assert.Equal(3, h.Undo.UndoCount);

        // A right-click on the surface disarms (no context menu, no selection change).
        h.RightClick("Button1");
        Assert.Null(h.Host.ArmedPaletteClass);
        Assert.Equal(1, h.Host.DisarmCount);
        Assert.Empty(h.Host.ContextMenus);
        Assert.Equal(["Button3"], h.Selected);

        // Single-shot: placing disarms; Escape on the surface disarms too.
        h.Host.Arm("TButton");
        h.ClickAt(Client(h, 120, 16));
        Assert.Null(h.Host.ArmedPaletteClass);
        h.Host.Arm("TButton");
        Assert.True(h.Key(DesignKey.Escape));
        Assert.Null(h.Host.ArmedPaletteClass);
        Assert.Equal(["Button4"], h.Selected); // Escape disarmed instead of selecting the parent
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-TP-053")]
    [Trait("Req", "DSG-SUR-127")]
    public void PlacementAndPasteAddFieldsThroughCodeSync()
    {
        using var h = DesignerHarness.FromText(Form);
        var edit = new FreexComp.Designer.Editing.TextEditRecord(10, "", "Panel2: TPanel;");
        h.Host.CodeSync.AddFieldsEdits = _ => [edit];

        // Placement: one field with the class's unit, its edits and the logical operation in the "Add" unit.
        h.Controller.PlaceAtDefaultPosition("TPanel");
        var fields = Assert.Single(h.Host.CodeSync.AddedFields);
        Assert.Equal([new FreexComp.Designer.CodeSync.CodeSyncField("Panel1", "TPanel", "ExtCtrls")], fields);
        Assert.Equal([edit], h.Undo.Peek!.CodeEdits);
        Assert.Equal(fields, Assert.IsType<FreexComp.Designer.CodeSync.AddFieldsOp>(Assert.Single(h.Undo.Peek.CodeOps)).Fields);

        // Paste of a container: every pasted component (pre-order), with the new names.
        h.Select("P1");
        h.Execute("edit.copy");
        h.Select("F");
        h.Execute("edit.paste");
        Assert.Equal(["P2"], h.Host.CodeSync.AddedFields[^1].Select(f => f.Name));
        h.ExecuteUndo();
        Assert.Equal([edit], h.Host.CodeSync.Reverted[^1]);

        // Refused by code sync (DSG-CS-004): nothing is created, the conflict is shown.
        h.Host.CodeSync.AddFieldsEdits = _ => throw new FreexComp.Designer.CodeSync.CodeSyncException("broken");
        var before = h.Text;
        var units = h.Undo.UndoCount;
        h.Controller.PlaceAtDefaultPosition("TButton");
        h.Execute("edit.paste");
        Assert.Equal(before, h.Text);
        Assert.Equal(units, h.Undo.UndoCount);
        Assert.Equal(("Msg.Designer.CodeSyncConflict", "broken"), (h.Host.Statuses[^1].Id, h.Host.Statuses[^1].Args[0]));
    }
}
