using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Search Results tool window (I-123, tool-windows.md §8).</summary>
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
}
