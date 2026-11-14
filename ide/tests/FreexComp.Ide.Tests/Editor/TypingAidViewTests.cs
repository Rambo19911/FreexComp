using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>The typing aids in the code editor view (I-050): auto-close keys, overwrite caret, column blocks.</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class TypingAidViewTests
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

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-020")]
    [Trait("Req", "IDE-ED-022")]
    [Trait("Req", "IDE-ED-025")]
    public async Task TypedKeysUseTheTypingAids()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "F;\r\n");
        var (doc, editor) = await OpenAsync(h);
        editor.TextArea.Caret.Offset = 1;
        Dispatcher.UIThread.RunJobs();

        h.Type("(");
        Assert.Equal("F();\r\n", doc.Buffer.Text);
        Assert.Equal(2, editor.CaretOffset);
        h.Press("Backspace");
        Assert.Equal("F;\r\n", doc.Buffer.Text);

        // Insert toggles overwrite mode: block caret, status R4, typed text replaces.
        h.Press("Insert");
        Assert.True(doc.ViewState.Overwrite);
        Assert.True(editor.TextArea.OverstrikeMode);
        Assert.Equal("OVR", h.Status.Field("InsertMode"));
        h.Type("X");
        Assert.Equal("FX\r\n", doc.Buffer.Text);
        h.Press("Insert");
        Assert.False(editor.TextArea.OverstrikeMode);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-026")]
    public async Task ColumnBlockTypingAndCopy()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "abcd\r\nabcd\r\n");
        var (doc, editor) = await OpenAsync(h);
        Assert.True(editor.Options.EnableRectangularSelection);

        // An Alt+drag block is mirrored into the document's view state.
        var area = editor.TextArea;
        area.Selection = new RectangleSelection(area, new TextViewPosition(1, 2), new TextViewPosition(2, 4));
        area.Caret.Position = new TextViewPosition(2, 4);
        Dispatcher.UIThread.RunJobs();
        Assert.True(doc.ViewState.RectangularSelection);
        Assert.Equal(new TextPosition(1, 2), doc.ViewState.SelectionAnchor);

        await h.ExecuteAsync("edit.copy");
        Assert.Equal("bc\r\nbc", await TopLevel.GetTopLevel(h.Window)!.Clipboard!.TryGetTextAsync());

        h.Type("Z");
        Assert.Equal("aZd\r\naZd\r\n", doc.Buffer.Text);
        Assert.IsType<RectangleSelection>(area.Selection);
    }
}
