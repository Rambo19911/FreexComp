using FreexComp.Designer.Catalog;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Designer.CodeSync;

/// <summary>code-sync.md §9: reconciliation of the form class with the form (D-070).</summary>
[Trait("Phase", "5")]
public sealed class ReconcilerTests
{
    private static readonly Lazy<ComponentCatalog> LoadedCatalog = new(() =>
    {
        var result = CatalogLoader.Load([Path.Combine(AppContext.BaseDirectory, "TestData", "metadata", "index.json")]);
        Assert.Empty(result.Problems);
        return result.Catalog;
    });

    private static ComponentCatalog Catalog => LoadedCatalog.Value;

    private static FormDocument Form(string text)
    {
        var result = FormParser.Parse(text.Replace("\r\n", "\n", StringComparison.Ordinal), "U.form", Catalog);
        return result.Document ?? throw new InvalidOperationException("form did not parse");
    }

    private const string UnsyncedForm = """
        object F: TF
          object OkButton: TButton
            OnClick = OkButtonClick
          end
          object NameEdit: TEdit
            OnChange = NameEditChange
          end
          object Memo1: TMemo
          end
          object Timer1: TTimer
            OnTimer = Timer1Timer
          end
        end
        """;

    /// <summary>Missing fields (NameEdit, Timer1), a type mismatch (Memo1), a missing handler, an incompatible one, no directive.</summary>
    private const string UnsyncedUnit =
        "unit U;\r\n\r\ninterface\r\n\r\nuses\r\n  Classes, Controls, Forms, StdCtrls;\r\n\r\ntype\r\n  TF = class(TForm)\r\n"
        + "    OkButton: TButton;\r\n    Memo1: TEdit;\r\n    procedure NameEditChange;\r\n  private\r\n  end;\r\n\r\nvar\r\n  F: TF;\r\n\r\n"
        + "implementation\r\n\r\nprocedure TF.NameEditChange;\r\nbegin\r\nend;\r\n\r\nend.\r\n";

    [Fact, Trait("Req", "DSG-CS-090")]
    public void CheckOnlyMakesNoEdits()
    {
        var buffer = new StringTextBuffer(UnsyncedUnit);
        var unit = new FormUnit(buffer, "TF");
        var form = Form(UnsyncedForm);

        var result = Reconciler.Reconcile(unit, form, Catalog, "U.form", ReconcileMode.CheckOnly);

        Assert.Empty(result.Edits);
        Assert.Equal(UnsyncedUnit, buffer.Text);
        Assert.Equal(0, buffer.Version);
        Assert.False(buffer.CanUndo);
        Assert.Equal(
            [
                (ReconcileIssueKind.MissingField, "NameEdit"),
                (ReconcileIssueKind.FieldTypeMismatch, "Memo1"),
                (ReconcileIssueKind.MissingField, "Timer1"),
                (ReconcileIssueKind.MissingHandler, "OkButtonClick"),
                (ReconcileIssueKind.IncompatibleHandler, "NameEditChange"),
                (ReconcileIssueKind.MissingHandler, "Timer1Timer"),
                (ReconcileIssueKind.FormDirective, "U.form"),
            ],
            result.Issues.Select(i => (i.Kind, i.Name)));

        // Form diagnostics F0104 / F0108 (form-format.md §5) for the event properties.
        var mismatch = Assert.Single(result.Issues, i => i.Kind == ReconcileIssueKind.FieldTypeMismatch);
        Assert.Equal(new FieldMismatch("Memo1", "TEdit", "TMemo"), mismatch.Mismatch);
        Assert.Equal(
            [
                ("F0104", "Method OkButtonClick not found in TF", "OkButton", "OnClick"),
                ("F0108", "Method NameEditChange is not compatible with event OnChange", "NameEdit", "OnChange"),
                ("F0104", "Method Timer1Timer not found in TF", "Timer1", "OnTimer"),
            ],
            result.Issues.Where(i => i.Diagnostic is not null).Select(i => (i.Diagnostic!.Code, i.Diagnostic.Message, i.Diagnostic.ObjectName!, i.Diagnostic.PropertyPath!)));
        Assert.All(result.Issues.Where(i => i.Diagnostic is not null), i => Assert.Equal("U.form", i.Diagnostic!.Path));
        Assert.Equal(3, result.Issues.Single(i => i.Name == "OkButtonClick").Diagnostic!.Line);

        // A unit in sync reports nothing; a state other than Ok reports nothing and edits nothing.
        var synced = new FormUnit(new StringTextBuffer(TestUnits.Read("MainForm.pas")), "TMainForm");
        Assert.Empty(Reconciler.Reconcile(synced, Form(TestUnits.Read("MainForm.form")), Catalog, "MainForm.form", ReconcileMode.CheckOnly).Issues);
        var broken = new FormUnit(new StringTextBuffer(TestUnits.Read("SyntaxError.pas")), "TBrokenForm");
        var none = Reconciler.Reconcile(broken, form, Catalog, "U.form", ReconcileMode.Repair);
        Assert.Empty(none.Issues);
        Assert.Empty(none.Edits);
    }

    [Fact, Trait("Req", "DSG-CS-091"), Trait("Req", "DSG-CS-035"), Trait("Req", "DSG-CS-051")]
    public void RepairAddsFieldsStubsDirective()
    {
        var buffer = new StringTextBuffer(UnsyncedUnit);
        var unit = new FormUnit(buffer, "TF");
        var form = Form(UnsyncedForm);

        var result = Reconciler.Reconcile(unit, form, Catalog, "U.form", ReconcileMode.Repair);

        const string Expected =
            "unit U;\r\n\r\ninterface\r\n\r\nuses\r\n  Classes, Controls, Forms, StdCtrls, ExtCtrls;\r\n\r\ntype\r\n  TF = class(TForm)\r\n"
            + "    OkButton: TButton;\r\n    Memo1: TEdit;\r\n    NameEdit: TEdit;\r\n    Timer1: TTimer;\r\n    procedure NameEditChange;\r\n"
            + "    procedure OkButtonClick(Sender: TObject);\r\n    procedure Timer1Timer(Sender: TObject);\r\n  private\r\n  end;\r\n\r\nvar\r\n  F: TF;\r\n\r\n"
            + "implementation\r\n\r\n{$FORM U.form}\r\n\r\nprocedure TF.NameEditChange;\r\nbegin\r\nend;\r\n\r\n"
            + "procedure TF.OkButtonClick(Sender: TObject);\r\nbegin\r\n  \r\nend;\r\n\r\n"
            + "procedure TF.Timer1Timer(Sender: TObject);\r\nbegin\r\n  \r\nend;\r\n\r\nend.\r\n";
        Assert.Equal(Expected, buffer.Text);
        Assert.NotEmpty(result.Edits);

        // The type mismatch and the incompatible handler are never auto-fixed: a second check reports exactly those.
        var again = Reconciler.Reconcile(unit, form, Catalog, "U.form", ReconcileMode.CheckOnly);
        Assert.Equal([(ReconcileIssueKind.FieldTypeMismatch, "Memo1"), (ReconcileIssueKind.IncompatibleHandler, "NameEditChange")],
            again.Issues.Select(i => (i.Kind, i.Name)));

        // The whole repair is one code-editor undo step (DSG-CS-001, 002).
        buffer.Undo();
        Assert.Equal(UnsyncedUnit, buffer.Text);
        Assert.False(buffer.CanUndo);

        // A wrong directive argument is fixed in place.
        var wrong = new StringTextBuffer(Expected.Replace("{$FORM U.form}", "{$FORM Old.form}", StringComparison.Ordinal));
        Reconciler.Reconcile(new FormUnit(wrong, "TF"), form, Catalog, "U.form", ReconcileMode.Repair);
        Assert.Equal(Expected, wrong.Text);
    }

    [Fact, Trait("Req", "DSG-CS-093"), Trait("Req", "DSG-CS-040")]
    public void RepairRemovesOrphanComponentFieldsOnlyInManagedSection()
    {
        const string Before =
            "unit U;\r\n\r\ninterface\r\n\r\nuses\r\n  Classes, Controls, Forms, StdCtrls;\r\n\r\ntype\r\n  TF = class(TForm)\r\n"
            + "    OkButton: TButton;\r\n    OldLabel: TLabel; // gone from the form\r\n    Counter: Integer;\r\n    Helper: TStringHelper;\r\n"
            + "    First, Second: TEdit;\r\n  private\r\n    HiddenButton: TButton;\r\n  published\r\n    PublishedEdit: TEdit;\r\n  end;\r\n\r\n"
            + "var\r\n  F: TF;\r\n\r\nimplementation\r\n\r\n{$FORM U.form}\r\n\r\nend.\r\n";
        const string FormText = """
            object F: TF
              object OkButton: TButton
              end
              object Second: TEdit
              end
            end
            """;
        var buffer = new StringTextBuffer(Before);

        var result = Reconciler.Reconcile(new FormUnit(buffer, "TF"), Form(FormText), Catalog, "U.form", ReconcileMode.Repair);

        // Orphans of a catalog class in the managed section go (OldLabel with its comment, First of a multi-name
        // declaration); non-component types (Integer, unknown classes) and explicit visibility sections stay.
        Assert.Equal(
            Before.Replace("    OldLabel: TLabel; // gone from the form\r\n", "", StringComparison.Ordinal)
                .Replace("First, Second: TEdit;", "Second: TEdit;", StringComparison.Ordinal),
            buffer.Text);
        Assert.Equal([(ReconcileIssueKind.OrphanField, "OldLabel"), (ReconcileIssueKind.OrphanField, "First")],
            result.Issues.Select(i => (i.Kind, i.Name)));
    }
}
