using FreexComp.Designer.Editing;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Undo granularity and selection restore (design-surface.md §12, form-document.md DSG-FD-092; D-056).</summary>
public sealed class UndoGranularityTests
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
            object Top1: TPanel
              Left = 1
              Top = 1
              Width = 183
              Height = 41
              Align = alTop
              TabOrder = 0
            end
          end
          object B1: TButton
            Left = 8
            Top = 200
            Width = 75
            Height = 25
            TabOrder = 1
          end
          object B2: TButton
            Left = 96
            Top = 240
            Width = 75
            Height = 25
            TabOrder = 2
          end
          object E1: TEdit
            Left = 8
            Top = 8
            Width = 121
            Height = 23
            TabOrder = 3
          end
        end

        """;

    /// <summary>One interaction of the §12 table: what to do, the units it must leave and the title of the newest one.</summary>
    private sealed record Interaction(string Name, Action<DesignerHarness> Run, int Units, string? Title);

    private static void Drag(DesignerHarness h, string on, int dx, int dy, InputModifiers release = InputModifiers.None)
    {
        h.Press(on);
        h.MoveBy(dx / 2, dy / 2);
        h.MoveBy(dx - dx / 2, dy - dy / 2);
        h.Release(release);
    }

    private static readonly Interaction[] Interactions =
    [
        new("Pointer move drag", h => Drag(h, "B1", 40, -24), 1, UndoTitles.Move),
        new("Move drag with re-parenting and tab renumbering", h => Drag(h, "B1", 220, -60), 1, UndoTitles.Move),
        new("Ctrl-drag copy", h => Drag(h, "B1", 0, 40, InputModifiers.Ctrl), 1, UndoTitles.Copy),
        new("Pointer resize drag with child re-flow", h =>
        {
            h.Select("P1");
            h.PressHandle("P1", HandleKind.E);
            h.MoveBy(10, 0);
            h.MoveBy(14, 0);
            h.Release();
        }, 1, UndoTitles.Resize),
        new("Form resize drag", h =>
        {
            h.Select("F");
            h.PressHandle("F", HandleKind.SE);
            h.MoveBy(16, 16);
            h.Release();
        }, 1, UndoTitles.Resize),
        new("Keyboard nudges within the merge window", h =>
        {
            h.Select("B1");
            h.Key(DesignKey.Right);
            h.Time.Advance(TimeSpan.FromMilliseconds(500));
            h.Key(DesignKey.Right);
            h.Key(DesignKey.Down);
        }, 1, UndoTitles.Move),
        new("Keyboard nudges after the merge window", h =>
        {
            h.Select("B1");
            h.Key(DesignKey.Right);
            h.Time.Advance(TimeSpan.FromSeconds(2));
            h.Key(DesignKey.Right);
        }, 2, UndoTitles.Move),
        new("Keyboard resizes", h =>
        {
            h.Select("B1");
            h.Key(DesignKey.Right, InputModifiers.Shift);
            h.Key(DesignKey.Down, InputModifiers.Shift);
        }, 1, UndoTitles.Resize),
        new("Palette placement", h =>
        {
            h.Host.Arm("TButton");
            h.ClickAt(h.Controller.Geometry.FormClientToSurface((300, 20)));
        }, 1, UndoTitles.Add),
        new("Paste", h =>
        {
            h.Select("B1", "B2");
            h.Execute(Commands.Copy);
            h.Execute(Commands.Paste);
        }, 1, UndoTitles.Paste),
        new("Cut", h =>
        {
            h.Select("B1", "B2");
            h.Execute(Commands.Cut);
        }, 1, UndoTitles.Cut),
        new("Delete of a subtree", h =>
        {
            h.Select("P1");
            h.Key(DesignKey.Delete);
        }, 1, UndoTitles.Delete),
        new("Align command", h =>
        {
            h.Select("B2", "B1");
            h.Execute(Commands.AlignLeft);
        }, 1, UndoTitles.Align),
        new("Size command", h =>
        {
            h.Select("E1", "B1");
            h.Execute(Commands.SameWidth);
        }, 1, UndoTitles.Size),
        new("Z-order command", h =>
        {
            h.Select("B1");
            h.Execute(Commands.BringToFront);
        }, 1, UndoTitles.Order),
        new("Tab-order clicks", h =>
        {
            h.Execute(Commands.TabOrder);
            h.Click("E1");
            h.Click("B2");
            h.Controller.ExitTabOrderMode();
        }, 2, UndoTitles.TabOrder),
        new("Create default event handler", h =>
        {
            h.Select("B1");
            h.Execute(Commands.CreateDefaultHandler);
        }, 1, UndoTitles.Event),
        new("Selection changes, select-all, Tab, Escape, lasso", h =>
        {
            h.Click("B1");
            h.Click("B2", modifiers: InputModifiers.Shift);
            h.Execute(Commands.SelectAll);
            h.Key(DesignKey.Tab);
            h.Key(DesignKey.Escape);
            h.PressAt(h.Controller.Geometry.FormClientToSurface((150, 150)));
            h.MoveBy(60, 80);
            h.Release();
        }, 0, null),
        new("Tab-order mode entry and exit", h =>
        {
            h.Execute(Commands.TabOrder);
            h.Execute(Commands.TabOrder);
        }, 0, null),
        new("Copy", h =>
        {
            h.Select("B1");
            h.Execute(Commands.Copy);
        }, 0, null),
        new("Cancelled drag", h =>
        {
            h.Press("B1");
            h.MoveBy(30, 30);
            h.Key(DesignKey.Escape);
            h.Release();
        }, 0, null),
        new("Drop without change", h =>
        {
            h.Press("B1");
            h.MoveBy(30, 0);
            h.MoveBy(-30, 0);
            h.Release();
        }, 0, null),
    ];

    public static TheoryData<string> Table => new(Interactions.Select(i => i.Name));

    [Theory]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-090")]
    [Trait("Req", "DSG-SUR-170")]
    [Trait("Req", "DSG-SUR-172")]
    [MemberData(nameof(Table))]
    public void OneUnitPerInteraction(string name)
    {
        var interaction = Interactions.Single(i => i.Name == name);
        using var h = DesignerHarness.FromText(Form);
        h.Settings.NoSnapping();
        var original = h.Text;

        interaction.Run(h);

        Assert.Equal(interaction.Units, h.Undo.UndoCount);
        Assert.Equal(interaction.Title, h.Undo.UndoTitleKey);
        if (interaction.Units == 0)
        {
            Assert.Equal(original, h.Text);
            return;
        }

        // Undoing the units restores the text exactly; redoing them gives the edited text again.
        var edited = h.Text;
        Assert.NotEqual(original, edited);
        for (var i = 0; i < interaction.Units; i++)
        {
            h.ExecuteUndo();
            h.AssertInvariant();
        }

        Assert.Equal(original, h.Text);
        for (var i = 0; i < interaction.Units; i++)
        {
            h.ExecuteRedo();
            h.AssertInvariant();
        }

        Assert.Equal(edited, h.Text);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-092")]
    [Trait("Req", "DSG-SUR-170")]
    public void UndoRestoresSelection()
    {
        using var h = DesignerHarness.FromText(Form);
        h.Settings.NoSnapping();

        // A multi-selection move: undo and redo restore the selection (with its primary) whatever it is now.
        h.Select("B2", "B1");
        Drag(h, "B1", 16, 0);
        h.Click("E1");
        h.ExecuteUndo();
        Assert.Equal(["B2", "B1"], h.Selected);
        h.Click("E1");
        h.ExecuteRedo();
        Assert.Equal(["B2", "B1"], h.Selected);

        // Paste: before = the copied components, after = the copies.
        h.Select("B1");
        h.Execute(Commands.Copy);
        h.Execute(Commands.Paste);
        var copy = Assert.Single(h.Selected);
        Assert.NotEqual("B1", copy);
        h.ExecuteUndo();
        Assert.Equal(["B1"], h.Selected);
        h.ExecuteRedo();
        Assert.Equal([copy], h.Selected);
        h.ExecuteUndo();

        // Delete: before = the deleted components (they exist again after undo), after = the parent.
        h.Select("Top1");
        h.Key(DesignKey.Delete);
        Assert.Equal(["P1"], h.Selected);
        h.Click("E1");
        h.ExecuteUndo();
        Assert.Equal(["Top1"], h.Selected);
        h.ExecuteRedo();
        Assert.Equal(["P1"], h.Selected);
        h.ExecuteUndo();

        // Placement: before = the old selection, after = the new component.
        h.Select("E1");
        h.Host.Arm("TButton");
        h.ClickAt(h.Controller.Geometry.FormClientToSurface((300, 20)));
        Assert.Equal(["Button1"], h.Selected);
        h.ExecuteUndo();
        Assert.Equal(["E1"], h.Selected);
        h.ExecuteRedo();
        Assert.Equal(["Button1"], h.Selected);
        h.ExecuteUndo();

        // Tab-order clicks: undo leaves the mode and restores the selection of the unit (the form).
        h.Select("B1");
        h.Execute(Commands.TabOrder);
        h.Click("E1");
        h.ExecuteUndo();
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        Assert.Equal(["F"], h.Selected);

        // Names that no longer exist are skipped; nothing left selects the form (units from other sources, e.g. a rename).
        h.Undo.Push(new DesignerUndoUnit(UndoTitles.Move, [], [], [], new(["Gone", "B1"], "B1"), new(["Gone"], "Gone")));
        h.Click("E1");
        h.ExecuteUndo();
        Assert.Equal(["B1"], h.Selected);
        h.ExecuteRedo();
        Assert.Equal(["F"], h.Selected);
    }
}
