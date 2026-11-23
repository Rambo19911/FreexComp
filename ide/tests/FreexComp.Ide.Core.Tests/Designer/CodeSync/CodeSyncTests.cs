using FreexComp.Designer.Catalog;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Designer.CodeSync;

/// <summary>code-sync.md §1: the service's edit contract — rollback on new syntax errors, one undo group per call (D-071).</summary>
[Trait("Phase", "5")]
public sealed class CodeSyncTests
{
    private static readonly Lazy<ComponentCatalog> LoadedCatalog = new(() =>
    {
        var result = CatalogLoader.Load([Path.Combine(AppContext.BaseDirectory, "TestData", "metadata", "index.json")]);
        Assert.Empty(result.Problems);
        return result.Catalog;
    });

    internal static ComponentCatalog Catalog => LoadedCatalog.Value;

    internal static CodeSyncService Service(StringTextBuffer buffer, string formType = "TMainForm") =>
        new(new FormUnit(buffer, formType, @"C:\Project\MainForm.pas"), Catalog);

    [Fact, Trait("Req", "DSG-CS-004")]
    public void RollsBackWhenEditIntroducesSyntaxError()
    {
        var original = TestUnits.Read("MainForm.pas");
        var buffer = new StringTextBuffer(original);
        var service = Service(buffer);

        // A type text that does not parse: the batch is undone and the first new parser diagnostic is the reason.
        var error = Assert.Throws<CodeSyncException>(() => service.AddFields([("Grid1", "array of", "Grids")]));
        Assert.Equal(original, buffer.Text);
        Assert.Equal(CodeSyncState.Ok, service.State);
        Assert.False(buffer.CanUndo);
        var expected = FormClassLocator.Locate(original.Replace("GreetButton: TButton;", "GreetButton: TButton;\r\n    Grid1: array of;", StringComparison.Ordinal), "TMainForm").FirstError;
        Assert.NotNull(expected);
        Assert.Equal(expected.Message, error.Reason);

        // A clean edit is kept.
        service.AddFields([("Label1", "TLabel", "StdCtrls")]);
        Assert.Contains("    Label1: TLabel;", buffer.Text, StringComparison.Ordinal);
    }

    [Fact, Trait("Req", "DSG-CS-002"), Trait("Req", "DSG-CS-001")]
    public void EditsAreOneTextUndoGroup()
    {
        var original = TestUnits.Read("MainForm.pas");
        var buffer = new StringTextBuffer(original);
        var service = Service(buffer);

        // Two fields and a new unit in the uses clause: several parse-and-apply steps, one undo step.
        var edits = service.AddFields([("Label1", "TLabel", "StdCtrls"), ("Timer1", "TTimer", "ExtCtrls")]).Edits;
        Assert.Equal(3, edits.Count);
        Assert.Contains("StdCtrls, ExtCtrls;", buffer.Text, StringComparison.Ordinal);
        buffer.Undo();
        Assert.Equal(original, buffer.Text);
        Assert.False(buffer.CanUndo);

        // Declaration and stub of a handler.
        Assert.Equal(2, service.CreateHandler("NameEditChange", "TNotifyEvent").Count);
        buffer.Undo();
        Assert.Equal(original, buffer.Text);
        Assert.False(buffer.CanUndo);

        // A repair: field, stub and directive.
        var unsynced = original.Replace("    GreetButton: TButton;\r\n", "", StringComparison.Ordinal)
            .Replace("{$FORM MainForm.form}\r\n\r\n", "", StringComparison.Ordinal);
        var other = new StringTextBuffer(unsynced);
        var form = FormParser.Parse(TestUnits.Read("MainForm.form").Replace("\r\n", "\n", StringComparison.Ordinal), "MainForm.form", Catalog).Document!;
        var repair = Service(other).Reconcile(form, ReconcileMode.Repair);
        Assert.Equal([ReconcileIssueKind.MissingField, ReconcileIssueKind.FormDirective], repair.Issues.Select(i => i.Kind));
        Assert.Equal(original, other.Text);
        other.Undo();
        Assert.Equal(unsynced, other.Text);
        Assert.False(other.CanUndo);

        // Rename, remove and form rename are single steps too.
        service.RenameField("NameEdit", "FirstEdit");
        service.RemoveFields(["GreetButton"]);
        service.RenameFormClassAndVariable("MainForm", "HelloForm");
        buffer.Undo();
        buffer.Undo();
        buffer.Undo();
        Assert.Equal(original, buffer.Text);
        Assert.False(buffer.CanUndo);
    }
}

/// <summary>code-sync.md §10: designer undo and redo of the code part of an undo unit (D-071).</summary>
[Trait("Phase", "5")]
public sealed class CodeSyncUndoTests
{
    private const string UserComment = "// a comment the user typed\r\n";

    [Fact, Trait("Req", "DSG-CS-100"), Trait("Req", "DSG-CS-101")]
    public void DesignerUndoRevertsCodeWhenUntouched()
    {
        var original = TestUnits.Read("MainForm.pas");
        var buffer = new StringTextBuffer(original);
        var service = CodeSyncTests.Service(buffer);
        var edits = service.AddFields([("Label1", "TLabel", "StdCtrls"), ("Timer1", "TTimer", "ExtCtrls")]).Edits;
        var stub = service.CreateHandler("NameEditChange", "TNotifyEvent");

        // The user edits outside the inserted ranges (before them, so every offset moves; right after one of them).
        buffer.ApplyEdits([new TextEdit(0, 0, UserComment)]);
        var end = buffer.Text.IndexOf("end.", StringComparison.Ordinal);
        buffer.ApplyEdits([new TextEdit(end, 0, "{ tail }")]);

        Assert.True(service.RevertEdits(stub));
        Assert.True(service.RevertEdits(edits));
        var expected = UserComment + original.Replace("end.", "{ tail }end.", StringComparison.Ordinal);
        Assert.Equal(expected, buffer.Text);

        // Each revert is one code-editor undo group.
        buffer.Undo();
        Assert.Contains("    Timer1: TTimer;", buffer.Text, StringComparison.Ordinal);
        Assert.Contains("ExtCtrls", buffer.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("NameEditChange", buffer.Text, StringComparison.Ordinal);
    }

    [Fact, Trait("Req", "DSG-CS-101")]
    public void DesignerUndoSkipsCodeWhenUserEdited()
    {
        var original = TestUnits.Read("MainForm.pas");
        var buffer = new StringTextBuffer(original);
        var service = CodeSyncTests.Service(buffer);
        var stub = service.CreateHandler("NameEditChange", "TNotifyEvent");
        Assert.Equal(2, stub.Count);

        // The user writes into the stub body: that record is skipped, the untouched declaration is reverted.
        var body = buffer.Text.IndexOf("procedure TMainForm.NameEditChange", StringComparison.Ordinal);
        body = buffer.Text.IndexOf("begin\r\n", body, StringComparison.Ordinal) + "begin\r\n".Length;
        buffer.ApplyEdits([new TextEdit(body, 0, "  ShowMessage('x');")]);
        var edited = buffer.Text;

        Assert.False(service.RevertEdits(stub));
        Assert.DoesNotContain("    procedure NameEditChange(Sender: TObject);", buffer.Text, StringComparison.Ordinal);
        Assert.Contains("procedure TMainForm.NameEditChange(Sender: TObject);\r\nbegin\r\n  ShowMessage('x');  \r\nend;", buffer.Text, StringComparison.Ordinal);

        // A record whose range was deleted by the user is skipped as well; nothing is reverted twice.
        Assert.False(service.RevertEdits(stub));
        buffer.Undo();
        Assert.Equal(edited, buffer.Text);
    }

    [Fact, Trait("Req", "DSG-CS-102")]
    public void RedoReexecutesOperations()
    {
        var original = TestUnits.Read("MainForm.pas");
        var buffer = new StringTextBuffer(original);
        var service = CodeSyncTests.Service(buffer);
        IReadOnlyList<CodeSyncOp> ops =
        [
            new AddFieldsOp([new CodeSyncField("Timer1", "TTimer", "ExtCtrls")]),
            new CreateHandlerOp("Timer1Timer", "TNotifyEvent"),
            new RenameFieldOp("NameEdit", "FirstEdit"),
        ];
        var first = service.Execute(ops);
        Assert.True(service.RevertEdits(first));
        Assert.Equal(original, buffer.Text);

        // The user changes the unit before redo: the operations run again against the current text, not the offsets.
        buffer.ApplyEdits([new TextEdit(0, 0, UserComment + UserComment)]);
        var fresh = service.Execute(ops);

        var reference = new StringTextBuffer(UserComment + UserComment + original);
        var direct = CodeSyncTests.Service(reference);
        direct.AddFields([("Timer1", "TTimer", "ExtCtrls")]);
        direct.CreateHandler("Timer1Timer", "TNotifyEvent");
        direct.RenameField("NameEdit", "FirstEdit");
        Assert.Equal(reference.Text, buffer.Text);
        Assert.Equal(first.Count, fresh.Count);
        Assert.NotEqual(first.Select(e => e.Offset), fresh.Select(e => e.Offset));

        // The fresh records are tracked: a later designer undo reverts them; the redo was one code-editor undo group.
        Assert.True(service.RevertEdits(fresh));
        Assert.Equal(UserComment + UserComment + original, buffer.Text);
        buffer.Undo();
        buffer.Undo();
        Assert.Equal(UserComment + UserComment + original, buffer.Text);
    }
}
