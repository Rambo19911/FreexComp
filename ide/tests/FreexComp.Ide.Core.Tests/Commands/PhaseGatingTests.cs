using System.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Tests.Designer;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Commands;

/// <summary>Commands and menus follow the composition's implemented phase, not the product constant (I-213).</summary>
[Trait("Phase", "5")]
public sealed class PhaseGatingTests
{
    private static async Task<(IdeServices Services, FormDocumentViewModel Form)> OpenFormAsync(int phase)
    {
        var fs = new InMemoryFileSystem();
        foreach (var (path, text) in (ReadOnlySpan<(string, string)>)[(DesignerFixture.UnitPath, DesignerFixture.Unit), (DesignerFixture.FormPath, DesignerFixture.Form)])
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
        var form = Assert.IsType<FormDocumentViewModel>((await services.Documents.OpenAsync(DesignerFixture.UnitPath)).Value);
        Assert.Same(form, services.Documents.ActiveDocument);
        return (services, form);
    }

    /// <summary>
    /// <c>view.toggleFormCode</c> / <c>view.formAsText</c> (P5) have their handlers only from P5 (before: the later-phase
    /// stub, never executable); the editor context menu omits later-phase items by the composition's phase (UI-MENU-042).
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-042")]
    public Task FormViewCommandsFollowPhase() => TestUiThread.RunAsync(FormViewCommandsFollowPhaseOnUiThread);

    private static async Task FormViewCommandsFollowPhaseOnUiThread()
    {
        var (services, form) = await OpenFormAsync(phase: 4);
        Assert.False(services.Commands.CanExecute("view.toggleFormCode"));
        Assert.False(services.Commands.CanExecute("view.formAsText"));
        await services.Commands.ExecuteAsync("view.toggleFormCode");
        Assert.Equal(DocumentView.Code, form.ActiveView);
        Assert.DoesNotContain(form.TextAreaMenu!(), i => i.CommandId == "view.toggleFormCode");

        (services, form) = await OpenFormAsync(phase: 5);
        Assert.True(services.Commands.CanExecute("view.toggleFormCode"));
        Assert.True(services.Commands.CanExecute("view.formAsText"));
        Assert.Contains(form.TextAreaMenu!(), i => i.CommandId == "view.toggleFormCode");
        await services.Commands.ExecuteAsync("view.formAsText");
        Assert.Equal(DocumentView.Text, form.ActiveView);
    }
}
