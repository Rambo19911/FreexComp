using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
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

    /// <summary>
    /// IDE-ED-058: a click on the <c>…</c> box toggles (unfolds) the region — after the double-click time, so that a
    /// double-click instead unfolds and selects the hidden text; hovering the box shows the folded lines in
    /// <c>type.codeSmall</c>; copying a selection across a folded region copies the hidden text too.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-058")]
    [Trait("Req", "UI-ED-132")]
    public async Task FoldBoxClickHoverAndCopy()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = Unit);
        var (doc, editor, probe) = await OpenAsync(h, "A.pas");
        FoldRegion Region(int startLine) => doc.Folding.Regions.First(r => r.StartLine == startLine);
        var textView = editor.TextArea.TextView;

        Avalonia.Point Box()
        {
            Dispatcher.UIThread.RunJobs();
            var start = doc.Buffer.GetPosition(Region(6).StartOffset);
            var p = textView.GetVisualPosition(new AvaloniaEdit.TextViewPosition(start.Line, start.Column), AvaloniaEdit.Rendering.VisualYPosition.LineMiddle)
                    - textView.ScrollOffset + new Avalonia.Point(6, 0);
            return textView.TranslatePoint(p, h.Window)!.Value;
        }

        // Hover: the tooltip lists the hidden lines in type.codeSmall.
        doc.Folding.SetFolded(Region(6), true);
        h.Window.MouseMove(Box());
        Dispatcher.UIThread.RunJobs();
        var tip = Assert.IsType<Avalonia.Controls.TextBlock>(Avalonia.Controls.ToolTip.GetTip(textView));
        Assert.Equal(Application.Current!.FindResource("FxcTypeCodeSmallSize"), tip.FontSize); // type.codeSmall

        // UI-ED-132: the tooltip opens a line below the pointer (never over the hovered word) and does not take focus.
        Assert.Equal(PlacementMode.Pointer, ToolTip.GetPlacement(textView));
        Assert.True(ToolTip.GetVerticalOffset(textView) > textView.DefaultLineHeight, "below the hovered line");
        editor.TextArea.Focus();
        ToolTip.SetIsOpen(textView, true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.TextArea.IsFocused);
        ToolTip.SetIsOpen(textView, false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Application.Current!.FindResource("FxcFontMono"), tip.FontFamily);
        Assert.Contains("Writeln;", tip.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text).Aggregate("", (a, b) => a + b), StringComparison.Ordinal);

        // One click: the region unfolds once the double-click time has passed.
        var box = Box();
        h.Window.MouseDown(box, Avalonia.Input.MouseButton.Left);
        h.Window.MouseUp(box, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["6-12"], Folded(probe));
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Folded(probe));

        // A double-click (the box's BoxDoubleClicked, as NavigationUnfolds drives it) unfolds and selects the hidden
        // text and cancels the pending single click.
        doc.Folding.SetFolded(Region(6), true);
        box = Box();
        h.Window.MouseDown(box, Avalonia.Input.MouseButton.Left);
        h.Window.MouseUp(box, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var view = (CodeEditorView)probe;
        view.UnfoldAndSelect(Assert.Single(_foldings(view), s => s.Tag is FoldRegion { StartLine: 6 }));
        doc.Folding.SetFolded(Region(6), true); // folded again before the double-click time ends
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["6-12"], Folded(probe)); // the canceled single click did not unfold it
        // Copy across a folded region: the hidden text is on the clipboard.
        doc.Folding.SetFolded(Region(6), true);
        editor.TextArea.Focus();
        editor.Select(doc.Buffer.GetOffset(5, 1), doc.Buffer.GetOffset(13, 1) - doc.Buffer.GetOffset(5, 1));
        Dispatcher.UIThread.RunJobs();
        await h.ExecuteAsync("edit.copy");
        Assert.Equal(doc.Buffer.GetText(doc.Buffer.GetOffset(5, 1), doc.Buffer.GetOffset(13, 1) - doc.Buffer.GetOffset(5, 1)),
            ((TestSupport.FakeClipboard)h.Services.Clipboard).Text);
        Assert.Contains("Writeln;", ((TestSupport.FakeClipboard)h.Services.Clipboard).Text, StringComparison.Ordinal);
    }

    private static IEnumerable<FoldingSection> _foldings(CodeEditorView view) =>
        view.Editor.TextArea.TextView.ElementGenerators.OfType<FoldBoxGenerator>().Single().Inner.FoldingManager!.AllFoldings;
}
