using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subjects of the user help (I-257).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>
    /// Help › Contents…, light and dark: <c>help-contents</c> — the dialog at its own size (440 × 360) and enlarged
    /// (800 × 600) with one row per deployed page; the list scrolls, and every row, scrolled into view, is drawn
    /// completely inside the list's view port; <c>help-page</c> — the main window at 800 × 500 and 1920 × 1040 with
    /// the page "Library source" open as a read-only document. Rules: the list, every row and the buttons are drawn
    /// completely inside the dialog; the page's tab and text are drawn completely inside the window, the text is the
    /// one derived from the deployed file (UI-MENU-032), and its heading lines (only they) are heading lines.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-MENU-031")]
    public async Task HelpContentsAndPage_RenderWithoutClipping()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        string[] subjects = ["help-contents", "help-page"];
        Size[] dialogSizes = [new(440, 360), new(800, 600)];
        Size[] windowSizes = [new(800, 500), new(1920, 1040)];
        foreach (var subject in subjects)
        {
            if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
                Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        }

        var deployed = Path.Combine(AppContext.BaseDirectory, "help", "library-source.md");
        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await HeadlessHarness.StartAsync(o => o.Settings["appearance.theme"] = theme);

            // The IDE of a test runs from an empty directory below its user directory: the pages the build deployed next
            // to the real IDE (HelpServiceTests.HelpPagesAreDeployed) are put there.
            var source = Path.Combine(AppContext.BaseDirectory, "help");
            foreach (var file in Directory.EnumerateFiles(source, "*.md", SearchOption.AllDirectories))
            {
                var target = Path.Combine(h.UserDir, "ide", "help", Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }

            var pages = h.Services.Help.Pages;
            Assert.True(pages.Count >= 3, "the deployed help pages are listed");

            // The dialog: one row per page; the wanted page is chosen and opened with the default button.
            var running = h.Services.Commands.ExecuteAsync("help.contents");
            var dlg = await h.WaitForDialogAsync("HelpContents");
            var opened = false;
            try
            {
                // The list virtualizes its rows: the rows are its items, only the ones in view are realized.
                var list = dlg.Find<ListBox>("Pages");
                string[] rowIds = [.. pages.Select(p => "Dialog.HelpContents.Page." + p.Id)];
                Assert.Equal(rowIds, list.Items.Cast<Control>().Select(AutomationIds.Get));
                dlg.Click("Page.library-source");
                Control[] parts =
                [
                    list,
                    dlg.TryFind<Control>("Open") ?? dlg.Find<Control>("Button.Open"),
                    dlg.TryFind<Control>("Cancel") ?? dlg.Find<Control>("Button.Cancel"),
                ];
                foreach (var size in dialogSizes)
                {
                    VisualSnapshot.Arrange(dlg.Window, size, 1.0);
                    var variant = $"{subjects[0]} {VisualSnapshot.FileName(theme, 1.0, size)}";
                    Assert.Equal(size, dlg.Window.ClientSize);
                    foreach (var control in parts)
                    {
                        Assert.True(control.IsEffectivelyVisible, $"{variant}: {AutomationIds.Get(control)} is not shown");
                        if (VisualSnapshot.HiddenPart(control, dlg.Window) is { } hidden)
                            defects.Add((variant, $"cut: {AutomationIds.Get(control)} — {hidden}"));
                    }

                    // Every row is reachable: scrolled into view, it is realized, shown and lies completely inside the
                    // list's view port (not only inside the scrolled extent).
                    var port = list.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
                    for (var i = 0; i < rowIds.Length; i++)
                    {
                        list.ScrollIntoView(i);
                        Dispatcher.UIThread.RunJobs();
                        if (list.ContainerFromIndex(i) is not { } row)
                        {
                            defects.Add((variant, $"not realized when scrolled into view: {rowIds[i]}"));
                            continue;
                        }

                        Assert.Equal(rowIds[i], AutomationIds.Get(row));
                        Assert.True(row.IsEffectivelyVisible, $"{variant}: {rowIds[i]} is not shown");
                        if (VisualSnapshot.HiddenPart(row, dlg.Window) is { } hidden)
                            defects.Add((variant, $"cut: {rowIds[i]} — {hidden}"));
                        var rowBox = new Rect(row.TranslatePoint(default, dlg.Window)!.Value, row.Bounds.Size);
                        var portBox = new Rect(port.TranslatePoint(default, dlg.Window)!.Value, port.Bounds.Size).Inflate(0.5);
                        if (!portBox.Contains(rowBox))
                            defects.Add((variant, $"cut: {rowIds[i]} {rowBox} outside the list's view port {portBox} when scrolled into view"));
                    }

                    list.ScrollIntoView(0);
                    Dispatcher.UIThread.RunJobs();
                    if (VisualSnapshot.DistinctColors(dlg.Window, parts[0]) < 3)
                        defects.Add((variant, "the page list is not drawn"));
                    defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
                    files.Add((subjects[0], theme, size, VisualSnapshot.Capture(dlg.Window, subjects[0], theme)));
                }

                dlg.Click("Open");
                opened = true;
                await running;
            }
            finally
            {
                if (!opened)
                {
                    dlg.Click("Cancel"); // also after a failure, so that the failure is what the test reports
                    await running;
                }
            }

            Dispatcher.UIThread.RunJobs();
            var document = Assert.IsType<GeneratedTextDocumentViewModel>(h.Services.Documents.ActiveDocument);
            Assert.Equal(("Library source", null, true), (document.Title, document.FilePath, document.IsReadOnly));
            var shown = Core.Services.HelpPageFormatter.Format(File.ReadAllText(deployed));
            Assert.Equal(shown.Text, document.Text);
            Assert.Equal(shown.Lines, document.HelpLines);
            foreach (var size in windowSizes)
            {
                VisualSnapshot.Arrange(h.Window, size, 1.0);
                var variant = $"{subjects[1]} {VisualSnapshot.FileName(theme, 1.0, size)}";
                Assert.Equal(size, h.Window.ClientSize);
                var tab = h.Find<Control>(AutomationIds.DocumentTab("Library source"));
                var editor = h.Find<TextEditor>("Document.Help.library-source");
                Assert.True(editor.IsReadOnly);
                Assert.Equal(document.Text, editor.Text);
                foreach (var control in (Control[])[tab, editor])
                {
                    Assert.True(control.IsEffectivelyVisible, $"{variant}: {AutomationIds.Get(control)} is not shown");
                    if (VisualSnapshot.HiddenPart(control, h.Window) is { } hidden)
                        defects.Add((variant, $"cut: {AutomationIds.Get(control)} — {hidden}"));
                }

                // The text keeps its left padding: no glyph touches the left edge of the tab's content.
                var textLeft = editor.TextArea.TextView.TranslatePoint(default, editor)!.Value.X;
                if (textLeft < 8)
                    defects.Add((variant, $"the text starts {textLeft} px from the left edge"));

                // Heading lines are styled by the help colorizer; body lines are not.
                Assert.Single(editor.TextArea.TextView.LineTransformers.OfType<HelpPageColorizer>());
                Assert.StartsWith("# ", editor.Document.GetText(editor.Document.GetLineByNumber(1)), StringComparison.Ordinal);
                Assert.Equal(Core.Services.HelpLineKind.Heading, document.HelpLines![0]);
                Assert.Equal(Core.Services.HelpLineKind.Text, document.HelpLines[2]);
                if (VisualSnapshot.DistinctColors(h.Window, editor) < 3)
                    defects.Add((variant, "the page text is not drawn"));
                defects.AddRange(VisualSnapshot.LayoutDefects(tab).Select(d => (variant, d)));
                files.Add((subjects[1], theme, size, VisualSnapshot.Capture(h.Window, subjects[1], theme)));
            }

            // Another generated document keeps its plain text: no heading style there.
            await h.ExecuteAsync("help.keyboardShortcuts");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(h.Find<TextEditor>("Document.KeyboardShortcuts").TextArea.TextView.LineTransformers.OfType<HelpPageColorizer>());
        }

        AssertSnapshots(files, subjects, 4, defects);
    }
}
