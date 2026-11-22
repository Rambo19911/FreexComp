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

    private static readonly string NestedWithoutAlign =
        File.ReadAllText(TestPaths.Combine("forms/canonical/nested.form")).Replace("    Align = alClient\n", "", StringComparison.Ordinal);

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-024")]
    [Trait("Req", "DSG-SUR-023")]
    public void AncestorCoversDescendant()
    {
        // forms/canonical/nested.form without OuterPanel's Align: its stored bounds there are not the alClient result,
        // which the harness's final layout-invariant check (DSG-LAY-040) rejects.
        using var h = DesignerHarness.FromText(NestedWithoutAlign);

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
}
