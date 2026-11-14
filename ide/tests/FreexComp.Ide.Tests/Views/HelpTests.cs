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
}
