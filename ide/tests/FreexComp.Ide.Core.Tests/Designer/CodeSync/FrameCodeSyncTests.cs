using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Text;
using static FreexComp.Ide.Core.Tests.Designer.FrameDesign;

namespace FreexComp.Ide.Core.Tests.Designer.CodeSync;

/// <summary>Code synchronisation of frame instances (frames-designer.md §8, DSG-CS-112, 116; D-119).</summary>
[Trait("Phase", "8")]
public sealed class FrameCodeSyncTests
{
    [Fact, Trait("Req", "DSG-CS-112"), Trait("Req", "DSG-CS-116")]
    public void InstanceFieldOnly_NoFieldsForInheritedObjects_Reconcile()
    {
        var frames = Frames();
        var form = ParseForm(Form, frames);
        Assert.True(form.FindByName("BillingAddress")!.IsFrameInstance);

        // The instance has a field of its frame class; the frame's components have none (they are fields of the frame).
        var synced = new FormUnit(new StringTextBuffer(Unit), "TMainForm");
        Assert.Empty(Reconciler.Reconcile(synced, form, Catalog, "MainForm.form", ReconcileMode.CheckOnly).Issues);

        // Without the field: only the instance's field is missing, and the repair adds it with the frame class.
        var missingText = Unit.Replace("    BillingAddress: TAddressFrame;\r\n", "", StringComparison.Ordinal);
        var buffer = new StringTextBuffer(missingText);
        var missing = new FormUnit(buffer, "TMainForm");
        Assert.Equal([(ReconcileIssueKind.MissingField, "BillingAddress")],
            Reconciler.Reconcile(missing, form, Catalog, "MainForm.form", ReconcileMode.CheckOnly).Issues.Select(i => (i.Kind, i.Name)));
        Reconciler.Reconcile(missing, form, Catalog, "MainForm.form", ReconcileMode.Repair);
        Assert.Contains("    BillingAddress: TAddressFrame;\r\n", buffer.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("StreetLabel", buffer.Text, StringComparison.Ordinal);
        Assert.Empty(Reconciler.Reconcile(missing, form, Catalog, "MainForm.form", ReconcileMode.CheckOnly).Issues);

        // DSG-CS-116: the instance field's type is checked against the frame class.
        var wrongType = new FormUnit(new StringTextBuffer(Unit.Replace("BillingAddress: TAddressFrame;", "BillingAddress: TPanel;",
            StringComparison.Ordinal)), "TMainForm");
        var mismatch = Assert.Single(Reconciler.Reconcile(wrongType, form, Catalog, "MainForm.form", ReconcileMode.CheckOnly).Issues);
        Assert.Equal((ReconcileIssueKind.FieldTypeMismatch, new FieldMismatch("BillingAddress", "TPanel", "TAddressFrame")),
            (mismatch.Kind, mismatch.Mismatch!));

        // A field of a frame class without its instance is an orphan, like a field of a component class.
        var orphanForm = ParseForm(Form.Replace("  inline BillingAddress: TAddressFrame\n    Left = 8\n    Top = 8\n    Width = 320\n"
            + "    Height = 120\n    TabOrder = 0\n    inherited StreetLabel: TLabel\n      Caption = 'Billing street'\n    end\n  end\n", "",
            StringComparison.Ordinal), frames);
        Assert.Equal([(ReconcileIssueKind.OrphanField, "BillingAddress")],
            Reconciler.Reconcile(synced, orphanForm, Catalog, "MainForm.form", ReconcileMode.CheckOnly).Issues.Select(i => (i.Kind, i.Name)));

        // An event override of an inherited object names a method of the form class (DSG-CS-113): reported when missing.
        var handled = ParseForm(Form.Replace("      Caption = 'Billing street'\n    end\n",
            "      Caption = 'Billing street'\n    end\n    inherited StreetEdit: TEdit\n      OnChange = BillingAddressStreetEditChange\n    end\n",
            StringComparison.Ordinal), frames);
        Assert.Equal([(ReconcileIssueKind.MissingHandler, "BillingAddressStreetEditChange")],
            Reconciler.Reconcile(synced, handled, Catalog, "MainForm.form", ReconcileMode.CheckOnly).Issues.Select(i => (i.Kind, i.Name)));
    }
}
