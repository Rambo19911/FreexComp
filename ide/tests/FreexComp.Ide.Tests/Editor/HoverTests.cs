using Avalonia;
using Avalonia.Controls;
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

    /// <summary>
    /// editor.md §11.2: UI-ED-120 the popup (<c>Editor.SignatureHelp</c>, our tokens); UI-ED-121 above the caret line, left edge
    /// at the opening parenthesis, below the line without room above, max width 600, padding 6 × 4; UI-ED-123 it follows the
    /// caret between arguments and closes when the caret leaves the argument list or the editor loses focus.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-120")]
    [Trait("Req", "UI-ED-121")]
    [Trait("Req", "UI-ED-123")]
    public async Task SignatureHelpPlacementAndTracking()
    {
        var text = "unit Unit1;\r\ninterface\r\nimplementation\r\nfunction Join(const A: string; B: Integer): string;\r\n" +
            "begin\r\n  Result := A;\r\nend;\r\nvar S: string;\r\nbegin\r\n  S := Join('x', 2);\r\n" +
            string.Concat(Enumerable.Repeat("\r\n", 120)) + "end.\r\n";
        await using var h = await BuildScenario.StartAsync(o =>
        {
            LiveDiagnosticsTests.AddRealRtl(o);
            o.InitialFiles[@"Hello\Unit1.pas"] = text;
        });
        var doc = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Value);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.Unit1.pas");
        Assert.True(editor.TextArea.Focus());
        var view = editor.FindAncestorOfType<CodeEditorView>()!;
        var textView = editor.TextArea.TextView;
        var signature = h.Find<SignatureHelpView>("Editor.SignatureHelp");
        var paren = text.IndexOf("Join('x'", StringComparison.Ordinal) + "Join".Length;
        var line = doc.Buffer.GetPosition(paren).Line;

        async Task OpenAtAsync(int offset)
        {
            doc.Selection = TextSelection.At(offset);
            Dispatcher.UIThread.RunJobs();
            h.Press("Ctrl+Shift+Space");
            await doc.ParameterInfo!.Pending;
            Dispatcher.UIThread.RunJobs();
            Assert.True(signature.IsVisible);
        }

        Point InView(VisualYPosition y)
        {
            textView.EnsureVisualLines();
            var column = doc.Buffer.GetPosition(paren).Column;
            var at = textView.GetVisualPosition(new TextViewPosition(line, column), y) - textView.ScrollOffset;
            return textView.TranslatePoint(at, view)!.Value;
        }

        // UI-ED-120/121: our popup with the tokens, above the caret line, its left edge at the opening parenthesis.
        await OpenAtAsync(paren + 1);
        Assert.Equal("const A: string", signature.CurrentParameter);
        Assert.Equal(600, signature.MaxWidth);
        Assert.Equal(new Thickness(6, 4), signature.Padding);
        Assert.True(signature.TryFindResource("FxcBgRaisedBrush", signature.ActualThemeVariant, out var raised));
        Assert.Same(raised, signature.Background);
        Assert.Equal(InView(VisualYPosition.LineTop).X, signature.Margin.Left, 0.5);
        Assert.True(signature.Margin.Top + signature.Bounds.Height <= InView(VisualYPosition.LineTop).Y + 0.5, "above the caret line");

        // UI-ED-123: the current parameter follows the caret to the next argument; leaving the list closes the popup.
        doc.Selection = TextSelection.At(text.IndexOf(" 2);", StringComparison.Ordinal) + 1);
        Dispatcher.UIThread.RunJobs();
        await doc.ParameterInfo!.Pending;
        Dispatcher.UIThread.RunJobs();
        Assert.True(signature.IsVisible);
        Assert.Equal("B: Integer", signature.CurrentParameter);
        Assert.True(signature.Margin.Top + signature.Bounds.Height <= InView(VisualYPosition.LineTop).Y + 0.5, "still above after the update");
        doc.Selection = TextSelection.At(text.IndexOf(");", paren, StringComparison.Ordinal) + 2);
        Dispatcher.UIThread.RunJobs();
        await doc.ParameterInfo!.Pending;
        Dispatcher.UIThread.RunJobs();
        Assert.False(signature.IsVisible);

        // Focus loss closes it.
        await OpenAtAsync(paren + 1);
        h.Services.ToolWindows.Show("ProjectManager", focus: true);
        Dispatcher.UIThread.RunJobs();
        Assert.False(editor.TextArea.IsKeyboardFocusWithin);
        Assert.False(signature.IsVisible);

        // No room above (the call line is the first visible line): below the line.
        Assert.True(editor.TextArea.Focus());
        var scroller = editor.GetVisualDescendants().OfType<ScrollViewer>().First(); // as the view restores offsets
        scroller.Offset = new Vector(0, textView.GetVisualTopByDocumentLine(line));
        Dispatcher.UIThread.RunJobs();
        await OpenAtAsync(paren + 1);
        Assert.True(InView(VisualYPosition.LineTop).Y < signature.Bounds.Height, "the line is at the top");
        Assert.True(signature.Margin.Top >= InView(VisualYPosition.LineBottom).Y - 0.5, "below the caret line");
        Assert.Equal(InView(VisualYPosition.LineTop).X, signature.Margin.Left, 0.5);
    }
}
