using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.History;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The History tool window (I-332, F11-060; local-history.md §2 UI-TW-1620…1625): the revisions of the active document
/// after the <c>(current buffer)</c> row, the empty states, compare through the diff document, the form Part choice and the
/// label edited in place.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class HistoryWindowTests
{
    private const string FormUnit = "unit Unit1;\r\ninterface\r\nuses Classes, Controls, Forms, StdCtrls;\r\ntype\r\n  TForm1 = class(TForm)\r\n"
        + "    Button1: TButton;\r\n  end;\r\nvar\r\n  Form1: TForm1;\r\nimplementation\r\n{$R *.form}\r\nend.\r\n";

    private const string Form = "object Form1: TForm1\r\n  Caption = 'Form1'\r\n  object Button1: TButton\r\n    Caption = 'Go'\r\n  end\r\nend\r\n";

    /// <summary>The product's phase (phase 8 is delivered card by card): the History window registers.</summary>
    private static Task<HeadlessHarness> StartAsync(Action<HarnessOptions>? configure = null) =>
        BuildScenario.StartAsync(configure, implementedPhase: null);

    private static HistoryViewModel View(HeadlessHarness h) => Assert.IsType<HistoryViewModel>(h.Services.ToolWindows.Get(HistoryViewModel.ToolName));

    private static async Task<TextDocumentViewModel> OpenAsync(HeadlessHarness h, string path)
    {
        var document = Assert.IsAssignableFrom<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(path)).Value);
        Dispatcher.UIThread.RunJobs();
        return document;
    }

    /// <summary>Sets the unit text, saves, waits for the recording; then one second passes.</summary>
    private static async Task SaveAsync(HeadlessHarness h, TextDocumentViewModel document, string? text = null)
    {
        if (text is not null)
            document.Buffer.ReplaceAll(text, resetUndo: false);
        Assert.True(await h.Services.Documents.SaveAsync(document));
        await h.Services.History.Pending;
        Dispatcher.UIThread.RunJobs();
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Show(HeadlessHarness h)
    {
        h.Services.Layout.Layout.BottomSize = 300; // room for every row of the test
        Assert.True(h.Services.ToolWindows.Show(HistoryViewModel.ToolName, focus: true));
        Dispatcher.UIThread.RunJobs();
    }

    private static string Cell(HeadlessHarness h, string key, string column) => DebugScenario.Cell(h, "History", key, column);

    private static string Size(long bytes) => bytes.ToString("N0", CultureInfo.CurrentCulture);

    private static long Bytes(string path) => new FileInfo(path).Length;

    private static DiffDocumentViewModel ActiveDiff(HeadlessHarness h) => Assert.IsType<DiffDocumentViewModel>(h.Services.Documents.ActiveDocument);

    private static MenuItem? MenuItemOf(ItemsControl parent, string id) =>
        parent.Items.OfType<MenuItem>().Select(i => AutomationIds.Get(i) == id ? i : MenuItemOf(i, id)).FirstOrDefault(i => i is not null);

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1620")]
    [Trait("Req", "UI-TW-1625")]
    public async Task ListsRevisionsNewestFirst()
    {
        await using var h = await StartAsync();
        var unitPath = BuildScenario.UnitPath(h);
        var original = File.ReadAllText(unitPath);
        var unit = await OpenAsync(h, unitPath);
        await SaveAsync(h, unit, original + "// one\r\n");
        await SaveAsync(h, unit, original + "// two\r\n");
        var view = View(h);

        // Hidden by default; view.history shows it as the last tab of the bottom dock.
        Assert.Null(h.TryFind<Control>("Tool.History"));
        Assert.DoesNotContain(HistoryViewModel.ToolName, h.Services.Layout.Layout.Bottom.Tools);
        var item = MenuItemOf(h.Find<Menu>(AutomationIds.MenuBar), AutomationIds.Menu("view.history"));
        Assert.Equal("Histor_y", item!.Header);
        Show(h);
        Assert.Equal(HistoryViewModel.ToolName, h.Services.Layout.Layout.Bottom.Tools[^1]);
        Assert.Equal("HISTORY — Unit1.pas", view.Caption);
        Assert.Equal("History", view.Title); // the dock tab

        // The (current buffer) row, then the revisions newest first: the two saves and the original before the first.
        var revisions = h.Services.History.GetRevisions(unitPath);
        Assert.Equal([HistoryViewModel.CurrentKey, "3", "2", "1"], view.Rows.Select(r => r.Key));
        Assert.Equal("(current buffer)", Cell(h, "current", "Time"));
        Assert.Equal("", Cell(h, "current", "Kind"));
        Assert.Equal(Size(Bytes(unitPath)), Cell(h, "current", "Size"));
        Assert.Equal(["Saved", "Saved", "Original"], new[] { "3", "2", "1" }.Select(k => Cell(h, k, "Kind")));
        Assert.Equal(revisions.Select(HistoryViewModel.TimeText), new[] { "3", "2", "1" }.Select(k => Cell(h, k, "Time")));
        Assert.Equal(revisions[0].TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Cell(h, "3", "Time"));
        Assert.Equal(Size(Bytes(unitPath)), Cell(h, "3", "Size"));
        Assert.Equal(Size(System.Text.Encoding.UTF8.GetByteCount(original)), Cell(h, "1", "Size"));
        Assert.NotNull(h.TryFind<Control>("Tool.History.Row.3.Label"));

        // Tool bar and context menu: Compare, Label, Revert, Refresh (Revert joins with I-333: disabled).
        foreach (var id in new[] { "history.compare", "history.label", "history.revert", "history.refresh" })
            Assert.True(h.Find<Button>("Tool.History.ToolBar." + id).IsVisible, id);
        Assert.False(h.Services.Commands.CanExecute("history.revert"));
        Assert.Equal(["history.compare", "history.label", "history.revert", "history.refresh"],
            view.BuildContextMenu().Select(m => m.Command?.Id));

        // Live: a new save adds its row at the top; the part choice is not shown for a unit without form.
        await SaveAsync(h, unit, original + "// three\r\n");
        Assert.Equal([HistoryViewModel.CurrentKey, "4", "3", "2", "1"], view.Rows.Select(r => r.Key));
        Assert.False(view.ToolBarOptions!.IsVisible);
        Assert.False(h.Find<Control>("Tool.History.Part.unit").IsEffectivelyVisible);

        // The editor tab context menu: Show History.
        var tabMenu = new DocumentAreaViewModel(h.Services.Commands, h.Services.Keymap, h.Services.Documents).TabContextMenu;
        Assert.Contains(tabMenu, m => m.Command?.Id == "view.history" && m.Label == "Show &History");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1621")]
    public async Task EmptyStates()
    {
        await using var h = await StartAsync(o => o.InitialFiles[@"Loose\Other.pas"] = "unit Other;\r\nend.\r\n");
        var view = View(h);
        foreach (var open in h.Services.Documents.Documents.ToList())
            Assert.True(h.Services.Documents.Close(open));
        Show(h);

        // No active text document.
        Assert.True(view.IsEmpty);
        Assert.Equal("Open a file to see its history.", view.EmptyHeading);
        Assert.True(h.Find<Control>("Tool.History.Empty").IsEffectivelyVisible);
        Assert.Empty(view.Rows);
        Assert.Equal("HISTORY", view.Caption);

        // A file outside the project folder.
        await OpenAsync(h, Path.Combine(h.TempDir, @"Loose\Other.pas"));
        Assert.Equal("Local history is kept for files in the project folder.", view.EmptyHeading);
        Assert.Empty(view.Rows);
        Assert.Equal("HISTORY — Other.pas", view.Caption);

        // A project file without revisions: the (current buffer) row only, the message under it.
        var unit = await OpenAsync(h, BuildScenario.UnitPath(h));
        Assert.Equal([HistoryViewModel.CurrentKey], view.Rows.Select(r => r.Key));
        Assert.Equal("No saved revisions yet.", view.EmptyHeading);
        Assert.True(view.ShowsContentWhenEmpty);
        Assert.True(h.Find<Control>("Tool.History.Row.current").IsEffectivelyVisible);
        Assert.True(h.Find<Control>("Tool.History.Empty").IsEffectivelyVisible);
        Assert.False(h.Services.Commands.CanExecute("history.compare"));

        // History turned off.
        h.Services.Settings.Set(SettingsCatalog.HistoryEnabled, false);
        await h.ExecuteAsync("history.refresh");
        Assert.Equal("Local history is turned off (Options › Environment › Files).", view.EmptyHeading);
        Assert.Empty(view.Rows);

        // On again, saved once: the revisions, no empty state.
        h.Services.Settings.Set(SettingsCatalog.HistoryEnabled, true);
        await SaveAsync(h, unit, unit.Buffer.Text + "// x\r\n");
        Assert.False(view.IsEmpty);
        Assert.Equal([HistoryViewModel.CurrentKey, "2", "1"], view.Rows.Select(r => r.Key));

        // Closing the last documents: no document again.
        foreach (var open in h.Services.Documents.Documents.ToList())
            Assert.True(h.Services.Documents.Close(open));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Open a file to see its history.", view.EmptyHeading);
        Assert.Empty(view.Rows);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1623")]
    public async Task CompareTwoRevisionsOneHunk()
    {
        await using var h = await StartAsync();
        var unitPath = BuildScenario.UnitPath(h);
        var unit = await OpenAsync(h, unitPath);
        const string Text = "unit Unit1;\r\ninterface\r\nconst A = 1;\r\nimplementation\r\nend.\r\n";
        await SaveAsync(h, unit, Text);
        await SaveAsync(h, unit, Text.Replace("A = 1", "A = 2", StringComparison.Ordinal));
        var view = View(h);
        Show(h);
        Assert.Equal([HistoryViewModel.CurrentKey, "3", "2", "1"], view.Rows.Select(r => r.Key));
        var revisions = h.Services.History.GetRevisions(unitPath);

        // Two revisions differing in one line: the older left, the newer right, one change block (F11-060 acceptance).
        view.SelectedKeys = ["3", "2"];
        Assert.True(h.Services.Commands.CanExecute("history.compare"));
        await h.ExecuteAsync("history.compare");
        await view.Comparing;
        Dispatcher.UIThread.RunJobs();
        var diff = ActiveDiff(h);
        Assert.Equal($"Unit1.pas ({HistoryViewModel.TimeText(revisions[1])}) ↔ Unit1.pas ({HistoryViewModel.TimeText(revisions[0])})", diff.Title);
        Assert.Single(diff.Blocks);
        Assert.Equal(Strings.Msg_Diff_ChangesOne, diff.ChangesText); // ChangeCount 1
        Assert.Contains("A = 1", diff.Left.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("A = 2", diff.Right.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(unitPath, diff.LeftToolTip);
        h.Press("Alt+Down"); // Next Difference is the diff document's (IDE-DIFF-024)
        Assert.Equal(0, diff.CurrentBlock);

        // The window keeps the document while the diff tab is active; the same pair again activates the open tab.
        Assert.Equal([HistoryViewModel.CurrentKey, "3", "2", "1"], view.Rows.Select(r => r.Key));
        var count = h.Services.Documents.Documents.Count;
        h.Services.Documents.Activate(unit);
        view.SelectedKeys = ["2", "3"];
        await h.ExecuteAsync("history.compare");
        await view.Comparing;
        Assert.Same(diff, h.Services.Documents.ActiveDocument);
        Assert.Equal(count, h.Services.Documents.Documents.Count);

        // Disabled for no row, more than two rows, and the (current buffer) row alone.
        view.SelectedKeys = [];
        Assert.False(h.Services.Commands.CanExecute("history.compare"));
        view.SelectedKeys = ["current", "3", "2"];
        Assert.False(h.Services.Commands.CanExecute("history.compare"));
        view.SelectedKeys = ["current"];
        Assert.False(h.Services.Commands.CanExecute("history.compare"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1623")]
    public async Task CompareWithCurrentBuffer()
    {
        await using var h = await StartAsync();
        var unitPath = BuildScenario.UnitPath(h);
        var unit = await OpenAsync(h, unitPath);
        const string Text = "unit Unit1;\r\ninterface\r\nconst A = 1;\r\nimplementation\r\nend.\r\n";
        await SaveAsync(h, unit, Text);
        var view = View(h);
        Show(h);
        var saved = h.Services.History.GetRevisions(unitPath)[0];

        // One revision row with the live buffer (suffix "(editor)"); the buffer's unsaved change is the one block.
        unit.Buffer.ReplaceAll(Text.Replace("A = 1", "A = 3", StringComparison.Ordinal), resetUndo: false);
        h.Click("Tool.History.Row.2.Time");
        Assert.Equal(["2"], view.SelectedKeys);
        h.Press("Enter"); // the default action
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument is DiffDocumentViewModel, "the diff tab opened");
        await view.Comparing;
        Dispatcher.UIThread.RunJobs();
        var diff = ActiveDiff(h);
        Assert.Equal($"Unit1.pas ({HistoryViewModel.TimeText(saved)}) ↔ Unit1.pas (editor)", diff.Title);
        Assert.Single(diff.Blocks);
        Assert.Contains("A = 3", diff.Right.Buffer.Text, StringComparison.Ordinal);

        // The (current buffer) row with a revision counts as the newer side.
        h.Services.Documents.Activate(unit);
        view.SelectedKeys = ["current", "1"];
        await h.ExecuteAsync("history.compare");
        await view.Comparing;
        Assert.EndsWith("↔ Unit1.pas (editor)", ActiveDiff(h).Title, StringComparison.Ordinal);
        Assert.NotSame(diff, ActiveDiff(h));

        // Equal texts still open the tab, with the identical banner.
        h.Services.Documents.Activate(unit);
        unit.Buffer.ReplaceAll(Text, resetUndo: false);
        h.Services.Documents.Activate(unit);
        view.SelectedKeys = ["2"];
        await h.ExecuteAsync("history.compare");
        await view.Comparing;
        Dispatcher.UIThread.RunJobs();
        var identical = ActiveDiff(h);
        Assert.Same(diff, identical); // the same pair: refreshed
        Assert.Empty(identical.Blocks);
        Assert.True(identical.IsBannerVisible);
        Assert.Equal(Strings.Msg_Diff_IdenticalBanner, identical.BannerText);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1622")]
    public async Task FormPartSelectsEntry()
    {
        await using var h = await StartAsync(o =>
        {
            o.InitialFiles[@"Hello\Unit1.pas"] = FormUnit;
            o.InitialFiles[@"Hello\Unit1.form"] = Form;
        });
        var unitPath = BuildScenario.UnitPath(h);
        var formPath = Path.ChangeExtension(unitPath, ".form");
        var document = Assert.IsType<FormDocumentViewModel>((await h.Services.Documents.OpenAsync(unitPath)).Value);
        Dispatcher.UIThread.RunJobs();

        // A unit change, then a form change (Text view): two saves after the original.
        await SaveAsync(h, document, FormUnit.Replace("end.", "// unit change\r\nend.", StringComparison.Ordinal));
        Assert.True(document.ShowView(DocumentView.Text));
        Dispatcher.UIThread.RunJobs();
        document.FormBuffer.ReplaceAll(Form.Replace("'Form1'", "'Changed'", StringComparison.Ordinal), resetUndo: false);
        Assert.True(document.ShowView(DocumentView.Code));
        Dispatcher.UIThread.RunJobs();
        await SaveAsync(h, document);
        var view = View(h);
        Show(h);
        var revisions = h.Services.History.GetRevisions(unitPath);
        Assert.Equal(3, revisions.Count);

        // The Part radio buttons: shown, Unit by default; Size follows the part.
        Assert.True(view.ToolBarOptions!.IsVisible);
        Assert.True(h.Find<RadioButton>("Tool.History.Part.unit").IsChecked);
        Assert.Equal(HistoryRole.Unit, view.Role);
        Assert.Equal(Size(Bytes(unitPath)), Cell(h, "3", "Size"));
        h.Click("Tool.History.Part.form");
        Assert.Equal(HistoryRole.Form, view.Role);
        Assert.Equal("form", h.Services.Layout.GetUiValue("history", "part"));
        Assert.Equal(Size(Bytes(formPath)), Cell(h, "3", "Size"));
        Assert.Equal(Size(revisions[2].Entry(HistoryRole.Form)!.Size), Cell(h, "1", "Size"));

        // Compare uses the chosen part: the form changed between the two saves, the unit did not.
        view.SelectedKeys = ["3", "2"];
        await h.ExecuteAsync("history.compare");
        await view.Comparing;
        Dispatcher.UIThread.RunJobs();
        var formDiff = ActiveDiff(h);
        Assert.StartsWith("Unit1.form (", formDiff.Title, StringComparison.Ordinal);
        Assert.NotEmpty(formDiff.Blocks); // the designer writes the form text in its own layout
        Assert.DoesNotContain("'Changed'", formDiff.Left.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("'Changed'", formDiff.Right.Buffer.Text, StringComparison.Ordinal);
        h.Services.Documents.Activate(document);
        Dispatcher.UIThread.RunJobs();
        h.Click("Tool.History.Part.unit");
        view.SelectedKeys = ["3", "2"];
        await h.ExecuteAsync("history.compare");
        await view.Comparing;
        Dispatcher.UIThread.RunJobs();
        var unitDiff = ActiveDiff(h);
        Assert.StartsWith("Unit1.pas (", unitDiff.Title, StringComparison.Ordinal);
        Assert.Empty(unitDiff.Blocks);
        Assert.True(unitDiff.IsBannerVisible);

        // A plain unit hides the choice again.
        var main = await OpenAsync(h, Path.Combine(h.TempDir, @"Hello\Hello.pas"));
        Assert.Same(main, view.Document);
        Assert.False(view.ToolBarOptions.IsVisible);
        Assert.Equal(HistoryRole.Text, view.Role);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-1624")]
    public async Task LabelEditInPlace()
    {
        await using var h = await StartAsync();
        var unitPath = BuildScenario.UnitPath(h);
        var unit = await OpenAsync(h, unitPath);
        await SaveAsync(h, unit, unit.Buffer.Text + "// one\r\n");
        var view = View(h);
        Show(h);

        // F2 on a revision row edits its Label cell; Enter commits through SetLabel (trimmed).
        h.Click("Tool.History.Row.2.Time");
        Assert.True(h.Services.Commands.CanExecute("history.label"));
        h.Press("F2");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.History.Row.2.Label.Edit", "the label input has the focus");
        h.Type("  before refactor  ");
        h.Press("Enter");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(view.Edit);
        Assert.Equal("before refactor", h.Services.History.GetRevisions(unitPath)[0].Label);
        Assert.Equal("before refactor", Cell(h, "2", "Label"));

        // Escape cancels.
        h.Press("F2");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Tool.History.Row.2.Label.Edit", "the label input has the focus");
        Assert.Equal("before refactor", h.Find<TextBox>("Tool.History.Row.2.Label.Edit").Text);
        h.Find<TextBox>("Tool.History.Row.2.Label.Edit").Text = "discarded";
        h.Press("Escape");
        Assert.Null(view.Edit);
        Assert.Equal("before refactor", h.Services.History.GetRevisions(unitPath)[0].Label);

        // Double-click on the Label cell edits it; an empty label removes it.
        var row = view.Rows.Single(r => r.Key == "1");
        await view.ActivateAsync(row, "Label");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(("1", "Label"), (view.Edit!.RowKey, view.Edit.Column));
        await view.Edit.CommitAsync("original state");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("original state", Cell(h, "1", "Label"));
        h.Services.Documents.Activate(unit);
        view.SelectedKeys = ["1"];
        await h.ExecuteAsync("history.label");
        await view.Edit!.CommitAsync("   ");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.Services.History.GetRevisions(unitPath)[1].Label);
        Assert.Equal("", Cell(h, "1", "Label"));

        // Not for the (current buffer) row, nor for two rows.
        view.SelectedKeys = ["current"];
        Assert.False(h.Services.Commands.CanExecute("history.label"));
        await view.ActivateAsync(view.Rows[0], "Label");
        Assert.Null(view.Edit);
        view.SelectedKeys = ["2", "1"];
        Assert.False(h.Services.Commands.CanExecute("history.label"));
    }
}
