using Avalonia;
using Avalonia.Controls;
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
/// Ctrl+hover (I-322, IDE-ED-149): while Ctrl alone is held over an identifier that has a declaration, the identifier is
/// underlined in <c>text.link</c> and the pointer is the hand cursor (UI-TOK-053); releasing Ctrl, moving off the token,
/// scrolling or editing removes it at once.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class CtrlHoverTests
{
    // Line 6 uses the declared Foo1, line 7 the undeclared Foo; a comment and a string; blank lines so the view can scroll.
    private static readonly string Unit = "unit Unit1;\r\ninterface\r\nimplementation\r\nvar Foo1: Integer;\r\nbegin\r\n"
        + "  Foo1 := 1; { Foo1 note }\r\n  Foo := Length('Foo1');\r\nend.\r\n" + string.Concat(Enumerable.Repeat("\r\n", 200));

    private sealed record Scenario(HeadlessHarness H, TextDocumentViewModel Document, TextEditor Editor, IEditorTestProbe Probe) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => H.DisposeAsync();

        public Point At(int line, int column) => GoToDeclarationTests.At(H, Editor, line, column);

        public void CtrlMove(int line, int column) => H.Window.MouseMove(At(line, column), RawInputModifiers.Control);

        public void ReleaseCtrl() => H.Window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);

        public (int Offset, int Length) Token(string text, int occurrence = 0)
        {
            var at = -1;
            for (var i = 0; i <= occurrence; i++)
                at = Document.Buffer.Text.IndexOf(text, at + 1, StringComparison.Ordinal);
            return (at, text.Length);
        }

        /// <summary>The underline request of the last pointer position has finished.</summary>
        public async Task SettleAsync()
        {
            await Document.DeclarationLink!.Pending;
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task<Scenario> StartAsync()
    {
        var h = await BuildScenario.StartAsync(o =>
        {
            LiveDiagnosticsTests.AddRealRtl(o);
            o.InitialFiles[@"Hello\Unit1.pas"] = Unit;
        });
        var doc = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(BuildScenario.UnitPath(h))).Value);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.Unit1.pas");
        Assert.True(editor.TextArea.Focus());
        Assert.NotNull(doc.DeclarationLink);
        return new Scenario(h, doc, editor, editor.FindAncestorOfType<CodeEditorView>()!);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-149")]
    public async Task UnderlineWhileCtrlHeld()
    {
        await using var s = await StartAsync();
        var foo1 = s.Token("Foo1", 1); // the use on line 6

        // Without Ctrl nothing is underlined.
        s.H.Window.MouseMove(s.At(6, 4));
        await s.SettleAsync();
        Assert.Null(s.Probe.DeclarationLink);
        Assert.False(s.Probe.HandCursor);

        // Ctrl pressed while the pointer rests on Foo1: underline in text.link, hand cursor.
        s.H.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        await s.H.WaitForAsync(() => s.Probe.DeclarationLink is not null, "underline");
        Assert.Equal(foo1, s.Probe.DeclarationLink);
        Assert.Equal("text.link", s.Probe.DeclarationLinkToken);
        Assert.True(s.Probe.HandCursor);

        // Moving within the token keeps it; Ctrl+move onto the declaration's own name underlines that one.
        s.CtrlMove(6, 6);
        Assert.Equal(foo1, s.Probe.DeclarationLink);
        s.CtrlMove(4, 6);
        await s.H.WaitForAsync(() => s.Probe.DeclarationLink == s.Token("Foo1"), "underline on the declaration");

        // Ctrl+Shift is not Ctrl alone.
        s.H.Window.MouseMove(s.At(6, 4), RawInputModifiers.Control | RawInputModifiers.Shift);
        await s.SettleAsync();
        Assert.Null(s.Probe.DeclarationLink);
        Assert.False(s.Probe.HandCursor);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-149")]
    public async Task ReleaseCtrlRemovesUnderline()
    {
        await using var s = await StartAsync();
        async Task ShowAsync()
        {
            s.CtrlMove(6, 4);
            await s.H.WaitForAsync(() => s.Probe.DeclarationLink is not null, "underline");
            Assert.True(s.Probe.HandCursor);
        }

        void AssertGone(string why)
        {
            Assert.True(s.Probe.DeclarationLink is null, why); // at once: no wait
            Assert.False(s.Probe.HandCursor, why);
        }

        await ShowAsync();
        s.ReleaseCtrl();
        AssertGone("Ctrl released");

        await ShowAsync();
        s.CtrlMove(5, 2); // onto "begin"
        AssertGone("moved off the token");

        await ShowAsync();
        s.Editor.GetVisualDescendants().OfType<ScrollViewer>().First().Offset = new Vector(0, 40);
        Dispatcher.UIThread.RunJobs();
        AssertGone("scrolled");

        s.Editor.GetVisualDescendants().OfType<ScrollViewer>().First().Offset = new Vector(0, 0);
        Dispatcher.UIThread.RunJobs();
        await ShowAsync();
        s.Document.Buffer.ApplyEdits([new TextEdit(0, 0, " ")]);
        AssertGone("edited");

        // A request still running when Ctrl is released is cancelled: nothing appears afterwards.
        s.CtrlMove(6, 4);
        s.ReleaseCtrl();
        await s.SettleAsync();
        AssertGone("released while asking");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-149")]
    public async Task KeywordGetsNoUnderline()
    {
        await using var s = await StartAsync();
        foreach (var (line, column, what) in new[]
                 {
                     (5, 2, "keyword begin"), (6, 18, "comment"), (7, 18, "string"), (7, 4, "undeclared Foo"),
                 })
        {
            s.CtrlMove(line, column);
            await s.SettleAsync();
            Assert.True(s.Probe.DeclarationLink is null, what);
            Assert.False(s.Probe.HandCursor, what);
        }

        // The control case: the declared identifier on the same lines is underlined.
        s.CtrlMove(6, 4);
        await s.SettleAsync();
        Assert.Equal(s.Token("Foo1", 1), s.Probe.DeclarationLink);
    }
}
