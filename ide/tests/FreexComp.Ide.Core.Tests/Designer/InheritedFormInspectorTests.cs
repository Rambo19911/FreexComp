using System.Globalization;
using FreexComp.Designer.Inspector;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using static FreexComp.Ide.Core.Tests.Designer.DesignerFixture;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>The Object Inspector over a descendant form (form-inheritance-designer.md §6, DSG-OI-122…126, DSG-CS-120…122; D-131).</summary>
[Trait("Phase", "8")]
public sealed class InheritedFormInspectorTests
{
    private readonly DesignerFixture _f = new();

    /// <summary><see cref="InheritedDesign.Form"/> opened in the Design view with its ancestor known.</summary>
    private async Task<(FormDocumentViewModel Document, FormDesignerViewModel Designer, ObjectInspectorViewModel Inspector)> OpenAsync(
        string? unit = null, string? form = null)
    {
        var documents = (DocumentService)_f.Services.Documents;
        documents.DesignCatalog = FrameDesign.Catalog;
        documents.DesignAncestors = InheritedDesign.Ancestors();
        var document = await _f.OpenAsync(unit ?? InheritedDesign.Unit, form ?? InheritedDesign.Form);
        var inspector = new ObjectInspectorViewModel(_f.Services.Status);
        inspector.Bind(document.Designer);
        inspector.AttachCommands(_f.Services.Commands, null);
        Assert.True(document.Designer.Model!.Root.IsInheritedRoot);
        return (document, document.Designer, inspector);
    }

    private static PropertyRow Row(ObjectInspectorViewModel inspector, string path) => inspector.Rows.Single(r => r.Path == path);

    private static (string, bool) Shown(ObjectInspectorViewModel inspector, string path) => (Row(inspector, path).Display, Row(inspector, path).IsBold);

    private static string ResetLabel(ObjectInspectorViewModel inspector) => inspector.BuildContextMenu()[0].Label;

    private const string Implementation = "procedure TMainForm.OkButtonClick(Sender: TObject);\r\nbegin\r\n  inherited;\r\n  \r\nend;\r\n";

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-122")]
    [Trait("Req", "DSG-OI-123")]
    [Trait("Req", "DSG-OI-124")]
    [Trait("Req", "DSG-CS-123")]
    public Task BoldMeansOverridden_RevertLabel_NameReadOnly_RootNameEditable() => TestUiThread.RunAsync(BoldOnUiThread);

    private async Task BoldOnUiThread()
    {
        var (document, designer, inspector) = await OpenAsync();
        var resetTitle = _f.Services.Commands.Get("objectInspector.resetToDefault").Title;

        // DSG-OI-122: an inherited component shows its effective values; bold = stored by the descendant.
        Select(designer, "OkButton");
        Assert.Equal(("Apply", true), Shown(inspector, "Caption"));
        Assert.Equal(("230", false), Shown(inspector, "Left"));
        Assert.Equal(("75", false), Shown(inspector, "Width"));

        // DSG-OI-124: its Name is read-only with the message as tooltip; the selector lists it with its ancestor class.
        Assert.True(Row(inspector, "Name").IsReadOnly);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_InheritedComponentFixed, "TBaseDialog"), inspector.Grid.FixedNameTip);
        Assert.Equal(["MainForm: TMainForm", "ButtonPanel: TPanel", "CancelButton: TButton", "HelpButton: TButton", "NameEdit: TEdit",
            "OkButton: TButton", "TitleLabel: TLabel"], inspector.ComponentItems);
        Assert.Equal([null, "TBaseDialog", "TBaseDialog", null, null, "TBaseDialog", "TBaseDialog"], inspector.ComponentFrames);

        // DSG-OI-123: Revert to Inherited removes the override.
        inspector.SetCurrent("Caption");
        Assert.True(inspector.CanResetToDefault);
        Assert.Equal(Strings.Inspector_RevertToInherited, ResetLabel(inspector));
        inspector.ResetToDefault();
        Assert.Equal(("OK", false), Shown(inspector, "Caption"));
        Assert.DoesNotContain("inherited OkButton", designer.CurrentFormText(), StringComparison.Ordinal);
        inspector.SetCurrent("Left");
        Assert.False(inspector.CanResetToDefault);

        // The inherited root: geometry is not bold unless overridden; its caption is; its Name is editable.
        Select(designer, "MainForm");
        Assert.Equal(("Main", true), Shown(inspector, "Caption"));
        Assert.Equal(("200", false), Shown(inspector, "Left"));
        Assert.Equal(("120", false), Shown(inspector, "Top"));
        Assert.Equal(("400", false), Shown(inspector, "ClientWidth"));
        Assert.Equal(Strings.Inspector_RevertToInherited, ResetLabel(inspector));
        inspector.SetCurrent("ClientWidth");
        Assert.False(inspector.CanResetToDefault);
        Assert.True(inspector.BeginEdit());
        inspector.EditText = "480";
        Assert.True(inspector.Commit());
        Assert.Equal(("480", true), Shown(inspector, "ClientWidth"));
        Assert.True(inspector.CanResetToDefault);
        inspector.ResetToDefault();
        Assert.Equal(("400", false), Shown(inspector, "ClientWidth"));
        Assert.False(Row(inspector, "Name").IsReadOnly);

        // A new component keeps the usual label and an editable Name.
        Select(designer, "NameEdit");
        Assert.Equal(resetTitle, ResetLabel(inspector));
        Assert.False(Row(inspector, "Name").IsReadOnly);
        Assert.Null(inspector.Grid.FixedNameTip);
        Select(designer, "NameEdit", "OkButton");
        Assert.Equal(resetTitle, ResetLabel(inspector));

        // DSG-CS-123: renaming a new component renames its field; renaming the root renames the class and the header.
        Select(designer, "NameEdit");
        inspector.SetCurrent("Name");
        Assert.True(inspector.BeginEdit());
        inspector.EditText = "TitleEdit";
        Assert.True(inspector.Commit());
        Assert.Contains("    TitleEdit: TEdit;", document.Buffer.Text, StringComparison.Ordinal);
        Select(designer, "MainForm");
        inspector.SetCurrent("Name");
        Assert.True(inspector.BeginEdit());
        inspector.EditText = "EditDialog";
        Assert.True(inspector.Commit());
        Assert.Contains("  TEditDialog = class(TBaseDialog)", document.Buffer.Text, StringComparison.Ordinal);
        Assert.StartsWith("inherited EditDialog: TEditDialog\n", designer.CurrentFormText(), StringComparison.Ordinal);
        Assert.True(designer.Model!.Root.IsInheritedRoot);
        Assert.Equal(("Main", true), Shown(inspector, "Caption"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-125")]
    [Trait("Req", "DSG-CS-120")]
    [Trait("Req", "DSG-CS-121")]
    [Trait("Req", "DSG-SUR-244")]
    public Task InheritedHandler_DoubleClickCreatesHidingStubWithInherited_NoFormLine() => TestUiThread.RunAsync(HidingStubOnUiThread);

    private async Task HidingStubOnUiThread()
    {
        var (document, designer, inspector) = await OpenAsync();
        inspector.Page = InspectorPage.Events;
        EventRow OnClick() => inspector.EventRows.Single(r => r.Name == "OnClick");

        // DSG-OI-125: the ancestor's handler is shown as inherited.
        Select(designer, "OkButton");
        Assert.Equal(("OkButtonClick", true), (OnClick().Value, OnClick().IsInherited));

        // Double-click: the hiding method with an inherited call is created in one "Event" unit; no form line.
        var form = designer.CurrentFormText();
        var count = designer.Undo.UndoCount;
        inspector.ActivateEvent("OnClick");
        Assert.Contains("    procedure OkButtonClick(Sender: TObject);\r\n", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("\r\n" + Implementation, document.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(form, designer.CurrentFormText());
        Assert.Equal(count + 1, designer.Undo.UndoCount);
        Assert.Equal("Undo_Designer_Event", designer.Undo.UndoTitleKey);
        Assert.Contains("OkButtonClick", designer.CodeSync.HandlerCandidates);

        // The caret: the end of the empty indented line.
        Assert.Equal(DocumentView.Code, designer.ActiveView);
        var stub = document.Buffer.Text.IndexOf(Implementation, StringComparison.Ordinal);
        var empty = document.Buffer.Text.IndexOf("  \r\nend;", stub, StringComparison.Ordinal) + 2;
        Assert.Equal(document.Buffer.GetPosition(empty), document.ViewState.Caret);

        // The row still shows the inherited handler; a second double-click only navigates.
        Assert.True(document.ShowView(DocumentView.Design));
        Select(designer, "OkButton");
        Assert.Equal(("OkButtonClick", true), (OnClick().Value, OnClick().IsInherited));
        var unit = document.Buffer.Text;
        count = designer.Undo.UndoCount;
        inspector.ActivateEvent("OnClick");
        Assert.Equal(unit, document.Buffer.Text);
        Assert.Equal(count, designer.Undo.UndoCount);
        Assert.Equal(DocumentView.Code, designer.ActiveView);

        // DSG-SUR-244: the default handler of the inherited root is its ancestor's FormCreate, hidden the same way.
        Assert.True(document.ShowView(DocumentView.Design));
        Select(designer, "MainForm");
        designer.Surface!.Execute(FreexComp.Designer.Surface.Commands.CreateDefaultHandler);
        Assert.Contains("procedure TMainForm.FormCreate(Sender: TObject);\r\nbegin\r\n  inherited;\r\n  \r\nend;\r\n", document.Buffer.Text,
            StringComparison.Ordinal);
        Assert.Equal(form, designer.CurrentFormText());

        // Undo removes that stub again (one unit).
        Assert.True(document.ShowView(DocumentView.Design));
        designer.Surface!.Execute("edit.undo");
        Assert.DoesNotContain("TMainForm.FormCreate", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains(Implementation, document.Buffer.Text, StringComparison.Ordinal);

        // DSG-CS-121: an untouched stub counts as empty and goes on save.
        Assert.True(await _f.Services.Documents.SaveAsync(document));
        Assert.DoesNotContain("OkButtonClick", _f.Disk(UnitPath), StringComparison.Ordinal);
        Assert.Equal(form, _f.Disk(FormPath));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-125")]
    [Trait("Req", "DSG-OI-126")]
    [Trait("Req", "DSG-CS-122")]
    public Task InheritedHandler_NewNameStoresOverride_RevertRestores_ShowInheritedOpensAncestor() => TestUiThread.RunAsync(NewNameOnUiThread);

    private async Task NewNameOnUiThread()
    {
        var (document, designer, inspector) = await OpenAsync();
        inspector.Page = InspectorPage.Events;
        EventRow OnClick() => inspector.EventRows.Single(r => r.Name == "OnClick");
        var requests = new List<(string FormPath, string Method)>();
        designer.InheritedHandlerRequested += (_, e) => requests.Add((e.Definition.FormPath, e.Method));
        MenuItemModel ShowInherited() => inspector.BuildContextMenu().Single(i => i.ActionId == ObjectInspectorViewModel.ShowInheritedHandlerId);

        // Show Inherited Handler follows the reset item; it opens the ancestor's method and changes nothing.
        Select(designer, "OkButton");
        inspector.CurrentEvent = "OnClick";
        Assert.Equal(ObjectInspectorViewModel.ShowInheritedHandlerId, inspector.BuildContextMenu()[1].ActionId);
        Assert.Equal(Strings.Inspector_ShowInheritedHandler, ShowInherited().Label);
        Assert.False(ShowInherited().IsDisabled);
        var form = designer.CurrentFormText();
        ShowInherited().Run!();
        Assert.Equal([(InheritedDesign.BaseFormPath, "OkButtonClick")], requests);
        Assert.Equal(form, designer.CurrentFormText());

        // DSG-OI-126: the inherited name is no change; a different one stores an override and an ordinary stub.
        Assert.True(inspector.CommitEvent("OnClick", "OkButtonClick"));
        Assert.Equal(form, designer.CurrentFormText());
        Assert.DoesNotContain("OkButtonClick", document.Buffer.Text, StringComparison.Ordinal);
        Assert.True(inspector.CommitEvent("OnClick", "ApplyClick"));
        Assert.Contains("    inherited OkButton: TButton\n      Caption = 'Apply'\n      OnClick = ApplyClick\n    end\n", designer.CurrentFormText(),
            StringComparison.Ordinal);
        Assert.Contains("procedure TMainForm.ApplyClick(Sender: TObject);\r\nbegin\r\n  \r\nend;\r\n", document.Buffer.Text, StringComparison.Ordinal);
        Assert.True(document.ShowView(DocumentView.Design));
        Select(designer, "OkButton");
        Assert.Equal(("ApplyClick", false), (OnClick().Value, OnClick().IsInherited));
        inspector.CurrentEvent = "OnClick";
        Assert.True(ShowInherited().IsDisabled);

        // Clearing the value removes only that line; the created method stays a removal candidate (DSG-CS-071).
        Assert.True(inspector.CommitEvent("OnClick", ""));
        Assert.Equal(("OkButtonClick", true), (OnClick().Value, OnClick().IsInherited));
        Assert.Equal(form, designer.CurrentFormText());
        Assert.Contains("ApplyClick", designer.CodeSync.HandlerCandidates);

        // Revert to Inherited on the component removes its overrides, the event line among them.
        Assert.True(inspector.CommitEvent("OnClick", "ApplyClick"));
        Assert.Equal(("ApplyClick", false), (OnClick().Value, OnClick().IsInherited));
        designer.Surface!.Execute(FreexComp.Designer.Surface.Commands.RevertToInherited);
        Assert.Equal(("OkButtonClick", true), (OnClick().Value, OnClick().IsInherited));
        Assert.DoesNotContain("inherited OkButton", designer.CurrentFormText(), StringComparison.Ordinal);
        Assert.Contains("ApplyClick", designer.CodeSync.HandlerCandidates);

        // DSG-CS-122: an event without an ancestor handler gets an ordinary handler whose name avoids the chain's names.
        Select(designer, "CancelButton");
        Assert.Equal(("", false), (OnClick().Value, OnClick().IsInherited));
        inspector.CurrentEvent = "OnClick";
        Assert.True(ShowInherited().IsDisabled);
        Assert.Equal("CancelButtonClick2", inspector.DefaultHandlerName("OnClick"));
        inspector.ActivateEvent("OnClick");
        Assert.Contains("    inherited CancelButton: TButton\n      OnClick = CancelButtonClick2\n    end\n", designer.CurrentFormText(),
            StringComparison.Ordinal);
        Assert.Contains("procedure TMainForm.CancelButtonClick2(Sender: TObject);\r\nbegin\r\n  \r\nend;\r\n", document.Buffer.Text,
            StringComparison.Ordinal);
        Assert.True(document.ShowView(DocumentView.Design));
        Select(designer, "NameEdit");
        Assert.Equal("NameEditChange", inspector.DefaultHandlerName("OnChange"));
        Select(designer, "MainForm");
        Assert.Equal("FormClick", inspector.DefaultHandlerName("OnClick"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-145")]
    public Task ReadOnlyDescendant_InfoBarShowsDiagnostic() => TestUiThread.RunAsync(ReadOnlyOnUiThread);

    private async Task ReadOnlyOnUiThread()
    {
        // The ancestor is not listed: F0112, the whole form read-only with the diagnostic's text in the read-only bar.
        var documents = (DocumentService)_f.Services.Documents;
        documents.DesignCatalog = FrameDesign.Catalog;
        documents.DesignAncestors = new FreexComp.Designer.Model.AncestorCatalog();
        var document = await _f.OpenAsync(InheritedDesign.Unit, InheritedDesign.Form);
        var designer = document.Designer;
        var problem = designer.Model!.Diagnostics.Single(d => d.Code == "F0112");
        Assert.True(designer.Surface!.IsReadOnly);
        var bar = designer.DesignInfoBars.Single(b => b.Kind == "FormReadOnlyKind");
        Assert.Equal(problem.Message, bar.Text);
        Assert.Contains(bar.Actions, a => a.Name == "ViewAsText");
        Assert.True(document.ShowView(DocumentView.Text));
        Assert.False(document.FormText.Buffer.IsReadOnly);
    }

    /// <summary>
    /// DSG-SUR-096, 240 (D-133): a TabOrder entry in the Object Inspector renumbers only the new siblings of a descendant;
    /// the inherited ones keep theirs (no override line is written for them), like the surface's renumbering. An inherited
    /// control whose own TabOrder is entered stores it as an override.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-096")]
    [Trait("Req", "DSG-SUR-240")]
    public Task TabOrderEdit_RenumbersNewSiblingsOnly() => TestUiThread.RunAsync(TabOrderOnUiThread);

    private async Task TabOrderOnUiThread()
    {
        // The inherited panel holds OkButton 0, CancelButton 1 (inherited), HelpButton 2 and ExtraButton 3 (new).
        var form = InheritedDesign.Form.Replace("      TabOrder = 2\n    end\n", "      TabOrder = 2\n    end\n    object ExtraButton: TButton\n"
            + "      Left = 88\n      Top = 8\n      Width = 75\n      Height = 25\n      Caption = 'Extra'\n      TabOrder = 3\n    end\n",
            StringComparison.Ordinal);
        var unit = InheritedDesign.Unit.Replace("    HelpButton: TButton;", "    HelpButton: TButton;\r\n    ExtraButton: TButton;", StringComparison.Ordinal);
        var (_, designer, inspector) = await OpenAsync(unit, form);
        var model = designer.Model!;
        var original = designer.CurrentFormText();
        string? Stored(string name) => model.FindByName(name)!.FindProperty("TabOrder")?.Value.ToString();
        int Value(string name) => FreexComp.Designer.Model.PropertyAccess.GetInt(model.FindByName(name)!, "TabOrder");

        // A new button moved to the front: the other new one is renumbered, the inherited ones keep their values.
        Select(designer, "ExtraButton");
        inspector.SetCurrent("TabOrder");
        Assert.True(inspector.BeginEdit());
        inspector.EditText = "0";
        Assert.True(inspector.Commit());
        Assert.Equal((0, 3), (Value("ExtraButton"), Value("HelpButton")));
        Assert.Equal((0, 1), (Value("OkButton"), Value("CancelButton")));
        Assert.Null(Stored("OkButton"));
        Assert.Null(Stored("CancelButton"));
        Assert.DoesNotContain("inherited CancelButton", designer.CurrentFormText(), StringComparison.Ordinal);
        Assert.DoesNotContain("      TabOrder = 1\n", designer.CurrentFormText(), StringComparison.Ordinal);
        Assert.Equal(1, designer.Undo.UndoCount);
        designer.Editor!.Apply(designer.Undo.Undo()!.Undo);
        Assert.Equal(original, designer.CurrentFormText());

        // An inherited button entered at the front stores its own value; its inherited sibling keeps its value.
        Select(designer, "CancelButton");
        inspector.SetCurrent("TabOrder");
        Assert.True(inspector.BeginEdit());
        inspector.EditText = "0";
        Assert.True(inspector.Commit());
        Assert.Equal(0, Value("CancelButton"));
        Assert.Null(Stored("OkButton"));
        Assert.NotNull(Stored("CancelButton"));
        Assert.Equal((2, 3), (Value("HelpButton"), Value("ExtraButton")));
    }
}
