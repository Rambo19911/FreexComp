using System.Text;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>The new form unit template and <c>file.newForm</c> / <c>project.addNewForm</c> (code-sync.md §11; D-073, I-131).</summary>
[Trait("Phase", "5")]
public sealed class NewFormTemplateTests
{
    private const string Dir = @"C:\p\Hello";
    private const string ProjectPath = Dir + @"\Hello.pproj";

    /// <summary>code-sync.md §11 with the line ending of the settings.</summary>
    internal static string Unit(string name, string newLine) => string.Join(newLine,
        $"unit {name};", "", "interface", "", "uses", "  Classes, Controls, Forms;", "", "type",
        $"  T{name} = class(TForm)", "  private", "  public", "  end;", "", "var", $"  {name}: T{name};", "",
        "implementation", "", $"{{$FORM {name}.form}}", "", "end.", "");

    /// <summary>code-sync.md §11: always LF (UTF-8 without BOM, like every form file).</summary>
    internal static string Form(string name) =>
        $"object {name}: T{name}\n  Left = 200\n  Top = 120\n  Caption = '{name}'\n  ClientWidth = 400\n  ClientHeight = 300\n"
        + "  PixelsPerInch = 96\nend\n";

    private readonly InMemoryFileSystem _fs = new();
    private readonly IdeServices _services;

    public NewFormTemplateTests()
    {
        _services = TestComposition.Build(o => o.FileSystem = _fs);
        ((DocumentService)_services.Documents).DesignCatalog = CodeSyncTests.Catalog;
    }

    private void File(string path, string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    private string Disk(string path) => Encoding.UTF8.GetString(_fs.ReadAllBytes(path));

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-110")]
    [Trait("Req", "DSG-CS-111")]
    public Task GeneratesUnitAndFormAndProjectEntry() => TestUiThread.RunAsync(() => GeneratesUnitAndFormAndProjectEntryOnUiThread()); // I-209

    private async Task GeneratesUnitAndFormAndProjectEntryOnUiThread()
    {
        // The texts of §11.
        Assert.Equal(Unit("Form2", "\n"), NewFormTemplate.UnitText("Form2", "Form2"));
        Assert.Equal(Form("Form2"), NewFormTemplate.FormText("Form2"));

        File(ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "files": [ { "path": "Hello.pas" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");
        Assert.False(_services.Commands.CanExecute("project.addNewForm"));
        Assert.False(_services.Commands.CanExecute("file.newForm"));
        Assert.True(await _services.Projects.OpenAsync(ProjectPath));
        Assert.True(_services.Commands.CanExecute("project.addNewForm"));

        // The first form of the project: MainForm; the unit with the settings' line ending, the form UTF-8 + LF.
        await _services.Commands.ExecuteAsync("project.addNewForm");
        Assert.Equal(Unit("MainForm", "\r\n"), Disk(Dir + @"\MainForm.pas"));
        Assert.Equal(Form("MainForm"), Disk(Dir + @"\MainForm.form"));
        var entry = _services.Projects.Project!.Files[^1];
        Assert.Equal(("MainForm.pas", "MainForm.form"), (entry.Path, entry.Form));
        Assert.Contains("\"form\": \"MainForm.form\"", Disk(ProjectPath), StringComparison.Ordinal); // saved at once

        // Opened as a form document in the Design view, unmodified; the unit matches the form (no reconcile issue).
        var document = Assert.IsType<FormDocumentViewModel>(_services.Documents.ActiveDocument);
        Assert.Equal(Dir + @"\MainForm.pas", document.FilePath);
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.False(document.IsModified);
        Assert.Equal(CodeSyncState.Ok, document.Designer.CodeSync.State);
        Assert.Empty(document.Designer.CodeSync.Reconcile(document.Designer.Model!, ReconcileMode.CheckOnly).Issues);
        Assert.Empty(FormParser.Parse(Disk(Dir + @"\MainForm.form"), "MainForm.form", CodeSyncTests.Catalog).Diagnostics);

        // Further forms: Form<N>, the smallest free N ≥ 2 (a file Form2.pas in the folder is taken); file.newForm too.
        File(Dir + @"\Form2.pas", "unit Form2;\n");
        await _services.Commands.ExecuteAsync("file.newForm");
        Assert.Equal(Unit("Form3", "\r\n"), Disk(Dir + @"\Form3.pas"));
        Assert.Equal(Form("Form3"), Disk(Dir + @"\Form3.form"));
        entry = _services.Projects.Project!.Files[^1];
        Assert.Equal(("Form3.pas", "Form3.form"), (entry.Path, entry.Form));
        Assert.Equal(Dir + @"\Form3.pas", _services.Documents.ActiveDocument!.FilePath);
        await _services.Commands.ExecuteAsync("project.addNewForm");
        Assert.Equal("Form4.pas", _services.Projects.Project!.Files[^1].Path);
    }
}
