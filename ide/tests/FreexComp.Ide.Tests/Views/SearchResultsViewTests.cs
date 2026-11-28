using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Search Results tool window (I-123, tool-windows.md §8) and its result sets (I-354, UI-TW-615…618).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class SearchResultsViewTests
{
    private const string Unit = "unit Unit1;\r\ninterface\r\nimplementation\r\nvar Caption: string;\r\nbegin\r\n    Caption := Caption + 'caption';\r\nend.\r\n";

    /// <summary>The keys of the list's rows (the list virtualizes: rows outside the viewport have no control).</summary>
    private static List<string> RowKeys(HeadlessHarness h) =>
        [.. (h.Find<ListBox>("Tool.SearchResults.List").ItemsSource ?? Array.Empty<object>()).OfType<SearchResultRow>().Select(r => r.Key)];

    /// <summary>Scrolls the row into view and returns the control with the AutomationId <c>Tool.SearchResults.Row.&lt;id&gt;</c>.</summary>
    private static T Cell<T>(HeadlessHarness h, string id) where T : Control
    {
        var list = h.Find<ListBox>("Tool.SearchResults.List");
        var key = id.EndsWith(".Line", StringComparison.Ordinal) || id.EndsWith(".Text", StringComparison.Ordinal) ? id[..id.LastIndexOf('.')] : id;
        list.ScrollIntoView(list.ItemsSource!.OfType<SearchResultRow>().First(r => r.Key == key));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(h.FindAll("Tool.SearchResults.Row."), c => AutomationProperties.GetAutomationId(c) == "Tool.SearchResults.Row." + key);
        return h.Find<T>("Tool.SearchResults.Row." + id);
    }

    /// <summary>The text before the highlighted run and the highlighted text of a match row's <c>.Text</c> cell.</summary>
    private static (string Before, string Match) Highlight(TextBlock text)
    {
        var runs = text.Inlines!.OfType<Run>().ToList();
        var index = runs.FindIndex(r => r.Background is not null);
        Assert.True(index >= 0, "no highlighted run");
        return (string.Concat(runs.Take(index).Select(r => r.Text)), runs[index].Text!);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-600")]
    [Trait("Req", "UI-TW-601")]
    [Trait("Req", "UI-TW-602")]
    [Trait("Req", "UI-TW-603")]
    [Trait("Req", "UI-TW-607")]
    [Trait("Req", "UI-TW-608")]
    [Trait("Req", "IDE-ED-121")]
    public async Task OneRowPerMatchWithHighlight()
    {
        await using var h = await BuildScenario.StartAsync(o => o.InitialFiles[@"Hello\Unit1.pas"] = Unit);
        var helloPath = Path.Combine(h.TempDir, @"Hello\Hello.pas");
        var unitPath = BuildScenario.UnitPath(h);
        await h.ExecuteAsync("view.searchResults");
        Assert.True(h.Find<Control>("Tool.SearchResults.Empty").IsVisible);

        // An unsaved edit is searched in the buffer (IDE-ED-121).
        Assert.True((await h.Services.Documents.OpenAsync(helloPath)).Success);
        h.Find<TextEditor>("Editor.Hello.pas").Document.Insert(0, "// Caption\r\n");
        Dispatcher.UIThread.RunJobs();

        var opening = h.Services.Commands.ExecuteAsync("search.findInFiles");
        var dlg = await h.WaitForDialogAsync("FindInFiles");
        dlg.Find<TextBox>("SearchText").Text = "Caption";
        dlg.Find<CheckBox>("MatchCase").IsChecked = true;
        dlg.Click("Find");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        await opening;
        var header = h.Find<TextBlock>("Tool.SearchResults.Header");
        await h.WaitForAsync(() => header.Text == "Find \"Caption\" in project — 4 matches in 2 files (match case)", "header after the search");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.SearchResults.List", "focus moved to the results");

        // Files in project order, then one row per match (two on line 6), leading whitespace removed.
        Assert.Equal(["Hello.pas", "Hello.pas:1:4", "Unit1.pas", "Unit1.pas:4:5", "Unit1.pas:6:5", "Unit1.pas:6:16"], RowKeys(h));
        Assert.Equal("6", Cell<TextBlock>(h, "Unit1.pas:6:5.Line").Text);
        Assert.Equal(("", "Caption"), Highlight(Cell<TextBlock>(h, "Unit1.pas:6:5.Text")));
        Assert.Equal(("Caption := ", "Caption"), Highlight(Cell<TextBlock>(h, "Unit1.pas:6:16.Text")));
        Assert.Equal(("// ", "Caption"), Highlight(Cell<TextBlock>(h, "Hello.pas:1:4.Text")));

        // Enter on a match row: the file opens with the match selected, the editor has focus.
        Cell<Control>(h, "Unit1.pas:6:16");
        h.Click("Tool.SearchResults.Row.Unit1.pas:6:16");
        h.Press("Enter");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Unit1.pas", "editor focused");
        var unit = h.Find<TextEditor>("Editor.Unit1.pas");
        Assert.Equal((Unit.IndexOf("Caption +", StringComparison.Ordinal), 7), (unit.SelectionStart, unit.SelectionLength));

        // F8 steps through match rows while Search Results was activated last (wraps to the first match).
        h.Press("F8");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Hello.pas", "next match");
        var hello = h.Find<TextEditor>("Editor.Hello.pas");
        Assert.Equal((3, 7), (hello.SelectionStart, hello.SelectionLength));

        // Ctrl+C on a match row copies <abs-path>(<line>,<col>): <line text>.
        Cell<Control>(h, "Unit1.pas:4:5");
        h.Click("Tool.SearchResults.Row.Unit1.pas:4:5");
        h.Press("Ctrl+C");
        await h.WaitForAsync(() => ((FakeClipboard)h.Services.Clipboard).Text is not null, "copied");
        Assert.Equal(unitPath + "(4,5): var Caption: string;", ((FakeClipboard)h.Services.Clipboard).Text);

        // Collapse all hides the match rows; clear returns to the empty state.
        await h.ExecuteAsync("searchResults.collapseAll");
        Assert.Equal(["Hello.pas", "Unit1.pas"], RowKeys(h));
        await h.ExecuteAsync("searchResults.clear");
        Assert.True(h.Find<Control>("Tool.SearchResults.Empty").IsVisible);
        Assert.Empty(RowKeys(h));
    }

    /// <summary>A unit beside the project with forty comment lines that hold the word <c>unit</c>: enough rows to scroll.</summary>
    private static readonly string Long = "unit Long;\r\ninterface\r\nimplementation\r\n"
        + string.Concat(Enumerable.Range(1, 40).Select(i => $"// unit line {i:00}\r\n")) + "end.\r\n";

    /// <summary>The project Hello with the product's implemented phase (so the delivered Phase 8 commands work) and Search Results shown.</summary>
    private static async Task<HeadlessHarness> StartSetsAsync()
    {
        var h = await BuildScenario.StartAsync(o =>
        {
            o.InitialFiles[@"Hello\Unit1.pas"] = Unit;
            o.InitialFiles[@"Hello\Long.pas"] = Long;
        }, implementedPhase: null);
        await h.ExecuteAsync("view.searchResults");
        return h;
    }

    /// <summary>Runs a Find in Files search over the project — or, with <paramref name="folder"/>, over the project's folder — to its end.</summary>
    private static async Task SearchAsync(HeadlessHarness h, string text, bool matchCase = false, bool folder = false)
    {
        var query = new FindQuery(text, matchCase, WholeWord: false, Regex: false);
        await h.Services.SearchResults.RunAsync(folder
            ? new FindInFilesRequest(query, FindInFilesScope.Folder, Path.Combine(h.TempDir, "Hello"))
            : new FindInFilesRequest(query, FindInFilesScope.Project));
        Dispatcher.UIThread.RunJobs();
    }

    private static Control? Strip(HeadlessHarness h) => h.TryFind<Control>("Tool.SearchResults.Sets") is { IsEffectivelyVisible: true } strip ? strip : null;

    /// <summary>The tabs of the set strip, left to right: title, a leading <c>*</c> for the shown one (none when the strip is not shown).</summary>
    private static List<string> Tabs(HeadlessHarness h)
    {
        List<string> tabs = [];
        if (Strip(h) is null)
            return tabs;
        for (var n = 1; h.TryFind<Control>("Tool.SearchResults.Set." + n) is { } tab; n++)
            tabs.Add((h.Services.SearchResults.Sets[n - 1].IsShown ? "*" : "") + tab.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        return tabs;
    }

    private static List<string> TabIcons(HeadlessHarness h, int n) =>
        [.. h.Find<Control>("Tool.SearchResults.Set." + n).GetVisualDescendants().OfType<IdeIcon>().Where(i => i.IsEffectivelyVisible).Select(i => i.IconName!)];

    private static string? SelectedKey(HeadlessHarness h) => (h.Find<ListBox>("Tool.SearchResults.List").SelectedItem as SearchResultRow)?.Key;

    private static ScrollViewer ListScroll(HeadlessHarness h) => h.Find<ListBox>("Tool.SearchResults.List").GetVisualDescendants().OfType<ScrollViewer>().First();

    private static Color Token(HeadlessHarness h, string token) =>
        h.Window.TryFindResource(IconCatalog.BrushKey(token), h.Window.ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException("No brush for " + token);

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-615")]
    [Trait("Req", "UI-TW-616")]
    [Trait("Req", "UI-TW-617")]
    [Trait("Req", "UI-TW-618")]
    public async Task PinnedResultSetSurvivesANewSearch()
    {
        await using var h = await StartSetsAsync();
        var results = h.Services.SearchResults;
        var folderHeader = $"Find \"unit\" in {Path.Combine(h.TempDir, "Hello")} — 44 matches in 3 files";

        // The first search: three files, many rows. Its state: a collapsed file row, a selected row far down, the list scrolled.
        await SearchAsync(h, "unit", folder: true);
        var header = h.Find<TextBlock>("Tool.SearchResults.Header");
        Assert.Equal(folderHeader, header.Text);
        Assert.Null(Strip(h));
        h.Click("Tool.SearchResults.Row.Hello.pas");
        h.Press("Left");
        Assert.DoesNotContain("Hello.pas:2:6", RowKeys(h));
        var list = h.Find<ListBox>("Tool.SearchResults.List");
        Cell<Control>(h, "Long.pas:40:4");
        h.Click("Tool.SearchResults.Row.Long.pas:40:4");
        var firstRows = RowKeys(h);
        var offset = ListScroll(h).Offset.Y;
        Assert.True(offset > 0, "the list is scrolled");
        Assert.Equal(46, firstRows.Count);

        // Keep Results on the tool bar, between Collapse All and Clear: the set is pinned and the strip appears with its tab.
        var pin = h.Find<Button>("Tool.SearchResults.ToolBar.searchResults.pin");
        Assert.Equal("pin", pin.GetVisualDescendants().OfType<IdeIcon>().Single().IconName);
        Assert.Equal(h.Services.Commands.Get("searchResults.pin").Title, AutomationProperties.GetName(pin));
        double Left(string command) => h.Find<Button>("Tool.SearchResults.ToolBar." + command).TranslatePoint(default, h.Window)!.Value.X;
        Assert.True(Left("searchResults.collapseAll") < Left("searchResults.pin") && Left("searchResults.pin") < Left("searchResults.clear"));
        Assert.True(pin.IsEffectivelyEnabled);
        h.Click("Tool.SearchResults.ToolBar.searchResults.pin");
        Assert.False(pin.IsEffectivelyEnabled); // one way: a pinned set is closed, never unpinned
        Assert.Equal(["*\"unit\""], Tabs(h));
        Assert.Equal(firstRows, RowKeys(h));
        Assert.Equal(offset, ListScroll(h).Offset.Y);

        // A second search: it becomes the current set and is shown; the pinned one keeps its tab.
        await SearchAsync(h, "Caption", matchCase: true);
        Assert.Equal(["\"unit\"", "*\"Caption\""], Tabs(h));
        Assert.Equal("Find \"Caption\" in project — 3 matches in 1 files (match case)", header.Text);
        Assert.Equal(["Unit1.pas", "Unit1.pas:4:5", "Unit1.pas:6:5", "Unit1.pas:6:16"], RowKeys(h));
        Assert.Equal(0.0, ListScroll(h).Offset.Y);
        Assert.Null(SelectedKey(h));
        Assert.True(pin.IsEffectivelyEnabled);

        // Find in Files moves the keyboard focus to its results (UI-TW-603): to the rows, not to the strip above them.
        Assert.Equal("Tool.SearchResults.List", h.FocusedAutomationId);
        Assert.IsType<ListBoxItem>(h.Window.FocusManager?.GetFocusedElement());
        Cell<Control>(h, "Unit1.pas:6:5");
        h.Click("Tool.SearchResults.Row.Unit1.pas:6:5");

        // Its tab is selected: the rows, the expansion, the selection and the scroll position of the first set are back.
        h.Click("Tool.SearchResults.Set.1");
        Assert.Equal(["*\"unit\"", "\"Caption\""], Tabs(h));
        Assert.Equal(folderHeader, header.Text);
        Assert.Equal(firstRows, RowKeys(h));
        Assert.DoesNotContain("Hello.pas:2:6", RowKeys(h));
        Assert.Equal("Long.pas:40:4", SelectedKey(h));
        Assert.Equal(offset, ListScroll(h).Offset.Y);
        Assert.True(h.Find<Control>("Tool.SearchResults.Row.Long.pas:40:4").IsEffectivelyVisible);
        Assert.False(pin.IsEffectivelyEnabled);
        Assert.Same(list, h.Find<ListBox>("Tool.SearchResults.List"));

        // And the second one has its own selection.
        h.Click("Tool.SearchResults.Set.2");
        Assert.Equal(["Unit1.pas", "Unit1.pas:4:5", "Unit1.pas:6:5", "Unit1.pas:6:16"], RowKeys(h));
        Assert.Equal("Unit1.pas:6:5", SelectedKey(h));
        Assert.Equal(0.0, ListScroll(h).Offset.Y);

        // A third search replaces the current set only.
        await SearchAsync(h, "begin");
        Assert.Equal(["\"unit\"", "*\"begin\""], Tabs(h));
        Assert.Equal(["Hello.pas", "Hello.pas:3:1", "Unit1.pas", "Unit1.pas:5:1"], RowKeys(h));
        h.Click("Tool.SearchResults.Set.1");
        Assert.Equal(firstRows, RowKeys(h));

        // The context menu offers Keep Results before Repeat Search.
        var menu = results.BuildContextMenu().Select(i => i.CommandId).ToList();
        Assert.Equal(menu.IndexOf("searchResults.repeat") - 1, menu.IndexOf("searchResults.pin"));
        Assert.True(menu.IndexOf("searchResults.pin") > 0);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-615")]
    public async Task SetStripOnlyWithPinnedSets()
    {
        await using var h = await StartSetsAsync();
        var results = h.Services.SearchResults;
        var view = h.Find<ListBox>("Tool.SearchResults.List").FindAncestorOfType<FreexComp.Ide.Views.SearchResultsView>()!;
        double Top(Control control) => control.TranslatePoint(default, view)!.Value.Y;

        // One unpinned set: no strip — the header lies directly under the tool bar, as in Phases 3–7.
        await SearchAsync(h, "Caption", matchCase: true);
        var header = h.Find<TextBlock>("Tool.SearchResults.Header");
        var headerStrip = Assert.IsType<Border>(header.Parent);
        Assert.Null(Strip(h));
        Assert.Empty(Tabs(h));
        Assert.Equal(0.0, Top(headerStrip));

        // Pinned: the strip — 26 high, on bg.sunken — between the tool bar and the header.
        await h.ExecuteAsync("searchResults.pin");
        var strip = Assert.IsType<Border>(Strip(h));
        Assert.Equal(26.0, strip.Bounds.Height);
        Assert.Equal(Token(h, "bg.sunken"), Assert.IsAssignableFrom<ISolidColorBrush>(strip.Background).Color);
        Assert.Equal(0.0, Top(strip));
        Assert.Equal(26.0, Top(headerStrip));
        Assert.Equal(Strings.SearchResults_Sets, AutomationProperties.GetName(strip));

        // Its tab: the pin glyph, the icon of the kind, the search text in double quotes, the close button; the tooltip
        // is the set's header text.
        var tab = h.Find<Control>("Tool.SearchResults.Set.1");
        Assert.Equal(["*\"Caption\""], Tabs(h));
        Assert.Equal(["pin", "find-in-files", "close"], TabIcons(h, 1));
        Assert.Equal(header.Text, ToolTip.GetTip(tab));
        Assert.Equal(string.Format(Strings.SearchResults_SetKept, "\"Caption\""), AutomationProperties.GetName(tab));
        var close = h.Find<Button>("Tool.SearchResults.Set.1.Close");
        Assert.Equal((16.0, 16.0), (close.Bounds.Width, close.Bounds.Height));
        Assert.Equal(Strings.SearchResults_CloseSet, AutomationProperties.GetName(close));
        Assert.True(tab.Bounds.Height <= strip.Bounds.Height);

        // A long search text: the title is cut to 24 characters with an ellipsis inside the quotes; the tooltip has it all.
        const string text = "Caption := Caption + 'caption'";
        await SearchAsync(h, text);
        Assert.Equal(["\"Caption\"", "*\"Caption := Caption + …\""], Tabs(h));
        Assert.Equal(24, Tabs(h)[1].Length - 1);
        Assert.Equal(["find-in-files", "close"], TabIcons(h, 2)); // the current set: no pin glyph
        Assert.Equal($"Find \"{text}\" in project — 1 match in 1 file", ToolTip.GetTip(h.Find<Control>("Tool.SearchResults.Set.2")));
        Assert.Equal("\"Caption := Caption + …\"", AutomationProperties.GetName(h.Find<Control>("Tool.SearchResults.Set.2")));
        Assert.Equal("ALongIdentifierOfThirty…", SearchResultsViewModel.SetTitle("ALongIdentifierOfThirtyLetters", quoted: false));
        Assert.Equal("ExactlyTwentyFourLetters", SearchResultsViewModel.SetTitle("ExactlyTwentyFourLetters", quoted: false));
        Assert.Equal("\"twenty-two characters.\"", SearchResultsViewModel.SetTitle("twenty-two characters.", quoted: true));

        // The close button is shown on the selected tab and on the tab under the pointer.
        Assert.False(h.Find<Button>("Tool.SearchResults.Set.1.Close").IsEffectivelyVisible);
        Assert.True(h.Find<Button>("Tool.SearchResults.Set.2.Close").IsEffectivelyVisible);
        Assert.Equal(["pin", "find-in-files"], TabIcons(h, 1));
        h.Hover("Tool.SearchResults.Set.1");
        Assert.True(h.Find<Button>("Tool.SearchResults.Set.1.Close").IsEffectivelyVisible);
        h.Hover("Tool.SearchResults.Header");
        Assert.False(h.Find<Button>("Tool.SearchResults.Set.1.Close").IsEffectivelyVisible);

        // The strip is one tab stop between the filter box and the list.
        h.Find<TextBox>("Tool.SearchResults.Filter").Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Tool.SearchResults.Filter", h.FocusedAutomationId);
        h.Press("Tab");
        Assert.Equal("Tool.SearchResults.Sets", h.FocusedAutomationId);
        h.Press("Tab");
        Assert.Equal("Tool.SearchResults.List", h.FocusedAutomationId);
        h.Press("Shift+Tab");
        Assert.Equal("Tool.SearchResults.Sets", h.FocusedAutomationId);

        // Left / Right change the shown set (no wrapping); the focus stays on the strip.
        h.Press("Left");
        Assert.Equal(["*\"Caption\"", "\"Caption := Caption + …\""], Tabs(h));
        Assert.Equal(["Unit1.pas", "Unit1.pas:4:5", "Unit1.pas:6:5", "Unit1.pas:6:16"], RowKeys(h));
        h.Press("Left");
        Assert.Equal(["*\"Caption\"", "\"Caption := Caption + …\""], Tabs(h));
        h.Press("Right");
        Assert.Equal(["\"Caption\"", "*\"Caption := Caption + …\""], Tabs(h));
        Assert.Equal(["Unit1.pas", "Unit1.pas:6:5"], RowKeys(h));
        h.Press("Right");
        Assert.Equal(["\"Caption\"", "*\"Caption := Caption + …\""], Tabs(h));
        Assert.Equal("Tool.SearchResults.Sets", h.FocusedAutomationId);

        // Down goes on to the rows; Escape on the strip returns the focus to the document (UI-A11Y-004).
        h.Press("Down");
        Assert.Equal("Tool.SearchResults.List", h.FocusedAutomationId);
        Assert.True((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Success);
        Dispatcher.UIThread.RunJobs();
        strip.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Tool.SearchResults.Sets", h.FocusedAutomationId);
        h.Press("Escape");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Unit1.pas", "the focus is back in the document");

        // The last pinned set is closed: the strip is gone again, the current set stays.
        h.Click("Tool.SearchResults.Set.1");
        h.Click("Tool.SearchResults.Set.1.Close");
        Assert.Null(Strip(h));
        Assert.Equal(["Unit1.pas", "Unit1.pas:6:5"], RowKeys(h));
        Assert.Equal(0.0, Top(headerStrip));
        Assert.Single(results.Sets);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-618")]
    public async Task ClearAndCloseShowTheNeighbour()
    {
        await using var h = await StartSetsAsync();
        var results = h.Services.SearchResults;
        var filter = h.Find<TextBox>("Tool.SearchResults.Filter");

        // Three pinned sets and the current one.
        foreach (var text in (string[])["Caption", "unit", "begin"])
        {
            await SearchAsync(h, text);
            await h.ExecuteAsync("searchResults.pin");
        }

        await SearchAsync(h, "end");
        Assert.Equal(["\"Caption\"", "\"unit\"", "\"begin\"", "*\"end\""], Tabs(h));
        Assert.Equal(["Hello.pas", "Hello.pas:4:1", "Unit1.pas", "Unit1.pas:7:1"], RowKeys(h));

        // The filter belongs to what is looked at: its text is cleared when another set is shown.
        filter.Text = "hello";
        h.Clock.Advance(ToolWindowViewModel.FilterDelay);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Hello.pas", "Hello.pas:4:1"], RowKeys(h));
        h.Click("Tool.SearchResults.Set.2");
        Assert.Equal("", filter.Text ?? "");
        Assert.Equal(["\"Caption\"", "*\"unit\"", "\"begin\"", "\"end\""], Tabs(h));
        Assert.Equal(["Hello.pas", "Hello.pas:2:6", "Unit1.pas", "Unit1.pas:1:1", "Unit1.pas:1:6"], RowKeys(h));
        h.Click("Tool.SearchResults.Set.4");
        Assert.Equal(["Hello.pas", "Hello.pas:4:1", "Unit1.pas", "Unit1.pas:7:1"], RowKeys(h)); // all its rows again
        h.Click("Tool.SearchResults.Set.2");

        // searchResults.clear removes the shown set: the next one in the strip is shown.
        await h.ExecuteAsync("searchResults.clear");
        Assert.Equal(["\"Caption\"", "*\"begin\"", "\"end\""], Tabs(h));
        Assert.Equal(["Hello.pas", "Hello.pas:3:1", "Unit1.pas", "Unit1.pas:5:1"], RowKeys(h));

        // The close button of a tab that is not shown (it appears under the pointer) removes that set; the shown one stays.
        h.Hover("Tool.SearchResults.Set.3");
        h.Click("Tool.SearchResults.Set.3.Close");
        Assert.Equal(["\"Caption\"", "*\"begin\""], Tabs(h));
        Assert.Equal(["Hello.pas", "Hello.pas:3:1", "Unit1.pas", "Unit1.pas:5:1"], RowKeys(h));

        // The close button of the shown, last tab: there is no next one, so the previous one is shown.
        h.Click("Tool.SearchResults.Set.2.Close");
        Assert.Equal(["*\"Caption\""], Tabs(h));
        Assert.Equal("Find \"Caption\" in project — 4 matches in 1 files", h.Find<TextBlock>("Tool.SearchResults.Header").Text);
        Assert.Equal(["Unit1.pas", "Unit1.pas:4:5", "Unit1.pas:6:5", "Unit1.pas:6:16", "Unit1.pas:6:27"], RowKeys(h));

        // Delete on the strip closes the shown set; it was the only one: the empty state.
        Assert.IsType<Border>(Strip(h)).Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        h.Press("Delete");
        Assert.Null(Strip(h));
        Assert.Empty(results.Sets);
        Assert.True(results.IsEmpty);
        Assert.True(h.Find<Control>("Tool.SearchResults.Empty").IsVisible);
        Assert.Empty(RowKeys(h));
        Assert.False(h.Services.Commands.CanExecute("searchResults.clear"));

        // Delete with a next set: that one is shown.
        await SearchAsync(h, "Caption");
        await h.ExecuteAsync("searchResults.pin");
        await SearchAsync(h, "unit");
        h.Click("Tool.SearchResults.Set.1");
        Assert.IsType<Border>(Strip(h)).Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        h.Press("Delete");
        Assert.Null(Strip(h)); // no pinned set is left
        Assert.Equal(["1 \"unit\""], results.Sets.Select(s => $"{s.Number} {s.Title}"));
        Assert.Equal(["Hello.pas", "Hello.pas:2:6", "Unit1.pas", "Unit1.pas:1:1", "Unit1.pas:1:6"], RowKeys(h));

        // The strip went away under the keyboard focus: the focus is on a row of the set that is shown now.
        Assert.Equal("Tool.SearchResults.List", h.FocusedAutomationId);
        var focused = Assert.IsType<ListBoxItem>(h.Window.FocusManager?.GetFocusedElement());
        Assert.Equal("Hello.pas", Assert.IsType<SearchResultRow>(focused.DataContext).Key);
    }
}
