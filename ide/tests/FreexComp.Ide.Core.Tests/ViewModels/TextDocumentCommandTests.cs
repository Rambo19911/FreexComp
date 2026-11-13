using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Tests.ViewModels;

/// <summary>The document's own command target (I-043): undo/redo through <see cref="ITextBuffer"/>, user insertions.</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Unit")]
public sealed class TextDocumentCommandTests
{
    private static TextDocumentViewModel Create(string text) =>
        new(new StringTextBuffer(text), @"C:\work\A.pas", null, new PlainTextLanguage(), FileEncoding.Utf8, LineEndingKind.CrLf);

    [Fact]
    [Trait("Req", "IDE-ARCH-030")]
    public async Task UndoRedoUseTheBufferAndFollowItsState()
    {
        var doc = Create("abc");
        var target = doc.CommandTarget!;
        Assert.False(target.CanExecute("edit.undo", null));
        Assert.False(target.CanExecute("edit.redo", null));

        Assert.True(doc.TryEdit([new TextEdit(0, 0, "x")]));
        Assert.True(target.CanExecute("edit.undo", null));
        Assert.True(await target.ExecuteAsync("edit.undo", null));
        Assert.Equal("abc", doc.Buffer.Text);
        Assert.False(doc.IsModified);
        Assert.True(await target.ExecuteAsync("edit.redo", null));
        Assert.Equal("xabc", doc.Buffer.Text);

        doc.Buffer.IsReadOnly = true;
        Assert.False(target.CanExecute("edit.undo", null));
        Assert.False(await target.ExecuteAsync("edit.undo", null));
        Assert.False(target.CanExecute("edit.convertLineEndings", "lf")); // still delegated: disabled while read-only
    }

    [Fact]
    [Trait("Req", "IDE-P1-076")]
    [Trait("Req", "IDE-P1-040")]
    public void InsertTextNormalizesBreaksAndRespectsReadOnly()
    {
        var doc = Create("a\r\nb\r\nc\n");
        var refused = 0;
        doc.ReadOnlyEditRefused += (_, _) => refused++;

        Assert.True(doc.TryInsertText(doc.Buffer.Length - 1, 0, "\n"));
        Assert.Equal("a\r\nb\r\nc\r\n\n", doc.Buffer.Text);

        doc.Buffer.IsReadOnly = true;
        Assert.False(doc.TryInsertText(0, 0, "x"));
        Assert.Equal(1, refused);
    }
}
