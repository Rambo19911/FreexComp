using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using FreexComp.Ide.Core.Resources;
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
        await TopLevel.GetTopLevel(h.Window)!.Clipboard!.SetTextAsync("x\ny\r\nz");

        h.Press("Ctrl+V");
        await h.WaitForAsync(() => doc.Buffer.Text != "a\r\n", "paste applied");
        Assert.Equal("x\r\ny\r\nza\r\n", doc.Buffer.Text);
        Assert.Equal(new TextPosition(3, 2), doc.ViewState.Caret);
        doc.Buffer.Undo();
        Assert.Equal("a\r\n", doc.Buffer.Text); // one undo step
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
