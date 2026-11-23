using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using DesignCommandIds = FreexComp.Designer.Surface.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>Designer verbs through the IDE's commands (design-surface.md DSG-SUR-191, 195, 196; UI-DSG-082; D-097).</summary>
[Trait("Phase", "5")]
public sealed class DesignerVerbTests
{
    /// <summary>The standard unit with a status bar (one panel) and a main menu.</summary>
    internal static readonly string Unit = DesignerFixture.Unit
        .Replace("    Panel1: TPanel;", "    Panel1: TPanel;\r\n    StatusBar1: TStatusBar;\r\n    MainMenu1: TMainMenu;", StringComparison.Ordinal)
        .Replace("StdCtrls, ExtCtrls;", "StdCtrls, ExtCtrls, ComCtrls, Menus;", StringComparison.Ordinal);

    internal static readonly string Form = DesignerFixture.Form[..DesignerFixture.Form.TrimEnd().LastIndexOf("end", StringComparison.Ordinal)]
        + "  object StatusBar1: TStatusBar\n    Left = 0\n    Top = 281\n    Width = 400\n    Height = 19\n    Panels = <\n      item\n        Text = 'Ready'\n      end\n    >\n  end\n"
        + "  object MainMenu1: TMainMenu\n    DesignLeft = 8\n    DesignTop = 8\n  end\nend\n";

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-195")]
    [Trait("Req", "DSG-SUR-191")]
    [Trait("Req", "UI-DSG-082")]
    public Task EditCollectionVerbOpensEditor() => TestUiThread.RunAsync(EditCollectionVerbOpensEditorOnUiThread);

    private static async Task EditCollectionVerbOpensEditorOnUiThread()
    {
        var dialogs = new FakeDialogService();
        var f = new DesignerFixture(o => o.Dialogs = dialogs);
        var designer = (await f.OpenAsync(Unit, Form)).Designer;
        var commands = f.Services.Commands;
        DesignerFixture.Select(designer, "StatusBar1");

        // UI-DSG-082: the verb group first (metadata caption, the verb's command and argument), then a separator.
        var menu = designer.ContextMenu!();
        Assert.Equal("editPanels", menu[0].VerbId);
        Assert.Equal("designer.editCollection", menu[0].CommandId);
        Assert.Equal("Panels", menu[0].Argument);
        Assert.True(menu[0].IsEnabled);
        Assert.True(menu[1].IsSeparator);
        Assert.Equal("edit.cut", menu[2].CommandId);
        Assert.False(commands.CanExecute(DesignCommandIds.EditCollection, "Width")); // not a collection property

        // The command opens the collection editor; OK with a change is one "Property" unit.
        dialogs.Script(CollectionEditorViewModel.DialogName, async dialog =>
        {
            var editor = Assert.IsType<CollectionEditorViewModel>(dialog);
            Assert.Equal("Edit StatusBar1.Panels", editor.Title);
            editor.Add();
            await editor.PressDefaultAsync();
        });
        await commands.ExecuteAsync(DesignCommandIds.EditCollection, "Panels");
        Assert.Equal(2, ((CollectionValue)DesignerFixture.Object(designer, "StatusBar1").FindProperty("Panels")!.Value).Items.Length);
        Assert.Equal((1, UndoTitles.Property), (designer.Undo.UndoCount, designer.Undo.UndoTitleKey));
        Assert.Null(designer.PendingEditor);

        // Cancel applies nothing.
        dialogs.Script(CollectionEditorViewModel.DialogName, dialog => dialog.PressCancelAsync());
        await commands.ExecuteAsync(DesignCommandIds.EditCollection, "Panels");
        Assert.Equal(1, designer.Undo.UndoCount);

        // A component without verbs: no verb group.
        DesignerFixture.Select(designer, "GreetButton");
        Assert.Equal("edit.cut", designer.ContextMenu!()[0].CommandId);
        Assert.False(commands.CanExecute(DesignCommandIds.EditCollection, "Panels"));

        // The menu's component editor is the Menu Designer (DSG-SUR-196), also opened by Enter on the tray tile.
        DesignerFixture.Select(designer, "MainMenu1");
        Assert.Equal("menuDesigner", designer.ContextMenu!()[0].VerbId);
        var opened = 0;
        dialogs.Script(MenuDesignerViewModel.DialogName, dialog =>
        {
            opened++;
            Assert.Equal("Menu Designer – MainMenu1", dialog.Title);
            return dialog.PressCancelAsync();
        });
        await commands.ExecuteAsync(DesignCommandIds.EditComponent);
        Assert.Equal(1, opened);
        Assert.Equal(1, designer.Undo.UndoCount); // Close only closes (DSG-SUR-199)
    }
}
