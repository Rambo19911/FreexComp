using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Text;

[Trait("Phase", "1")]
public sealed class LineEndingTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "files", "eol", name));

    private static LineEndingModel Model(string text, LineEndingKind whenNone = LineEndingKind.CrLf) =>
        new(new StringTextBuffer(text, whenNone), whenNone);

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-075")]
    [Trait("Req", "IDE-P1-078")]
    public void DetectsCrlfLfMixed()
    {
        Assert.Equal(new LineEndingCounts(7, 0), LineEndingDetector.Count(Fixture("crlf.pas")));
        Assert.Equal(new LineEndingCounts(0, 7), LineEndingDetector.Count(Fixture("lf.pas")));
        Assert.Equal(new LineEndingCounts(3, 2), LineEndingDetector.Count(Fixture("mixed.pas")));
        Assert.Equal(new LineEndingCounts(0, 3), LineEndingDetector.Count(Fixture("nofinal.pas")));
        Assert.Equal(new LineEndingCounts(0, 5), LineEndingDetector.Count(Fixture("lonecr.pas")));   // a lone CR is not counted

        Assert.Equal(LineEndingKind.CrLf, Model(Fixture("crlf.pas"), LineEndingKind.Lf).Kind);
        Assert.Equal(LineEndingKind.Lf, Model(Fixture("lf.pas")).Kind);
        Assert.Equal(LineEndingKind.Lf, Model(Fixture("nofinal.pas")).Kind);
        Assert.Equal(LineEndingKind.Lf, Model(Fixture("lonecr.pas")).Kind);
        var mixed = Model(Fixture("mixed.pas"));
        Assert.Equal(LineEndingKind.Mixed, mixed.Kind);
        Assert.Equal(LineEndingKind.CrLf, mixed.NewLineKind);                 // dominant kind

        // No line break: files.defaultLineEnding decides.
        Assert.Equal(LineEndingKind.CrLf, Model("x", LineEndingKind.CrLf).Kind);
        Assert.Equal(LineEndingKind.Lf, Model("x", LineEndingKind.Lf).Kind);
        Assert.Equal(LineEndingKind.Lf, Model("", LineEndingKind.Lf).Kind);

        // The buffer keeps every line's own ending, the lone CR included (IDE-P1-078).
        var text = Fixture("mixed.pas");
        var buffer = new StringTextBuffer(text);
        _ = new LineEndingModel(buffer, LineEndingKind.CrLf);
        Assert.Equal(text, buffer.Text);

        // The kind follows edits; KindChanged fires only when it changes.
        var model = Model("a\r\nb");
        int raised = 0;
        model.KindChanged += (_, _) => raised++;
        model.Buffer.ApplyEdits([new TextEdit(4, 0, "\nc")]);
        Assert.Equal((LineEndingKind.Mixed, 1), (model.Kind, raised));
        model.Buffer.ApplyEdits([new TextEdit(0, 0, "z")]);
        Assert.Equal((LineEndingKind.Mixed, 1), (model.Kind, raised));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-077")]
    public Task ConvertIsOneUndoStep() => TestUiThread.RunAsync(() => ConvertIsOneUndoStepOnUiThread()); // I-209

    private async Task ConvertIsOneUndoStepOnUiThread()
    {
        var text = Fixture("mixed.pas");
        var model = Model(text);
        var buffer = model.Buffer;
        buffer.MarkSavePoint();
        int raised = 0;
        model.KindChanged += (_, _) => raised++;

        Assert.True(model.CanExecute(LineEndingModel.ConvertCommandId, "lf"));
        Assert.True(await model.ExecuteAsync(LineEndingModel.ConvertCommandId, "lf"));
        Assert.Equal(text.Replace("\r\n", "\n"), buffer.Text);
        Assert.Equal(LineEndingKind.Lf, model.Kind);
        Assert.Equal(1, raised);                       // the document service removes Msg.File.MixedLineEndings on this
        Assert.False(buffer.IsAtSavePoint);            // modified

        buffer.Undo();                                 // one step back to the original text
        Assert.Equal(text, buffer.Text);
        Assert.Equal(LineEndingKind.Mixed, model.Kind);
        Assert.False(buffer.CanUndo);
        Assert.True(buffer.IsAtSavePoint);

        Assert.True(await model.ExecuteAsync(LineEndingModel.ConvertCommandId, "crlf"));
        Assert.Equal(text.Replace("\r\n", "\n").Replace("\n", "\r\n"), buffer.Text);
        Assert.Equal(LineEndingKind.CrLf, model.Kind);

        // A lone CR is text, not a counted line break: conversion keeps it.
        var lonecr = Model(Fixture("lonecr.pas"));
        lonecr.Convert(LineEndingKind.CrLf);
        Assert.Equal("unit A;\r\n// a\rb\r\ninterface\r\nimplementation\r\nend.\r\n", lonecr.Buffer.Text);

        // Without line breaks only the kind changes (no buffer edit, no undo step).
        var single = Model("x", LineEndingKind.CrLf);
        single.Convert(LineEndingKind.Lf);
        Assert.Equal((LineEndingKind.Lf, false), (single.Kind, single.Buffer.CanUndo));

        // Argument and state checks.
        Assert.False(model.CanExecute(LineEndingModel.ConvertCommandId, "mixed"));
        Assert.False(model.CanExecute(LineEndingModel.ConvertCommandId, null));
        Assert.False(model.CanExecute("edit.copy", "lf"));
        Assert.False(await model.ExecuteAsync("edit.copy", "lf"));
        buffer.IsReadOnly = true;
        Assert.False(model.CanExecute(LineEndingModel.ConvertCommandId, "lf"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-076")]
    public void TypedAndPastedBreaksUseDocumentEnding()
    {
        Assert.Equal("\r\n", Model("a\r\nb").NewLine);
        Assert.Equal("\n", Model("a\nb").NewLine);
        Assert.Equal("\r\n", Model("a\r\nb\r\nc\nd").NewLine);    // Mixed: the dominant kind
        Assert.Equal("\n", Model("a\nb\nc\r\nd").NewLine);
        Assert.Equal("\r\n", Model("a\r\nb\nc").NewLine);         // equal counts: CRLF
        Assert.Equal("\n", Model("x", LineEndingKind.Lf).NewLine);

        // Typed text: a break of any kind becomes the document's ending.
        var lf = Model("a\nb");
        lf.Insert(1, "\r\n");
        Assert.Equal("a\n\nb", lf.Buffer.Text);

        // Pasted text is normalized (CRLF and LF; a lone CR stays text) and inserted as one undo step.
        var crlf = Model("a\r\nb");
        crlf.Insert(1, "x\ny\r\nz\rw");
        Assert.Equal("ax\r\ny\r\nz\rw\r\nb", crlf.Buffer.Text);
        crlf.Buffer.Undo();
        Assert.Equal("a\r\nb", crlf.Buffer.Text);
        Assert.Equal("p\r\nq\r\n", crlf.Normalize("p\nq\r\n"));

        // Inserting never turns a document mixed.
        Assert.Equal(LineEndingKind.CrLf, crlf.Kind);
    }
}
