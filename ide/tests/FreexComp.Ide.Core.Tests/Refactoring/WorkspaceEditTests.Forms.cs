using System.Text;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Designer;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.Tests.Refactoring;

/// <summary>
/// I-348 (F13-003): the <c>.form</c> of an open form document is a form-text target — judged by the designer's current
/// form text and edited through the designer — and language-service edits become a workspace edit with their base texts.
/// </summary>
public sealed partial class WorkspaceEditTests
{
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-004")]
    [Trait("Req", "IDE-REFAC-006")]
    [Trait("Req", "IDE-REFAC-011")]
    public Task FormTextOfOpenFormDocument() => TestUiThread.RunAsync(() => FormTextOfOpenFormDocumentOnUiThread());

    private async Task FormTextOfOpenFormDocumentOnUiThread()
    {
        await FormFilesAsync();
        Dialogs.Answer("Msg.Refactoring.Blocked", "OK");

        // A .form whose form document is not open is a file target: the file is written and nothing is opened.
        var closed = await Engine.ApplyAsync(Edit(Handler(FormPath, Form, "ClosedClick")), PreviewMode.Never);
        Assert.Equal(RefactoringOutcome.Applied, closed.Outcome);
        Assert.Equal(WithHandler(Form, "ClosedClick"), Text(FormPath));
        Assert.Empty(_services.Documents.Documents);
        File(FormPath, Form);

        var document = await OpenFormAsync();
        var designer = document.Designer;
        Assert.Equal(Form, designer.CurrentFormText());
        Assert.Equal("GreetButtonClick", OnClick(document));
        DesignerFixture.Select(designer, "GreetButton");

        async Task BlockedAsync(FileEdit edit, BlockReason reason, string reasonText)
        {
            var files = FilesSnapshot();
            var text = designer.CurrentFormText();
            var (model, version, unit) = (designer.Model, document.FormBuffer.Version, document.Buffer.Version);
            var requests = Dialogs.Requests.Count;

            var result = await Engine.ApplyAsync(Edit(edit, Rename(C, Text(C))), PreviewMode.Never);

            Assert.Equal(RefactoringOutcome.Blocked, result.Outcome);
            Assert.Equal([new BlockedFile(FormPath, reason)], result.Blocked);
            var request = Assert.Single(Dialogs.Requests.Skip(requests));
            Assert.Equal("Msg.Refactoring.Blocked", request.MessageId);
            Assert.Equal([Title, $"{FormPath} — {reasonText}"], request.Args);
            // Nothing changed: no file, not the form buffer, not the unit, and the designer still has its model.
            Assert.Equal(files, FilesSnapshot());
            Assert.Equal(text, designer.CurrentFormText());
            Assert.Same(model, designer.Model);
            Assert.Equal((version, unit), (document.FormBuffer.Version, document.Buffer.Version));
        }

        // A read-only form document — the document itself (its unit cannot be written, a library source) or the form buffer.
        document.SetReadOnly(true);
        await BlockedAsync(Handler(FormPath, Form, "GreetClick"), BlockReason.NotWritable, Strings.Reason_Refactoring_NotWritable);
        document.SetReadOnly(false);
        document.FormText.SetReadOnly(true);
        await BlockedAsync(Handler(FormPath, Form, "GreetClick"), BlockReason.NotWritable, Strings.Reason_Refactoring_NotWritable);
        document.FormText.SetReadOnly(false);

        // The document shows that one of its files changed outside the IDE.
        document.ShowInfoBar(new InfoBarViewModel(TextDocumentViewModel.ExternalChangeInfoBarKind, InfoBarSeverity.Warning, "changed", [], null));
        await BlockedAsync(Handler(FormPath, Form, "GreetClick"), BlockReason.ChangedOnDisk, Strings.Reason_Refactoring_ChangedOnDisk);
        document.RemoveInfoBar(TextDocumentViewModel.ExternalChangeInfoBarKind);
        Assert.False(document.IsModified);

        // The Design view, nothing unsaved: the designer receives the new text. The .form is neither a document of the
        // list nor a file target: nothing is written.
        var before = FilesSnapshot();
        var calls = _recording.Calls.Count;
        var unitVersion = document.Buffer.Version;

        var result = await Engine.ApplyAsync(Edit(Handler(FormPath, Form, "GreetClick")), PreviewMode.Never);

        Assert.Equal((RefactoringOutcome.Applied, 1, 1, null), (result.Outcome, result.EditCount, result.FileCount, result.Error));
        Assert.Empty(result.Blocked);
        Assert.Equal(WithHandler(Form, "GreetClick"), designer.CurrentFormText());
        Assert.Equal(WithHandler(Form, "GreetClick"), document.FormBuffer.Text);
        Assert.Equal("GreetClick", OnClick(document));
        Assert.True(document.IsModified);
        Assert.False(document.IsUnitModified);
        Assert.Equal(unitVersion, document.Buffer.Version);
        Assert.Equal(before, FilesSnapshot());
        Assert.DoesNotContain(_recording.Calls.Skip(calls), IsWrite);
        Assert.Equal(Format(Strings.Msg_Refactoring_AppliedOne, Title), _services.Status.Message);
        // The mechanism of a form-text revert (IDE-HIST-020): the view stays, the selection is kept by name, the designer's
        // own undo stack is empty, and the form buffer has the step.
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.Equal("GreetButton", designer.Surface!.Selection.Primary.Name);
        Assert.False(designer.Undo.CanUndo);
        Assert.True(document.FormBuffer.CanUndo);
        Assert.Same(document, Assert.Single(_services.Documents.Documents));

        // Unsaved designer changes: the current form text is not the text on disk any more, so an edit computed against
        // the file (what the language service reads) is blocked.
        SetCaption(document, "Hello");
        Assert.Contains("Caption = 'Hello'", designer.CurrentFormText(), StringComparison.Ordinal);
        await BlockedAsync(Handler(FormPath, Text(FormPath), "OtherClick"), BlockReason.ChangedSinceComputed, Strings.Reason_Refactoring_ChangedSinceComputed);
        Assert.True(designer.Undo.CanUndo);
        Assert.Equal("GreetClick", OnClick(document));

        // An edit computed against the current form text applies, unsaved changes or not.
        var current = designer.CurrentFormText();
        var last = await Engine.ApplyAsync(Edit(Handler(FormPath, current, "LastClick")), PreviewMode.Never);
        Assert.Equal(RefactoringOutcome.Applied, last.Outcome);
        Assert.Equal("LastClick", OnClick(document));
        Assert.Contains("Caption = 'Hello'", designer.CurrentFormText(), StringComparison.Ordinal);
        Assert.Equal(Form, Text(FormPath));
    }

    /// <summary>
    /// IDE-REFAC-004: a <c>.form</c> opened without its unit is a document with the form's path and an empty placeholder
    /// buffer — still a form-text target; without a project it is a file that is open as a document (IDE-REFAC-005). In the
    /// Text view the form buffer gets the text and the designer reads it when the view is left (IDE-REFAC-011).
    /// </summary>
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-004")]
    [Trait("Req", "IDE-REFAC-005")]
    [Trait("Req", "IDE-REFAC-011")]
    public Task FormOpenedWithoutItsUnitIsAFormTextTarget() => TestUiThread.RunAsync(() => FormOpenedWithoutItsUnitIsAFormTextTargetOnUiThread());

    private async Task FormOpenedWithoutItsUnitIsAFormTextTargetOnUiThread()
    {
        await FormFilesAsync(project: false);
        _fs.DeleteFile(FormUnitPath);
        var document = await OpenFormAsync(FormPath);
        Assert.False(document.HasUnit);
        Assert.Equal(FormPath, document.FilePath);
        Assert.Equal("", document.Buffer.Text);
        var placeholder = document.Buffer.Version;
        var files = FilesSnapshot();

        var result = await Engine.ApplyAsync(Edit(Handler(FormPath, Form, "GreetClick")), PreviewMode.Never);

        Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
        Assert.Empty(Dialogs.Requests);
        Assert.Equal("GreetClick", OnClick(document));
        Assert.Equal(WithHandler(Form, "GreetClick"), document.Designer.CurrentFormText());
        Assert.Equal(("", placeholder), (document.Buffer.Text, document.Buffer.Version));
        Assert.True(document.IsModified);
        Assert.Equal(files, FilesSnapshot());

        // The Text view: the text the user sees is the current form text; the model follows when the view is left.
        Assert.True(document.ShowView(DocumentView.Text));
        var shown = document.FormBuffer.Text;
        result = await Engine.ApplyAsync(Edit(Handler(FormPath, shown, "TextClick")), PreviewMode.Never);
        Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
        Assert.Equal(DocumentView.Text, document.ActiveView);
        Assert.Equal(WithHandler(Form, "TextClick"), document.FormBuffer.Text);
        Assert.Equal("GreetClick", OnClick(document));
        Assert.True(document.ShowView(DocumentView.Design));
        Assert.Equal("TextClick", OnClick(document));
        Assert.Equal(files, FilesSnapshot());
    }

    /// <summary>
    /// IDE-REFAC-008 for a form-text target: a form changed in the designer while the closed files are written blocks and
    /// the files get their bytes back; a form already edited when a later buffer fails gets back the text it had.
    /// </summary>
    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-REFAC-008")]
    [Trait("Req", "IDE-REFAC-011")]
    public Task FormTextChangedWhileFilesAreWrittenRollsBack() => TestUiThread.RunAsync(() => FormTextChangedWhileFilesAreWrittenRollsBackOnUiThread());

    private async Task FormTextChangedWhileFilesAreWrittenRollsBackOnUiThread()
    {
        File(Z, Unit("Z"));
        await FormFilesAsync();
        Dialogs.Answer("Msg.Refactoring.Blocked", "OK");
        Dialogs.Answer("Msg.Refactoring.Failed", "OK");
        var document = await OpenFormAsync();
        var designer = document.Designer;
        var files = FilesSnapshot();
        var calls = _recording.Calls.Count;

        // While the closed file is written the user changes the form in the designer.
        DuringFirstWriteOf(C, () => SetCaption(document, "Meanwhile"));
        var result = await Engine.ApplyAsync(Edit(Handler(FormPath, Form, "GreetClick"), Rename(C, Text(C))), PreviewMode.Never);

        _recording.FailWhen = null;
        Assert.Equal(RefactoringOutcome.Blocked, result.Outcome);
        Assert.Equal([new BlockedFile(FormPath, BlockReason.ChangedSinceComputed)], result.Blocked);
        Assert.Equal([Title, $"{FormPath} — {Strings.Reason_Refactoring_ChangedSinceComputed}"], Assert.Single(Dialogs.Requests).Args);
        Assert.Equal(2, Replacements(calls, C));
        Assert.Equal(files, FilesSnapshot());
        // The designer keeps the user's change — model, undo stack — and got nothing of the operation.
        var meanwhile = Form.Replace("Caption = 'MainForm'", "Caption = 'Meanwhile'", StringComparison.Ordinal);
        Assert.Equal(meanwhile, designer.CurrentFormText());
        Assert.True(designer.Undo.CanUndo);
        Assert.Equal("GreetButtonClick", OnClick(document));

        // The form (edited first, path order) has its new text when the buffer of Z fails — Z became read only during the
        // writes, without a change of its text: the form gets back the text it had, the file its bytes.
        var z = await OpenAsync(Z);
        files = FilesSnapshot(); // opening a document may write the project's session state
        calls = _recording.Calls.Count;
        DuringFirstWriteOf(C, () => z.Buffer.IsReadOnly = true);
        result = await Engine.ApplyAsync(Edit(Handler(FormPath, meanwhile, "GreetClick"), Rename(Z, z.Buffer.Text), Rename(C, Text(C))), PreviewMode.Never);

        _recording.FailWhen = null;
        Assert.Equal(RefactoringOutcome.Failed, result.Outcome);
        Assert.Equal("Msg.Refactoring.Failed", Dialogs.Requests[^1].MessageId);
        Assert.Equal(2, Replacements(calls, C));
        Assert.Equal(files, FilesSnapshot());
        Assert.Equal(Unit("Z"), z.Buffer.Text);
        Assert.Equal(meanwhile, designer.CurrentFormText());
        Assert.Equal("GreetButtonClick", OnClick(document));
        Assert.False(Engine.IsBusy);
    }

    private const string RenameProjectFile = """
        { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
          "files": [ { "path": "App.pas" }, { "path": "A.pas", "form": "A.form" }, { "path": "B.pas" } ] }
        """;

    private const string RenameA = "unit A;\n\ninterface\n\ntype\n  TMainForm = class\n  published\n    procedure Old(Sender: TObject);\n  end;\n\n"
        + "implementation\n\n{$FORM A.form}\n\nprocedure TMainForm.Old(Sender: TObject);\nbegin\nend;\n\nend.\n";

    private const string RenameB = "unit B;\n\ninterface\n\nuses A;\n\nprocedure Run(F: TMainForm);\n\nimplementation\n\n"
        + "procedure Run(F: TMainForm);\nbegin\n  F.Old(nil);\nend;\n\nend.\n";

    private const string RenameForm = "object MainForm: TMainForm\n  OnShow = Old\n  object Button1: TButton\n    OnClick = Old\n  end\nend\n";

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-003")]
    public Task FromLanguageServiceMapsEditsAndBaseTexts() => TestUiThread.RunAsync(() => FromLanguageServiceMapsEditsAndBaseTextsOnUiThread());

    private async Task FromLanguageServiceMapsEditsAndBaseTextsOnUiThread()
    {
        const string AForm = Dir + @"\A.form";
        WriteToolchain();
        File(ProjectPath, RenameProjectFile);
        File(Dir + @"\App.pas", "program App;\n\nuses A, B;\n\nbegin\n  Run(nil);\nend.\n");
        File(A, RenameA);
        File(B, RenameB);
        Write(AForm, [.. Utf8Bom, .. Encoding.UTF8.GetBytes(RenameForm)]); // the form has a byte-order mark on disk
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.True(await _services.Documents.CloseAllAsync());
        await _services.ProjectManager.MissingFileChecks;

        // B is open and has an unsaved change; A and its form are closed.
        var b = await OpenAsync(B);
        b.Buffer.ApplyEdits([new TextEdit(0, 0, "// unsaved\n")]);
        var bText = b.Buffer.Text;
        Assert.NotEqual(bText, Text(B));

        // The real rename of the published method, asked for at its call in B.
        Assert.NotNull(_services.CodeModel.Synchronize());
        var document = _services.CodeModel.Workspace.GetDocument(B);
        var position = bText.IndexOf("Old", StringComparison.Ordinal) + 1;
        var renamed = await _services.CodeModel.Worker.RunAsync("rename", ct => RenameService.Rename(document, position, "Renamed", ct), CancellationToken.None);
        Assert.True(renamed.Success, renamed.Error);
        Assert.Null(renamed.FileRename);

        var uiThread = Environment.CurrentManagedThreadId;
        var readers = new List<int>();
        _recording.BeforeRead = path =>
        {
            if (string.Equals(path, AForm, StringComparison.OrdinalIgnoreCase))
            {
                lock (readers)
                    readers.Add(Environment.CurrentManagedThreadId);
            }
        };

        var edit = await WorkspaceEdit.FromLanguageService(Title, renamed.Edits, document.Compilation, _recording);

        _recording.BeforeRead = null;
        // Paths and offsets: one file edit per document edit, in the service's order, every change as it is.
        Assert.Equal(Title, edit.Title);
        Assert.Equal([AForm, A, B], edit.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(renamed.Edits.Select(e => e.Path), edit.Files.Select(f => f.Path));
        Assert.Equal(renamed.Edits.Select(e => string.Join(' ', e.Changes.Select(c => $"{c.Span.Start}+{c.Span.Length}={c.NewText}"))),
            edit.Files.Select(f => string.Join(' ', f.Edits.Select(e => $"{e.Offset}+{e.Length}={e.NewText}"))));
        Assert.Equal([2, 2, 1], edit.Files.Select(f => f.Edits.Length));

        // Base texts: the form as it is on disk, decoded and without the byte-order mark, read off the UI thread; the
        // closed unit's file text; the open unit's buffer text.
        Assert.Equal([RenameForm, RenameA, bText], edit.Files.Select(f => f.BaseText));
        Assert.All(edit.Files, f => Assert.All(f.Edits, e => Assert.Equal("Old", f.BaseText.Substring(e.Offset, e.Length))));
        Assert.NotEqual(uiThread, Assert.Single(readers));

        // Applying the result gives the texts the service's edits describe.
        var result = await Engine.ApplyAsync(edit, PreviewMode.Never);
        Assert.Equal((RefactoringOutcome.Applied, 5, 3), (result.Outcome, result.EditCount, result.FileCount));
        Assert.Empty(Dialogs.Requests);
        Assert.Equal(Renamed(bText), b.Buffer.Text);
        Assert.Equal(RenameB, Text(B)); // the open unit is not saved
        Assert.Equal(Renamed(RenameA), Text(A));
        Assert.Equal([.. Utf8Bom, .. Encoding.UTF8.GetBytes(Renamed(RenameForm))], Bytes(AForm));

        // A cancelled conversion ends as cancelled.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => WorkspaceEdit.FromLanguageService(Title, renamed.Edits, document.Compilation, _recording, cancelled.Token));

        // A form that is gone since the service read it: no base text, the conversion fails with the system's error.
        _fs.DeleteFile(AForm);
        await Assert.ThrowsAnyAsync<IOException>(() => WorkspaceEdit.FromLanguageService(Title, renamed.Edits, document.Compilation, _recording));
    }
}
