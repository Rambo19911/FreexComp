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
}
