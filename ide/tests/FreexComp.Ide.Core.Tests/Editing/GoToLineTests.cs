using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>Go to Line without the dialog (I-059, phase1-shell.md §10 last paragraph, dialogs.md §12).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Unit")]
public sealed class GoToLineTests
{
    private const string Code =
        "program P;\r\n" +
        "\r\n" +
        "procedure Q;\r\n" +
        "begin\r\n" +
        "    WriteLn;\r\n" +
        "end;\r\n" +
        "\r\n" +
        "begin\r\n" +
        "end.\r\n";

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-120")]
    [Trait("Req", "IDE-P1-100")]
    public Task ClampsAndUnfolds() => TestUiThread.RunAsync(() => ClampsAndUnfoldsOnUiThread()); // I-209

    private async Task ClampsAndUnfoldsOnUiThread()
    {
        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o => o.FileSystem = fs);
        fs.CreateDirectory(@"C:\t");
        fs.WriteAllBytes(@"C:\t\A.pas", Encoding.UTF8.GetBytes(Code));
        var doc = Assert.IsType<TextDocumentViewModel>((await services.Documents.OpenAsync(@"C:\t\A.pas")).Value);
        services.FocusTracker.Update(new FocusState(CommandScope.Editor, null, doc, false));
        Assert.True(services.Commands.CanExecute("search.goToLine"));
        var navigation = services.Navigation;
        var moves = new List<NavigatedEventArgs>();
        navigation.Navigated += (_, e) => moves.Add(e);

        // Fold the routine, select something: going to a hidden line opens the fold, clears the selection and puts
        // the caret on the first non-blank character when no column is given.
        var routine = doc.Folding.Regions.Single(r => r.StartLine == 3);
        doc.Folding.SetFolded(routine, true);
        Assert.True(doc.Folding.Regions.Single(r => r.StartLine == 3).IsFolded);
        doc.Selection = new TextSelection(0, 7);
        navigation.GoToLine(doc, 5, null);
        Assert.Equal((new TextPosition(5, 5), (TextPosition?)null), (doc.ViewState.Caret, doc.ViewState.SelectionAnchor));
        Assert.False(doc.Folding.Regions.Single(r => r.StartLine == 3).IsFolded);
        Assert.Equal([(doc, new TextPosition(1, 8))], moves.Select(m => (m.Document, m.From)));

        // Columns: below 1 → 1; beyond the line → the line end; lines outside the document are clamped.
        navigation.GoToLine(doc, 5, 0);
        Assert.Equal(new TextPosition(5, 1), doc.ViewState.Caret);
        navigation.GoToLine(doc, 5, 99);
        Assert.Equal(new TextPosition(5, 13), doc.ViewState.Caret);
        navigation.GoToLine(doc, 3, 4);
        Assert.Equal(new TextPosition(3, 4), doc.ViewState.Caret);
        navigation.GoToLine(doc, 0, null);
        Assert.Equal(new TextPosition(1, 1), doc.ViewState.Caret);
        navigation.GoToLine(doc, 99, null);
        Assert.Equal(new TextPosition(10, 1), doc.ViewState.Caret);
        navigation.GoToLine(doc, 2, null); // an empty line: column 1
        Assert.Equal(new TextPosition(2, 1), doc.ViewState.Caret);
        Assert.Equal(7, moves.Count);

        // The dialog's input: "L", "L:C" or "L,C", decimal, surrounding spaces ignored; L must be 1…n.
        Assert.Equal((true, 12, (int?)null), Parse("12", 20));
        Assert.Equal((true, 3, (int?)4), Parse(" 3:4 ", 20));
        Assert.Equal((true, 3, (int?)4), Parse("3,4", 20));
        Assert.Equal((true, 3, (int?)0), Parse("3:0", 20)); // C < 1 is treated as 1 when going
        Assert.Equal((true, 20, (int?)null), Parse("20", 20));
        foreach (var bad in new[] { "", " ", "0", "21", "-1", "+3", "abc", "3:", ":4", "3:x", "3:4:5", "3 :4", "1.5", "99999999999" })
            Assert.False(GoToLineInput.TryParse(bad, 20, out _, out _), bad);
        Assert.Equal("Enter a line number from 1 to 20.", GoToLineInput.OutOfRangeMessage(20));
    }

    private static (bool, int, int?) Parse(string text, int lineCount) =>
        (GoToLineInput.TryParse(text, lineCount, out var line, out var column), line, column);
}
