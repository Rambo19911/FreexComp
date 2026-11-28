using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Tests.Views;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>
/// The inline rename in the editor (I-381, F13-001; UI-ED-160, 161): F2 on a local draws its occurrences as fields with
/// the primary outlined; typing goes to every occurrence; a problem outlines the primary in the error colour and the status
/// bar says why; Enter commits, Escape takes it back — through the real keyboard of the main window.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class InlineRenameViewTests
{
    /// <summary>Unit1 of the rename fixture with the routine <c>Sum</c> and its local <c>Total</c> (five occurrences).</summary>
    internal static readonly string Unit1 = RenameDialogTests.Unit1.Replace("procedure Log(const S: string);\r\nbegin\r\n  Log(1);\r\nend;\r\nend.\r\n",
        "procedure Log(const S: string);\r\nbegin\r\n  Log(1);\r\nend;\r\nprocedure Sum;\r\nvar\r\n  Total: Integer;\r\nbegin\r\n  Total := 0;\r\n"
        + "  Total := Total + 1;\r\n  Log(Total);\r\nend;\r\nend.\r\n", StringComparison.Ordinal);

    /// <summary>The fixture with Unit1 open; the caret in the second <c>Total</c>, F2 pressed, the session active.</summary>
    internal static async Task<(HeadlessHarness H, TextEditor Editor, TextDocumentViewModel Doc, IEditorTestProbe Probe, InlineRenameSession Session)> StartAsync(
        Action<HarnessOptions>? configure = null)
    {
        var (h, editor, doc) = await RenameDialogTests.StartAsync(o =>
        {
            o.InitialFiles[@"Hello\Unit1.pas"] = Unit1;
            configure?.Invoke(o);
        });
        RenameDialogTests.CaretOn(editor, doc, "Total", 2);
        h.Press("F2");
        await h.WaitForAsync(() => doc.RenameSession is not null, "the inline session started");
        Dispatcher.UIThread.RunJobs();
        return (h, editor, doc, editor.FindAncestorOfType<CodeEditorView>()!, doc.RenameSession!);
    }

    private static int[] Occurrences(string text) =>
        [.. Enumerable.Range(0, text.Length).Where(i => string.CompareOrdinal(text, i, "Total", 0, 5) == 0)];

    /// <summary>The late work of the session ends before the test does (I-210).</summary>
    internal static async Task SettleAsync(HeadlessHarness h, TextDocumentViewModel doc, InlineRenameSession session)
    {
        await h.WaitForAsync(() => doc.RenameCommitting.IsCompleted && session.Checking.IsCompleted, "the session's work ended");
        await doc.RenameCommitting;
        await session.Checking;
        await h.WaitForAsync(() => h.Services.CodeModel.Worker.IsIdle, "the worker idle");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-160")]
    [Trait("Req", "IDE-REFAC-050")]
    [Trait("Req", "IDE-REFAC-053")]
    public async Task OccurrencesAreDrawnAndThePrimaryIsOutlined()
    {
        var (h, editor, doc, probe, session) = await StartAsync();
        await using var _ = h;
        await TrustDialogTests.Guarded(h, async () =>
        {
            // Every occurrence is a field, the one at the caret first; the primary is selected and outlined.
            var at = Occurrences(Unit1);
            Assert.Equal(5, at.Length);
            Assert.Equal(at[1], probe.RenameFields[0].Offset);
            Assert.Equal(at, probe.RenameFields.Select(f => f.Offset).Order());
            Assert.All(probe.RenameFields, f => Assert.Equal(5, f.Length));
            Assert.Equal(RenameFieldState.Editing, probe.RenameFieldState);
            Assert.Equal((at[1], at[1] + 5), (editor.SelectionStart, editor.SelectionStart + editor.SelectionLength));
            Assert.Equal("Rename Total: type the new name. Enter applies, Esc cancels", h.Status.Message);

            // Typed keys go to every occurrence.
            h.Type("Sum");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Unit1.Replace("Total", "Sum", StringComparison.Ordinal), doc.Buffer.Text);
            Assert.All(probe.RenameFields, f => Assert.Equal(3, f.Length));

            // Enter commits after the check: the fields are gone, the status says what happened.
            h.Press("Enter");
            await h.WaitForAsync(() => doc.RenameSession is null, "the rename committed");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(probe.RenameFields);
            Assert.Equal(RenameFieldState.None, probe.RenameFieldState);
            Assert.Equal("Renamed Total to Sum: 5 occurrences", h.Status.Message);
            Assert.True(doc.IsModified);
            Assert.Equal(Unit1, File.ReadAllText(RenameDialogTests.PathOf(h, "Unit1.pas")));
            await SettleAsync(h, doc, session);
        });
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-161")]
    [Trait("Req", "IDE-REFAC-052")]
    [Trait("Req", "IDE-REFAC-054")]
    public async Task ProblemOutlineAndStatusText()
    {
        var (h, editor, doc, probe, session) = await StartAsync();
        await using var _ = h;
        await TrustDialogTests.Guarded(h, async () =>
        {
            // A reserved word: after the 200 ms the outline is the error's and the status bar gives the reason.
            h.Type("begin");
            await h.WaitForAsync(() => probe.RenameFieldState == RenameFieldState.Problem, "the problem shown");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("begin is a reserved word.", h.Status.Message);

            // Enter keeps the session; a valid name takes the problem away.
            h.Press("Enter");
            await h.WaitForAsync(() => doc.RenameCommitting.IsCompleted, "the commit refused");
            Assert.Same(session, doc.RenameSession);
            h.Press("Backspace");
            h.Type("x");
            await h.WaitForAsync(() => probe.RenameFieldState == RenameFieldState.Editing && session.Checking.IsCompleted, "the problem gone");
            Assert.Equal("Rename Total: type the new name. Enter applies, Esc cancels", h.Status.Message);

            // Escape takes everything back: the text, the caret, the fields.
            var start = session.StartCaret;
            h.Press("Escape");
            Dispatcher.UIThread.RunJobs();
            Assert.Null(doc.RenameSession);
            Assert.Equal(RenameFieldState.None, probe.RenameFieldState);
            Assert.Equal(Unit1, doc.Buffer.Text);
            Assert.False(doc.IsModified);
            Assert.Equal(start, editor.CaretOffset);
            Assert.Equal("Rename canceled; Total is unchanged", h.Status.Message);
            await SettleAsync(h, doc, session);
        });
    }
}
