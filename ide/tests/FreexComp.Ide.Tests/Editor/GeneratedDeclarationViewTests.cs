using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>
/// I-323 (IDE-ED-148): Go to Declaration on a symbol without source (a <c>System</c> built-in) opens the generated, read-only
/// declaration view <c>&lt;Unit&gt; [generated]</c> of <c>NavigationService.GetGeneratedDeclaration</c> (CMP-1420) with the
/// name selected; a second jump into the same unit reuses its tab; the view is never saved, in the session or in recent files.
/// The Ctrl+hover check (IDE-ED-149) underlines such a symbol too.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class GeneratedDeclarationViewTests
{
    // Line 4 declares I (Integer, a predefined type); line 6 calls WriteLn (an intrinsic); line 7 uses the undeclared Foo.
    private const string Unit = "unit Unit1;\r\ninterface\r\nimplementation\r\nvar I: Integer;\r\nbegin\r\n"
        + "  WriteLn(I);\r\n  Foo := 1;\r\nend.\r\n";

    private static async Task<(HeadlessHarness H, TextDocumentViewModel Unit)> StartAsync()
    {
        var h = await BuildScenario.StartAsync(o =>
        {
            LiveDiagnosticsTests.AddRealRtl(o);
            o.InitialFiles[@"Hello\Unit1.pas"] = Unit;
        });
        var doc = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Find<TextEditor>("Editor.Unit1.pas").TextArea.Focus());
        return (h, doc);
    }

    /// <summary>F12 with the caret on <paramref name="word"/> of the unit; returns the then active document.</summary>
    private static async Task<TextDocumentViewModel> F12Async(HeadlessHarness h, TextDocumentViewModel unit, string word)
    {
        h.Services.Documents.Activate(unit);
        Dispatcher.UIThread.RunJobs();
        unit.Selection = TextSelection.At(Unit.IndexOf(word, StringComparison.Ordinal) + 1);
        await h.ExecuteAsync("search.goToDeclaration");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument != unit, "the generated view opens");
        return Assert.IsType<TextDocumentViewModel>(h.Services.Documents.ActiveDocument);
    }

    private static string Selected(TextDocumentViewModel document) =>
        document.Buffer.GetText(document.Selection.Start, document.Selection.End - document.Selection.Start);

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-148")]
    [Trait("Req", "IDE-ED-149")]
    public async Task BuiltinOpensReadOnlyGeneratedView()
    {
        var (h, unit) = await StartAsync();
        await using var _ = h;
        var editor = h.Find<TextEditor>("Editor.Unit1.pas");
        var probe = (IEditorTestProbe)editor.FindAncestorOfType<CodeEditorView>()!;

        // IDE-ED-149: Ctrl+hover over the built-in underlines it (it has a generated declaration).
        h.Window.MouseMove(GoToDeclarationTests.At(h, editor, 6, 4), RawInputModifiers.Control);
        await h.WaitForAsync(() => probe.DeclarationLink is not null, "underline on WriteLn");
        Assert.Equal((Unit.IndexOf("WriteLn", StringComparison.Ordinal), "WriteLn".Length), probe.DeclarationLink);
        h.Window.MouseMove(GoToDeclarationTests.At(h, editor, 6, 4));
        Dispatcher.UIThread.RunJobs();

        // F12: the read-only System [generated] view, Pascal, not modified, the name selected; a history entry in Unit1.
        var generated = await F12Async(h, unit, "WriteLn");
        Assert.Equal("System [generated]", generated.Title);
        Assert.Equal("generated:/System.pas", generated.GeneratedPath);
        Assert.Null(generated.FilePath);
        Assert.True(generated.IsReadOnly);
        Assert.False(generated.IsModified);
        Assert.Equal("pascal", generated.Language.Id);
        Assert.StartsWith("unit System;\n{ Generated from compiled symbols; read only. }\ninterface\n", generated.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal("writeln", Selected(generated).ToLowerInvariant());
        var (lineStart, lineLength, _) = generated.Buffer.GetLine(generated.ViewState.Caret.Line);
        Assert.StartsWith("procedure Writeln(", generated.Buffer.GetText(lineStart, lineLength), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Unit1.pas", h.Services.Navigation.History[^1].Name);
        Assert.Equal(new TextPosition(6, 4), h.Services.Navigation.History[^1].Position);
        var view = h.Find<TextEditor>("Editor.System [generated]");
        Assert.True(view.IsReadOnly);

        // Edits are refused as in a protected document; Save As is not offered.
        var text = generated.Buffer.Text;
        Assert.True(view.TextArea.Focus());
        h.Type("x");
        Assert.Equal(text, generated.Buffer.Text);
        Assert.Equal(Core.Resources.Strings.Msg_Editor_ReadOnlyEdit, h.Status.Message);
        Assert.False(h.Services.Commands.CanExecute("file.saveAs"));
        Assert.False(h.Services.Commands.CanExecute("file.save"));

        // Neither a source nor a generated declaration: the status message stays.
        h.Services.Documents.Activate(unit);
        Dispatcher.UIThread.RunJobs();
        unit.Selection = TextSelection.At(Unit.IndexOf("Foo", StringComparison.Ordinal) + 1);
        await h.ExecuteAsync("search.goToDeclaration");
        await h.WaitForAsync(() => h.Status.Message?.Contains("Foo", StringComparison.Ordinal) == true, "no declaration message");
        Assert.Same(unit, h.Services.Documents.ActiveDocument);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-148")]
    public async Task SecondJumpReusesTab()
    {
        var (h, unit) = await StartAsync();
        await using var _ = h;
        var generated = await F12Async(h, unit, "WriteLn");
        var count = h.Services.Documents.Documents.Count;
        var version = generated.Buffer.Version;

        // The predefined type Integer is in the same generated unit: the same tab, the text not replaced, the new name selected.
        var again = await F12Async(h, unit, "Integer");
        Assert.Same(generated, again);
        Assert.Equal(count, h.Services.Documents.Documents.Count);
        Assert.Equal(version, generated.Buffer.Version);
        Assert.Equal("integer", Selected(generated).ToLowerInvariant());
        Assert.Single(h.Services.Documents.Documents, d => d.Title == "System [generated]");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-148")]
    public async Task GeneratedViewNotInSessionOrRecent()
    {
        var (h, unit) = await StartAsync();
        await using var _ = h;
        var generated = await F12Async(h, unit, "WriteLn");

        await h.ExecuteAsync("file.saveAll");
        h.Services.Session.Save();
        var session = File.ReadAllText(h.Services.Session.SessionPath);
        Assert.Contains("Unit1.pas", session, StringComparison.Ordinal);
        Assert.DoesNotContain("generated", session, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(h.Services.RecentItems.Files, f => f.Path.Contains("generated", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.GetFiles(h.Root, "*generated*", SearchOption.AllDirectories));

        // Closing asks nothing (never modified).
        Assert.True(h.Services.Documents.Close(generated));
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(generated, h.Services.Documents.Documents);
    }
}
