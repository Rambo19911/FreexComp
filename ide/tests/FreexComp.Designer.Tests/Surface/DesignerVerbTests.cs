using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Designer verbs (design-surface.md §11.6, DSG-SUR-190 … 196; D-097) on the test metadata.</summary>
[Trait("Phase", "5")]
public sealed class DesignerVerbTests
{
    internal const string Text = """
        object Form1: TForm
          ClientWidth = 400
          ClientHeight = 300
          object Bar: TToolBar
            Left = 0
            Top = 0
            Width = 400
            Height = 29
            TabOrder = 0
            object Tb1: TToolButton
              Width = 23
            end
          end
          object Book: TPageControl
            Left = 8
            Top = 40
            Width = 289
            Height = 193
            TabOrder = 1
            object S1: TTabSheet
              Caption = 'First'
            end
            object S2: TTabSheet
              Caption = 'Second'
            end
            object S3: TTabSheet
              Caption = 'Third'
            end
          end
          object Btn: TButton
            Left = 304
            Top = 40
            Width = 75
            Height = 25
            Caption = 'Btn'
            TabOrder = 2
          end
          object Empty: TPageControl
            Left = 8
            Top = 240
            Width = 120
            Height = 50
            TabOrder = 3
          end
          object MainMenu1: TMainMenu
            DesignLeft = 8
            DesignTop = 8
            object FileMenu: TMenuItem
              Caption = '&File'
              object FileExit: TMenuItem
                Caption = 'E&xit'
              end
            end
          end
          object ActionList1: TActionList
            DesignLeft = 48
            DesignTop = 8
            object Action1: TAction
              Caption = 'Act'
            end
          end
        end

        """;

    private static IReadOnlyList<string> VerbIds(DesignerHarness h) => [.. h.Controller.Verbs.Select(v => v.Id)];

    private static string? VerbComponent(DesignerHarness h) => h.Controller.VerbComponent?.Name;

    /// <summary>The block of one object in the form text (from its <c>object</c> line to its <c>end</c>).</summary>
    private static string Block(DesignerHarness h, string name)
    {
        var text = h.Text;
        var start = text.IndexOf("object " + name + ":", StringComparison.Ordinal);
        Assert.True(start >= 0, name + " not in\n" + text);
        var indent = start - text.LastIndexOf('\n', start) - 1;
        var end = text.IndexOf("\n" + new string(' ', indent) + "end\n", start, StringComparison.Ordinal);
        return text[start..end];
    }

    [Fact]
    [Trait("Req", "DSG-SUR-190")]
    [Trait("Req", "DSG-SUR-191")]
    public void VerbComponentIsSelectionOrOwner()
    {
        using var h = DesignerHarness.FromText(Text);

        // The selected component's own verbs, in metadata order.
        h.Select("Book");
        Assert.Equal("Book", VerbComponent(h));
        Assert.Equal(["newPage", "deletePage", "nextPage", "previousPage"], VerbIds(h));

        // A child whose class has parentClasses offers its owner's verbs (tab sheet, tool button, menu item, action).
        h.Select("S2");
        Assert.Equal("Book", VerbComponent(h));
        h.Select("Tb1");
        Assert.Equal("Bar", VerbComponent(h));
        Assert.Equal(["newButton", "newSeparator"], VerbIds(h));
        h.Select("FileMenu");
        Assert.Equal("MainMenu1", VerbComponent(h));
        Assert.Equal(["menuDesigner"], VerbIds(h));
        h.Select("FileExit");
        Assert.Equal("MainMenu1", VerbComponent(h)); // a sub-item: the menu that owns its parent item
        h.Select("Action1");
        Assert.Equal("ActionList1", VerbComponent(h));
        Assert.Equal(["actionListEditor"], VerbIds(h));

        // No verbs: a class without them, the form, a multi-selection.
        h.Select("Btn");
        Assert.Null(VerbComponent(h));
        Assert.Empty(VerbIds(h));
        h.Select("Form1");
        Assert.Empty(VerbIds(h));
        h.Select("Book", "Btn");
        Assert.Null(VerbComponent(h));
        Assert.False(h.Controller.CanExecute(Commands.NewChild, "TTabSheet"));

        // Enabled: the command's own rule; a read-only form disables every verb (they stay listed).
        h.Select("S1");
        Assert.True(h.Controller.CanExecute(Commands.NewChild, "TTabSheet"));
        Assert.False(h.Controller.CanExecute(Commands.NewChild, "TButton")); // childClasses forbid it
        Assert.False(h.Controller.CanExecute(Commands.NewChild, "TNoSuchClass"));
        Assert.False(h.Controller.CanExecute(Commands.NewChild, null));
        Assert.True(h.Controller.CanExecute(Commands.DeletePage));
        Assert.True(h.Controller.CanExecute(Commands.NextPage));
        Assert.True(h.Controller.CanExecute(Commands.PreviousPage));
        Assert.False(h.Controller.CanExecute(Commands.EditComponent));
        h.Controller.IsReadOnly = true;
        Assert.Equal(["newPage", "deletePage", "nextPage", "previousPage"], VerbIds(h));
        Assert.False(h.Controller.CanExecute(Commands.NewChild, "TTabSheet"));
        Assert.False(h.Controller.CanExecute(Commands.DeletePage));
        Assert.False(h.Controller.CanExecute(Commands.NextPage));
        h.Controller.IsReadOnly = false;
        h.Select("Tb1");
        Assert.False(h.Controller.CanExecute(Commands.DeletePage)); // not a page control
        Assert.True(h.Controller.CanExecute(Commands.NewChild, "TToolButton;Style=tbsSeparator;Width=8"));
        h.Select("MainMenu1");
        Assert.True(h.Controller.CanExecute(Commands.EditComponent));
        Assert.False(h.Controller.CanExecute(Commands.EditCollection, "Panels"));
        Assert.Equal(0, h.Undo.UndoCount);
    }

    [Fact]
    [Trait("Req", "DSG-SUR-192")]
    [Trait("Req", "DSG-RND-017")]
    public void NewPageCreatesActiveTabSheetWithField()
    {
        using var h = DesignerHarness.FromText(Text);
        h.Select("S2");
        Assert.True(h.Layout.GetVisible(h.Object("S1")));

        h.Execute(Commands.NewChild, "TTabSheet");

        // The last child of the page control, named per DSG-TP-050, the only selection, the active page.
        Assert.Equal(["S1", "S2", "S3", "TabSheet1"], h.ChildNames("Book"));
        Assert.Equal(["TabSheet1"], h.Selected);
        Assert.True(h.Layout.GetVisible(h.Object("TabSheet1")));
        Assert.False(h.Layout.GetVisible(h.Object("S1")));
        Assert.Contains("    ActivePage = TabSheet1\n", h.Text, StringComparison.Ordinal);

        // No geometry lines (stored: false); the metadata initial value; the field through code sync.
        Assert.Equal("object TabSheet1: TTabSheet\n      Caption = 'TabSheet1'", Block(h, "TabSheet1"));
        var fields = Assert.Single(h.Host.CodeSync.AddedFields);
        Assert.Equal([new CodeSyncField("TabSheet1", "TTabSheet", "ComCtrls")], fields);

        // One unit "Add TTabSheet"; undo removes the sheet and the ActivePage line.
        Assert.Equal((1, UndoTitles.Add, "TTabSheet"), (h.Undo.UndoCount, h.Undo.UndoTitleKey, h.Undo.Peek!.TitleArgument));
        h.ExecuteUndo();
        Assert.Equal(["S1", "S2", "S3"], h.ChildNames("Book"));
        Assert.DoesNotContain("ActivePage", h.Text, StringComparison.Ordinal);
        h.ExecuteRedo();
        Assert.Contains("    ActivePage = TabSheet1\n", h.Text, StringComparison.Ordinal);

        // The first page of an empty page control is the default page: ActivePage is not stored.
        h.Select("Empty");
        h.Execute(Commands.NewChild, "TTabSheet");
        Assert.Equal(["TabSheet2"], h.ChildNames("Empty"));
        Assert.True(h.Layout.GetVisible(h.Object("TabSheet2")));
        Assert.DoesNotContain("ActivePage = TabSheet2", h.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "DSG-SUR-193")]
    public void DeletePageActivatesNeighbour()
    {
        using var h = DesignerHarness.FromText(Text.Replace("    TabOrder = 1\n    object S1", "    ActivePage = S2\n    TabOrder = 1\n    object S1", StringComparison.Ordinal));
        h.Select("S1"); // the verb component is the page control; the design-active page (S2) is deleted
        h.Execute(Commands.DeletePage);
        Assert.Equal(["S1", "S3"], h.ChildNames("Book"));
        Assert.Equal(["Book"], h.Selected);
        Assert.True(h.Layout.GetVisible(h.Object("S3"))); // the following page
        Assert.Contains("    ActivePage = S3\n", h.Text, StringComparison.Ordinal);
        Assert.Equal((1, UndoTitles.Delete), (h.Undo.UndoCount, h.Undo.UndoTitleKey));
        Assert.Equal(["S2"], Assert.Single(h.Host.CodeSync.RemovedFields));

        // The last page: the preceding one, here the default page (no ActivePage line).
        h.Execute(Commands.DeletePage);
        Assert.Equal(["S1"], h.ChildNames("Book"));
        Assert.True(h.Layout.GetVisible(h.Object("S1")));
        Assert.DoesNotContain("ActivePage", h.Text, StringComparison.Ordinal);

        // Undo restores the page and the stored ActivePage.
        h.ExecuteUndo();
        Assert.Equal(["S1", "S3"], h.ChildNames("Book"));
        Assert.Contains("    ActivePage = S3\n", h.Text, StringComparison.Ordinal);
        h.ExecuteRedo();

        // The only page can be deleted; without pages the command is disabled.
        h.Execute(Commands.DeletePage);
        Assert.Empty(h.ChildNames("Book"));
        Assert.False(h.Controller.CanExecute(Commands.DeletePage));
        Assert.Equal(3, h.Undo.UndoCount); // S2, S3 (undone and redone), S1
    }

    [Fact]
    [Trait("Req", "DSG-SUR-194")]
    public void NextPreviousPageWrapAndStoreActivePage()
    {
        using var h = DesignerHarness.FromText(Text);
        h.Select("Book");
        h.Execute(Commands.NextPage);
        Assert.Equal(["S2"], h.Selected);
        Assert.True(h.Layout.GetVisible(h.Object("S2")));
        Assert.Contains("    ActivePage = S2\n", h.Text, StringComparison.Ordinal);
        Assert.Equal((1, UndoTitles.Property), (h.Undo.UndoCount, h.Undo.UndoTitleKey));

        h.Execute(Commands.NextPage);
        Assert.Contains("    ActivePage = S3\n", h.Text, StringComparison.Ordinal);

        // Wraps around to the first page, which is the default: the line is removed.
        h.Execute(Commands.NextPage);
        Assert.Equal(["S1"], h.Selected);
        Assert.True(h.Layout.GetVisible(h.Object("S1")));
        Assert.DoesNotContain("ActivePage", h.Text, StringComparison.Ordinal);

        h.Execute(Commands.PreviousPage);
        Assert.Equal(["S3"], h.Selected);
        Assert.Contains("    ActivePage = S3\n", h.Text, StringComparison.Ordinal);
        Assert.Equal(4, h.Undo.UndoCount);

        h.ExecuteUndo();
        Assert.DoesNotContain("ActivePage", h.Text, StringComparison.Ordinal);
        Assert.True(h.Layout.GetVisible(h.Object("S1")));

        // A page control with fewer than two pages has neither command.
        h.Select("Empty");
        Assert.False(h.Controller.CanExecute(Commands.NextPage));
        Assert.False(h.Controller.CanExecute(Commands.PreviousPage));
    }

    [Fact]
    [Trait("Req", "DSG-SUR-192")]
    public void NewButtonAndSeparatorAreFlowed()
    {
        using var h = DesignerHarness.FromText(Text);
        h.Select("Tb1");
        h.Execute(Commands.NewChild, "TToolButton");
        Assert.Equal(["Tb1", "ToolButton1"], h.ChildNames("Bar"));
        Assert.Equal(["ToolButton1"], h.Selected);
        Assert.Equal(new Rect(23, 2, 23, 22), h.Bounds("ToolButton1")); // flowed after Tb1 (UIF-800)
        Assert.Equal("object ToolButton1: TToolButton\n      Width = 23", Block(h, "ToolButton1"));

        // The arg's property pairs are stored after the geometry; the separator is flowed with its own width.
        h.Execute(Commands.NewChild, "TToolButton;Style=tbsSeparator;Width=8");
        Assert.Equal(["Tb1", "ToolButton1", "ToolButton2"], h.ChildNames("Bar"));
        Assert.Equal(new Rect(46, 2, 8, 22), h.Bounds("ToolButton2"));
        Assert.Equal("object ToolButton2: TToolButton\n      Width = 8\n      Style = tbsSeparator", Block(h, "ToolButton2"));
        Assert.Equal((2, UndoTitles.Add, "TToolButton"), (h.Undo.UndoCount, h.Undo.UndoTitleKey, h.Undo.Peek!.TitleArgument));
        Assert.Equal(2, h.Host.CodeSync.AddedFields.Count);

        // An unparsable pair disables the command.
        Assert.False(h.Controller.CanExecute(Commands.NewChild, "TToolButton;Style"));
        Assert.False(h.Controller.CanExecute(Commands.NewChild, "TToolButton;Width='x"));
    }

    [Fact]
    [Trait("Req", "DSG-SUR-195")]
    [Trait("Req", "DSG-SUR-196")]
    public void EditCollectionAndComponentVerbsAskTheHost()
    {
        using var h = DesignerHarness.FromText(Text.Replace("  object Btn: TButton", "  object Status: TStatusBar\n    Left = 0\n    Top = 281\n    Width = 400\n    Height = 19\n  end\n  object Btn: TButton", StringComparison.Ordinal));
        h.Select("Status");
        Assert.Equal(["editPanels"], VerbIds(h));
        Assert.True(h.Controller.CanExecute(Commands.EditCollection, "Panels"));
        Assert.False(h.Controller.CanExecute(Commands.EditCollection, "Columns"));
        h.Execute(Commands.EditCollection, "Panels");
        Assert.Equal([("Status", "Panels")], h.Host.CollectionEdits);

        // editComponent opens the class's component editor; Enter on a component with one runs it too.
        h.Select("FileMenu");
        h.Execute(Commands.EditComponent);
        h.Select("MainMenu1");
        Assert.True(h.Key(DesignKey.Enter));
        Assert.Equal(["MainMenu1", "MainMenu1"], h.Host.ComponentEdits);
        Assert.Empty(h.Host.CodeSync.CreatedHandlers);
        Assert.Equal(0, h.Undo.UndoCount);
    }
}
