using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Text;

/// <summary>Clipboard commands of the code editor (IDE-ED-038, I-054), through the command service and the fake clipboard.</summary>
[Trait("Phase", "1")]
public sealed class ClipboardTests
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);

    private static async Task<(IdeServices Services, TextDocumentViewModel Doc)> OpenAsync(string text, FakeClipboard clipboard, ManualClock clock)
    {
        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.Clipboard = clipboard;
            o.Clock = clock;
        });
        fs.CreateDirectory(@"C:\t");
        fs.WriteAllBytes(@"C:\t\A.pas", Encoding.UTF8.GetBytes(text));
        var opened = await services.Documents.OpenAsync(@"C:\t\A.pas");
        Assert.True(opened.Success, opened.MessageId);
        var doc = Assert.IsType<TextDocumentViewModel>(opened.Value);
        services.FocusTracker.Update(new FocusState(CommandScope.Editor, null, doc, false)); // the editor has focus
        return (services, doc);
    }

    private static void Caret(TextDocumentViewModel doc, int line, int column) => doc.ViewState.Caret = new TextPosition(line, column);

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-038")]
    [Trait("Req", "IDE-P1-076")]
    public async Task LineCopyPasteWithoutSelection()
    {
        var clipboard = new FakeClipboard();
        var clock = new ManualClock();
        var (services, doc) = await OpenAsync("one\r\ntwo\r\nthree", clipboard, clock);
        var commands = services.Commands;

        // Copy without a selection: the whole caret line with its break; the text is unchanged.
        Caret(doc, 2, 3);
        Assert.True(commands.CanExecute("edit.copy"));
        await commands.ExecuteAsync("edit.copy");
        Assert.Equal("two\r\n", clipboard.Text);
        Assert.Equal("one\r\ntwo\r\nthree", doc.Buffer.Text);

        // Paste of a copied line: a full line above the caret line, the caret keeps its column; one undo step.
        Caret(doc, 3, 2);
        await commands.ExecuteAsync("edit.paste");
        Assert.Equal("one\r\ntwo\r\ntwo\r\nthree", doc.Buffer.Text);
        Assert.Equal(new TextPosition(4, 2), doc.ViewState.Caret);
        await commands.ExecuteAsync("edit.undo");
        Assert.Equal("one\r\ntwo\r\nthree", doc.Buffer.Text);

        // The last line has no break: the copied line gets the document's.
        Caret(doc, 3, 1);
        await commands.ExecuteAsync("edit.copy");
        Assert.Equal("three\r\n", clipboard.Text);

        // Cut without a selection: the line and its break leave the document.
        Caret(doc, 1, 2);
        Assert.True(commands.CanExecute("edit.cut"));
        await commands.ExecuteAsync("edit.cut");
        Assert.Equal("one\r\n", clipboard.Text);
        Assert.Equal("two\r\nthree", doc.Buffer.Text);
        Caret(doc, 2, 3);
        await commands.ExecuteAsync("edit.paste");
        Assert.Equal("two\r\none\r\nthree", doc.Buffer.Text);
        Assert.Equal(new TextPosition(3, 3), doc.ViewState.Caret);

        // With a selection, Paste replaces it (also a copied line), Copy and Cut take the selected text only.
        doc.Selection = new TextSelection(0, 3); // "two"
        await commands.ExecuteAsync("edit.paste");
        Assert.Equal("one\r\n\r\none\r\nthree", doc.Buffer.Text);
        doc.Selection = new TextSelection(0, 2);
        await commands.ExecuteAsync("edit.cut");
        Assert.Equal("on", clipboard.Text);
        Assert.Equal("e\r\n\r\none\r\nthree", doc.Buffer.Text);

        // Text from elsewhere is pasted at the caret with the document's line breaks (IDE-P1-076).
        clipboard.Text = "x\ny";
        Caret(doc, 1, 1);
        await commands.ExecuteAsync("edit.paste");
        Assert.Equal("x\r\nye\r\n\r\none\r\nthree", doc.Buffer.Text);
        Assert.Equal(new TextPosition(2, 2), doc.ViewState.Caret);

        // A busy clipboard is retried every 50 ms; the copy lands when it frees up.
        Caret(doc, 1, 1);
        clipboard.FailNext = 2;
        var copy = commands.ExecuteAsync("edit.copy");
        Assert.False(copy.IsCompleted);
        clock.Advance(RetryInterval);
        Assert.False(copy.IsCompleted);
        clock.Advance(RetryInterval);
        await copy;
        Assert.Equal("x\r\n", clipboard.Text);
        Assert.Null(services.Status.Message);

        // Still busy after three retries: Status_ClipboardBusy, and Cut keeps the text.
        clipboard.FailNext = 4;
        var calls = clipboard.Calls;
        var cut = commands.ExecuteAsync("edit.cut");
        for (int i = 0; i < 3; i++)
        {
            Assert.False(cut.IsCompleted);
            clock.Advance(RetryInterval);
        }

        await cut;
        Assert.Equal(4, clipboard.Calls - calls);
        Assert.Equal(Strings.Status_ClipboardBusy, services.Status.Message);
        Assert.Equal("x\r\nye\r\n\r\none\r\nthree", doc.Buffer.Text);
        Assert.Equal("x\r\n", clipboard.Text);

        // Select All selects the whole text.
        await commands.ExecuteAsync("edit.selectAll");
        Assert.Equal(new TextSelection(0, doc.Buffer.Length), doc.Selection);

        // A read-only document copies but neither cuts nor pastes.
        doc.Buffer.IsReadOnly = true;
        doc.Selection = TextSelection.At(0);
        Assert.True(commands.CanExecute("edit.copy"));
        Assert.False(commands.CanExecute("edit.cut"));
        Assert.False(commands.CanExecute("edit.paste"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-038")]
    public async Task LineFormatAcrossInstances()
    {
        // Two IDE instances share the OS clipboard.
        var clipboard = new FakeClipboard();
        var (first, a) = await OpenAsync("one\r\ntwo\r\nthree", clipboard, new ManualClock());
        var (second, b) = await OpenAsync("alpha\r\nbeta", clipboard, new ManualClock());

        // A line copied in the first instance carries the line format (application/x-freexcomp-line) next to its text …
        Caret(a, 2, 1);
        await first.Commands.ExecuteAsync("edit.copy");
        Assert.Equal("two\r\n", clipboard.Text);
        Assert.Equal("two\r\n", clipboard.Formats[IClipboardService.LineFormat]);

        // … so the second instance pastes it as a full line above the caret line, keeping the caret column.
        Caret(b, 2, 3);
        await second.Commands.ExecuteAsync("edit.paste");
        Assert.Equal("alpha\r\ntwo\r\nbeta", b.Buffer.Text);
        Assert.Equal(new TextPosition(3, 3), b.ViewState.Caret);

        // The same text copied as a selection, or by another program, has no line format: pasted at the caret.
        b.Selection = new TextSelection(7, 12); // "two\r\n"
        await second.Commands.ExecuteAsync("edit.copy");
        Assert.Equal("two\r\n", clipboard.Text);
        Assert.Empty(clipboard.Formats);
        Caret(a, 1, 2);
        await first.Commands.ExecuteAsync("edit.paste");
        Assert.Equal("otwo\r\nne\r\ntwo\r\nthree", a.Buffer.Text);

        b.ViewState.SelectionAnchor = null;
        Caret(b, 3, 1);
        await second.Commands.ExecuteAsync("edit.copy"); // the line "beta" (with the document's break)
        Assert.Equal("beta\r\n", clipboard.Formats[IClipboardService.LineFormat]);
        clipboard.Text = "beta\r\n"; // another program copies the same text: its copy replaces every format
        Assert.Empty(clipboard.Formats);
        Caret(a, 1, 3);
        await first.Commands.ExecuteAsync("edit.paste");
        Assert.StartsWith("otbeta\r\nwo\r\n", a.Buffer.Text, StringComparison.Ordinal); // at the caret, not as a line above
        Assert.Equal(new TextPosition(2, 1), a.ViewState.Caret);
    }
}