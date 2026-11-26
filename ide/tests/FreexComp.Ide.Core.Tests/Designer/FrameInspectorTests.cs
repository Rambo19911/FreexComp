using FreexComp.Designer.Editing;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using static FreexComp.Ide.Core.Tests.Designer.DesignerFixture;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>The Object Inspector over frame instances (frames-designer.md §7, DSG-OI-117…120; D-119).</summary>
[Trait("Phase", "8")]
public sealed class FrameInspectorTests
{
    private readonly DesignerFixture _f = new();

    /// <summary>The customer form of <see cref="FrameDesign"/> opened in the Design view with the address frame known.</summary>
    private async Task<(FormDocumentViewModel Document, FormDesignerViewModel Designer, ObjectInspectorViewModel Inspector)> OpenAsync(
        string? unit = null)
    {
        var documents = (DocumentService)_f.Services.Documents;
        documents.DesignCatalog = FrameDesign.Catalog;
        documents.DesignFrames = FrameDesign.Frames();
        var document = await _f.OpenAsync(unit ?? FrameDesign.Unit, FrameDesign.Form);
        var inspector = new ObjectInspectorViewModel(_f.Services.Status);
        inspector.Bind(document.Designer);
        inspector.AttachCommands(_f.Services.Commands, null);
        Assert.True(document.Designer.Model!.FindByName("BillingAddress")!.IsFrameInstance);
        return (document, document.Designer, inspector);
    }

    private static PropertyRow Row(ObjectInspectorViewModel inspector, string path) => inspector.Rows.Single(r => r.Path == path);

    private static string ResetLabel(ObjectInspectorViewModel inspector) => inspector.BuildContextMenu()[0].Label;

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-117")]
    [Trait("Req", "DSG-OI-118")]
    [Trait("Req", "DSG-OI-119")]
    public Task BoldMeansOverridden_RevertLabel_NameReadOnly() => TestUiThread.RunAsync(BoldMeansOverriddenOnUiThread);

    private async Task BoldMeansOverriddenOnUiThread()
    {
        var (document, designer, inspector) = await OpenAsync();
        var resetTitle = _f.Services.Commands.Get("objectInspector.resetToDefault").Title;

        // DSG-OI-117: an inherited object shows its effective values; bold = overridden, its geometry included.
        Select(designer, "BillingAddress.StreetLabel");
        Assert.Equal(["BillingAddress.StreetLabel"], inspector.SelectedNames);
        Assert.Equal(("Billing street", true), (Row(inspector, "Caption").Display, Row(inspector, "Caption").IsBold));
        Assert.Equal(("8", false), (Row(inspector, "Left").Display, Row(inspector, "Left").IsBold));
        Assert.Equal(("36", false), (Row(inspector, "Width").Display, Row(inspector, "Width").IsBold));

        // DSG-OI-119: its Name row is read-only; the selector lists it as <Instance>.<Name> after its instance.
        Assert.True(Row(inspector, "Name").IsReadOnly);
        Assert.Equal("StreetLabel", Row(inspector, "Name").Display);
        var items = inspector.ComponentItems;
        var at = items.ToList().IndexOf("BillingAddress: TAddressFrame");
        Assert.Equal(["BillingAddress.StreetLabel: TLabel", "BillingAddress.StreetEdit: TEdit", "BillingAddress.CityPanel: TPanel",
            "BillingAddress.CityLabel: TLabel"], items.Skip(at + 1).Take(4));
        Assert.Equal(at + 1, inspector.SelectedComponentIndex);

        // DSG-OI-118: Revert to Inherited on an overridden row (the label of the reset command), geometry rows included.
        inspector.SetCurrent("Caption");
        Assert.True(inspector.CanResetToDefault);
        Assert.Equal(Strings.Inspector_RevertToInherited, ResetLabel(inspector));
        inspector.ResetToDefault();
        Assert.Equal(("Street", false), (Row(inspector, "Caption").Display, Row(inspector, "Caption").IsBold));
        Assert.DoesNotContain("inherited StreetLabel", designer.CurrentFormText(), StringComparison.Ordinal);
        designer.Surface!.Commit(UndoTitles.Move, [new SetProperty("BillingAddress.StreetLabel", "Left", new IntegerValue(20, false))]);
        Assert.Equal(("20", true), (Row(inspector, "Left").Display, Row(inspector, "Left").IsBold));
        inspector.SetCurrent("Left");
        Assert.True(inspector.CanResetToDefault);
        inspector.ResetToDefault();
        Assert.Equal(("8", false), (Row(inspector, "Left").Display, Row(inspector, "Left").IsBold));
        inspector.SetCurrent("Width");
        Assert.False(inspector.CanResetToDefault);

        // An instance's own rows compare with the frame root; a value equal to it is not stored.
        Select(designer, "BillingAddress");
        Assert.False(Row(inspector, "Name").IsReadOnly);
        Assert.Equal(("clWindow", false), (Row(inspector, "Color").Display, Row(inspector, "Color").IsBold));
        inspector.SetCurrent("Color");
        Assert.True(inspector.BeginEdit());
        inspector.EditText = "clRed";
        Assert.True(inspector.Commit());
        Assert.True(Row(inspector, "Color").IsBold);
        Assert.Equal(Strings.Inspector_RevertToInherited, ResetLabel(inspector));
        inspector.ResetToDefault();
        Assert.Equal(("clWindow", false), (Row(inspector, "Color").Display, Row(inspector, "Color").IsBold));

        // A selection with an ordinary component keeps the usual label.
        Select(designer, "OwnPanel", "BillingAddress.StreetLabel");
        Assert.Equal(resetTitle, ResetLabel(inspector));
        Assert.True(Row(inspector, "Name").IsReadOnly); // several selected
        Assert.False(document.IsModified && designer.Undo.IsAtSavePoint);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-120")]
    [Trait("Req", "DSG-CS-113")]
    [Trait("Req", "DSG-SUR-231")]
    [Trait("Req", "DSG-OI-121")]
    public Task InheritedHandlerShown_OverrideCreatesFormMethod_RevertRestores() => TestUiThread.RunAsync(InheritedHandlerOnUiThread);

    private async Task InheritedHandlerOnUiThread()
    {
        var unit = FrameDesign.Unit
            .Replace("    OwnPanel: TPanel;\r\n", "    OwnPanel: TPanel;\r\n    procedure EditChanged(Sender: TObject);\r\n", StringComparison.Ordinal)
            .Replace("{$FORM MainForm.form}\r\n", "{$FORM MainForm.form}\r\n\r\nprocedure TMainForm.EditChanged(Sender: TObject);\r\nbegin\r\n\r\nend;\r\n",
                StringComparison.Ordinal);
        var (document, designer, inspector) = await OpenAsync(unit);
        inspector.Page = InspectorPage.Events;
        EventRow OnChange() => inspector.EventRows.Single(r => r.Name == "OnChange");

        // The frame's handler is shown as inherited; the drop-down lists the methods of the form class.
        Select(designer, "BillingAddress.StreetEdit");
        Assert.Equal(("StreetEditChange", true), (OnChange().Value, OnChange().IsInherited));
        Assert.Equal(["EditChanged"], inspector.EventMethods("OnChange"));

        // Double-click on the inherited value navigates to the frame's method; nothing changes.
        var requests = new List<(string FormPath, string Method)>();
        designer.FrameHandlerRequested += (_, e) => requests.Add((e.Frame.FormPath, e.Method));
        var text = designer.CurrentFormText();
        inspector.ActivateEvent("OnChange");
        Assert.Equal([(@"C:\p\AddressFrameUnit.form", "StreetEditChange")], requests);
        Assert.Equal(text, designer.CurrentFormText());

        // DSG-OI-121: the frame's handler name as shown is no change (no override, no method of the form).
        Assert.True(inspector.CommitEvent("OnChange", "StreetEditChange"));
        Assert.Equal(text, designer.CurrentFormText());
        Assert.DoesNotContain("StreetEditChange", document.Buffer.Text, StringComparison.Ordinal);

        // DSG-SUR-231, DSG-CS-113: the default handler of an inherited control is a method of the form, named
        // <Instance><Component><Event>, stored as an override.
        Assert.Equal("BillingAddressStreetEditChange", inspector.DefaultHandlerName("OnChange"));
        Assert.True(document.ShowView(DocumentView.Design));
        Select(designer, "BillingAddress.StreetEdit");
        designer.Surface!.Execute(FreexComp.Designer.Surface.Commands.CreateDefaultHandler);
        Assert.Contains("    procedure BillingAddressStreetEditChange(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("procedure TMainForm.BillingAddressStreetEditChange(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("    inherited StreetEdit: TEdit\n      OnChange = BillingAddressStreetEditChange\n    end\n", designer.CurrentFormText(),
            StringComparison.Ordinal);
        Assert.True(document.ShowView(DocumentView.Design));
        Select(designer, "BillingAddress.StreetEdit");
        Assert.Equal(("BillingAddressStreetEditChange", false), (OnChange().Value, OnChange().IsInherited));

        // Revert to Inherited brings the frame's handler back.
        designer.Surface!.Execute(FreexComp.Designer.Surface.Commands.RevertToInherited);
        Assert.Equal(("StreetEditChange", true), (OnChange().Value, OnChange().IsInherited));

        // Choosing a method of the form stores an override; reverting it makes the now unused method a removal candidate
        // (DSG-CS-071; D-120); clearing it reverts too.
        Assert.True(inspector.CommitEvent("OnChange", "EditChanged"));
        Assert.Equal(("EditChanged", false), (OnChange().Value, OnChange().IsInherited));
        Assert.DoesNotContain("EditChanged", designer.CodeSync.HandlerCandidates);
        designer.Surface!.Execute(FreexComp.Designer.Surface.Commands.RevertToInherited);
        Assert.Equal(("StreetEditChange", true), (OnChange().Value, OnChange().IsInherited));
        Assert.Contains("EditChanged", designer.CodeSync.HandlerCandidates);
        Assert.True(inspector.CommitEvent("OnChange", "EditChanged"));
        Assert.Equal(("EditChanged", false), (OnChange().Value, OnChange().IsInherited));
        Assert.True(inspector.CommitEvent("OnChange", ""));
        Assert.Equal(("StreetEditChange", true), (OnChange().Value, OnChange().IsInherited));
        Assert.DoesNotContain("inherited StreetEdit", designer.CurrentFormText(), StringComparison.Ordinal);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-111")]
    [Trait("Req", "DSG-SUR-235")]
    public Task Remerge_KeepsSelectionByQualifiedName_NotModified() => TestUiThread.RunAsync(RemergeOnUiThread);

    private async Task RemergeOnUiThread()
    {
        var (document, designer, inspector) = await OpenAsync();
        Select(designer, "BillingAddress.StreetEdit", "OwnPanel", "BillingAddress.CityLabel", "BillingAddress.StreetLabel");
        var before = designer.Model!.FindByQualifiedName("BillingAddress.StreetLabel");
        var text = designer.CurrentFormText();
        Assert.False(document.IsModified);

        // The frame is changed in its own designer: the instance is merged again with new inherited objects.
        var frame = designer.Frames!.Find("TAddressFrame")!.Document;
        new FormEditor(frame, FrameDesign.Catalog).Apply(new SetProperty("StreetEdit", "Width", new IntegerValue(200, false)));

        var after = designer.Model.FindByQualifiedName("BillingAddress.StreetLabel");
        Assert.NotSame(before, after);
        Assert.Equal(["BillingAddress.StreetEdit", "OwnPanel", "BillingAddress.CityLabel", "BillingAddress.StreetLabel"], inspector.SelectedNames);
        Assert.Same(after, designer.Surface!.Selection.Primary);
        Assert.Equal(200, ((IntegerValue)PropertyAccess.GetEffective(after!.Parent!.Children.Single(c => c.Name == "StreetEdit"), "Width")).Value);
        Assert.False(document.IsModified);
        Assert.False(designer.IsFormModified);
        Assert.Equal(text, designer.CurrentFormText());
        Assert.True(document.ShowView(DocumentView.Text));
        Assert.Equal(text, document.FormText.Buffer.Text);
        Assert.False(document.IsModified);

        // The frame loses the label: its override block is preserved (F0109); the selection drops it, the file is unchanged
        // and the problem is still found at its line of the text.
        Assert.True(document.ShowView(DocumentView.Design));
        new FormEditor(frame, FrameDesign.Catalog).Apply(new RemoveObject("StreetLabel"));
        Assert.Equal(["BillingAddress.StreetEdit", "OwnPanel", "BillingAddress.CityLabel"], inspector.SelectedNames);
        Assert.False(document.IsModified);
        Assert.Equal(text, designer.CurrentFormText());
        var problem = designer.Model.Diagnostics.Single(d => d.Code == "F0109");
        designer.GoToProblem(problem);
        Assert.Equal(DocumentView.Text, designer.ActiveView);
        Assert.Equal(new TextPosition(problem.Line, problem.Col), document.FormText.ViewState.Caret);
        var (offset, length, _) = document.FormText.Buffer.GetLine(problem.Line);
        Assert.StartsWith("    inherited StreetLabel", document.FormText.Buffer.Text.Substring(offset, length), StringComparison.Ordinal);
        Assert.False(document.IsModified);
    }
}
