using System.Text;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.TestSupport;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.Tests.Refactoring;

/// <summary>
/// I-378 (F13-001), IDE-REFAC-047 a, b, d: the engine additions the rename is built on — form texts given to the
/// conversion of language-service edits, the callback of an applied workspace edit, and the check without an operation.
/// </summary>
public sealed partial class WorkspaceEditTests
{
    /// <summary>
    /// IDE-REFAC-047 a: a form text given for a <c>.form</c> path is that file's base text and the file is not read —
    /// so the edits the language service computed against the designer's current form text apply to an open form with
    /// unsaved changes. A form without a given text is read from disk as before; a key that is not a form is not used.
    /// </summary>
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-047")]
    public Task FromLanguageServiceUsesGivenFormTexts() => TestUiThread.RunAsync(() => FromLanguageServiceUsesGivenFormTextsOnUiThread());

    private async Task FromLanguageServiceUsesGivenFormTextsOnUiThread()
    {
        const string OtherForm = Dir + @"\Other.form";
        const string OtherText = "object Other: TOther\n  OnShow = Old\nend\n";
        await FormFilesAsync();
        Write(OtherForm, [.. Utf8Bom, .. Encoding.UTF8.GetBytes(OtherText)]);

        // The form is open and has an unsaved designer change: its current text is not the text on disk.
        var document = await OpenFormAsync();
        SetCaption(document, "Hello");
        var current = document.Designer.CurrentFormText();
        Assert.NotEqual(Text(FormPath), current);
        var compilation = _services.CodeModel.Synchronize()!;

        // Edits as the language service gives them when it was handed the current form text (offsets into that text).
        static DocumentEdit Change(string path, string text, string old, string replacement)
        {
            var at = text.IndexOf(old, StringComparison.Ordinal);
            Assert.True(at >= 0, old);
            return new DocumentEdit(path, [new TextChange(new TextSpan(at, old.Length), replacement)]);
        }

        DocumentEdit[] edits =
        [
            Change(FormPath, current, "GreetButtonClick", "HelloClick"), Change(OtherForm, OtherText, "Old", "Renamed"), Change(A, Unit("A"), "Old", "Renamed"),
        ];
        var read = new List<string>();
        _recording.BeforeRead = path =>
        {
            lock (read)
                read.Add(path);
        };

        // The key is the full path in any letter case; a path that is not a form is never taken from the texts.
        var formTexts = new Dictionary<string, string> { [FormPath.ToUpperInvariant()] = current, [A] = "not a form" };
        var edit = await WorkspaceEdit.FromLanguageService(Title, edits, compilation, _recording, CancellationToken.None, formTexts);

        Assert.Equal([FormPath, OtherForm, A], edit.Files.Select(f => f.Path));
        Assert.Equal([current, OtherText, Unit("A")], edit.Files.Select(f => f.BaseText));
        Assert.Equal(["GreetButtonClick", "Old", "Old"], edit.Files.Select(f => f.BaseText.Substring(f.Edits[0].Offset, f.Edits[0].Length)));
        lock (read)
        {
            Assert.DoesNotContain(read, p => string.Equals(p, FormPath, StringComparison.OrdinalIgnoreCase)); // the disk is not read
            Assert.Contains(read, p => string.Equals(p, OtherForm, StringComparison.OrdinalIgnoreCase));      // a form without a given text is
        }

        // The edit applies to the open form: the designer has the renamed handler and keeps its unsaved change.
        _recording.BeforeRead = null;
        var applied = await Engine.ApplyAsync(edit, PreviewMode.Never);
        Assert.Equal((RefactoringOutcome.Applied, 3, 3), (applied.Outcome, applied.EditCount, applied.FileCount));
        Assert.Equal("HelloClick", OnClick(document));
        Assert.Contains("Caption = 'Hello'", document.Designer.CurrentFormText(), StringComparison.Ordinal);
        Assert.Equal(Form, Text(FormPath)); // not saved

        // A given text needs no file at all; without the texts the same edits fail on the missing file, as before.
        _fs.DeleteFile(OtherForm);
        var given = await WorkspaceEdit.FromLanguageService(Title, [edits[1]], compilation, _recording,
            formTexts: new Dictionary<string, string> { [OtherForm] = OtherText });
        Assert.Equal(OtherText, Assert.Single(given.Files).BaseText);
        await Assert.ThrowsAsync<WorkspaceEditReadException>(() => WorkspaceEdit.FromLanguageService(Title, [edits[1]], compilation, _recording));
        await Assert.ThrowsAsync<WorkspaceEditReadException>(
            () => WorkspaceEdit.FromLanguageService(Title, [edits[1]], compilation, _recording, formTexts: new Dictionary<string, string>()));
    }

    /// <summary>
    /// IDE-REFAC-047 b: the callback of an applied workspace edit runs once, on the UI thread, with the result the caller
    /// gets, after the operation is on the history and its status is shown; normalisation keeps it; an exception it throws
    /// is logged and changes nothing; an undo does not call it.
    /// </summary>
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-047")]
    public Task OnAppliedRunsOnceAfterTheOperationIsRecorded() => TestUiThread.RunAsync(() => OnAppliedRunsOnceAfterTheOperationIsRecordedOnUiThread());

    private async Task OnAppliedRunsOnceAfterTheOperationIsRecordedOnUiThread()
    {
        await OpenProjectAsync();
        var a = await OpenAsync(A);
        var uiThread = Environment.CurrentManagedThreadId;
        var calls = new List<(RefactoringResult Result, int Thread, int History, string? UndoTitle, string? Status, string AText, string CText, bool Busy)>();
        void Record(RefactoringResult result) => calls.Add((result, Environment.CurrentManagedThreadId, HistoryCount, Engine.UndoTitle,
            _services.Status.Message, a.Buffer.Text, Text(C), Engine.IsBusy));

        // An open buffer, a closed file, and a file edit without edits that normalisation drops.
        var edit = Edit(Insert(A), Insert(C), new FileEdit(D, Text(D), [])) with { OnApplied = Record };
        var result = await Engine.ApplyAsync(edit, PreviewMode.Never);

        Assert.Equal((RefactoringOutcome.Applied, 2, 2), (result.Outcome, result.EditCount, result.FileCount));
        var call = Assert.Single(calls);
        Assert.Same(result, call.Result);
        Assert.Equal(uiThread, call.Thread);
        // Step 4 of the apply is over: both texts are there, the operation is on the history and its status is shown.
        // The operation itself has not ended: the engine is still busy.
        Assert.Equal((1, Title, "Rename Symbol: 2 changes in 2 files", "{x}" + Unit("A"), "{x}" + Unit("C"), true),
            (call.History, call.UndoTitle, call.Status, call.AText, call.CText, call.Busy));
        Assert.False(Engine.IsBusy);

        // A status the callback shows replaces the engine's (it is shown after it).
        var said = await Engine.ApplyAsync(Edit(Insert(A)) with { OnApplied = _ => _services.Status.ShowMessage("from the callback", TimeSpan.FromSeconds(5)) }, PreviewMode.Never);
        Assert.Equal(RefactoringOutcome.Applied, said.Outcome);
        Assert.Equal("from the callback", _services.Status.Message);

        // A callback that throws: logged as a warning, nothing else happens — the operation is applied and recorded.
        var logged = Log.Lines.Count;
        var requests = Dialogs.Requests.Count;
        var thrown = await Engine.ApplyAsync(Edit(Insert(A)) with { OnApplied = _ => throw new InvalidOperationException("callback failed") }, PreviewMode.Never);
        Assert.Equal((RefactoringOutcome.Applied, 1, 1, null), (thrown.Outcome, thrown.EditCount, thrown.FileCount, thrown.Error));
        Assert.Contains("callback failed", Assert.Single(Log.Lines.Skip(logged), l => l.StartsWith("WARN Refactoring: ", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Equal(requests, Dialogs.Requests.Count);
        Assert.Equal((3, false), (HistoryCount, Engine.IsBusy));
        Assert.Equal("{x}{x}{x}" + Unit("A"), a.Buffer.Text);

        // The edit a refactoring computes carries its callback the same way.
        var refactoring = new FakeRefactoring("insert", "Insert")
        {
            Compute = (_, _) => Task.FromResult<WorkspaceEdit?>(Edit(Insert(A)) with { OnApplied = Record }),
        };
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.RunAsync(refactoring)).Outcome);
        Assert.Equal(2, calls.Count);
        Assert.Equal((4, RefactoringOutcome.Applied, 1), (calls[1].History, calls[1].Result.Outcome, calls[1].Result.EditCount));

        // Taking an operation back is not an applied operation of that edit.
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.UndoAsync()).Outcome);
        Assert.Equal(2, calls.Count);
    }

    /// <summary>
    /// IDE-REFAC-047 b with the Preview dialog: the edit the dialog returns — reduced to the checked changes — still has
    /// the callback, which gets the result of what was applied.
    /// </summary>
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-047")]
    public Task OnAppliedSurvivesThePreviewReduction() => TestUiThread.RunAsync(() => OnAppliedSurvivesThePreviewReductionOnUiThread());

    private async Task OnAppliedSurvivesThePreviewReductionOnUiThread()
    {
        await OpenProjectAsync();
        var a = await OpenAsync(A);
        var results = new List<RefactoringResult>();
        var edit = Edit(Rename(C, Text(C)), Rename(A, a.Buffer.Text)) with { OnApplied = results.Add };
        RefactoringPreviewViewModel? shown = null;

        // The real dialog model: the file row of C.pas is unchecked, then Apply.
        Dialogs.Script(RefactoringPreviewViewModel.DialogName, async dialog =>
        {
            var preview = shown = Assert.IsType<RefactoringPreviewViewModel>(dialog);
            Assert.Equal([A, C], preview.Files.Select(f => f.Path));
            preview.Toggle(preview.Files[1]);
            await preview.PressDefaultAsync();
        });
        var result = await Engine.ApplyAsync(edit, PreviewMode.Always);

        // The dialog's diff work (UI-DLG-364) must not outlive the test (I-210).
        Assert.NotNull(shown);
        await shown.DiffUpdate;
        if (shown.Diff is { } diff)
            await diff.Recomputing;

        Assert.Single(Dialogs.Dialogs);
        Assert.Equal((RefactoringOutcome.Applied, 2, 1), (result.Outcome, result.EditCount, result.FileCount));
        Assert.Same(result, Assert.Single(results));
        Assert.Equal(Renamed(Unit("A")), a.Buffer.Text);
        Assert.Equal(Unit("C"), Text(C)); // the unchecked file is left out
    }

    /// <summary>IDE-REFAC-047 b: the callback is for the outcome <c>Applied</c> only.</summary>
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-047")]
    public Task OnAppliedIsNotCalledWhenBlockedOrCanceled() => TestUiThread.RunAsync(() => OnAppliedIsNotCalledWhenBlockedOrCanceledOnUiThread());

    private async Task OnAppliedIsNotCalledWhenBlockedOrCanceledOnUiThread()
    {
        await OpenProjectAsync();
        var a = await OpenAsync(A);
        Dialogs.Answer("Msg.Refactoring.Blocked", "OK");
        Dialogs.Answer("Msg.Refactoring.Failed", "OK");
        Dialogs.Answer("Msg.Refactoring.ApplyFailed", "OK");
        var calls = 0;
        WorkspaceEdit WithCallback(params FileEdit[] files) => Edit(files) with { OnApplied = _ => calls++ };
        async Task<RefactoringOutcome> OutcomeAsync(WorkspaceEdit edit, PreviewMode mode = PreviewMode.Never, CancellationToken ct = default) =>
            (await Engine.ApplyAsync(edit, mode, ct)).Outcome;
        var before = Snapshot();

        // Blocked: a file that does not exist.
        Assert.Equal(RefactoringOutcome.Blocked, await OutcomeAsync(WithCallback(Insert(A), Rename(Dir + @"\Gone.pas", Unit("Gone")))));

        // Cancelled in the Preview dialog, and by the caller's token.
        Dialogs.Script(RefactoringPreviewViewModel.DialogName, dialog => dialog.PressCancelAsync());
        Assert.Equal(RefactoringOutcome.Canceled, await OutcomeAsync(WithCallback(Insert(A), Insert(C)), PreviewMode.Always));
        Assert.Single(Dialogs.Dialogs);
        Assert.Equal(RefactoringOutcome.Canceled, await OutcomeAsync(WithCallback(Insert(A), Insert(C)), ct: new CancellationToken(canceled: true)));

        // Nothing to change, and an invalid edit (two file edits for one path).
        Assert.Equal(RefactoringOutcome.NoChanges, await OutcomeAsync(WithCallback(new FileEdit(A, a.Buffer.Text, []))));
        Assert.Equal(RefactoringOutcome.Failed, await OutcomeAsync(WithCallback(Insert(A), Insert(A))));

        // A write that fails: everything is put back.
        _recording.FailWhen = call => call.StartsWith("WriteAllBytesDurable " + C, StringComparison.Ordinal);
        Assert.Equal(RefactoringOutcome.Failed, await OutcomeAsync(WithCallback(Insert(A), Insert(C))));
        _recording.FailWhen = null;

        Assert.Equal(0, calls);
        Assert.Equal(before, Snapshot());
        Assert.Equal(0, HistoryCount);

        // The same kind of edit, applied: now it is called.
        Assert.Equal(RefactoringOutcome.Applied, await OutcomeAsync(WithCallback(Insert(A), Insert(C))));
        Assert.Equal(1, calls);
    }

    /// <summary>
    /// IDE-REFAC-047 d: <c>CheckAsync</c> gives the files that block an edit, with the first reason of each, in path order —
    /// empty when the edit may be applied — and shows and changes nothing; it may be asked at any time, also while an
    /// operation runs.
    /// </summary>
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-047")]
    public Task CheckReportsBlockedFilesWithoutChangingAnything() => TestUiThread.RunAsync(() => CheckReportsBlockedFilesWithoutChangingAnythingOnUiThread());

    private async Task CheckReportsBlockedFilesWithoutChangingAnythingOnUiThread()
    {
        const string Gone = Dir + @"\Gone.pas";
        const string Outside = @"E:\other\Other.pas";
        File(Outside, Unit("Other"));
        await OpenProjectAsync();
        var a = await OpenAsync(A);
        var b = await OpenAsync(B);
        _services.Status.ShowMessage("before", TimeSpan.FromHours(1));
        var busy = new List<bool>();
        Engine.Changed += (_, _) => busy.Add(Engine.IsBusy);

        // An edit that may be applied: no blocked file.
        Assert.Empty(await Engine.CheckAsync(Edit(Insert(A), Insert(C))));

        // One file per reason, given out of order; a file edit that changes nothing is not judged (it is normalised away).
        var stale = Insert(B);
        b.Buffer.ApplyEdits([new TextEdit(0, 0, "// typed\n")]);
        _fs.SetReadOnly(D, true);
        var snapshot = Snapshot();
        var calls = _recording.Calls.Count;
        var blocked = await Engine.CheckAsync(Edit(Rename(Outside, Unit("Other")), Rename(Gone, Unit("Gone")), Rename(D, Unit("D")), stale, Insert(A),
            Rename(C, Unit("C") + "// computed on an older text\n"), new FileEdit(Dir + @"\Missing.pas", "", [])));
        Assert.Equal(
            [
                new BlockedFile(B, BlockReason.ChangedSinceComputed), new BlockedFile(C, BlockReason.ChangedOnDisk), new BlockedFile(D, BlockReason.NotWritable),
                new BlockedFile(Gone, BlockReason.NotFound), new BlockedFile(Outside, BlockReason.OutsideProject),
            ],
            blocked);

        // Nothing was shown, nothing changed, the engine was never busy.
        Assert.Equal(snapshot, Snapshot());
        Assert.Empty(Dialogs.Requests);
        Assert.Empty(Dialogs.Dialogs);
        Assert.Equal("before", _services.Status.Message);
        Assert.Empty(busy);
        Assert.Equal(0, HistoryCount);
        Assert.DoesNotContain(_recording.Calls.Skip(calls), IsWrite);
        b.Buffer.Undo();
        _fs.SetReadOnly(D, false);
        var before = Snapshot();

        // While an operation runs — here inside the computation of a refactoring — the check answers too.
        IReadOnlyList<BlockedFile>? during = null;
        var refactoring = new FakeRefactoring("check", "Check")
        {
            Compute = async (_, _) =>
            {
                Assert.True(Engine.IsBusy);
                during = await Engine.CheckAsync(Edit(Insert(A), Rename(Gone, Unit("Gone"))));
                return null;
            },
        };
        Assert.Equal(RefactoringOutcome.Canceled, (await Engine.RunAsync(refactoring)).Outcome);
        Assert.Equal([new BlockedFile(Gone, BlockReason.NotFound)], during);
        Assert.Equal(before, Snapshot());
        Assert.Empty(Dialogs.Requests);
    }
}
