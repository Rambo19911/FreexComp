using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// I-284 (found by I-281 in the real IDE): an open context menu is used with the keyboard — Down / Up move between its
/// items, Enter runs the item, Escape closes it and gives the focus back (menus.md, UI-A11Y-002). In the real IDE a
/// context menu is a popup window of its own (another top level); the main window's focus rule must not take the focus
/// back from it. The headless platform shows popups in the window's overlay layer, so the rule is also tested on its
/// own with an element in another top level that belongs to the main window's logical tree, as a popup's content does.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class ContextMenuKeyboardTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Task", "I-284")]
    [Trait("Req", "UI-A11Y-002")]
    [Trait("Req", "UI-MENU-043")]
    public async Task OpenContextMenu_ArrowsEnterAndEscapeWork()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "unit A;\ninterface\nimplementation\nbegin\nend.\n");
        await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "A.pas"));
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.A.pas");
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        string? Focused() => h.Window.FocusManager?.GetFocusedElement() is Control c ? AutomationIds.Get(c) ?? c.GetType().Name : null;

        // Shift+F10 opens the menu; the focus is in it after the queued focus checks ran; Down / Up move through it.
        h.Press("Shift+F10");
        Dispatcher.UIThread.RunJobs();
        var menu = editor.TextArea.ContextMenu!;
        Assert.True(menu.IsOpen);
        h.Press("Down");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Context.edit.cut", Focused());
        h.Press("Down");
        h.Press("Down");
        h.Press("Up");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Context.edit.copy", Focused());
        Assert.True(menu.IsOpen);

        // Escape closes it and the editor has the focus again.
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.False(menu.IsOpen);
        Assert.True(editor.TextArea.IsKeyboardFocusWithin);

        // Enter runs the item: Select All.
        h.Press("Shift+F10");
        Dispatcher.UIThread.RunJobs();
        menu = editor.TextArea.ContextMenu!; // built anew for each opening
        Assert.True(menu.IsOpen);
        for (var i = 0; i < 10 && Focused() != "Context.edit.selectAll"; i++)
        {
            h.Press("Down");
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal("Context.edit.selectAll", Focused());
        h.Press("Enter");
        Dispatcher.UIThread.RunJobs();
        Assert.False(menu.IsOpen);
        Assert.Equal(editor.Document.TextLength, editor.TextArea.Selection.Length);
        Assert.True(editor.TextArea.IsKeyboardFocusWithin);
    }

    /// <summary>
    /// The focus rule of the main window (<c>MainWindow.FocusLeft</c>, UI-A11Y-013): focus that went nowhere or to another
    /// window has left; focus in this window, or in another top level whose content belongs to this window's logical tree —
    /// an open popup such as a context menu — has not, so the window does not take it back.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Task", "I-284")]
    [Trait("Req", "UI-A11Y-013")]
    public async Task FocusRule_FocusInAPopupOfTheWindowHasNotLeft()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var inWindow = h.Find<Control>(AutomationIds.StatusBar);
        Assert.True(MainWindow.FocusLeft(h.Window, null));
        Assert.False(MainWindow.FocusLeft(h.Window, inWindow));

        // Another top level of its own: focus there has left the window.
        var elsewhere = new Button { Content = "elsewhere" };
        var other = new Window { Content = elsewhere };
        other.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.NotSame(h.Window, TopLevel.GetTopLevel(elsewhere));
            Assert.True(MainWindow.FocusLeft(h.Window, elsewhere));

            // The same top level as a popup of the window: its logical parent is in the window (a popup's content has the
            // popup, and so the control that opened it, as its logical ancestor).
            ((ISetLogicalParent)other).SetParent(inWindow);
            Assert.Contains(h.Window, elsewhere.GetLogicalAncestors());
            Assert.NotSame(h.Window, TopLevel.GetTopLevel(elsewhere));
            Assert.False(MainWindow.FocusLeft(h.Window, elsewhere));
        }
        finally
        {
            ((ISetLogicalParent)other).SetParent(null);
            other.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
