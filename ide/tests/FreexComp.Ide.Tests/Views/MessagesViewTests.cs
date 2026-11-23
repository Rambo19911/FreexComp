using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Messages tool window (I-100, I-101; tool-windows.md §6, phase3-build-run.md §7.1).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class MessagesViewTests
{
    private static Diagnostic Diag(HeadlessHarness h, string code, DiagnosticSeverity severity, string message, int line, int column, int length = 0) =>
        FakeBuildEngine.Diag(code, severity, message, BuildScenario.UnitPath(h), line, column, length);

    /// <summary>2 errors (the first with a related location), 1 warning, 1 hint; builds and waits for Idle.</summary>
    private static async Task BuildWithMessagesAsync(HeadlessHarness h)
    {
        var related = new RelatedLocation(new FreexComp.Compiler.Text.Location(BuildScenario.UnitPath(h), default,
            new FreexComp.Compiler.Text.LinePositionSpan(new(3, 4), new(3, 8))), "Declared here");
        h.Build.Enqueue(FakeBuildEngine.Failure(BuildScenario.ProjectPath(h),
            Diag(h, "E2003", DiagnosticSeverity.Error, "Undeclared identifier 'Foo'", 6, 3, 3) with { Related = [related] },
            Diag(h, "E2010", DiagnosticSeverity.Error, "Incompatible types: 'Integer' and 'string'", 4, 5, 4),
            Diag(h, "W1002", DiagnosticSeverity.Warning, "Variable 'Foo1' is declared but never used", 4, 5),
            Diag(h, "H2077", DiagnosticSeverity.Hint, "Value assigned to 'Foo' never used", 6, 3)));
        h.Press("Ctrl+Shift+B");
        await h.WaitForAsync(() => h.Services.State.Current.State == Core.Models.IdeState.Idle && h.Services.Build.ResultText is not null,
            "build finished");
        Dispatcher.UIThread.RunJobs();
    }

    private static string Name(HeadlessHarness h, string id) => AutomationProperties.GetName(h.Find<Control>(id)) ?? "";

    /// <summary>Scrolls the row into view (the list is virtualized) and returns the cell or row control.</summary>
    private static T Row<T>(HeadlessHarness h, string key, string? cell = null) where T : Control
    {
        var list = h.Find<ListBox>("Tool.Messages.List");
        if (BuildScenario.Messages(h).Rows.FirstOrDefault(r => r.Key == key) is { } row)
            list.ScrollIntoView(row);
        Dispatcher.UIThread.RunJobs();
        return h.Find<T>("Tool.Messages.Row." + key + (cell is null ? "" : "." + cell));
    }

    private static bool HasRow(HeadlessHarness h, string key) => BuildScenario.Messages(h).Rows.Any(r => r.Key == key);

    /// <summary>UI-TW-406, -407, -410, -403: toggles with counts, filter, filtered-empty state, tab counts, related rows.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-406")]
    [Trait("Req", "UI-TW-407")]
    [Trait("Req", "UI-TW-410")]
    [Trait("Req", "UI-TW-403")]
    [Trait("Req", "UI-TW-409")]
    [Trait("Req", "UI-TW-411")]
    [Trait("Req", "UI-TW-400")]
    [Trait("Req", "UI-TW-402")]
    [Trait("Req", "IDE-P3-067")]
    public async Task SeverityTogglesAndFilter()
    {
        await using var h = await BuildScenario.StartAsync();
        await BuildWithMessagesAsync(h);

        // UI-TW-409: the tab was activated after a build with errors.
        Assert.Equal(MessagesViewModel.ToolName, h.Services.Layout.Layout.Bottom.ActiveTool);
        foreach (var key in (string[])["0", "1", "2", "3", "summary"])
            Assert.NotNull(Row<Control>(h, key));
        // UI-TW-400: the diagnostics of the last build plus one summary row, which is last.
        Assert.Equal(["0", "1", "2", "3", "summary"], BuildScenario.Messages(h).Rows.Select(r => r.Key));
        // UI-TW-402: no row tint; the severity is the icon and its word; the summary row is type.bodyStrong.
        Assert.Null(Row<Grid>(h, "0").Background);
        Assert.Equal(FontWeight.Normal, Row<TextBlock>(h, "0", "Description").FontWeight);
        Assert.Equal(FontWeight.SemiBold, Row<TextBlock>(h, "summary", "Description").FontWeight);
        Assert.Equal(Strings.MessagesView_Severity_Warning, ToolTip.GetTip(Row<Control>(h, "2", "Severity")));
        Assert.Equal("E2003", Row<TextBlock>(h, "0", "Code").Text);
        Assert.Equal("Unit1.pas", h.Find<TextBlock>("Tool.Messages.Row.0.File").Text);
        Assert.Equal(("6", "3"), (h.Find<TextBlock>("Tool.Messages.Row.0.Line").Text, h.Find<TextBlock>("Tool.Messages.Row.0.Col").Text));
        Assert.Equal(Strings.MessagesView_Severity_Error, Name(h, "Tool.Messages.Row.0.Severity"));
        Assert.Equal(Strings.Msg_Build_FailedMany.Replace("{0}", "2", StringComparison.Ordinal), Row<TextBlock>(h, "summary", "Description").Text);
        Assert.Equal(["2 Errors", "1 Warning", "1 Hint"],
            ((string[])["toggleErrors", "toggleWarnings", "toggleHints"]).Select(t => Name(h, "Tool.Messages.ToolBar.messages." + t)));
        Assert.Equal("Messages (2 ⊘, 1 ⚠)", BuildScenario.Messages(h).Title);
        Assert.Equal("Messages, 2 errors, 1 warning", Name(h, "ToolTab.Messages"));

        // A released toggle hides its rows; the summary stays; counts do not change.
        h.Click("Tool.Messages.ToolBar.messages.toggleWarnings");
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Services.Settings.Get(SettingsCatalog.MessagesShowWarnings));
        Assert.False(HasRow(h, "2"));
        Assert.NotNull(Row<Control>(h, "summary"));
        Assert.Equal("1 Warning", Name(h, "Tool.Messages.ToolBar.messages.toggleWarnings"));
        Assert.False(h.Find<ToggleButton>("Tool.Messages.ToolBar.messages.toggleWarnings").IsChecked);

        // The filter matches code, description and file.
        h.Find<TextBox>("Tool.Messages.Filter").Text = "E2010";
        h.Clock.Advance(ToolWindowViewModel.FilterDelay);
        Dispatcher.UIThread.RunJobs();
        Assert.False(HasRow(h, "0"));
        Assert.NotNull(Row<Control>(h, "1"));
        Assert.False(h.Find<Control>("Tool.Messages.FilteredEmpty").IsVisible);

        // Everything hidden: the filtered-empty block; Clear Filter clears the box and presses every toggle.
        h.Find<TextBox>("Tool.Messages.Filter").Text = "zzz";
        h.Clock.Advance(ToolWindowViewModel.FilterDelay);
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Find<Control>("Tool.Messages.FilteredEmpty").IsVisible);
        h.Click("Tool.Messages.FilteredEmpty.ClearFilter");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", h.Find<TextBox>("Tool.Messages.Filter").Text);
        Assert.True(h.Services.Settings.Get(SettingsCatalog.MessagesShowWarnings));
        Assert.NotNull(Row<Control>(h, "2"));
        Assert.False(h.Find<Control>("Tool.Messages.FilteredEmpty").IsVisible);

        // Related rows: collapsed by default, Right expands, Left collapses.
        Assert.True(h.Services.ToolWindows.Show(MessagesViewModel.ToolName, focus: true));
        Dispatcher.UIThread.RunJobs();
        var list = h.Find<ListBox>("Tool.Messages.List");
        list.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.False(HasRow(h, "0.r0"));
        h.Press("Right");
        Assert.Equal("↳ Declared here", Row<TextBlock>(h, "0.r0", "Description").Text);
        h.Press("Left");
        Assert.False(HasRow(h, "0.r0"));

        // messages.clear: the empty state, counts gone; Output keeps its lines (IDE-P3-067).
        var outputLines = h.Services.Output.GetLines(OutputChannels.Build).Count;
        await h.ExecuteAsync("messages.clear");
        Assert.True(h.Find<Control>("Tool.Messages.Empty").IsVisible);
        Assert.Equal("Messages", BuildScenario.Messages(h).Title);
        Assert.Empty(h.Services.Messages.Items);
        Assert.Equal((0, 0, 0), (h.Services.Messages.ErrorCount, h.Services.Messages.WarningCount, h.Services.Messages.HintCount));
        Assert.Equal(outputLines, h.Services.Output.GetLines(OutputChannels.Build).Count);

        // Closing the project clears Messages.
        await BuildWithMessagesAsync(h);
        Assert.NotEmpty(h.Services.Messages.Items);
        await h.ExecuteAsync("file.closeProject");
        Assert.Null(h.Services.Projects.Project);
        Assert.Empty(h.Services.Messages.Items);
    }

    /// <summary>UI-TW-408: Ctrl+C copies the selected rows in the text diagnostic format; Copy All copies every row.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-408")]
    public async Task CopyUsesDiagnosticFormat()
    {
        await using var h = await BuildScenario.StartAsync();
        await BuildWithMessagesAsync(h);
        var unit = BuildScenario.UnitPath(h);
        Assert.True(h.Services.ToolWindows.Show(MessagesViewModel.ToolName, focus: true));
        Dispatcher.UIThread.RunJobs();
        var list = h.Find<ListBox>("Tool.Messages.List");
        list.SelectedItems!.Add(BuildScenario.Messages(h).Rows[0]);
        list.SelectedItems!.Add(BuildScenario.Messages(h).Rows[2]);
        Dispatcher.UIThread.RunJobs();

        h.Press("Ctrl+C");
        var clipboard = (FakeClipboard)h.Services.Clipboard;
        await h.WaitForAsync(() => clipboard.Text is not null, "copied");
        Assert.Equal($"{unit}(6,3): error E2003: Undeclared identifier 'Foo'\r\n{unit}(4,5): warning W1002: Variable 'Foo1' is declared but never used",
            clipboard.Text);

        await h.ExecuteAsync("messages.copyAll");
        var lines = clipboard.Text!.Split("\r\n");
        Assert.Equal(6, lines.Length); // 4 diagnostics, the related location, the summary
        Assert.Equal($"{unit}(4,5): info : Declared here".Replace(" : ", ": ", StringComparison.Ordinal), lines[1]);
        Assert.Equal(Strings.Msg_Build_FailedMany.Replace("{0}", "2", StringComparison.Ordinal), lines[^1]);
    }

    /// <summary>UI-TW-405: F8 / Shift+F8 visit the visible rows with a file, wrap, and keep the focus in the editor.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-405")]
    [Trait("Req", "IDE-P3-066")]
    [Trait("Req", "IDE-ED-103")]
    public async Task F8CyclesVisibleRows()
    {
        await using var h = await BuildScenario.StartAsync();
        await BuildWithMessagesAsync(h);
        await h.ExecuteAsync("messages.toggleHints"); // the hint row is hidden: F8 skips it

        h.Press("F8");
        await h.WaitForAsync(() => h.Services.Messages.Current?.Code == "E2003" && h.Services.Commands.CanExecute("search.nextMessage"), "first row");
        h.Press("F8");
        await h.WaitForAsync(() => h.Services.Messages.Current?.Code == "E2010" && h.Services.Commands.CanExecute("search.nextMessage"), "second row");
        h.Press("F8");
        await h.WaitForAsync(() => h.Services.Messages.Current?.Code == "W1002" && h.Services.Commands.CanExecute("search.nextMessage"), "third row");
        h.Press("F8");
        await h.WaitForAsync(() => h.Services.Messages.Current?.Code == "E2003" && h.Services.Commands.CanExecute("search.nextMessage"), "wrapped to the first row");
        h.Press("Shift+F8");
        await h.WaitForAsync(() => h.Services.Messages.Current?.Code == "W1002" && h.Services.Commands.CanExecute("search.nextMessage"), "wrapped back to the last visible row");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Unit1.pas", "editor focused");
        Assert.Same(BuildScenario.Messages(h).Rows[2], h.Find<ListBox>("Tool.Messages.List").SelectedItem);
    }

    /// <summary>Phase 3 "error navigation tested": double-click opens the file, caret at line:col, span selected, editor focused.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-404")]
    [Trait("Req", "IDE-P3-065")]
    public async Task DoubleClickNavigatesToErrorPosition()
    {
        await using var h = await BuildScenario.StartAsync();
        await BuildWithMessagesAsync(h);

        Row<Control>(h, "0");
        h.DoubleClick("Tool.Messages.Row.0.Description");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument is TextDocumentViewModel { Title: "Unit1.pas" }, "Unit1.pas active");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Unit1.pas", "editor focused");
        var doc = (TextDocumentViewModel)h.Services.Documents.ActiveDocument!;
        Assert.Equal(new TextPosition(6, 6), doc.ViewState.Caret);
        Assert.Equal("Foo", doc.Buffer.GetText(doc.Selection.Start, doc.Selection.End - doc.Selection.Start));

        // The summary row has no file: nothing happens.
        Row<Control>(h, "summary");
        h.DoubleClick("Tool.Messages.Row.summary.Description");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new TextPosition(6, 6), doc.ViewState.Caret);
    }

    /// <summary>Keyboard navigation of errors: Enter on the selected row (messages.goToSource), then F8.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-404")]
    [Trait("Req", "UI-TW-405")]
    public async Task EnterAndF8NavigateByKeyboard()
    {
        await using var h = await BuildScenario.StartAsync();
        await BuildWithMessagesAsync(h);
        Assert.True(h.Services.ToolWindows.Show(MessagesViewModel.ToolName, focus: true));
        Dispatcher.UIThread.RunJobs();
        var list = h.Find<ListBox>("Tool.Messages.List");
        await h.WaitForAsync(() => h.FocusedAutomationId?.StartsWith("Tool.Messages", StringComparison.Ordinal) == true, "list focused");
        list.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        h.Press("Enter");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Unit1.pas", "editor focused");
        var doc = (TextDocumentViewModel)h.Services.Documents.ActiveDocument!;
        Assert.Equal(new TextPosition(4, 9), doc.ViewState.Caret); // E2010 at 4:5, span of 4 selected

        h.Press("F8");
        await h.WaitForAsync(() => h.Services.Messages.Current?.Code == "W1002" && h.Services.Commands.CanExecute("search.nextMessage"), "next row");
        Assert.Equal(4, doc.ViewState.Caret.Line);
        Assert.Equal("Editor.Unit1.pas", h.FocusedAutomationId);
        Assert.Equal(MessagesViewModel.ToolName, h.Services.Layout.Layout.Bottom.ActiveTool);
    }

    /// <summary>UI-TW-405 / IDE-P3-066 (I-190): F8 visits the related rows of an expanded diagnostic, in list order.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-405")]
    [Trait("Req", "IDE-P3-066")]
    public async Task F8VisitsExpandedRelatedRows()
    {
        await using var h = await BuildScenario.StartAsync();
        await BuildWithMessagesAsync(h);
        var messages = BuildScenario.Messages(h);

        async Task<string> NextAsync()
        {
            var before = h.Services.Messages.Current;
            await h.WaitForAsync(() => h.Services.Commands.CanExecute("search.nextMessage"), "Next Message available");
            h.Press("F8");
            await h.WaitForAsync(() => h.Services.Messages.Current != before, "F8 moved");
            return h.Services.Messages.Current!.Text;
        }

        // Collapsed: the related row is skipped.
        Assert.Equal("Undeclared identifier 'Foo'", await NextAsync());
        Assert.Equal("Incompatible types: 'Integer' and 'string'", await NextAsync());

        // Expanded: E2003 → its related row (4:5, the span of 4 selected) → E2010.
        messages.SetExpanded(messages.Rows.Single(r => r.Key == "0"), expanded: true);
        Assert.Contains(messages.Rows, r => r.Key == "0.r0");
        await messages.GoToAsync(messages.Rows.Single(r => r.Key == "0"));
        Assert.Equal("Declared here", await NextAsync());
        var doc = (TextDocumentViewModel)h.Services.Documents.ActiveDocument!;
        Assert.Equal(new TextPosition(4, 9), doc.ViewState.Caret);
        Assert.Equal("0.r0", messages.CurrentRow?.Key); // the list selects the related row
        Assert.Equal("Incompatible types: 'Integer' and 'string'", await NextAsync());
    }

    /// <summary>IDE-P3-065 (I-190): a file outside the project opens read-only from Messages; project files stay writable.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-065")]
    public async Task NonProjectFileOpensReadOnly()
    {
        await using var h = await BuildScenario.StartAsync(o =>
        {
            o.InitialFiles[@"lib\Lib.pas"] = "unit Lib;\r\ninterface\r\nimplementation\r\nend.\r\n";
            o.InitialFiles[@"lib\Open.pas"] = "unit Open;\r\ninterface\r\nimplementation\r\nend.\r\n";
        });
        var lib = Path.Combine(h.TempDir, @"lib\Lib.pas");
        var open = Path.Combine(h.TempDir, @"lib\Open.pas");
        var alreadyOpen = (TextDocumentViewModel)(await h.Services.Documents.OpenAsync(open)).Value!;
        h.Build.Enqueue(FakeBuildEngine.Failure(BuildScenario.ProjectPath(h),
            FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "in a library", lib, 2, 1, 9),
            FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "in the project", BuildScenario.UnitPath(h), 6, 3, 3),
            FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "in an open file", open, 2, 1, 9)));
        h.Press("Ctrl+Shift+B");
        await h.WaitForAsync(() => h.Services.State.Current.State == Core.Models.IdeState.Idle && h.Services.Build.ResultText is not null,
            "build finished");
        var items = h.Services.Messages.Items;

        await h.Services.Messages.NavigateToAsync(items[0]);
        var libDoc = Assert.IsType<TextDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Assert.Equal(lib, libDoc.FilePath);
        Assert.True(libDoc.Buffer.IsReadOnly);
        Assert.Equal(new TextPosition(2, 10), libDoc.ViewState.Caret);

        await h.Services.Messages.NavigateToAsync(items[1]);
        Assert.False(((TextDocumentViewModel)h.Services.Documents.ActiveDocument!).Buffer.IsReadOnly);
        await h.Services.Messages.NavigateToAsync(items[2]);
        Assert.Same(alreadyOpen, h.Services.Documents.ActiveDocument);
        Assert.False(alreadyOpen.Buffer.IsReadOnly); // a document the user opened keeps its state
    }

    private static string Header(HeadlessHarness h, string column) =>
        ((TextBlock)h.Find<Border>("Tool.Messages.Column." + column).Child!).Text ?? "";

    private static void SortBy(HeadlessHarness h, string column)
    {
        h.Click("Tool.Messages.Column." + column);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// UI-TW-401 (I-212), UI-TW-011: a header click cycles ascending → descending → unsorted (arrival order); ties keep
    /// arrival order; related rows stay under their diagnostic; the summary row stays last; Col is not sortable.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-401")]
    public async Task ColumnsSortWithArrivalOrderAsTieBreaker()
    {
        await using var h = await BuildScenario.StartAsync();
        await BuildWithMessagesAsync(h); // 0 E2003 line 6, 1 E2010 line 4, 2 W1002 line 4, 3 H2077 line 6
        List<string> Keys() => [.. BuildScenario.Messages(h).Rows.Select(r => r.Key)];
        Assert.Equal(["0", "1", "2", "3", "summary"], Keys());
        Assert.Equal(Strings.MessagesView_Column_Line, Header(h, "Line"));

        SortBy(h, "Line");
        Assert.Equal(["1", "2", "0", "3", "summary"], Keys());
        Assert.Equal(Strings.MessagesView_Column_Line + " ▲", Header(h, "Line"));
        SortBy(h, "Line");
        Assert.Equal(["0", "3", "1", "2", "summary"], Keys()); // descending; ties still in arrival order
        Assert.Equal(Strings.MessagesView_Column_Line + " ▼", Header(h, "Line"));
        SortBy(h, "Line");
        Assert.Equal(["0", "1", "2", "3", "summary"], Keys()); // unsorted = natural order
        Assert.Equal(Strings.MessagesView_Column_Line, Header(h, "Line"));

        SortBy(h, "Code");
        Assert.Equal(["0", "1", "3", "2", "summary"], Keys());
        SortBy(h, "Description"); // another column starts ascending, the previous one loses its glyph
        Assert.Equal(["1", "0", "3", "2", "summary"], Keys());
        Assert.Equal(Strings.MessagesView_Column_Code, Header(h, "Code"));
        SortBy(h, "Severity");
        Assert.Equal(["0", "1", "2", "3", "summary"], Keys()); // error < warning < hint
        SortBy(h, "Severity");
        Assert.Equal(["3", "2", "0", "1", "summary"], Keys());
        SortBy(h, "File"); // one file: arrival order
        Assert.Equal(["0", "1", "2", "3", "summary"], Keys());
        SortBy(h, "Col"); // not sortable: nothing changes
        Assert.Equal(["0", "1", "2", "3", "summary"], Keys());
        Assert.Equal(Strings.MessagesView_Column_Col, Header(h, "Col"));
        Assert.Equal(Strings.MessagesView_Column_File + " ▲", Header(h, "File"));

        // Related rows follow their diagnostic; the filter still applies and the summary stays last and visible.
        SortBy(h, "File");
        SortBy(h, "File");
        SortBy(h, "Line");
        BuildScenario.Messages(h).SetExpanded(BuildScenario.Messages(h).Rows.Single(r => r.Key == "0"), true);
        Assert.Equal(["1", "2", "0", "0.r0", "3", "summary"], Keys());
        h.Find<TextBox>("Tool.Messages.Filter").Text = "E20";
        h.Clock.Advance(ToolWindowViewModel.FilterDelay);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["1", "0", "0.r0", "summary"], Keys());
    }
}
