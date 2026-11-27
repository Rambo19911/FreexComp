using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Diff;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Themes;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Diff;

/// <summary>I-330: the diff document and its two commands (diff-viewer.md §2, IDE-DIFF-020…028).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class DiffDocumentTests
{
    /// <summary>The saved unit: line 2 is removed, line 3 changed, two lines are added after line 6 (0-based).</summary>
    private const string Saved =
        "unit Unit1;\r\ninterface\r\nuses SysUtils;\r\nprocedure A;\r\nimplementation\r\nprocedure A;\r\nbegin\r\nend;\r\nend.\r\n";

    private const string Edited =
        "unit Unit1;\r\ninterface\r\nprocedure A(X: Integer);\r\nimplementation\r\nprocedure A;\r\nbegin\r\n  Writeln;\r\n  Writeln;\r\nend;\r\nend.\r\n";

    /// <summary>300 lines; edited: line 10 changed, lines 100–101 removed, three lines added after line 200, one long line.</summary>
    private static string LongSaved() => string.Join("\r\n", Enumerable.Range(0, 300).Select(LongLine)) + "\r\n";

    private static string LongEdited()
    {
        var lines = Enumerable.Range(0, 300).Select(LongLine).ToList();
        lines.InsertRange(201, ["new a", "new b", "new c"]);
        lines.RemoveRange(100, 2);
        lines[10] = "line 10 changed";
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static string LongLine(int i) => i == 50 ? "// " + new string('x', 400) : "line " + i;

    private static async Task<(DiffDocumentViewModel Diff, DiffDocumentView View)> CompareWithSavedAsync(HeadlessHarness h, string saved, string edited)
    {
        var path = Path.Combine(h.TempDir, "Unit1.pas");
        File.WriteAllText(path, saved);
        var result = await h.Services.Documents.OpenAsync(path);
        Assert.True(result.Success, result.MessageId);
        var text = Assert.IsType<TextDocumentViewModel>(result.Value);
        text.Buffer.ReplaceAll(edited, resetUndo: false);
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Services.Commands.CanExecute("file.compareWithSaved"));
        await h.ExecuteAsync("file.compareWithSaved");
        var diff = Assert.IsType<DiffDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Dispatcher.UIThread.RunJobs();
        return (diff, h.Find<DiffDocumentView>("Document.Diff"));
    }

    private static IReadOnlyList<int?> Numbers(DiffLineNumberMargin margin, int rows) => [.. Enumerable.Range(0, rows).Select(margin.NumberAt)];

    private static ScrollViewer Scroller(DiffPaneEditor pane) => Assert.IsType<ScrollViewer>(pane.Scroller);

    /// <summary>The 0-based row whose top is nearest to one third of the viewport (IDE-DIFF-023/024).</summary>
    private static int RowAtThird(DiffPaneEditor pane)
    {
        var scroller = Scroller(pane);
        var textView = pane.Editor.TextArea.TextView;
        return textView.GetDocumentLineByVisualTop(scroller.Offset.Y + scroller.Viewport.Height / 3).LineNumber - 1;
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-021")]
    [Trait("Req", "IDE-DIFF-022")]
    public async Task SideBySideAlignsWithFillers()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var (diff, view) = await CompareWithSavedAsync(h, Saved, Edited);
        Assert.Equal(DiffViewMode.SideBySide, diff.Mode);
        var left = view.PaneOf(DiffPane.Left);
        var right = view.PaneOf(DiffPane.Right);
        Assert.True(left.IsEffectivelyVisible && right.IsEffectivelyVisible && !view.PaneOf(DiffPane.Inline).IsEffectivelyVisible);

        // Both views have one row per aligned line: removed opposite added, the rest opposite filler rows.
        DiffRowKind[] leftKinds = [DiffRowKind.Context, DiffRowKind.Context, DiffRowKind.Removed, DiffRowKind.Removed, DiffRowKind.Context,
            DiffRowKind.Context, DiffRowKind.Context, DiffRowKind.Filler, DiffRowKind.Filler, DiffRowKind.Context, DiffRowKind.Context];
        DiffRowKind[] rightKinds = [DiffRowKind.Context, DiffRowKind.Context, DiffRowKind.Added, DiffRowKind.Filler, DiffRowKind.Context,
            DiffRowKind.Context, DiffRowKind.Context, DiffRowKind.Added, DiffRowKind.Added, DiffRowKind.Context, DiffRowKind.Context];
        Assert.Equal(leftKinds, diff.Layout.Left.Select(r => r.Kind));
        Assert.Equal(rightKinds, diff.Layout.Right.Select(r => r.Kind));
        Assert.Equal(11, left.Editor.Document.LineCount); // one document line per row
        Assert.Equal(left.Editor.Document.LineCount, right.Editor.Document.LineCount);
        Assert.Equal("", left.Editor.Document.GetText(left.Editor.Document.GetLineByNumber(8)));
        Assert.Equal("  Writeln;", right.Editor.Document.GetText(right.Editor.Document.GetLineByNumber(8)));

        // Own line numbers, none on a filler row.
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, null, null, 8, 9], Numbers(Assert.Single(left.LineNumbers), 11));
        Assert.Equal([1, 2, 3, null, 4, 5, 6, 7, 8, 9, 10], Numbers(Assert.Single(right.LineNumbers), 11));

        // IDE-DIFF-022: the row colours.
        Assert.Equal("diff.removed", DiffRowRenderer.BackgroundToken(diff.Layout.Left[2].Kind));
        Assert.Equal("diff.added", DiffRowRenderer.BackgroundToken(diff.Layout.Right[2].Kind));
        Assert.Equal("diff.filler", DiffRowRenderer.BackgroundToken(diff.Layout.Right[3].Kind));
        Assert.Null(DiffRowRenderer.BackgroundToken(DiffRowKind.Context));
        Assert.Contains(left.Editor.TextArea.TextView.BackgroundRenderers, r => r is DiffRowRenderer);
        Assert.Contains(left.Editor.TextArea.TextView.LineTransformers, t => t is SyntaxColorizer);

        // Read-only: typing changes nothing.
        Assert.True(left.Editor.IsReadOnly && right.Editor.IsReadOnly);
        right.Editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        var before = right.Editor.Document.Text;
        h.Type("x");
        Assert.Equal(before, right.Editor.Document.Text);
        Assert.Empty(AutomationIdChecks.ControlsWithoutId(view));
        Assert.Empty(AutomationIdChecks.DuplicateIds(h.Window));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-021")]
    [Trait("Req", "IDE-DIFF-022")]
    public async Task ThemeSwitchRecolorsBothSides()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.Settings["appearance.theme"] = "light");
        var (_, view) = await CompareWithSavedAsync(h, Saved, Edited);
        var left = view.PaneOf(DiffPane.Left).Editor;
        var right = view.PaneOf(DiffPane.Right).Editor;
        var name = "unit ".Length; // the identifier Unit1: syntax.text

        Assert.Equal(Color.Parse("#1C211E"), Editor.SyntaxColorizerTests.ColorAt(left, name));
        Assert.Equal(Color.Parse("#0B5C8A"), Editor.SyntaxColorizerTests.ColorAt(right, 0)); // keyword

        await h.ExecuteAsync("view.theme.dark");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.Parse("#E4E8E5"), Editor.SyntaxColorizerTests.ColorAt(left, name));
        Assert.Equal(Color.Parse("#E4E8E5"), Editor.SyntaxColorizerTests.ColorAt(right, name));
        Assert.Equal(Color.Parse("#6CB6F0"), Editor.SyntaxColorizerTests.ColorAt(right, 0));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-021")]
    public async Task InlineModeShowsBothLineNumbers()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var (diff, view) = await CompareWithSavedAsync(h, Saved, Edited);
        h.Click("Diff.Mode.Inline");
        Assert.Equal(DiffViewMode.Inline, diff.Mode);
        var inline = view.PaneOf(DiffPane.Inline);
        Assert.True(inline.IsEffectivelyVisible && !view.PaneOf(DiffPane.Left).IsEffectivelyVisible);
        Assert.True(h.Find<ToggleButton>("Diff.Mode.Inline").IsChecked == true && h.Find<ToggleButton>("Diff.Mode.SideBySide").IsChecked == false);

        // Removed lines above added lines in each block; two number columns (left, right).
        Assert.Equal(
            ["unit Unit1;", "interface", "uses SysUtils;", "procedure A;", "procedure A(X: Integer);", "implementation", "procedure A;", "begin",
             "  Writeln;", "  Writeln;", "end;", "end."],
            inline.Editor.Document.Text.Split('\n'));
        Assert.Equal(2, inline.LineNumbers.Count);
        Assert.Equal([1, 2, 3, 4, null, 5, 6, 7, null, null, 8, 9], Numbers(inline.LineNumbers[0], 12));
        Assert.Equal([1, 2, null, null, 3, 4, 5, 6, 7, 8, 9, 10], Numbers(inline.LineNumbers[1], 12));
        Assert.True(inline.LineNumbers[0].Bounds.Right <= inline.LineNumbers[1].Bounds.Left);

        // The mode is UI state diff.mode; a new diff document opens in it.
        Assert.Equal("inline", h.Services.Layout.GetUiValue("diff", "mode"));
        h.Click("Diff.Mode.SideBySide");
        Assert.Equal("sideBySide", h.Services.Layout.GetUiValue("diff", "mode"));
        Assert.True(view.PaneOf(DiffPane.Left).IsEffectivelyVisible);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-024")]
    public async Task NextPreviousDifference()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var (diff, view) = await CompareWithSavedAsync(h, LongSaved(), LongEdited());
        var left = view.PaneOf(DiffPane.Left);
        Assert.Equal([10, 100, 201], diff.Blocks.Select(b => b.FirstRow));

        // On open the first block is selected; the keys of the diff scope move between blocks, scrolled to one third.
        Assert.Equal((10, 0), (diff.CaretRow, diff.CurrentBlock));
        Assert.True(left.Editor.TextArea.IsKeyboardFocusWithin);
        h.Press("Alt+Down");
        Assert.Equal((100, 1), (diff.CaretRow, diff.CurrentBlock));
        Assert.Equal(101, left.Editor.TextArea.Caret.Line);
        Assert.InRange(RowAtThird(left), 99, 101);
        h.Press("Alt+Down");
        Assert.Equal((201, 2), (diff.CaretRow, diff.CurrentBlock));
        Assert.InRange(RowAtThird(left), 200, 202);
        Assert.Equal(Scroller(left).Offset.Y, Scroller(view.PaneOf(DiffPane.Right)).Offset.Y);

        // At the end: a status message, no wrap.
        h.Press("Alt+Down");
        Assert.Equal((201, Strings.Msg_Diff_NoMoreDifferences), (diff.CaretRow, h.Status.Message));
        h.Press("Alt+Up");
        Assert.Equal(100, diff.CaretRow);
        h.Click("Diff.PreviousDifference");
        Assert.Equal(10, diff.CaretRow);
        h.Services.Status.ShowMessage("", TimeSpan.FromSeconds(1));
        await h.ExecuteAsync(DiffDocumentViewModel.PreviousDifferenceCommandId);
        Assert.Equal((10, Strings.Msg_Diff_NoMoreDifferences), (diff.CaretRow, h.Status.Message));
        h.Click("Diff.NextDifference");
        Assert.Equal(100, diff.CaretRow);

        // The read-only view does not move lines: Alt+Up is the diff command there, not edit.moveLineUp.
        Assert.Equal(LongEdited(), h.Services.Documents.Documents.OfType<TextDocumentViewModel>().Single().Buffer.Text);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-023")]
    public async Task OverviewStripMarks()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var (diff, view) = await CompareWithSavedAsync(h, LongSaved(), LongEdited());
        var strip = h.Find<DiffOverviewStrip>("Diff.Overview");
        Assert.Equal(DiffOverviewStrip.StripWidth, strip.Bounds.Width);
        Assert.Equal(view.Bounds.Width, strip.TranslatePoint(new Point(strip.Bounds.Width, 0), view)!.Value.X, 1);

        // One mark per block at its proportional position: changed (both) = accent, removed only = red, added only = green.
        var rows = diff.RowCount;
        Assert.Equal(303, rows); // 300 left lines + the three added lines opposite filler rows
        Assert.Equal(
            [(10.0 / rows, DiffMarkKind.Changed, "accent.default"), (100.0 / rows, DiffMarkKind.Removed, "status.error"),
             (201.0 / rows, DiffMarkKind.Added, "status.success")],
            diff.Marks.Select(m => (m.Top, m.Kind, DiffOverviewStrip.MarkToken(m.Kind))));
        Assert.Equal(201.0 / rows * strip.Bounds.Height, strip.MarkBounds(diff.Marks[2]).Top, 3);

        // The visible range: the viewport's share of the rows.
        var scroller = Scroller(view.PaneOf(DiffPane.Left));
        Assert.Equal(scroller.Viewport.Height / scroller.Extent.Height, strip.Visible.Height, 3);
        Assert.Equal(scroller.Offset.Y / scroller.Extent.Height, strip.Visible.Top, 3);

        // A click on a mark selects its block and scrolls it to one third.
        var mark = strip.MarkBounds(diff.Marks[2]);
        var point = strip.TranslatePoint(mark.Center, h.Window)!.Value;
        h.Window.MouseDown(point, MouseButton.Left);
        h.Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((201, 2), (diff.CaretRow, diff.CurrentBlock));
        Assert.InRange(RowAtThird(view.PaneOf(DiffPane.Left)), 200, 202);
        Assert.Equal(scroller.Offset.Y / scroller.Extent.Height, strip.Visible.Top, 3);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-020")]
    [Trait("Req", "IDE-DIFF-027")]
    public async Task CompareWithSavedOpensTab()
    {
        await using var h = await HeadlessHarness.StartAsync();
        // Disabled without a document; File menu after Revert to Saved and in the tab context menu.
        Assert.False(h.Services.Commands.CanExecute("file.compareWithSaved"));
        var (diff, _) = await CompareWithSavedAsync(h, Saved, Edited);
        var path = Path.Combine(h.TempDir, "Unit1.pas");

        Assert.Equal("Unit1.pas (saved) ↔ Unit1.pas (editor)", diff.Title);
        Assert.Equal(2, h.Services.Documents.Documents.Count);
        Assert.False(diff.IsModified);
        Assert.Null(diff.FilePath); // not saved in the session
        Assert.NotNull(h.Find<Control>(AutomationIds.DocumentTab(diff.Title)));
        Assert.Equal(("Unit1.pas (saved)", path), (h.Find<TextBlock>("Diff.LeftLabel").Text, ToolTip.GetTip(h.Find<TextBlock>("Diff.LeftLabel"))));
        Assert.Equal(("Unit1.pas (editor)", path), (h.Find<TextBlock>("Diff.RightLabel").Text, ToolTip.GetTip(h.Find<TextBlock>("Diff.RightLabel"))));
        Assert.Equal("2 changes", h.Find<TextBlock>("Diff.Changes").Text);
        Assert.False(h.Find<TextBlock>("Diff.Notes").IsEffectivelyVisible);
        Assert.False(h.Find<TextBlock>("Diff.Banner").IsEffectivelyVisible);
        foreach (var id in new[] { "Diff.Mode.SideBySide", "Diff.Mode.Inline", "Diff.IgnoreWhitespace", "Diff.PreviousDifference", "Diff.NextDifference", "Diff.Refresh" })
            Assert.True(h.Find<Control>(id).IsEffectivelyVisible, id);
        Assert.Equal("Next Difference", h.Find<Button>("Diff.NextDifference").Content);

        // The notes: a line-ending difference shows in the header in text.secondary.
        h.Services.Documents.Activate(h.Services.Documents.Documents.OfType<TextDocumentViewModel>().Single());
        h.Services.Documents.Documents.OfType<TextDocumentViewModel>().Single().Buffer.ReplaceAll(Edited.Replace("\r\n", "\n", StringComparison.Ordinal), resetUndo: false);
        await h.ExecuteAsync("file.compareWithSaved"); // the open diff is activated and refreshed
        Assert.Same(diff, h.Services.Documents.ActiveDocument);
        Dispatcher.UIThread.RunJobs();
        var notes = h.Find<TextBlock>("Diff.Notes");
        Assert.Equal("Line endings: CRLF → LF", notes.Text);
        Assert.True(notes.IsEffectivelyVisible);
        Assert.True(h.Window.TryFindResource("FxcTextSecondaryBrush", h.Window.ActualThemeVariant, out var secondary));
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(secondary).Color, Assert.IsAssignableFrom<ISolidColorBrush>(notes.Foreground).Color);
        Assert.Equal(2, h.Services.Documents.Documents.Count); // still one diff tab

        // The menu entries: File after Revert to Saved, Tools after External Tools.
        static IReadOnlyList<string> Commands(string menu) => [.. Core.Menus.MainMenuDefinition.TopLevel.Single(m => m.Key == menu).Children
            .OfType<Core.Menus.CommandMenuNode>().Select(c => c.CommandId)];
        var file = Commands("File");
        Assert.Equal("file.compareWithSaved", file[file.ToList().IndexOf("file.revert") + 1]);
        var tools = Commands("Tools");
        Assert.Equal("tools.compareFiles", tools[tools.ToList().IndexOf("tools.externalTools") + 1]);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-027")]
    public async Task CompareWithSavedIdenticalShowsStatus()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["Unit1.pas"] = Saved);
        var result = await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "Unit1.pas"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Services.Commands.CanExecute("file.compareWithSaved"));
        await h.ExecuteAsync("file.compareWithSaved");
        Assert.Equal(Strings.Msg_Diff_Identical, h.Status.Message);
        Assert.Same(result.Value, h.Services.Documents.ActiveDocument);
        Assert.Single(h.Services.Documents.Documents);

        // Not for a document without a file on disk.
        h.Services.Documents.NewUntitledUnit();
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Services.Commands.CanExecute("file.compareWithSaved"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-028")]
    public async Task CompareFilesBinaryRefused()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles[@"a\Left.pas"] = Saved;
            o.InitialFiles[@"b\Right.pas"] = Saved;
        });
        var binary = Path.Combine(h.TempDir, @"b\Data.bin");
        File.WriteAllBytes(binary, [0x4D, 0x5A, 0x00, 0x01]);
        h.Messages.Answer("Msg.Diff.Binary", "OK");

        // Two pickers in turn; a binary file → the message, no tab.
        h.FileDialogs.NextOpen(Path.Combine(h.TempDir, @"a\Left.pas"));
        h.FileDialogs.NextOpen(binary);
        await h.ExecuteAsync("tools.compareFiles");
        Assert.Equal(["Select Left File", "Select Right File"], h.FileDialogs.Requests.Select(r => r.Title));
        Assert.Equal([binary], h.Messages.Requests.Single(r => r.MessageId == "Msg.Diff.Binary").Args);
        Assert.Empty(h.Services.Documents.Documents);

        // The last folder is UI state diff.lastFolder and the next picker starts there.
        Assert.Equal(Path.Combine(h.TempDir, "b"), h.Services.Layout.GetUiValue("diff", "lastFolder"));
        h.FileDialogs.NextOpen(Path.Combine(h.TempDir, @"a\Left.pas"));
        h.FileDialogs.NextOpen(Path.Combine(h.TempDir, @"b\Right.pas"));
        await h.ExecuteAsync("tools.compareFiles");
        Assert.Equal(Path.Combine(h.TempDir, "b"), h.FileDialogs.Requests[2].InitialFolder);

        // Identical texts still open a tab, with the banner.
        var diff = Assert.IsType<DiffDocumentViewModel>(Assert.Single(h.Services.Documents.Documents));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Left.pas ↔ Right.pas", diff.Title);
        var banner = h.Find<TextBlock>("Diff.Banner");
        Assert.Equal(("The files are identical.", true), (banner.Text, banner.IsEffectivelyVisible));
        Assert.Equal("0 changes", h.Find<TextBlock>("Diff.Changes").Text);

        // A canceled picker opens nothing.
        h.FileDialogs.NextCancel();
        await h.ExecuteAsync("tools.compareFiles");
        Assert.Single(h.Services.Documents.Documents);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-021")]
    public async Task ScrollIsSynchronised()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var (_, view) = await CompareWithSavedAsync(h, LongSaved(), LongEdited());
        var left = Scroller(view.PaneOf(DiffPane.Left));
        var right = Scroller(view.PaneOf(DiffPane.Right));
        Assert.True(left.Extent.Height > left.Viewport.Height * 2);
        Assert.Equal(left.Extent.Height, right.Extent.Height);

        left.Offset = new Vector(0, 900);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(900, right.Offset.Y);
        right.Offset = new Vector(0, 1500);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1500, left.Offset.Y);

        // Horizontally where the long line is visible (the text view measures the width of its visible lines).
        var y = view.PaneOf(DiffPane.Right).Editor.TextArea.TextView.GetVisualTopByDocumentLine(51);
        right.Offset = new Vector(0, y);
        Dispatcher.UIThread.RunJobs();
        right.Offset = new Vector(120, y);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Vector(120, y), right.Offset);
        Assert.Equal(new Vector(120, y), left.Offset);

        // The mouse wheel over one view scrolls both.
        var editor = view.PaneOf(DiffPane.Right).Editor;
        var point = editor.TranslatePoint(new Point(editor.Bounds.Width / 2, editor.Bounds.Height / 2), h.Window)!.Value;
        h.Window.MouseWheel(point, new Vector(0, -3));
        Dispatcher.UIThread.RunJobs();
        Assert.True(right.Offset.Y > y);
        Assert.Equal(right.Offset, left.Offset);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-025")]
    public async Task CopyAndFindInFocusedSide()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var (diff, view) = await CompareWithSavedAsync(h, Saved, Edited);
        var left = view.PaneOf(DiffPane.Left);
        left.Editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();

        // Rows 7…10 of the left view include two filler rows: the copy has the original lines only.
        var document = left.Editor.Document;
        left.Editor.TextArea.Selection = AvaloniaEdit.Editing.Selection.Create(left.Editor.TextArea,
            document.GetLineByNumber(7).Offset, document.GetLineByNumber(10).EndOffset);
        Dispatcher.UIThread.RunJobs();
        h.Press("Ctrl+C");
        Assert.Equal("begin\r\nend;", await h.Services.Clipboard.GetTextAsync());

        // Ctrl+F searches the focused side.
        var right = view.PaneOf(DiffPane.Right);
        right.Editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(DiffPane.Right, diff.FocusedPane);
        h.Press("Ctrl+F");
        var search = h.Find<TextBox>("Diff.Right.FindPanel.SearchText");
        Assert.True(search.IsEffectivelyVisible);
        Assert.False(h.Find<Control>("Diff.Left.FindPanel").IsVisible);
        h.Type("Writeln");
        h.Press("Enter");
        Assert.Equal("Writeln", right.Editor.SelectedText);
        Assert.Equal(8, right.Editor.TextArea.Caret.Line); // row 7: the first added Writeln
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-026")]
    public async Task IgnoreWhitespaceAndRefresh()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var edited = LongEdited().Replace("line 0\r\n", "line  0 \r\n", StringComparison.Ordinal);
        var (diff, view) = await CompareWithSavedAsync(h, LongSaved(), edited);
        Assert.Equal("4 changes", diff.ChangesText);

        // Ignore whitespace re-computes (UI state diff.ignoreWhitespace); the context row shows the right side's own text.
        h.Click("Diff.IgnoreWhitespace");
        await diff.Recomputing;
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Services.Layout.GetUiFlag("diff", "ignoreWhitespace"));
        Assert.Equal("3 changes", h.Find<TextBlock>("Diff.Changes").Text);
        Assert.Equal(DiffRowKind.Context, diff.Layout.Right[0].Kind);
        Assert.Equal("line  0 ", diff.Layout.Right[0].Text);
        Assert.Equal("line 0", diff.Layout.Left[0].Text);

        // Refresh re-reads both sources and keeps the scroll position by line: a line inserted above moves the rows down.
        var left = view.PaneOf(DiffPane.Left);
        Scroller(left).Offset = new Vector(0, left.Editor.TextArea.TextView.GetVisualTopByDocumentLine(121));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(120, diff.TopRow);
        var text = h.Services.Documents.Documents.OfType<TextDocumentViewModel>().Single();
        text.Buffer.ReplaceAll(edited.Replace("line 5\r\n", "line 5\r\ninserted\r\n", StringComparison.Ordinal), resetUndo: false);
        h.Click("Diff.Refresh");
        await h.WaitForAsync(() => diff.Layout.Right[6].Text == "inserted", "the refreshed layout");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(121, diff.TopRow);
        Assert.Equal(120, diff.Layout.Left[121].LeftLine);
        Assert.Equal(121, left.Editor.TextArea.TextView.GetDocumentLineByVisualTop(Scroller(left).Offset.Y + 1).LineNumber - 1);
        Assert.Equal("inserted", view.PaneOf(DiffPane.Right).Editor.Document.GetText(view.PaneOf(DiffPane.Right).Editor.Document.GetLineByNumber(7)));

        // The algorithm comes from UI state diff.algorithm (no UI).
        Assert.Equal(DiffAlgorithm.Myers, diff.Algorithm);
        h.Services.Layout.SetUiValue("diff", "algorithm", "patience");
        Assert.Equal(DiffAlgorithm.Patience, diff.Algorithm);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-021")]
    public async Task CopyBlockToCurrent()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var (diff, view) = await CompareWithSavedAsync(h, Saved, Edited);
        var text = h.Services.Documents.Documents.OfType<TextDocumentViewModel>().Single();
        Assert.Equal(2, diff.Blocks.Length);
        Assert.Equal(0, diff.CurrentBlock);

        // The header tools wrap rather than run past the tab's right edge.
        foreach (var id in new[] { "Diff.CopyBlockToRight", "Diff.Refresh" })
        {
            var tool = h.Find<Control>(id);
            Assert.True(tool.TranslatePoint(new Point(tool.Bounds.Width, 0), view)!.Value.X <= view.Bounds.Width + 0.5, id);
        }

        // The header button and the views' context menu, enabled with the caret in a block.
        var button = h.Find<Button>("Diff.CopyBlockToRight");
        Assert.True(button.IsEffectivelyVisible);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.Equal("Copy Block to Current", button.Content);
        Assert.True(h.Services.Commands.CanExecute(DiffDocumentViewModel.CopyBlockToRightCommandId));
        Assert.Equal(["edit.copy", "edit.selectAll", null, "diff.previousDifference", "diff.nextDifference", null, "diff.copyBlockToRight"],
            diff.ContextMenu!().Select(m => m.Command?.Id));
        Assert.Equal("Copy Block to Current", diff.ContextMenu!()[^1].Label);

        // Alt+Right: the right line of the changed block becomes its two left lines; the diff re-computes and the caret
        // moves to the next block.
        var afterFirst = Edited.Replace("procedure A(X: Integer);\r\n", "uses SysUtils;\r\nprocedure A;\r\n", StringComparison.Ordinal);
        h.Press("Alt+Right");
        await h.WaitForAsync(() => diff.Blocks.Length == 1, "the re-computed diff");
        Assert.Equal(afterFirst, text.Buffer.Text);
        Assert.Equal(Strings.Msg_Diff_ChangesOne, diff.ChangesText);
        Assert.Equal((0, diff.Blocks[0].FirstRow), (diff.CurrentBlock, diff.CaretRow));
        Assert.True(diff.Blocks[0] is { HasAdded: true, HasRemoved: false });
        Assert.Contains("uses SysUtils;", diff.Right.Buffer.Text, StringComparison.Ordinal);

        // Inline mode, a block with an empty left side: its right lines are deleted; no next block, the caret stays.
        diff.Mode = DiffViewMode.Inline;
        Dispatcher.UIThread.RunJobs();
        diff.SelectBlock(0);
        var caret = diff.CaretRow;
        await h.ExecuteAsync(DiffDocumentViewModel.CopyBlockToRightCommandId);
        Assert.Equal(Saved, text.Buffer.Text);
        Assert.Empty(diff.Blocks);
        Assert.True(diff.IsIdentical);
        Assert.Equal((-1, caret), (diff.CurrentBlock, diff.CaretRow));
        Assert.False(h.Services.Commands.CanExecute(DiffDocumentViewModel.CopyBlockToRightCommandId));
        Dispatcher.UIThread.RunJobs();
        Assert.False(button.IsEffectivelyEnabled);

        // One undo step per copy.
        text.Buffer.Undo();
        Assert.Equal(afterFirst, text.Buffer.Text);
        text.Buffer.Undo();
        Assert.Equal(Edited, text.Buffer.Text);

        // A buffer changed since the computation is compared again first; nothing is copied.
        diff.Mode = DiffViewMode.SideBySide;
        await diff.RefreshAsync();
        diff.SelectBlock(0);
        text.Buffer.ReplaceAll(Edited + "// later\r\n", resetUndo: false);
        await h.ExecuteAsync(DiffDocumentViewModel.CopyBlockToRightCommandId);
        Assert.Equal(Edited + "// later\r\n", text.Buffer.Text);
        Assert.Equal(3, diff.Blocks.Length);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-021")]
    public async Task CopyBlockDisabledWithoutLiveRight()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles[@"a\Left.pas"] = Saved;
            o.InitialFiles[@"b\Right.pas"] = Edited;
        });
        var rightPath = Path.Combine(h.TempDir, @"b\Right.pas");

        // Compare Files: both sides are files; no button, no menu entry, the command and its key do nothing.
        h.FileDialogs.NextOpen(Path.Combine(h.TempDir, @"a\Left.pas"));
        h.FileDialogs.NextOpen(rightPath);
        await h.ExecuteAsync("tools.compareFiles");
        var files = Assert.IsType<DiffDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, files.CurrentBlock);
        Assert.False(files.HasLiveRight);
        Assert.False(h.Find<Button>("Diff.CopyBlockToRight").IsEffectivelyVisible);
        Assert.False(h.Services.Commands.CanExecute(DiffDocumentViewModel.CopyBlockToRightCommandId));
        Assert.DoesNotContain(files.ContextMenu!(), m => m.Command?.Id == DiffDocumentViewModel.CopyBlockToRightCommandId);
        h.Press("Alt+Right");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Edited, File.ReadAllText(rightPath));
        Assert.Equal(2, files.Blocks.Length);

        // Compare with Saved has a live right side: disabled while the caret is outside every block or the buffer is read-only.
        var (live, _) = await CompareWithSavedAsync(h, Saved, Edited);
        var text = h.Services.Documents.Documents.OfType<TextDocumentViewModel>().Single();
        Assert.True(live.HasLiveRight);
        Assert.True(h.Services.Commands.CanExecute(DiffDocumentViewModel.CopyBlockToRightCommandId));
        live.CaretRow = 0; // a context row
        Assert.Equal(-1, live.CurrentBlock);
        Assert.False(h.Services.Commands.CanExecute(DiffDocumentViewModel.CopyBlockToRightCommandId));
        Assert.False(live.CopyBlockCommand.CanExecute(null));
        live.SelectBlock(0);
        Assert.True(live.CopyBlockCommand.CanExecute(null));
        text.Buffer.IsReadOnly = true;
        Assert.False(h.Services.Commands.CanExecute(DiffDocumentViewModel.CopyBlockToRightCommandId));
        await live.CopyBlockAsync();
        Assert.Equal(Edited, text.Buffer.Text);
    }

    /// <summary>
    /// IDE-DIFF-022, theme colour lint for the new tokens: every syntax colour (but <c>syntax.inactive</c>, which reaches
    /// 4.5:1 only on white) and <c>text.primary</c> keep 4.5:1 on each diff line and span colour; <c>diff.filler</c> =
    /// <c>bg.panel</c>; a span colour stands out from its line colour (at least 1.1:1, the most the 4.5:1 rule leaves in Light).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-DIFF-022")]
    public void DiffColoursKeepContrast()
    {
        var problems = new List<string>();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var dict = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri($"avares://FreexComp.Ide/Themes/{theme}.axaml"));
            string Hex(string key) => dict[key] is Color c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : throw new InvalidOperationException(key);
            var foregrounds = dict.Keys.Select(k => k.ToString()!)
                .Where(k => k.StartsWith("FxcSyntax", StringComparison.Ordinal) && !k.EndsWith("Brush", StringComparison.Ordinal) && k != "FxcSyntaxInactive")
                .Append("FxcTextPrimary").ToList();
            Assert.True(foregrounds.Count >= 15, string.Join(", ", foregrounds));
            foreach (var bg in new[] { "FxcDiffAdded", "FxcDiffRemoved", "FxcDiffAddedInline", "FxcDiffRemovedInline" })
            {
                Assert.True(dict.ContainsKey(bg + "Brush"), bg);
                foreach (var fg in foregrounds)
                {
                    var ratio = ColorContrast.Ratio(Hex(fg), Hex(bg));
                    if (ratio < 4.5)
                        problems.Add($"{theme}: {fg} on {bg} = {ratio:0.00}");
                }
            }

            Assert.Equal(Hex("FxcBgPanel"), Hex("FxcDiffFiller"));
            Assert.True(ColorContrast.Ratio(Hex("FxcDiffAdded"), Hex("FxcDiffAddedInline")) >= 1.1, theme + ": added span too close to its line");
            Assert.True(ColorContrast.Ratio(Hex("FxcDiffRemoved"), Hex("FxcDiffRemovedInline")) >= 1.1, theme + ": removed span too close to its line");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
