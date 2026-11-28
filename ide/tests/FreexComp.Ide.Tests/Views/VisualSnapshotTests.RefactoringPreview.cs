using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subject of the Preview dialog of a refactoring (I-350, F13-003).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>
    /// The dialog <c>RefactoringPreview</c>, light and dark, at its minimum size (720 × 420), its own size (960 × 600)
    /// and enlarged (1400 × 900): <c>refactoring-preview</c>. Seventeen files (units, a form, a text file) with more rows
    /// than the tree shows at once, one edit unchecked (its file row is indeterminate), the diff of the first file.
    /// Rules: the tree, the summary, the splitter, the diff with every control of its header and both code views, and
    /// the four buttons are shown and drawn completely inside the dialog; every row, scrolled into view, lies inside the
    /// tree's view port; nothing overlaps or is cut.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-360")]
    [Trait("Req", "UI-DLG-361")]
    [Trait("Req", "UI-DLG-362")]
    [Trait("Req", "UI-DLG-364")]
    public async Task RefactoringPreview_RendersAtMinimumAndLargerSizes()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        const string Subject = "refactoring-preview";
        const string Prefix = "Dialog.RefactoringPreview.";
        Size[] sizes = [new(720, 420), new(960, 600), new(1400, 900)];
        if (Directory.Exists(VisualSnapshot.FolderOf(Subject)))
            Directory.Delete(VisualSnapshot.FolderOf(Subject), recursive: true);

        const string FormText = "object MainForm: TMainForm\n  Caption = 'Old'\n  object OldButton: TButton\n    OnClick = OldClick\n  end\nend\n";
        const string Notes = "Notes\n\nThe procedure Old is called from the units listed here.\n";
        string[] units = [.. Enumerable.Range(1, 12).Select(n => $"Unit{n:00}")];
        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await RefactoringPreviewTests.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                o.InitialFiles["MainForm.form"] = FormText;
                o.InitialFiles["Notes.txt"] = Notes;
                foreach (var unit in units)
                    o.InitialFiles[unit + ".pas"] = RefactoringPreviewTests.Unit(unit);
            });
            await TrustDialogTests.Guarded(h, async () =>
            {
                var a = await RefactoringPreviewTests.OpenAsync(h, "A.pas"); // the active document: its row comes first
                FileEdit Rename(string name, string text) => RefactoringPreviewTests.Rename(RefactoringPreviewTests.PathOf(h, name), text);
                var edit = new WorkspaceEdit("Rename Symbol",
                [
                    Rename("A.pas", a.Buffer.Text), Rename("B.pas", RefactoringPreviewTests.Unit("B")), Rename("C.pas", RefactoringPreviewTests.Unit("C")),
                    Rename("MainForm.form", FormText), Rename("Notes.txt", Notes),
                    .. units.Select(u => Rename(u + ".pas", RefactoringPreviewTests.Unit(u))),
                ]);
                var (running, dlg, model) = await RefactoringPreviewTests.ShowAsync(h, edit);
                try
                {
                    Assert.Equal(["unit", "unit", "unit", "form", "file"], model.Files.Take(5).Select(f => f.Icon));
                    dlg.Click(Prefix + "File.A.pas.Edit.3.Check");
                    await RefactoringPreviewTests.DiffAsync(h, model);
                    Assert.Null(dlg.Find<CheckBox>(Prefix + "File.A.pas.Check").IsChecked);
                    Assert.Equal("Selected changes: 34 of 35, files: 17", dlg.Find<TextBlock>("Summary").Text);

                    var list = dlg.Find<ListBox>("Tree");
                    Control Part(string id) => dlg.Window.GetVisualDescendants().OfType<Control>().Single(c => AutomationIds.Get(c) == id);
                    string[] ids =
                    [
                        Prefix + "Tree", Prefix + "Summary", Prefix + "Splitter", Prefix + "Diff", Prefix + "SelectAll", Prefix + "SelectNone", Prefix + "Ok",
                        Prefix + "Cancel", "Diff.Header", "Diff.LeftLabel", "Diff.RightLabel", "Diff.Changes", "Diff.Mode.SideBySide", "Diff.Mode.Inline",
                        "Diff.IgnoreWhitespace", "Diff.PreviousDifference", "Diff.NextDifference", "Diff.Left", "Diff.Right", "Diff.Overview",
                    ];
                    foreach (var size in sizes)
                    {
                        VisualSnapshot.Arrange(dlg.Window, size, 1.0);
                        await RefactoringPreviewTests.DiffAsync(h, model);
                        var variant = $"{Subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                        Assert.Equal(size, dlg.Window.ClientSize);
                        foreach (var id in ids)
                        {
                            var control = Part(id);
                            Assert.True(control.IsEffectivelyVisible, $"{variant}: {id} is not shown");
                            if (VisualSnapshot.HiddenPart(control, dlg.Window) is { } hidden)
                                defects.Add((variant, $"cut: {id} — {hidden}"));
                        }

                        // The tree has more rows than it shows; every row, scrolled into view, lies inside its view port.
                        var port = list.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
                        Assert.True(port.Extent.Height > port.Viewport.Height, $"{variant}: the tree does not scroll");
                        for (var i = 0; i < model.Rows.Count; i++)
                        {
                            list.ScrollIntoView(i);
                            Dispatcher.UIThread.RunJobs();
                            if (list.ContainerFromIndex(i) is not { } row)
                            {
                                defects.Add((variant, $"not realized when scrolled into view: {model.Rows[i].Key}"));
                                continue;
                            }

                            Assert.Equal(Prefix + model.Rows[i].Key, AutomationIds.Get(row));
                            var rowBox = new Rect(row.TranslatePoint(default, dlg.Window)!.Value, row.Bounds.Size);
                            var portBox = new Rect(port.TranslatePoint(default, dlg.Window)!.Value, port.Bounds.Size).Inflate(0.5);
                            if (!portBox.Contains(rowBox))
                                defects.Add((variant, $"cut: {model.Rows[i].Key} {rowBox} outside the tree's view port {portBox} when scrolled into view"));
                        }

                        list.ScrollIntoView(0);
                        Dispatcher.UIThread.RunJobs();
                        if (VisualSnapshot.DistinctColors(dlg.Window, list) < 3)
                            defects.Add((variant, "the tree is not drawn"));
                        if (VisualSnapshot.DistinctColors(dlg.Window, Part("Diff.Right")) < 3)
                            defects.Add((variant, "the diff is not drawn"));
                        defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
                        files.Add((Subject, theme, size, VisualSnapshot.Capture(dlg.Window, Subject, theme)));
                    }
                }
                finally
                {
                    dlg.Click("Cancel");
                    await h.WaitForAsync(() => running.IsCompleted, "the operation ended");
                }

                Assert.Equal(RefactoringOutcome.Canceled, (await running).Outcome);
            });
        }

        AssertSnapshots(files, [Subject], 6, defects);
    }

    /// <summary>
    /// The dialog <c>RefactoringPreview</c> in its confirm mode (UI-DLG-387, I-378), light and dark, at its minimum size
    /// (720 × 420) and its own size (960 × 600): <c>refactoring-preview-confirm</c>. Five files (units, a form, a text
    /// file), rows without check boxes, the summary of all changes, and a note long enough to wrap at the minimum width.
    /// Rules: the tree, the summary, the splitter, the diff, the note and the two buttons are shown and drawn completely
    /// inside the dialog; the note lies between the panes and the buttons and is not cut; nothing overlaps.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-387")]
    public async Task RefactoringPreviewConfirm()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        const string Subject = "refactoring-preview-confirm";
        const string Prefix = "Dialog.RefactoringPreview.";
        const string Note = "The file MainForm.pas is renamed to CustomerRegistrationForm.pas together with its form. "
            + "Open documents among the changed files are saved.";
        Size[] sizes = [new(720, 420), new(960, 600)];
        if (Directory.Exists(VisualSnapshot.FolderOf(Subject)))
            Directory.Delete(VisualSnapshot.FolderOf(Subject), recursive: true);

        const string FormText = "object MainForm: TMainForm\n  Caption = 'Old'\n  object OldButton: TButton\n    OnClick = OldClick\n  end\nend\n";
        const string Notes = "Notes\n\nThe procedure Old is called from the units listed here.\n";
        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await RefactoringPreviewTests.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                o.InitialFiles["MainForm.form"] = FormText;
                o.InitialFiles["Notes.txt"] = Notes;
            });
            await TrustDialogTests.Guarded(h, async () =>
            {
                var a = await RefactoringPreviewTests.OpenAsync(h, "A.pas"); // the active document: its row comes first
                FileEdit Rename(string name, string text) => RefactoringPreviewTests.Rename(RefactoringPreviewTests.PathOf(h, name), text);
                var edit = new WorkspaceEdit("Rename 'MainForm' to 'CustomerRegistrationForm'",
                [
                    Rename("A.pas", a.Buffer.Text), Rename("B.pas", RefactoringPreviewTests.Unit("B")), Rename("C.pas", RefactoringPreviewTests.Unit("C")),
                    Rename("MainForm.form", FormText), Rename("Notes.txt", Notes),
                ]);
                var (running, dlg, model, answer) = await RefactoringPreviewTests.ConfirmAsync(h, edit, Note);
                try
                {
                    Assert.Equal("Changes: 11, files: 5", dlg.Find<TextBlock>("Summary").Text);
                    var list = dlg.Find<ListBox>("Tree");
                    Assert.Empty(list.GetVisualDescendants().OfType<CheckBox>());
                    Control Part(string id) => dlg.Window.GetVisualDescendants().OfType<Control>().Single(c => AutomationIds.Get(c) == id);
                    string[] ids =
                    [
                        Prefix + "Tree", Prefix + "Summary", Prefix + "Splitter", Prefix + "Diff", Prefix + "Note", Prefix + "Ok", Prefix + "Cancel",
                        "Diff.Header", "Diff.LeftLabel", "Diff.RightLabel", "Diff.Changes", "Diff.Left", "Diff.Right", "Diff.Overview",
                    ];
                    foreach (var size in sizes)
                    {
                        VisualSnapshot.Arrange(dlg.Window, size, 1.0);
                        await RefactoringPreviewTests.DiffAsync(h, model);
                        var variant = $"{Subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                        Assert.Equal(size, dlg.Window.ClientSize);
                        foreach (var id in ids)
                        {
                            var control = Part(id);
                            Assert.True(control.IsEffectivelyVisible, $"{variant}: {id} is not shown");
                            if (VisualSnapshot.HiddenPart(control, dlg.Window) is { } hidden)
                                defects.Add((variant, $"cut: {id} — {hidden}"));
                        }

                        // The note: below both panes, above the buttons; at the minimum width it takes more than one line.
                        Rect Box(Control control) => new(control.TranslatePoint(default, dlg.Window)!.Value, control.Bounds.Size);
                        var (note, tree, diff, ok) = (Box(Part(Prefix + "Note")), Box(list), Box(Part(Prefix + "Diff")), Box(Part(Prefix + "Ok")));
                        if (note.Top < Math.Max(tree.Bottom, diff.Bottom) || note.Bottom > ok.Top)
                            defects.Add((variant, $"the note {note} is not between the panes ({tree}, {diff}) and the buttons ({ok})"));
                        var lineHeight = ((TextBlock)Part(Prefix + "Summary")).Bounds.Height;
                        if (size.Width == 720 && note.Height < 1.5 * lineHeight)
                            defects.Add((variant, $"the note does not wrap: {note.Height} high, a line is {lineHeight}"));
                        if (tree.Height < 5 * 22)
                            defects.Add((variant, $"the tree shows fewer than five rows: {tree.Height} high"));

                        if (VisualSnapshot.DistinctColors(dlg.Window, list) < 3)
                            defects.Add((variant, "the tree is not drawn"));
                        if (VisualSnapshot.DistinctColors(dlg.Window, Part("Diff.Right")) < 3)
                            defects.Add((variant, "the diff is not drawn"));
                        if (VisualSnapshot.DistinctColors(dlg.Window, Part(Prefix + "Note")) < 2)
                            defects.Add((variant, "the note is not drawn"));
                        defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
                        files.Add((Subject, theme, size, VisualSnapshot.Capture(dlg.Window, Subject, theme)));
                    }
                }
                finally
                {
                    dlg.Click("Cancel");
                    await h.WaitForAsync(() => running.IsCompleted, "the operation ended");
                }

                Assert.Equal(RefactoringOutcome.Canceled, (await running).Outcome);
                Assert.False(answer());
            });
        }

        AssertSnapshots(files, [Subject], 4, defects);
    }
}
