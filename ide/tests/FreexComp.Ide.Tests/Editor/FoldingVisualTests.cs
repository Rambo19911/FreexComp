using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit.Folding;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;
using static FreexComp.Ide.Tests.Editor.FoldingTests;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>Folding in the editor view: navigation, typing, the "…" box (I-047, UI-ED-033…036, IDE-ED-055/058).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class FoldingVisualTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-055")]
    [Trait("Req", "UI-ED-036")]
    [Trait("Req", "UI-ED-034")]
    [Trait("Req", "UI-ED-035")]
    public async Task NavigationUnfolds()
    {
        var longBody = string.Concat(Enumerable.Range(1, 25).Select(i => $"  Writeln({i});\r\n"));
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["A.pas"] = Unit;
            o.InitialFiles["L.pas"] = "program L;\r\nbegin\r\n" + longBody + "end.\r\n";
        });
        var (doc, editor, probe) = await OpenAsync(h, "A.pas");
        var view = (CodeEditorView)probe;
        FoldRegion Region(int startLine) => doc.Folding.Regions.First(r => r.StartLine == startLine);

        // Navigation that sets the caret through the document (go to line, message click) opens every fold hiding it.
        doc.Folding.SetFolded(Region(6), true);
        doc.Folding.SetFolded(Region(9), true);
        Assert.Equal(["6-12", "9-11"], Folded(probe));
        doc.ViewState.Caret = new TextPosition(10, 3);
        Assert.Empty(Folded(probe));
        Assert.Equal(doc.Buffer.GetOffset(10, 3), editor.CaretOffset);

        // A caret placed in the editor itself (find, mouse) does the same.
        doc.Folding.SetFolded(Region(13), true);
        editor.TextArea.Caret.Offset = doc.Buffer.GetOffset(15, 2);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Folded(probe));

        // Typing on the start line after the "…" box unfolds the region.
        doc.Folding.SetFolded(Region(13), true);
        Assert.Equal(["13-16"], Folded(probe));
        editor.TextArea.Caret.Offset = Region(13).EndOffset; // right after the box
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["13-16"], Folded(probe));
        h.Type("x");
        Assert.Empty(Folded(probe));
        Assert.EndsWith("end;x\r\nend.\r\n", doc.Buffer.Text, StringComparison.Ordinal);

        // A click on the chevron toggles through the document; a double-click on the box unfolds and selects the hidden text.
        var section = Assert.Single(_foldings(view), s => s.Tag is FoldRegion { StartLine: 6 });
        Assert.Equal(FoldRegion.DefaultTitle, section.Title);
        doc.Folding.SetFolded(Region(6), true);
        section = Assert.Single(_foldings(view), s => s.Tag is FoldRegion { StartLine: 6 });
        Assert.True(section.IsFolded);
        view.UnfoldAndSelect(section);
        Assert.Empty(Folded(probe));
        Assert.Equal(Region(6).StartOffset, editor.SelectionStart);
        Assert.Equal(Region(6).EndOffset - Region(6).StartOffset, editor.SelectionLength);

        // The box tooltip: the hidden lines, at most 20, then "…".
        Assert.Equal(["begin", "  if True then", "  begin", "    Writeln;", "  end;", "end;"], view.FoldTooltipLines(Region(6)));
        var (_, _, longProbe) = await OpenAsync(h, "L.pas");
        var longView = (CodeEditorView)longProbe;
        var lDoc = h.Services.Documents.ActiveDocument as Core.ViewModels.TextDocumentViewModel;
        var lines = longView.FoldTooltipLines(lDoc!.Folding.Regions.Single());
        Assert.Equal(21, lines.Count);
        Assert.Equal("  Writeln(1);", lines[0]);
        Assert.Equal("  Writeln(20);", lines[19]);
        Assert.Equal("…", lines[20]);
    }

    private static IEnumerable<FoldingSection> _foldings(CodeEditorView view) =>
        view.Editor.TextArea.TextView.ElementGenerators.OfType<FoldBoxGenerator>().Single().Inner.FoldingManager!.AllFoldings;
}
