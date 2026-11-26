using System.Globalization;
using System.Text;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>The project frame catalog (frames-designer.md §2, DSG-FD-103…105; D-120).</summary>
[Trait("Phase", "8")]
public sealed class ProjectFrameCatalogTests
{
    private const string Dir = @"C:\p\Hello";
    private const string ProjectPath = Dir + @"\Hello.pproj";
    private const string FrameUnitPath = Dir + @"\AddressFrameUnit.pas";
    private const string FrameFormPath = Dir + @"\AddressFrameUnit.form";

    /// <summary>The unit of <see cref="FrameDesign.AddressFrame"/>: a class with the direct ancestor TFrame.</summary>
    internal static readonly string FrameUnit = string.Join("\r\n",
        "unit AddressFrameUnit;", "", "interface", "", "uses", "  Classes, Controls, Forms, StdCtrls, ExtCtrls;", "", "type",
        "  TAddressFrame = class(TFrame)", "    StreetLabel: TLabel;", "    StreetEdit: TEdit;", "    CityPanel: TPanel;",
        "    CityLabel: TLabel;", "    procedure StreetEditChange(Sender: TObject);", "  end;", "", "implementation", "",
        "{$FORM AddressFrameUnit.form}", "", "procedure TAddressFrame.StreetEditChange(Sender: TObject);", "begin", "end;", "",
        "initialization", "  RegisterClass(TAddressFrame);", "end.", "");

    private readonly InMemoryFileSystem _fs = new();
    private readonly ManualClock _clock = new();
    private readonly IdeServices _services;
    private readonly List<IReadOnlyList<string>> _changes = [];

    public ProjectFrameCatalogTests()
    {
        _services = TestComposition.Build(o =>
        {
            o.FileSystem = _fs;
            o.Clock = _clock;
        });
        ((DocumentService)_services.Documents).DesignCatalog = FrameDesign.Catalog;
    }

    private ProjectAncestorCatalog Frames => _services.Frames!;

    private void File(string path, string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    private List<string> Lines()
    {
        _clock.Advance(TimeSpan.FromMilliseconds(50)); // batched Output (IDE-P3-070)
        return [.. _services.Output.GetLines(OutputChannels.Designer).Select(l => l.Text)];
    }

    private static string Caption(AncestorDefinition frame) =>
        ((StringValue)PropertyAccess.GetEffective(frame.Document.FindByName("StreetLabel")!, "Caption")).Value;

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-103")]
    [Trait("Req", "DSG-FD-104")]
    [Trait("Req", "DSG-FD-105")]
    public Task ListsFrameUnits_SkipsUnreadable_RefreshesOnSaveAndEdit() => TestUiThread.RunAsync(ListsOnUiThread);

    private async Task ListsOnUiThread()
    {
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "gui", "mainSource": "Hello.pas", "files": [
              { "path": "Hello.pas" },
              { "path": "MainForm.pas", "form": "MainForm.form" },
              { "path": "AddressFrameUnit.pas", "form": "AddressFrameUnit.form" },
              { "path": "Broken.pas", "form": "Broken.form" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nuses Forms, MainForm;\nbegin\nend.\n");
        File(Dir + @"\MainForm.pas", FrameDesign.Unit);
        File(Dir + @"\MainForm.form", FrameDesign.Form);
        File(FrameUnitPath, FrameUnit);
        File(FrameFormPath, FrameDesign.AddressFrame);
        File(Dir + @"\Broken.pas", FrameUnit.Replace("AddressFrame", "BrokenFrame", StringComparison.Ordinal)); // its form is missing
        Assert.NotNull(_services.Frames);
        Assert.Same(Frames, ((DocumentService)_services.Documents).DesignFrames);
        Assert.Empty(Frames.All); // no project: no frames
        Frames.Changed += (_, e) => _changes.Add(e.ClassNames);

        // DSG-FD-103: the project's units with a form entry whose class derives directly from TFrame; the form unit is no frame.
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        var frame = Assert.Single(Frames.All);
        Assert.Equal(("TAddressFrame", "AddressFrameUnit", FrameFormPath), (frame.ClassName, frame.UnitName, frame.FormPath));
        Assert.Same(frame, Frames.Find("taddressframe"));
        Assert.Equal(FrameUnitPath, Frames.UnitPathOf("TAddressFrame"));
        Assert.Null(Frames.Find("TMainForm"));
        Assert.Equal("Street", Caption(frame));
        Assert.Equal([["TAddressFrame"]], _changes);

        // A unit whose form cannot be read is left out with one line on the Output channel designer, once per reason.
        var line = Assert.Single(Lines());
        Assert.StartsWith(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_FrameUnreadable, Dir + @"\Broken.pas", ""), line, StringComparison.Ordinal);
        Assert.Null(Frames.Find("TBrokenFrame"));

        // A refresh without a difference keeps the definitions and raises nothing (window activation: DSG-FD-104).
        _changes.Clear();
        Frames.Refresh();
        Assert.Same(frame, Frames.Find("TAddressFrame"));
        Assert.Empty(_changes);
        Assert.Single(Lines());

        // An edit on disk of a closed frame unit is read at the next refresh.
        File(FrameFormPath, FrameDesign.AddressFrame.Replace("'Street'", "'Road'", StringComparison.Ordinal));
        Frames.Refresh();
        Assert.Equal([["TAddressFrame"]], _changes);
        Assert.Equal("Road", Caption(Frames.Find("TAddressFrame")!));

        // Opened in the IDE: the definition is the open designer model; an applied edit raises Changed for its class.
        _changes.Clear();
        var document = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(FrameUnitPath)).Value);
        var designer = document.Designer;
        Assert.Same(designer.Model, Frames.Find("TAddressFrame")!.Document);
        Assert.Equal([["TAddressFrame"]], _changes);
        _changes.Clear();
        var change = new SetProperty("StreetLabel", "Caption", new StringValue("Lane"));
        var inverse = designer.Editor!.Apply([change]);
        designer.Undo.Push(new DesignerUndoUnit("Undo_Designer_Property", [change], inverse, [], SelectionSnapshot.Empty, SelectionSnapshot.Empty));
        Assert.Equal([["TAddressFrame"]], _changes);
        Assert.Equal("Lane", Caption(Frames.Find("TAddressFrame")!));

        // Saving keeps the open model's definition; closing reads the saved form from disk again.
        _changes.Clear();
        Assert.True(await _services.Documents.SaveAsync(document));
        Assert.Same(designer.Model, Frames.Find("TAddressFrame")!.Document);
        Assert.Empty(_changes);
        Assert.True(await _services.Documents.CloseAsync(document));
        Assert.Equal([["TAddressFrame"]], _changes);
        Assert.NotSame(designer.Model, Frames.Find("TAddressFrame")!.Document);
        Assert.Equal("Lane", Caption(Frames.Find("TAddressFrame")!));

        // DSG-FD-105: only the loaded project; closing it empties the list.
        _changes.Clear();
        Assert.True(await _services.Projects.CloseAsync());
        Assert.Empty(Frames.All);
        Assert.Equal([["TAddressFrame"]], _changes);
    }

    /// <summary>
    /// DSG-OI-120 / DSG-CS-117 (D-120): a double-click on a handler a frame assigns opens the frame's unit (found through the
    /// project catalog) in the Code view at that method; the form is not changed.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-117")]
    [Trait("Req", "DSG-OI-120")]
    public Task InheritedHandlerRequest_OpensFrameUnitAtMethod() => TestUiThread.RunAsync(OpensFrameUnitOnUiThread);

    private async Task OpensFrameUnitOnUiThread()
    {
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "gui", "mainSource": "Hello.pas", "files": [
              { "path": "Hello.pas" },
              { "path": "MainForm.pas", "form": "MainForm.form" },
              { "path": "AddressFrameUnit.pas", "form": "AddressFrameUnit.form" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nuses Forms, MainForm;\nbegin\nend.\n");
        File(Dir + @"\MainForm.pas", FrameDesign.Unit);
        File(Dir + @"\MainForm.form", FrameDesign.Form);
        File(FrameUnitPath, FrameUnit);
        File(FrameFormPath, FrameDesign.AddressFrame);
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        var form = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(Dir + @"\MainForm.pas")).Value);
        Assert.True(form.ShowView(DocumentView.Design));
        Assert.True(form.Designer.Model!.FindByName("BillingAddress")!.IsFrameInstance);
        var inspector = new ObjectInspectorViewModel(_services.Status);
        inspector.Bind(form.Designer);
        inspector.Page = InspectorPage.Events;
        form.Designer.Surface!.Selection.Set(["BillingAddress.StreetEdit"]);
        var text = form.Designer.CurrentFormText();

        inspector.ActivateEvent("OnChange");
        await ((DocumentService)_services.Documents).InheritedHandlerOpening;

        var frame = Assert.IsType<FormDocumentViewModel>(_services.Documents.ActiveDocument);
        Assert.Equal(FrameUnitPath, frame.FilePath);
        Assert.Equal(DocumentView.Code, frame.ActiveView);
        var lines = frame.Buffer.Text.Split("\r\n");
        var procedure = Array.IndexOf(lines, "procedure TAddressFrame.StreetEditChange(Sender: TObject);") + 1;
        Assert.InRange(frame.ViewState.Caret.Line, procedure, procedure + 2); // in the method (DSG-CS-064)
        Assert.Equal(text, form.Designer.CurrentFormText());
        Assert.False(form.IsModified);
    }

    private const string BaseUnitPath = Dir + @"\BaseDialogUnit.pas";
    private const string BaseFormInProject = Dir + @"\forms\BaseDialogUnit.form";
    private const string MainUnitPath = Dir + @"\MainForm.pas";

    /// <summary>A unit declaring <c>T&lt;Name&gt; = class(&lt;Ancestor&gt;)</c> with no members (the form file says the rest).</summary>
    private static string BareUnit(string name, string ancestor, bool frame = false) => string.Join("\r\n",
        [$"unit {name};", "", "interface", "", "uses", "  Classes, Controls, Forms;", "", "type", $"  T{name} = class({ancestor})", "  end;", "",
            "implementation", "", $"{{$FORM {name}.form}}", "", .. frame ? new[] { "initialization", $"  RegisterClass(T{name});", "" } : [], "end.", ""]);

    /// <summary>
    /// The project of the inheritance tests (D-132): the base dialog (its form in a sub-folder), the main form inheriting
    /// from it, the address frame; <paramref name="entries"/> are further <c>files</c> entries.
    /// </summary>
    private void InheritanceProject(string entries = "")
    {
        File(ProjectPath, $$"""
            { "formatVersion": 1, "name": "Hello", "appType": "gui", "mainSource": "Hello.pas", "files": [
              { "path": "Hello.pas" },
              { "path": "BaseDialogUnit.pas", "form": "forms/BaseDialogUnit.form" },
              { "path": "MainForm.pas", "form": "MainForm.form" },
              { "path": "AddressFrameUnit.pas", "form": "AddressFrameUnit.form" }{{entries}} ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nuses Forms, MainForm;\nbegin\nend.\n");
        File(BaseUnitPath, InheritedDesign.BaseUnit);
        File(BaseFormInProject, InheritedDesign.BaseDialog);
        File(MainUnitPath, InheritedDesign.Unit);
        File(Dir + @"\MainForm.form", InheritedDesign.Form);
        File(FrameUnitPath, FrameUnit);
        File(FrameFormPath, FrameDesign.AddressFrame);
    }

    private static string Caption(FormDocument document, string name) =>
        ((StringValue)PropertyAccess.GetEffective(document.FindByName(name)!, "Caption")).Value;

    private static string Caption(FormObject obj) => ((StringValue)PropertyAccess.GetEffective(obj, "Caption")).Value;

    private static void Edit(FormDocumentViewModel document, FormChange change)
    {
        var designer = document.Designer;
        var inverse = designer.Editor!.Apply([change]);
        designer.Undo.Push(new DesignerUndoUnit("Undo_Designer_Property", [change], inverse, [], SelectionSnapshot.Empty, SelectionSnapshot.Empty));
    }

    /// <summary>
    /// DSG-FD-123 (D-132): the ancestor view lists every designed unit of the project (forms and frames, whatever their
    /// ancestor) by class name; a descendant's definition is merged against its ancestor; unreadable units and repeating
    /// ancestor chains are left out with one Output line each; a refresh re-reads changed units and descendants follow.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-123")]
    [Trait("Req", "DSG-FD-104")]
    public Task Ancestors_ListsFormsAndFrames_DescendantsMerged_SkipsUnreadable_Refreshes() => TestUiThread.RunAsync(AncestorsOnUiThread);

    private async Task AncestorsOnUiThread()
    {
        InheritanceProject("""
            ,
              { "path": "Broken.pas", "form": "Broken.form" },
              { "path": "LoopA.pas", "form": "LoopA.form" },
              { "path": "LoopB.pas", "form": "LoopB.form" }
            """);
        File(Dir + @"\Broken.pas", FrameUnit.Replace("AddressFrame", "BrokenFrame", StringComparison.Ordinal)); // its form is missing
        File(Dir + @"\LoopA.pas", BareUnit("LoopA", "TLoopB"));
        File(Dir + @"\LoopA.form", "inherited LoopA: TLoopA\nend\n");
        File(Dir + @"\LoopB.pas", BareUnit("LoopB", "TLoopA"));
        File(Dir + @"\LoopB.form", "inherited LoopB: TLoopB\nend\n");
        var ancestors = Frames.Ancestors;
        Assert.Same(ancestors, ((DocumentService)_services.Documents).DesignAncestors);
        Assert.Empty(ancestors.All);
        var changes = new List<IReadOnlyList<string>>();
        ancestors.Changed += (_, e) => changes.Add(e.ClassNames);
        Frames.Changed += (_, e) => _changes.Add(e.ClassNames);

        // Every designed unit by class name; the frames view keeps the frame; both views share the definitions.
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.Equal(["TAddressFrame", "TBaseDialog", "TMainForm"], ancestors.All.Select(d => d.ClassName));
        Assert.Equal(["TAddressFrame"], Frames.All.Select(d => d.ClassName));
        Assert.Equal([["TAddressFrame"]], _changes);
        Assert.Contains(changes, c => c.Contains("TBaseDialog") && c.Contains("TMainForm") && c.Contains("TAddressFrame"));
        Assert.Same(Frames.Find("TAddressFrame"), ancestors.Find("TAddressFrame"));
        Assert.Null(Frames.Find("TMainForm"));
        var baseDialog = ancestors.Find("tbasedialog")!;
        Assert.Equal(("TBaseDialog", "BaseDialogUnit", BaseFormInProject), (baseDialog.ClassName, baseDialog.UnitName, baseDialog.FormPath));
        Assert.Equal(BaseUnitPath, Frames.UnitPathOf("TBaseDialog"));
        Assert.Equal(MainUnitPath, Frames.UnitPathOf("TMainForm"));

        // A descendant's definition is merged against its ancestor's definition.
        var main = ancestors.Find("TMainForm")!.Document;
        Assert.Empty(main.Diagnostics);
        Assert.True(main.Root.IsInheritedRoot);
        Assert.Same(baseDialog, main.Ancestor);
        Assert.Equal(("Apply", "Cancel", "Help"), (Caption(main, "OkButton"), Caption(main, "CancelButton"), Caption(main, "HelpButton")));

        // The unreadable unit and the two units of the repeating chain: one line each; none is listed.
        var lines = Lines();
        Assert.Equal(3, lines.Count);
        string Line(string unit, string reason) => string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_FrameUnreadable, unit, reason);
        Assert.Contains(lines, l => l.StartsWith(Line(Dir + @"\Broken.pas", ""), StringComparison.Ordinal));
        Assert.Contains(Line(Dir + @"\LoopA.pas", string.Format(CultureInfo.CurrentCulture, Strings.Designer_AncestorChainRepeats, "TLoopA", "TLoopA")), lines);
        Assert.Contains(Line(Dir + @"\LoopB.pas", string.Format(CultureInfo.CurrentCulture, Strings.Designer_AncestorChainRepeats, "TLoopB", "TLoopB")), lines);
        Assert.Null(ancestors.Find("TLoopA"));
        Assert.Null(Frames.UnitPathOf("TLoopB"));

        // A refresh without a difference raises nothing and reports nothing again.
        changes.Clear();
        _changes.Clear();
        Frames.Refresh();
        Assert.Empty(changes);
        Assert.Equal(3, Lines().Count);

        // The ancestor changed on disk: re-read at the next refresh; the unchanged descendant follows; the frames view is quiet.
        var mainDefinition = ancestors.Find("TMainForm");
        File(BaseFormInProject, InheritedDesign.BaseDialog.Replace("'Cancel'", "'Close'", StringComparison.Ordinal));
        Frames.Refresh();
        Assert.Contains(changes, c => c.Contains("TBaseDialog"));
        Assert.Empty(_changes);
        Assert.NotSame(baseDialog, ancestors.Find("TBaseDialog"));
        Assert.Same(mainDefinition, ancestors.Find("TMainForm"));
        Assert.Same(ancestors.Find("TBaseDialog"), main.Ancestor);
        Assert.Equal(("Apply", "Close"), (Caption(main, "OkButton"), Caption(main, "CancelButton")));

        // Only the loaded project: closing it empties both views.
        Assert.True(await _services.Projects.CloseAsync());
        Assert.Empty(ancestors.All);
        Assert.Empty(Frames.All);
    }

    /// <summary>
    /// DSG-FD-124 (D-132): a frame derived from a project frame is a frame — listed in the frames view, placeable, its
    /// instances merging the whole chain; a change of the base frame reaches an open form's instance.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-124")]
    [Trait("Req", "DSG-FD-123")]
    public Task FrameDerivedFromProjectFrameIsAFrame() => TestUiThread.RunAsync(DerivedFrameOnUiThread);

    private async Task DerivedFrameOnUiThread()
    {
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "gui", "mainSource": "Hello.pas", "files": [
              { "path": "Hello.pas" },
              { "path": "HouseForm.pas", "form": "HouseForm.form" },
              { "path": "AddressFrameUnit.pas", "form": "AddressFrameUnit.form" },
              { "path": "FancyFrame.pas", "form": "FancyFrame.form" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nuses Forms, HouseForm;\nbegin\nend.\n");
        File(FrameUnitPath, FrameUnit);
        File(FrameFormPath, FrameDesign.AddressFrame);
        File(Dir + @"\FancyFrame.pas", BareUnit("FancyFrame", "TAddressFrame", frame: true));
        File(Dir + @"\FancyFrame.form", string.Join("\n",
            "inherited FancyFrame: TFancyFrame", "  inherited StreetLabel: TLabel", "    Caption = 'Fancy street'", "  end",
            "  inherited CityPanel: TPanel", "    object CountryLabel: TLabel", "      Left = 8", "      Top = 32", "      Width = 50",
            "      Height = 15", "      Caption = 'Country'", "    end", "  end", "end", ""));
        File(Dir + @"\HouseForm.pas", string.Join("\r\n",
            "unit HouseForm;", "", "interface", "", "uses", "  Classes, Controls, Forms, FancyFrame;", "", "type",
            "  THouseForm = class(TForm)", "    House: TFancyFrame;", "  end;", "", "var", "  HouseForm: THouseForm;", "", "implementation", "",
            "{$FORM HouseForm.form}", "", "end.", ""));
        File(Dir + @"\HouseForm.form", string.Join("\n",
            "object HouseForm: THouseForm", "  Left = 200", "  Top = 120", "  ClientWidth = 640", "  ClientHeight = 400", "  PixelsPerInch = 96",
            "  inline House: TFancyFrame", "    Left = 8", "    Top = 8", "    Width = 320", "    Height = 120", "    TabOrder = 0",
            "    inherited CountryLabel: TLabel", "      Caption = 'Land'", "    end", "  end", "end", ""));
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));

        // The derived frame is in the frames view, merged against the base frame; the form is not a frame.
        Assert.Equal(["TAddressFrame", "TFancyFrame"], Frames.All.Select(f => f.ClassName));
        Assert.Equal(["TAddressFrame", "TFancyFrame", "THouseForm"], Frames.Ancestors.All.Select(f => f.ClassName));
        var fancy = Frames.Find("TFancyFrame")!;
        Assert.True(fancy.Document.Root.IsInheritedRoot);
        Assert.Same(Frames.Find("TAddressFrame"), fancy.Document.Ancestor);
        Assert.Equal(["StreetLabel", "StreetEdit", "CityPanel"], fancy.Document.Root.Children.Select(c => c.Name));
        Assert.Null(AncestorCatalog.RepeatedClass(Frames, "TFancyFrame", "THouseForm")); // placeable on the form
        Assert.Equal("TAddressFrame", AncestorCatalog.RepeatedClass(Frames, "TFancyFrame", "TAddressFrame")); // not inside its ancestor

        // Its instance on the open form merges the whole chain: base, derived frame, instance overrides.
        var house = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(Dir + @"\HouseForm.pas")).Value);
        FormObject Object(string qualified) => house.Designer.Model!.FindByQualifiedName(qualified)!;
        Assert.Empty(house.Designer.Model!.Diagnostics);
        Assert.True(Object("House").IsFrameInstance);
        Assert.Same(fancy, Object("House").Frame);
        Assert.Equal(["StreetLabel", "StreetEdit", "CityPanel"], Object("House").Children.Select(c => c.Name));
        Assert.Equal(["CityLabel", "CountryLabel"], Object("House.CityPanel").Children.Select(c => c.Name));
        Assert.Equal(("Fancy street", "Land", "City"), (Caption(Object("House.StreetLabel")), Caption(Object("House.CountryLabel")),
            Caption(Object("House.CityLabel"))));

        // The base frame changed on disk: the derived frame and the open form's instance follow at the next refresh.
        File(FrameFormPath, FrameDesign.AddressFrame.Replace("'City'", "'Town'", StringComparison.Ordinal));
        Frames.Refresh();
        Assert.Equal("Town", Caption(Frames.Find("TFancyFrame")!.Document, "CityLabel"));
        Assert.Equal("Town", Caption(Object("House.CityLabel")));
        Assert.False(house.IsModified);
    }

    /// <summary>
    /// DSG-FD-123, 134 (D-132): with the ancestor and the descendant open, an edit of the ancestor reaches the descendant
    /// at once (its overrides kept); the descendant is not modified and its form text does not change; undo follows too.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-123")]
    [Trait("Req", "DSG-FD-134")]
    public Task AncestorEditedInOpenDocument_DescendantFollowsLive() => TestUiThread.RunAsync(FollowsLiveOnUiThread);

    private async Task FollowsLiveOnUiThread()
    {
        InheritanceProject();
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        var main = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(MainUnitPath)).Value);
        var dialog = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(BaseUnitPath)).Value);
        Assert.Equal(BaseFormInProject, dialog.FormText.FilePath);
        var model = main.Designer.Model!;
        Assert.True(model.Root.IsInheritedRoot);
        Assert.Same(dialog.Designer.Model, model.Ancestor!.Document); // the open ancestor model
        Assert.Same(model, Frames.Ancestors.Find("TMainForm")!.Document);
        var text = main.Designer.CurrentFormText();

        Edit(dialog, new SetProperty("CancelButton", "Caption", new StringValue("Close")));
        Edit(dialog, new SetProperty("OkButton", "Caption", new StringValue("Yes")));
        Assert.Equal(("Yes", "Close"), (Caption(dialog.Designer.Model!, "OkButton"), Caption(dialog.Designer.Model!, "CancelButton")));
        Assert.Equal(("Apply", "Close"), (Caption(main.Designer.Model!, "OkButton"), Caption(main.Designer.Model!, "CancelButton")));
        Assert.False(main.IsModified);
        Assert.Equal(text, main.Designer.CurrentFormText());

        // Undo in the ancestor: the descendant follows back.
        Assert.True(dialog.ShowView(DocumentView.Design));
        dialog.Designer.Surface!.Execute(FreexComp.Designer.Surface.Commands.Undo);
        dialog.Designer.Surface!.Execute(FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal("Cancel", Caption(dialog.Designer.Model!, "CancelButton"));
        Assert.Equal("Cancel", Caption(main.Designer.Model!, "CancelButton"));
        Assert.False(main.IsModified);
    }

    /// <summary>
    /// DSG-OI-125, DSG-CS-117 (D-132): Show Inherited Handler on an inherited button of an open descendant opens the
    /// ancestor's unit — found through the project catalog, not beside the ancestor's form — in the Code view at the method.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-125")]
    [Trait("Req", "DSG-CS-117")]
    public Task ShowInheritedHandler_OpensAncestorUnitAtMethod() => TestUiThread.RunAsync(ShowInheritedOnUiThread);

    private async Task ShowInheritedOnUiThread()
    {
        InheritanceProject();
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        var form = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(MainUnitPath)).Value);
        Assert.True(form.ShowView(DocumentView.Design));
        Assert.True(form.Designer.Model!.FindByName("OkButton")!.IsInherited);
        var inspector = new ObjectInspectorViewModel(_services.Status);
        inspector.Bind(form.Designer);
        inspector.AttachCommands(_services.Commands, null);
        inspector.Page = InspectorPage.Events;
        form.Designer.Surface!.Selection.Set(["OkButton"]);
        inspector.CurrentEvent = "OnClick";
        var text = form.Designer.CurrentFormText();

        var show = inspector.BuildContextMenu().Single(i => i.ActionId == ObjectInspectorViewModel.ShowInheritedHandlerId);
        Assert.False(show.IsDisabled);
        show.Run!();
        await ((DocumentService)_services.Documents).InheritedHandlerOpening;

        var ancestor = Assert.IsType<FormDocumentViewModel>(_services.Documents.ActiveDocument);
        Assert.Equal(BaseUnitPath, ancestor.FilePath);
        Assert.Equal(DocumentView.Code, ancestor.ActiveView);
        var lines = ancestor.Buffer.Text.Split("\r\n");
        var procedure = Array.IndexOf(lines, "procedure TBaseDialog.OkButtonClick(Sender: TObject);") + 1;
        Assert.InRange(ancestor.ViewState.Caret.Line, procedure, procedure + 2); // in the method (DSG-CS-064)
        Assert.Equal(text, form.Designer.CurrentFormText());
        Assert.False(form.IsModified);
    }

    /// <summary>
    /// DSG-FD-134, IDE-PM-051 (D-134 item 4): with three levels (TBaseDialog, TMainForm, TMain2) and the middle level's form
    /// file not named after its unit (<c>{$FORM main-form.form}</c>), an open leaf whose ancestor's unit is removed from the
    /// project goes read-only with <c>F0112</c>; when the unit is added back (the Project Manager's Add to Project bar adds
    /// existing files) it comes back with the form its directive names and the leaf is merged against it again: no
    /// diagnostic, editable, no read-only bar, not modified.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-134")]
    public Task Remerge_AncestorUnitRemovedThenAddedBack_DescendantReturns() => TestUiThread.RunAsync(AddedBackOnUiThread);

    private async Task AddedBackOnUiThread()
    {
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "gui", "mainSource": "Hello.pas", "files": [
              { "path": "Hello.pas" },
              { "path": "BaseDialogUnit.pas", "form": "BaseDialogUnit.form" },
              { "path": "MainForm.pas", "form": "main-form.form" },
              { "path": "Main2.pas", "form": "Main2.form" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nuses Forms, MainForm, Main2;\nbegin\nend.\n");
        File(BaseUnitPath, InheritedDesign.BaseUnit);
        File(Dir + @"\BaseDialogUnit.form", InheritedDesign.BaseDialog);
        File(MainUnitPath, InheritedDesign.Unit.Replace("{$FORM MainForm.form}", "{$FORM main-form.form}", StringComparison.Ordinal));
        File(Dir + @"\main-form.form", InheritedDesign.Form);
        const string Leaf = Dir + @"\Main2.pas";
        File(Leaf, string.Join("\r\n", "unit Main2;", "", "interface", "", "uses", "  Classes, Controls, Forms, StdCtrls, ExtCtrls, MainForm;", "",
            "type", "  TMain2 = class(TMainForm)", "  end;", "", "var", "  Main2: TMain2;", "", "implementation", "", "{$FORM Main2.form}", "",
            "end.", ""));
        File(Dir + @"\Main2.form", "inherited Main2: TMain2\n  Caption = 'Leaf'\nend\n");
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        var leaf = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(Leaf)).Value);
        Assert.True(leaf.ShowView(DocumentView.Design));
        var designer = leaf.Designer;
        Assert.True(designer.Model!.Root.IsInheritedRoot);
        Assert.Empty(designer.Model.Diagnostics);
        Assert.False(designer.Surface!.IsReadOnly);

        // The ancestor's unit leaves the project: read-only with F0112 and its bar.
        Assert.True(await _services.Projects.RemoveFromProjectAsync(["MainForm.pas"]));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(Frames.Ancestors.Find("TMainForm"));
        Assert.Equal(["F0112"], designer.Model!.Diagnostics.Select(d => d.Code));
        Assert.True(designer.Surface!.IsReadOnly);
        Assert.Contains(designer.DesignInfoBars, b => b.Kind == "FormReadOnlyKind");

        // Added back: merged again, editable, the bar gone, the file untouched.
        Assert.Equal(1, await _services.Projects.AddExistingFilesAsync([MainUnitPath], reportFirstDuplicateOnly: true));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Contains(_services.Projects.Project!.Files, f => f is { Path: "MainForm.pas", Form: "main-form.form" });
        Assert.NotNull(Frames.Ancestors.Find("TMainForm"));
        Assert.Empty(designer.Model!.Diagnostics.Select(d => d.ToString()));
        Assert.True(designer.Model.Root.IsInheritedRoot);
        Assert.Same(Frames.Ancestors.Find("TMainForm"), designer.Model.Ancestor);
        Assert.False(designer.Surface!.IsReadOnly);
        Assert.DoesNotContain(designer.DesignInfoBars, b => b.Kind == "FormReadOnlyKind");
        Assert.False(leaf.IsModified);
    }

    /// <summary>
    /// DSG-FD-134, DSG-FD-027 (D-135): with a new component inside an inherited panel selected, the ancestor's unit leaves
    /// the project: the descendant is merged again into a read-only <c>F0112</c> document whose components are not
    /// placeable; the selection is chosen again by name among the components that are still placeable, else the root; the
    /// re-merged read-only root is drawn at the metadata's default client size and the status bar shows that size.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-134")]
    [Trait("Req", "DSG-FD-027")]
    public Task Remerge_AncestorUnitRemovedWithNewComponentSelected_SelectsRootAtDefaultSize() => TestUiThread.RunAsync(RemovedSelectedOnUiThread);

    private async Task RemovedSelectedOnUiThread()
    {
        InheritanceProject();
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        var form = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(MainUnitPath)).Value);
        Assert.True(form.ShowView(DocumentView.Design));
        var designer = form.Designer;
        var surface = designer.Surface!;
        surface.Selection.Set(["HelpButton"]);
        Assert.True(surface.Layout.HasBounds(surface.Selection.Primary));
        var selections = 0;
        surface.Selection.Changed += (_, _) =>
        {
            selections++;
            _ = surface.Geometry.GetBounds(surface.Selection.Primary); // every selected component is placeable
        };

        Assert.True(await _services.Projects.RemoveFromProjectAsync(["BaseDialogUnit.pas"]));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(Frames.Ancestors.Find("TBaseDialog"));
        Assert.Equal(["F0112"], designer.Model!.Diagnostics.Select(d => d.Code));
        Assert.True(designer.Surface!.IsReadOnly);
        Assert.Same(surface, designer.Surface);
        Assert.NotNull(designer.Model.FindByName("HelpButton"));
        Assert.Equal([designer.Model.Root], surface.Selection.Items);
        Assert.Equal(1, selections);

        // The read-only root stores no size: the default client size of the metadata's form class (DSG-FD-027).
        var size = FrameDesign.Catalog.All.First(c => c.Kind == FreexComp.Designer.Catalog.ComponentKind.Form).DefaultSize;
        Assert.Equal((size.Width, size.Height), (surface.Geometry.FormClientRect.Width, surface.Geometry.FormClientRect.Height));
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_Selection, "MainForm", "TMainForm", 0, 0, size.Width, size.Height),
            designer.SelectionStatusText);
        Assert.False(form.IsModified);
    }
}
