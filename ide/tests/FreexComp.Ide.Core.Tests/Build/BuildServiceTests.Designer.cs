using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

public sealed partial class BuildServiceTests
{
    /// <summary>
    /// IDE-P3-024: from Phase 5 a form document saved before a build runs its own save pipeline (code-sync.md): the unit is
    /// repaired, the empty handler removed, then the canonical form written.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Phase", "5")]
    [Trait("Req", "IDE-P3-024")]
    public Task SaveBeforeBuildRunsTheDesignerSavePipeline() => TestUiThread.RunAsync(() => SaveBeforeBuildRunsTheDesignerSavePipelineOnUiThread());

    private async Task SaveBeforeBuildRunsTheDesignerSavePipelineOnUiThread()
    {
        var bed = new BuildTestBed();
        ((DocumentService)bed.Services.Documents).DesignCatalog = CodeSyncTests.Catalog;
        var unitPath = BuildTestBed.Dir + @"\MainForm.pas";
        var formPath = BuildTestBed.Dir + @"\MainForm.form";
        bed.File(unitPath, TestUnits.Read("MainForm.pas").Replace("    NameEdit: TEdit;\r\n", "", StringComparison.Ordinal));
        bed.File(formPath, TestUnits.Read("MainForm.form"));
        bed.File(BuildTestBed.MainPath, "program Hello;\nuses Forms, MainForm;\nbegin\nend.\n");
        bed.File(BuildTestBed.ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "gui", "mainSource": "Hello.pas",
              "files": [ { "path": "Hello.pas" }, { "path": "MainForm.pas", "form": "MainForm.form" } ] }
            """);
        Assert.True(await bed.Services.Projects.OpenAsync(BuildTestBed.ProjectPath));
        var document = Assert.IsType<FormDocumentViewModel>((await bed.Services.Documents.OpenAsync(unitPath)).Value);
        var designer = document.Designer;
        designer.CodeSync.CreateHandler("NameEditChange", "TNotifyEvent");
        var change = new SetProperty("MainForm", "Caption", new StringValue("Built"));
        var inverse = designer.Editor!.Apply([change]);
        designer.Undo.Push(new DesignerUndoUnit("Undo_Designer_Property", [change], inverse, [], SelectionSnapshot.Empty, SelectionSnapshot.Empty));
        Assert.True(document.IsModified);

        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));

        Assert.False(document.IsModified);
        var unit = bed.Text(unitPath);
        Assert.Contains("    NameEdit: TEdit;", unit, StringComparison.Ordinal); // repaired
        Assert.DoesNotContain("NameEditChange", unit, StringComparison.Ordinal); // the empty handler removed
        Assert.Equal(FormWriter.Write(designer.Model!), bed.Text(formPath));
        Assert.Contains("Caption = 'Built'", bed.Text(formPath), StringComparison.Ordinal);
        Assert.Single(bed.Engine.Requests);
    }
}
