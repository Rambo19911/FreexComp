using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>The hover tooltip and the parameter-info popup (I-116, IDE-ED-090/091, editor.md §11.2/§11.3).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class HoverTests
{
    private const string Unit =
        "unit Unit1;\r\ninterface\r\nimplementation\r\n/// Returns the greeting\r\n/// for a name.\r\nfunction Greet(const AName: string): string;\r\n" +
        "begin\r\n  Result := AName;\r\nend;\r\nvar Foo1: Integer;\r\nbegin\r\n  Foo1 := Length(Greet('x'));\r\nend.\r\n";

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-091")]
    [Trait("Req", "UI-ED-130")]
    [Trait("Req", "UI-ED-122")]
    public async Task ShowsDeclarationAndDoc()
    {
        await using var h = await BuildScenario.StartAsync(o =>
        {
            LiveDiagnosticsTests.AddRealRtl(o);
            o.InitialFiles[@"Hello\Unit1.pas"] = Unit;
        });
        var doc = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Value);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.Unit1.pas");
        Assert.True(editor.TextArea.Focus());
        var view = editor.FindAncestorOfType<CodeEditorView>()!;
        var hover = view.GetVisualDescendants().OfType<HoverView>().Single();
        var textView = editor.TextArea.TextView;
        var delay = TimeSpan.FromMilliseconds(h.Services.Settings.Get(SettingsCatalog.EditorHoverDelayMs));
        var call = Unit.IndexOf("Greet('x')", StringComparison.Ordinal);

        void PointTo(int offset)
        {
            textView.EnsureVisualLines();
            var position = doc.Buffer.GetPosition(offset);
            var at = textView.GetVisualPosition(new TextViewPosition(position.Line, position.Column), VisualYPosition.LineMiddle) - textView.ScrollOffset;
            h.Window.MouseMove(textView.TranslatePoint(at + new Point(textView.WideSpaceWidth / 2, 0), h.Window)!.Value);
            Dispatcher.UIThread.RunJobs();
        }

        async Task RestAsync(TimeSpan time)
        {
            h.Clock.Advance(time);
            await doc.Hover!.Pending;
            Dispatcher.UIThread.RunJobs();
        }

        // Resting on the call for the delay shows declaration, origin and the doc comment, below the word.
        PointTo(call + 1);
        await RestAsync(delay - TimeSpan.FromMilliseconds(1));
        Assert.False(hover.IsVisible);
        await RestAsync(TimeSpan.FromMilliseconds(1));
        Assert.True(hover.IsVisible);
        Assert.Same(hover, h.Find<HoverView>("Editor.Hover"));
        Assert.Contains("Greet(const AName: string): string", hover.Declaration, StringComparison.Ordinal);
        Assert.Equal("Unit1 · line 6", hover.Origin);
        Assert.Equal("Returns the greeting\nfor a name.", hover.Documentation?.ReplaceLineEndings("\n"));
        var word = textView.TranslatePoint(textView.GetVisualPosition(new TextViewPosition(12, 18), VisualYPosition.LineBottom) - textView.ScrollOffset, view)!.Value;
        Assert.True(hover.Margin.Top >= word.Y, "below the hovered word");
        Assert.True(editor.TextArea.IsKeyboardFocusWithin); // it never takes focus

        // Any key press closes it.
        h.Press("Right");
        Dispatcher.UIThread.RunJobs();
        Assert.False(hover.IsVisible);

        // Off: nothing after the delay.
        h.Services.Settings.Set(SettingsCatalog.EditorHoverEnabled, false);
        PointTo(Unit.IndexOf("Foo1 :=", StringComparison.Ordinal) + 1);
        await RestAsync(delay * 2);
        Assert.False(hover.IsVisible);

        // Ctrl+Shift+Space on an identifier outside an argument list: the same tooltip at the caret.
        doc.Selection = TextSelection.At(Unit.IndexOf("Foo1 :=", StringComparison.Ordinal) + 2);
        h.Press("Ctrl+Shift+Space");
        await doc.ParameterInfo!.Pending;
        await doc.Hover!.Pending;
        Dispatcher.UIThread.RunJobs();
        Assert.True(hover.IsVisible);
        Assert.Contains("Foo1: Integer", hover.Declaration, StringComparison.Ordinal);
        Assert.Equal("Unit1 · line 10", hover.Origin);
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.False(hover.IsVisible);

        // Inside an argument list it shows parameter info instead, the current parameter strong.
        var signature = view.GetVisualDescendants().OfType<SignatureHelpView>().Single();
        doc.Selection = TextSelection.At(call + "Greet(".Length);
        h.Press("Ctrl+Shift+Space");
        await doc.ParameterInfo!.Pending;
        Dispatcher.UIThread.RunJobs();
        Assert.True(signature.IsVisible);
        Assert.Same(signature, h.Find<SignatureHelpView>("Editor.SignatureHelp"));
        Assert.Contains("Greet(const AName: string)", signature.Signature, StringComparison.Ordinal);
        Assert.Equal("const AName: string", signature.CurrentParameter);
        Assert.False(hover.IsVisible);
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.False(signature.IsVisible);
    }
}
