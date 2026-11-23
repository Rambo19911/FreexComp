using System.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Designer;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Settings;

/// <summary>Options that change what the IDE does (I-213).</summary>
[Trait("Phase", "5")]
public sealed class OptionsTests
{
    private const string Dir = @"C:\p\App";

    private static async Task<(IdeServices Services, ProjectManagerViewModel Tree)> OpenAsync(int? phase = null)
    {
        var fs = new InMemoryFileSystem();
        void File(string path, string text)
        {
            fs.CreateDirectory(Path.GetDirectoryName(path)!);
            fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        }

        var services = TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.ImplementedPhase = phase;
        });
        ((Core.Documents.DocumentService)services.Documents).DesignCatalog = CodeSyncTests.Catalog;
        File(Dir + @"\App.pproj", """
            { "formatVersion": 1, "name": "App", "appType": "gui", "mainSource": "App.pas",
              "files": [ { "path": "App.pas" }, { "path": "MainForm.pas", "form": "MainForm.form" } ] }
            """);
        File(Dir + @"\App.pas", "program App;\nuses MainForm;\nbegin\nend.\n");
        File(Dir + @"\MainForm.pas", DesignerFixture.Unit);
        File(Dir + @"\MainForm.form", DesignerFixture.Form);
        Assert.True(await services.Projects.OpenAsync(Dir + @"\App.pproj"));
        return (services, (ProjectManagerViewModel)services.ToolWindows.Get(ProjectManagerViewModel.ToolName)!);
    }

    private static async Task<FormDocumentViewModel> OpenFromTreeAsync(IdeServices services, ProjectManagerViewModel tree)
    {
        tree.Select(tree.Find("MainForm.pas")!);
        await tree.OpenSelectionAsync(); // UI-TW-108: the default action of the unit row
        return Assert.IsType<FormDocumentViewModel>(services.Documents.Documents.Single(d => d.FilePath == Dir + @"\MainForm.pas"));
    }

    /// <summary>
    /// UI-TW-108, settings.md <c>designer.defaultView</c>: from P5 a unit with a form opened from the Project Manager shows
    /// the view of the setting (default <c>design</c>); before P5 the code view; an open document keeps its view.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-TW-108")]
    public Task DesignerDefaultView() => TestUiThread.RunAsync(DesignerDefaultViewOnUiThread);

    private static async Task DesignerDefaultViewOnUiThread()
    {
        var (services, tree) = await OpenAsync();
        Assert.Equal("design", services.Settings.Get(SettingsCatalog.DesignerDefaultView));
        var form = await OpenFromTreeAsync(services, tree);
        Assert.Equal(DocumentView.Design, form.ActiveView);
        Assert.True(form.ShowView(DocumentView.Code));
        await OpenFromTreeAsync(services, tree);
        Assert.Equal(DocumentView.Code, form.ActiveView); // already open: it is only activated

        (services, tree) = await OpenAsync();
        services.Settings.Set(SettingsCatalog.DesignerDefaultView, "code");
        Assert.Equal(DocumentView.Code, (await OpenFromTreeAsync(services, tree)).ActiveView);

        (services, tree) = await OpenAsync(phase: 4);
        Assert.Equal(DocumentView.Code, (await OpenFromTreeAsync(services, tree)).ActiveView);
    }
}
