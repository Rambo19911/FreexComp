using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>Undo grouping of typed input and caret restoration (I-051, phase1-shell.md §9).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class UndoGroupingTests
{
    private static async Task<(TextDocumentViewModel Doc, TextEditor Editor)> OpenAsync(HeadlessHarness h)
    {
        var result = await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "A.pas"));
        Assert.True(result.Success, result.MessageId);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.A.pas");
        Assert.True(editor.TextArea.Focus());
        Dispatcher.UIThread.RunJobs();
        return (Assert.IsType<TextDocumentViewModel>(result.Value), editor);
    }

    /// <summary>One text-input event per character, as from the keyboard.</summary>
    private static void TypeKeys(HeadlessHarness h, string text)
    {
        foreach (var ch in text)
            h.Type(ch.ToString());
    }

    private static void MoveCaret(TextEditor editor, int offset)
    {
        editor.TextArea.Caret.Offset = offset; // as a click does
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-091")]
    public async Task TypingGroupsByWordAndPause()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "");
        var (doc, editor) = await OpenAsync(h);

        // Word by word: a space after a non-space starts a new step.
        TypeKeys(h, "abc def");
        h.Press("Ctrl+Z");
        Assert.Equal("abc", doc.Buffer.Text);
        h.Press("Ctrl+Z");
        Assert.Equal("", doc.Buffer.Text);
        h.Press("Ctrl+Y");
        h.Press("Ctrl+Y");
        Assert.Equal("abc def", doc.Buffer.Text);

        // A pause of 1 s ends the step.
        TypeKeys(h, "gh");
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        TypeKeys(h, "ij");
        h.Press("Ctrl+Z");
        Assert.Equal("abc defgh", doc.Buffer.Text);
        h.Press("Ctrl+Z");
        Assert.Equal("abc def", doc.Buffer.Text);

        // A line break ends the step, and is a step of its own.
        TypeKeys(h, "x");
        h.Press("Enter");
        TypeKeys(h, "y");
        h.Press("Ctrl+Z");
        Assert.Equal("abc defx\r\n", doc.Buffer.Text);
        h.Press("Ctrl+Z");
        Assert.Equal("abc defx", doc.Buffer.Text);
        h.Press("Ctrl+Z");
        Assert.Equal("abc def", doc.Buffer.Text);

        // Moving the caret ends the step, even back to the same place.
        TypeKeys(h, "12");
        MoveCaret(editor, 0);
        MoveCaret(editor, doc.Buffer.Length);
        TypeKeys(h, "3");
        h.Press("Ctrl+Z");
        Assert.Equal("abc def12", doc.Buffer.Text);

        // Consecutive Backspace presses form one step; so do consecutive Delete presses.
        h.Press("Backspace");
        h.Press("Backspace");
        h.Press("Backspace");
        Assert.Equal("abc de", doc.Buffer.Text);
        h.Press("Ctrl+Z");
        Assert.Equal("abc def12", doc.Buffer.Text);
        MoveCaret(editor, 0);
        h.Press("Delete");
        h.Press("Delete");
        Assert.Equal("c def12", doc.Buffer.Text);
        h.Press("Ctrl+Z");
        Assert.Equal("abc def12", doc.Buffer.Text);

        // Typing after Backspace is a different kind of edit.
        MoveCaret(editor, doc.Buffer.Length);
        h.Press("Backspace");
        TypeKeys(h, "Z");
        h.Press("Ctrl+Z");
        Assert.Equal("abc def1", doc.Buffer.Text);

        // Saving ends the step: undo returns exactly to the saved text.
        TypeKeys(h, "k");
        await h.ExecuteAsync("file.save");
        Assert.False(doc.IsModified);
        var saved = doc.Buffer.Text; // the save may add a final line break
        MoveCaret(editor, 9);
        TypeKeys(h, "l");
        Assert.Equal("abc def1kl", doc.Buffer.Text[..10]);
        h.Press("Ctrl+Z");
        Assert.Equal(saved, doc.Buffer.Text);
        Assert.False(doc.IsModified);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-093")]
    public async Task UndoRestoresCaretAndSelection()
    {
        var lines = string.Concat(Enumerable.Range(1, 200).Select(i => $"line {i}\r\n"));
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "x abc y\r\n" + lines);
        var (doc, editor) = await OpenAsync(h);

        // A wrapped selection: undo selects it again as before, redo as after the step.
        MoveCaret(editor, 5);
        editor.TextArea.Selection = AvaloniaEdit.Editing.Selection.Create(editor.TextArea, 2, 5);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new TextSelection(2, 5), doc.Selection);
        h.Type("(");
        Assert.Equal("x (abc) y", doc.Buffer.Text[..9]);
        MoveCaret(editor, 0);
        h.Press("Ctrl+Z");
        Assert.Equal(new TextSelection(2, 5), doc.Selection);
        Assert.Equal(2, editor.SelectionStart);
        Assert.Equal(3, editor.SelectionLength);
        MoveCaret(editor, 0);
        h.Press("Ctrl+Y");
        Assert.Equal(new TextSelection(3, 6), doc.Selection);

        // A command: the caret returns to where it was before it.
        doc.Selection = TextSelection.At(1);
        Dispatcher.UIThread.RunJobs();
        await h.ExecuteAsync("edit.duplicateLine");
        Assert.Equal(new TextPosition(2, 2), doc.ViewState.Caret);
        MoveCaret(editor, doc.Buffer.GetOffset(5, 1));
        h.Press("Ctrl+Z");
        Assert.Equal(new TextPosition(1, 2), doc.ViewState.Caret);
        Assert.Null(doc.ViewState.SelectionAnchor);

        // Typed text far down: undo after scrolling to the top brings the caret back into view.
        MoveCaret(editor, doc.Buffer.GetOffset(190, 1));
        TypeKeys(h, "q");
        editor.GetVisualDescendants().OfType<ScrollViewer>().First().Offset = default; // as the wheel does
        await h.WaitForAsync(() => editor.VerticalOffset == 0, "scrolled to the top");
        h.Press("Ctrl+Z");
        Assert.Equal(new TextPosition(190, 1), doc.ViewState.Caret);
        await h.WaitForAsync(() => editor.VerticalOffset > 0, "caret scrolled into view");
    }
}
