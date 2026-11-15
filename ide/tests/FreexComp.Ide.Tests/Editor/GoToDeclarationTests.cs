using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>Ctrl+click Go to Declaration (I-196, IDE-ED-101) in the editor view.</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class GoToDeclarationTests
{
    private const string Unit = "unit Unit1;\r\ninterface\r\nimplementation\r\nvar Foo1: Integer;\r\nbegin\r\n  Foo1 := 1;\r\nend.\r\n";

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-101")]
    public async Task CtrlClickJumps()
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
        var textView = editor.TextArea.TextView;

        Point At(int line, int column)
        {
            textView.EnsureVisualLines();
            var p = textView.GetVisualPosition(new TextViewPosition(line, column), VisualYPosition.LineMiddle) - textView.ScrollOffset;
            return textView.TranslatePoint(p + new Point(textView.WideSpaceWidth / 2, 0), h.Window)!.Value;
        }

        // A plain click only moves the caret.
        h.Window.MouseDown(At(6, 4), MouseButton.Left);
        h.Window.MouseUp(At(6, 4), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new TextPosition(6, 4), doc.ViewState.Caret);

        // Ctrl+click on the use of Foo1 (line 6) jumps to its declaration (line 4, column 5).
        h.Window.MouseDown(At(6, 4), MouseButton.Left, RawInputModifiers.Control);
        h.Window.MouseUp(At(6, 4), MouseButton.Left, RawInputModifiers.Control);
        await h.WaitForAsync(() => doc.ViewState.Caret == new TextPosition(4, 5), "jump to the declaration");
        Assert.Equal(new TextPosition(4, 5), doc.ViewState.Caret);
        Assert.Null(doc.ViewState.SelectionAnchor); // no word selection from the click
    }
}
