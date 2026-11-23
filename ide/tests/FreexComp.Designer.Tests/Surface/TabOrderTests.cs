using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Tab order on the design surface (design-surface.md §8, form-format.md §2.3; D-045, D-046).</summary>
public sealed class TabOrderTests
{
    private const string Form = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object Panel1: TPanel
            Left = 8
            Top = 8
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
            object Chk3: TCheckBox
              Left = 8
              Top = 72
              Width = 97
              Height = 17
              TabOrder = 2
            end
          end
          object Edit1: TEdit
            Left = 240
            Top = 8
            Width = 121
            Height = 23
            TabOrder = 1
          end
          object Btn1: TButton
            Left = 240
            Top = 48
            Width = 75
            Height = 25
            TabOrder = 2
          end
          object Label1: TLabel
            Left = 240
            Top = 100
            Width = 40
            Height = 15
          end
        end

        """;

    private static IReadOnlyList<(string, int)> Orders(DesignerHarness h, string parent) =>
        h.Object(parent).Children.Where(TabOrderRules.IsFocusable)
            .Select(c => (c.Name, PropertyAccess.GetInt(c, "TabOrder"))).ToList();

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-091")]
    [Trait("Req", "DSG-SUR-096")]
    public void AutomaticMaintenanceOnAddDeleteReparent()
    {
        using var h = DesignerHarness.FromText(Form);
        h.Settings.NoSnapping();
        var original = h.Text;

        // Focusable = wincontrol or container with a TabOrder property; labels and the tray are not.
        Assert.True(TabOrderRules.IsFocusable(h.Object("Panel1")));
        Assert.False(TabOrderRules.IsFocusable(h.Object("Label1")));

        // A new control gets the last position (= number of focusable siblings before insertion).
        Assert.Equal(3, TabOrderRules.NextValue(h.Object("Panel1")));
        Assert.Equal(3, TabOrderRules.NextValue(h.Object("F")));

        // Re-parenting by drag: the old parent closes the gap, the moved control is last in the new parent; one unit.
        h.Press("Chk2");
        h.MoveTo(h.SurfacePoint("F", (300, 200)));
        h.Release();
        Assert.Equal("F", h.Parent("Chk2"));
        Assert.Equal([("Chk1", 0), ("Chk3", 1)], Orders(h, "Panel1"));
        Assert.Equal([("Panel1", 0), ("Edit1", 1), ("Btn1", 2), ("Chk2", 3)], Orders(h, "F"));
        Assert.Equal(1, h.Undo.UndoCount);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // Several controls into a container: appended in creation order; a non-focusable control gets no TabOrder.
        h.Select("Edit1", "Btn1", "Label1");
        h.Press("Btn1");
        h.MoveTo(h.SurfacePoint("Panel1", (150, 120)));
        h.Release();
        Assert.Equal("Panel1", h.Parent("Label1"));
        Assert.Equal([("Chk1", 0), ("Chk2", 1), ("Chk3", 2), ("Edit1", 3), ("Btn1", 4)], Orders(h, "Panel1"));
        Assert.Equal([("Panel1", 0)], Orders(h, "F"));
        Assert.False(PropertyAccess.IsStored(h.Object("Label1"), "TabOrder"));
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // A move inside the same parent changes no TabOrder.
        h.Press("Edit1");
        h.MoveBy(10, 10);
        h.Release();
        Assert.Equal([("Panel1", 0), ("Edit1", 1), ("Btn1", 2)], Orders(h, "F"));
        h.ExecuteUndo();

        // Deleting renumbers the remaining siblings (the effective order: explicit values first, then the others).
        var changes = TabOrderRules.Renumber(h.Object("Panel1"), [h.Object("Chk1")]);
        Assert.Equal([new SetProperty("Chk2", "TabOrder", new IntegerValue(0, false)), new SetProperty("Chk3", "TabOrder", new IntegerValue(1, false))],
            changes);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-091")]
    public void UnassignedValuesFollowExplicitOnesAndAreNumberedOnFirstChange()
    {
        using var h = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object A: TEdit
                Left = 8
                Top = 8
                Width = 100
                Height = 23
              end
              object B: TEdit
                Left = 8
                Top = 40
                Width = 100
                Height = 23
                TabOrder = 0
              end
              object C: TEdit
                Left = 8
                Top = 72
                Width = 100
                Height = 23
              end
            end

            """);

        // Effective order: B (explicit 0), then A, C in creation order.
        Assert.Equal(["B", "A", "C"], TabOrderRules.Sequence(h.Object("F")).Select(o => o.Name));
        Assert.Equal([new SetProperty("A", "TabOrder", new IntegerValue(1, false)), new SetProperty("C", "TabOrder", new IntegerValue(2, false))],
            TabOrderRules.Renumber(h.Object("F")));

        // In tab-order mode the first click in a parent numbers all its siblings (one unit).
        h.Execute("designer.tabOrder");
        h.Click("C");
        Assert.Equal(["C", "B", "A"], TabOrderRules.Sequence(h.Object("F")).Select(o => o.Name));
        Assert.Contains("TabOrder = 2", h.Text);
        Assert.Equal(1, h.Undo.UndoCount);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-090")]
    [Trait("Req", "DSG-SUR-092")]
    [Trait("Req", "DSG-SUR-093")]
    [Trait("Req", "DSG-SUR-094")]
    [Trait("Req", "DSG-SUR-095")]
    public void ClickSequenceAssignsOrderPerParent()
    {
        using var h = DesignerHarness.FromText(Form);
        var original = h.Text;
        h.Select("Edit1");

        // Entry: state TabOrder, selection { form }; not an undo unit.
        h.Execute("designer.tabOrder");
        Assert.Equal(InteractionState.TabOrder, h.Controller.State);
        Assert.Equal(["F"], h.Selected);
        Assert.Equal(0, h.Undo.UndoCount);

        // Each click puts the control at the parent's next position; one unit per click.
        h.Click("Btn1");
        Assert.Equal([("Panel1", 1), ("Edit1", 2), ("Btn1", 0)], Orders(h, "F"));
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Equal("Undo_Designer_TabOrder", h.Undo.UndoTitleKey);
        h.Click("Edit1");
        Assert.Equal([("Panel1", 2), ("Edit1", 1), ("Btn1", 0)], Orders(h, "F"));

        // Counters are per parent.
        h.Click("Chk3");
        Assert.Equal([("Chk1", 1), ("Chk2", 2), ("Chk3", 0)], Orders(h, "Panel1"));
        Assert.Equal(3, h.Undo.UndoCount);

        // Non-focusable controls, the form background and right-clicks do nothing; nothing moves by drag.
        h.Click("Label1");
        h.Click("F", (390, 290));
        h.RightClick("Edit1");
        Assert.Empty(h.Host.ContextMenus);
        h.Press("Label1");
        h.MoveBy(30, 30);
        h.Release();
        Assert.Equal((240, 100), h.Position("Label1"));
        Assert.Equal(3, h.Undo.UndoCount);
        Assert.Equal(["F"], h.Selected);

        // A click that changes nothing records nothing; after the last position the parent restarts at 0.
        h.Click("Panel1", (150, 120));
        Assert.Equal(3, h.Undo.UndoCount);
        h.Click("Btn1"); // restart: k = 0, Btn1 is already 0
        Assert.Equal(3, h.Undo.UndoCount);
        h.Click("Panel1", (150, 120)); // k = 1
        Assert.Equal([("Panel1", 1), ("Edit1", 2), ("Btn1", 0)], Orders(h, "F"));
        Assert.Equal(4, h.Undo.UndoCount);

        // Restart Numbering clears every counter.
        h.Controller.RestartTabOrderNumbering();
        h.Click("Edit1");
        Assert.Equal([("Panel1", 2), ("Edit1", 0), ("Btn1", 1)], Orders(h, "F"));
        h.Click("Chk1");
        Assert.Equal([("Chk1", 0), ("Chk2", 2), ("Chk3", 1)], Orders(h, "Panel1"));

        // Keyboard: Tab / Shift+Tab move the focus rectangle through the focusable controls in pre-order (wrapping);
        // Enter or Space click the focused control.
        Assert.Null(h.Controller.TabOrderFocus);
        Assert.True(h.Key(DesignKey.Tab));
        Assert.Same(h.Object("Panel1"), h.Controller.TabOrderFocus);
        Assert.True(h.Key(DesignKey.Tab));
        Assert.True(h.Key(DesignKey.Tab));
        Assert.Same(h.Object("Chk2"), h.Controller.TabOrderFocus);
        Assert.True(h.Key(DesignKey.Space));
        Assert.Equal([("Chk1", 0), ("Chk2", 1), ("Chk3", 2)], Orders(h, "Panel1"));
        Assert.True(h.Key(DesignKey.Tab, InputModifiers.Shift));
        Assert.True(h.Key(DesignKey.Tab, InputModifiers.Shift));
        Assert.True(h.Key(DesignKey.Tab, InputModifiers.Shift));
        Assert.Same(h.Object("Btn1"), h.Controller.TabOrderFocus);
        Assert.True(h.Key(DesignKey.Enter)); // F's counter is 1: Btn1 is already 1
        Assert.Equal([("Panel1", 2), ("Edit1", 0), ("Btn1", 1)], Orders(h, "F"));
        Assert.Equal(["F"], h.Selected);

        // Escape, the command again and undo leave the mode.
        Assert.True(h.Key(DesignKey.Escape));
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        Assert.Null(h.Controller.TabOrderFocus);
        h.Execute("designer.tabOrder");
        h.Execute("designer.tabOrder");
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        h.Execute("designer.tabOrder");
        h.ExecuteUndo();
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        while (h.Undo.CanUndo)
        {
            h.ExecuteUndo();
        }

        Assert.Equal(original, h.Text);
    }
}
