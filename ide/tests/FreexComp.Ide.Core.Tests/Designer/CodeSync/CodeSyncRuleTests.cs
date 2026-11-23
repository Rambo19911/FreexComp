using FreexComp.Designer.Catalog;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.TestSupport;
using static FreexComp.Ide.Core.Tests.Designer.DesignerFixture;

namespace FreexComp.Ide.Core.Tests.Designer.CodeSync;

/// <summary>Code-sync rules that had no test of their own (code-sync.md §1, §4, §7, §8, §10; D-087).</summary>
[Trait("Phase", "5")]
public sealed class CodeSyncRuleTests
{
    private static FormDocument Form(string text) =>
        FormParser.Parse(text.Replace("\r\n", "\n", StringComparison.Ordinal), "U.form", CodeSyncTests.Catalog).Document
        ?? throw new InvalidOperationException("form did not parse");

    private const string MultiNameUnit =
        "unit U;\r\n\r\ninterface\r\n\r\nuses\r\n  Classes, Controls, Forms, StdCtrls;\r\n\r\ntype\r\n  TF = class(TForm)\r\n"
        + "    OkButton, CancelButton: TButton;\r\n    procedure OkButtonClick(Sender: TObject);\r\n  end;\r\n\r\nvar\r\n  F: TF;\r\n\r\n"
        + "implementation\r\n\r\n{$FORM U.form}\r\n\r\nprocedure TF.OkButtonClick(Sender: TObject);\r\nbegin\r\nend;\r\n\r\nend.\r\n";

    private static CodeSyncService Service(StringTextBuffer buffer, string formType = "TF") =>
        new(new FormUnit(buffer, formType, @"C:\Project\U.pas"), CodeSyncTests.Catalog) { FormFileName = "U.form" };

    [Fact(Timeout = 30000), Trait("Req", "DSG-CS-010")]
    public Task StateIsRecomputed300MsAfterTheLastChange() => TestUiThread.RunAsync(StateIsRecomputed300MsAfterTheLastChangeOnUiThread);

    private static async Task StateIsRecomputed300MsAfterTheLastChangeOnUiThread()
    {
        var f = new DesignerFixture();
        var document = await f.OpenAsync();
        var designer = document.Designer;
        var clock = (ManualClock)f.Services.Clock;
        var buffer = designer.Unit.Buffer;
        Assert.False(designer.Surface!.IsReadOnly);

        // A parser error typed into the unit while the Design view is shown: the edit methods see it at once ...
        var at = buffer.Text.IndexOf("  private", StringComparison.Ordinal);
        buffer.ApplyEdits([new TextEdit(at, 0, "    procedure ;\r\n")]);
        Assert.Equal(CodeSyncState.UnitHasErrors, designer.CodeSync.State);

        // ... the displayed state follows 300 ms after the last change (each change restarts the wait).
        clock.Advance(TimeSpan.FromMilliseconds(200));
        buffer.ApplyEdits([new TextEdit(at, 0, " ")]);
        clock.Advance(TimeSpan.FromMilliseconds(299));
        Assert.False(designer.Surface.IsReadOnly);
        Assert.DoesNotContain(designer.DesignInfoBars, b => b.Kind == "UnitErrors");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(designer.Surface.IsReadOnly);
        Assert.Contains(designer.DesignInfoBars, b => b.Kind == "UnitErrors");

        // Fixed again: 300 ms later the form is editable and the bar is gone.
        buffer.Undo();
        buffer.Undo();
        Assert.Equal(CodeSyncState.Ok, designer.CodeSync.State);
        Assert.True(designer.Surface.IsReadOnly);
        clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.False(designer.Surface.IsReadOnly);
        Assert.DoesNotContain(designer.DesignInfoBars, b => b.Kind == "UnitErrors");
    }

    /// <summary>The test metadata plus one file declaring <c>eventSignatureUnits</c> (A-CS-2), read through the loader's file hook.</summary>
    private static ComponentCatalog CatalogWithSignatureUnits()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "TestData", "metadata");
        var index = Path.Combine(folder, "signature-units-index.json");
        var extra = Path.Combine(folder, "SignatureUnits.components.json");
        var real = File.ReadAllText(Path.Combine(folder, "index.json"));
        var result = CatalogLoader.Load([index], path =>
            string.Equals(path, index, StringComparison.OrdinalIgnoreCase)
                ? System.Text.Encoding.UTF8.GetBytes(real.Replace("\"ActnList.components.json\"", "\"ActnList.components.json\", \"SignatureUnits.components.json\"", StringComparison.Ordinal))
                : string.Equals(path, extra, StringComparison.OrdinalIgnoreCase)
                    ? System.Text.Encoding.UTF8.GetBytes("""{ "formatVersion": 1, "eventSignatureUnits": { "TKeyPressEvent": "KeyEvents", "TNotifyEvent": "Classes" } }""")
                    : File.Exists(path) ? File.ReadAllBytes(path) : null);
        Assert.Empty(result.Problems);
        return result.Catalog;
    }

    [Fact, Trait("Req", "DSG-CS-055")]
    public void HandlerSignatureUnitsAreEnsuredOnlyWhenTheMetadataNamesThem()
    {
        var catalog = CatalogWithSignatureUnits();
        Assert.Equal("KeyEvents", catalog.FindEventSignatureUnit("TKeyPressEvent"));
        Assert.Null(catalog.FindEventSignatureUnit("TMouseEvent"));
        Assert.Null(CodeSyncTests.Catalog.FindEventSignatureUnit("TKeyPressEvent"));

        // Declared: creating a TKeyPressEvent handler adds KeyEvents to the interface uses clause; Classes is there already.
        var buffer = new StringTextBuffer(MultiNameUnit);
        var service = new CodeSyncService(new FormUnit(buffer, "TF", @"C:\Project\U.pas"), catalog) { FormFileName = "U.form" };
        service.CreateHandler("OkButtonKeyPress", "TKeyPressEvent");
        Assert.Contains("  Classes, Controls, Forms, StdCtrls, KeyEvents;\r\n", buffer.Text, StringComparison.Ordinal);
        Assert.Contains("procedure OkButtonKeyPress(Sender: TObject; var Key: Char);", buffer.Text, StringComparison.Ordinal);
        var before = buffer.Text;
        service.CreateHandler("CancelButtonClick", "TNotifyEvent");
        Assert.Contains("  Classes, Controls, Forms, StdCtrls, KeyEvents;\r\n", buffer.Text, StringComparison.Ordinal);
        Assert.NotEqual(before, buffer.Text);

        // Absent key: no unit is added for the signature.
        buffer = new StringTextBuffer(MultiNameUnit);
        Service(buffer).CreateHandler("OkButtonKeyPress", "TKeyPressEvent");
        Assert.Contains("  Classes, Controls, Forms, StdCtrls;\r\n", buffer.Text, StringComparison.Ordinal);
        Assert.Contains("procedure OkButtonKeyPress(Sender: TObject; var Key: Char);", buffer.Text, StringComparison.Ordinal);
    }

    [Fact, Trait("Req", "DSG-CS-007")]
    public void IdentifiersCompareIgnoringCase()
    {
        var buffer = new StringTextBuffer(MultiNameUnit);

        // The form class is found by its type name in any case.
        var service = Service(buffer, "tf");
        Assert.Equal(CodeSyncState.Ok, service.State);

        // Members, compatible methods and field presence ignore case.
        Assert.Equal(MemberLookup.Field, service.LookupMember("okbutton"));
        Assert.Equal(MemberLookup.Method, service.LookupMember("OKBUTTONCLICK"));
        Assert.Equal(MemberLookup.Other, service.LookupMember("caption"));
        Assert.Equal(["OkButtonClick"], service.GetCompatibleMethods("tnotifyevent"));
        var before = buffer.Text;
        Assert.Empty(service.AddFields([("OKBUTTON", "tbutton", "stdctrls")]).Edits); // same field, same type, unit present
        Assert.Equal(before, buffer.Text);

        // A form whose names differ only in case from the declarations is in sync.
        var form = Form("object F: TF\n  object okbutton: TButton\n    OnClick = okbuttonclick\n  end\n  object CANCELBUTTON: TButton\n  end\nend\n");
        Assert.Empty(service.Reconcile(form, ReconcileMode.CheckOnly).Issues);
    }

    [Fact, Trait("Req", "DSG-CS-038")]
    public void MultiNameDeclarationsAreUnderstoodButNeverCreated()
    {
        var buffer = new StringTextBuffer(MultiNameUnit);
        var service = Service(buffer);

        // Both names of "OkButton, CancelButton: TButton;" are fields; the form is in sync.
        Assert.Equal(MemberLookup.Field, service.LookupMember("CancelButton"));
        var form = Form("object F: TF\n  object OkButton: TButton\n    OnClick = OkButtonClick\n  end\n  object CancelButton: TButton\n  end\nend\n");
        Assert.Empty(service.Reconcile(form, ReconcileMode.CheckOnly).Issues);

        // A new field of the same type gets its own declaration line.
        service.AddFields([("HelpButton", "TButton", "StdCtrls")]);
        Assert.Contains("    OkButton, CancelButton: TButton;\r\n    HelpButton: TButton;\r\n", buffer.Text, StringComparison.Ordinal);
    }

    [Fact, Trait("Req", "DSG-CS-030")]
    public void OneFieldPerComponentExceptTheRootTypedAsWritten()
    {
        const string Unit =
            "unit U;\r\n\r\ninterface\r\n\r\nuses\r\n  Classes, Controls, Forms, StdCtrls;\r\n\r\ntype\r\n  TF = class(TForm)\r\n"
            + "    OkButton: TButton;\r\n  end;\r\n\r\nvar\r\n  F: TF;\r\n\r\nimplementation\r\n\r\n{$FORM U.form}\r\n\r\nend.\r\n";
        var buffer = new StringTextBuffer(Unit);
        var service = Service(buffer);
        var form = Form("""
            object F: TF
              object OkButton: TButton
              end
              object Fancy1: TFancyWidget
                Left = 1
              end
              object Edit1: TEdit
              end
            end
            """);

        service.Reconcile(form, ReconcileMode.Repair);

        // Every component but the root once, typed with its class name; the preserved unknown class keeps its declared type.
        var location = FormClassLocator.Locate(buffer.Text, "TF");
        Assert.Equal(CodeSyncState.Ok, location.State);
        var text = buffer.Text;
        Assert.Equal(1, Count(text, "OkButton: TButton;"));
        Assert.Equal(1, Count(text, "Fancy1: TFancyWidget;"));
        Assert.Equal(1, Count(text, "Edit1: TEdit;"));
        Assert.DoesNotContain("    F: TF;", text, StringComparison.Ordinal);
        Assert.DoesNotContain(service.Reconcile(form, ReconcileMode.CheckOnly).Issues, i => i.Kind == ReconcileIssueKind.MissingField);

        // A second repair adds nothing.
        service.Reconcile(form, ReconcileMode.Repair);
        Assert.Equal(text, buffer.Text);
    }

    private static int Count(string text, string part)
    {
        var n = 0;
        for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }

    [Fact(Timeout = 30000), Trait("Req", "DSG-CS-075")]
    public Task DeletingAComponentKeepsItsHandlerAsCandidate() => TestUiThread.RunAsync(DeletingAComponentKeepsItsHandlerAsCandidateOnUiThread);

    private static async Task DeletingAComponentKeepsItsHandlerAsCandidateOnUiThread()
    {
        var f = new DesignerFixture();
        var document = await f.OpenAsync();
        var designer = document.Designer;
        Select(designer, "GreetButton");
        designer.Surface!.Execute(FreexComp.Designer.Surface.Commands.Delete);
        Assert.Null(designer.Model!.FindByName("GreetButton"));

        // The field goes; the handler code stays and is a candidate for removal on save.
        var text = designer.Unit.Buffer.Text;
        Assert.DoesNotContain("GreetButton: TButton;", text, StringComparison.Ordinal);
        Assert.Contains("    procedure GreetButtonClick(Sender: TObject);", text, StringComparison.Ordinal);
        Assert.Contains("procedure TMainForm.GreetButtonClick(Sender: TObject);", text, StringComparison.Ordinal);
        Assert.Contains("GreetButtonClick", designer.CodeSync.HandlerCandidates);
    }

    [Fact(Timeout = 30000), Trait("Req", "DSG-CS-082"), Trait("Req", "DSG-CS-103")]
    public Task CodeStaysEditableAndReconciliationRestoresConsistency() =>
        TestUiThread.RunAsync(CodeStaysEditableAndReconciliationRestoresConsistencyOnUiThread);

    private static async Task CodeStaysEditableAndReconciliationRestoresConsistencyOnUiThread()
    {
        var f = new DesignerFixture();
        var document = await f.OpenAsync();
        var designer = document.Designer;
        var buffer = designer.Unit.Buffer;

        // A designer edit that writes code: a new button gets its field.
        designer.Surface!.PlaceAtDefaultPosition("TButton");
        var added = designer.Surface.Selection.Primary.Name;
        Assert.Contains($"    {added}: TButton;", buffer.Text, StringComparison.Ordinal);
        var model = FormWriter.Write(designer.Model!);

        // DSG-CS-103: Ctrl+Z in the Code view reverts that text group; the form is not changed; CheckOnly reports the gap.
        buffer.Undo();
        Assert.DoesNotContain($"{added}: TButton;", buffer.Text, StringComparison.Ordinal);
        Assert.Equal(model, FormWriter.Write(designer.Model!));
        Assert.Contains(designer.CodeSync.Reconcile(designer.Model!, ReconcileMode.CheckOnly).Issues,
            i => i.Kind == ReconcileIssueKind.MissingField && i.Name == added);

        // DSG-CS-082: the user edits the class freely while the designer is open (nothing locks the buffer) ...
        var at = buffer.Text.IndexOf("  private", StringComparison.Ordinal);
        buffer.ApplyEdits([new TextEdit(at, 0, "    { the user's note }\r\n")]);
        Assert.Contains("{ the user's note }", buffer.Text, StringComparison.Ordinal);

        // ... and the repair before the next mutating designer action restores consistency (DSG-CS-091).
        designer.CodeSync.RepairPending = true;
        Select(designer, "OkButton");
        designer.Surface.Execute(FreexComp.Designer.Surface.Commands.Delete);
        Assert.Contains($"    {added}: TButton;", buffer.Text, StringComparison.Ordinal);
        Assert.Contains("{ the user's note }", buffer.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(designer.CodeSync.Reconcile(designer.Model!, ReconcileMode.CheckOnly).Issues, i => i.Kind == ReconcileIssueKind.MissingField);
    }
}
