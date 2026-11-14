using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>Fold commands through the real editor (I-047, IDE-ED-054).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class FoldingTests
{
    internal const string Unit = "unit A;\r\n"          // 1
        + "interface\r\n"                                // 2
        + "implementation\r\n"                           // 3
        + "{ notes\r\n"                                  // 4
        + "  more }\r\n"                                 // 5
        + "procedure P;\r\n"                             // 6
        + "begin\r\n"                                    // 7
        + "  if True then\r\n"                           // 8
        + "  begin\r\n"                                  // 9
        + "    Writeln;\r\n"                             // 10
        + "  end;\r\n"                                   // 11
        + "end;\r\n"                                     // 12
        + "procedure Q;\r\n"                             // 13
        + "begin\r\n"                                    // 14
        + "  Writeln;\r\n"                               // 15
        + "end;\r\n"                                     // 16
        + "end.\r\n";                                    // 17

    internal static async Task<(TextDocumentViewModel Document, TextEditor Editor, IEditorTestProbe Probe)> OpenAsync(HeadlessHarness h, string fileName)
    {
        var result = await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, fileName));
        Assert.True(result.Success, result.MessageId);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor." + fileName);
        Assert.True(editor.TextArea.Focus());
        Dispatcher.UIThread.RunJobs();
        var view = editor.FindAncestorOfType<CodeEditorView>();
        Assert.NotNull(view);
        return (Assert.IsType<TextDocumentViewModel>(result.Value), editor, view);
    }

    internal static List<string> Folded(IEditorTestProbe probe) =>
        [.. probe.Foldings.Where(f => f.IsFolded).Select(f => $"{f.StartLine}-{f.EndLine}")];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-054")]
    [Trait("Req", "UI-ED-030")]
    public async Task FoldUnfoldCommands()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = Unit;
            o.InitialFiles["B.pas"] = "unit B;\r\n";
        });
        var (doc, editor, probe) = await OpenAsync(h, "A.pas");

        // The editor's foldings are the document's regions (UI-ED-030).
        Assert.Equal(["3-16", "4-5", "6-12", "9-11", "13-16"], probe.Foldings.Select(f => $"{f.StartLine}-{f.EndLine}"));
        Assert.Empty(Folded(probe));

        // edit.fold: the innermost region containing the caret; a caret it would hide moves to the end of the start line.
        editor.TextArea.Caret.Offset = doc.Buffer.GetOffset(10, 5);
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Services.Commands.CanExecute("edit.fold"));
        await h.ExecuteAsync("edit.fold");
        Assert.Equal(["9-11"], Folded(probe));
        Assert.Equal(new TextPosition(9, 8), doc.ViewState.Caret);
        Assert.Equal(doc.Buffer.GetOffset(9, 8), editor.CaretOffset);
        await h.ExecuteAsync("edit.fold");
        Assert.Equal(["6-12", "9-11"], Folded(probe));
        Assert.Equal(new TextPosition(6, 13), doc.ViewState.Caret);
        Assert.Equal([6, 9], doc.ViewState.FoldedLines);

        // edit.unfold: the innermost folded region on the caret line.
        await h.ExecuteAsync("edit.unfold");
        Assert.Equal(["9-11"], Folded(probe));
        Assert.False(h.Services.Commands.CanExecute("edit.unfold"));

        // edit.foldAll: routines, types, regions and top-level multi-line comments; not sections or blocks.
        await h.ExecuteAsync("edit.foldAll");
        Assert.Equal(["4-5", "6-12", "9-11", "13-16"], Folded(probe));
        Assert.False(h.Services.Commands.CanExecute("edit.foldAll"));

        // The fold state belongs to the document: a new view after a tab switch shows it again.
        await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "B.pas"));
        Dispatcher.UIThread.RunJobs();
        h.Services.Documents.Activate(doc);
        Dispatcher.UIThread.RunJobs();
        editor = h.Find<TextEditor>("Editor.A.pas");
        probe = editor.FindAncestorOfType<CodeEditorView>()!;
        Assert.Equal(["4-5", "6-12", "9-11", "13-16"], Folded(probe));
        Assert.True(editor.TextArea.Focus());
        Dispatcher.UIThread.RunJobs();

        // edit.unfoldAll.
        await h.ExecuteAsync("edit.unfoldAll");
        Assert.Empty(Folded(probe));
        Assert.Empty(doc.ViewState.FoldedLines);
        Assert.False(h.Services.Commands.CanExecute("edit.unfoldAll"));
    }
}
