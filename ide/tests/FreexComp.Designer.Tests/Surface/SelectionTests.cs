using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Selection and the click rules of the design surface (design-surface.md §3, §4; D-035).</summary>
public sealed class SelectionTests
{
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-020")]
    [Trait("Req", "DSG-SUR-021")]
    [Trait("Req", "DSG-SUR-022")]
    [Trait("Req", "DSG-SUR-025")]
    [Trait("Req", "DSG-SUR-041")]
    public void ClickShiftClickCtrlClick()
    {
        using var h = DesignerHarness.FromFile("forms/canonical/hello.form");
        var changes = 0;
        h.Selection.Changed += (_, _) => changes++;
        Assert.Equal(["MainForm"], h.Selected); // "nothing selected" = the form

        h.Click("NameEdit");
        Assert.Equal(["NameEdit"], h.Selected);

        h.Click("GreetButton", modifiers: InputModifiers.Shift); // appended, becomes primary
        Assert.Equal(["NameEdit", "GreetButton"], h.Selected);
        Assert.Equal("GreetButton", h.Selection.Primary.Name);

        h.Click("NameEdit", modifiers: InputModifiers.Ctrl); // selected, more than one → removed
        Assert.Equal(["GreetButton"], h.Selected);

        changes = 0;
        h.Click("GreetButton", modifiers: InputModifiers.Ctrl); // the only element → no change
        h.ClickAt((380, 250), InputModifiers.Shift); // adding the form is ignored
        Assert.Equal(["GreetButton"], h.Selected);
        Assert.Equal(0, changes);

        h.ClickAt((380, 250)); // form client area → { form }
        Assert.Equal(["MainForm"], h.Selected);
        h.Click("NameEdit", modifiers: InputModifiers.Shift); // replaces a selection of the form
        Assert.Equal(["NameEdit"], h.Selected);

        // Pointer down on an unselected component selects it at once (DSG-SUR-041) ...
        h.Press("GreetButton", modifiers: InputModifiers.None);
        Assert.Equal(["GreetButton"], h.Selected);
        Assert.Equal(InteractionState.PressPending, h.Controller.State);
        h.Release();
        Assert.Equal(InteractionState.Idle, h.Controller.State);

        // ... on a selected one of a multi-selection it waits for the release: a click selects only it, a drag keeps the selection.
        h.Click("NameEdit", modifiers: InputModifiers.Shift);
        h.Press("GreetButton");
        Assert.Equal(["GreetButton", "NameEdit"], h.Selected);
        h.MoveBy(3, -3); // below the drag threshold (4)
        h.Release();
        Assert.Equal(["GreetButton"], h.Selected);
        h.Click("NameEdit", modifiers: InputModifiers.Shift);
        h.Press("GreetButton");
        h.MoveBy(4, 0);
        h.Release();
        Assert.Equal(["GreetButton", "NameEdit"], h.Selected);

        // Caption, tray background and backdrop select the form; right-click keeps a selected component and opens the menu.
        h.ClickAt((100, 30));
        Assert.Equal(["MainForm"], h.Selected);
        h.Select("NameEdit", "GreetButton");
        h.RightClick("NameEdit");
        Assert.Equal(["NameEdit", "GreetButton"], h.Selected);
        Assert.Single(h.Host.ContextMenus);
        h.ClickAt((2, 2));
        Assert.Equal(["MainForm"], h.Selected);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-024")]
    [Trait("Req", "DSG-SUR-023")]
    public void AncestorCoversDescendant()
    {
        using var h = DesignerHarness.FromFile("forms/canonical/nested.form");

        // Shift+click across parents: the selection keeps all three, the effective set drops the covered check box.
        h.Click("InfoLabel");
        h.Click("FastCheck", modifiers: InputModifiers.Shift);
        h.Click("OptionsBox", at: (150, 10), modifiers: InputModifiers.Shift); // beside the caption, clear of the children
        Assert.Equal(["InfoLabel", "FastCheck", "OptionsBox"], h.Selected);
        Assert.Equal(["OptionsBox", "InfoLabel"], h.Selection.GetEffectiveSet().Select(o => o.Name)); // pre-order

        // An ancestor two levels up covers everything below it.
        h.Select("SafeRadio", "OuterPanel", "FastCheck");
        Assert.Equal(["OuterPanel"], h.Selection.GetEffectiveSet().Select(o => o.Name));
        Assert.Equal("FastCheck", h.Selection.Primary.Name);

        // Siblings are not covered by each other.
        h.Select("SafeRadio", "FastCheck");
        Assert.Equal(["FastCheck", "SafeRadio"], h.Selection.GetEffectiveSet().Select(o => o.Name));
    }

    // Panel1 at surface (25, 55)–(225, 155), client origin (26, 56); Btn1 (36, 66)–(111, 91), Btn2 (126, 66)–(201, 91);
    // Edit1 (25, 197)–(146, 220). Tray: Timer1's cell (25, 365)–(97, 415), Dialog1's (105, 365)–(177, 415).
    // Edit1 is created after Timer1: the Tab cycle still visits every control before the tray items.
    internal const string Mixed = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object Panel1: TPanel
            Left = 8
            Top = 8
            Width = 200
            Height = 100
            object Btn1: TButton
              Left = 10
              Top = 10
              Width = 75
              Height = 25
            end
            object Btn2: TButton
              Left = 100
              Top = 10
              Width = 75
              Height = 25
            end
          end
          object Timer1: TTimer
            DesignLeft = 8
            DesignTop = 8
          end
          object Edit1: TEdit
            Left = 8
            Top = 150
            Width = 121
            Height = 23
          end
          object Dialog1: TOpenDialog
            DesignLeft = 88
            DesignTop = 8
          end
        end

        """;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-026")]
    public void SelectAllSelectsSiblingsOfPrimary()
    {
        using var h = DesignerHarness.FromText(Mixed);
        Assert.True(h.Controller.CanExecute("edit.selectAll"));

        h.Select("Edit1", "Btn1"); // the primary decides: Btn1's parent is Panel1
        h.Execute("edit.selectAll");
        Assert.Equal(["Btn1", "Btn2"], h.Selected);
        Assert.Equal("Btn2", h.Selection.Primary.Name);

        h.Select("Panel1");
        h.Execute("edit.selectAll"); // direct children of the form, creation order, tray items excluded
        Assert.Equal(["Panel1", "Edit1"], h.Selected);

        h.Select("F");
        h.Execute("edit.selectAll");
        Assert.Equal(["Panel1", "Edit1"], h.Selected);

        h.Select("Timer1");
        h.Execute("edit.selectAll"); // a tray item: all tray items instead
        Assert.Equal(["Timer1", "Dialog1"], h.Selected);

        using var empty = DesignerHarness.FromText("object E: TForm\n  ClientWidth = 200\n  ClientHeight = 100\nend\n");
        var changes = 0;
        empty.Selection.Changed += (_, _) => changes++;
        empty.Execute("edit.selectAll"); // nothing to select: unchanged
        Assert.Equal(["E"], empty.Selected);
        Assert.Equal(0, changes);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-027")]
    [Trait("Req", "DSG-SUR-104")]
    public void EscapeSelectsParent()
    {
        using var h = DesignerHarness.FromFile("forms/canonical/nested.form");
        h.Select("InfoLabel", "FastCheck"); // primary FastCheck
        Assert.True(h.Controller.CanExecute("designer.selectParent"));
        Assert.True(h.Key(DesignKey.Escape));
        Assert.Equal(["OptionsBox"], h.Selected);
        Assert.True(h.Key(DesignKey.Escape));
        Assert.Equal(["OuterPanel"], h.Selected);
        h.Execute("designer.selectParent"); // a direct child of the form → { form }
        Assert.Equal(["NestedForm"], h.Selected);

        // The form selected: no change, and the key is reported unhandled (focus stays, UI-A11Y-004).
        Assert.False(h.Controller.CanExecute("designer.selectParent"));
        Assert.False(h.Key(DesignKey.Escape));
        Assert.Equal(["NestedForm"], h.Selected);

        using var tray = DesignerHarness.FromFile("forms/canonical/nonvisual.form");
        tray.Select("OpenDialog1");
        Assert.True(tray.Key(DesignKey.Escape)); // a tray item → { form }
        Assert.Equal(["TrayForm"], tray.Selected);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-028")]
    public void TabCyclesInPreOrder()
    {
        using var h = DesignerHarness.FromText(Mixed);
        string[] cycle = ["Panel1", "Btn1", "Btn2", "Edit1", "Timer1", "Dialog1"];

        foreach (var name in cycle) // from the form: the first, then pre-order with the tray items last
        {
            Assert.True(h.Key(DesignKey.Tab));
            Assert.Equal([name], h.Selected);
        }

        Assert.True(h.Key(DesignKey.Tab)); // from the last: the first (the form is not in the cycle)
        Assert.Equal(["Panel1"], h.Selected);
        Assert.True(h.Key(DesignKey.Tab, InputModifiers.Shift)); // and back
        Assert.Equal(["Dialog1"], h.Selected);
        h.Execute("designer.selectPrevious");
        Assert.Equal(["Timer1"], h.Selected);

        h.Select("F");
        h.Execute("designer.selectPrevious"); // from the form, Shift+Tab selects the last
        Assert.Equal(["Dialog1"], h.Selected);

        h.Select("Btn2", "Btn1"); // a multi-selection continues from the primary; the result is single
        h.Execute("designer.selectNext");
        Assert.Equal(["Btn2"], h.Selected);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-035")]
    [Trait("Req", "DSG-SUR-036")]
    [Trait("Req", "DSG-SUR-037")]
    public void LassoSelectsIntersectingChildren()
    {
        using var h = DesignerHarness.FromText(Mixed);

        // On the form client area: direct children of the form whose bounds intersect, in creation order.
        h.PressAt((300, 60));
        h.MoveTo((140, 210));
        Assert.Equal(InteractionState.Lasso, h.Controller.State);
        Assert.Equal(new Rect(140, 60, 160, 150), h.Controller.LassoRect);
        Assert.Equal(["F"], h.Selected); // nothing changes before the release
        h.Release();
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        Assert.Null(h.Controller.LassoRect);
        Assert.Equal(["Panel1", "Edit1"], h.Selected);

        // Touching edges do not count (Panel1 ends at x = 225): no hit → { the lasso container }.
        h.PressAt((300, 60));
        h.MoveTo((225, 100));
        h.Release();
        Assert.Equal(["F"], h.Selected);

        // Inside a container only with Ctrl (without it the drag moves the container).
        h.PressAt((200, 140), InputModifiers.Ctrl);
        h.MoveTo((150, 80));
        Assert.Equal(InteractionState.Lasso, h.Controller.State);
        h.Release();
        Assert.Equal(["Btn2"], h.Selected); // Btn1 (36 … 111) is outside

        // Shift held at release adds to the selection.
        h.PressAt((300, 190));
        h.MoveTo((140, 215));
        h.Release(InputModifiers.Shift);
        Assert.Equal(["Btn2", "Edit1"], h.Selected);

        // Escape and CaptureLost cancel without changing the selection.
        h.PressAt((300, 60));
        h.MoveTo((140, 210));
        Assert.True(h.Key(DesignKey.Escape));
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        h.Release();
        Assert.Equal(["Btn2", "Edit1"], h.Selected);
        h.PressAt((300, 60));
        h.MoveTo((140, 210));
        h.CaptureLost();
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        Assert.Equal(["Btn2", "Edit1"], h.Selected);

        // The tray background starts a tray lasso over the tray items; no hit → { form }.
        h.PressAt((300, 400));
        h.MoveTo((150, 380));
        h.Release();
        Assert.Equal(["Dialog1"], h.Selected);
        h.PressAt((300, 400));
        h.MoveTo((250, 380));
        h.Release();
        Assert.Equal(["F"], h.Selected);

        // The backdrop starts nothing, and a drag is no click.
        h.Select("Btn1");
        h.PressAt((600, 600));
        h.MoveTo((10, 10));
        Assert.Equal(InteractionState.PressPending, h.Controller.State);
        h.Release();
        Assert.Equal(["Btn1"], h.Selected);
    }
}
