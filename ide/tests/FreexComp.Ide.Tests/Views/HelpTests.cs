using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Help menu documents (menus.md §2.10).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class HelpTests
{
    /// <summary>The rows of the generated table: columns are separated by at least two spaces.</summary>
    private static List<string[]> Rows(string text) =>
    [
        .. text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
    ];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-030")]
    public async Task KeyboardShortcutsDocumentListsKeymap()
    {
        await using var h = await HeadlessHarness.StartAsync();
        h.Services.Keymap.SetGestures("file.saveAs", [KeyGesture.Parse("Ctrl+Alt+S")]);

        await h.ExecuteAsync("help.keyboardShortcuts");
        var doc = Assert.IsType<GeneratedTextDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Assert.Equal(("Keyboard Shortcuts", null, false, true), (doc.Title, doc.FilePath, doc.IsModified, doc.IsReadOnly));

        // Columns Category, Command, Shortcut; one row per bound command of the current keymap (all its gestures).
        var rows = Rows(doc.Text);
        Assert.Equal(["Category", "Command", "Shortcut"], rows[0]);
        var body = rows.Skip(1).ToList();
        Assert.All(body, r => Assert.Equal(3, r.Length));
        Assert.Contains(["File", "Save", "Ctrl+S"], body);
        Assert.Contains(["File", "Close", "Ctrl+W, Ctrl+F4"], body);
        Assert.Contains(["File", "Save As…", "Ctrl+Alt+S"], body);       // the user's binding
        Assert.Contains(["Edit", "Redo", "Ctrl+Y, Ctrl+Shift+Z"], body);
        Assert.DoesNotContain(body, r => r[1] == "New Unit");             // unbound
        var bound = CommandCatalog.All.Count(c => h.Services.Keymap.GetGestures(c.Id).Count > 0);
        Assert.Equal(bound, body.Count);

        // Sorted by category, then title.
        var sorted = body.OrderBy(r => r[0], StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r[1], StringComparer.CurrentCultureIgnoreCase).ToList();
        Assert.Equal(sorted.Select(r => string.Join("|", r)), body.Select(r => string.Join("|", r)));

        // Not a file document: Save / Save As are disabled; the text is shown read-only in its tab.
        Assert.False(h.Services.Commands.CanExecute("file.save"));
        Assert.False(h.Services.Commands.CanExecute("file.saveAs"));
        Assert.NotNull(h.Find<Avalonia.Controls.Control>("DocumentTab.Keyboard Shortcuts"));
        var editor = h.Find<TextEditor>("Document.KeyboardShortcuts");
        Assert.True(editor.IsReadOnly);
        Assert.Equal(doc.Text, editor.Text);

        // Running it again reuses the tab and regenerates the text from the current keymap.
        h.Services.Keymap.SetGestures("file.saveAs", []);
        await h.ExecuteAsync("file.newUnit");
        await h.ExecuteAsync("help.keyboardShortcuts");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(doc, Assert.Single(h.Services.Documents.Documents.OfType<GeneratedTextDocumentViewModel>()));
        Assert.Same(doc, h.Services.Documents.ActiveDocument);
        Assert.DoesNotContain(Rows(doc.Text), r => r[1] == "Save As…");
        Assert.Equal(doc.Text, h.Find<TextEditor>("Document.KeyboardShortcuts").Text);
    }

    /// <summary>
    /// I-257: Help Contents without an installed page (the IDE of a test runs from an empty directory) shows the
    /// empty text in place of the list, Open is disabled and Cancel opens nothing.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-MENU-031")]
    [Trait("Req", "UI-DLG-282")]
    public async Task ContentsDialogWithoutPagesShowsEmptyText()
    {
        await using var h = await HeadlessHarness.StartAsync();
        Assert.True(h.Services.Commands.CanExecute("help.contents"));

        var running = h.Services.Commands.ExecuteAsync("help.contents");
        var dlg = await h.WaitForDialogAsync("HelpContents");
        try
        {
        Assert.Equal("Help Contents", dlg.Window.Title);
        var empty = dlg.Find<Avalonia.Controls.TextBlock>("Empty");
        Assert.Equal("No help pages are installed.", empty.Text);
        Assert.Equal("Dialog.HelpContents.Empty", AutomationIds.Get(empty));
        Assert.Equal(h.Window.FindResource(h.Window.ActualThemeVariant, "FxcTextSecondaryBrush"), empty.Foreground);

        // Centred in the place of the list (UI-DLG-282): the dialog's area above its button strip.
        var root = Assert.IsType<DockPanel>(empty.Parent);
        var strip = root.Children[0];
        Assert.NotSame(empty, strip);
        var centre = empty.TranslatePoint(new Point(empty.Bounds.Width / 2, empty.Bounds.Height / 2), root)!.Value;
        Assert.Equal(root.Bounds.Width / 2, centre.X, 1.0);
        Assert.Equal(strip.Bounds.Top / 2, centre.Y, 1.0);
        Assert.True(strip.Bounds.Top > 200, "the button strip is at the bottom");
        Assert.Null(dlg.TryFind<Avalonia.Controls.Control>("Pages"));
        Assert.Empty(dlg.FindAll("Dialog.HelpContents.Page."));
        Assert.False((dlg.TryFind<Avalonia.Controls.Button>("Open") ?? dlg.Find<Avalonia.Controls.Button>("Button.Open")).IsEffectivelyEnabled);
        dlg.Press("Enter"); // nothing to open: the dialog stays
        Assert.True(dlg.Window.IsVisible);
        }
        finally
        {
            dlg.Click("Cancel"); // also after a failure, so that the failure is what the test reports
            await running;
        }

        Assert.Empty(h.Services.Documents.Documents.OfType<GeneratedTextDocumentViewModel>());
    }

    private static byte[] Page(string markdown) => System.Text.Encoding.UTF8.GetBytes(markdown);

    private static void InstallPages(HarnessOptions o)
    {
        o.UserFiles["ide/help/b.md"] = Page("# Beta\n\nThe second page.\n\n| Key | Meaning |\n|---|---|\n| `F1` | Help Contents |\n");
        o.UserFiles["ide/help/zz.md"] = Page("intro\n\n# alpha\n");
        o.UserFiles["ide/help/sub/c.md"] = Page("## No first-level heading\n");
    }

    /// <summary>
    /// UI-DLG-280, UI-DLG-281: one row per installed page (its first <c># </c> heading, else its id), sorted by title
    /// then id, the first selected, each with its AutomationId; Open (default, accent), Enter and a double-click on a
    /// row close the dialog and show that page; Cancel and Esc open nothing; the folder is read every time.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-280")]
    [Trait("Req", "UI-DLG-281")]
    public async Task ContentsDialogListsPagesAndOpensTheChosenOne()
    {
        await using var h = await HeadlessHarness.StartAsync(InstallPages);
        List<GeneratedTextDocumentViewModel> Open() => [.. h.Services.Documents.Documents.OfType<GeneratedTextDocumentViewModel>()];

        async Task<GeneratedTextDocumentViewModel?> ChooseAsync(Action<DialogDriver> act)
        {
            var before = h.Services.Documents.ActiveDocument;
            var running = h.Services.Commands.ExecuteAsync("help.contents");
            var dlg = await h.WaitForDialogAsync("HelpContents");
            try
            {
                act(dlg);
                Assert.False(dlg.Window.IsVisible, "the dialog is still open");
            }
            finally
            {
                if (dlg.Window.IsVisible)
                    dlg.Click("Cancel"); // after a failure, so that the failure is what the test reports
                await running;
            }

            Dispatcher.UIThread.RunJobs();
            return ReferenceEquals(before, h.Services.Documents.ActiveDocument) ? null : h.Services.Documents.ActiveDocument as GeneratedTextDocumentViewModel;
        }

        static void DoubleClick(DialogDriver dlg, string id)
        {
            var row = dlg.Find<Avalonia.Controls.Control>(id);
            var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), dlg.Window)!.Value;
            dlg.Window.MouseDown(point, Avalonia.Input.MouseButton.Left);
            dlg.Window.MouseUp(point, Avalonia.Input.MouseButton.Left);
            Assert.True(dlg.Window.IsVisible, "one click on a row does not close the dialog");
            dlg.Window.MouseDown(point, Avalonia.Input.MouseButton.Left); // the second press is the double-click: it closes the dialog
            Dispatcher.UIThread.RunJobs();
            if (dlg.Window.IsVisible)
                dlg.Window.MouseUp(point, Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        // The list: rows in order, the first selected; Esc opens nothing.
        Assert.Null(await ChooseAsync(dlg =>
        {
            var list = dlg.Find<Avalonia.Controls.ListBox>("Pages");
            Assert.Equal("Dialog.HelpContents.Pages", AutomationIds.Get(list));
            var rows = dlg.FindAll("Dialog.HelpContents.Page.");
            Assert.Equal(
                ["Dialog.HelpContents.Page.zz", "Dialog.HelpContents.Page.b", "Dialog.HelpContents.Page.sub/c"],
                rows.Select(AutomationIds.Get));
            Assert.Equal(["alpha", "Beta", "sub/c"],
                rows.Select(r => Assert.IsType<Avalonia.Controls.TextBlock>(Assert.IsType<Avalonia.Controls.ListBoxItem>(r).Content).Text));
            Assert.Equal(0, list.SelectedIndex);
            Assert.Null(dlg.TryFind<Avalonia.Controls.Control>("Empty"));
            var open = dlg.TryFind<Avalonia.Controls.Button>("Open") ?? dlg.Find<Avalonia.Controls.Button>("Button.Open");
            Assert.True(open.IsEffectivelyEnabled);
            Assert.Contains("accent", open.Classes);

            // A double-click below the rows is not one on a row: nothing opens.
            var free = list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height - 6), dlg.Window)!.Value;
            for (var i = 0; i < 2; i++)
            {
                dlg.Window.MouseDown(free, Avalonia.Input.MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.True(dlg.Window.IsVisible);
                dlg.Window.MouseUp(free, Avalonia.Input.MouseButton.Left);
            }

            Dispatcher.UIThread.RunJobs();
            dlg.Press("Escape");
        }));
        Assert.Empty(Open());

        // Cancel opens nothing either.
        Assert.Null(await ChooseAsync(dlg => dlg.Click("Cancel")));
        Assert.Empty(Open());

        // Open shows the selected row's page (the first one).
        var alpha = Assert.IsType<GeneratedTextDocumentViewModel>(await ChooseAsync(dlg => dlg.Click("Open")));
        Assert.Equal(("alpha", "help.zz", true), (alpha.Title, alpha.Kind, alpha.IsReadOnly));

        // Enter opens the row chosen with the keyboard.
        var beta = Assert.IsType<GeneratedTextDocumentViewModel>(await ChooseAsync(dlg =>
        {
            Assert.Equal("Dialog.HelpContents.Page.zz", dlg.FocusedAutomationId); // the selected row has the focus
            dlg.Press("Down");
            Assert.Equal(1, dlg.Find<Avalonia.Controls.ListBox>("Pages").SelectedIndex);
            dlg.Press("Enter");
        }));
        Assert.Equal(("Beta", "help.b"), (beta.Title, beta.Kind));

        // A double-click on a row opens that row's page.
        var third = Assert.IsType<GeneratedTextDocumentViewModel>(await ChooseAsync(dlg => DoubleClick(dlg, "Page.sub/c")));
        Assert.Equal(("sub/c", "help.sub/c"), (third.Title, third.Kind));
        Assert.Equal([alpha, beta, third], Open());

        // A page that is open already: its tab is reused (UI-MENU-032).
        Assert.Same(alpha, await ChooseAsync(dlg => DoubleClick(dlg, "Page.zz")));
        Assert.Equal(3, Open().Count);

        // Read from the disk each time the dialog opens: a page installed meanwhile is listed, a removed one is not.
        File.WriteAllText(Path.Combine(h.UserDir, "ide", "help", "added.md"), "# Added\n");
        File.Delete(Path.Combine(h.UserDir, "ide", "help", "b.md"));
        Assert.Null(await ChooseAsync(dlg =>
        {
            Assert.Equal(["Dialog.HelpContents.Page.added", "Dialog.HelpContents.Page.zz", "Dialog.HelpContents.Page.sub/c"],
                dlg.FindAll("Dialog.HelpContents.Page.").Select(AutomationIds.Get));
            dlg.Press("Escape");
        }));
    }

    /// <summary>
    /// UI-MENU-032: a help page is a read-only generated document tab titled with the page title, its text view
    /// <c>Document.Help.&lt;id&gt;</c>: Save / Save As are disabled, Ctrl+F finds in the text shown, the view wraps (no
    /// horizontal scroll bar), has no line numbers, and shows the page's table aligned without its delimiter row; the file
    /// is not modified.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-MENU-032")]
    public async Task HelpPageIsAReadOnlyWrappedDocument()
    {
        await using var h = await HeadlessHarness.StartAsync(InstallPages);
        var file = Path.Combine(h.UserDir, "ide", "help", "b.md");
        var bytes = File.ReadAllBytes(file);
        h.Services.Help.OpenPage(h.Services.Help.Pages.Single(p => p.Id == "b"));
        Dispatcher.UIThread.RunJobs();

        var doc = Assert.IsType<GeneratedTextDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Assert.Equal(("Beta", null, false, true), (doc.Title, doc.FilePath, doc.IsModified, doc.IsReadOnly));
        Assert.Equal("# Beta\r\n\r\nThe second page.\r\n\r\nKey   Meaning\r\n`F1`  Help Contents\r\n", doc.Text);
        Assert.False(h.Services.Commands.CanExecute("file.save"));
        Assert.False(h.Services.Commands.CanExecute("file.saveAs"));
        Assert.NotNull(h.Find<Avalonia.Controls.Control>("DocumentTab.Beta"));

        var editor = h.Find<TextEditor>("Document.Help.b");
        Assert.True(editor.IsReadOnly);
        Assert.Equal(doc.Text, editor.Text);
        Assert.True(editor.WordWrap);
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, editor.HorizontalScrollBarVisibility);
        Assert.False(editor.ShowLineNumbers);
        Assert.False(editor.Options.HighlightCurrentLine);
        var left = editor.TextArea.TextView.TranslatePoint(default, editor)!.Value.X;
        Assert.True(left >= 8, $"the text starts {left} px from the left edge of the document");

        // Typing changes nothing.
        editor.TextArea.Focus();
        editor.TextArea.Caret.Offset = 0;
        Dispatcher.UIThread.RunJobs();
        h.Type("x");
        Assert.Equal(doc.Text, editor.Text);

        // Ctrl+F: the find panel of this document (find only), searching the text shown.
        Assert.False(h.Services.Commands.CanExecute("search.replace"));
        h.Press("Ctrl+F");
        const string panel = "Document.Help.b.FindPanel";
        Assert.True(h.TryFind<Avalonia.Controls.Control>(panel) is { IsEffectivelyVisible: true });
        Assert.Equal(panel + ".SearchText", h.FocusedAutomationId);
        h.Find<Avalonia.Controls.TextBox>(panel + ".SearchText").Text = "Help Contents";
        Dispatcher.UIThread.RunJobs();
        h.Clock.Advance(Core.Editing.FindSession.Delay);
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("1 of 1", h.Find<Avalonia.Controls.TextBlock>(panel + ".MatchCount").Text, StringComparison.Ordinal);
        Assert.Equal("Help Contents", editor.SelectedText);
        h.Press("Escape");
        Assert.Equal("Document.Help.b", h.FocusedAutomationId);

        // Opening the page again reuses its tab; the file was only read.
        h.Services.Help.OpenPage(h.Services.Help.Pages.Single(p => p.Id == "b"));
        Dispatcher.UIThread.RunJobs();
        Assert.Same(doc, Assert.Single(h.Services.Documents.Documents.OfType<GeneratedTextDocumentViewModel>()));
        Assert.Equal(bytes, File.ReadAllBytes(file));

        // The Keyboard Shortcuts document, in the same kind of view, keeps its plain code-font text without wrap.
        await h.ExecuteAsync("help.keyboardShortcuts");
        Dispatcher.UIThread.RunJobs();
        var shortcuts = h.Find<TextEditor>("Document.KeyboardShortcuts");
        Assert.False(shortcuts.WordWrap);
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, shortcuts.HorizontalScrollBarVisibility);
    }

    /// <summary>
    /// I-257: the text of a generated document (Keyboard Shortcuts, a help page: one view) does not start at the left
    /// edge of its tab, where the first glyph of every line was cut: the text area keeps 8 px free before it.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-MENU-030")]
    public async Task GeneratedDocumentTextKeepsLeftPadding()
    {
        await using var h = await HeadlessHarness.StartAsync();
        await h.ExecuteAsync("help.keyboardShortcuts");
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Document.KeyboardShortcuts");
        var view = editor.TextArea.TextView;
        var left = view.TranslatePoint(default, editor)!.Value.X;
        Assert.True(left >= 8, $"the text starts {left} px from the left edge of the document");

        // Also when the text is scrolled to its start and the first column is asked for: column 1 lies inside the view.
        var first = view.GetVisualPosition(new AvaloniaEdit.TextViewPosition(1, 1), AvaloniaEdit.Rendering.VisualYPosition.LineTop);
        Assert.True(view.TranslatePoint(first - view.ScrollOffset, editor)!.Value.X >= 8);
    }
}
