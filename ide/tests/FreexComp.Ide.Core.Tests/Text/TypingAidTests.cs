using System.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Text;

/// <summary>Typing aids of editor-features.md §3 (I-050): auto-close, type-over, pair delete, wrap, overwrite, column blocks.</summary>
[Trait("Phase", "1")]
public sealed class TypingAidTests
{
    private static async Task<(IdeServices Services, TextDocumentViewModel Doc)> OpenAsync(string text, Action<ISettingsService>? settings = null)
    {
        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o => o.FileSystem = fs);
        settings?.Invoke(services.Settings);
        fs.CreateDirectory(@"C:\t");
        fs.WriteAllBytes(@"C:\t\A.pas", Encoding.UTF8.GetBytes(text));
        var opened = await services.Documents.OpenAsync(@"C:\t\A.pas");
        Assert.True(opened.Success, opened.MessageId);
        return (services, Assert.IsType<TextDocumentViewModel>(opened.Value));
    }

    /// <summary>The text with <c>|</c> at the caret and <c>‹…›</c> around a selection.</summary>
    private static string Marked(TextDocumentViewModel doc)
    {
        var s = doc.Selection;
        var text = doc.Buffer.Text;
        return s.IsEmpty ? text.Insert(s.Caret, "|") : text.Insert(s.End, "›").Insert(s.Start, "‹");
    }

    private static void Caret(TextDocumentViewModel doc, int offset) => doc.Selection = TextSelection.At(offset);

    private static void Type(TextDocumentViewModel doc, string text)
    {
        foreach (var ch in text)
            doc.TypeText(ch.ToString());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-020")]
    [Trait("Req", "IDE-ED-021")]
    [Trait("Req", "IDE-ED-022")]
    [Trait("Req", "IDE-ED-023")]
    public async Task AutoCloseTypeOverPairDelete()
    {
        var (_, doc) = await OpenAsync("X := F;");
        Caret(doc, 6);

        // IDE-ED-020 + 021: ( before ';' closes; typing the closer steps over the auto-inserted one.
        Type(doc, "(");
        Assert.Equal("X := F(|);", Marked(doc));
        Type(doc, "[a");
        Assert.Equal("X := F([a|]);", Marked(doc));
        Type(doc, "])");
        Assert.Equal("X := F([a])|;", Marked(doc));

        // IDE-ED-022: Backspace between an auto-inserted pair deletes both.
        Type(doc, "(");
        Assert.Equal("X := F([a])(|);", Marked(doc));
        doc.HandleKey("Backspace");
        Assert.Equal("X := F([a])|;", Marked(doc));

        // A closer that was not auto-inserted is typed normally.
        doc.Buffer.ApplyEdits([new TextEdit(0, doc.Buffer.Length, "F()")]);
        Caret(doc, 2);
        Type(doc, ")");
        Assert.Equal("F()|)", Marked(doc));

        // Before a letter: no closer.
        doc.Buffer.ApplyEdits([new TextEdit(0, doc.Buffer.Length, "abc")]);
        Caret(doc, 0);
        Type(doc, "(");
        Assert.Equal("(|abc", Marked(doc));

        // Type-over ends when the caret leaves the pair.
        doc.Buffer.ApplyEdits([new TextEdit(0, doc.Buffer.Length, "")]);
        Type(doc, "(");
        Assert.Equal("(|)", Marked(doc));
        Caret(doc, 2);
        Caret(doc, 1);
        Type(doc, ")");
        Assert.Equal("()|)", Marked(doc));

        // Quotes: closed outside strings and comments, not after a letter, digit or quote; type-over too.
        doc.Buffer.ApplyEdits([new TextEdit(0, doc.Buffer.Length, "S := ;")]);
        Caret(doc, 5);
        Type(doc, "'ab'");
        Assert.Equal("S := 'ab'|;", Marked(doc));
        doc.Buffer.ApplyEdits([new TextEdit(0, doc.Buffer.Length, "don")]);
        Caret(doc, 3);
        Type(doc, "'");
        Assert.Equal("don'|", Marked(doc));
        doc.Buffer.ApplyEdits([new TextEdit(0, doc.Buffer.Length, "// x ")]);
        Caret(doc, 5);
        Type(doc, "'");
        Assert.Equal("// x '|", Marked(doc));
        doc.Buffer.ApplyEdits([new TextEdit(0, doc.Buffer.Length, "S := 'a  b';")]);
        Caret(doc, 8);
        Type(doc, "'");
        Assert.Equal("S := 'a '| b';", Marked(doc));
        // { starts a comment: never closed.
        doc.Buffer.ApplyEdits([new TextEdit(0, doc.Buffer.Length, "")]);
        Type(doc, "{");
        Assert.Equal("{|", Marked(doc));

        // IDE-ED-023: a selection is wrapped and stays selected.
        doc.Buffer.ApplyEdits([new TextEdit(0, doc.Buffer.Length, "x abc y")]);
        doc.Selection = new TextSelection(2, 5);
        Type(doc, "(");
        Assert.Equal("x (‹abc›) y", Marked(doc));
        Type(doc, "'");
        Assert.Equal("x ('‹abc›') y", Marked(doc));
        doc.Buffer.Undo();
        Assert.Equal("x (abc) y", doc.Buffer.Text); // one undo step per wrap
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-020")]
    public async Task AutoCloseFollowsTheSetting()
    {
        var (_, doc) = await OpenAsync("", s => s.Set(SettingsCatalog.EditorAutoCloseBrackets, false));
        Type(doc, "('");
        Assert.Equal("('|", Marked(doc));
        doc.Selection = new TextSelection(0, 2);
        Type(doc, "[");
        Assert.Equal("[|", Marked(doc));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-025")]
    public async Task OverwriteModeReplacesCharacters()
    {
        var (services, doc) = await OpenAsync("abc\r\nd");
        Caret(doc, 1);
        Assert.True(services.Commands.CanExecute("edit.toggleOverwrite"));
        await services.Commands.ExecuteAsync("edit.toggleOverwrite");
        Assert.True(doc.ViewState.Overwrite);
        Assert.True(services.Status.Editor!.Overwrite); // status field R4

        Type(doc, "XY");
        Assert.Equal("aXY|\r\nd", Marked(doc));
        Type(doc, "Z("); // end of line: inserted; no auto-close in overwrite mode
        Assert.Equal("aXYZ(|\r\nd", Marked(doc));
        Caret(doc, 0);
        Type(doc, "(");
        Assert.Equal("(|XYZ(\r\nd", Marked(doc));
        doc.Buffer.Undo();
        Assert.Equal("aXYZ(\r\nd", doc.Buffer.Text);

        // A selection is replaced as in insert mode.
        doc.Selection = new TextSelection(1, 3);
        Type(doc, "q");
        Assert.Equal("aq|Z(\r\nd", Marked(doc));

        await services.Commands.ExecuteAsync("edit.toggleOverwrite");
        Assert.False(doc.ViewState.Overwrite);
        Type(doc, "r");
        Assert.Equal("aqr|Z(\r\nd", Marked(doc));

        // Read-only documents may still toggle the mode, but typing is refused.
        doc.Buffer.IsReadOnly = true;
        Assert.True(services.Commands.CanExecute("edit.toggleOverwrite"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-026")]
    public async Task RectangularSelectionTypingCopyPaste()
    {
        var (_, doc) = await OpenAsync("abcdef\r\nab\r\nabcdef\r\n");
        // Block from line 1 column 2 to line 3 column 4 (columns 2..3 on every line).
        doc.ViewState.SelectionAnchor = new TextPosition(1, 2);
        doc.ViewState.Caret = new TextPosition(3, 4);
        doc.ViewState.RectangularSelection = true;

        // Copy: the block's lines joined by the document line ending.
        Assert.Equal("bc\r\nb\r\nbc", doc.CopySelection());

        // Typing replaces each line's part; the block stays a zero-width column after the text.
        Type(doc, "X");
        Assert.Equal("aXdef\r\naX\r\naXdef\r\n", doc.Buffer.Text);
        Assert.True(doc.ViewState.RectangularSelection);
        Assert.Equal(new TextPosition(1, 3), doc.ViewState.SelectionAnchor);
        Assert.Equal(new TextPosition(3, 3), doc.ViewState.Caret);
        doc.Buffer.Undo();
        Assert.Equal("abcdef\r\nab\r\nabcdef\r\n", doc.Buffer.Text); // one undo step

        // Paste of the copied block into an empty selection: line by line at the same column (short lines padded).
        doc.ViewState.RectangularSelection = false;
        doc.Selection = TextSelection.At(doc.Buffer.GetOffset(2, 3));
        Assert.True(doc.Paste("bc\r\nb\r\nbc"));
        Assert.Equal("abcdef\r\nabbc\r\nabbcdef\r\n  bc", doc.Buffer.Text);
        doc.Buffer.Undo();

        // Other clipboard text is pasted normally.
        doc.Selection = TextSelection.At(0);
        Assert.True(doc.Paste("p\nq"));
        Assert.Equal("p\r\nqabcdef\r\nab\r\nabcdef\r\n", doc.Buffer.Text);
        doc.Buffer.Undo();

        // Paste of the block into a rectangular selection replaces the block parts first.
        doc.ViewState.SelectionAnchor = new TextPosition(1, 5);
        doc.ViewState.Caret = new TextPosition(3, 6);
        doc.ViewState.RectangularSelection = true;
        Assert.True(doc.Paste("bc\r\nb\r\nbc"));
        Assert.Equal("abcdbcf\r\nab  b\r\nabcdbcf\r\n", doc.Buffer.Text);
        Assert.False(doc.ViewState.RectangularSelection);
    }
}
