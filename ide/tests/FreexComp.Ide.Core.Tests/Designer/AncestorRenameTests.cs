using System.Text;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>
/// D-137: renaming a component of an ancestor form in the designer renames the inherited component in every descendant
/// form of the project (any depth) — the <c>inherited</c> headers and the component references of the descendants'
/// lines — as one undoable change with the ancestor's rename; open descendants follow in their tabs (DSG-FD-134).
/// </summary>
[Trait("Phase", "8")]
public sealed class AncestorRenameTests
{
    private const string Dir = @"C:\p\Hello";
    private const string ProjectPath = Dir + @"\Hello.pproj";
    private const string BaseUnitPath = Dir + @"\BaseDialogUnit.pas";
    private const string BaseFormPath = Dir + @"\forms\BaseDialogUnit.form";
    private const string MainUnitPath = Dir + @"\MainForm.pas";
    private const string MainFormPath = Dir + @"\MainForm.form";
    private const string LeafUnitPath = Dir + @"\Main2.pas";
    private const string LeafFormPath = Dir + @"\Main2.form";
    private const string OtherUnitPath = Dir + @"\OtherForm.pas";
    private const string OtherFormPath = Dir + @"\OtherForm.form";
    private const string PlainUnitPath = Dir + @"\PlainForm.pas";
    private const string PlainFormPath = Dir + @"\PlainForm.form";

    /// <summary>Level 3: overrides the OK button that level 1 declares and level 2 overrides.</summary>
    private const string Leaf = "inherited Main2: TMain2\n  inherited ButtonPanel: TPanel\n    inherited OkButton: TButton\n      Caption = 'Leaf'\n    end\n  end\nend\n";

    /// <summary>A second direct descendant that moves the OK button.</summary>
    private const string Other = "inherited OtherForm: TOtherForm\n  inherited ButtonPanel: TPanel\n    inherited OkButton: TButton\n      Left = 100\n    end\n  end\nend\n";

    /// <summary>A direct descendant that does not mention the OK button.</summary>
    private const string Plain = "inherited PlainForm: TPlainForm\n  Caption = 'Plain'\nend\n";

    private readonly InMemoryFileSystem _fs = new();
    private readonly ManualClock _clock = new();
    private readonly IdeServices _services;

    public AncestorRenameTests()
    {
        _services = TestComposition.Build(o =>
        {
            o.FileSystem = _fs;
            o.Clock = _clock;
        });
        ((DocumentService)_services.Documents).DesignCatalog = FrameDesign.Catalog;
    }

    private ProjectFrameCatalog Frames => _services.Frames!;

    private void File(string path, string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    private string Disk(string path) => Encoding.UTF8.GetString(_fs.ReadAllBytes(path));

    private static string Renamed(string text) => text.Replace("inherited OkButton:", "inherited YesButton:", StringComparison.Ordinal);

    /// <summary>A unit declaring <c>T&lt;Name&gt; = class(&lt;Ancestor&gt;)</c> with <paramref name="members"/>.</summary>
    private static string BareUnit(string name, string ancestor, params string[] members) => string.Join("\r\n",
        [$"unit {name};", "", "interface", "", "uses", "  Classes, Controls, Forms, StdCtrls, ExtCtrls;", "", "type",
            $"  T{name} = class({ancestor})", .. members.Select(m => "    " + m), "  end;", "", "implementation", "", $"{{$FORM {name}.form}}", "",
            "end.", ""]);

    /// <summary>
    /// The base dialog (its form in a sub-folder), the main form inheriting from it (<paramref name="mainForm"/>), a leaf
    /// inheriting from the main form, another descendant overriding the OK button and one that does not mention it.
    /// </summary>
    private void Project(string? mainForm = null, string? mainUnit = null)
    {
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "gui", "mainSource": "Hello.pas", "files": [
              { "path": "Hello.pas" },
              { "path": "BaseDialogUnit.pas", "form": "forms/BaseDialogUnit.form" },
              { "path": "MainForm.pas", "form": "MainForm.form" },
              { "path": "Main2.pas", "form": "Main2.form" },
              { "path": "OtherForm.pas", "form": "OtherForm.form" },
              { "path": "PlainForm.pas", "form": "PlainForm.form" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nuses Forms, MainForm;\nbegin\nend.\n");
        File(BaseUnitPath, InheritedDesign.BaseUnit);
        File(BaseFormPath, InheritedDesign.BaseDialog);
        File(MainUnitPath, mainUnit ?? InheritedDesign.Unit);
        File(MainFormPath, mainForm ?? InheritedDesign.Form);
        File(LeafUnitPath, BareUnit("Main2", "TMainForm"));
        File(LeafFormPath, Leaf);
        File(OtherUnitPath, BareUnit("OtherForm", "TBaseDialog"));
        File(OtherFormPath, Other);
        File(PlainUnitPath, BareUnit("PlainForm", "TBaseDialog"));
        File(PlainFormPath, Plain);
    }

    /// <summary>Opens the base dialog in the Design view with an Object Inspector bound to it.</summary>
    private async Task<(FormDocumentViewModel Base, ObjectInspectorViewModel Inspector)> OpenBaseAsync()
    {
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        var document = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(BaseUnitPath)).Value);
        Assert.True(document.ShowView(DocumentView.Design));
        var inspector = new ObjectInspectorViewModel(_services.Status);
        inspector.Bind(document.Designer);
        return (document, inspector);
    }

    /// <summary>Renames the base dialog's OK button through the Object Inspector's Name row.</summary>
    private static void RenameOkButton(FormDocumentViewModel document, ObjectInspectorViewModel inspector, string name = "YesButton")
    {
        document.Designer.Surface!.Selection.Set(["OkButton"]);
        inspector.Page = InspectorPage.Properties;
        inspector.CurrentPath = "Name";
        Assert.True(inspector.BeginEdit());
        inspector.EditText = name;
        Assert.True(inspector.Commit());
    }

    private static void Execute(FormDocumentViewModel document, string command) => document.Designer.Surface!.Execute(command);

    private static string Caption(FormDocument document, string name) =>
        ((StringValue)PropertyAccess.GetEffective(document.FindByName(name)!, "Caption")).Value;

    /// <summary>Merged without diagnostics or preserved blocks; <paramref name="name"/> is inherited, <paramref name="gone"/> absent.</summary>
    private static void AssertResolved(FormDocument document, string name, string gone = "OkButton")
    {
        Assert.Empty(document.Diagnostics.Select(d => d.ToString()));
        Assert.DoesNotContain(document.AllObjects(), o => o.IsPreserved || o.PreservedBlocks.Count > 0);
        Assert.True(document.FindByName(name)!.IsInherited);
        Assert.Null(document.FindByName(gone));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-134")]
    public Task RenameRewritesInheritedHeadersOfEveryDescendant() => TestUiThread.RunAsync(RewritesOnUiThread);

    private async Task RewritesOnUiThread()
    {
        Project();
        var (document, inspector) = await OpenBaseAsync();
        var plainBytes = _fs.ReadAllBytes(PlainFormPath);

        RenameOkButton(document, inspector);

        // Every descendant form of the project, any depth, names the component by its new name; nothing else changed.
        Assert.NotNull(document.Designer.Model!.FindByName("YesButton"));
        Assert.Equal(Renamed(InheritedDesign.Form), Disk(MainFormPath));
        Assert.Equal(Renamed(Leaf), Disk(LeafFormPath));
        Assert.Equal(Renamed(Other), Disk(OtherFormPath));
        Assert.Equal(plainBytes, _fs.ReadAllBytes(PlainFormPath)); // no line names the button: the file is not written
        Assert.Contains("    YesButton: TButton;", document.Buffer.Text, StringComparison.Ordinal); // the ancestor's field (DSG-CS-045)

        // The project's ancestor view merges every descendant against the renamed ancestor without preserved blocks.
        Frames.Refresh();
        foreach (var className in (string[])["TMainForm", "TMain2", "TOtherForm"])
        {
            AssertResolved(Frames.Ancestors.Find(className)!.Document, "YesButton");
        }

        Assert.Equal("Leaf", Caption(Frames.Ancestors.Find("TMain2")!.Document, "YesButton"));
        Assert.Equal("Apply", Caption(Frames.Ancestors.Find("TMainForm")!.Document, "YesButton"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-082")]
    [Trait("Req", "DSG-FD-134")]
    public Task EventBindingReferencesFollow() => TestUiThread.RunAsync(ReferencesOnUiThread);

    private async Task ReferencesOnUiThread()
    {
        // The main form binds the OK button's OnClick to its own handler, makes the button its ActiveControl and points a
        // new label's FocusControl at it.
        var form = InheritedDesign.Form
            .Replace("  Caption = 'Main'\n", "  ActiveControl = OkButton\n  Caption = 'Main'\n", StringComparison.Ordinal)
            .Replace("      Caption = 'Apply'\n", "      Caption = 'Apply'\n      OnClick = ApplyClick\n", StringComparison.Ordinal)
            .Replace("  object NameEdit: TEdit\n", string.Join("\n", "  object NameLabel: TLabel", "    Left = 8", "    Top = 60", "    Width = 40",
                "    Height = 15", "    Caption = 'Name'", "    FocusControl = OkButton", "  end", "  object NameEdit: TEdit", ""), StringComparison.Ordinal);
        var unit = InheritedDesign.Unit
            .Replace("    NameEdit: TEdit;\r\n", "    NameEdit: TEdit;\r\n    NameLabel: TLabel;\r\n    procedure ApplyClick(Sender: TObject);\r\n", StringComparison.Ordinal)
            .Replace("{$FORM MainForm.form}\r\n", "{$FORM MainForm.form}\r\n\r\nprocedure TMainForm.ApplyClick(Sender: TObject);\r\nbegin\r\nend;\r\n", StringComparison.Ordinal);
        Project(form, unit);
        var (document, inspector) = await OpenBaseAsync();
        Assert.Empty(Frames.Ancestors.Find("TMainForm")!.Document.Diagnostics.Select(d => d.ToString()));

        RenameOkButton(document, inspector);

        // The component references follow; the event binding stays bound to the same handler, now under the new name.
        var text = Disk(MainFormPath);
        Assert.Contains("  ActiveControl = YesButton\n", text, StringComparison.Ordinal);
        Assert.Contains("    FocusControl = YesButton\n", text, StringComparison.Ordinal);
        Assert.Contains("    inherited YesButton: TButton\n      Caption = 'Apply'\n      OnClick = ApplyClick\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OkButton", text, StringComparison.Ordinal);
        Frames.Refresh();
        var main = Frames.Ancestors.Find("TMainForm")!.Document;
        AssertResolved(main, "YesButton");
        Assert.Equal(new IdentValue("YesButton"), main.FindByName("NameLabel")!.FindProperty("FocusControl")!.Value);
        Assert.Equal(new IdentValue("ApplyClick"), main.FindByName("YesButton")!.FindProperty("OnClick")!.Value);

        // The ancestor's own handler binding is untouched (DSG-FD-082: handler names stay).
        Assert.Equal(new IdentValue("OkButtonClick"), document.Designer.Model!.FindByName("YesButton")!.FindProperty("OnClick")!.Value);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-134")]
    public Task UndoRestoresEveryFile() => TestUiThread.RunAsync(UndoOnUiThread);

    private async Task UndoOnUiThread()
    {
        Project();
        var (document, inspector) = await OpenBaseAsync();
        var main = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(MainUnitPath)).Value);
        var files = new[] { MainFormPath, LeafFormPath, OtherFormPath, PlainFormPath }.ToDictionary(p => p, _fs.ReadAllBytes);
        var unitText = document.Buffer.Text;

        RenameOkButton(document, inspector);
        Assert.Equal(Renamed(Leaf), Disk(LeafFormPath));
        Assert.Equal(Renamed(InheritedDesign.Form), main.Designer.CurrentFormText());

        // One undo in the ancestor: the ancestor, every descendant file and the open descendant are back.
        Execute(document, FreexComp.Designer.Surface.Commands.Undo);
        Assert.NotNull(document.Designer.Model!.FindByName("OkButton"));
        Assert.Null(document.Designer.Model.FindByName("YesButton"));
        Assert.Equal(unitText, document.Buffer.Text);
        foreach (var (path, bytes) in files)
        {
            Assert.Equal(bytes, _fs.ReadAllBytes(path));
        }

        Assert.Equal(InheritedDesign.Form, main.Designer.CurrentFormText());
        AssertResolved(main.Designer.Model!, "OkButton", gone: "YesButton");
        Frames.Refresh();
        foreach (var className in (string[])["TMainForm", "TMain2", "TOtherForm"])
        {
            AssertResolved(Frames.Ancestors.Find(className)!.Document, "OkButton", gone: "YesButton");
        }

        // Redo renames every file again.
        Execute(document, FreexComp.Designer.Surface.Commands.Redo);
        Assert.NotNull(document.Designer.Model!.FindByName("YesButton"));
        Assert.Equal(Renamed(Leaf), Disk(LeafFormPath));
        Assert.Equal(Renamed(Other), Disk(OtherFormPath));
        Assert.Equal(files[MainFormPath], _fs.ReadAllBytes(MainFormPath)); // open: the tab holds the change until saved
        Assert.Equal(Renamed(InheritedDesign.Form), main.Designer.CurrentFormText());
        AssertResolved(main.Designer.Model!, "YesButton");
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-134")]
    public Task OpenDescendantFollowsWithoutPreservedBlocks() => TestUiThread.RunAsync(OpenFollowsOnUiThread);

    private async Task OpenFollowsOnUiThread()
    {
        Project();
        var (document, inspector) = await OpenBaseAsync();
        var main = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(MainUnitPath)).Value);
        var leaf = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(LeafUnitPath)).Value);
        Assert.True(main.ShowView(DocumentView.Design));
        Assert.True(leaf.ShowView(DocumentView.Text));
        var mainModel = main.Designer.Model!;
        var leafModel = leaf.Designer.Model!;
        var mainBytes = _fs.ReadAllBytes(MainFormPath);
        var leafBytes = _fs.ReadAllBytes(LeafFormPath);
        var preserved = 0;
        mainModel.Changed += (_, _) => preserved += mainModel.AllObjects().Count(o => o.IsPreserved || o.PreservedBlocks.Count > 0);
        leafModel.Changed += (_, _) => preserved += leafModel.AllObjects().Count(o => o.IsPreserved || o.PreservedBlocks.Count > 0);

        RenameOkButton(document, inspector);

        // Both open descendants follow in their tabs — same models, never a preserved block on the way — and are modified;
        // their files on disk wait for the save.
        Assert.Same(mainModel, main.Designer.Model);
        Assert.Same(leafModel, leaf.Designer.Model);
        Assert.Equal(0, preserved);
        AssertResolved(mainModel, "YesButton");
        AssertResolved(leafModel, "YesButton");
        Assert.Equal("Apply", Caption(mainModel, "YesButton"));
        Assert.Equal("Leaf", Caption(leafModel, "YesButton"));
        Assert.Equal(Renamed(InheritedDesign.Form), main.Designer.CurrentFormText());
        Assert.Equal(Renamed(Leaf), leaf.FormBuffer.Text); // the Text view shows the renamed header
        Assert.True(main.IsModified);
        Assert.True(leaf.IsModified);
        Assert.Equal(mainBytes, _fs.ReadAllBytes(MainFormPath));
        Assert.Equal(leafBytes, _fs.ReadAllBytes(LeafFormPath));
        Assert.Equal(Renamed(Other), Disk(OtherFormPath)); // closed: written at once

        // A selection by the old name finds nothing; the inherited button is selectable by its new name.
        main.Designer.Surface!.Selection.Set(["YesButton"]);
        Assert.Equal("YesButton", main.Designer.Surface.Selection.Primary.Name);

        // Saving the descendant writes the renamed header.
        Assert.True(await _services.Documents.SaveAsync(main));
        Assert.Equal(Renamed(InheritedDesign.Form), Disk(MainFormPath));
        Assert.False(main.IsModified);
    }

    // ---- D-140: open descendants keep their history, Text-view edits follow, undo write failures ------------------

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-134")]
    public Task OpenDescendantKeepsItsUndoHistory() => TestUiThread.RunAsync(KeepsHistoryOnUiThread);

    private async Task KeepsHistoryOnUiThread()
    {
        Project();
        var (document, inspector) = await OpenBaseAsync();
        var main = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(MainUnitPath)).Value);
        Assert.True(main.ShowView(DocumentView.Design));

        // The descendant's own edit names the OK button twice: its caption and the form's ActiveControl.
        Assert.True(main.Designer.Surface!.Commit(FreexComp.Designer.Surface.UndoTitles.Property,
            [new FreexComp.Designer.Editing.SetProperty("OkButton", "Caption", new StringValue("Go")),
                new FreexComp.Designer.Editing.SetProperty("MainForm", "ActiveControl", new IdentValue("OkButton"))]));
        var edited = main.Designer.CurrentFormText();
        Assert.Contains("ActiveControl = OkButton", edited, StringComparison.Ordinal);
        var renamed = edited.Replace("OkButton", "YesButton", StringComparison.Ordinal);

        RenameOkButton(document, inspector);
        Assert.Equal(renamed, main.Designer.CurrentFormText());
        Assert.True(main.Designer.Undo.CanUndo); // the history is kept, rewritten to the new name
        Assert.True(main.IsModified);

        // The descendant's own undo reverts its edit on the renamed component; redo makes it again under the new name.
        Execute(main, FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal(Renamed(InheritedDesign.Form), main.Designer.CurrentFormText());
        AssertResolved(main.Designer.Model!, "YesButton");
        Execute(main, FreexComp.Designer.Surface.Commands.Redo);
        Assert.Equal(renamed, main.Designer.CurrentFormText());
        Assert.Equal(new IdentValue("YesButton"), main.Designer.Model!.Root.FindProperty("ActiveControl")!.Value);
        Assert.Equal("Go", Caption(main.Designer.Model, "YesButton"));

        // Undoing the ancestor's rename takes the descendant back with its history: undo and redo name the old name again.
        Execute(document, FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal(edited, main.Designer.CurrentFormText());
        Execute(main, FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal(InheritedDesign.Form, main.Designer.CurrentFormText());
        AssertResolved(main.Designer.Model!, "OkButton", gone: "YesButton");
        Execute(main, FreexComp.Designer.Surface.Commands.Redo);
        Assert.Equal(edited, main.Designer.CurrentFormText());
        AssertResolved(main.Designer.Model!, "OkButton", gone: "YesButton");
        Assert.Equal(new IdentValue("OkButton"), main.Designer.Model!.Root.FindProperty("ActiveControl")!.Value);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-134")]
    public Task TextViewEditGetsTheRename() => TestUiThread.RunAsync(TextViewOnUiThread);

    private async Task TextViewOnUiThread()
    {
        Project();
        var (document, inspector) = await OpenBaseAsync();
        var main = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(MainUnitPath)).Value);
        Assert.True(main.ShowView(DocumentView.Text));
        var mainBytes = _fs.ReadAllBytes(MainFormPath);

        // An edit of the descendant's text the model does not have yet.
        var edited = InheritedDesign.Form.Replace("      Caption = 'Apply'\n", "      Caption = 'Go'\n", StringComparison.Ordinal);
        main.FormBuffer.ReplaceAll(edited, resetUndo: false);

        RenameOkButton(document, inspector);

        // The pending text gets the rename and stays pending (modified, not saved); leaving the Text view reads it.
        Assert.Equal(Renamed(edited), main.FormBuffer.Text);
        Assert.True(main.IsModified);
        Assert.Equal(mainBytes, _fs.ReadAllBytes(MainFormPath));
        Assert.True(main.ShowView(DocumentView.Design));
        AssertResolved(main.Designer.Model!, "YesButton");
        Assert.Equal("Go", Caption(main.Designer.Model!, "YesButton"));

        // A pending text that no longer parses refuses the rename, naming the file; nothing changes anywhere.
        Assert.True(main.ShowView(DocumentView.Text));
        var broken = Renamed(edited).Replace("  object NameEdit: TEdit\n", "  object NameEdit TEdit\n", StringComparison.Ordinal);
        main.FormBuffer.ReplaceAll(broken, resetUndo: false);
        var files = new[] { MainFormPath, LeafFormPath, OtherFormPath, PlainFormPath }.ToDictionary(p => p, _fs.ReadAllBytes);
        var unitText = document.Buffer.Text;
        document.Designer.Surface!.Selection.Set(["YesButton"]);
        inspector.Page = InspectorPage.Properties;
        inspector.CurrentPath = "Name";
        Assert.True(inspector.BeginEdit());
        inspector.EditText = "AcceptButton";
        Assert.False(inspector.Commit());
        Assert.Contains(MainFormPath, inspector.EditError, StringComparison.Ordinal);
        Assert.NotNull(document.Designer.Model!.FindByName("YesButton"));
        Assert.Null(document.Designer.Model.FindByName("AcceptButton"));
        Assert.Equal(unitText, document.Buffer.Text);
        Assert.Equal(broken, main.FormBuffer.Text);
        foreach (var (path, bytes) in files)
        {
            Assert.Equal(bytes, _fs.ReadAllBytes(path)); // the leaf, written first, was written back
        }
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-134")]
    public Task UndoWriteFailureIsReported() => TestUiThread.RunAsync(UndoFailureOnUiThread);

    private async Task UndoFailureOnUiThread()
    {
        Project();
        var (document, inspector) = await OpenBaseAsync();
        var dialogs = Assert.IsType<FakeDialogService>(_services.Dialogs);
        dialogs.Answer("Msg.File.SaveFailed", "Cancel");
        var original = new[] { MainFormPath, LeafFormPath, OtherFormPath }.ToDictionary(p => p, _fs.ReadAllBytes);
        RenameOkButton(document, inspector);
        var renamed = original.Keys.ToDictionary(p => p, _fs.ReadAllBytes);

        // Undo: the other descendant cannot be written back. The dialog names it and the step is not taken: the ancestor
        // keeps the new name and every descendant file stays renamed (the leaf, reverted first, is written again).
        _fs.SetReadOnly(OtherFormPath, true);
        Execute(document, FreexComp.Designer.Surface.Commands.Undo);
        var request = Assert.Single(dialogs.Requests);
        Assert.Equal("Msg.File.SaveFailed", request.MessageId);
        Assert.Equal(OtherFormPath, request.Args[0]);
        Assert.NotNull(document.Designer.Model!.FindByName("YesButton"));
        Assert.True(document.Designer.Undo.CanUndo);
        Assert.False(document.Designer.Undo.CanRedo);
        Assert.Contains("    YesButton: TButton;", document.Buffer.Text, StringComparison.Ordinal);
        foreach (var (path, bytes) in renamed)
        {
            Assert.Equal(bytes, _fs.ReadAllBytes(path));
        }

        // Writable again: the undo goes through.
        _fs.SetReadOnly(OtherFormPath, false);
        Execute(document, FreexComp.Designer.Surface.Commands.Undo);
        Assert.NotNull(document.Designer.Model!.FindByName("OkButton"));
        foreach (var (path, bytes) in original)
        {
            Assert.Equal(bytes, _fs.ReadAllBytes(path));
        }

        // Redo: the same failure leaves everything at the old name, the unit stays redoable.
        _fs.SetReadOnly(OtherFormPath, true);
        Execute(document, FreexComp.Designer.Surface.Commands.Redo);
        Assert.Equal(2, dialogs.Requests.Count);
        Assert.Equal("Msg.File.SaveFailed", dialogs.Requests[1].MessageId);
        Assert.Equal(OtherFormPath, dialogs.Requests[1].Args[0]);
        Assert.NotNull(document.Designer.Model!.FindByName("OkButton"));
        Assert.True(document.Designer.Undo.CanRedo);
        Assert.False(document.Designer.Undo.CanUndo);
        foreach (var (path, bytes) in original)
        {
            Assert.Equal(bytes, _fs.ReadAllBytes(path));
        }

        _fs.SetReadOnly(OtherFormPath, false);
        Execute(document, FreexComp.Designer.Surface.Commands.Redo);
        Assert.NotNull(document.Designer.Model!.FindByName("YesButton"));
        foreach (var (path, bytes) in renamed)
        {
            Assert.Equal(bytes, _fs.ReadAllBytes(path));
        }
    }

    // ---- D-139: the instances of a renamed frame follow -----------------------------------------------------------

    private const string FrameUnitPath = Dir + @"\AddressFrameUnit.pas";
    private const string FrameFormPath = Dir + @"\AddressFrameUnit.form";
    private const string OuterUnitPath = Dir + @"\OuterFrameUnit.pas";
    private const string OuterFormPath = Dir + @"\OuterFrameUnit.form";
    private const string OrderUnitPath = Dir + @"\OrderForm.pas";
    private const string OrderFormPath = Dir + @"\OrderForm.form";

    /// <summary>
    /// The customer form placing the address frame: the root's <c>ActiveControl</c> names the instance's edit, the label's
    /// override points its <c>FocusControl</c> at it (a plain name of the instance scope) and the edit has an override.
    /// </summary>
    private static readonly string PlacingForm = FrameDesign.Form
        .Replace("  Caption = 'Customer'\n", "  ActiveControl = BillingAddress.StreetEdit\n  Caption = 'Customer'\n", StringComparison.Ordinal)
        .Replace("      Caption = 'Billing street'\n    end\n", "      Caption = 'Billing street'\n      FocusControl = StreetEdit\n    end\n"
            + "    inherited StreetEdit: TEdit\n      Width = 200\n    end\n", StringComparison.Ordinal);

    /// <summary>A descendant of the customer form overriding the inherited instance's edit.</summary>
    private const string PlacingLeaf = "inherited Main2: TMain2\n  inherited BillingAddress: TAddressFrame\n    inherited StreetEdit: TEdit\n"
        + "      Width = 180\n    end\n  end\nend\n";

    /// <summary>A frame placing the address frame (a frame inside a frame) and overriding its edit.</summary>
    private const string OuterFrame = "object OuterFrame: TOuterFrame\n  Left = 0\n  Top = 0\n  Width = 340\n  Height = 140\n"
        + "  inline Home: TAddressFrame\n    Left = 8\n    Top = 8\n    Width = 320\n    Height = 120\n    TabOrder = 0\n"
        + "    inherited StreetEdit: TEdit\n      Width = 150\n    end\n  end\nend\n";

    /// <summary>A form placing the outer frame: an override of the nested instance's edit and a three-part reference to it.</summary>
    private const string OrderForm = "object OrderForm: TOrderForm\n  Left = 200\n  Top = 120\n  Caption = 'Order'\n  ClientWidth = 400\n"
        + "  ClientHeight = 300\n  PixelsPerInch = 96\n  object NoteLabel: TLabel\n    Left = 8\n    Top = 160\n    Width = 30\n    Height = 15\n"
        + "    Caption = 'Note'\n    FocusControl = Outer1.Home.StreetEdit\n  end\n  inline Outer1: TOuterFrame\n    Left = 8\n    Top = 8\n"
        + "    Width = 340\n    Height = 140\n    TabOrder = 0\n    inherited Home: TAddressFrame\n      inherited StreetEdit: TEdit\n"
        + "        Width = 100\n      end\n    end\n  end\nend\n";

    private static readonly string[] PlacingFiles = [MainFormPath, LeafFormPath, OuterFormPath, OrderFormPath];

    private static string Moved(string text) => text.Replace("StreetEdit", "AddressEdit", StringComparison.Ordinal);

    /// <summary>
    /// The address frame, the customer form placing it, a descendant of that form, a frame placing the address frame and a
    /// form placing that frame.
    /// </summary>
    private void FrameProject()
    {
        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "gui", "mainSource": "Hello.pas", "files": [
              { "path": "Hello.pas" },
              { "path": "AddressFrameUnit.pas", "form": "AddressFrameUnit.form" },
              { "path": "MainForm.pas", "form": "MainForm.form" },
              { "path": "Main2.pas", "form": "Main2.form" },
              { "path": "OuterFrameUnit.pas", "form": "OuterFrameUnit.form" },
              { "path": "OrderForm.pas", "form": "OrderForm.form" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nuses Forms, MainForm;\nbegin\nend.\n");
        File(FrameUnitPath, ProjectFrameCatalogTests.FrameUnit);
        File(FrameFormPath, FrameDesign.AddressFrame);
        File(MainUnitPath, FrameDesign.Unit);
        File(MainFormPath, PlacingForm);
        File(LeafUnitPath, BareUnit("Main2", "TMainForm"));
        File(LeafFormPath, PlacingLeaf);
        File(OuterUnitPath, BareUnit("OuterFrameUnit", "TFrame", "Home: TAddressFrame;").Replace("TOuterFrameUnit", "TOuterFrame", StringComparison.Ordinal));
        File(OuterFormPath, OuterFrame);
        File(OrderUnitPath, BareUnit("OrderForm", "TForm", "NoteLabel: TLabel;", "Outer1: TOuterFrame;"));
        File(OrderFormPath, OrderForm);
    }

    /// <summary>
    /// Opens the address frame in the Design view (after <paramref name="beforeRename"/>) and renames its edit through the
    /// Object Inspector.
    /// </summary>
    private Task<FormDocumentViewModel> RenameFrameEditAsync(Func<Task>? beforeRename = null) =>
        RenameInDesignerAsync(FrameUnitPath, "StreetEdit", "AddressEdit", beforeRename);

    /// <summary>
    /// Opens the project and <paramref name="unitPath"/> in the Design view (after <paramref name="beforeRename"/>) and
    /// renames its component <paramref name="oldName"/> to <paramref name="newName"/> through the Object Inspector.
    /// </summary>
    private async Task<FormDocumentViewModel> RenameInDesignerAsync(string unitPath, string oldName, string newName, Func<Task>? beforeRename = null)
    {
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        var document = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(unitPath)).Value);
        Assert.True(document.ShowView(DocumentView.Design));
        if (beforeRename is not null)
        {
            await beforeRename();
        }

        RenameInDesigner(document, oldName, newName);
        return document;
    }

    private void RenameInDesigner(FormDocumentViewModel document, string oldName, string newName)
    {
        var inspector = new ObjectInspectorViewModel(_services.Status);
        inspector.Bind(document.Designer);
        document.Designer.Surface!.Selection.Set([oldName]);
        inspector.Page = InspectorPage.Properties;
        inspector.CurrentPath = "Name";
        Assert.True(inspector.BeginEdit());
        inspector.EditText = newName;
        Assert.True(inspector.Commit());
    }

    /// <summary>
    /// Merged without diagnostics or preserved blocks; every instance named by <paramref name="instances"/> has an inherited
    /// <paramref name="name"/> and no <paramref name="gone"/>.
    /// </summary>
    private static void AssertInstances(FormDocument document, string name, string gone, params string[] instances)
    {
        Assert.Empty(document.Diagnostics.Select(d => d.ToString()));
        Assert.DoesNotContain(document.AllObjects(), o => o.IsPreserved || o.PreservedBlocks.Count > 0);
        foreach (var instance in instances)
        {
            Assert.True(document.FindByQualifiedName(instance + "." + name)!.IsInherited);
            Assert.Null(document.FindByQualifiedName(instance + "." + gone));
        }
    }

    private static FormValue Value(FormDocument document, string qualifiedName, string path) =>
        PropertyAccess.GetEffective(document.FindByQualifiedName(qualifiedName)!, path);

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-082")]
    [Trait("Req", "DSG-FD-134")]
    public Task FrameInstancesFollow() => TestUiThread.RunAsync(InstancesFollowOnUiThread);

    private async Task InstancesFollowOnUiThread()
    {
        FrameProject();
        var frame = await RenameFrameEditAsync();

        // The frame and its field are renamed; its handler keeps its name (DSG-FD-082, DSG-CS-045).
        var edit = frame.Designer.Model!.FindByName("AddressEdit")!;
        Assert.Contains("    AddressEdit: TEdit;", frame.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(new IdentValue("StreetEditChange"), edit.FindProperty("OnChange")!.Value);

        // Every file placing an instance — directly, through a descendant, inside another frame — names the edit by its
        // new name in its override blocks and its references; nothing else changed.
        foreach (var (path, text) in new[] { (MainFormPath, PlacingForm), (LeafFormPath, PlacingLeaf), (OuterFormPath, OuterFrame), (OrderFormPath, OrderForm) })
        {
            Assert.Equal(Moved(text), Disk(path));
        }

        // The project's catalog merges them against the renamed frame without F0109 blocks; the overrides are kept.
        Frames.Refresh();
        var main = Frames.Ancestors.Find("TMainForm")!.Document;
        AssertInstances(main, "AddressEdit", "StreetEdit", "BillingAddress");
        Assert.Equal(new QualifiedIdentValue(["BillingAddress", "AddressEdit"]), main.Root.FindProperty("ActiveControl")!.Value);
        Assert.Equal(new IdentValue("AddressEdit"), main.FindByQualifiedName("BillingAddress.StreetLabel")!.FindProperty("FocusControl")!.Value);
        Assert.Equal(new IntegerValue(200, false), Value(main, "BillingAddress.AddressEdit", "Width"));
        var leaf = Frames.Ancestors.Find("TMain2")!.Document;
        AssertInstances(leaf, "AddressEdit", "StreetEdit", "BillingAddress");
        Assert.Equal(new IntegerValue(180, false), Value(leaf, "BillingAddress.AddressEdit", "Width"));
        var outer = Frames.Ancestors.Find("TOuterFrame")!.Document;
        AssertInstances(outer, "AddressEdit", "StreetEdit", "Home");
        Assert.Equal(new IntegerValue(150, false), Value(outer, "Home.AddressEdit", "Width"));
        var order = Frames.Ancestors.Find("TOrderForm")!.Document;
        AssertInstances(order, "AddressEdit", "StreetEdit", "Outer1.Home");
        Assert.Equal(new IntegerValue(100, false), Value(order, "Outer1.Home.AddressEdit", "Width"));
        Assert.Equal(new QualifiedIdentValue(["Outer1", "Home", "AddressEdit"]), order.FindByName("NoteLabel")!.FindProperty("FocusControl")!.Value);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-134")]
    public Task FrameInstanceUndoRestoresEveryFile() => TestUiThread.RunAsync(InstanceUndoOnUiThread);

    private async Task InstanceUndoOnUiThread()
    {
        FrameProject();
        var files = PlacingFiles.ToDictionary(p => p, _fs.ReadAllBytes);
        FormDocumentViewModel? main = null;
        var preserved = 0;
        var frame = await RenameFrameEditAsync(async () =>
        {
            main = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(MainUnitPath)).Value);
            Assert.True(main.ShowView(DocumentView.Design));
            var model = main.Designer.Model!;
            AssertInstances(model, "StreetEdit", "AddressEdit", "BillingAddress");
            model.Changed += (_, _) => preserved += model.AllObjects().Count(o => o.IsPreserved || o.PreservedBlocks.Count > 0);
        });
        var unitText = frame.Buffer.Text;
        Assert.Contains("    AddressEdit: TEdit;", unitText, StringComparison.Ordinal);
        Assert.Equal(Moved(PlacingForm), main!.Designer.CurrentFormText()); // open: follows in its tab
        Assert.True(main.IsModified);
        Assert.Equal(files[MainFormPath], _fs.ReadAllBytes(MainFormPath));
        Assert.Equal(Moved(OrderForm), Disk(OrderFormPath)); // closed: written at once

        // One undo in the frame: the frame, every placing file and the open placing form are back.
        Execute(frame, FreexComp.Designer.Surface.Commands.Undo);
        Assert.NotNull(frame.Designer.Model!.FindByName("StreetEdit"));
        Assert.Null(frame.Designer.Model.FindByName("AddressEdit"));
        Assert.Contains("    StreetEdit: TEdit;", frame.Buffer.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("AddressEdit", frame.Buffer.Text, StringComparison.Ordinal);
        foreach (var (path, bytes) in files)
        {
            Assert.Equal(bytes, _fs.ReadAllBytes(path));
        }

        Assert.Equal(PlacingForm, main.Designer.CurrentFormText());
        AssertInstances(main.Designer.Model!, "StreetEdit", "AddressEdit", "BillingAddress");
        Frames.Refresh();
        AssertInstances(Frames.Ancestors.Find("TMain2")!.Document, "StreetEdit", "AddressEdit", "BillingAddress");
        AssertInstances(Frames.Ancestors.Find("TOuterFrame")!.Document, "StreetEdit", "AddressEdit", "Home");
        AssertInstances(Frames.Ancestors.Find("TOrderForm")!.Document, "StreetEdit", "AddressEdit", "Outer1.Home");

        // Redo renames every file again; the open form never showed a preserved block on the way.
        Execute(frame, FreexComp.Designer.Surface.Commands.Redo);
        Assert.NotNull(frame.Designer.Model!.FindByName("AddressEdit"));
        Assert.Equal(unitText, frame.Buffer.Text);
        Assert.Equal(Moved(PlacingLeaf), Disk(LeafFormPath));
        Assert.Equal(Moved(OuterFrame), Disk(OuterFormPath));
        Assert.Equal(Moved(OrderForm), Disk(OrderFormPath));
        Assert.Equal(files[MainFormPath], _fs.ReadAllBytes(MainFormPath));
        Assert.Equal(Moved(PlacingForm), main.Designer.CurrentFormText());
        AssertInstances(main.Designer.Model!, "AddressEdit", "StreetEdit", "BillingAddress");
        Assert.Equal(0, preserved);
    }

    // ---- D-141: references follow every rename exactly by scope ---------------------------------------------------

    /// <summary>The outer frame with an own label pointing at the nested instance's edit (<c>Home.StreetEdit</c>).</summary>
    private const string OuterWithLabel = "object OuterFrame: TOuterFrame\n  Left = 0\n  Top = 0\n  Width = 340\n  Height = 140\n"
        + "  object HomeLabel: TLabel\n    Left = 8\n    Top = 128\n    Width = 36\n    Height = 15\n    Caption = 'Street'\n"
        + "    FocusControl = Home.StreetEdit\n  end\n"
        + "  inline Home: TAddressFrame\n    Left = 8\n    Top = 8\n    Width = 320\n    Height = 120\n    TabOrder = 0\n"
        + "    inherited StreetEdit: TEdit\n      Width = 150\n    end\n  end\nend\n";

    /// <summary>The order form with an override of the outer frame's label in the outer instance's scope.</summary>
    private static readonly string OrderWithLabel = OrderForm.Replace("    inherited Home: TAddressFrame\n",
        "    inherited HomeLabel: TLabel\n      FocusControl = Home.StreetEdit\n    end\n    inherited Home: TAddressFrame\n", StringComparison.Ordinal);

    /// <summary>The outer frame's unit with the label's field.</summary>
    private static readonly string OuterWithLabelUnit = BareUnit("OuterFrameUnit", "TFrame", "HomeLabel: TLabel;", "Home: TAddressFrame;")
        .Replace("TOuterFrameUnit", "TOuterFrame", StringComparison.Ordinal);

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-082")]
    [Trait("Req", "DSG-FD-108")]
    public Task NestedInstanceRenameRewritesMiddleParts() => TestUiThread.RunAsync(MiddlePartsOnUiThread);

    private async Task MiddlePartsOnUiThread()
    {
        FrameProject();
        File(OuterUnitPath, OuterWithLabelUnit);
        File(OuterFormPath, OuterWithLabel);
        File(OrderFormPath, OrderWithLabel);

        // Renaming the address frame instance inside the outer frame.
        var outer = await RenameInDesignerAsync(OuterUnitPath, "Home", "House");

        // The outer frame's own reference through the instance (first part) follows.
        Assert.Contains("    House: TAddressFrame;", outer.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(OuterWithLabel.Replace("inline Home:", "inline House:", StringComparison.Ordinal)
            .Replace("= Home.StreetEdit", "= House.StreetEdit", StringComparison.Ordinal), outer.Designer.CurrentFormText());

        // The order form placing the outer frame: the override block's header, the reference through the nested instance as
        // a middle part, and the reference of the outer instance's scope that starts with the nested instance.
        Assert.Equal(OrderWithLabel.Replace("inherited Home:", "inherited House:", StringComparison.Ordinal)
            .Replace("= Outer1.Home.StreetEdit", "= Outer1.House.StreetEdit", StringComparison.Ordinal)
            .Replace("= Home.StreetEdit", "= House.StreetEdit", StringComparison.Ordinal), Disk(OrderFormPath));
        Frames.Refresh();
        var order = Frames.Ancestors.Find("TOrderForm")!.Document;
        AssertInstances(order, "StreetEdit", "AddressEdit", "Outer1.House");
        Assert.Null(order.FindByQualifiedName("Outer1.Home"));
        Assert.Equal(new QualifiedIdentValue(["Outer1", "House", "StreetEdit"]), order.FindByName("NoteLabel")!.FindProperty("FocusControl")!.Value);
        Assert.Equal(new QualifiedIdentValue(["House", "StreetEdit"]), order.FindByQualifiedName("Outer1.HomeLabel")!.FindProperty("FocusControl")!.Value);
        Assert.Equal(new IntegerValue(100, false), Value(order, "Outer1.House.StreetEdit", "Width"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-082")]
    [Trait("Req", "DSG-FD-108")]
    public Task RootQualifiedReferencesFollow() => TestUiThread.RunAsync(RootQualifiedOnUiThread);

    private async Task RootQualifiedOnUiThread()
    {
        // The frame's label names the edit by the frame's root name: in the frame's own file the root name names the root
        // (RTL-1104). The placing files hold references qualified by a frame's root name in instance lines: the address
        // frame's in the customer form's instance and in the nested instance, the outer frame's in the outer instance. In
        // another file a frame's header name is no name of that file (RTL-1104), so they do not name the instance.
        var frameText = FrameDesign.AddressFrame.Replace("    Caption = 'Street'\n",
            "    Caption = 'Street'\n    FocusControl = AddressFrame.StreetEdit\n", StringComparison.Ordinal);
        var main = PlacingForm.Replace("    inherited StreetEdit: TEdit\n      Width = 200\n    end\n",
            "    inherited StreetEdit: TEdit\n      Width = 200\n    end\n    inherited CityPanel: TPanel\n      inherited CityLabel: TLabel\n"
            + "        FocusControl = AddressFrame.StreetEdit\n      end\n    end\n", StringComparison.Ordinal);
        var order = OrderWithLabel.Replace("      FocusControl = Home.StreetEdit\n", "      FocusControl = OuterFrame.Home.StreetEdit\n", StringComparison.Ordinal)
            .Replace("    inherited Home: TAddressFrame\n      inherited StreetEdit: TEdit\n", "    inherited Home: TAddressFrame\n"
                + "      inherited StreetLabel: TLabel\n        FocusControl = AddressFrame.StreetEdit\n      end\n      inherited StreetEdit: TEdit\n",
                StringComparison.Ordinal);
        FrameProject();
        File(FrameFormPath, frameText);
        File(MainFormPath, main);
        File(OuterUnitPath, OuterWithLabelUnit);
        File(OuterFormPath, OuterWithLabel);
        File(OrderFormPath, order);

        var frame = await RenameFrameEditAsync();

        Assert.Equal(new QualifiedIdentValue(["AddressFrame", "AddressEdit"]),
            frame.Designer.Model!.FindByName("StreetLabel")!.FindProperty("FocusControl")!.Value);
        // Every placing file follows the rename except its root-qualified references, which stay byte-identical.
        static string Kept(string text) => Moved(text).Replace("AddressFrame.AddressEdit", "AddressFrame.StreetEdit", StringComparison.Ordinal)
            .Replace("OuterFrame.Home.AddressEdit", "OuterFrame.Home.StreetEdit", StringComparison.Ordinal);
        foreach (var (path, text) in new[] { (MainFormPath, main), (LeafFormPath, PlacingLeaf), (OuterFormPath, OuterWithLabel), (OrderFormPath, order) })
        {
            Assert.Equal(Kept(text), Disk(path));
        }

        Assert.Contains("        FocusControl = AddressFrame.StreetEdit\n", Disk(MainFormPath), StringComparison.Ordinal);
        Assert.Contains("      FocusControl = OuterFrame.Home.StreetEdit\n", Disk(OrderFormPath), StringComparison.Ordinal);
        Assert.Contains("        FocusControl = AddressFrame.StreetEdit\n", Disk(OrderFormPath), StringComparison.Ordinal);

        Frames.Refresh();
        var placing = Frames.Ancestors.Find("TMainForm")!.Document;
        AssertInstances(placing, "AddressEdit", "StreetEdit", "BillingAddress");
        Assert.Equal(new QualifiedIdentValue(["AddressFrame", "StreetEdit"]),
            placing.FindByQualifiedName("BillingAddress.CityLabel")!.FindProperty("FocusControl")!.Value);
        var ordering = Frames.Ancestors.Find("TOrderForm")!.Document;
        AssertInstances(ordering, "AddressEdit", "StreetEdit", "Outer1.Home");
        Assert.Equal(new QualifiedIdentValue(["OuterFrame", "Home", "StreetEdit"]),
            ordering.FindByQualifiedName("Outer1.HomeLabel")!.FindProperty("FocusControl")!.Value);
        Assert.Equal(new QualifiedIdentValue(["AddressFrame", "StreetEdit"]),
            ordering.FindByQualifiedName("Outer1.Home.StreetLabel")!.FindProperty("FocusControl")!.Value);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-082")]
    [Trait("Req", "DSG-FD-108")]
    public Task FileScopeRenameLeavesInstanceScopeNames() => TestUiThread.RunAsync(FileScopeOnUiThread);

    private async Task FileScopeOnUiThread()
    {
        // The customer form has an own StreetEdit beside the instance's: the instance's label names the instance's edit
        // (its scope resolves the plain name first), the form's own label names the form's edit. The instance's city label
        // names a form component its scope does not hold.
        const string S = "{S}", N = "{N}";
        var template = PlacingForm
            .Replace("    inherited StreetEdit: TEdit\n      Width = 200\n    end\n", "    inherited StreetEdit: TEdit\n      Width = 200\n    end\n"
                + "    inherited CityPanel: TPanel\n      inherited CityLabel: TLabel\n        FocusControl = " + N + "\n      end\n    end\n", StringComparison.Ordinal)
            .Replace("    TabOrder = 1\n  end\n", "    TabOrder = 1\n"
                + "    object " + S + ": TEdit\n      Left = 8\n      Top = 8\n      Width = 120\n      Height = 23\n      TabOrder = 0\n    end\n"
                + "    object " + N + ": TEdit\n      Left = 8\n      Top = 40\n      Width = 120\n      Height = 23\n      TabOrder = 1\n    end\n"
                + "    object OwnLabel: TLabel\n      Left = 8\n      Top = 72\n      Width = 36\n      Height = 15\n      Caption = 'Street'\n"
                + "      FocusControl = " + S + "\n    end\n  end\n", StringComparison.Ordinal);
        string Form(string street, string name) => template.Replace(S, street, StringComparison.Ordinal).Replace(N, name, StringComparison.Ordinal);
        var unit = FrameDesign.Unit.Replace("    OwnPanel: TPanel;\r\n",
            "    OwnPanel: TPanel;\r\n    StreetEdit: TEdit;\r\n    NameEdit: TEdit;\r\n    OwnLabel: TLabel;\r\n", StringComparison.Ordinal);
        FrameProject();
        File(MainUnitPath, unit);
        File(MainFormPath, Form("StreetEdit", "NameEdit"));
        var leafBytes = _fs.ReadAllBytes(LeafFormPath);

        var document = await RenameInDesignerAsync(MainUnitPath, "StreetEdit", "PostEdit");
        Assert.Equal(Form("PostEdit", "NameEdit"), document.Designer.CurrentFormText());
        RenameInDesigner(document, "NameEdit", "FullNameEdit");
        Assert.Equal(Form("PostEdit", "FullNameEdit"), document.Designer.CurrentFormText());

        var model = document.Designer.Model!;
        Assert.Empty(model.Diagnostics.Select(d => d.ToString()));
        Assert.Equal(new IdentValue("StreetEdit"), model.FindByQualifiedName("BillingAddress.StreetLabel")!.FindProperty("FocusControl")!.Value);
        Assert.Equal(new IdentValue("FullNameEdit"), model.FindByQualifiedName("BillingAddress.CityLabel")!.FindProperty("FocusControl")!.Value);
        Assert.Equal(new IdentValue("PostEdit"), model.FindByName("OwnLabel")!.FindProperty("FocusControl")!.Value);
        Assert.Equal(new QualifiedIdentValue(["BillingAddress", "StreetEdit"]), model.Root.FindProperty("ActiveControl")!.Value);
        Assert.Equal(leafBytes, _fs.ReadAllBytes(LeafFormPath)); // the descendant's instance override keeps its name
    }
}
