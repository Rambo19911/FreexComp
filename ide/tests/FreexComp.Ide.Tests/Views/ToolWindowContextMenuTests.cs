using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The context menus of the list tool windows belong to the right-clicked row whatever had the keyboard focus before
/// (UI-TW-014; task I-283, the check that I-281 made for the Project Manager). The Locals case runs a debug session over
/// the scripted adapter, hence the traits and the timeout of the debug-session tests.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class ToolWindowContextMenuTests
{
    private const string Unit = "unit Unit1;\r\ninterface\r\nimplementation\r\nvar Caption: string;\r\nbegin\r\n  Caption := Caption + 'x';\r\nend.\r\n";

    /// <summary>The item <c>Context.&lt;command&gt;</c> of the context menu that is open now.</summary>
    private static MenuItem OpenItem(List<ContextMenu> opened, string command)
    {
        var menu = Assert.Single(opened, m => m.IsOpen);
        return menu.Items.OfType<MenuItem>().Single(i => AutomationIds.Get(i) == "Context." + command);
    }

    private static void Click(MenuItem item)
    {
        Assert.True(item.IsEffectivelyEnabled, $"{AutomationIds.Get(item)} is disabled");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The editor of <paramref name="path"/> gets the focus, its caret in the first word of the file; no tool window has it.</summary>
    private static async Task<TextEditor> FocusEditorAsync(HeadlessHarness h, string path)
    {
        var (_, editor, _) = await DebugScenario.OpenAsync(h, path);
        editor.TextArea.Caret.Offset = 1;
        editor.TextArea.ClearSelection();
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        var id = "Editor." + Path.GetFileName(path);
        await h.WaitForAsync(() => h.FocusedAutomationId == id, "editor focused");
        Assert.Equal(CommandScope.Editor, h.Services.FocusTracker.Current.Scope);
        Assert.Null(h.Services.FocusTracker.Current.ToolWindowName);
        return editor;
    }

    private static void Show(HeadlessHarness h, string tool)
    {
        h.Services.Layout.Layout.BottomSize = 400; // room for the rows of the test
        Assert.True(h.Services.ToolWindows.Show(tool, focus: false));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// I-283, UI-TW-014: with the focus in the editor — the tool window never had it — a right click on a row that is
    /// not selected opens the menu of that row, and its command acts on that row:
    /// <list type="bullet">
    /// <item>Breakpoints — <c>Enable/Disable Breakpoint</c> toggles the breakpoint of the row, not the caret line's;</item>
    /// <item>Watches — <c>Edit Watch</c> edits the row in place (the window has the focus), not through the dialog;</item>
    /// <item>Locals — <c>Add Watch</c> adds the variable of the row, not the word at the editor's caret;</item>
    /// <item>Messages — <c>Copy</c> copies the message of the row;</item>
    /// <item>Search Results — <c>Copy</c> copies the match of the row, not the editor's text.</item>
    /// </list>
    /// The right click gives the tool window the focus, as a left click does.
    /// </summary>
    [AvaloniaTheory(Timeout = TestDeadline.DebugTestTimeout)]
    [InlineData("Breakpoints")]
    [InlineData("Watches")]
    [InlineData("Locals")]
    [InlineData("Messages")]
    [InlineData("SearchResults")]
    [Trait("Req", "UI-TW-014")]
    public async Task ToolWindowContextMenus_ActOnTheRightClickedRowWithoutPriorFocus(string tool)
    {
        var opened = new List<ContextMenu>();
        using var subscription = MenuBase.OpenedEvent.AddClassHandler<ContextMenu>((menu, _) => opened.Add(menu), handledEventsToo: true);
        await using var h = tool == "Locals"
            ? await DebugScenario.StartDemoAsync()
            : await DebugScenario.StartAsync(o => o.InitialFiles[@"Hello\Unit1.pas"] = Unit);
        var clipboard = (FakeClipboard)h.Services.Clipboard;
        var unit = DebugScenario.UnitPath(h);
        var debug = h.Services.Debug!;
        var shell = h.Services.DebugShell!;

        // The right click gave the tool window the focus: it is where the commands of the menu look for their rows.
        void AssertHasFocus() => Assert.Equal(tool, h.Services.FocusTracker.Current.ToolWindowName);

        switch (tool)
        {
            case "Breakpoints":
            {
                var breakpoints = h.Services.Breakpoints!;
                breakpoints.Toggle(unit, 5);
                breakpoints.Toggle(unit, 6);
                Show(h, BreakpointsViewModel.ToolName);
                await FocusEditorAsync(h, unit); // the caret is in line 1: no breakpoint there
                Assert.Empty(shell.Breakpoints.SelectedRows);

                h.RightClick("Tool.Breakpoints.Row.Unit1.pas:6.Location");
                AssertHasFocus();
                Assert.Equal(["Unit1.pas:6"], shell.Breakpoints.SelectedRows.Select(r => r.Key));
                Click(OpenItem(opened, "run.toggleBreakpointEnabled"));
                Assert.Equal([(5, true), (6, false)], breakpoints.All.Select(b => (b.Line, b.Enabled)));
                break;
            }

            case "Watches":
            {
                debug.Watches.Add("Alpha");
                debug.Watches.Add("Beta");
                Show(h, WatchesViewModel.ToolName);
                await FocusEditorAsync(h, unit);
                Assert.Empty(shell.Watches.SelectedRows);

                h.RightClick("Tool.Watches.Row.1.Expression");
                AssertHasFocus();
                Assert.Equal(["1"], shell.Watches.SelectedRows.Select(r => r.Key));
                Click(OpenItem(opened, "watches.edit"));
                Assert.Single(h.AllWindows); // no Edit Watch dialog: the row is edited in place
                Assert.Equal(("1", "Expression", "Beta"), (shell.Watches.Edit?.RowKey, shell.Watches.Edit?.Column, shell.Watches.Edit?.Text));
                h.Press("Escape");
                Dispatcher.UIThread.RunJobs();
                Assert.Null(shell.Watches.Edit);
                Assert.Equal(["Alpha", "Beta"], debug.Watches.Expressions);
                break;
            }

            case "Locals":
            {
                var demo = DebugScenario.DemoPath(h);
                h.DebugSessions.UseScenario($$"""
                    { {{DebugDemo.Data()}}, "steps": [
                      {{DebugDemo.FirstStep(demo)}},
                      {{DebugDemo.AtStop(demo)}},
                      {{DebugDemo.Scopes}} ] }
                    """);
                h.Press("F11");
                await DebugScenario.WaitForStopAsync(h, DebugDemo.StopLine);
                Show(h, LocalsViewModel.ToolName);
                await DebugScenario.SettleAsync(h);
                await FocusEditorAsync(h, demo); // the caret is in the first word of the file, far from "Total"
                Assert.Empty(debug.Watches.Expressions);

                h.RightClick("Tool.Locals.Row.Total.Name");
                AssertHasFocus();
                Assert.Equal(["Total"], shell.Locals.SelectedRows.Select(r => r.Key));
                Click(OpenItem(opened, "run.addWatch"));
                Assert.Equal(["Total"], debug.Watches.Expressions);
                await DebugScenario.SettleAsync(h);
                break;
            }

            case "Messages":
            {
                await MessagesViewTests.BuildWithMessagesAsync(h);
                await FocusEditorAsync(h, unit);
                var messages = BuildScenario.Messages(h);
                Assert.DoesNotContain(messages.SelectedRows, r => r.Key == "2");

                h.RightClick("Tool.Messages.Row.2.Description");
                AssertHasFocus();
                Assert.Equal(["2"], messages.SelectedRows.Select(r => r.Key));
                Click(OpenItem(opened, "messages.copy"));
                await h.WaitForAsync(() => clipboard.Text is not null, "copied");
                Assert.Contains("W1002", clipboard.Text, StringComparison.Ordinal);
                Assert.DoesNotContain("E2003", clipboard.Text, StringComparison.Ordinal);
                break;
            }

            default:
            {
                var opening = h.Services.Commands.ExecuteAsync("search.findInFiles");
                var dlg = await h.WaitForDialogAsync("FindInFiles");
                dlg.Find<TextBox>("SearchText").Text = "Caption";
                dlg.Click("Find");
                await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
                await opening;
                await h.WaitForAsync(() => h.TryFind<Control>("Tool.SearchResults.Row.Unit1.pas:6:3") is not null, "the matches");
                var editor = await FocusEditorAsync(h, unit);
                editor.Select(0, 4); // "unit": what Copy takes while the editor has the focus
                Dispatcher.UIThread.RunJobs();

                h.RightClick("Tool.SearchResults.Row.Unit1.pas:6:3");
                AssertHasFocus();
                Assert.Equal(["Unit1.pas:6:3"], h.Services.SearchResults.SelectedRows.Select(r => r.Key));
                Click(OpenItem(opened, "edit.copy"));
                await h.WaitForAsync(() => clipboard.Text is not null, "copied");
                Assert.Equal(unit + "(6,3): Caption := Caption + 'x';", clipboard.Text);
                break;
            }
        }

        foreach (var menu in opened.Where(m => m.IsOpen))
            menu.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
