using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Text;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>How the editor control is hosted: buffers, the test probe, our find panel, the editor font.</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class EditorHostingTests
{
    private static async Task<(HeadlessHarness H, TextDocumentViewModel Doc, TextEditor Editor)> OpenAsync()
    {
        var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "unit A;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\nend.\r\n");
        var doc = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "A.pas"))).Value);
        Dispatcher.UIThread.RunJobs();
        return (h, doc, h.Find<TextEditor>(AutomationIds.Editor("A.pas")));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-031")]
    public async Task DocumentBufferIsTheEditorsDocument()
    {
        var (h, doc, editor) = await OpenAsync();
        await using var _ = h;
        var buffer = Assert.IsType<AvaloniaEditTextBuffer>(doc.Buffer);
        Assert.Same(buffer.Document, editor.Document);
        h.Type("x");
        Assert.StartsWith("x", doc.Buffer.Text, StringComparison.Ordinal); // typing in the view edits the document's buffer
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-113")]
    public async Task CodeEditorViewIsTheTestProbe()
    {
        var (h, doc, editor) = await OpenAsync();
        await using var _ = h;
        var view = editor.FindAncestorOfType<CodeEditorView>();
        IEditorTestProbe probe = Assert.IsAssignableFrom<IEditorTestProbe>(view);
        Assert.Equal("syntax.keyword", probe.TokenClassAt(doc.Buffer.Text.IndexOf("unit", StringComparison.Ordinal)));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-102")]
    public async Task FindUsesOwnPanelNotTheBuiltInSearchPanel()
    {
        var (h, _, editor) = await OpenAsync();
        await using var __ = h;
        Assert.True(editor.TextArea.Focus());
        h.Press("Ctrl+F");
        var view = editor.FindAncestorOfType<CodeEditorView>()!;
        Assert.Single(view.GetVisualDescendants().OfType<FindPanelView>(), p => p.IsEffectivelyVisible);
        Assert.DoesNotContain(view.GetVisualDescendants(), v => v is AvaloniaEdit.Search.SearchPanel);
        Assert.False(editor.GetVisualDescendants().OfType<FindPanelView>().Any()); // an overlay of the view, not inside the text area
        h.Press("Escape");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TOK-021")]
    public async Task EditorUsesMonoFontTokenUnlessOverridden()
    {
        var (h, _, editor) = await OpenAsync();
        await using var _ = h;
        Assert.True(Avalonia.Application.Current!.TryGetResource("FxcFontMono", null, out var mono));
        Assert.Equal(["Cascadia Mono", "Consolas", "Courier New", "monospace"], ((FontFamily)mono!).FamilyNames);
        Assert.Equal(mono, editor.FontFamily);

        h.Services.Settings.Set(SettingsCatalog.EditorFontFamily, "Consolas");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Consolas", editor.FontFamily.ToString());
    }

    /// <summary>
    /// UI-TOK-021 "Ligatures are off": every text run the editor shapes switches the ligature features off (<c>liga</c>,
    /// <c>clig</c>, <c>calt</c> = 0), also with a user font and with syntax colours applied. Headless tests cannot see
    /// glyphs (IDE-TEST-010), so the shaping input is asserted.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TOK-021")]
    public async Task LigaturesOff()
    {
        var (h, doc, editor) = await OpenAsync();
        await using var _ = h;
        editor.TextArea.Focus();
        h.Type("x := a <= b; // => != ->\r\n");
        h.Services.Settings.Set(SettingsCatalog.EditorFontFamily, "Fira Code");
        Dispatcher.UIThread.RunJobs();

        var textView = editor.TextArea.TextView;
        var runs = Enumerable.Range(1, doc.Buffer.LineCount)
            .Select(n => textView.GetOrConstructVisualLine(textView.Document.GetLineByNumber(n)))
            .SelectMany(l => l.TextLines).SelectMany(t => t.TextRuns)
            .OfType<Avalonia.Media.TextFormatting.ShapedTextRun>()
            .ToList();
        Assert.NotEmpty(runs);
        Assert.All(runs, run =>
        {
            var features = run.Properties.FontFeatures;
            Assert.NotNull(features);
            foreach (var tag in new[] { "liga", "clig", "calt" })
                Assert.Contains(features, f => f.Tag == tag && f.Value == 0);
        });
    }
}
