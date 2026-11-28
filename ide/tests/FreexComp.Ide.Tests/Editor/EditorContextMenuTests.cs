using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>The editor text-area context menu (menus.md §12).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class EditorContextMenuTests
{
    /// <param name="phase">The implemented phase of the composition; null = the product's.</param>
    private static async Task<(HeadlessHarness H, TextEditor Editor, TextDocumentViewModel Doc)> OpenAsync(int? phase = null)
    {
        var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = "unit A;\ninterface\nimplementation\nbegin\nend.\n";
            o.ImplementedPhase = phase;
        });
        var doc = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "A.pas"))).Value);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.A.pas");
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        return (h, editor, doc);
    }

    /// <summary>The window point of (line, column) in the text.</summary>
    private static Point At(HeadlessHarness h, TextEditor editor, int line, int column)
    {
        var view = editor.TextArea.TextView;
        var p = view.GetVisualPosition(new TextViewPosition(line, column), VisualYPosition.LineMiddle) - view.ScrollOffset;
        return view.TranslatePoint(p, h.Window)!.Value;
    }

    private static void RightClick(HeadlessHarness h, Point point)
    {
        h.Window.MouseDown(point, MouseButton.Right);
        h.Window.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }

    private static MenuItem? MainMenuItem(HeadlessHarness h, string commandId)
    {
        var bar = h.Find<Menu>(AutomationIds.MenuBar);
        var item = ((Avalonia.LogicalTree.ILogical)bar).GetLogicalDescendants().OfType<MenuItem>()
            .FirstOrDefault(i => AutomationIds.Get(i) == AutomationIds.Menu(commandId))
            ;
        if (item is null)
            return null; // a context-only command (file.showInExplorer, file.copyPath): no main-menu item to compare
        // Menu-item state is refreshed when its menu opens (phase1-shell.md lead note): open the chain, read, close.
        var chain = ((Avalonia.LogicalTree.ILogical)item).GetLogicalAncestors().OfType<MenuItem>().Reverse().ToList();
        foreach (var parent in chain)
        {
            parent.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
        }

        return item;
    }

    private static void CloseMenus(HeadlessHarness h)
    {
        h.Find<Menu>(AutomationIds.MenuBar).Close();
        Dispatcher.UIThread.RunJobs();
    }
    /// <summary>
    /// UI-MENU-040 / UI-MENU-041: the items come from the main menu's command objects — ids <c>Context.&lt;commandId&gt;</c>,
    /// the main-menu label, shortcut and enabled state; later-phase items are left out (UI-MENU-042).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-040")]
    [Trait("Req", "UI-MENU-041")]
    [Trait("Req", "UI-MENU-042")]
    public async Task ItemsMirrorTheMainMenu()
    {
        var (h, editor, _) = await OpenAsync(phase: 5); // the items up to Phase 5; the Phase 6 items (breakpoints, Run to Cursor, watches) are left out
        await using var _ = h;

        RightClick(h, At(h, editor, 1, 2));
        var menu = Assert.IsType<ContextMenu>(editor.TextArea.ContextMenu);
        Assert.True(menu.IsOpen);
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(
            ["Context.edit.cut", "Context.edit.copy", "Context.edit.paste", "Context.edit.selectAll", "Context.search.goToDeclaration",
             "Context.edit.toggleLineComment", "Context.edit.insertTemplate", "Context.file.showInExplorer", "Context.file.copyPath"], // Phase 3 items (UI-MENU-042)
            items.Select(AutomationIds.Get));
        Assert.Equal(3, menu.Items.OfType<Separator>().Count()); // the separators of the omitted groups collapse
        var snapshot = items.Select(i => (Id: AutomationIds.Get(i)!, Header: i.Header, Gesture: i.InputGesture?.ToString(), Enabled: i.Command?.CanExecute(i.CommandParameter))).ToList();
        menu.Close();
        Dispatcher.UIThread.RunJobs();

        foreach (var (id, header, gesture, enabled) in snapshot)
        {
            if (MainMenuItem(h, id["Context.".Length..]) is not { } main)
                continue;
            Assert.Equal(main.Header, header);
            Assert.Equal(main.InputGesture?.ToString(), gesture);
            Assert.Equal(main.Command?.CanExecute(main.CommandParameter), enabled);
            CloseMenus(h);
        }
    }

    /// <summary>
    /// UI-MENU-043: Shift+F10 opens the menu at the caret (a right-click at the pointer) — below the caret line with its
    /// left edge at the caret, as the quick-action menu (lead decision 2026-11-28, I-352).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-043")]
    public async Task KeyboardOpensAtTheCaret()
    {
        var (h, editor, doc) = await OpenAsync();
        await using var _ = h;
        doc.ViewState.Caret = new Core.Text.TextPosition(4, 3);
        Dispatcher.UIThread.RunJobs();

        // Shift+F10 (the Menu key takes the same path; the headless platform does not deliver Key.Apps to the window).
        foreach (var gesture in new[] { "Shift+F10" })
        {
            editor.TextArea.Focus();
            Dispatcher.UIThread.RunJobs();
            h.Press(gesture);

            var menu = Assert.IsType<ContextMenu>(editor.TextArea.ContextMenu);
            Assert.True(menu.IsOpen, gesture);
            Assert.Equal(Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft, menu.Placement);
            Assert.Same(editor.TextArea.TextView, menu.PlacementTarget);
            var caret = editor.TextArea.Caret.CalculateCaretRectangle();
            Assert.Equal(caret.X - editor.TextArea.TextView.ScrollOffset.X, menu.PlacementRect!.Value.X, 0.5);
            Assert.Equal(caret.Y - editor.TextArea.TextView.ScrollOffset.Y, menu.PlacementRect!.Value.Y, 0.5);
            Assert.Equal(new Core.Text.TextPosition(4, 3), doc.ViewState.Caret); // the caret did not move
            menu.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>UI-MENU-044: a right-click inside the selection keeps it; outside it moves the caret there and clears the selection.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-044")]
    public async Task RightClickKeepsOrMovesTheSelection()
    {
        var (h, editor, doc) = await OpenAsync();
        await using var _ = h;
        var area = editor.TextArea;
        area.Selection = AvaloniaEdit.Editing.Selection.Create(area, 0, 6); // "unit A"
        Dispatcher.UIThread.RunJobs();

        RightClick(h, At(h, editor, 1, 3)); // inside
        Assert.Equal("unit A", area.Selection.GetText());
        area.ContextMenu!.Close();
        Dispatcher.UIThread.RunJobs();

        RightClick(h, At(h, editor, 3, 5)); // outside
        Assert.True(area.Selection.IsEmpty);
        Assert.Equal(new Core.Text.TextPosition(3, 5), doc.ViewState.Caret);
        Assert.True(area.ContextMenu!.IsOpen);
        area.ContextMenu.Close();
    }
}
