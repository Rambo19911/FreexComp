using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
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

    /// <summary>
    /// F13-022 (IDE-P1-108, IDE-P1-134): Escape ends the search at the found text — the match stays selected, the editor
    /// has the focus — and Navigate Back (Alt+Left) then puts the caret back where the search started.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-108")]
    [Trait("Req", "IDE-P1-134")]
    public async Task EscapeKeepsMatchAndNavigateBackReturnsToOrigin()
    {
        var text = string.Join("\r\n", Enumerable.Range(1, 40).Select(i => i switch
        {
            5 => "  need",
            30 => "  needle here",
            _ => "line",
        })) + "\r\n";
        var (h, editor) = await OpenAsync(text);
        await using var _ = h;
        IEditorTestProbe probe = editor.FindAncestorOfType<CodeEditorView>()!;
        var origin = editor.Document.GetOffset(2, 3);
        editor.TextArea.Caret.Offset = origin; // the search starts at line 2, column 3
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(h.Services.Navigation.History);

        // Ctrl+F and typing "need": the first match after the origin, 3 lines below, is selected; nothing is recorded.
        h.Press("Ctrl+F");
        h.Type("need");
        Search(h);
        Assert.Equal(("1 of 2", At(text, "need"), 4), (Count(h), editor.SelectionStart, editor.SelectionLength));
        Assert.Equal(5, editor.TextArea.Caret.Line);
        Assert.Empty(h.Services.Navigation.History);

        // Typing on ("le"): the selection moves to the first "needle" after the original caret, 28 lines below it.
        h.Type("le");
        Search(h);
        var match = At(text, "needle");
        Assert.Equal(("1 of 1", match, 6), (Count(h), editor.SelectionStart, editor.SelectionLength));
        Assert.Equal(30, editor.TextArea.Caret.Line);
        Assert.Equal(Panel + ".SearchText", h.FocusedAutomationId);
        var recorded = Assert.Single(h.Services.Navigation.History);
        Assert.Equal(("A.pas", new TextPosition(2, 3)), (recorded.Name, recorded.Position));

        // Escape: the panel and the highlights go, the match stays selected, the text area has the focus.
        h.Press("Escape");
        Assert.False(Shown(h, Panel));
        Assert.Empty(probe.FindHighlights);
        Assert.Equal((match, 6), (editor.SelectionStart, editor.SelectionLength));
        Assert.Equal("Editor.A.pas", h.FocusedAutomationId);

        // Alt+Left: back at the line and column the search started from, nothing selected.
        h.Press("Alt+Left");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((origin, 0), (editor.TextArea.Caret.Offset, editor.SelectionLength));
        Assert.Equal((2, 3), (editor.TextArea.Caret.Line, editor.TextArea.Caret.Column));
        Assert.Equal("Editor.A.pas", h.FocusedAutomationId);

        // Alt+Right returns to the found text.
        h.Press("Alt+Right");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(30, editor.TextArea.Caret.Line);
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

    /// <summary>
    /// UI-ED-100: the panel floats over the text at the top-right corner, flush with the top, 8 px left of the vertical
    /// scrollbar; bg.raised with a 1 px border.default on left, bottom and right; at least 420 wide; 34 high (find) or
    /// 66 (find + replace).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-100")]
    public async Task PanelGeometry()
    {
        var (h, editor) = await OpenAsync(Code);
        await using var _ = h;
        await h.ExecuteAsync("search.find");
        var panel = h.Find<FindPanelView>(Panel);
        var textView = editor.TextArea.TextView;
        Dispatcher.UIThread.RunJobs();

        Avalonia.Rect InWindow(Avalonia.Visual v) => new(v.TranslatePoint(default, h.Window)!.Value, v.Bounds.Size);
        var rect = InWindow(panel);
        var view = InWindow(textView);
        var scrollBar = editor.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
        Assert.Equal(view.Top, rect.Top, 1); // flush with the top of the text area
        Assert.Equal(scrollBar.IsVisible ? InWindow(scrollBar).Left - 8 : view.Right - 8, rect.Right, 1);
        Assert.True(rect.Width >= 420, $"{rect.Width} wide");
        Assert.Equal(34, rect.Height, 1);
        Assert.Equal(new Avalonia.Thickness(1, 0, 1, 1), panel.BorderThickness);
        Avalonia.Media.Color Token(string key) => Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(
            Avalonia.Application.Current!.TryGetResource(key, h.Window.ActualThemeVariant, out var value) ? value : null).Color;
        Assert.Equal(Token("FxcBgRaisedBrush"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(panel.Background).Color);
        Assert.Equal(Token("FxcBorderDefaultBrush"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(panel.BorderBrush).Color);
        Assert.Equal(view.Top, InWindow(textView).Top); // the text is not pushed down

        await h.ExecuteAsync("search.replace");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(66, InWindow(panel).Height, 1);
    }

    /// <summary>
    /// I-377 (real-window finding): the text of the search and of the replace field is whole. The field is 26 high
    /// (UI-ED-100: 34 = 4 + 26 + 4), so a line of its font must fit between its borders: the text presenter lies inside
    /// the field's viewport, the viewport inside the border, and the viewport is at least one text line high — with the
    /// real fonts (the headless font's line is lower), at 100, 125, 150 and 200 % (UI-TOK-062), light and dark.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-ED-100")]
    [Trait("Req", "UI-TOK-062")]
    public async Task SearchFieldTextIsNotClipped()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(FindPanelTests));
            return;
        }

        const string Sample = "gypsy Ålqj"; // ascenders and descenders
        var problems = new List<string>();
        foreach (var theme in new[] { "light", "dark" })
        {
            var (h, _) = await OpenAsync(Code, ("appearance.theme", theme));
            await using var __ = h;
            await h.ExecuteAsync("search.replace");
            foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                h.Window.SetRenderScaling(scale);
                Dispatcher.UIThread.RunJobs();
                foreach (var id in new[] { Panel + ".SearchText", Panel + ".ReplaceText" })
                {
                    var box = h.Find<TextBox>(id);
                    Assert.True(box.IsEffectivelyVisible, id);
                    SetText(box, Sample);
                    var presenter = box.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Single();
                    var viewport = box.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ScrollContentPresenter>().First();
                    var line = new Avalonia.Media.TextFormatting.TextLayout(Sample,
                        new Avalonia.Media.Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch), box.FontSize, null).Height;
                    var text = new Rect(presenter.TranslatePoint(default, box)!.Value, presenter.Bounds.Size);
                    var clip = new Rect(viewport.TranslatePoint(default, box)!.Value, viewport.Bounds.Size);
                    var inner = new Rect(box.Bounds.Size).Deflate(box.BorderThickness);
                    // design-tokens.md §8: a text input is 26 high (on whole device pixels: 26.4 at 125 %).
                    Assert.InRange(box.Bounds.Height, 26 - 1 / scale, 26 + 1 / scale);
                    if (text.Top < clip.Top - 0.5 || text.Bottom > clip.Bottom + 0.5 || clip.Top < inner.Top - 0.5 || clip.Bottom > inner.Bottom + 0.5
                        || text.Height < line - 0.5 || clip.Height < line - 0.5)
                    {
                        problems.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"{theme} {scale * 100:0} % {id}: text {text.Top:0.##}…{text.Bottom:0.##} (line {line:0.##}) outside viewport {clip.Top:0.##}…{clip.Bottom:0.##} (field inner {inner.Top:0.##}…{inner.Bottom:0.##})"));
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// I-377: the labels of Replace and Replace All are whole too — the buttons are 26 high (design-tokens.md §8), so
    /// the label's text line must fit inside: with the real fonts, at 100, 125, 150 and 200 % (UI-TOK-062).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-ED-100")]
    [Trait("Req", "UI-TOK-062")]
    public async Task ReplaceButtonTextIsNotClipped()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(FindPanelTests));
            return;
        }

        var (h, _) = await OpenAsync(Code);
        await using var __ = h;
        await h.ExecuteAsync("search.replace");
        var problems = new List<string>();
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            h.Window.SetRenderScaling(scale);
            Dispatcher.UIThread.RunJobs();
            foreach (var id in new[] { Panel + ".Replace", Panel + ".ReplaceAll" })
            {
                var button = h.Find<Button>(id);
                Assert.True(button.IsEffectivelyVisible, id);
                Assert.InRange(button.Bounds.Height, 26 - 1 / scale, 26 + 1 / scale);
                var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
                var line = new Avalonia.Media.TextFormatting.TextLayout(label.Text,
                    new Avalonia.Media.Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch), label.FontSize, null).Height;
                var top = label.TranslatePoint(default, button)!.Value.Y;
                var inner = new Rect(button.Bounds.Size).Deflate(button.BorderThickness);
                if (label.Bounds.Height < line - 0.5 || top < inner.Top - 0.5 || top + line > inner.Bottom + 0.5)
                {
                    problems.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"{scale * 100:0} % {id}: label {top:0.##}…{top + label.Bounds.Height:0.##} (line {line:0.##}) in a button with inner {inner.Top:0.##}…{inner.Bottom:0.##}"));
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>UI-ED-101: a match the panel would cover (top 80 px, x-range under the panel) is scrolled to one third of the viewport.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-101")]
    public async Task MatchUnderThePanelIsScrolledToOneThird()
    {
        var lines = Enumerable.Range(1, 200).Select(i => i == 100 ? new string(' ', 55) + "needle" : "x").ToList();
        var (h, editor) = await OpenAsync(string.Join("\r\n", lines) + "\r\n");
        await using var _ = h;
        var textView = editor.TextArea.TextView;
        var scroller = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
        await h.ExecuteAsync("search.find");
        h.Type("needle");
        Search(h);
        editor.TextArea.Caret.Offset = 0;
        editor.TextArea.ClearSelection();

        // The match's line sits at the very top of the viewport, its x-range under the panel.
        var lineTop = textView.GetVisualTopByDocumentLine(100);
        scroller.Offset = new Avalonia.Vector(0, lineTop);
        Dispatcher.UIThread.RunJobs();
        await h.ExecuteAsync("search.findNext");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(100, editor.TextArea.Caret.Line);
        var panelRect = h.Find<FindPanelView>(Panel).TranslatePoint(default, textView);
        var matchX = textView.GetVisualPosition(editor.TextArea.Caret.Position, AvaloniaEdit.Rendering.VisualYPosition.LineTop).X - textView.HorizontalOffset;
        Assert.True(Math.Abs(lineTop - scroller.Viewport.Height / 3 - scroller.Offset.Y) < 1, $"offset {scroller.Offset.Y}, lineTop {lineTop}, viewport {scroller.Viewport.Height}, panel {panelRect} w {h.Find<FindPanelView>(Panel).Bounds.Width}, match x {matchX}, visible {h.Find<FindPanelView>(Panel).IsVisible}");
    }

    /// <summary>
    /// UI-ED-061: the vertical scrollbar is always shown, the horizontal one only when needed and never with word wrap;
    /// 12 thick, no arrow buttons; the wheel scrolls 3 lines per notch, Shift+wheel horizontally.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-061")]
    public async Task ScrollBarsAndWheel()
    {
        var lines = Enumerable.Range(1, 300).Select(i => new string('w', 300) + i); // wider than the view on every line
        var (h, editor) = await OpenAsync(string.Join("\r\n", lines) + "\r\n");
        await using var _ = h;
        var scroller = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.Equal(ScrollBarVisibility.Visible, editor.VerticalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Auto, editor.HorizontalScrollBarVisibility);
        var vertical = editor.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
        Assert.Equal(12, vertical.Bounds.Width, 0.5);
        Assert.False(vertical.AllowAutoHide);
        Assert.DoesNotContain(vertical.GetVisualDescendants().OfType<RepeatButton>(), b => b.IsEffectivelyVisible && b.Name is "PART_LineUpButton" or "PART_LineDownButton");

        var point = editor.TextArea.TextView.TranslatePoint(new Point(100, 100), h.Window)!.Value;
        var lineHeight = editor.TextArea.TextView.DefaultLineHeight;
        h.Window.MouseWheel(point, new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3 * lineHeight, scroller.Offset.Y, 0.5);
        h.Window.MouseWheel(point, new Vector(0, -1), Avalonia.Input.RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3 * lineHeight, scroller.Offset.Y, 0.5); // Shift: the vertical offset stays …
        Assert.True(scroller.Offset.X > 0, "… and the text scrolls horizontally");

        await h.ExecuteAsync("view.wordWrap");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ScrollBarVisibility.Disabled, editor.HorizontalScrollBarVisibility);
    }
}