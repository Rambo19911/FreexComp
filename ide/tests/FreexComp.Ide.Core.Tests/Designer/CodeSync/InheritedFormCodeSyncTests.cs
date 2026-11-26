using FreexComp.Designer.CodeSync;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Text;
using static FreexComp.Ide.Core.Tests.Designer.InheritedDesign;

namespace FreexComp.Ide.Core.Tests.Designer.CodeSync;

/// <summary>Code synchronisation of descendant forms (form-inheritance-designer.md §7, DSG-CS-119, 121, 123; D-131).</summary>
[Trait("Phase", "8")]
public sealed class InheritedFormCodeSyncTests
{
    private const string Notify = "TNotifyEvent";

    [Fact, Trait("Req", "DSG-CS-119"), Trait("Req", "DSG-CS-121"), Trait("Req", "DSG-CS-123")]
    public void FieldsOnlyForNewComponents_Reconcile_UntouchedInheritedStubRemovedOnSave()
    {
        var form = ParseForm(Form, Ancestors());
        var catalog = FrameDesign.Catalog;

        // DSG-CS-119: the new components have fields; the inherited ones are fields of the ancestor and are not missing.
        var synced = new FormUnit(new StringTextBuffer(Unit), "TMainForm");
        Assert.Empty(Reconciler.Reconcile(synced, form, catalog, "MainForm.form", ReconcileMode.CheckOnly).Issues);

        // Without a new component's field: only that one is missing, and the repair adds only it.
        var buffer = new StringTextBuffer(Unit.Replace("    HelpButton: TButton;\r\n", "", StringComparison.Ordinal));
        var missing = new FormUnit(buffer, "TMainForm");
        Assert.Equal([(ReconcileIssueKind.MissingField, "HelpButton")],
            Reconciler.Reconcile(missing, form, catalog, "MainForm.form", ReconcileMode.CheckOnly).Issues.Select(i => (i.Kind, i.Name)));
        Reconciler.Reconcile(missing, form, catalog, "MainForm.form", ReconcileMode.Repair);
        Assert.Contains("    HelpButton: TButton;\r\n", buffer.Text, StringComparison.Ordinal);
        foreach (var inherited in (string[])["TitleLabel", "ButtonPanel", "OkButton", "CancelButton"])
        {
            Assert.DoesNotContain(inherited + ":", buffer.Text, StringComparison.Ordinal);
        }

        Assert.Empty(Reconciler.Reconcile(missing, form, catalog, "MainForm.form", ReconcileMode.CheckOnly).Issues);

        // An override line naming a method of the descendant class is checked like any handler.
        var handled = ParseForm(Form.Replace("      Caption = 'Apply'\n", "      Caption = 'Apply'\n      OnClick = ApplyClick\n", StringComparison.Ordinal),
            Ancestors());
        Assert.Equal([(ReconcileIssueKind.MissingHandler, "ApplyClick")],
            Reconciler.Reconcile(synced, handled, catalog, "MainForm.form", ReconcileMode.CheckOnly).Issues.Select(i => (i.Kind, i.Name)));

        // DSG-CS-121: the hiding stub, exactly; untouched it counts as empty and is removed (one undo group).
        var unitBuffer = new StringTextBuffer(Unit);
        var unit = new FormUnit(unitBuffer, "TMainForm");
        var service = new CodeSyncService(unit, catalog);
        service.CreateInheritedHandler("OkButtonClick", Notify);
        Assert.Contains("    procedure OkButtonClick(Sender: TObject);\r\n  end;", unitBuffer.Text, StringComparison.Ordinal);
        Assert.Contains("{$FORM MainForm.form}\r\n\r\nprocedure TMainForm.OkButtonClick(Sender: TObject);\r\nbegin\r\n  inherited;\r\n  \r\nend;\r\n",
            unitBuffer.Text, StringComparison.Ordinal);
        Assert.Equal(2, service.RemoveEmptyUnreferencedHandlers(form).Count);
        Assert.Equal(Unit, unitBuffer.Text);

        // A stub the user edited is kept.
        service.CreateInheritedHandler("OkButtonClick", Notify);
        var body = unitBuffer.Text.IndexOf("  inherited;\r\n", StringComparison.Ordinal) + "  inherited;\r\n".Length;
        unitBuffer.ApplyEdits([new TextEdit(body, 2, "  Close;")]);
        Assert.Empty(service.RemoveEmptyUnreferencedHandlers(form));
        Assert.Contains("  inherited;\r\n  Close;\r\nend;", unitBuffer.Text, StringComparison.Ordinal);

        // A comment in the body also counts as an edit.
        service.CreateInheritedHandler("FormCreate", Notify);
        var create = unitBuffer.Text.IndexOf("TMainForm.FormCreate", StringComparison.Ordinal);
        var line = unitBuffer.Text.IndexOf("  inherited;\r\n", create, StringComparison.Ordinal) + "  inherited;\r\n".Length;
        unitBuffer.ApplyEdits([new TextEdit(line, 2, "  // later")]);
        Assert.Empty(service.RemoveEmptyUnreferencedHandlers(form));
        Assert.Contains("TMainForm.FormCreate", unitBuffer.Text, StringComparison.Ordinal);
    }
}
