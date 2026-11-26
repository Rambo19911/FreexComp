using System.Text;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>The new frame unit template and <c>file.newFrame</c> / <c>project.addNewFrame</c> (code-sync.md §11; DSG-CS-114, 115; D-120).</summary>
[Trait("Phase", "8")]
public sealed class NewFrameTemplateTests
{
    private const string Dir = @"C:\p\Hello";
    private const string ProjectPath = Dir + @"\Hello.pproj";

    /// <summary>DSG-CS-114: the form unit with <c>class(TFrame)</c>, no <c>var</c> section, the class registered (Q-P8A-15).</summary>
    internal static string Unit(string name, string newLine) => string.Join(newLine,
        $"unit {name};", "", "interface", "", "uses", "  Classes, Controls, Forms;", "", "type",
        $"  T{name} = class(TFrame)", "  private", "  public", "  end;", "",
        "implementation", "", $"{{$FORM {name}.form}}", "", "initialization", $"  RegisterClass(T{name});", "", "end.", "");

    /// <summary>DSG-CS-114: the default size of TFrame at 0, 0; always LF (UTF-8 without BOM).</summary>
    internal static string Form(string name) => $"object {name}: T{name}\n  Left = 0\n  Top = 0\n  Width = 320\n  Height = 240\nend\n";

    private readonly InMemoryFileSystem _fs = new();
    private readonly IdeServices _services;

    public NewFrameTemplateTests()
    {
        _services = TestComposition.Build(o => o.FileSystem = _fs);
        ((DocumentService)_services.Documents).DesignCatalog = FrameDesign.Catalog;
    }

    private void File(string path, string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    private string Disk(string path) => Encoding.UTF8.GetString(_fs.ReadAllBytes(path));

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-114")]
    [Trait("Req", "DSG-CS-115")]
    public Task GeneratesUnitFormAndProjectEntry_OpensInDesign() => TestUiThread.RunAsync(GeneratesOnUiThread);

    private async Task GeneratesOnUiThread()
    {
        // The texts of DSG-CS-114 (the caller applies the line-ending setting to the unit).
        Assert.Equal(Unit("Frame1", "\n"), NewFrameTemplate.UnitText("Frame1", "Frame1"));
        Assert.Equal(Form("Frame1"), NewFrameTemplate.FormText("Frame1"));
        Assert.Equal("Frame1", NewFrameTemplate.NextName(_ => false));
        Assert.Equal("Frame3", NewFrameTemplate.NextName(n => n is "Frame1" or "Frame2"));

        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "gui", "mainSource": "Hello.pas", "files": [ { "path": "Hello.pas" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");

        // Both commands need a project (DSG-CS-115).
        Assert.False(_services.Commands.CanExecute("project.addNewFrame"));
        Assert.False(_services.Commands.CanExecute("file.newFrame"));
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.True(_services.Commands.CanExecute("project.addNewFrame"));
        Assert.True(_services.Commands.CanExecute("file.newFrame"));

        // The first frame: Frame1; the unit with the settings' line ending, the form UTF-8 + LF; the entry saved at once.
        await _services.Commands.ExecuteAsync("file.newFrame");
        Assert.Equal(Unit("Frame1", "\r\n"), Disk(Dir + @"\Frame1.pas"));
        Assert.Equal(Form("Frame1"), Disk(Dir + @"\Frame1.form"));
        var entry = _services.Projects.Project!.Files[^1];
        Assert.Equal(("Frame1.pas", "Frame1.form"), (entry.Path, entry.Form));
        Assert.Contains("\"form\": \"Frame1.form\"", Disk(ProjectPath), StringComparison.Ordinal);

        // Opened as a form document in the Design view, unmodified; the unit matches the frame (no reconcile issue).
        var document = Assert.IsType<FormDocumentViewModel>(_services.Documents.ActiveDocument);
        Assert.Equal(Dir + @"\Frame1.pas", document.FilePath);
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.False(document.IsModified);
        Assert.Equal(CodeSyncState.Ok, document.Designer.CodeSync.State);
        Assert.Empty(document.Designer.CodeSync.Reconcile(document.Designer.Model!, ReconcileMode.CheckOnly).Issues);
        Assert.Empty(FormParser.Parse(Disk(Dir + @"\Frame1.form"), "Frame1.form", FrameDesign.Catalog, "TFrame").Diagnostics);

        // DSG-FD-104: the new frame is in the project's frame catalog; its definition is the open model.
        Assert.Same(document.Designer.Model, _services.Frames!.Find("TFrame1")!.Document);

        // The next one: Frame2 (project.addNewFrame from the Project Manager).
        await _services.Commands.ExecuteAsync("project.addNewFrame");
        Assert.Equal(Unit("Frame2", "\r\n"), Disk(Dir + @"\Frame2.pas"));
        Assert.Equal("Frame2.pas", _services.Projects.Project!.Files[^1].Path);
        Assert.Equal(Dir + @"\Frame2.pas", _services.Documents.ActiveDocument!.FilePath);
        Assert.Equal(["TFrame1", "TFrame2"], _services.Frames.All.Select(f => f.ClassName));
    }
}
