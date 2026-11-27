using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Designer;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Refactoring;

/// <summary>
/// Undo of a whole operation of the refactoring engine (I-349, F13-003; IDE-REFAC-020…024): the history, the command
/// <c>refactor.undo</c>, <c>edit.undo</c> right after an operation, how every kind of file gets its text back, and that a
/// blocked or failed undo changes nothing and keeps the operation.
/// </summary>
[Trait("Phase", "8")]
public sealed class RefactoringUndoTests : RefactoringTestBase
{
    private RefactoringService Service => (RefactoringService)Engine;

    /// <summary><c>edit.undo</c> routed to <paramref name="document"/> (UI-CMD-010), as the key and the menu do.</summary>
    private static Task<bool> EditUndoAsync(TextDocumentViewModel document)
    {
        Assert.True(((ICommandTarget)document).CanExecute("edit.undo", null));
        return ((ICommandTarget)document).ExecuteAsync("edit.undo", null);
    }

    private static WorkspaceEdit Titled(string title, params FileEdit[] files) => new(title, [.. files]);

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-024")]
    public Task EditorUndoRightAfterApplyUndoesEveryFile() => TestUiThread.RunAsync(() => EditorUndoRightAfterApplyUndoesEveryFileOnUiThread());

    private async Task EditorUndoRightAfterApplyUndoesEveryFileOnUiThread()
    {
        var before = await ApplyToThreeFilesAsync();

        // One undo in A, in which nothing happened since the operation, takes back every file.
        Assert.True(await EditUndoAsync(before.A));

        AssertRestored(before);
        Assert.False(Engine.CanUndo);
        Assert.Empty(Dialogs.Requests);
        Assert.Equal(Format(Strings.Msg_Refactoring_UndoneMany, Title, 3), _services.Status.Message);

        // A second undo is the document's own: A has nothing to undo (as before the operation), B loses its own step.
        Assert.False(((ICommandTarget)before.A).CanExecute("edit.undo", null));
        before.A.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        var files = FilesSnapshot();
        Assert.True(await EditUndoAsync(before.A));
        Assert.Equal(before.AText, before.A.Buffer.Text);
        Assert.True(await EditUndoAsync(before.B));
        Assert.Equal(Unit("B"), before.B.Buffer.Text);
        Assert.Equal(files, FilesSnapshot());
        Assert.Empty(Dialogs.Requests);
        Assert.Equal(Format(Strings.Msg_Refactoring_UndoneMany, Title, 3), _services.Status.Message); // no second operation undo
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-022")]
    public Task EditedBufferIsRestoredByAnInverseEdit() => TestUiThread.RunAsync(() => EditedBufferIsRestoredByAnInverseEditOnUiThread());

    private async Task EditedBufferIsRestoredByAnInverseEditOnUiThread()
    {
        var before = await ApplyToThreeFilesAsync();
        var a = before.A.Buffer;
        var after = a.Text;

        // A character typed and taken back: A has the operation's text again, but something happened in it.
        a.ApplyEdits([new TextEdit(0, 0, "x")]);
        a.Undo();
        Assert.Equal(after, a.Text);
        Assert.True(a.CanRedo);
        var changes = new List<TextChangedEventArgs>();
        a.Changed += (_, e) => changes.Add(e);

        // edit.undo in A is now the document's own undo; refactor.undo takes the operation back.
        var result = await Engine.UndoAsync();

        Assert.Equal((RefactoringOutcome.Applied, 0, 3, null), (result.Outcome, result.EditCount, result.FileCount, result.Error));
        Assert.Empty(result.Blocked);
        Assert.Equal(before.AText, a.Text);
        // One edit that replaces the differing middle only: from the first renamed identifier to the last.
        var change = Assert.Single(changes);
        Assert.Equal(after.IndexOf("Renamed", StringComparison.Ordinal), change.Offset);
        Assert.True(change.Offset + change.RemovedLength < after.Length - 10, "the common end of the text was replaced too");
        Assert.Equal(before.AText.Length - after.Length, change.InsertedText.Length - change.RemovedLength);
        // It is one new undo step: undoing it brings the operation's text back; the earlier redo history is gone.
        Assert.True(before.A.IsModified);
        a.Undo();
        Assert.Equal(after, a.Text);
        a.Redo();
        Assert.False(a.CanRedo);

        // B, in which nothing happened, and the closed file are restored exactly.
        Assert.Equal(before.BText, before.B.Buffer.Text);
        Assert.False(before.B.Buffer.CanRedo);
        Assert.Equal(before.CBytes, Bytes(C));
        Assert.False(Engine.CanUndo);
        Assert.Equal(Format(Strings.Msg_Refactoring_UndoneMany, Title, 3), _services.Status.Message);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-021")]
    public Task LocallyUndoneBufferIsSkipped() => TestUiThread.RunAsync(() => LocallyUndoneBufferIsSkippedOnUiThread());

    private async Task LocallyUndoneBufferIsSkippedOnUiThread()
    {
        var before = await ApplyToThreeFilesAsync();
        var (a, b) = (before.A.Buffer, before.B.Buffer);

        // (1) A was taken back by its own undo: it is left alone, B and C are restored.
        a.Undo();
        var aVersion = a.Version;
        var result = await Engine.UndoAsync();
        Assert.Equal((RefactoringOutcome.Applied, 2), (result.Outcome, result.FileCount));
        Assert.Equal((before.AText, aVersion), (a.Text, a.Version));
        Assert.True(a.CanRedo); // its own redo is untouched
        Assert.Equal(before.BText, b.Text);
        Assert.Equal(before.CBytes, Bytes(C));
        Assert.False(Engine.CanUndo);
        Assert.Equal("Undid Rename Symbol in 2 files", _services.Status.Message);

        // (2) Both buffers taken back: one file is left to restore.
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.ApplyAsync(Edit(Rename(A, a.Text), Rename(B, b.Text), Rename(C, Text(C))), PreviewMode.Never)).Outcome);
        a.Undo();
        b.Undo();
        result = await Engine.UndoAsync();
        Assert.Equal((RefactoringOutcome.Applied, 1), (result.Outcome, result.FileCount));
        Assert.Equal(before.CBytes, Bytes(C));
        Assert.Equal("Undid Rename Symbol in 1 file", _services.Status.Message);
        Assert.Equal(Format(Strings.Msg_Refactoring_UndoneOne, Title), _services.Status.Message);

        // (3) Every file is back already (C by hand): nothing is written or edited, and the operation leaves the history.
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.ApplyAsync(Edit(Rename(A, a.Text), Rename(B, b.Text), Rename(C, Text(C))), PreviewMode.Never)).Outcome);
        a.Undo();
        b.Undo();
        Write(C, before.CBytes);
        var state = Snapshot();
        var calls = _recording.Calls.Count;
        result = await Engine.UndoAsync();
        Assert.Equal((RefactoringOutcome.NoChanges, 0, 0, null), (result.Outcome, result.EditCount, result.FileCount, result.Error));
        Assert.Equal(state, Snapshot());
        Assert.DoesNotContain(_recording.Calls.Skip(calls), IsWrite);
        Assert.False(Engine.CanUndo);
        Assert.Equal("Rename Symbol was already undone", _services.Status.Message);
        Assert.Equal(Format(Strings.Msg_Refactoring_AlreadyUndone, Title), _services.Status.Message);
        Assert.Empty(Dialogs.Requests);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-021")]
    public Task ChangedFileBlocksUndoAndChangesNothing() => TestUiThread.RunAsync(() => ChangedFileBlocksUndoAndChangesNothingOnUiThread());

    private async Task ChangedFileBlocksUndoAndChangesNothingOnUiThread()
    {
        var before = await ApplyToThreeFilesAsync();
        Dialogs.Answer("Msg.Refactoring.UndoBlocked", "OK");
        var afterC = Bytes(C);

        async Task BlockedAsync(string path, BlockReason reason, string reasonText)
        {
            var state = Snapshot();
            var requests = Dialogs.Requests.Count;
            var status = _services.Status.Message;
            var calls = _recording.Calls.Count;

            var result = await Engine.UndoAsync();

            Assert.Equal((RefactoringOutcome.Blocked, 0, 0), (result.Outcome, result.EditCount, result.FileCount));
            Assert.Equal([new BlockedFile(path, reason)], result.Blocked);
            var request = Assert.Single(Dialogs.Requests.Skip(requests));
            Assert.Equal("Msg.Refactoring.UndoBlocked", request.MessageId);
            Assert.Equal([Title, $"{path} — {reasonText}"], request.Args);
            // Nothing changed anywhere, and the operation is still the one to undo.
            Assert.Equal(state, Snapshot());
            Assert.DoesNotContain(_recording.Calls.Skip(calls), IsWrite);
            Assert.Equal(status, _services.Status.Message);
            Assert.True(Engine.CanUndo);
            Assert.Equal(Title, Engine.UndoTitle);
            Assert.False(Engine.IsBusy);
        }

        // (1) C was changed on disk after the operation.
        Write(C, [.. afterC, .. Encoding.UTF8.GetBytes("// later\r\n")]);
        await BlockedAsync(C, BlockReason.ChangedAfterwards, "The file was changed after the refactoring.");

        // (2) C is gone.
        _fs.DeleteFile(C);
        await BlockedAsync(C, BlockReason.NotFound, Strings.Reason_Refactoring_NotFound);

        // (3) C has the operation's bytes again but cannot be written.
        Write(C, afterC);
        _fs.SetReadOnly(C, true);
        await BlockedAsync(C, BlockReason.NotWritable, Strings.Reason_Refactoring_NotWritable);
        _fs.SetReadOnly(C, false);

        // (4) A buffer that is read only, and one that was edited after the operation.
        before.B.Buffer.IsReadOnly = true;
        await BlockedAsync(B, BlockReason.NotWritable, Strings.Reason_Refactoring_NotWritable);
        before.B.Buffer.IsReadOnly = false;
        before.B.Buffer.ApplyEdits([new TextEdit(0, 0, "// typed\n")]);
        await BlockedAsync(B, BlockReason.ChangedAfterwards, Strings.Reason_Refactoring_ChangedAfterwards);
        before.B.Buffer.Undo();

        // With C as the operation left it, the undo goes through.
        var done = await Engine.UndoAsync();
        Assert.Equal((RefactoringOutcome.Applied, 3), (done.Outcome, done.FileCount));
        Assert.Equal(before.CBytes, Bytes(C));
        Assert.Equal(before.AText, before.A.Buffer.Text);
        Assert.False(before.A.IsModified);
        Assert.Equal(before.BText, before.B.Buffer.Text);
        Assert.False(Engine.CanUndo);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-024")]
    public Task BlockedEditorUndoOffersThisFileOnly() => TestUiThread.RunAsync(() => BlockedEditorUndoOffersThisFileOnlyOnUiThread());

    private async Task BlockedEditorUndoOffersThisFileOnlyOnUiThread()
    {
        var before = await ApplyToThreeFilesAsync();
        var afterC = Bytes(C);
        Write(C, [.. afterC, .. Encoding.UTF8.GetBytes("// later\r\n")]); // C was changed on disk after the operation

        // Cancel: nothing changes, the operation stays.
        Dialogs.Enqueue("Msg.Refactoring.UndoBlockedLocal", "Cancel");
        var state = Snapshot();
        Assert.True(await EditUndoAsync(before.A));
        var request = Assert.Single(Dialogs.Requests);
        Assert.Equal("Msg.Refactoring.UndoBlockedLocal", request.MessageId);
        Assert.Equal([Title, $"{C} — {Strings.Reason_Refactoring_ChangedAfterwards}"], request.Args);
        Assert.Equal(state, Snapshot());
        Assert.True(Engine.CanUndo);

        // Undo in This File: A's own undo runs — A only.
        Dialogs.Enqueue("Msg.Refactoring.UndoBlockedLocal", "UndoInThisFile");
        var files = FilesSnapshot();
        Assert.True(await EditUndoAsync(before.A));
        Assert.Equal(2, Dialogs.Requests.Count);
        Assert.Equal(before.AText, before.A.Buffer.Text);
        Assert.False(before.A.IsModified);
        Assert.True(before.A.Buffer.CanRedo); // its own undo: the step can be redone
        Assert.Equal(Renamed(before.BText), before.B.Buffer.Text);
        Assert.Equal(files, FilesSnapshot());
        Assert.True(Engine.CanUndo);

        // A is back already for a later refactor.undo: with C as the operation left it, B and C are restored.
        Write(C, afterC);
        var result = await Engine.UndoAsync();
        Assert.Equal((RefactoringOutcome.Applied, 2), (result.Outcome, result.FileCount));
        Assert.Equal(before.BText, before.B.Buffer.Text);
        Assert.Equal(before.CBytes, Bytes(C));
        Assert.True(before.A.Buffer.CanRedo);
        Assert.False(Engine.CanUndo);

        // A write that fails is offered the same way, with the file and the system's reason; nothing has changed.
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.ApplyAsync(Edit(Insert(B), Insert(C)), PreviewMode.Never)).Outcome);
        Dialogs.Enqueue("Msg.Refactoring.UndoBlockedLocal", "Cancel");
        _recording.FailWhen = call => call.StartsWith("WriteAllBytesDurable " + C, StringComparison.Ordinal);
        state = Snapshot();
        Assert.True(await EditUndoAsync(before.B));
        _recording.FailWhen = null;
        request = Dialogs.Requests[^1];
        Assert.Equal(3, Dialogs.Requests.Count);
        Assert.Equal("Msg.Refactoring.UndoBlockedLocal", request.MessageId);
        Assert.Equal(Title, request.Args[0]);
        Assert.StartsWith($"{C} — Injected failure: WriteAllBytesDurable {C}", request.Args[1], StringComparison.Ordinal);
        Assert.Equal(state, Snapshot());
        Assert.True(Engine.CanUndo);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-021")]
    [Trait("Req", "IDE-REFAC-022")]
    [Trait("Req", "IDE-REFAC-023")]
    public Task FileOpenedOrClosedSinceTheApply() => TestUiThread.RunAsync(() => FileOpenedOrClosedSinceTheApplyOnUiThread());

    private async Task FileOpenedOrClosedSinceTheApplyOnUiThread()
    {
        await OpenProjectAsync();
        // A: UTF-16 with a byte-order mark and CRLF, open. C: closed.
        var utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        var aText = Unit("A", "\r\n");
        byte[] aBytes = [.. utf16.GetPreamble(), .. utf16.GetBytes(aText)];
        Write(A, aBytes);
        var a = await OpenAsync(A);
        Assert.Equal((aText, FileEncoding.Utf16Le), (a.Buffer.Text, a.Encoding));
        var cText = Text(C);
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.ApplyAsync(Edit(Rename(A, aText), Rename(C, cText)), PreviewMode.Never)).Outcome);
        var afterC = Bytes(C);

        // After the operation: A is saved and closed, C is opened in the editor.
        Assert.True(await _services.Documents.SaveAsync(a));
        Assert.True(await _services.Documents.CloseAsync(a));
        Assert.Equal([.. utf16.GetPreamble(), .. utf16.GetBytes(Renamed(aText))], Bytes(A));
        var c = await OpenAsync(C);
        Assert.Equal(Renamed(cText), c.Buffer.Text);
        var reread = new List<string?>();
        _services.CodeModel.Workspace.Changed += (_, e) =>
        {
            if (e.Kind == FreexComp.LanguageService.WorkspaceChangeKind.FileChangedOnDisk)
                reread.Add(e.Path);
        };
        var calls = _recording.Calls.Count;

        var result = await Engine.UndoAsync();

        Assert.Equal((RefactoringOutcome.Applied, 2), (result.Outcome, result.FileCount));
        Assert.Empty(Dialogs.Requests);
        // C is restored in its buffer, as one undo step; the file is not written.
        Assert.Equal(cText, c.Buffer.Text);
        Assert.True(c.IsModified);
        Assert.Equal(afterC, Bytes(C));
        Assert.Equal(0, Replacements(calls, C));
        c.Buffer.Undo();
        Assert.Equal(Renamed(cText), c.Buffer.Text);
        // A is a file now: the earlier text is written in the file's encoding. It is not opened.
        Assert.Equal(aBytes, Bytes(A));
        Assert.Equal(1, Replacements(calls, A));
        Assert.Equal(A, Assert.Single(reread), ignoreCase: true);
        Assert.Same(c, Assert.Single(_services.Documents.Documents));
        Assert.False(Engine.CanUndo);
        Assert.Equal(Format(Strings.Msg_Refactoring_UndoneMany, Title, 2), _services.Status.Message);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-021")]
    public Task WriteFailureDuringUndoPutsFilesBack() => TestUiThread.RunAsync(() => WriteFailureDuringUndoPutsFilesBackOnUiThread());

    private async Task WriteFailureDuringUndoPutsFilesBackOnUiThread()
    {
        await OpenProjectAsync();
        Dialogs.Answer("Msg.Refactoring.UndoFailed", "OK");
        var before = FilesSnapshot();
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.ApplyAsync(Edit(Rename(C, Text(C)), Rename(D, Text(D))), PreviewMode.Never)).Outcome);
        var applied = FilesSnapshot();
        var failures = 0;
        _recording.FailWhen = call => call.StartsWith("WriteAllBytesDurable " + D, StringComparison.Ordinal) && failures++ == 0; // the second restore, once
        var calls = _recording.Calls.Count;
        var status = _services.Status.Message;

        var result = await Engine.UndoAsync();

        _recording.FailWhen = null;
        Assert.Equal(1, failures);
        Assert.Equal(RefactoringOutcome.Failed, result.Outcome);
        Assert.StartsWith("Injected failure: WriteAllBytesDurable " + D, result.Error, StringComparison.Ordinal);
        var request = Assert.Single(Dialogs.Requests);
        Assert.Equal("Msg.Refactoring.UndoFailed", request.MessageId);
        Assert.Equal([Title, D, result.Error!], request.Args);
        // The first file was restored and has the operation's bytes again; no temporary file is left; the operation stays.
        Assert.Equal(2, Replacements(calls, C));
        Assert.Equal(0, Replacements(calls, D));
        Assert.Equal(applied, FilesSnapshot());
        Assert.Equal(status, _services.Status.Message);
        Assert.True(Engine.CanUndo);
        Assert.False(Engine.IsBusy);

        // Without the failure the same undo goes through.
        var done = await Engine.UndoAsync();
        Assert.Equal((RefactoringOutcome.Applied, 2), (done.Outcome, done.FileCount));
        Assert.Equal(before, FilesSnapshot());
        Assert.False(Engine.CanUndo);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-020")]
    public Task HistoryIsLastInFirstOutCappedAndClearedOnProjectClose() => TestUiThread.RunAsync(() => HistoryIsLastInFirstOutCappedAndClearedOnProjectCloseOnUiThread());

    private async Task HistoryIsLastInFirstOutCappedAndClearedOnProjectCloseOnUiThread()
    {
        await OpenProjectAsync();
        var original = FilesSnapshot();

        async Task ApplyAsync(string title, string path) =>
            Assert.Equal(RefactoringOutcome.Applied, (await Engine.ApplyAsync(Titled(title, Insert(path)), PreviewMode.Never)).Outcome);

        // Last in, first out: the newest operation is the one to undo, then the one below it.
        await ApplyAsync("First", C);
        await ApplyAsync("Second", D);
        Assert.Equal((2, "Second"), (HistoryCount, Engine.UndoTitle));
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.UndoAsync()).Outcome);
        Assert.Equal("Undid Second in 1 file", _services.Status.Message);
        Assert.Equal((Unit("D"), "{x}" + Unit("C")), (Text(D), Text(C)));
        Assert.Equal((1, "First"), (HistoryCount, Engine.UndoTitle));
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.UndoAsync()).Outcome);
        Assert.Equal((0, null, false), (HistoryCount, Engine.UndoTitle, Engine.CanUndo));
        Assert.Equal(original, FilesSnapshot());

        // At most 20 operations: the oldest is dropped.
        Assert.Equal(20, RefactoringService.MaxHistoryOperations);
        for (var i = 1; i <= 21; i++)
            await ApplyAsync("Op " + i, C);
        Assert.Equal((20, "Op 21"), (HistoryCount, Engine.UndoTitle));
        for (var i = 21; i >= 2; i--)
        {
            Assert.Equal("Op " + i, Engine.UndoTitle);
            Assert.Equal(RefactoringOutcome.Applied, (await Engine.UndoAsync()).Outcome);
        }

        Assert.False(Engine.CanUndo);
        Assert.Equal("{x}" + Unit("C"), Text(C)); // the dropped operation stays applied
        File(C, Unit("C"));

        // At most 64 000 000 characters of before and after texts: the oldest operations go first, the newest always stays.
        Assert.Equal(64_000_000, Service.MaxHistoryCharacters);
        var length = Unit("C").Length;
        Service.MaxHistoryCharacters = (2 * length + 9) + (2 * length + 15); // the second and the third operation exactly
        await ApplyAsync("One", C);   // length + (length + 3)
        await ApplyAsync("Two", C);   // (length + 3) + (length + 6)
        Assert.Equal(2, HistoryCount);
        await ApplyAsync("Three", C); // (length + 6) + (length + 9)
        Assert.Equal((2, "Three"), (HistoryCount, Engine.UndoTitle));
        Service.MaxHistoryCharacters = 1;
        await ApplyAsync("Four", C);
        Assert.Equal((1, "Four"), (HistoryCount, Engine.UndoTitle));
        Service.MaxHistoryCharacters = 64_000_000;

        // A change of the open project keeps the history.
        var projectChanges = 0;
        _services.Projects.ProjectChanged += (_, _) => projectChanges++;
        Assert.True(await _services.Projects.ApplyOptionsAsync(_services.Projects.Definition! with { SearchPaths = ["lib"] }));
        Assert.True(projectChanges > 0);
        Assert.Equal((1, "Four"), (HistoryCount, Engine.UndoTitle));

        // Loading another project empties it …
        var changed = 0;
        Engine.Changed += (_, _) => changed++;
        const string Other = @"C:\q\Other";
        File(Other + @"\Other.pproj", """{ "formatVersion": 1, "name": "Other", "appType": "console", "mainSource": "Other.pas", "files": [ { "path": "Other.pas" } ] }""");
        File(Other + @"\Other.pas", "program Other;\n\nbegin\nend.\n");
        Assert.True(await _services.Projects.OpenAsync(Other + @"\Other.pproj"));
        Assert.True(await _services.Documents.CloseAllAsync());
        await _services.ProjectManager.MissingFileChecks;
        Assert.Equal((0, null, false), (HistoryCount, Engine.UndoTitle, Engine.CanUndo));
        Assert.True(changed > 0);
        Assert.False(_services.Commands.CanExecute("refactor.undo"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine.UndoAsync());

        // … and so does closing the project.
        await ApplyAsync("Five", Other + @"\Other.pas");
        Assert.Equal((1, "Five"), (HistoryCount, Engine.UndoTitle));
        changed = 0;
        Assert.True(await _services.Projects.CloseAsync());
        Assert.Equal((0, null, false), (HistoryCount, Engine.UndoTitle, Engine.CanUndo));
        Assert.True(changed > 0);
        Assert.StartsWith("{x}", Text(Other + @"\Other.pas"), StringComparison.Ordinal); // clearing the history undoes nothing
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-022")]
    public Task FormTextIsRestoredThroughTheDesigner() => TestUiThread.RunAsync(() => FormTextIsRestoredThroughTheDesignerOnUiThread());

    private async Task FormTextIsRestoredThroughTheDesignerOnUiThread()
    {
        await FormFilesAsync();
        var document = await OpenFormAsync();
        var designer = document.Designer;
        DesignerFixture.Select(designer, "GreetButton");
        var cBytes = Bytes(C);
        var files = FilesSnapshot();
        Assert.Equal(RefactoringOutcome.Applied,
            (await Engine.ApplyAsync(Edit(Handler(FormPath, Form, "GreetClick"), Rename(C, Text(C))), PreviewMode.Never)).Outcome);
        Assert.Equal("GreetClick", OnClick(document));
        var unitVersion = document.Buffer.Version;

        var result = await Engine.UndoAsync();

        // The designer has the earlier form text and model again; the closed file its bytes; the .form was never written.
        Assert.Equal((RefactoringOutcome.Applied, 0, 2, null), (result.Outcome, result.EditCount, result.FileCount, result.Error));
        Assert.Equal(Form, designer.CurrentFormText());
        Assert.Equal(Form, document.FormBuffer.Text);
        Assert.Equal("GreetButtonClick", OnClick(document));
        Assert.Equal(cBytes, Bytes(C));
        Assert.Equal(files, FilesSnapshot());
        Assert.Equal(unitVersion, document.Buffer.Version);
        // As every revert of a form text: the view and the selection stay, the designer's own undo stack is empty.
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.Equal("GreetButton", designer.Surface!.Selection.Primary.Name);
        Assert.False(designer.Undo.CanUndo);
        Assert.False(Engine.CanUndo);
        Assert.Equal(Format(Strings.Msg_Refactoring_UndoneMany, Title, 2), _services.Status.Message);

        // The restore is one undo step of the form buffer (reachable from the Text view): taking it back gives the buffer
        // the operation's text, the step below it is the operation's own.
        Assert.True(document.ShowView(DocumentView.Text));
        document.FormBuffer.Undo();
        Assert.Equal(WithHandler(Form, "GreetClick"), document.FormBuffer.Text);
        document.FormBuffer.Undo();
        Assert.Equal(Form, document.FormBuffer.Text);
        Assert.False(document.FormBuffer.CanUndo);
    }

    /// <summary>IDE-REFAC-021 for a form-text target: a form changed in the designer after the operation blocks the undo.</summary>
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-021")]
    public Task FormChangedInTheDesignerBlocksUndo() => TestUiThread.RunAsync(() => FormChangedInTheDesignerBlocksUndoOnUiThread());

    private async Task FormChangedInTheDesignerBlocksUndoOnUiThread()
    {
        await FormFilesAsync();
        var document = await OpenFormAsync();
        var designer = document.Designer;
        Dialogs.Answer("Msg.Refactoring.UndoBlocked", "OK");
        Assert.Equal(RefactoringOutcome.Applied,
            (await Engine.ApplyAsync(Edit(Handler(FormPath, Form, "GreetClick"), Rename(C, Text(C))), PreviewMode.Never)).Outcome);
        SetCaption(document, "Hello");
        var changedText = designer.CurrentFormText();
        var files = FilesSnapshot();
        var formVersion = document.FormBuffer.Version;

        var blocked = await Engine.UndoAsync();

        Assert.Equal(RefactoringOutcome.Blocked, blocked.Outcome);
        Assert.Equal([new BlockedFile(FormPath, BlockReason.ChangedAfterwards)], blocked.Blocked);
        Assert.Equal([Title, $"{FormPath} — {Strings.Reason_Refactoring_ChangedAfterwards}"], Assert.Single(Dialogs.Requests).Args);
        Assert.Equal(changedText, designer.CurrentFormText());
        Assert.Equal("GreetClick", OnClick(document));
        Assert.Equal(formVersion, document.FormBuffer.Version);
        Assert.Equal(files, FilesSnapshot());
        Assert.True(designer.Undo.CanUndo);
        Assert.True(Engine.CanUndo);

        // A form that already has its earlier text (here: the operation's step taken back in the Text view) is left alone;
        // the closed file is restored.
        Assert.True(document.ShowView(DocumentView.Text));
        document.FormBuffer.ReplaceAll(Form, resetUndo: false);
        Assert.True(document.ShowView(DocumentView.Design));
        Assert.Equal(Form, designer.CurrentFormText());
        formVersion = document.FormBuffer.Version;
        var result = await Engine.UndoAsync();
        Assert.Equal((RefactoringOutcome.Applied, 1), (result.Outcome, result.FileCount));
        Assert.Equal(formVersion, document.FormBuffer.Version);
        Assert.Equal(Unit("C"), Text(C));
        Assert.False(Engine.CanUndo);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-012")]
    [Trait("Req", "IDE-REFAC-021")]
    public Task BusyEngineDisablesUndo() => TestUiThread.RunAsync(() => BusyEngineDisablesUndoOnUiThread());

    private async Task BusyEngineDisablesUndoOnUiThread()
    {
        await OpenProjectAsync();
        Dialogs.Answer("Msg.Refactoring.Blocked", "OK");
        var a = await OpenAsync(A);
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.ApplyAsync(Titled("First", Insert(A)), PreviewMode.Never)).Outcome);
        Assert.True(_services.Commands.CanExecute("refactor.undo"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new ManualResetEventSlim();
        using var release = new Releaser(gate);
        // A second operation is held in its write step: the write of the closed file waits for the gate.
        _recording.FailWhen = call =>
        {
            if (call.StartsWith("WriteAllBytesDurable " + C, StringComparison.Ordinal))
            {
                entered.TrySetResult();
                gate.Wait();
            }

            return false;
        };
        var running = Engine.ApplyAsync(Titled("Second", new FileEdit(A, a.Buffer.Text, [new TextEdit(a.Buffer.Length, 0, "{y}")]), Insert(C)), PreviewMode.Never);
        await entered.Task;

        // Busy: refactor.undo is disabled and a direct call is refused; the history is as it was.
        Assert.True(Engine.IsBusy);
        Assert.True(Engine.CanUndo);
        Assert.False(_services.Commands.CanExecute("refactor.undo"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine.UndoAsync());
        Assert.Equal("First", Engine.UndoTitle);

        // edit.undo in A — nothing happened in it since the first operation — is the buffer's own undo while busy.
        var files = FilesSnapshot();
        Assert.True(await EditUndoAsync(a));
        Assert.Equal(Unit("A"), a.Buffer.Text);
        Assert.True(a.Buffer.CanRedo);
        Assert.Equal((1, "First"), (HistoryCount, Engine.UndoTitle));
        Assert.Empty(Dialogs.Requests);

        // The held operation ends (blocked: A changed under it); the engine's command is back.
        gate.Set();
        var result = await running;
        _recording.FailWhen = null;
        Assert.Equal(RefactoringOutcome.Blocked, result.Outcome);
        Assert.Equal(files, FilesSnapshot());
        Assert.False(Engine.IsBusy);
        Assert.True(_services.Commands.CanExecute("refactor.undo"));
        Assert.Equal((1, "First"), (HistoryCount, Engine.UndoTitle));

        // The first operation is back in its only file already.
        Assert.Equal(RefactoringOutcome.NoChanges, (await Engine.UndoAsync()).Outcome);
        Assert.False(_services.Commands.CanExecute("refactor.undo"));
    }
}
