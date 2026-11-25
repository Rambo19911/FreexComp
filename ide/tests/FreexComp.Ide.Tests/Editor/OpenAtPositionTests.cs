using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>
/// A document that is opened or activated at a position shows that position (I-273): the request to scroll to the caret
/// arrives before the editor has measured the document it was just given, and must not be lost.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class OpenAtPositionTests
{
    private const int FarLine = 333;

    /// <summary>Unit1: the declaration of <c>FarAway</c> at line <see cref="FarLine"/>, far below the first page.</summary>
    private static string Unit1()
    {
        var text = new StringBuilder("unit Unit1;\r\ninterface\r\n");
        for (var line = 3; line < FarLine; line++)
            text.Append("// filler line ").Append(line).Append("\r\n");
        text.Append("var FarAway: Integer;\r\n");
        for (var line = FarLine + 1; line <= 600; line++)
            text.Append("// filler line ").Append(line).Append("\r\n");
        return text.Append("implementation\r\nend.\r\n").ToString();
    }

    private const string Program = "program Hello;\r\nuses Unit1;\r\nbegin\r\n  FarAway := 1;\r\nend.\r\n";

    private static Task<HeadlessHarness> StartAsync() => BuildScenario.StartAsync(o =>
    {
        LiveDiagnosticsTests.AddRealRtl(o);
        o.InitialFiles[@"Hello\Unit1.pas"] = Unit1();
        o.InitialFiles[@"Hello\Hello.pas"] = Program;
    });

    /// <summary>The first and the last document line that are completely inside the editor's viewport, after layout.</summary>
    private static (int First, int Last) VisibleLines(HeadlessHarness h, string editorId)
    {
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>(editorId);
        var textView = editor.TextArea.TextView;
        textView.EnsureVisualLines();
        var scroller = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroller.Viewport.Height > 5 * textView.DefaultLineHeight, "the editor has a viewport");
        var top = scroller.Offset.Y;
        var first = (int)Math.Ceiling(top / textView.DefaultLineHeight) + 1;
        var last = (int)Math.Floor((top + scroller.Viewport.Height) / textView.DefaultLineHeight);
        return (first, last);
    }

    private static void AssertCaretLineVisible(HeadlessHarness h, TextDocumentViewModel document, int line)
    {
        Assert.Equal(line, document.ViewState.Caret.Line);
        var (first, last) = VisibleLines(h, "Editor." + Path.GetFileName(document.FilePath));
        Assert.True(line >= first && line <= last, $"line {line} is not on screen: lines {first}…{last} are");
        // The model knows where the view is (what a session or a copy of the document restores).
        Assert.True(document.ViewState.VerticalOffset > 0, "the scroll position did not reach the model");
    }

    /// <summary>Go to Declaration into a unit that was not open: the caret line is on screen, not line 1.</summary>
    [AvaloniaFact(Timeout = 30000)]
    public async Task GoToDeclarationIntoAnUnopenedFileShowsTheCaretLine()
    {
        await using var h = await StartAsync();
        var program = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, @"Hello\Hello.pas"))).Value);
        Dispatcher.UIThread.RunJobs();
        program.Selection = TextSelection.At(Program.IndexOf("FarAway", StringComparison.Ordinal) + 2);
        Assert.True(h.Services.Commands.CanExecute("search.goToDeclaration"));

        await h.ExecuteAsync("search.goToDeclaration");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument is TextDocumentViewModel { FilePath: { } path } && path.EndsWith("Unit1.pas", StringComparison.OrdinalIgnoreCase),
            "the unit of the declaration is opened");

        var unit = Assert.IsType<TextDocumentViewModel>(h.Services.Documents.ActiveDocument);
        AssertCaretLineVisible(h, unit, FarLine);

        // Back in the program and to the declaration again: the unit is open now and is activated at the position.
        unit.Selection = TextSelection.At(0);
        h.Services.Documents.Activate(program);
        Dispatcher.UIThread.RunJobs();
        await h.ExecuteAsync("search.goToDeclaration");
        await h.WaitForAsync(() => ReferenceEquals(h.Services.Documents.ActiveDocument, unit) && unit.ViewState.Caret.Line == FarLine, "the open unit is activated");
        AssertCaretLineVisible(h, unit, FarLine);
    }

    /// <summary>The other open-at-position paths use the same request: Go to Line from the navigation service on a file just opened.</summary>
    [AvaloniaFact(Timeout = 30000)]
    public async Task NavigationIntoAFileJustOpenedShowsTheCaretLine()
    {
        await using var h = await StartAsync();
        var unit = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Value);
        // No dispatcher run in between: the editor has the document but has not measured it (message double-click,
        // search result, debugger location and bookmark all navigate like this right after the open).
        h.Services.Navigation.GoToLine(unit, 480, 1);
        AssertCaretLineVisible(h, unit, 480);

        // A find navigation (scroll just enough) while the editor is switching to the document.
        var program = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, @"Hello\Hello.pas"))).Value);
        Dispatcher.UIThread.RunJobs();
        unit.Selection = TextSelection.At(unit.Buffer.GetOffset(40, 1));
        Dispatcher.UIThread.RunJobs();
        h.Services.Documents.Activate(unit);
        h.Services.Navigation.GoToLine(unit, 120, 1);
        AssertCaretLineVisible(h, unit, 120);
        _ = program;
    }

    /// <summary>
    /// Copy to Project (IDE-LIB-033): the copy opens with the caret and the vertical scroll position of the library
    /// document, which itself was opened at a position.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-LIB-033")]
    public async Task CopyOfALibraryUnitOpensAtTheSamePosition()
    {
        await using var h = await BuildScenario.StartAsync(o => o.InitialFiles[@"tc\rtl\LongLib.pas"] = Unit1().Replace("unit Unit1;", "unit LongLib;", StringComparison.Ordinal));
        var library = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, @"tc\rtl\LongLib.pas"))).Value);
        h.Services.Navigation.GoToLine(library, FarLine, 3);
        AssertCaretLineVisible(h, library, FarLine);
        Assert.True(library.IsLibraryProtected);
        var first = VisibleLines(h, "Editor.LongLib.pas").First;

        h.Click("InfoBar.LibrarySource.Action.CopyToProject");
        await h.WaitForAsync(() => h.Services.Status.Message == "LongLib.pas was copied into the project. This project is now built from the copy.",
            "Copy to Project finished");

        var copy = Assert.IsType<TextDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Assert.Equal(Path.Combine(h.TempDir, @"Hello\LongLib.pas"), copy.FilePath);
        Assert.Equal(library.ViewState.Caret, copy.ViewState.Caret);
        AssertCaretLineVisible(h, copy, FarLine);
        Assert.Equal(first, VisibleLines(h, "Editor.LongLib.pas").First); // the same top line (the copy has no library bar, so more lines below)
        Assert.Equal(library.ViewState.VerticalOffset, copy.ViewState.VerticalOffset);
    }
}
