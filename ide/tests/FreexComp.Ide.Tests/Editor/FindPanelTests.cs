using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>The in-editor find / replace panel (I-058, dialogs.md §10, editor.md §10, UI-ED-055).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class FindPanelTests
{
    private const string Panel = "Editor.A.pas.FindPanel";

    private const string Code =
        "begin\r\n" +
        "  WriteLn('a');\r\n" +
        "  writeln('b');\r\n" +
        "end.\r\n";

    private static async Task<(HeadlessHarness H, TextEditor Editor)> OpenAsync(string text, params (string Key, string Value)[] settings)
    {
        var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings["appearance.theme"] = "light";
            foreach (var (key, value) in settings)
                o.Settings[key] = value;
            o.InitialFiles["A.pas"] = text;
        });
        var result = await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "A.pas"));
        Assert.True(result.Success, result.MessageId);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.A.pas");
        Assert.True(editor.TextArea.Focus());
        editor.TextArea.Caret.Offset = 0;
        Dispatcher.UIThread.RunJobs();
        return (h, editor);
    }

    private static void Search(HeadlessHarness h)
    {
        h.Clock.Advance(FindSession.Delay);
        Dispatcher.UIThread.RunJobs();
    }

    private static string Count(HeadlessHarness h, string panel = Panel) => h.Find<TextBlock>(panel + ".MatchCount").Text ?? "";

    private static bool Shown(HeadlessHarness h, string id) => h.TryFind<Control>(id) is { IsEffectivelyVisible: true };

    private static int At(string text, string what) => text.IndexOf(what, StringComparison.Ordinal);

    private static void SetText(TextBox box, string text)
    {
        box.Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-100")]
    [Trait("Req", "IDE-P1-101")]
    [Trait("Req", "UI-ED-055")]
    [Trait("Req", "UI-ED-054")]
    [Trait("Req", "IDE-P1-108")]
    public async Task IncrementalSearchAndCount()
    {
        var (h, editor) = await OpenAsync(Code);
        await using var _ = h;
        IEditorTestProbe probe = editor.FindAncestorOfType<CodeEditorView>()!;
        Assert.False(Shown(h, Panel));

        // Ctrl+F opens the panel and focuses the empty search field; the counter is empty.
        h.Press("Ctrl+F");
        Assert.True(Shown(h, Panel));
        Assert.False(Shown(h, Panel + ".ReplaceText"));
        Assert.Equal(Panel + ".SearchText", h.FocusedAutomationId);
        Assert.Equal("", Count(h));

        // Incremental: 100 ms after typing, every match is highlighted and the first after the origin selected.
        h.Type("Write");
        Assert.Empty(probe.FindHighlights);
        Search(h);
        int first = At(Code, "WriteLn"), second = At(Code, "writeln");
        Assert.Equal([(first, 5), (second, 5)], probe.FindHighlights);
        Assert.Equal((first, 5), probe.CurrentFindHighlight);
        Assert.Equal((first, 5), (editor.SelectionStart, editor.SelectionLength));
        Assert.Equal("1 of 2", Count(h));
        h.Type("Ln");
        Search(h);
        Assert.Equal(("1 of 2", first, 7), (Count(h), editor.SelectionStart, editor.SelectionLength));
        Assert.Equal(Panel + ".SearchText", h.FocusedAutomationId); // the focus stays in the field

        // UI-ED-054: no word highlight while the panel shows matches (the selection is exactly one identifier).
        h.Clock.Advance(WordHighlighter.Delay);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(probe.OccurrenceHighlights);

        // Enter = next, Shift+Enter = previous, wrapping with a status message.
        h.Press("Enter");
        Assert.Equal(("2 of 2", second), (Count(h), editor.SelectionStart));
        Assert.Equal((second, 7), probe.CurrentFindHighlight);
        h.Press("Enter");
        Assert.Equal(("1 of 2", first, Strings.Msg_Find_WrappedForward), (Count(h), editor.SelectionStart, h.Status.Message));
        h.Press("Shift+Enter");
        Assert.Equal(("2 of 2", second, Strings.Msg_Find_WrappedBackward), (Count(h), editor.SelectionStart, h.Status.Message));

        // The navigation recorded the text in the history (dialogs.Find.history.SearchText, UI-DLG-009).
        Assert.Equal(["WriteLn"], h.Services.Layout.GetDialogHistory("Find", "SearchText"));

        // No match: the counter says so; an invalid regular expression shows the message below the first row.
        var box = h.Find<TextBox>(Panel + ".SearchText");
        SetText(box, "zzz");
        Search(h);
        Assert.Equal(Strings.Msg_Find_NoMatches, Count(h));
        Assert.Empty(probe.FindHighlights);
        Assert.False(Shown(h, Panel + ".Error"));
        h.Press("Alt+R");
        Assert.True(h.Find<ToggleButton>(Panel + ".Regex").IsChecked);
        SetText(box, "(");
        Search(h);
        Assert.True(Shown(h, Panel + ".Error"));
        Assert.StartsWith(Strings.Msg_Find_RegexInvalid[..20], h.Find<TextBlock>(Panel + ".Error").Text, StringComparison.Ordinal);
        Assert.Equal("", Count(h));
        h.Press("Alt+R");
        SetText(box, "");
        Search(h);
        Assert.Equal("", Count(h));
        Assert.False(Shown(h, Panel + ".Error"));

        // Escape closes the panel: highlights gone, the current match stays selected, the text area has the focus.
        SetText(box, "writeln");
        Search(h);
        h.Press("Enter");
        var selected = (editor.SelectionStart, editor.SelectionLength);
        h.Press("Escape");
        Assert.False(Shown(h, Panel));
        Assert.Empty(probe.FindHighlights);
        Assert.Null(probe.CurrentFindHighlight);
        Assert.Equal(selected, (editor.SelectionStart, editor.SelectionLength));
        Assert.Equal("Editor.A.pas", h.FocusedAutomationId);
        Assert.Equal(["writeln", "WriteLn"], h.Services.Layout.GetDialogHistory("Find", "SearchText"));

        // Word highlight works again once the panel is closed.
        h.Clock.Advance(WordHighlighter.Delay);
        Dispatcher.UIThread.RunJobs();
        Assert.NotEmpty(probe.OccurrenceHighlights);

        // Ctrl+F with a single-line selection seeds the search text and selects it.
        editor.Select(At(Code, "end"), 3);
        h.Press("Ctrl+F");
        Assert.Equal(("end", 0, 3), (box.Text, box.SelectionStart, box.SelectionEnd));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-100")]
    [Trait("Req", "IDE-P1-107")]
    [Trait("Req", "IDE-P1-109")]
    public async Task ReplaceAllIsOneUndoStep()
    {
        const string text = "a := 1; a := 2;\r\nb := a + ab;\r\n";
        var (h, editor) = await OpenAsync(text);
        await using var _ = h;

        // Ctrl+H opens the panel in replace mode.
        h.Press("Ctrl+H");
        Assert.True(Shown(h, Panel + ".ReplaceText"));
        Assert.True(h.Find<ToggleButton>(Panel + ".ToggleReplace").IsChecked);
        Assert.Equal(Panel + ".SearchText", h.FocusedAutomationId);
        h.Type("a");
        h.Press("Alt+W");
        Assert.True(h.Find<ToggleButton>(Panel + ".WholeWord").IsChecked);
        Search(h);
        Assert.Equal("1 of 3", Count(h));

        // Tab goes to the replace field; Alt+A replaces all matches as one undo step.
        h.Press("Tab");
        Assert.Equal(Panel + ".ReplaceText", h.FocusedAutomationId);
        h.Type("x");
        h.Press("Alt+A");
        Assert.Equal("x := 1; x := 2;\r\nb := x + ab;\r\n", editor.Text);
        Assert.Equal(string.Format(Strings.Msg_Find_ReplacedMany, 3), h.Status.Message);
        Assert.Equal(["x"], h.Services.Layout.GetDialogHistory("Find", "ReplaceText"));
        editor.TextArea.Focus(); // edit.undo acts on a focused text field (I-153)
        Dispatcher.UIThread.RunJobs();
        await h.ExecuteAsync("edit.undo");
        Assert.Equal(text, editor.Text);
        await h.ExecuteAsync("edit.redo");
        Assert.Equal("x := 1; x := 2;\r\nb := x + ab;\r\n", editor.Text);
        await h.ExecuteAsync("edit.undo");

        // Enter in the replace field replaces the selected match, then selects the next one.
        h.Find<TextBox>(Panel + ".ReplaceText").Focus();
        editor.Select(0, 1);
        Dispatcher.UIThread.RunJobs();
        h.Press("Enter");
        Assert.Equal("x := 1; a := 2;\r\nb := a + ab;\r\n", editor.Text);
        Assert.Equal((At(text, "a := 2"), 1), (editor.SelectionStart, editor.SelectionLength));
        h.Click(Panel + ".Replace");
        Assert.Equal("x := 1; x := 2;\r\nb := a + ab;\r\n", editor.Text);

        // Ctrl+H in an open panel toggles the replace row; Ctrl+F switches back to find mode.
        h.Press("Ctrl+H");
        Assert.False(Shown(h, Panel + ".ReplaceText"));
        h.Press("Ctrl+H");
        Assert.True(Shown(h, Panel + ".ReplaceText"));
        h.Press("Ctrl+F");
        Assert.False(Shown(h, Panel + ".ReplaceText"));
        h.Press("Escape");

        // Read-only: find works, Replace and Replace All are disabled, search.replace too.
        h.Services.Documents.ActiveDocument!.GetType(); // the document is A.pas
        var doc = (Core.ViewModels.TextDocumentViewModel)h.Services.Documents.ActiveDocument!;
        doc.Buffer.IsReadOnly = true;
        Assert.False(h.Services.Commands.CanExecute("search.replace"));
        h.Press("Ctrl+F");
        h.Click(Panel + ".ToggleReplace");
        Assert.True(Shown(h, Panel + ".ReplaceText"));
        Assert.False(h.Find<Button>(Panel + ".Replace").IsEffectivelyEnabled);
        Assert.False(h.Find<Button>(Panel + ".ReplaceAll").IsEffectivelyEnabled);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-101")]
    [Trait("Req", "UI-DLG-102")]
    [Trait("Req", "UI-DLG-104")]
    [Trait("Req", "UI-DLG-105")]
    public async Task KeyboardShortcuts()
    {
        var (h, editor) = await OpenAsync(Code);
        await using var _ = h;

        // F3 without a search text opens the panel (UI-DLG-104).
        Assert.True(h.Services.Commands.CanExecute("search.findNext"));
        h.Press("F3");
        Assert.True(Shown(h, Panel));
        Assert.Equal(Panel + ".SearchText", h.FocusedAutomationId);
        h.Type("writeln");
        Search(h);
        Assert.Equal("1 of 2", Count(h));

        // Alt+C / Alt+W / Alt+R toggle the options from the field; they persist in uistate.json (find.*).
        h.Press("Alt+C");
        Search(h);
        Assert.Equal(("1 of 1", true, true), (Count(h), h.Services.Find.MatchCase, h.Services.Layout.GetUiFlag("find", "matchCase")));
        Assert.True(h.Find<ToggleButton>(Panel + ".MatchCase").IsChecked);
        h.Press("Alt+C");
        h.Press("Alt+W");
        Assert.Equal((false, true), (h.Services.Find.MatchCase, h.Services.Find.WholeWord));
        h.Press("Alt+W");
        h.Press("Alt+R");
        Assert.True(h.Services.Find.Regex);
        h.Press("Alt+R");
        Search(h);
        Assert.Equal("1 of 2", Count(h));

        // F3 / Shift+F3 work from the panel.
        h.Press("F3");
        Assert.Equal("2 of 2", Count(h));
        h.Press("Shift+F3");
        Assert.Equal("1 of 2", Count(h));

        // Tab order: search field → toggles → Previous → Next → Close (find mode).
        string[] order = [".MatchCase", ".WholeWord", ".Regex", ".InSelection", ".Previous", ".Next", ".Close"];
        foreach (var id in order.Where(id => id != ".InSelection")) // In selection is disabled without a multi-line selection
        {
            h.Press("Tab");
            Assert.Equal(Panel + id, h.FocusedAutomationId);
        }

        // Escape closes from anywhere in the panel; F3 in the text repeats the search without opening it.
        h.Press("Escape");
        Assert.False(Shown(h, Panel));
        Assert.Equal("Editor.A.pas", h.FocusedAutomationId);
        editor.TextArea.Caret.Offset = 0;
        editor.TextArea.ClearSelection();
        Dispatcher.UIThread.RunJobs();
        h.Press("F3");
        Assert.False(Shown(h, Panel));
        Assert.Equal(At(Code, "WriteLn"), editor.SelectionStart);

        // A multi-line selection presses In selection; Alt+L toggles it.
        editor.Select(At(Code, "writeln"), Code.Length - At(Code, "writeln"));
        h.Press("Ctrl+F");
        var inSelection = h.Find<ToggleButton>(Panel + ".InSelection");
        Assert.True(inSelection.IsChecked);
        Search(h);
        Assert.Equal("1 of 1", Count(h));
        h.Press("Alt+L");
        Assert.False(inSelection.IsChecked);
        Search(h);
        Assert.Equal("1 of 2", Count(h)); // nothing after the origin (the caret at the end): the first match

        // Up / Down step through the history.
        var box = h.Find<TextBox>(Panel + ".SearchText");
        SetText(box, "");
        h.Press("Enter"); // nothing to record
        Assert.Equal(["writeln"], h.Services.Layout.GetDialogHistory("Find", "SearchText"));
        h.Press("Up");
        Assert.Equal("writeln", box.Text);
        h.Press("Down");
        Assert.Equal("", box.Text);
        h.Press("Escape");

        // Each document has its own panel; the generated Keyboard Shortcuts document has one too (find mode).
        await h.ExecuteAsync("help.keyboardShortcuts");
        var shortcuts = h.Find<TextEditor>("Document.KeyboardShortcuts");
        shortcuts.TextArea.Focus();
        shortcuts.TextArea.Caret.Offset = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Services.Commands.CanExecute("search.replace"));
        h.Press("Ctrl+F");
        const string generated = "Document.KeyboardShortcuts.FindPanel";
        Assert.True(Shown(h, generated));
        Assert.Equal(generated + ".SearchText", h.FocusedAutomationId);
        SetText(h.Find<TextBox>(generated + ".SearchText"), "Ctrl+F");
        Search(h);
        Assert.StartsWith("1 of ", Count(h, generated), StringComparison.Ordinal);
        Assert.Equal("Ctrl+F", shortcuts.SelectedText);
        h.Press("Escape");
        Assert.False(Shown(h, generated));
        Assert.Equal("Document.KeyboardShortcuts", h.FocusedAutomationId);
    }
}
