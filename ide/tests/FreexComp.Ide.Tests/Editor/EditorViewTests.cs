using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>The code editor view (I-043, IDE-ARCH-110…112).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class EditorViewTests
{
    private static async Task<TextDocumentViewModel> OpenAsync(HeadlessHarness h, string relativePath)
    {
        var result = await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, relativePath));
        Assert.True(result.Success, result.MessageId);
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<TextDocumentViewModel>(result.Value);
    }

    /// <summary>The editor of the active document, focused like a click into its text.</summary>
    private static TextEditor FocusEditor(HeadlessHarness h, string fileName)
    {
        var editor = h.Find<TextEditor>("Editor." + fileName);
        Assert.True(editor.TextArea.Focus());
        Dispatcher.UIThread.RunJobs();
        return editor;
    }

    private static IReadOnlyList<CodeEditorView> LiveEditorViews(HeadlessHarness h) =>
        [.. h.Window.GetVisualDescendants().OfType<CodeEditorView>()];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-110")]
    [Trait("Req", "IDE-ARCH-111")]
    public async Task TypingUpdatesBufferAndCaretStatus()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "unit A;\r\n");
        var doc = await OpenAsync(h, "A.pas");
        FocusEditor(h, "A.pas");

        h.Type("abc");
        Assert.Equal("abcunit A;\r\n", doc.Buffer.Text);
        Assert.Equal(new TextPosition(1, 4), doc.ViewState.Caret);
        Assert.Equal("Ln 1, Col 4", h.Status.Field("CaretPosition"));
        Assert.True(doc.IsModified);

        h.Press("Enter");
        Assert.Equal("abc\r\nunit A;\r\n", doc.Buffer.Text);
        Assert.Equal("Ln 2, Col 1", h.Status.Field("CaretPosition"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-111")]
    [Trait("Req", "IDE-ARCH-112")]
    public async Task ViewStateSurvivesTabSwitch()
    {
        var lines = string.Concat(Enumerable.Range(1, 300).Select(i => $"line {i}\r\n"));
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = lines;
            o.InitialFiles["B.pas"] = "unit B;\r\n";
        });
        var a = await OpenAsync(h, "A.pas");
        var editorA = h.Find<TextEditor>("Editor.A.pas");

        // The user selects backwards on line 120 and scrolls; the view mirrors it into the document's view state.
        var start = a.Buffer.GetOffset(120, 3);
        var end = a.Buffer.GetOffset(120, 7);
        editorA.TextArea.Selection = Selection.Create(editorA.TextArea, end, start);
        editorA.TextArea.Caret.Offset = start;
        editorA.GetVisualDescendants().OfType<ScrollViewer>().First().Offset = new Avalonia.Vector(0, 1500); // as the wheel does
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new TextPosition(120, 3), a.ViewState.Caret);
        Assert.Equal(new TextPosition(120, 7), a.ViewState.SelectionAnchor);
        Assert.Equal(1500, a.ViewState.VerticalOffset);

        // IDE-ARCH-112: only the active document's editor view exists.
        var b = await OpenAsync(h, "B.pas");
        var live = Assert.Single(LiveEditorViews(h));
        Assert.Same(b, live.DataContext);
        Assert.Null(h.TryFind<TextEditor>("Editor.A.pas"));
        Assert.Equal(new TextPosition(120, 3), a.ViewState.Caret); // untouched by the switch

        // Back to A: a new view restores caret, selection and scroll position from the view state.
        h.Services.Documents.Activate(a);
        Dispatcher.UIThread.RunJobs();
        live = Assert.Single(LiveEditorViews(h));
        Assert.Same(a, live.DataContext);
        var restored = h.Find<TextEditor>("Editor.A.pas");
        Assert.NotSame(editorA, restored);
        Assert.Equal(start, restored.CaretOffset);
        Assert.Equal(start, restored.SelectionStart);
        Assert.Equal(end - start, restored.SelectionLength);
        await h.WaitForAsync(() => restored.VerticalOffset == 1500, "scroll position restored");
        Assert.Equal("Ln 120, Col 3", h.Status.Field("CaretPosition"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-004")]
    public async Task ActivatingATabFocusesTheEditor()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = "unit A;\r\n";
            o.InitialFiles["B.pas"] = "unit B;\r\n";
        });
        await OpenAsync(h, "A.pas");
        await OpenAsync(h, "B.pas");
        object? Focused() => h.Window.FocusManager?.GetFocusedElement();
        FocusEditor(h, "B.pas");

        // A click on another tab: its editor takes the focus, so typing goes there.
        h.Click("DocumentTab.A.pas");
        Assert.Same(h.Find<TextEditor>("Editor.A.pas").TextArea, Focused());

        // Ctrl+Tab from the editor: the next document's editor.
        h.Press("Ctrl+Tab");
        Assert.Same(h.Find<TextEditor>("Editor.B.pas").TextArea, Focused());

        // window.focusEditor puts the focus into the text, not on the document area.
        h.Find<TextEditor>("Editor.B.pas").TextArea.Focus();
        h.Find<Control>("DocumentArea").Focus();
        Dispatcher.UIThread.RunJobs();
        h.Press("Alt+0");
        Assert.Same(h.Find<TextEditor>("Editor.B.pas").TextArea, Focused());
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-030")]
    public async Task UndoAndRedoGoThroughTheBuffer()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "unit A;\r\n");
        var doc = await OpenAsync(h, "A.pas");
        var editor = FocusEditor(h, "A.pas");

        // AvaloniaEdit's own undo/redo bindings are gone (its UndoStack would bypass the buffer's save point).
        Assert.DoesNotContain(editor.TextArea.CommandBindings,
            b => b.Command == ApplicationCommands.Undo || b.Command == ApplicationCommands.Redo);
        Assert.DoesNotContain(editor.TextArea.KeyBindings, b => b.Gesture is { KeyModifiers: KeyModifiers.Control, Key: Key.Z or Key.Y }
            || b.Gesture is { KeyModifiers: KeyModifiers.Control | KeyModifiers.Shift, Key: Key.Z });

        h.Type("x");
        Assert.True(doc.IsModified);
        h.Press("Ctrl+Z");
        Assert.Equal("unit A;\r\n", doc.Buffer.Text);
        Assert.False(doc.IsModified); // back at the save point: only ITextBuffer.Undo knows that
        h.Press("Ctrl+Y");
        Assert.Equal("xunit A;\r\n", doc.Buffer.Text);
        Assert.True(doc.IsModified);

        // The menu command acts on the focused editor too.
        await h.ExecuteAsync("edit.undo");
        Assert.Equal("unit A;\r\n", doc.Buffer.Text);
        Assert.False(doc.IsModified);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-076")]
    public async Task EnterUsesTheDocumentLineEnding()
    {
        // Mixed, CRLF dominant; the last line ends with LF, which AvaloniaEdit would copy.
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "a\r\nb\r\nc\n");
        var doc = await OpenAsync(h, "A.pas");
        var editor = FocusEditor(h, "A.pas");
        editor.TextArea.Caret.Offset = doc.Buffer.GetOffset(3, 2); // end of "c"
        Dispatcher.UIThread.RunJobs();

        h.Press("Enter");
        Assert.Equal("a\r\nb\r\nc\r\n\n", doc.Buffer.Text);
        Assert.Equal(new TextPosition(4, 1), doc.ViewState.Caret);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-076")]
    public async Task PasteNormalizesLineBreaks()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "a\r\n");
        var doc = await OpenAsync(h, "A.pas");
        FocusEditor(h, "A.pas");
        await h.Services.Clipboard.SetTextAsync("x\ny\r\nz");

        h.Press("Ctrl+V");
        await h.WaitForAsync(() => doc.Buffer.Text != "a\r\n", "paste applied");
        Assert.Equal("x\r\ny\r\nza\r\n", doc.Buffer.Text);
        Assert.Equal(new TextPosition(3, 2), doc.ViewState.Caret);
        doc.Buffer.Undo();
        Assert.Equal("a\r\n", doc.Buffer.Text); // one undo step
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-039")]
    [Trait("Req", "IDE-ED-040")]
    [Trait("Req", "UI-ED-010")]
    [Trait("Req", "UI-ED-058")]
    public async Task ZoomWrapWhitespaceToggles()
    {
        var longLine = "  begin " + string.Concat(Enumerable.Repeat("Writeln(1); ", 60)) + "end;";
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = "unit A;\r\n" + longLine + "\r\n";
            o.InitialFiles["B.pas"] = "unit B;\r\n";
        });
        var settings = h.Services.Settings;
        var commands = h.Services.Commands;
        var doc = await OpenAsync(h, "A.pas");
        var editor = FocusEditor(h, "A.pas");
        IEditorTestProbe Probe() => h.Find<TextEditor>("Editor.A.pas").FindAncestorOfType<CodeEditorView>()!;

        // The editor follows editor.fontSize and editor.fontFamily (empty = the mono font resource).
        Assert.Equal(13, editor.FontSize);
        settings.Set(SettingsCatalog.EditorFontSize, 15);
        settings.Set(SettingsCatalog.EditorFontFamily, "Courier New");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((15d, "Courier New"), (editor.FontSize, editor.FontFamily.Name));

        // IDE-ED-039: ±1 pt per document; the line-number margin's charWidth follows (UI-ED-010).
        var gutter = Probe().GutterWidth;
        Assert.False(commands.CanExecute("view.zoomReset"));
        h.Press("Ctrl+=");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((16d, 16), (editor.FontSize, doc.ViewState.FontSize));
        Assert.True(Probe().GutterWidth > gutter);
        Assert.True(commands.CanExecute("view.zoomReset"));
        h.Press("Ctrl+-");
        h.Press("Ctrl+-");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(14d, editor.FontSize);
        await h.ExecuteAsync("view.zoomReset");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((15d, (int?)null), (editor.FontSize, doc.ViewState.FontSize));

        // Limits 6…72.
        settings.Set(SettingsCatalog.EditorFontSize, 72);
        Dispatcher.UIThread.RunJobs();
        Assert.False(commands.CanExecute("view.zoomIn"));
        Assert.True(commands.CanExecute("view.zoomOut"));
        settings.Set(SettingsCatalog.EditorFontSize, 6);
        Dispatcher.UIThread.RunJobs();
        Assert.True(commands.CanExecute("view.zoomIn"));
        Assert.False(commands.CanExecute("view.zoomOut"));
        settings.Set(SettingsCatalog.EditorFontSize, 13);
        await h.ExecuteAsync("view.zoomIn");
        await h.ExecuteAsync("view.zoomIn");

        // IDE-ED-040: word wrap per document, checked in the menu, continuation lines keep the indentation.
        Assert.False(editor.WordWrap);
        h.Press("Alt+Z");
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.WordWrap);
        Assert.True(doc.ViewState.WordWrap);
        Assert.True(commands.Get("view.wordWrap").IsChecked);
        Assert.True(editor.Options.InheritWordWrapIndentation);

        // UI-ED-058: Show Whitespace toggles editor.showWhitespace for every editor.
        Assert.False(editor.Options.ShowSpaces);
        await h.ExecuteAsync("view.showWhitespace");
        Dispatcher.UIThread.RunJobs();
        Assert.True(settings.Get(SettingsCatalog.EditorShowWhitespace));
        Assert.True(commands.Get("view.showWhitespace").IsChecked);
        Assert.True(editor.Options.ShowSpaces && editor.Options.ShowTabs);

        // Another document: its own zoom and wrap; a new one starts from editor.wordWrap.
        settings.Set(SettingsCatalog.EditorWordWrap, true);
        await OpenAsync(h, "B.pas");
        var b = h.Find<TextEditor>("Editor.B.pas");
        Assert.Equal((13d, true), (b.FontSize, b.WordWrap));
        Assert.False(commands.CanExecute("view.zoomReset"));
        Assert.True(commands.CanExecute("view.wordWrap"));
        Assert.True(commands.Get("view.wordWrap").IsChecked);
        Assert.True(b.Options.ShowSpaces);

        // Zoom (percent of editor.fontSize) and word wrap go into the session and come back.
        h.Services.Session.Save();
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(h.Services.Session.SessionPath))!;
        var entry = json["documents"]!.AsArray().Single(d => ((string)d!["path"]!).EndsWith("A.pas", StringComparison.Ordinal))!;
        Assert.Equal((115, true), ((int)entry["zoom"]!, (bool)entry["wordWrap"]!));
        await h.Services.Documents.CloseAllAsync();
        settings.Set(SettingsCatalog.EditorWordWrap, false);
        await h.Services.Session.RestoreAsync();
        var restored = Assert.IsType<TextDocumentViewModel>(h.Services.Documents.Documents.Single(d => d.Title == "A.pas"));
        Assert.Equal((15, true), (restored.ViewState.FontSize, restored.ViewState.WordWrap));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-038")]
    public async Task ClassicClipboardKeysCopyAndPasteLines()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "one\r\ntwo\r\n");
        var doc = await OpenAsync(h, "A.pas");
        var editor = FocusEditor(h, "A.pas");
        editor.TextArea.Caret.Offset = doc.Buffer.GetOffset(2, 2);
        Dispatcher.UIThread.RunJobs();

        h.Press("Ctrl+Insert"); // edit.copy without a selection: the caret line, through IClipboardService
        await h.WaitForAsync(() => ((FreexComp.Ide.TestSupport.FakeClipboard)h.Services.Clipboard).Text == "two\r\n", "line copied");
        h.Press("Shift+Insert"); // edit.paste: the line goes in above the caret line
        await h.WaitForAsync(() => doc.Buffer.Text == "one\r\ntwo\r\ntwo\r\n", "line pasted");
        Assert.Equal(new TextPosition(3, 2), doc.ViewState.Caret);
        h.Press("Shift+Delete"); // edit.cut without a selection: the caret line
        await h.WaitForAsync(() => doc.Buffer.Text == "one\r\ntwo\r\n", "line cut");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-010")]
    [Trait("Req", "IDE-ED-006")]
    [Trait("Req", "IDE-ED-004")]
    public async Task EditorKeysUseTheIndentationRules()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "  begin\r\n");
        var doc = await OpenAsync(h, "A.pas");
        var editor = FocusEditor(h, "A.pas");
        editor.TextArea.Caret.Offset = doc.Buffer.GetOffset(1, 8);
        Dispatcher.UIThread.RunJobs();

        h.Press("Enter"); // IDE-ED-006/007 instead of AvaloniaEdit's own Enter
        Assert.Equal("  begin\r\n    \r\n", doc.Buffer.Text);
        h.Press("Backspace"); // IDE-ED-004: back to the previous tab stop
        Assert.Equal("  begin\r\n  \r\n", doc.Buffer.Text);
        h.Press("Tab");
        Assert.Equal("  begin\r\n    \r\n", doc.Buffer.Text);
        h.Type("en");
        h.Type("d"); // IDE-ED-008
        Assert.Equal("  begin\r\n  end\r\n", doc.Buffer.Text);
        h.Press("Home");
        Assert.Equal(new TextPosition(2, 3), doc.ViewState.Caret);

        // IDE-ED-010: pasted lines are inserted verbatim, without re-indentation.
        await h.Services.Clipboard.SetTextAsync("a\r\nb");
        h.Press("Ctrl+V");
        await h.WaitForAsync(() => doc.Buffer.Text.Contains('b', StringComparison.Ordinal), "paste applied");
        Assert.Equal("  begin\r\n  a\r\nbend\r\n", doc.Buffer.Text);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-031")]
    [Trait("Req", "IDE-ED-033")]
    public async Task LineCommandsActOnTheEditorSelection()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "one\r\ntwo\r\n");
        var doc = await OpenAsync(h, "A.pas");
        var editor = FocusEditor(h, "A.pas");
        editor.TextArea.Caret.Offset = doc.Buffer.GetOffset(2, 2);
        Dispatcher.UIThread.RunJobs();

        h.Press("Ctrl+D"); // edit.duplicateLine: the caret moves into the copy
        Assert.Equal("one\r\ntwo\r\ntwo\r\n", doc.Buffer.Text);
        Assert.Equal(doc.Buffer.GetOffset(3, 2), editor.TextArea.Caret.Offset);
        editor.Select(doc.Buffer.GetOffset(2, 1), 7); // "two\r\ntw"
        Dispatcher.UIThread.RunJobs();

        h.Press("Alt+Up"); // edit.moveLineUp: the selection moves with the lines
        Assert.Equal("two\r\ntwo\r\none\r\n", doc.Buffer.Text);
        Assert.Equal((0, 7), (editor.SelectionStart, editor.SelectionLength));
        await h.ExecuteAsync("edit.undo");
        Assert.Equal("one\r\ntwo\r\ntwo\r\n", doc.Buffer.Text); // one undo step
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-040")]
    public async Task TypingIntoReadOnlyDocumentIsRefused()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "unit A;\r\n");
        var path = Path.Combine(h.TempDir, "A.pas");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var doc = await OpenAsync(h, "A.pas");
            Assert.True(doc.IsReadOnly);
            FocusEditor(h, "A.pas");

            h.Type("x");
            h.Press("Enter");
            Assert.Equal("unit A;\r\n", doc.Buffer.Text);
            Assert.Equal(Strings.Msg_Editor_ReadOnlyEdit, h.Status.Message);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }
}
