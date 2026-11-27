using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Diff;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Preview dialog of a refactoring (I-350, F13-003; dialog <c>RefactoringPreview</c>, UI-DLG-360…367), shown by the
/// engine for a workspace edit applied with <see cref="PreviewMode.Always"/>: the real dialog over the main window.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class RefactoringPreviewTests
{
    internal const string Title = "Rename Symbol";
    private const string Prefix = "Dialog.RefactoringPreview.";

    internal const string ProjectFile = """
        { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
          "files": [ { "path": "App.pas" }, { "path": "A.pas" }, { "path": "B.pas" }, { "path": "C.pas" } ] }
        """;

    /// <summary><c>Old</c> on the lines 5, 9 and 15: three changes that are three change blocks.</summary>
    internal const string UnitA =
        "unit A;\n\ninterface\n\nprocedure Old;\n\nimplementation\n\nprocedure Old;\nbegin\nend;\n\nprocedure Other;\nbegin\n  Old;\nend;\n\nend.\n";

    internal static string Unit(string name) =>
        $"unit {name};\n\ninterface\n\nprocedure Old;\n\nimplementation\n\nprocedure Old;\nbegin\nend;\n\nend.\n";

    /// <summary>The file edit that replaces the first <paramref name="count"/> <c>Old</c> of <paramref name="baseText"/> by <c>Renamed</c>.</summary>
    internal static FileEdit Rename(string path, string baseText, int count = int.MaxValue)
    {
        var edits = new List<TextEdit>();
        for (var at = baseText.IndexOf("Old", StringComparison.Ordinal); at >= 0 && edits.Count < count; at = baseText.IndexOf("Old", at + 3, StringComparison.Ordinal))
            edits.Add(new TextEdit(at, 3, "Renamed"));
        Assert.NotEmpty(edits);
        return new FileEdit(path, baseText, [.. edits]);
    }

    /// <summary>The project with the units A, B, C and <c>Sub\A.pas</c> (under the project directory, not a member), open.</summary>
    internal static async Task<HeadlessHarness> StartAsync(Action<HarnessOptions>? configure = null)
    {
        var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["App.pproj"] = ProjectFile;
            o.InitialFiles["App.pas"] = "program App;\n\nuses A, B, C;\n\nbegin\nend.\n";
            o.InitialFiles["A.pas"] = UnitA;
            o.InitialFiles["B.pas"] = Unit("B");
            o.InitialFiles["C.pas"] = Unit("C");
            o.InitialFiles[@"Sub\A.pas"] = Unit("A");
            configure?.Invoke(o);
        });
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "App.pproj")));
        Dispatcher.UIThread.RunJobs();
        return h;
    }

    internal static string PathOf(HeadlessHarness h, string name) => Path.Combine(h.TempDir, name);

    internal static async Task<TextDocumentViewModel> OpenAsync(HeadlessHarness h, string name)
    {
        var document = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(PathOf(h, name))).Value);
        Dispatcher.UIThread.RunJobs();
        return document;
    }

    /// <summary>
    /// Five edits in three files: C.pas open, A.pas open and active (three edits), B.pas closed. The files are given out
    /// of order; the dialog lists A (the active document) first, then B and C.
    /// </summary>
    private static async Task<(WorkspaceEdit Edit, TextDocumentViewModel A, TextDocumentViewModel C)> FiveEditsAsync(HeadlessHarness h)
    {
        var c = await OpenAsync(h, "C.pas");
        var a = await OpenAsync(h, "A.pas");
        Assert.Same(a, h.Services.Documents.ActiveDocument);
        var edit = new WorkspaceEdit(Title,
        [
            Rename(PathOf(h, "C.pas"), c.Buffer.Text, 1), Rename(PathOf(h, "B.pas"), Unit("B"), 1), Rename(PathOf(h, "A.pas"), a.Buffer.Text),
        ]);
        return (edit, a, c);
    }

    /// <summary>Starts the operation and returns the open dialog, its diff computed and shown.</summary>
    internal static async Task<(Task<RefactoringResult> Running, DialogDriver Dialog, RefactoringPreviewViewModel Model)> ShowAsync(HeadlessHarness h, WorkspaceEdit edit)
    {
        var running = h.Services.Refactoring.ApplyAsync(edit, PreviewMode.Always);
        var dlg = await h.WaitForDialogAsync(RefactoringPreviewViewModel.DialogName);
        var model = Assert.IsType<RefactoringPreviewViewModel>(dlg.Window.Model);
        Assert.IsType<RefactoringPreviewWindow>(dlg.Window);
        await DiffAsync(h, model);
        return (running, dlg, model);
    }

    /// <summary>Waits until the dialog's diff has followed the last selection or check box change.</summary>
    internal static async Task DiffAsync(HeadlessHarness h, RefactoringPreviewViewModel model)
    {
        await h.WaitForAsync(() => model.DiffUpdate.IsCompleted, "the preview's diff computed");
        await model.DiffUpdate;
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<RefactoringResult> EndAsync(HeadlessHarness h, Task<RefactoringResult> running)
    {
        await h.WaitForAsync(() => running.IsCompleted, "the operation ended");
        Dispatcher.UIThread.RunJobs();
        return await running;
    }

    private static CheckBox Check(DialogDriver dlg, string row) => dlg.Find<CheckBox>(Prefix + row + ".Check");

    private static string[] RowIds(DialogDriver dlg) =>
        [.. dlg.Find<ListBox>("Tree").GetVisualDescendants().OfType<ListBoxItem>().Where(i => i.IsVisible).Select(i => AutomationIds.Get(i)!)];

    private static object Brush(DialogDriver dlg, string key) =>
        dlg.Window.TryFindResource(key, dlg.Window.ActualThemeVariant, out var brush) && brush is not null ? brush : throw new InvalidOperationException(key);

    /// <summary>The parts of an edit row's text: (text, token of its background or "").</summary>
    private static (string Text, string Token)[] Parts(DialogDriver dlg, string row)
    {
        var (removed, added) = (Brush(dlg, "FxcDiffRemovedInlineBrush"), Brush(dlg, "FxcDiffAddedInlineBrush"));
        return [.. dlg.Find<TextBlock>(Prefix + row + ".Text").Inlines!.OfType<Run>().Select(r =>
            (r.Text ?? "", ReferenceEquals(r.Background, removed) ? "removed" : ReferenceEquals(r.Background, added) ? "added" : r.Background is null ? "" : "?"))];
    }

    /// <summary>
    /// UI-DLG-363, 365, IDE-REFAC-007 (F13-003 acceptance 3): five edits in three files; the third edit of A.pas is
    /// unchecked with Space; Apply (Enter): exactly the other four edits are in the buffers and the closed file, the
    /// unchecked place is unchanged.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-363")]
    [Trait("Req", "UI-DLG-365")]
    [Trait("Req", "IDE-REFAC-007")]
    public async Task UncheckedEditIsExcluded()
    {
        await using var h = await StartAsync();
        await TrustDialogTests.Guarded(h, async () =>
        {
            var (edit, a, c) = await FiveEditsAsync(h);
            var (running, dlg, model) = await ShowAsync(h, edit);
            Assert.Equal("Selected changes: 5 of 5, files: 3", dlg.Find<TextBlock>("Summary").Text);

            // The tree has the focus on the first file row: three rows down is the third edit of A.pas.
            dlg.Press("Down");
            dlg.Press("Down");
            dlg.Press("Down");
            Assert.Equal(Prefix + "File.A.pas.Edit.3", dlg.FocusedAutomationId);
            dlg.Press("Space");
            Assert.False(Check(dlg, "File.A.pas.Edit.3").IsChecked);
            Assert.Null(Check(dlg, "File.A.pas").IsChecked);
            Assert.Equal("Selected changes: 4 of 5, files: 3", dlg.Find<TextBlock>("Summary").Text);
            await DiffAsync(h, model);

            dlg.Press("Enter");
            var result = await EndAsync(h, running);
            Assert.False(dlg.Window.IsVisible);
            Assert.Equal((RefactoringOutcome.Applied, 4, 3), (result.Outcome, result.EditCount, result.FileCount));

            // A.pas: the lines 5 and 9 are renamed, line 15 is as it was; B.pas (closed) and C.pas (open): the first Old.
            Assert.Equal(UnitA.Replace("procedure Old;", "procedure Renamed;", StringComparison.Ordinal), a.Buffer.Text);
            Assert.Contains("\n  Old;\n", a.Buffer.Text, StringComparison.Ordinal);
            var once = Unit("X").Replace("interface\n\nprocedure Old;", "interface\n\nprocedure Renamed;", StringComparison.Ordinal);
            Assert.Equal(once.Replace("unit X;", "unit B;", StringComparison.Ordinal), File.ReadAllText(PathOf(h, "B.pas")));
            Assert.Equal(once.Replace("unit X;", "unit C;", StringComparison.Ordinal), c.Buffer.Text);
            Assert.Equal(Unit("C"), File.ReadAllText(PathOf(h, "C.pas"))); // an open document is edited, not saved
            Assert.Equal("Rename Symbol: 4 changes in 3 files", h.Services.Status.Message);
            Assert.Equal(Title, h.Services.Refactoring.UndoTitle);
        });
    }

    /// <summary>
    /// IDE-REFAC-007 with the real dialog: <see cref="PreviewMode.Auto"/> applies a change confined to the buffer of the
    /// active document without a dialog and shows the dialog for a change in another file; <see cref="PreviewMode.Never"/>
    /// shows none.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-REFAC-007")]
    public async Task AutoPreviewsUnlessConfinedToTheActiveBuffer()
    {
        await using var h = await StartAsync();
        await TrustDialogTests.Guarded(h, async () =>
        {
            var a = await OpenAsync(h, "A.pas");
            var engine = h.Services.Refactoring;

            // The active buffer alone: applied at once.
            var own = await EndAsync(h, engine.ApplyAsync(new WorkspaceEdit(Title, [Rename(PathOf(h, "A.pas"), a.Buffer.Text, 1)])));
            Assert.Equal((RefactoringOutcome.Applied, 1, 1), (own.Outcome, own.EditCount, own.FileCount));
            Assert.Empty(h.Window.OwnedWindows);
            Assert.Contains("procedure Renamed;", a.Buffer.Text, StringComparison.Ordinal);

            // A closed file too: the dialog; cancelled, nothing changes.
            var text = a.Buffer.Text;
            var running = engine.ApplyAsync(new WorkspaceEdit(Title, [Rename(PathOf(h, "A.pas"), text, 1), Rename(PathOf(h, "B.pas"), Unit("B"), 1)]));
            var dlg = await h.WaitForDialogAsync(RefactoringPreviewViewModel.DialogName);
            await DiffAsync(h, Assert.IsType<RefactoringPreviewViewModel>(dlg.Window.Model));
            Assert.Equal("Selected changes: 2 of 2, files: 2", dlg.Find<TextBlock>("Summary").Text);
            dlg.Press("Escape");
            Assert.Equal(RefactoringOutcome.Canceled, (await EndAsync(h, running)).Outcome);
            Assert.Equal((text, Unit("B")), (a.Buffer.Text, File.ReadAllText(PathOf(h, "B.pas"))));

            // Never: the same change without a dialog.
            var direct = await EndAsync(h, engine.ApplyAsync(new WorkspaceEdit(Title, [Rename(PathOf(h, "A.pas"), text, 1), Rename(PathOf(h, "B.pas"), Unit("B"), 1)]), PreviewMode.Never));
            Assert.Equal((RefactoringOutcome.Applied, 2, 2), (direct.Outcome, direct.EditCount, direct.FileCount));
            Assert.Empty(h.Window.OwnedWindows);
        });
    }

    /// <summary>
    /// UI-DLG-360…362, 366: the title, the file order (the active document first, then by path), counts, directory and
    /// tooltip, line numbers, the removed and inserted spans of a row, <c>⏎</c> and the 120-character cut of a multi-line
    /// insertion, the 240-character window of a long line, the first line of an edit over several lines, row ids with
    /// <c>@2</c> for an equal file name, the summary, the first focus; more than 20 files open collapsed.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-360")]
    [Trait("Req", "UI-DLG-361")]
    [Trait("Req", "UI-DLG-362")]
    [Trait("Req", "UI-DLG-366")]
    public async Task TreeListsFilesAndEditsWithCounts()
    {
        var longLine = "  S = '" + new string('a', 300) + "Old" + new string('b', 300) + "';";
        var unitC = "unit C;\n\nconst\n" + longLine + "\n\nend.\n";
        await using var h = await StartAsync(o => o.InitialFiles["C.pas"] = unitC);
        await TrustDialogTests.Guarded(h, async () =>
        {
            var b = await OpenAsync(h, "B.pas"); // the active document
            var sub = Unit("A");
            var body = sub.IndexOf("procedure Old;\nbegin", StringComparison.Ordinal);
            var begin = UnitA.IndexOf("begin", StringComparison.Ordinal);
            var edit = new WorkspaceEdit(Title,
            [
                // Sub\A.pas: the three lines of the procedure body are removed.
                new FileEdit(PathOf(h, @"Sub\A.pas"), sub, [new TextEdit(body, "procedure Old;\nbegin\nend;\n".Length, "")]),
                Rename(PathOf(h, "C.pas"), unitC),
                Rename(PathOf(h, "B.pas"), b.Buffer.Text, 1),
                // A.pas: two insertions over several lines before "begin" (line 10) come second and third by offset.
                new FileEdit(PathOf(h, "A.pas"), UnitA,
                [
                    new TextEdit(begin + 5, 0, "\r\n  // " + new string('x', 130) + "\n  Run;"),
                    new TextEdit(begin, 0, "var\r\n  I: Integer;\n"),
                    new TextEdit(UnitA.IndexOf("Old", StringComparison.Ordinal), 3, "Renamed"),
                ]),
            ]);
            var (running, dlg, model) = await ShowAsync(h, edit);

            Assert.Equal("Preview — Rename Symbol", dlg.Window.Title);
            Assert.Equal((960, 600), (dlg.Window.Width, dlg.Window.Height));
            Assert.Equal(
                [
                    Prefix + "File.B.pas", Prefix + "File.B.pas.Edit.1",
                    Prefix + "File.A.pas", Prefix + "File.A.pas.Edit.1", Prefix + "File.A.pas.Edit.2", Prefix + "File.A.pas.Edit.3",
                    Prefix + "File.C.pas", Prefix + "File.C.pas.Edit.1",
                    Prefix + "File.A.pas@2", Prefix + "File.A.pas@2.Edit.1",
                ],
                RowIds(dlg));

            // First focus: the tree, on its first file row, which is selected.
            var list = dlg.Find<ListBox>("Tree");
            Assert.Equal(Prefix + "File.B.pas", dlg.FocusedAutomationId);
            Assert.Same(model.Files[0], list.SelectedItem);
            Assert.Equal("Selected changes: 6 of 6, files: 4", dlg.Find<TextBlock>("Summary").Text);
            Assert.True(Check(dlg, "File.A.pas").IsChecked);
            Assert.All(dlg.FindAll(Prefix + "File.").OfType<CheckBox>(), box => Assert.True(box.IsChecked));

            // A file row: icon, name with the number of edits, the directory (tooltip: the full path).
            var file = dlg.Find<ListBoxItem>(Prefix + "File.A.pas@2");
            var texts = file.GetVisualDescendants().OfType<TextBlock>().ToList();
            Assert.Contains(texts, t => string.Concat(t.Inlines!.OfType<Run>().Select(r => r.Text)) == "A.pas  (1)");
            Assert.Contains(texts, t => t.Text == Path.Combine(h.TempDir, "Sub") && t.TextTrimming == Avalonia.Media.TextTrimming.LeadingCharacterEllipsis);
            Assert.Equal(PathOf(h, @"Sub\A.pas"), ToolTip.GetTip(file));
            Assert.Equal(["unit", "unit", "unit", "unit"], model.Files.Select(f => f.Icon));

            // Edit rows: the line, then the line's text with the replaced range and the inserted text.
            Assert.Equal("5", dlg.Find<TextBlock>(Prefix + "File.B.pas.Edit.1.Line").Text);
            Assert.Equal([("procedure ", ""), ("Old", "removed"), ("Renamed", "added"), (";", "")], Parts(dlg, "File.B.pas.Edit.1"));
            Assert.Equal(["5", "10", "10"], new[] { 1, 2, 3 }.Select(n => dlg.Find<TextBlock>($"{Prefix}File.A.pas.Edit.{n}.Line").Text));

            // A multi-line insertion: every line break is ⏎; it is cut after 120 characters.
            Assert.Equal([("var⏎  I: Integer;⏎", "added"), ("begin", "")], Parts(dlg, "File.A.pas.Edit.2"));
            var cut = Parts(dlg, "File.A.pas.Edit.3");
            Assert.Equal(("begin", ""), cut[0]);
            Assert.Equal(("⏎  // " + new string('x', 114) + "…", "added"), cut[1]);
            Assert.Equal(2, cut.Length);

            // A long line: a window of 240 characters around the replaced range, … at both cut ends.
            var window = Parts(dlg, "File.C.pas.Edit.1");
            Assert.Equal("4", dlg.Find<TextBlock>(Prefix + "File.C.pas.Edit.1.Line").Text);
            Assert.Equal(["", "removed", "added", ""], window.Select(p => p.Token));
            Assert.Equal(("…" + new string('a', 118), "Old", "Renamed", new string('b', 119) + "…"), (window[0].Text, window[1].Text, window[2].Text, window[3].Text));

            // An edit that replaces more than one line shows its first line.
            Assert.Equal("9", dlg.Find<TextBlock>(Prefix + "File.A.pas@2.Edit.1.Line").Text);
            Assert.Equal([("procedure Old;", "removed")], Parts(dlg, "File.A.pas@2.Edit.1"));

            // Rows are 22 high; a check box is the 14 x 14 box of the design tokens.
            Assert.All(list.GetVisualDescendants().OfType<ListBoxItem>().Where(i => i.IsVisible), i => Assert.Equal(22, i.Bounds.Height));
            var box = Check(dlg, "File.B.pas").GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NormalRectangle");
            Assert.Equal(new Size(14, 14), box.Bounds.Size);

            // Every interactive control has an id, no id is used twice (the embedded diff view keeps its Diff.* ids).
            Assert.Empty(AutomationIdChecks.ControlsWithoutId(dlg.Window));
            Assert.Empty(AutomationIdChecks.DuplicateIds(dlg.Window));
            Assert.DoesNotContain(AutomationIdChecks.AllIds(dlg.Window), id => !AutomationIds.IsWellFormed(id));
            Assert.NotNull(dlg.TryFind<Control>("Diff"));
            Assert.NotNull(dlg.Window.GetVisualDescendants().OfType<TextEditor>().FirstOrDefault(e => AutomationIds.Get(e) == "Diff.Left"));
            foreach (var id in (string[])["SelectAll", "SelectNone", "Ok", "Cancel"])
                Assert.NotNull(dlg.TryFind<Button>(id));

            dlg.Click("Cancel");
            Assert.Equal(RefactoringOutcome.Canceled, (await EndAsync(h, running)).Outcome);

            // More than 20 files: every file row is collapsed.
            FileEdit Numbered(int n) => new(PathOf(h, $"U{n:00}.pas"), "Old\n", [new TextEdit(0, 3, "New")]);
            var many = new RefactoringPreviewViewModel(new WorkspaceEdit(Title, [.. Enumerable.Range(1, 21).Select(Numbered)]), null,
                h.Services.TextBuffers, h.Services.Languages);
            Assert.Equal(21, many.Rows.Count);
            Assert.All(many.Rows, r => Assert.IsType<RefactoringPreviewFile>(r));
            var twenty = new RefactoringPreviewViewModel(new WorkspaceEdit(Title, [.. Enumerable.Range(1, 20).Select(Numbered)]), null,
                h.Services.TextBuffers, h.Services.Languages);
            Assert.Equal(40, twenty.Rows.Count);
            await many.DiffUpdate;
            await twenty.DiffUpdate;
        });
    }

    /// <summary>
    /// UI-DLG-363: a file row checks and unchecks its edits and is indeterminate when they differ; a click on a box
    /// toggles it; Space sets every selected row to the state of the focused row; <c>Select None</c> disables
    /// <c>Apply</c>, <c>Select All</c> enables it; Left and Right collapse and expand a file row.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-363")]
    public async Task FileCheckBoxIsTriStateAndSelectButtonsWork()
    {
        await using var h = await StartAsync();
        await TrustDialogTests.Guarded(h, async () =>
        {
            var (edit, _, _) = await FiveEditsAsync(h);
            var (running, dlg, _) = await ShowAsync(h, edit);
            var summary = dlg.Find<TextBlock>("Summary");
            var apply = dlg.Find<Button>("Ok");
            bool?[] States() => [.. new[] { "File.A.pas", "File.A.pas.Edit.1", "File.A.pas.Edit.2", "File.A.pas.Edit.3" }.Select(r => Check(dlg, r).IsChecked)];

            // Space on the file row (the focused one): all its edits are unchecked; again: checked.
            dlg.Press("Space");
            Assert.Equal([false, false, false, false], States());
            Assert.Equal("Selected changes: 2 of 5, files: 2", summary.Text);
            dlg.Press("Space");
            Assert.Equal([true, true, true, true], States());

            // A click on the box of an edit: the file row is indeterminate; a click on the file's box then checks all.
            dlg.Click(Prefix + "File.A.pas.Edit.2.Check");
            Assert.Equal([null, true, false, true], States());
            Assert.Equal("Selected changes: 4 of 5, files: 3", summary.Text);
            dlg.Click(Prefix + "File.A.pas.Check");
            Assert.Equal([true, true, true, true], States());
            dlg.Click(Prefix + "File.A.pas.Check");
            Assert.Equal([false, false, false, false], States());

            // Multi-selection: the first two edits are selected; Space on the focused one sets both.
            dlg.Press("Down");
            dlg.Press("Shift+Down");
            Assert.Equal(Prefix + "File.A.pas.Edit.2", dlg.FocusedAutomationId);
            dlg.Press("Space");
            Assert.Equal([null, true, true, false], States());
            dlg.Press("Space");
            Assert.Equal([false, false, false, false], States());

            // Select None: nothing is checked and Apply is disabled (Enter does nothing); Select All: everything.
            dlg.Click("SelectNone");
            Assert.All(dlg.FindAll(Prefix + "File.").OfType<CheckBox>(), box => Assert.False(box.IsChecked));
            Assert.Equal("Selected changes: 0 of 5, files: 0", summary.Text);
            Assert.False(apply.IsEnabled);
            dlg.Press("Enter");
            Assert.True(dlg.Window.IsVisible);
            Assert.False(running.IsCompleted);
            dlg.Click("SelectAll");
            Assert.All(dlg.FindAll(Prefix + "File.").OfType<CheckBox>(), box => Assert.True(box.IsChecked));
            Assert.Equal("Selected changes: 5 of 5, files: 3", summary.Text);
            Assert.True(apply.IsEnabled);

            // Left collapses the file row, Right expands it again.
            dlg.Click(Prefix + "File.B.pas");
            dlg.Press("Left");
            Assert.Null(dlg.TryFind<Control>(Prefix + "File.B.pas.Edit.1"));
            Assert.Equal(Prefix + "File.B.pas", dlg.FocusedAutomationId);
            dlg.Press("Right");
            Assert.NotNull(dlg.TryFind<Control>(Prefix + "File.B.pas.Edit.1"));

            dlg.Press("Escape");
            Assert.Equal(RefactoringOutcome.Canceled, (await EndAsync(h, running)).Outcome);
        });
    }

    /// <summary>
    /// UI-DLG-364: the diff shows the selected row's file — base text against the base text with the checked edits,
    /// read only, without <c>Refresh</c> and <c>Copy Block to Current</c>; unchecking an edit lowers the change count and
    /// the right side has the base text there; an edit row selects its change block; a file with nothing checked shows
    /// the identical banner.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-364")]
    public async Task DiffShowsTheSelectedFileWithCheckedEditsOnly()
    {
        await using var h = await StartAsync();
        await TrustDialogTests.Guarded(h, async () =>
        {
            var (edit, a, _) = await FiveEditsAsync(h);
            var (running, dlg, model) = await ShowAsync(h, edit);
            // The embedded view keeps its own ids (Diff.*), which are not relative to the dialog.
            Control Part(string id) => dlg.Window.GetVisualDescendants().OfType<Control>().Single(c => AutomationIds.Get(c) == id);
            string Text(string id) => ((TextBlock)Part(id)).Text!;
            void ClickPart(string id)
            {
                var part = Part(id);
                var point = part.TranslatePoint(new Point(part.Bounds.Width / 2, part.Bounds.Height / 2), dlg.Window)!.Value;
                dlg.Window.MouseDown(point, MouseButton.Left);
                dlg.Window.MouseUp(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            }

            string Right() => string.Join('\n', model.Diff!.Layout.Right.Where(r => r.RightLine is not null).Select(r => r.Text));

            // The first file: labels, the change count, the first block selected, both sides read only.
            Assert.Same(model.Files[0], model.DiffFile);
            Assert.Equal(("A.pas", "A.pas (after)", "3 changes"), (Text("Diff.LeftLabel"), Text("Diff.RightLabel"), Text("Diff.Changes")));
            Assert.Equal(PathOf(h, "A.pas"), ToolTip.GetTip(Part("Diff.LeftLabel")));
            Assert.Equal(0, model.Diff!.CurrentBlock);
            Assert.Equal(a.Buffer.Text.TrimEnd('\n'), string.Join('\n', model.Diff.Layout.Left.Where(r => r.LeftLine is not null).Select(r => r.Text)));
            Assert.Equal(UnitA.Replace("Old", "Renamed", StringComparison.Ordinal).TrimEnd('\n'), Right());
            Assert.All(new[] { "Diff.Left", "Diff.Right" }, id => Assert.True(((TextEditor)Part(id)).IsReadOnly));
            Assert.Equal("pascal", model.Diff.Left.Language.Id);
            Assert.False(Part("Diff.Refresh").IsEffectivelyVisible);
            Assert.False(Part("Diff.CopyBlockToRight").IsEffectivelyVisible);
            Assert.True(Part("Diff.NextDifference").IsEffectivelyVisible);
            Assert.False(Part("Diff.Banner").IsEffectivelyVisible);

            // The header works by click: Next Difference selects the second block; the tree keeps the selection.
            ClickPart("Diff.NextDifference");
            Assert.Equal(1, model.Diff.CurrentBlock);

            // The mode toggles too; the keyboard focus is on the toggle, not in a code view.
            ClickPart("Diff.Mode.Inline");
            Assert.Equal(DiffViewMode.Inline, model.Diff.Mode);
            Assert.True(Part("Diff.Inline").IsEffectivelyVisible);
            Assert.False(Part("Diff.Left").IsEffectivelyVisible);
            Assert.Equal("Diff.Mode.Inline", dlg.FocusedAutomationId);
            ClickPart("Diff.Mode.SideBySide");
            Assert.Equal(DiffViewMode.SideBySide, model.Diff.Mode);
            Assert.True(Part("Diff.Left").IsEffectivelyVisible);

            // … and by the keyboard: Tab moves through the header, Space presses the focused toggle.
            dlg.Press("Tab");
            Assert.Equal("Diff.Mode.Inline", dlg.FocusedAutomationId);
            dlg.Press("Space");
            Assert.Equal(DiffViewMode.Inline, model.Diff.Mode);
            dlg.Press("Shift+Tab");
            Assert.Equal("Diff.Mode.SideBySide", dlg.FocusedAutomationId);
            dlg.Press("Space");
            Assert.Equal(DiffViewMode.SideBySide, model.Diff.Mode);
            dlg.Press("Tab");
            dlg.Press("Tab");
            dlg.Press("Tab");
            Assert.Equal("Diff.PreviousDifference", dlg.FocusedAutomationId);

            // From the tree, Tab reaches the splitter and then the header of the diff.
            dlg.Click(Prefix + "File.A.pas");
            dlg.Press("Tab");
            Assert.Equal(Prefix + "Splitter", dlg.FocusedAutomationId);
            dlg.Press("Tab");
            Assert.Equal("Diff.Mode.SideBySide", dlg.FocusedAutomationId);

            // An edit row: the block that shows its line.
            dlg.Click(Prefix + "File.A.pas.Edit.3");
            await DiffAsync(h, model);
            Assert.Equal(2, model.Diff.CurrentBlock);
            var shown = model.Diff;

            // The file row of the shown file: the same diff, its first block again; then the edit row once more.
            dlg.Click(Prefix + "File.A.pas");
            await DiffAsync(h, model);
            Assert.Same(shown, model.Diff);
            Assert.Equal(0, model.Diff.CurrentBlock);
            dlg.Click(Prefix + "File.A.pas.Edit.3");
            await DiffAsync(h, model);
            Assert.Equal(2, model.Diff.CurrentBlock);

            // Unchecking the second edit: two changes, the right side has the base text on line 9; the same diff is
            // kept (its scroll position stays), the selected row's block is now the second one.
            dlg.Click(Prefix + "File.A.pas.Edit.2.Check");
            await DiffAsync(h, model);
            Assert.Same(shown, model.Diff);
            Assert.Equal("2 changes", Text("Diff.Changes"));
            var lines = Right().Split('\n');
            Assert.Equal(("procedure Renamed;", "procedure Old;", "  Renamed;"), (lines[4], lines[8], lines[14]));

            // The unchecked edit's row: no block shows its line; the row itself is revealed.
            dlg.Click(Prefix + "File.A.pas.Edit.2");
            await DiffAsync(h, model);
            Assert.Equal(-1, model.Diff.CurrentBlock);
            Assert.Equal(8, model.Diff.Layout.Left[model.Diff.CaretRow].LeftLine);

            // Another file: its own diff, the first block selected; then the file row of A again.
            dlg.Click(Prefix + "File.B.pas");
            await DiffAsync(h, model);
            Assert.NotSame(shown, model.Diff);
            Assert.Equal(("B.pas", "B.pas (after)", "1 change"), (Text("Diff.LeftLabel"), Text("Diff.RightLabel"), Text("Diff.Changes")));
            Assert.Equal(0, model.Diff.CurrentBlock);

            // Nothing checked in the shown file: the identical banner.
            dlg.Click(Prefix + "File.B.pas.Check");
            await DiffAsync(h, model);
            Assert.Equal("0 changes", Text("Diff.Changes"));
            Assert.True(Part("Diff.Banner").IsEffectivelyVisible);
            Assert.Equal("The files are identical.", Text("Diff.Banner"));

            // The keyboard focus stayed in the tree while the diff followed.
            Assert.Equal(Prefix + "File.B.pas", dlg.FocusedAutomationId);

            // A code view of the dialog: Ctrl+C copies the selection as a diff tab does (the text of that side).
            dlg.Click(Prefix + "File.A.pas");
            await DiffAsync(h, model);
            var left = (TextEditor)Part("Diff.Left");
            Assert.True(left.TextArea.Focus());
            Dispatcher.UIThread.RunJobs();
            dlg.Press("Ctrl+A");
            dlg.Press("Ctrl+C");
            Assert.Equal(model.Diff.Copy(DiffPane.Left, 0, left.Document.TextLength), await h.Services.Clipboard.GetTextAsync());
            Assert.StartsWith("unit A;", await h.Services.Clipboard.GetTextAsync(), StringComparison.Ordinal);
            Assert.Contains("procedure Old;", await h.Services.Clipboard.GetTextAsync(), StringComparison.Ordinal);

            dlg.Click("Cancel");
            Assert.Equal(RefactoringOutcome.Canceled, (await EndAsync(h, running)).Outcome);
        });
    }

    /// <summary>
    /// UI-DLG-365: <c>Cancel</c>, Escape and the title-bar close end the operation as <c>Canceled</c>: no buffer and no
    /// file changes, no status message, nothing to undo.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-365")]
    public async Task CancelAndEscapeChangeNothing()
    {
        await using var h = await StartAsync();
        await TrustDialogTests.Guarded(h, async () =>
        {
            var (edit, a, c) = await FiveEditsAsync(h);
            string Snapshot() => string.Join('|', a.Buffer.Text, a.Buffer.Version, a.IsModified, c.Buffer.Text, c.Buffer.Version, c.IsModified,
                File.ReadAllText(PathOf(h, "A.pas")), File.ReadAllText(PathOf(h, "B.pas")), File.ReadAllText(PathOf(h, "C.pas")),
                string.Join(',', Directory.GetFiles(h.TempDir).Select(Path.GetFileName).Order(StringComparer.Ordinal)));
            var before = Snapshot();

            // The only status text a cancelled operation may show is its own "working" text while it checks the files
            // (IDE-REFAC-012: after 400 ms, gone again with the dialog); no result is announced.
            h.Services.Status.ShowMessage("before", TimeSpan.FromMinutes(1));
            var messages = new List<string?>();
            h.Services.Status.Changed += (_, _) => messages.Add(h.Services.Status.Message);

            foreach (var close in (Action<DialogDriver>[])[d => d.Click("Cancel"), d => d.Press("Escape"), d => d.Window.Close()])
            {
                var (running, dlg, _) = await ShowAsync(h, edit);
                dlg.Press("Down");
                dlg.Press("Space"); // a changed check box changes nothing either
                close(dlg);
                var result = await EndAsync(h, running);
                Assert.False(dlg.Window.IsVisible);
                Assert.Equal((RefactoringOutcome.Canceled, 0, 0, null), (result.Outcome, result.EditCount, result.FileCount, result.Error));
                Assert.Equal(before, Snapshot());
                Assert.True(h.Services.Status.Message is null or "before", h.Services.Status.Message);
                Assert.All(messages, m => Assert.True(m is null or "before" or "Rename Symbol…", m));
                Assert.False(h.Services.Refactoring.CanUndo);
                Assert.False(h.Services.Refactoring.IsBusy);
            }
        });
    }

    /// <summary>
    /// UI-DLG-360: 960 × 600 with the tree 320 wide by default, minimum 720 × 420, tree at least 200 and diff at least
    /// 300; the size and the tree width of the last use are remembered (<c>dialogs.RefactoringPreview.size</c>, <c>.split</c>).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-360")]
    public async Task SizeAndSplitAreRemembered()
    {
        await using var h = await StartAsync();
        await TrustDialogTests.Guarded(h, async () =>
        {
            var (edit, _, _) = await FiveEditsAsync(h);
            var layout = h.Services.Layout;
            Assert.Null(layout.GetDialogValue("RefactoringPreview", "size"));
            Assert.Null(layout.GetDialogValue("RefactoringPreview", "split"));

            var (running, dlg, _) = await ShowAsync(h, edit);
            var tree = dlg.Find<ListBox>("Tree");
            var diff = dlg.Find<Control>("Diff");
            var splitter = dlg.Find<GridSplitter>("Splitter");
            Assert.True(dlg.Window.CanResize);
            Assert.Equal((960, 600, 720, 420), (dlg.Window.Width, dlg.Window.Height, dlg.Window.MinWidth, dlg.Window.MinHeight));
            Assert.Equal(320, Width(tree));
            Assert.Equal(5, splitter.Bounds.Width);
            Assert.Equal(960 - 2 * 16 - 320 - 5, diff.Bounds.Width); // the diff takes the rest (outer padding space.6)

            // The splitter is dragged 80 to the right, the window is made larger.
            Drag(dlg, splitter, 80);
            Assert.Equal(400, Width(tree));
            dlg.Window.Width = 1100;
            dlg.Window.Height = 700;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(400, Width(tree)); // the diff takes all extra width
            Assert.Equal(1100 - 2 * 16 - 400 - 5, diff.Bounds.Width);
            dlg.Click("Cancel");
            await EndAsync(h, running);
            Assert.Equal("1100,700", layout.GetDialogValue("RefactoringPreview", "size"));
            Assert.Equal("400", layout.GetDialogValue("RefactoringPreview", "split"));

            // The next use opens with them. The tree cannot be made narrower than 200, the diff not narrower than 300.
            (running, dlg, _) = await ShowAsync(h, edit);
            tree = dlg.Find<ListBox>("Tree");
            diff = dlg.Find<Control>("Diff");
            splitter = dlg.Find<GridSplitter>("Splitter");
            Assert.Equal((1100, 700), (dlg.Window.Width, dlg.Window.Height));
            Assert.Equal(400, Width(tree));
            Drag(dlg, splitter, -600);
            Assert.Equal(200, Width(tree));
            Drag(dlg, splitter, 2000);
            Assert.Equal(300, diff.Bounds.Width);
            Assert.Equal(1100 - 2 * 16 - 300 - 5, Width(tree));

            // At the minimum size a remembered tree width that no longer fits gives way to the diff's minimum.
            dlg.Window.Width = 720;
            dlg.Window.Height = 420;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(300, diff.Bounds.Width);
            Assert.Equal(720 - 2 * 16 - 300 - 5, Width(tree));
            dlg.Press("Escape");
            await EndAsync(h, running);
        });

        static double Width(ListBox tree) => ((Control)tree.GetVisualParent()!).Bounds.Width; // the tree's frame

        static void Drag(DialogDriver dlg, GridSplitter splitter, double dx)
        {
            var start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), dlg.Window)!.Value;
            dlg.Window.MouseDown(start, MouseButton.Left);
            dlg.Window.MouseMove(start + new Point(dx / 2, 0));
            dlg.Window.MouseMove(start + new Point(dx, 0));
            dlg.Window.MouseUp(start + new Point(dx, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// UI-DLG-367: the tree is named by its label; a file row reads its name, the number of changes and its check
    /// state; an edit row its line, what is replaced, inserted or removed, and its check state; the summary is a live
    /// region; every check box and button has a name.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-367")]
    public async Task ScreenReaderNames()
    {
        await using var h = await StartAsync();
        await TrustDialogTests.Guarded(h, async () =>
        {
            var c = await OpenAsync(h, "C.pas");
            var a = await OpenAsync(h, "A.pas");
            var begin = UnitA.IndexOf("begin", StringComparison.Ordinal);
            var end = UnitA.IndexOf("end;", StringComparison.Ordinal);
            var edit = new WorkspaceEdit(Title,
            [
                new FileEdit(PathOf(h, "A.pas"), a.Buffer.Text,
                [
                    new TextEdit(UnitA.IndexOf("Old", StringComparison.Ordinal), 3, "Renamed"), // replaced
                    new TextEdit(begin, 0, "var I: Integer;\n"),                                 // inserted
                    new TextEdit(end, "end;".Length, ""),                                        // removed
                ]),
                Rename(PathOf(h, "C.pas"), c.Buffer.Text, 1),
            ]);
            var (running, dlg, _) = await ShowAsync(h, edit);
            string Name(string row) => AutomationProperties.GetName(dlg.Find<ListBoxItem>(Prefix + row))!;

            var list = dlg.Find<ListBox>("Tree");
            Assert.Equal("Changes", AutomationProperties.GetName(list));
            var label = dlg.Window.GetVisualDescendants().OfType<Label>().Single(l => ReferenceEquals(l.Target, list));
            Assert.Equal("_Changes:", label.Content);

            Assert.Equal("A.pas, 3 changes, checked", Name("File.A.pas"));
            Assert.Equal("C.pas, 1 change, checked", Name("File.C.pas"));
            Assert.Equal("line 5, Old replaced by Renamed, checked", Name("File.A.pas.Edit.1"));
            Assert.Equal("line 10, inserted var I: Integer;⏎, checked", Name("File.A.pas.Edit.2"));
            Assert.Equal("line 11, end; removed, checked", Name("File.A.pas.Edit.3"));

            // The names follow the check boxes; a box is named as its row.
            dlg.Click(Prefix + "File.A.pas.Edit.2.Check");
            Assert.Equal("line 10, inserted var I: Integer;⏎, not checked", Name("File.A.pas.Edit.2"));
            Assert.Equal("A.pas, 3 changes, partly checked", Name("File.A.pas"));
            Assert.Equal(Name("File.A.pas"), AutomationProperties.GetName(Check(dlg, "File.A.pas")));
            Assert.Equal(Name("File.A.pas.Edit.2"), AutomationProperties.GetName(Check(dlg, "File.A.pas.Edit.2")));
            dlg.Click(Prefix + "File.C.pas.Check");
            Assert.Equal("C.pas, 1 change, not checked", Name("File.C.pas"));

            // The summary is announced when it changes.
            var summary = dlg.Find<TextBlock>("Summary");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(summary));
            Assert.Equal("Selected changes: 2 of 4, files: 1", summary.Text);
            Assert.Equal(summary.Text, AutomationProperties.GetName(summary));

            // Buttons: the label without its mnemonic mark; the mnemonics of the dialog are distinct.
            Assert.Equal(
                [("SelectAll", "Select All", "Select A_ll"), ("SelectNone", "Select None", "Select _None"), ("Ok", "Apply", "_Apply"), ("Cancel", "Cancel", "Cancel")],
                new[] { "SelectAll", "SelectNone", "Ok", "Cancel" }.Select(id => dlg.Find<Button>(id)).Select(b => (AutomationIds.Get(b)![Prefix.Length..], AutomationProperties.GetName(b)!, (string)b.Content!)));
            Assert.All(dlg.Window.GetVisualDescendants().OfType<Button>().Where(b => b.TemplatedParent is null),
                b => Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(b)), AutomationIds.Get(b)));

            dlg.Click("Cancel");
            Assert.Equal(RefactoringOutcome.Canceled, (await EndAsync(h, running)).Outcome);
        });
    }
}
