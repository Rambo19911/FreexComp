using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Text;

/// <summary>Command edits are single undo steps and never join typed input (I-051, IDE-P1-091/092).</summary>
[Trait("Phase", "1")]
public sealed class UndoGroupingTests
{
    private static Task<bool> Undo(TextDocumentViewModel doc) => ((ICommandTarget)doc).ExecuteAsync("edit.undo", null);

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-092")]
    public async Task CommandEditsAreSingleSteps()
    {
        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o => o.FileSystem = fs);
        fs.CreateDirectory(@"C:\t");
        const string original = "begin\r\n  A := 1;  \r\n  B := 2;\r\nend.\r\n";
        fs.WriteAllBytes(@"C:\t\A.pas", Encoding.UTF8.GetBytes(original));
        var opened = await services.Documents.OpenAsync(@"C:\t\A.pas");
        var doc = Assert.IsType<TextDocumentViewModel>(opened.Value);

        // Each command, on a selection over several lines: one undo step restores the text before it.
        (string Command, string? Argument)[] commands =
        [
            ("edit.duplicateLine", null), ("edit.deleteLine", null), ("edit.moveLineDown", null), ("edit.toggleLineComment", null),
            ("edit.toggleBlockComment", null), ("edit.lowerCase", null), ("edit.trimTrailingWhitespace", null), ("edit.indent", null),
            ("edit.outdent", null), ("edit.convertLineEndings", "lf"),
        ];
        foreach (var (command, argument) in commands)
        {
            doc.Selection = new TextSelection(doc.Buffer.GetOffset(2, 3), doc.Buffer.GetOffset(3, 5));
            var before = doc.Buffer.Text;
            await services.Commands.ExecuteAsync(command, argument);
            Assert.True(before != doc.Buffer.Text, command);
            Assert.True(await Undo(doc), command);
            Assert.True(before == doc.Buffer.Text, command);
        }

        // Paste of several lines: one step.
        doc.Selection = TextSelection.At(0);
        Assert.True(doc.Paste("x\ny\nz"));
        await Undo(doc);
        Assert.Equal(original, doc.Buffer.Text);

        // A command never joins the typed text before or after it.
        doc.Selection = TextSelection.At(5);
        doc.TypeText("a");
        doc.TypeText("b");
        await services.Commands.ExecuteAsync("edit.duplicateLine");
        doc.TypeText("c");
        await Undo(doc);
        Assert.Equal("beginab\r\nbeginab\r\n", doc.Buffer.Text[..18]);
        await Undo(doc);
        Assert.Equal("beginab\r\n  A", doc.Buffer.Text[..12]);
        await Undo(doc);
        Assert.Equal(original, doc.Buffer.Text);
    }
}
