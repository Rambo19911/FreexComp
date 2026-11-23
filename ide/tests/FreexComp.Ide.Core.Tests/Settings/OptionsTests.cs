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

        // I-218: below P5 there is no form document at all; the unit opens as plain code.
        (services, tree) = await OpenAsync(phase: 4);
        tree.Select(tree.Find("MainForm.pas")!);
        await tree.OpenSelectionAsync();
        Assert.IsNotType<FormDocumentViewModel>(services.Documents.ActiveDocument);
        Assert.Equal(Dir + @"\MainForm.pas", services.Documents.ActiveDocument?.FilePath);
    }

    /// <summary>
    /// I-218, settings.md <c>designer.defaultView</c> ("view shown when a unit with a form is opened"): an open without a
    /// source position (File › Open, recent files, Go to File without a line, command line) shows the view of the setting
    /// too; navigation to a source position (Messages, Search Results, Go to Declaration, bookmarks) shows the Code view.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-TW-108")]
    public Task DesignerDefaultViewOnNavigation() => TestUiThread.RunAsync(DesignerDefaultViewOnNavigationOnUiThread);

    private static async Task DesignerDefaultViewOnNavigationOnUiThread()
    {
        const string unit = Dir + @"\MainForm.pas";
        var (services, _) = await OpenAsync();
        var form = Assert.IsType<FormDocumentViewModel>((await services.Documents.OpenInDefaultViewAsync(unit)).Value);
        Assert.Equal(DocumentView.Design, form.ActiveView);

        // A navigation to a line of the unit shows its code with the caret there.
        services.Navigation.GoToLine(form, 3, 1);
        Assert.Equal(DocumentView.Code, form.ActiveView);
        Assert.Equal(3, form.ViewState.Caret.Line);

        // An open that navigates to a source position (Messages, Search Results, …) shows the code.
        (services, _) = await OpenAsync();
        Assert.Equal(DocumentView.Code, Assert.IsType<FormDocumentViewModel>((await services.Documents.OpenAsync(unit)).Value).ActiveView);

        (services, _) = await OpenAsync();
        services.Settings.Set(SettingsCatalog.DesignerDefaultView, "code");
        Assert.Equal(DocumentView.Code, Assert.IsType<FormDocumentViewModel>((await services.Documents.OpenInDefaultViewAsync(unit)).Value).ActiveView);
    }
}
