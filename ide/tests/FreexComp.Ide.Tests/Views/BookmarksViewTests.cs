using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Bookmarks tool window (I-316, F11-045; tool-windows.md §18 UI-TW-1500…1507, editor-features.md IDE-ED-117): every
/// bookmark of the session with number, name, file, line and preview, live; navigation, delete, rename in place, the
/// empty state and <c>search.bookmark.addNamed</c>.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class BookmarksViewTests
{
    private const string SubUnit = "unit Unit1;\r\ninterface\r\nimplementation\r\nvar Bar: Integer;\r\n    Bar := 2;\r\nend.\r\n";

    /// <summary>The product's phase (phase 8 is delivered card by card): the Bookmarks window registers.</summary>
    private static Task<HeadlessHarness> StartAsync(Action<HarnessOptions>? configure = null) =>
        BuildScenario.StartAsync(o =>
        {
            o.InitialFiles[@"Hello\sub\Unit1.pas"] = SubUnit;
            configure?.Invoke(o);
        }, implementedPhase: null);

    private static BookmarksViewModel View(HeadlessHarness h) => Assert.IsType<BookmarksViewModel>(h.Services.ToolWindows.Get(BookmarksViewModel.ToolName));

    private static string MainPath(HeadlessHarness h) => Path.Combine(h.TempDir, @"Hello\Hello.pas");

    private static string SubPath(HeadlessHarness h) => Path.Combine(h.TempDir, @"Hello\sub\Unit1.pas");

    private static async Task<TextDocumentViewModel> OpenAtAsync(HeadlessHarness h, string path, int line)
    {
        var document = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(path)).Value);
        Dispatcher.UIThread.RunJobs();
        h.Services.Navigation.GoToLine(document, line, 1);
        Dispatcher.UIThread.RunJobs();
        return document;
    }

    /// <summary>Bookmarks of documents that are not open, with the previews read from disk.</summary>
    private static async Task LoadAsync(HeadlessHarness h, params BookmarkInfo[] bookmarks)
    {
        h.Services.Bookmarks.Load(bookmarks);
        await h.Services.Bookmarks.Pending;
        Dispatcher.UIThread.RunJobs();
    }

    private static void Show(HeadlessHarness h)
    {
        h.Services.Layout.Layout.BottomSize = 400; // room for every row of the test (rows outside the view are not drawn)
        Assert.True(h.Services.ToolWindows.Show(BookmarksViewModel.ToolName, focus: true));
        Dispatcher.UIThread.RunJobs();
    }

    private static string Row(DebugGridRow row) => string.Join(" | ", row.Cells.Select(c => c.Text));

    private static MenuItem? MenuItemOf(ItemsControl parent, string id) =>
        parent.Items.OfType<MenuItem>().Select(i => AutomationIds.Get(i) == id ? i : MenuItemOf(i, id)).FirstOrDefault(i => i is not null);

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-117")]
    [Trait("Req", "UI-TW-1504")]
    public async Task AddNamedCommandShowsWindowAndEditsName()
    {
        await using var h = await StartAsync();
        var unitPath = BuildScenario.UnitPath(h);
        var view = View(h);
        Assert.Null(h.TryFind<Control>("Tool.Bookmarks")); // hidden by default
        Assert.DoesNotContain(BookmarksViewModel.ToolName, h.Services.Layout.Layout.Bottom.Tools);
        var unit = await OpenAtAsync(h, unitPath, 4);
        Assert.True(h.Services.Commands.CanExecute("search.bookmark.addNamed"));
        Assert.Equal(Strings.Cmd_search_bookmark_addNamed, h.Services.Commands.Get("search.bookmark.addNamed").Title);

        // The caret line gets an unnumbered bookmark, the window shows (bottom dock, last tab) and the Name cell is edited.
        await h.ExecuteAsync("search.bookmark.addNamed");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.Bookmarks.Row.Unit1.pas:4.Name.Edit", "the name input has the focus");
        Assert.Equal(BookmarksViewModel.ToolName, h.Services.Layout.Layout.Bottom.Tools[^1]);
        Assert.Equal(BookmarksViewModel.ToolName, h.Services.Layout.Layout.Bottom.ActiveTool);
        Assert.Null(h.Services.Bookmarks.All.Single().Number);
        Assert.Equal("Bookmark", unit.Decorations.GlyphsAt(4).Single(g => g.Kind == LineGlyphKind.Bookmark).ProbeName);
        Assert.Equal(["Unit1.pas:4"], view.SelectedKeys);
        var input = h.Find<TextBox>("Tool.Bookmarks.Row.Unit1.pas:4.Name.Edit");
        Assert.Equal("", input.Text);
        Assert.DoesNotContain("codeSmall", input.Classes);
        h.Type("  entry point  ");
        h.Press("Enter");
        Assert.Equal("entry point", h.Services.Bookmarks.All.Single().Name); // IDE-ED-116: trimmed
        Assert.Null(view.Edit);
        Assert.Equal("entry point", DebugScenario.Cell(h, "Bookmarks", "Unit1.pas:4", "Name"));
        Assert.Equal("Tool.Bookmarks.List", h.FocusedAutomationId);

        // Again on the same line: the bookmark is kept, its name offered for editing; Escape keeps it as it is.
        await h.ExecuteAsync("search.bookmark.addNamed");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.Bookmarks.Row.Unit1.pas:4.Name.Edit", "the name input has the focus");
        Assert.Equal("entry point", h.Find<TextBox>("Tool.Bookmarks.Row.Unit1.pas:4.Name.Edit").Text);
        h.Press("Escape");
        Assert.Null(view.Edit);
        Assert.Equal(("entry point", 4), (h.Services.Bookmarks.All.Single().Name, h.Services.Bookmarks.All.Single().Line));

        // Another line, Escape at once: the bookmark stays without a name.
        h.Services.Navigation.GoToLine(unit, 6, 1);
        Dispatcher.UIThread.RunJobs();
        await h.ExecuteAsync("search.bookmark.addNamed");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.Bookmarks.Row.Unit1.pas:6.Name.Edit", "the name input has the focus");
        h.Press("Escape");
        Assert.Equal([(4, "entry point"), (6, (string?)null)], h.Services.Bookmarks.All.Select(b => (b.Line, b.Name)));
        Assert.Equal(["Unit1.pas:4", "Unit1.pas:6"], view.Rows.Select(r => r.Key));

        // The Search › Bookmarks menu item.
        var item = MenuItemOf(h.Find<Menu>(AutomationIds.MenuBar), AutomationIds.Menu("search.bookmark.addNamed"));
        Assert.True(item!.IsVisible);
        Assert.True(item.Command!.CanExecute(null));
        foreach (var document in h.Services.Documents.Documents.ToList())
            Assert.True(await h.Services.Documents.CloseAsync(document));
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Services.Commands.CanExecute("search.bookmark.addNamed")); // no text document
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1500")]
    [Trait("Req", "UI-TW-1503")]
    [Trait("Req", "UI-TW-1507")]
    public async Task ListsAllDocumentsSortedAndLive()
    {
        await using var h = await StartAsync();
        var unitPath = BuildScenario.UnitPath(h);
        var view = View(h);
        await LoadAsync(h,
            new BookmarkInfo(unitPath, 7, 1),
            new BookmarkInfo(unitPath, 5, null, "body"),
            new BookmarkInfo(SubPath(h), 5, null),
            new BookmarkInfo(MainPath(h), 3, 2, "main"));

        // UI-TW-1507: view.bookmarks shows the window with the focus on the grid.
        Assert.True(h.Services.Commands.CanExecute("view.bookmarks"));
        h.Services.Layout.Layout.BottomSize = 400;
        await h.ExecuteAsync("view.bookmarks");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.Bookmarks.List", "the grid has the focus");
        Assert.Equal(Strings.Cmd_view_bookmarks, view.Title);

        // UI-TW-1500: every bookmark, open or closed document, by full path then line; UI-TW-1503: keys with @<n>.
        Assert.Equal(["Number", "Name", "File", "Line", "Text"], view.Columns.Select(c => c.Id));
        Assert.Equal(["Hello.pas:3", "Unit1.pas:5", "Unit1.pas:5@2", "Unit1.pas:7"], view.Rows.Select(r => r.Key));
        Assert.Equal(
        [
            "2 | main | Hello.pas | 3 | begin",
            " |  | Unit1.pas | 5 | Bar := 2;", // sub\Unit1.pas sorts before Unit1.pas
            " | body | Unit1.pas | 5 | begin",
            "1 |  | Unit1.pas | 7 | end.",
        ], view.Rows.Select(Row));
        Assert.Equal(SubPath(h), ToolTip.GetTip(h.Find<TextBlock>("Tool.Bookmarks.Row.Unit1.pas:5.File")));
        Assert.Equal(unitPath, ToolTip.GetTip(h.Find<TextBlock>("Tool.Bookmarks.Row.Unit1.pas:5@2.File")));
        Assert.Equal("main", DebugScenario.Cell(h, "Bookmarks", "Hello.pas:3", "Name"));
        Assert.Equal("2", DebugScenario.Cell(h, "Bookmarks", "Hello.pas:3", "Number"));
        Assert.Equal("3", DebugScenario.Cell(h, "Bookmarks", "Hello.pas:3", "Line"));
        Assert.Contains("codeSmall", h.Find<TextBlock>("Tool.Bookmarks.Row.Hello.pas:3.Text").Classes);

        // Sorting: numbered first by number, unnumbered after; persisted (UI-TW-012); back to the default order.
        h.Click("Tool.Bookmarks.Column.Number");
        Assert.Equal(["Unit1.pas:7", "Hello.pas:3", "Unit1.pas:5", "Unit1.pas:5@2"], view.Rows.Select(r => r.Key));
        Assert.Equal(new ColumnSort("Number", false), h.Services.Layout.GetSort(BookmarksViewModel.ToolName));
        h.Click("Tool.Bookmarks.Column.Name");
        Assert.Equal(["Unit1.pas:5", "Unit1.pas:7", "Unit1.pas:5@2", "Hello.pas:3"], view.Rows.Select(r => r.Key));
        h.Click("Tool.Bookmarks.Column.Name");
        h.Click("Tool.Bookmarks.Column.Name");
        Assert.Null(h.Services.Layout.GetSort(BookmarksViewModel.ToolName));
        Assert.Equal(["Hello.pas:3", "Unit1.pas:5", "Unit1.pas:5@2", "Unit1.pas:7"], view.Rows.Select(r => r.Key));

        // Live: an edit above a bookmark moves it (new key, line and preview) and the selection follows it.
        h.Click("Tool.Bookmarks.Row.Unit1.pas:7.Text");
        Assert.Equal(["Unit1.pas:7"], view.SelectedKeys);
        var unit = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(unitPath)).Value);
        Dispatcher.UIThread.RunJobs();
        unit.Buffer.ApplyEdits([new TextEdit(0, 0, "// first\r\n")]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Hello.pas:3", "Unit1.pas:5", "Unit1.pas:6", "Unit1.pas:8"], view.Rows.Select(r => r.Key));
        Assert.Equal(["Unit1.pas:8"], view.SelectedKeys);
        Assert.Equal("8", DebugScenario.Cell(h, "Bookmarks", "Unit1.pas:8", "Line"));
        var (start, length, _) = unit.Buffer.GetLine(8);
        unit.Buffer.ApplyEdits([new TextEdit(start, length, "end. // done")]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("end. // done", DebugScenario.Cell(h, "Bookmarks", "Unit1.pas:8", "Text"));

        // Without a project: the window lists the loose files' bookmarks too.
        Assert.True(await h.Services.Documents.SaveAsync(unit));
        Assert.True(await h.Services.Projects.CloseAsync());
        Dispatcher.UIThread.RunJobs();
        var loose = await OpenAtAsync(h, SubPath(h), 2);
        await h.ExecuteAsync("search.bookmark.toggle");
        Assert.Equal(h.Services.Bookmarks.All.Count, view.Rows.Count);
        Assert.Equal("interface", view.Rows.Single(r => r.Key == "Unit1.pas:2").Cells[4].Text);
        Assert.True(await h.Services.Documents.CloseAsync(loose));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1501")]
    [Trait("Req", "UI-TW-1502")]
    [Trait("Req", "UI-TW-1505")]
    public async Task EnterNavigatesDeleteRemoves()
    {
        await using var h = await StartAsync();
        var unitPath = BuildScenario.UnitPath(h);
        var view = View(h);
        await LoadAsync(h, new BookmarkInfo(MainPath(h), 2, null), new BookmarkInfo(unitPath, 4, 3), new BookmarkInfo(unitPath, 6, null));
        Show(h);

        // UI-TW-1505: tool bar and context menu.
        Assert.Equal(["bookmarks.delete", "search.bookmark.clearAll"], view.ToolBarCommands.Select(c => c.CommandId));
        Assert.NotNull(h.TryFind<Control>("Tool.Bookmarks.ToolBar.bookmarks.delete"));
        Assert.NotNull(h.TryFind<Control>("Tool.Bookmarks.ToolBar.search.bookmark.clearAll"));
        Assert.Equal(["bookmarks.goToSource", null, "bookmarks.rename", "bookmarks.delete", null, "search.bookmark.clearAll"], view.ContextCommands);
        Assert.Equal([Strings.Cmd_bookmarks_goToSource, Strings.Cmd_bookmarks_rename, Strings.Cmd_bookmarks_delete, Strings.Cmd_search_bookmark_clearAll],
            view.BuildContextMenu().Where(m => m.CommandId is not null).Select(m => m.Label));

        // UI-TW-1501: Enter opens the document, caret at column 1 of the line, focus in the editor.
        h.Click("Tool.Bookmarks.Row.Unit1.pas:4.Text");
        h.Press("Enter"); // bookmarks.goToSource
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == unitPath, "the bookmark's file shown");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new TextPosition(4, 1), ((TextDocumentViewModel)h.Services.Documents.ActiveDocument!).ViewState.Caret);
        Assert.Equal("Editor.Unit1.pas", h.FocusedAutomationId);

        // Double-click (outside the Name cell) is the same default action.
        Show(h);
        h.DoubleClick("Tool.Bookmarks.Row.Hello.pas:2.File");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == MainPath(h), "the bookmark's file shown");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new TextPosition(2, 1), ((TextDocumentViewModel)h.Services.Documents.ActiveDocument!).ViewState.Caret);
        Assert.Equal("Editor.Hello.pas", h.FocusedAutomationId);
        Assert.Contains(h.Services.Navigation.History, l => l.FilePath == unitPath); // the jump is a navigation-history entry

        // UI-TW-1502: multi-selection; Delete removes the selected rows without confirmation, the next row is selected.
        Show(h);
        Assert.True(h.Find<ListBox>("Tool.Bookmarks.List").SelectionMode.HasFlag(SelectionMode.Multiple));
        h.Click("Tool.Bookmarks.Row.Hello.pas:2.Text");
        view.SelectedKeys = ["Hello.pas:2", "Unit1.pas:4"];
        h.Press("Delete");
        Assert.Empty(h.Messages.Requests);
        Assert.Equal(["Unit1.pas:6"], view.Rows.Select(r => r.Key));
        Assert.Equal(["Unit1.pas:6"], view.SelectedKeys);
        Assert.Equal([6], h.Services.Bookmarks.All.Select(b => b.Line));
        Assert.Null(((TextDocumentViewModel)h.Services.Documents.Documents.Single(d => d.FilePath == unitPath)).Decorations.GlyphsAt(4)
            .FirstOrDefault(g => g.Kind == LineGlyphKind.Bookmark)); // the glyph of the open document goes too

        // The tool bar's Clear All Bookmarks.
        h.Click("Tool.Bookmarks.ToolBar.search.bookmark.clearAll");
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(h.Services.Bookmarks.All);
        Assert.True(h.Find<Control>("Tool.Bookmarks.Empty").IsEffectivelyVisible);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1504")]
    public async Task RenameInPlace()
    {
        await using var h = await StartAsync();
        var unitPath = BuildScenario.UnitPath(h);
        var view = View(h);
        await LoadAsync(h, new BookmarkInfo(MainPath(h), 2, null), new BookmarkInfo(unitPath, 4, null, "old"));
        Show(h);
        string? Name(int line) => h.Services.Bookmarks.All.Single(b => b.Line == line).Name;

        // F2, type, Enter: committed (trimmed), the list has the focus again.
        h.Click("Tool.Bookmarks.Row.Unit1.pas:4.Text");
        h.Press("F2"); // bookmarks.rename
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.Bookmarks.Row.Unit1.pas:4.Name.Edit", "the name input has the focus");
        Assert.Equal("old", h.Find<TextBox>("Tool.Bookmarks.Row.Unit1.pas:4.Name.Edit").Text);
        h.Type("new name ");
        h.Press("Enter");
        Assert.Equal("new name", Name(4));
        Assert.Equal("Tool.Bookmarks.List", h.FocusedAutomationId);

        // Escape cancels.
        h.Press("F2");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.Bookmarks.Row.Unit1.pas:4.Name.Edit", "the name input has the focus");
        h.Type("zzz");
        h.Press("Escape");
        Assert.Equal("new name", Name(4));
        Assert.Null(view.Edit);

        // Focus loss commits; at most 80 characters.
        h.Press("F2");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.Bookmarks.Row.Unit1.pas:4.Name.Edit", "the name input has the focus");
        h.Type(new string('n', 100));
        h.Click("Tool.Bookmarks.Row.Hello.pas:2.Text");
        await h.WaitForAsync(() => view.Edit is null, "the focus loss commits");
        Assert.Equal(new string('n', 80), Name(4));
        Assert.Equal(["Hello.pas:2"], view.SelectedKeys); // the clicked row is selected

        // A double-click on the Name cell renames (it does not navigate); an empty name removes the name.
        var active = h.Services.Documents.ActiveDocument;
        h.DoubleClick("Tool.Bookmarks.Row.Unit1.pas:4.Name");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.Bookmarks.Row.Unit1.pas:4.Name.Edit", "the name input has the focus");
        Assert.Same(active, h.Services.Documents.ActiveDocument);
        h.Press("Delete");
        h.Press("Enter");
        Assert.Null(Name(4));
        Assert.Equal("", DebugScenario.Cell(h, "Bookmarks", "Unit1.pas:4", "Name"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1506")]
    public async Task EmptyState()
    {
        await using var h = await StartAsync();
        var view = View(h);
        Show(h);
        Assert.True(view.IsEmpty);
        Assert.Equal((Strings.Msg_Bookmarks_Empty, Strings.Msg_Bookmarks_Empty_Secondary), (view.EmptyHeading, view.EmptySecondary));
        Assert.True(h.Find<Control>("Tool.Bookmarks.Empty").IsEffectivelyVisible);
        Assert.False(h.Find<Control>("Tool.Bookmarks.List").IsEffectivelyVisible);
        Assert.False(h.Services.Commands.CanExecute("bookmarks.delete"));
        Assert.False(h.Services.Commands.CanExecute("search.bookmark.clearAll"));

        // A bookmark replaces the empty state by its row; removing it brings the empty state back.
        var unit = await OpenAtAsync(h, BuildScenario.UnitPath(h), 2);
        await h.ExecuteAsync("search.bookmark.toggle");
        Assert.False(view.IsEmpty);
        Assert.False(h.Find<Control>("Tool.Bookmarks.Empty").IsEffectivelyVisible);
        Assert.Equal(["Unit1.pas:2"], view.Rows.Select(r => r.Key));
        await h.ExecuteAsync("search.bookmark.toggle");
        Assert.True(view.IsEmpty);
        Assert.True(h.Find<Control>("Tool.Bookmarks.Empty").IsEffectivelyVisible);
        Assert.True(await h.Services.Documents.CloseAsync(unit));
    }
}
