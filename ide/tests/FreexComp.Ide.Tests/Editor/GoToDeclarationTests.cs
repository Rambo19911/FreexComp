using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>Ctrl+click Go to Declaration (I-196, IDE-ED-101; I-321, IDE-ED-147) in the editor view.</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class GoToDeclarationTests
{
    private const string Unit = "unit Unit1;\r\ninterface\r\nimplementation\r\nvar Foo1: Integer;\r\nbegin\r\n  Foo1 := 1;\r\nend.\r\n";

    /// <summary>The window point of the middle of the character at <paramref name="line"/>/<paramref name="column"/> of <paramref name="editor"/>.</summary>
    internal static Point At(HeadlessHarness h, TextEditor editor, int line, int column)
    {
        var textView = editor.TextArea.TextView;
        textView.EnsureVisualLines();
        var p = textView.GetVisualPosition(new TextViewPosition(line, column), VisualYPosition.LineMiddle) - textView.ScrollOffset;
        return textView.TranslatePoint(p + new Point(textView.WideSpaceWidth / 2, 0), h.Window)!.Value;
    }

    private static void CtrlClick(HeadlessHarness h, TextEditor editor, int line, int column)
    {
        Assert.True(editor.TextArea.Focus());
        h.Window.MouseDown(At(h, editor, line, column), MouseButton.Left, RawInputModifiers.Control);
        h.Window.MouseUp(At(h, editor, line, column), MouseButton.Left, RawInputModifiers.Control);
    }

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

        // A plain click only moves the caret.
        h.Window.MouseDown(At(h, editor, 6, 4), MouseButton.Left);
        h.Window.MouseUp(At(h, editor, 6, 4), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new TextPosition(6, 4), doc.ViewState.Caret);

        // Ctrl+click on the use of Foo1 (line 6) jumps to its declaration (line 4, column 5).
        CtrlClick(h, editor, 6, 4);
        await h.WaitForAsync(() => doc.ViewState.Caret == new TextPosition(4, 5), "jump to the declaration");
        Assert.Equal(new TextPosition(4, 5), doc.ViewState.Caret);
        Assert.Null(doc.ViewState.SelectionAnchor); // no word selection from the click
    }

    /// <summary>IDE-ED-147: Ctrl+click on a <c>uses</c> entry of an RTL unit opens <c>rtl/src/SysUtils.pas</c> protected at line 1.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-147")]
    public async Task CtrlClickUsesEntryOpensRtlUnit()
    {
        var rtl = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
        await using var h = await BuildScenario.StartAsync(o =>
        {
            o.Settings["toolchain.rtlDir"] = rtl; // the real RTL sources: opened protected, never written
            o.InitialFiles[@"Hello\Hello.pas"] = "program Hello;\r\nuses SysUtils, Unit1;\r\nbegin\r\nend.\r\n";
        });
        var main = Path.Combine(h.TempDir, @"Hello\Hello.pas");
        Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(main)).Value);
        Dispatcher.UIThread.RunJobs();

        CtrlClick(h, h.Find<TextEditor>("Editor.Hello.pas"), 2, 8); // inside "SysUtils"
        var target = Path.GetFullPath(Path.Combine(rtl, "SysUtils.pas"));
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument is TextDocumentViewModel { FilePath: { } p }
            && string.Equals(Path.GetFullPath(p), target, StringComparison.OrdinalIgnoreCase), "SysUtils.pas opened");
        var library = Assert.IsType<TextDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Assert.True(library.IsLibraryProtected);
        Assert.True(library.IsReadOnly);
        Assert.Equal(new TextPosition(1, 1), library.ViewState.Caret);
    }

    /// <summary>IDE-ED-147: Ctrl+click on a form component used in a form method goes to its field in the form class.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-147")]
    public async Task CtrlClickFormComponentGoesToField()
    {
        const string formUnit = "unit Unit1;\r\ninterface\r\nuses Classes, Controls, Forms, StdCtrls;\r\ntype\r\n  TForm1 = class(TForm)\r\n"
            + "    Button1: TButton;\r\n    procedure FormCreate(Sender: TObject);\r\n  end;\r\nvar\r\n  Form1: TForm1;\r\nimplementation\r\n"
            + "{$R *.form}\r\nprocedure TForm1.FormCreate(Sender: TObject);\r\nbegin\r\n  Button1.Caption := 'Go';\r\nend;\r\nend.\r\n";
        const string form = "object Form1: TForm1\r\n  Caption = 'Form1'\r\n  object Button1: TButton\r\n    Caption = 'Go'\r\n  end\r\nend\r\n";
        await using var h = await BuildScenario.StartAsync(o =>
        {
            o.Settings["toolchain.rtlDir"] = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
            o.Settings["toolchain.frameworkDir"] = Path.Combine(SpecTables.RepoRootPath(), "ui-framework"); // the compiler searches <framework>/src
            o.InitialFiles[@"Hello\Unit1.pas"] = formUnit;
            o.InitialFiles[@"Hello\Unit1.form"] = form;
        }, implementedPhase: null); // form documents need phase 5 (I-218)
        var document = Assert.IsType<FormDocumentViewModel>((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Value);
        Dispatcher.UIThread.RunJobs();
        var code = Assert.IsAssignableFrom<TextDocumentViewModel>(document.VisibleText); // the Code view

        CtrlClick(h, h.Find<TextEditor>("Editor.Unit1.pas"), 15, 4); // inside "Button1" of "  Button1.Caption := 'Go';"
        await h.WaitForAsync(() => code.ViewState.Caret == new TextPosition(6, 5), "jump to the field");
        Assert.Same(document, h.Services.Documents.ActiveDocument);
        Assert.Equal(new TextPosition(6, 5), code.ViewState.Caret); // "    Button1: TButton;"
    }
}
