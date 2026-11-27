using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Refactoring;

/// <summary>
/// The refactoring engine's core (I-347, F13-003): the workspace-edit model with normalisation and validation, the checks
/// that block an operation, and the all-or-nothing apply — closed files are written first, then every open buffer is
/// edited as one undo step and not saved; a failed write and a buffer changed meanwhile roll everything back.
/// </summary>
[Trait("Phase", "8")]
public sealed partial class WorkspaceEditTests : RefactoringTestBase
{
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-001")]
    [Trait("Req", "IDE-REFAC-004")]
    [Trait("Req", "IDE-REFAC-008")]
    [Trait("Req", "IDE-REFAC-009")]
    [Trait("Req", "IDE-REFAC-010")]
    [Trait("Req", "IDE-REFAC-012")]
    public Task ThreeFilesOneClosed_ApplyChangesAll() => TestUiThread.RunAsync(() => ThreeFilesOneClosed_ApplyChangesAllOnUiThread());

    private async Task ThreeFilesOneClosed_ApplyChangesAllOnUiThread()
    {
        await OpenProjectAsync();
        // C is closed: UTF-8 with a byte-order mark and CRLF line breaks.
        var cText = Unit("C", "\r\n");
        Write(C, [.. Utf8Bom, .. Encoding.UTF8.GetBytes(cText)]);
        var a = await OpenAsync(A);
        var b = await OpenAsync(B);
        b.Buffer.ApplyEdits([new TextEdit(0, 0, "// unsaved\n")]); // B has an unsaved change
        var aText = a.Buffer.Text;
        var bText = b.Buffer.Text;
        Assert.False(a.IsModified);
        Assert.True(b.IsModified);
        Assert.Equal(cText, _services.CodeModel.Synchronize()!.GetSyntaxTree(C).Text.Text); // the code model has read C
        var changed = 0;
        Engine.Changed += (_, _) => changed++;
        var reread = new List<string?>(); // the files the code model was told to read again
        _services.CodeModel.Workspace.Changed += (_, e) =>
        {
            if (e.Kind == FreexComp.LanguageService.WorkspaceChangeKind.FileChangedOnDisk)
                reread.Add(e.Path);
        };
        var calls = _recording.Calls.Count;

        var result = await Engine.ApplyAsync(Edit(Rename(B, bText), Rename(C, cText), Rename(A, aText)), PreviewMode.Never);

        Assert.Equal((RefactoringOutcome.Applied, 6, 3, null), (result.Outcome, result.EditCount, result.FileCount, result.Error));
        Assert.Empty(result.Blocked);
        Assert.Empty(Dialogs.Requests);
        Assert.False(Engine.IsBusy);
        Assert.True(changed > 0);

        // The open buffers hold the new text and are not saved: modified like after typing, nothing written.
        Assert.Equal(Renamed(aText), a.Buffer.Text);
        Assert.Equal(Renamed(bText), b.Buffer.Text);
        Assert.True(a.IsModified);
        Assert.True(b.IsModified);
        Assert.Equal(Unit("A"), Text(A));
        Assert.Equal(Unit("B"), Text(B));
        Assert.DoesNotContain(_recording.Calls.Skip(calls), c => IsWrite(c) && (c.Contains(A, StringComparison.Ordinal) || c.Contains(B, StringComparison.Ordinal)));

        // The closed file: exactly the expected bytes — byte-order mark and CRLF kept — and it is not opened.
        Assert.Equal([.. Utf8Bom, .. Encoding.UTF8.GetBytes(Renamed(cText))], Bytes(C));
        Assert.Equal(2, _services.Documents.Documents.Count);
        Assert.DoesNotContain(_services.Documents.Documents, d => string.Equals(d.FilePath, C, StringComparison.OrdinalIgnoreCase));
        Assert.Same(b, _services.Documents.ActiveDocument); // no document was activated

        // The code model was told about the written file — and only about it — and reads C's new text.
        Assert.Equal(C, Assert.Single(reread), ignoreCase: true);
        Assert.Equal(Renamed(cText), _services.CodeModel.Synchronize()!.GetSyntaxTree(C).Text.Text);

        Assert.Equal("Rename Symbol: 6 changes in 3 files", _services.Status.Message);
        Assert.Equal(Format(Strings.Msg_Refactoring_AppliedMany, Title, 6, 3), _services.Status.Message);

        // One undo step per document.
        a.Buffer.Undo();
        Assert.Equal(aText, a.Buffer.Text);
        b.Buffer.Undo();
        Assert.Equal(bText, b.Buffer.Text);
    }

    /// <summary>F13-003 acceptance 1: the operation over three files, one of them closed, then <c>refactor.undo</c>.</summary>
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-020")]
    [Trait("Req", "IDE-REFAC-021")]
    [Trait("Req", "IDE-REFAC-022")]
    [Trait("Req", "IDE-REFAC-023")]
    public Task ThreeFilesOneClosed_ApplyThenUndoIsByteIdentical() => TestUiThread.RunAsync(() => ThreeFilesOneClosed_ApplyThenUndoIsByteIdenticalOnUiThread());

    private async Task ThreeFilesOneClosed_ApplyThenUndoIsByteIdenticalOnUiThread()
    {
        Assert.False(Engine.CanUndo);
        Assert.Null(Engine.UndoTitle);
        Assert.False(_services.Commands.CanExecute("refactor.undo"));
        var before = await ApplyToThreeFilesAsync();
        Assert.True(Engine.CanUndo);
        Assert.Equal(Title, Engine.UndoTitle);
        Assert.True(_services.Commands.CanExecute("refactor.undo"));
        Assert.Equal("&Undo Rename Symbol", _services.Commands.Get("refactor.undo").MenuLabel);
        Assert.NotEqual(before.CBytes, Bytes(C));
        var changed = 0;
        Engine.Changed += (_, _) => changed++;
        var reread = new List<string?>();
        _services.CodeModel.Workspace.Changed += (_, e) =>
        {
            if (e.Kind == FreexComp.LanguageService.WorkspaceChangeKind.FileChangedOnDisk)
                reread.Add(e.Path);
        };
        var calls = _recording.Calls.Count;

        await _services.Commands.ExecuteAsync("refactor.undo");

        // C has the bytes it had; A is unmodified again and has nothing to redo; B has its unsaved text from before.
        AssertRestored(before);
        Assert.False(before.A.Buffer.CanUndo); // text, undo stack and save point as before the operation
        before.B.Buffer.Undo();
        Assert.Equal(Unit("B"), before.B.Buffer.Text); // B's own step from before the operation is still there
        Assert.False(before.B.IsModified);
        Assert.Empty(Dialogs.Requests);

        // Only the closed file was written; the code model reads it again.
        Assert.DoesNotContain(_recording.Calls.Skip(calls), c => IsWrite(c) && (c.Contains(A, StringComparison.Ordinal) || c.Contains(B, StringComparison.Ordinal)));
        Assert.Equal(1, Replacements(calls, C));
        Assert.DoesNotContain(_fs.AllFiles, f => f.Contains(".~tmp", StringComparison.Ordinal));
        Assert.Equal(C, Assert.Single(reread), ignoreCase: true);
        Assert.Equal(before.CText, _services.CodeModel.Synchronize()!.GetSyntaxTree(C).Text.Text);
        Assert.Equal(2, _services.Documents.Documents.Count);

        // The operation left the history; there is no redo.
        Assert.False(Engine.CanUndo);
        Assert.Null(Engine.UndoTitle);
        Assert.False(Engine.IsBusy);
        Assert.True(changed > 0);
        Assert.False(_services.Commands.CanExecute("refactor.undo"));
        Assert.Null(_services.Commands.Get("refactor.undo").MenuLabel);
        Assert.Equal("Undid Rename Symbol in 3 files", _services.Status.Message);
        Assert.Equal(Format(Strings.Msg_Refactoring_UndoneMany, Title, 3), _services.Status.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine.UndoAsync());
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-008")]
    public Task FailureOnThirdFileRollsBackTheFirstTwo() => TestUiThread.RunAsync(() => FailureOnThirdFileRollsBackTheFirstTwoOnUiThread());

    private async Task FailureOnThirdFileRollsBackTheFirstTwoOnUiThread()
    {
        await OpenProjectAsync();
        Dialogs.Answer("Msg.Refactoring.ApplyFailed", "OK");
        var a = await OpenAsync(A);
        var edit = Edit(Rename(A, a.Buffer.Text), Rename(B, Text(B)), Rename(C, Text(C)), Rename(D, Text(D)));
        var before = Snapshot();
        var failures = 0;
        _recording.FailWhen = call => call.StartsWith("WriteAllBytesDurable " + D, StringComparison.Ordinal) && failures++ == 0; // the third file, once
        var calls = _recording.Calls.Count;

        var result = await Engine.ApplyAsync(edit, PreviewMode.Never);

        _recording.FailWhen = null;
        Assert.Equal(1, failures);
        Assert.Equal(RefactoringOutcome.Failed, result.Outcome);
        Assert.StartsWith("Injected failure: WriteAllBytesDurable " + D, result.Error, StringComparison.Ordinal);
        var request = Assert.Single(Dialogs.Requests);
        Assert.Equal("Msg.Refactoring.ApplyFailed", request.MessageId);
        Assert.Equal([Title, D, result.Error!], request.Args);

        // The first two files were written and got their bytes back; the third is untouched; no temporary file is left;
        // the buffer was never edited.
        Assert.Equal(2, Replacements(calls, B));
        Assert.Equal(2, Replacements(calls, C));
        Assert.Equal(0, Replacements(calls, D));
        Assert.Equal(before, Snapshot());
        Assert.Equal(Unit("A"), a.Buffer.Text);
        Assert.False(a.IsModified);
        Assert.False(Engine.IsBusy);

        // Without the failure the same edit goes through.
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.ApplyAsync(edit, PreviewMode.Never)).Outcome);
        Assert.Equal(Renamed(Unit("D")), Text(D));
        Assert.Equal(Renamed(Unit("A")), a.Buffer.Text);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-006")]
    public Task ReadOnlyOrExternallyModifiedFileBlocks() => TestUiThread.RunAsync(() => ReadOnlyOrExternallyModifiedFileBlocksOnUiThread());

    private async Task ReadOnlyOrExternallyModifiedFileBlocksOnUiThread()
    {
        await OpenProjectAsync();
        Dialogs.Answer("Msg.Refactoring.Blocked", "OK");
        var a = await OpenAsync(A);
        var b = await OpenAsync(B);

        // Every case: an edit over the open A, the closed D and the file of the case; only that file has a reason.
        async Task BlockedAsync(FileEdit blocked, BlockReason reason, string reasonText)
        {
            var edit = Edit(Rename(A, Unit("A")), blocked, Rename(D, Unit("D")));
            var before = Snapshot();
            var requests = Dialogs.Requests.Count;
            var status = _services.Status.Message;

            var result = await Engine.ApplyAsync(edit, PreviewMode.Never);

            Assert.Equal(RefactoringOutcome.Blocked, result.Outcome);
            Assert.Equal([new BlockedFile(blocked.Path, reason)], result.Blocked);
            var request = Assert.Single(Dialogs.Requests.Skip(requests));
            Assert.Equal("Msg.Refactoring.Blocked", request.MessageId);
            Assert.Equal([Title, $"{blocked.Path} — {reasonText}"], request.Args);
            Assert.Equal(before, Snapshot());
            Assert.Equal(status, _services.Status.Message);
            Assert.False(Engine.IsBusy);
        }

        // (1) A read-only closed file.
        _fs.SetReadOnly(C, true);
        await BlockedAsync(Rename(C, Unit("C")), BlockReason.NotWritable, "The file is read-only or in use.");
        _fs.SetReadOnly(C, false);

        // (2) A read-only buffer.
        b.Buffer.IsReadOnly = true;
        await BlockedAsync(Rename(B, Unit("B")), BlockReason.NotWritable, Strings.Reason_Refactoring_NotWritable);
        b.Buffer.IsReadOnly = false;

        // (3) A closed file whose text is not the base text any more.
        await BlockedAsync(Rename(C, Unit("C") + "// computed on an older text\n"), BlockReason.ChangedOnDisk, "The file was changed outside the IDE.");

        // (4) An open document that shows an external-change info bar.
        b.ShowInfoBar(new InfoBarViewModel(TextDocumentViewModel.ExternalChangeInfoBarKind, InfoBarSeverity.Warning, "changed", [], null));
        await BlockedAsync(Rename(B, Unit("B")), BlockReason.ChangedOnDisk, Strings.Reason_Refactoring_ChangedOnDisk);
        b.RemoveInfoBar(TextDocumentViewModel.ExternalChangeInfoBarKind);
        b.ShowInfoBar(new InfoBarViewModel(TextDocumentViewModel.ExternalDeleteInfoBarKind, InfoBarSeverity.Warning, "deleted", [], null));
        await BlockedAsync(Rename(B, Unit("B")), BlockReason.ChangedOnDisk, Strings.Reason_Refactoring_ChangedOnDisk);
        b.RemoveInfoBar(TextDocumentViewModel.ExternalDeleteInfoBarKind);

        // (5) A buffer edited since the edit was computed.
        var computed = Rename(B, b.Buffer.Text);
        b.Buffer.ApplyEdits([new TextEdit(0, 0, "// typed\n")]);
        await BlockedAsync(computed, BlockReason.ChangedSinceComputed, "The file was edited after the changes were computed.");
        b.Buffer.Undo();

        // (6) A missing file.
        await BlockedAsync(Rename(Dir + @"\Gone.pas", Unit("Gone")), BlockReason.NotFound, "The file does not exist.");

        // (7) Twelve blocked files: ten lines in path order, then the rest as a count.
        var missing = Enumerable.Range(1, 12).Select(i => Dir + $@"\M{i:00}.pas").ToList();
        var many = Edit([Rename(A, Unit("A")), .. missing.AsEnumerable().Reverse().Select(p => Rename(p, Unit("M")))]);
        var snapshot = Snapshot();
        var count = Dialogs.Requests.Count;
        var blockedResult = await Engine.ApplyAsync(many, PreviewMode.Never);
        Assert.Equal(RefactoringOutcome.Blocked, blockedResult.Outcome);
        Assert.Equal(missing.Select(p => new BlockedFile(p, BlockReason.NotFound)), blockedResult.Blocked);
        var lines = Assert.Single(Dialogs.Requests.Skip(count)).Args[1].Split('\n');
        Assert.Equal([.. missing.Take(10).Select(p => $"{p} — The file does not exist."), "… and 2 more files"], lines);
        Assert.Equal(snapshot, Snapshot());

        // Nothing is wrong with the three files themselves: the same edit without a blocked file applies.
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.ApplyAsync(Edit(Rename(A, Unit("A")), Rename(C, Unit("C")), Rename(D, Unit("D"))), PreviewMode.Never)).Outcome);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-005")]
    [Trait("Req", "IDE-REFAC-006")]
    public Task LibraryOrOutsideProjectFileBlocks() => TestUiThread.RunAsync(() => LibraryOrOutsideProjectFileBlocksOnUiThread());

    private async Task LibraryOrOutsideProjectFileBlocksOnUiThread()
    {
        const string LibraryUnit = @"G:\rtl\System.pas";
        const string LibraryMember = @"G:\rtl\Mine.pas";
        const string Outside = @"E:\other\Other.pas";
        const string OnSearchPath = @"D:\shared\Util.pas";
        const string Member = @"D:\ext\Ext.pas";
        const string MemberForm = @"D:\ext\Ext.form";
        _services.Settings.Set(SettingsCatalog.ToolchainRtlDir, @"G:\rtl");
        foreach (var path in (string[])[LibraryUnit, LibraryMember, Outside, OnSearchPath, Member])
            File(path, Unit(Path.GetFileNameWithoutExtension(path)));
        File(MemberForm, "object Ext: TForm\n  OnCreate = Old\nend\n");
        await OpenProjectAsync("""
            { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas", "searchPaths": ["D:/shared"],
              "files": [ { "path": "App.pas" }, { "path": "A.pas" }, { "path": "B.pas" }, { "path": "C.pas" }, { "path": "D.pas" },
                         { "path": "D:/ext/Ext.pas", "form": "D:/ext/Ext.form" }, { "path": "G:/rtl/Mine.pas" } ] }
            """);
        Dialogs.Answer("Msg.Refactoring.Blocked", "OK");

        async Task BlockedAsync(string path, BlockReason reason, string reasonText)
        {
            var before = Snapshot();
            var requests = Dialogs.Requests.Count;
            var result = await Engine.ApplyAsync(Edit(Rename(C, Text(C)), Rename(path, Text(path))), PreviewMode.Never);
            Assert.Equal(RefactoringOutcome.Blocked, result.Outcome);
            Assert.Equal([new BlockedFile(path, reason)], result.Blocked);
            Assert.Equal([Title, $"{path} — {reasonText}"], Assert.Single(Dialogs.Requests.Skip(requests)).Args);
            Assert.Equal(before, Snapshot());
        }

        // A library source that is not a member, and a file outside the project folder and the search paths.
        await BlockedAsync(LibraryUnit, BlockReason.LibrarySource, "The file is a library source.");
        await BlockedAsync(Outside, BlockReason.OutsideProject, "The file is outside the project.");

        // A file under a search path, a member outside the project folder, its companion form and a member that lies
        // under a library root may be edited.
        string[] allowed = [OnSearchPath, Member, MemberForm, LibraryMember];
        var texts = allowed.ToDictionary(p => p, Text);
        var applied = await Engine.ApplyAsync(Edit([.. allowed.Select(p => Rename(p, texts[p]))]), PreviewMode.Never);
        Assert.Equal(RefactoringOutcome.Applied, applied.Outcome);
        Assert.Equal(4, applied.FileCount);
        Assert.All(allowed, p => Assert.Equal(Renamed(texts[p]), Text(p)));

        // Without a project only files that are open as documents pass — and never a protected library source.
        Assert.True(await _services.Projects.CloseAsync());
        Assert.Null(_services.Projects.Project);
        var a = await OpenAsync(A);
        var library = await OpenAsync(LibraryUnit);
        Assert.True(library.IsLibraryProtected);
        var before = Snapshot();
        var closed = await Engine.ApplyAsync(Edit(Rename(A, a.Buffer.Text), Rename(C, Text(C))), PreviewMode.Never);
        Assert.Equal([new BlockedFile(C, BlockReason.OutsideProject)], closed.Blocked);
        Assert.Equal([Title, $"{C} — {Strings.Reason_Refactoring_OutsideProject}"], Dialogs.Requests[^1].Args);
        var blocked = await Engine.ApplyAsync(Edit(Rename(A, a.Buffer.Text), Rename(LibraryUnit, library.Buffer.Text)), PreviewMode.Never);
        Assert.Equal([new BlockedFile(LibraryUnit, BlockReason.LibrarySource)], blocked.Blocked);
        Assert.Equal(before, Snapshot());
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.ApplyAsync(Edit(Rename(A, a.Buffer.Text)), PreviewMode.Never)).Outcome);
        Assert.Equal(Renamed(Unit("A")), a.Buffer.Text);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-008")]
    public Task BufferChangedWhileFilesAreWrittenRollsBack() => TestUiThread.RunAsync(() => BufferChangedWhileFilesAreWrittenRollsBackOnUiThread());

    private async Task BufferChangedWhileFilesAreWrittenRollsBackOnUiThread()
    {
        await OpenProjectAsync();
        Dialogs.Answer("Msg.Refactoring.Blocked", "OK");
        var a = await OpenAsync(A);
        var edit = Edit(Rename(A, a.Buffer.Text), Rename(C, Text(C)));
        var original = Bytes(C);
        // While the closed file is written the user types in the open buffer.
        DuringFirstWriteOf(C, () => a.Buffer.ApplyEdits([new TextEdit(0, 0, "// typed meanwhile\n")]));
        var calls = _recording.Calls.Count;

        var result = await Engine.ApplyAsync(edit, PreviewMode.Never);

        _recording.FailWhen = null;
        Assert.Equal(RefactoringOutcome.Blocked, result.Outcome);
        Assert.Equal([new BlockedFile(A, BlockReason.ChangedSinceComputed)], result.Blocked);
        var request = Assert.Single(Dialogs.Requests);
        Assert.Equal("Msg.Refactoring.Blocked", request.MessageId);
        Assert.Equal([Title, $"{A} — {Strings.Reason_Refactoring_ChangedSinceComputed}"], request.Args);

        // The closed file was written and has its original bytes again; the buffer keeps the user's edit and got none of
        // the operation's.
        Assert.Equal(2, Replacements(calls, C));
        Assert.Equal(original, Bytes(C));
        Assert.Equal("// typed meanwhile\n" + Unit("A"), a.Buffer.Text);
        Assert.DoesNotContain(_fs.AllFiles, f => f.Contains(".~tmp", StringComparison.Ordinal));
        Assert.False(Engine.IsBusy);

        // A document closed while the files are written counts as changed: nothing is edited in a buffer nobody sees.
        var b = await OpenAsync(B);
        var files = FilesSnapshot();
        Task<bool>? closing = null;
        DuringFirstWriteOf(D, () => closing = _services.Documents.CloseAsync(b));
        result = await Engine.ApplyAsync(Edit(Rename(B, b.Buffer.Text), Rename(D, Text(D))), PreviewMode.Never);
        _recording.FailWhen = null;
        Assert.True(await closing!);
        Assert.Equal(RefactoringOutcome.Blocked, result.Outcome);
        Assert.Equal([new BlockedFile(B, BlockReason.ChangedSinceComputed)], result.Blocked);
        Assert.Equal(files, FilesSnapshot());
        Assert.Equal(Unit("B"), b.Buffer.Text);
    }

    /// <summary>
    /// IDE-REFAC-008: an unexpected failure while a buffer is edited — here a buffer that became read only during the
    /// writes — undoes the buffers already edited, puts the files back and is reported as an edit that cannot be applied.
    /// </summary>
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-008")]
    public Task BufferEditFailureUndoesBuffersAndRestoresFiles() => TestUiThread.RunAsync(() => BufferEditFailureUndoesBuffersAndRestoresFilesOnUiThread());

    private async Task BufferEditFailureUndoesBuffersAndRestoresFilesOnUiThread()
    {
        await OpenProjectAsync();
        Dialogs.Answer("Msg.Refactoring.Failed", "OK");
        var a = await OpenAsync(A);
        var b = await OpenAsync(B);
        var edit = Edit(Rename(A, a.Buffer.Text), Rename(B, b.Buffer.Text), Rename(C, Text(C)));
        var files = FilesSnapshot();
        DuringFirstWriteOf(C, () => b.Buffer.IsReadOnly = true); // no change of the text: the version check passes
        var logged = Log.Lines.Count;
        var calls = _recording.Calls.Count;

        var result = await Engine.ApplyAsync(edit, PreviewMode.Never);

        _recording.FailWhen = null;
        Assert.Equal(RefactoringOutcome.Failed, result.Outcome);
        var request = Assert.Single(Dialogs.Requests);
        Assert.Equal("Msg.Refactoring.Failed", request.MessageId);
        Assert.Equal([Title], request.Args);
        Assert.Contains(B, Assert.Single(Log.Lines.Skip(logged), l => l.StartsWith("ERROR Refactoring: ", StringComparison.Ordinal)), StringComparison.Ordinal);

        // A was edited before B failed: its undo step is undone and cannot be redone. The written file has its bytes back.
        Assert.Equal(Unit("A"), a.Buffer.Text);
        Assert.False(a.IsModified);
        Assert.False(a.Buffer.CanRedo);
        Assert.False(Engine.CanUndo);
        Assert.Equal(Unit("B"), b.Buffer.Text);
        Assert.Equal(2, Replacements(calls, C));
        Assert.Equal(files, FilesSnapshot());
        Assert.False(Engine.IsBusy);
    }

    /// <summary>IDE-REFAC-008: a file that cannot be put back after a failed write is logged and named by <c>Msg.Refactoring.RestoreFailed</c>.</summary>
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-008")]
    public Task FileThatCannotBeRestoredIsNamed() => TestUiThread.RunAsync(() => FileThatCannotBeRestoredIsNamedOnUiThread());

    private async Task FileThatCannotBeRestoredIsNamedOnUiThread()
    {
        await OpenProjectAsync();
        Dialogs.Answer("Msg.Refactoring.RestoreFailed", "OK");
        var edit = Edit(Rename(B, Text(B)), Rename(C, Text(C)), Rename(D, Text(D)));
        var writesOfB = 0;
        // The third write fails, and so does the write that puts the first file back.
        _recording.FailWhen = call => call.StartsWith("WriteAllBytesDurable " + D, StringComparison.Ordinal)
            || (call.StartsWith("WriteAllBytesDurable " + B, StringComparison.Ordinal) && writesOfB++ == 1);
        var logged = Log.Lines.Count;

        var result = await Engine.ApplyAsync(edit, PreviewMode.Never);

        _recording.FailWhen = null;
        Assert.Equal(RefactoringOutcome.Failed, result.Outcome);
        var request = Assert.Single(Dialogs.Requests);
        Assert.Equal("Msg.Refactoring.RestoreFailed", request.MessageId);
        Assert.Equal([Title, B], request.Args.Take(2));
        Assert.StartsWith("Injected failure: WriteAllBytesDurable " + B, request.Args[2], StringComparison.Ordinal);
        Assert.Single(Log.Lines.Skip(logged), l => l.StartsWith("WARN Refactoring: " + B, StringComparison.Ordinal));

        // The second file is back, the third was never written; the first keeps the new text — that is what the message says.
        Assert.Equal(Unit("C"), Text(C));
        Assert.Equal(Unit("D"), Text(D));
        Assert.Equal(Renamed(Unit("B")), Text(B));
        Assert.DoesNotContain(_fs.AllFiles, f => f.Contains(".~tmp", StringComparison.Ordinal));
        Assert.False(Engine.IsBusy);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-009")]
    public Task EncodingsOfClosedFilesAreKept() => TestUiThread.RunAsync(() => EncodingsOfClosedFilesAreKeptOnUiThread());

    private async Task EncodingsOfClosedFilesAreKeptOnUiThread()
    {
        await OpenProjectAsync();
        // One edit in the middle of each file; the texts hold characters outside ASCII, the last file every kind of line break.
        const string Head = "unit X;\r\n// Grüße, 日本\r\nprocedure ";
        const string Tail = ";\r\n// ende — ✓\r\nend.\r\n";
        const string MixedHead = "unit X;\n// lf\r\n// crlf\r// cr\nprocedure ";
        const string MixedTail = ";\n\nend.";
        var utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        var latin = Encoding.Latin1; // the same bytes as Windows-1252 for the characters used here
        (string Path, byte[] Preamble, Encoding Encoding, string Head, string Tail)[] cases =
        [
            (Dir + @"\Utf8.pas", [], Encoding.UTF8, Head, Tail),
            (Dir + @"\Utf8Bom.pas", Utf8Bom, Encoding.UTF8, Head, Tail),
            (Dir + @"\Utf16.pas", [0xFF, 0xFE], utf16, Head, Tail),
            (Dir + @"\Lf.pas", [], Encoding.UTF8, MixedHead, MixedTail),
            (Dir + @"\Ansi.pas", [], latin, "unit X;\r\n// caf\u00E9 \u00FC\r\nprocedure ", ";\r\nend.\r\n"), // not valid UTF-8: read as Windows-1252
        ];
        var files = new List<FileEdit>();
        foreach (var (path, preamble, encoding, head, tail) in cases)
        {
            Write(path, [.. preamble, .. encoding.GetBytes(head + "Old" + tail)]);
            files.Add(new FileEdit(path, head + "Old" + tail, [new TextEdit(head.Length, 3, "Neu\u00E9\r\n")]));
        }

        var result = await Engine.ApplyAsync(Edit([.. files]), PreviewMode.Never);

        Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
        Assert.Equal(5, result.FileCount);
        foreach (var (path, preamble, encoding, head, tail) in cases)
        {
            // Every byte outside the edited range is the byte that was there: preamble, head and tail.
            byte[] expected = [.. preamble, .. encoding.GetBytes(head), .. encoding.GetBytes("Neu\u00E9\r\n"), .. encoding.GetBytes(tail)];
            Assert.True(expected.AsSpan().SequenceEqual(Bytes(path)), path);
        }

        // A character the file's encoding cannot store: no file is written (the UTF-8 file of the same edit neither), the
        // file and the reason are named.
        Dialogs.Answer("Msg.Refactoring.ApplyFailed", "OK");
        var (ansi, utf8) = (Dir + @"\Ansi.pas", Dir + @"\Utf8.pas");
        var before = FilesSnapshot();
        var failed = await Engine.ApplyAsync(Edit(new FileEdit(utf8, Text(utf8), [new TextEdit(0, 0, "\u65E5")]),
            new FileEdit(ansi, Text(ansi), [new TextEdit(0, 0, "\u65E5")])), PreviewMode.Never);
        Assert.Equal(RefactoringOutcome.Failed, failed.Outcome);
        var request = Assert.Single(Dialogs.Requests);
        Assert.Equal("Msg.Refactoring.ApplyFailed", request.MessageId);
        Assert.Equal([Title, ansi, failed.Error!], request.Args);
        Assert.Contains("Windows-1252", failed.Error, StringComparison.Ordinal);
        Assert.Equal(before, FilesSnapshot());
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-002")]
    public Task InvalidEditFailsWithoutChanges() => TestUiThread.RunAsync(() => InvalidEditFailsWithoutChangesOnUiThread());

    private async Task InvalidEditFailsWithoutChangesOnUiThread()
    {
        await OpenProjectAsync();
        Dialogs.Answer("Msg.Refactoring.Failed", "OK");
        var a = await OpenAsync(A);
        var aText = a.Buffer.Text;
        var cText = Text(C);

        async Task FailsAsync(string offendingPath, params FileEdit[] files)
        {
            var before = Snapshot();
            var requests = Dialogs.Requests.Count;
            var logged = Log.Lines.Count;
            var calls = _recording.Calls.Count;

            var result = await Engine.ApplyAsync(Edit(files), PreviewMode.Never);

            Assert.Equal(RefactoringOutcome.Failed, result.Outcome);
            Assert.Empty(result.Blocked);
            var request = Assert.Single(Dialogs.Requests.Skip(requests));
            Assert.Equal("Msg.Refactoring.Failed", request.MessageId);
            Assert.Equal([Title], request.Args);
            var line = Assert.Single(Log.Lines.Skip(logged), l => l.StartsWith("ERROR ", StringComparison.Ordinal));
            Assert.StartsWith("ERROR Refactoring: ", line, StringComparison.Ordinal);
            Assert.Contains(Title, line, StringComparison.Ordinal);
            Assert.Contains(offendingPath, line, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, Snapshot());
            Assert.DoesNotContain(_recording.Calls.Skip(calls), IsWrite);
            Assert.False(Engine.IsBusy);
        }

        // Overlapping edits (a valid file edit comes first: the log names the offending file).
        await FailsAsync(C, Rename(A, aText), new FileEdit(C, cText, [new TextEdit(0, 5, "x"), new TextEdit(3, 5, "y")]));

        // Two edits at one offset: an insertion at the start of a replaced range, and two insertions.
        await FailsAsync(A, new FileEdit(A, aText, [new TextEdit(5, 2, "x"), new TextEdit(5, 0, "y")]));
        await FailsAsync(A, new FileEdit(A, aText, [new TextEdit(5, 0, "x"), new TextEdit(5, 0, "y")]));

        // An edit beyond the base text; a negative offset.
        await FailsAsync(C, new FileEdit(C, cText, [new TextEdit(cText.Length - 1, 2, "x")]));
        await FailsAsync(C, new FileEdit(C, cText, [new TextEdit(-1, 1, "x")]));

        // A path listed twice (compared ignoring case).
        await FailsAsync(C, new FileEdit(C, cText, [new TextEdit(0, 0, "x")]), new FileEdit(C.ToUpperInvariant(), cText, [new TextEdit(4, 0, "y")]));

        // An insertion at the end of a replaced range is valid.
        var valid = await Engine.ApplyAsync(Edit(new FileEdit(A, aText, [new TextEdit(0, 4, "UNIT"), new TextEdit(4, 0, " ")])), PreviewMode.Never);
        Assert.Equal(RefactoringOutcome.Applied, valid.Outcome);
        Assert.StartsWith("UNIT  A;", a.Buffer.Text, StringComparison.Ordinal);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-001")]
    public Task NoOpEditsAreDroppedAndEmptyEditReportsNoChanges() => TestUiThread.RunAsync(() => NoOpEditsAreDroppedAndEmptyEditReportsNoChangesOnUiThread());

    private async Task NoOpEditsAreDroppedAndEmptyEditReportsNoChangesOnUiThread()
    {
        await OpenProjectAsync();
        var a = await OpenAsync(A);
        var b = await OpenAsync(B);
        var aText = a.Buffer.Text;
        var first = aText.IndexOf("Old", StringComparison.Ordinal);
        var second = aText.IndexOf("Old", first + 3, StringComparison.Ordinal);
        var bFirst = b.Buffer.Text.IndexOf("Old", StringComparison.Ordinal);

        // A: one edit replaces a text by the same text and an empty insertion sits at the offset of the real edit; B has
        // only edits that change nothing; C has no edit; D is missing but has nothing to change. One edit in one file is left.
        var mixed = Edit(
            new FileEdit(A, aText, [new TextEdit(first, 3, "Old"), new TextEdit(second, 0, ""), new TextEdit(second, 3, "New")]),
            new FileEdit(B, b.Buffer.Text, [new TextEdit(bFirst, 3, "Old"), new TextEdit(0, 0, "")]),
            new FileEdit(C, Text(C), []),
            new FileEdit(Dir + @"\Gone.pas", "", [new TextEdit(0, 0, "")]));
        var bVersion = b.Buffer.Version;
        var files = FilesSnapshot();

        var result = await Engine.ApplyAsync(mixed, PreviewMode.Never);

        Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
        Assert.Equal((1, 1), (result.EditCount, result.FileCount));
        Assert.Equal(string.Concat(aText.AsSpan(0, second), "New", aText.AsSpan(second + 3)), a.Buffer.Text);
        Assert.Equal(bVersion, b.Buffer.Version);
        Assert.False(b.IsModified);
        Assert.Equal(files, FilesSnapshot());
        Assert.Equal("Rename Symbol: 1 change", _services.Status.Message);
        Assert.Equal(Format(Strings.Msg_Refactoring_AppliedOne, Title), _services.Status.Message);

        // Several edits in one file.
        var two = await Engine.ApplyAsync(Edit(Rename(B, b.Buffer.Text)), PreviewMode.Never);
        Assert.Equal((2, 1), (two.EditCount, two.FileCount));
        Assert.Equal("Rename Symbol: 2 changes in 1 file", _services.Status.Message);
        Assert.Equal(Format(Strings.Msg_Refactoring_AppliedOneFile, Title, 2), _services.Status.Message);

        // Nothing but edits that change nothing, and an edit without files: no changes, said in the status bar only.
        var before = Snapshot();
        foreach (var empty in (WorkspaceEdit[])[
            Edit(new FileEdit(A, a.Buffer.Text, [new TextEdit(0, 4, "unit")]), new FileEdit(C, Text(C), [])),
            Edit()])
        {
            _services.Status.ShowMessage("before", TimeSpan.FromSeconds(1));
            var none = await Engine.ApplyAsync(empty, PreviewMode.Never);
            Assert.Equal((RefactoringOutcome.NoChanges, 0, 0, null), (none.Outcome, none.EditCount, none.FileCount, none.Error));
            Assert.Equal(2, HistoryCount); // an operation that changes nothing is not recorded
            Assert.Empty(none.Blocked);
            Assert.Equal("Rename Symbol: nothing to change", _services.Status.Message);
            Assert.Equal(Format(Strings.Msg_Refactoring_NoChanges, Title), _services.Status.Message);
            Assert.Equal(before, Snapshot());
        }

        Assert.Empty(Dialogs.Requests);
        Assert.False(Engine.IsBusy);
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-007")]
    public Task PreviewDecision() => TestUiThread.RunAsync(() => PreviewDecisionOnUiThread());

    private async Task PreviewDecisionOnUiThread()
    {
        await OpenProjectAsync();
        Dialogs.Answer("Msg.Refactoring.Blocked", "OK");
        var b = await OpenAsync(B);
        var a = await OpenAsync(A);
        Assert.Same(a, _services.Documents.ActiveDocument);
        var service = Assert.IsType<RefactoringService>(Engine);

        // The preview is the engine's hook (the dialog comes with I-350): it records what it was shown and answers.
        var shown = new List<WorkspaceEdit>();
        Func<WorkspaceEdit, WorkspaceEdit?> answer = edit => edit;
        service.Preview = edit =>
        {
            shown.Add(edit);
            return Task.FromResult(answer(edit));
        };

        async Task<bool> PreviewedAsync(PreviewMode mode, params string[] paths)
        {
            shown.Clear();
            var result = await Engine.ApplyAsync(Edit([.. paths.Select(Insert)]), mode);
            Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
            Assert.Equal(paths.Length, result.FileCount);
            Assert.True(shown.Count <= 1);
            return shown.Count == 1;
        }

        // Auto: no preview for a change confined to the buffer of the active document's visible text view …
        Assert.False(await PreviewedAsync(PreviewMode.Auto, A));
        // … a preview for two files, for a closed file and for an open file that is not the active one.
        Assert.True(await PreviewedAsync(PreviewMode.Auto, A, B));
        Assert.True(await PreviewedAsync(PreviewMode.Auto, C));
        Assert.True(await PreviewedAsync(PreviewMode.Auto, B));
        _services.Documents.Activate(b);
        Assert.False(await PreviewedAsync(PreviewMode.Auto, B));
        Assert.True(await PreviewedAsync(PreviewMode.Auto, A));

        // Always and Never do not look at the files.
        Assert.True(await PreviewedAsync(PreviewMode.Always, B));
        Assert.False(await PreviewedAsync(PreviewMode.Never, A, B, C));

        // The preview gets the normalised edit; what it returns is applied — here only the change of A.
        answer = edit => edit with { Files = [.. edit.Files.Where(f => f.Path == A)] };
        shown.Clear();
        var files = FilesSnapshot();
        var aText = a.Buffer.Text;
        var reduced = await Engine.ApplyAsync(Edit(Insert(A), Insert(C), new FileEdit(D, Text(D), [])), PreviewMode.Always);
        Assert.Equal([A, C], Assert.Single(shown).Files.Select(f => f.Path));
        Assert.Equal((RefactoringOutcome.Applied, 1, 1), (reduced.Outcome, reduced.EditCount, reduced.FileCount));
        Assert.Equal("{x}" + aText, a.Buffer.Text);
        Assert.Equal(files, FilesSnapshot());

        // A cancelled preview: nothing changes and nothing is said.
        answer = _ => null;
        shown.Clear();
        var before = Snapshot();
        _services.Status.ShowMessage("before", TimeSpan.FromSeconds(1));
        var canceled = await Engine.ApplyAsync(Edit(Insert(A), Insert(C)), PreviewMode.Always);
        Assert.Single(shown);
        Assert.Equal((RefactoringOutcome.Canceled, 0, 0, null), (canceled.Outcome, canceled.EditCount, canceled.FileCount, canceled.Error));
        Assert.Empty(canceled.Blocked);
        Assert.Equal(before, Snapshot());
        Assert.Equal("before", _services.Status.Message);
        Assert.False(Engine.IsBusy);

        // A token cancelled before the first write: the same.
        var stopped = await Engine.ApplyAsync(Edit(Insert(A), Insert(C)), PreviewMode.Never, new CancellationToken(canceled: true));
        Assert.Equal(RefactoringOutcome.Canceled, stopped.Outcome);
        Assert.Equal(before, Snapshot());
        Assert.Equal("before", _services.Status.Message);

        // A blocked operation never shows a preview.
        shown.Clear();
        Assert.Equal(RefactoringOutcome.Blocked, (await Engine.ApplyAsync(Edit(Insert(A), Rename(Dir + @"\Gone.pas", Unit("Gone"))), PreviewMode.Always)).Outcome);
        Assert.Empty(shown);

        // Without a preview (before the dialog exists) Auto and Always apply directly.
        service.Preview = null;
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.ApplyAsync(Edit(Insert(A), Insert(C)), PreviewMode.Always)).Outcome);
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.ApplyAsync(Edit(Insert(C)), PreviewMode.Auto)).Outcome);
        Assert.Single(Dialogs.Requests); // the blocked operation's message only
    }

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-012")]
    public Task OneOperationAtATime() => TestUiThread.RunAsync(() => OneOperationAtATimeOnUiThread());

    private async Task OneOperationAtATimeOnUiThread()
    {
        await OpenProjectAsync();
        var a = await OpenAsync(A);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new ManualResetEventSlim();
        using var release = new Releaser(gate);
        // The apply is held in its write step: the write of the closed file waits for the gate.
        _recording.FailWhen = call =>
        {
            if (call.StartsWith("WriteAllBytesDurable " + C, StringComparison.Ordinal))
            {
                entered.TrySetResult();
                gate.Wait();
            }

            return false;
        };
        var changes = new List<bool>();
        Engine.Changed += (_, _) => changes.Add(Engine.IsBusy);
        _services.Status.ShowMessage("before", TimeSpan.FromMinutes(1));

        Assert.False(Engine.IsBusy);
        var running = Engine.ApplyAsync(Edit(Insert(A), Insert(C)), PreviewMode.Never);
        Assert.True(Engine.IsBusy);
        await entered.Task;

        // Busy: a second operation is refused, whatever it is.
        Assert.True(Engine.IsBusy);
        Assert.False(running.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine.ApplyAsync(Edit(Insert(D)), PreviewMode.Never));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine.ApplyAsync(Edit(), PreviewMode.Never));
        Assert.True(Engine.IsBusy);
        Assert.Equal(Unit("D"), Text(D));

        // After 400 ms the status bar names the running operation; the text stays until the result.
        _clock.Advance(TimeSpan.FromMilliseconds(399));
        Assert.Equal("before", _services.Status.Message);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("Rename Symbol…", _services.Status.Message);
        Assert.Equal(Format(Strings.Msg_Refactoring_Working, Title), _services.Status.Message);
        _clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("Rename Symbol…", _services.Status.Message);
        Assert.Equal(Unit("A"), a.Buffer.Text); // buffers are edited after the files

        gate.Set();
        var result = await running;

        Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
        Assert.False(Engine.IsBusy);
        Assert.Equal("Rename Symbol: 2 changes in 2 files", _services.Status.Message);
        Assert.Equal([true, false], changes);
        Assert.Equal("{x}" + Unit("A"), a.Buffer.Text);

        // An operation that ends quickly never shows the text, and its timer is gone with it.
        _recording.FailWhen = null;
        var pending = _clock.PendingCount;
        Assert.Equal(RefactoringOutcome.Applied, (await Engine.ApplyAsync(Edit(Insert(D)), PreviewMode.Never)).Outcome);
        Assert.Equal(pending, _clock.PendingCount);
        Assert.Equal("Rename Symbol: 1 change", _services.Status.Message);
    }
}
