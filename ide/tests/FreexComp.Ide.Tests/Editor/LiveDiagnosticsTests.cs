using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;
using Avalonia.Headless.XUnit;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>Live diagnostics (I-111, IDE-ED-092): squiggles and the error glyph from the project <c>Compilation</c>.</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class LiveDiagnosticsTests
{
    /// <summary>Unit1.pas of <see cref="BuildScenario"/> with the undeclared <c>Foo</c> on line 6.</summary>
    private static readonly string Unit = BuildScenario.UnitText;

    /// <summary>The real RTL sources (System.pas and its include files) under the scenario's <c>tc\rtl</c>.</summary>
    internal static void AddRealRtl(HarnessOptions o)
    {
        var src = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
        foreach (var file in Directory.GetFiles(src, "*.inc").Append(Path.Combine(src, "System.pas")).Append(Path.Combine(src, "unit-aliases.json")).Where(File.Exists))
            o.InitialFiles[@"tc\rtl\" + Path.GetFileName(file)] = File.ReadAllText(file);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-092")]
    [Trait("Req", "IDE-ED-093")]
    public async Task SquigglesAppearAfterDelayAndClear()
    {
        await using var h = await BuildScenario.StartAsync(AddRealRtl);
        var delay = TimeSpan.FromMilliseconds(h.Services.Settings.Get(SettingsCatalog.EditorLiveDiagnosticsDelayMs));
        var doc = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Value);
        Dispatcher.UIThread.RunJobs();
        IEditorTestProbe probe = h.Find<TextEditor>("Editor.Unit1.pas").FindAncestorOfType<CodeEditorView>()!;
        var foo = Unit.IndexOf("Foo :=", StringComparison.Ordinal);

        // Nothing before the delay has passed; then an error squiggle on Foo and the error glyph on its line.
        h.Clock.Advance(delay - TimeSpan.FromMilliseconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(probe.Markers);
        Assert.Null(probe.GlyphAt(6));
        await h.WaitForAsync(() => probe.Markers.Count > 0, "squiggle");
        Assert.Equal([(foo, 3, "Error")], probe.Markers);
        Assert.Equal("Error", probe.GlyphAt(6));
        var tip = Assert.Single(doc.Decorations.MarkersAt(foo + 1));
        Assert.Equal("E2003", tip.Code);
        Assert.Contains("Foo", tip.Message, StringComparison.Ordinal);
        Assert.Empty(h.Services.Messages.Items); // IDE-ED-093: never in Messages

        // An edit before the error keeps the squiggle on Foo until the next analysis.
        doc.Buffer.ApplyEdits([new TextEdit(0, 0, "\r\n")]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([(foo + 2, 3, "Error")], probe.Markers);

        // The fix: after the delay the squiggle and the glyph are gone.
        var at = doc.Buffer.Text.IndexOf("Foo :=", StringComparison.Ordinal);
        doc.Buffer.ApplyEdits([new TextEdit(at, 3, "Foo1")]);
        Dispatcher.UIThread.RunJobs();
        await h.WaitForAsync(() => probe.Markers.Count == 0, "squiggle cleared");
        Assert.Null(probe.GlyphAt(7));

        // Switched off: nothing is analysed.
        h.Services.Settings.Set(SettingsCatalog.EditorLiveDiagnostics, false);
        at = doc.Buffer.Text.IndexOf("Foo1 :=", StringComparison.Ordinal);
        doc.Buffer.ApplyEdits([new TextEdit(at, 4, "Bar")]);
        h.Clock.Advance(delay * 4);
        Dispatcher.UIThread.RunJobs();
        await h.Services.CodeModel.Worker.RunAsync("idle", _ => 0); // everything queued before has run
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(probe.Markers);
    }
}
