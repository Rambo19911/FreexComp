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
    private static async Task<(IdeServices Services, TextDocumentViewModel Document)> OpenUnitAsync(int phase)
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
        var document = Assert.IsAssignableFrom<TextDocumentViewModel>((await services.Documents.OpenAsync(DesignerFixture.UnitPath)).Value);
        Assert.Same(document, services.Documents.ActiveDocument);
        return (services, document);
    }

    /// <summary>
    /// I-218: below P5 no form document is created even with a design catalogue — the unit opens as a plain text
    /// document and its <c>.form</c> opens as text (UI-TW-108 "before P5: opens the .form as text"); from P5 both open
    /// the form document.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-TW-108")]
    public Task NoFormDocumentBelowPhase5() => TestUiThread.RunAsync(NoFormDocumentBelowPhase5OnUiThread);

    private static async Task NoFormDocumentBelowPhase5OnUiThread()
    {
        var (services, unit) = await OpenUnitAsync(phase: 4);
        Assert.IsNotType<FormDocumentViewModel>(unit);
        var formText = Assert.IsAssignableFrom<TextDocumentViewModel>((await services.Documents.OpenAsync(DesignerFixture.FormPath)).Value);
        Assert.IsNotType<FormDocumentViewModel>(formText);
        Assert.Equal(DesignerFixture.FormPath, formText.FilePath);
        Assert.Equal(2, services.Documents.Documents.Count);
        Assert.DoesNotContain(services.Documents.Documents, d => d is FormDocumentViewModel);

        (services, unit) = await OpenUnitAsync(phase: 5);
        var form = Assert.IsType<FormDocumentViewModel>(unit);
        Assert.Same(form, (await services.Documents.OpenAsync(DesignerFixture.FormPath)).Value);
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
        var (services, document) = await OpenUnitAsync(phase: 4); // I-218: a plain text document below P5
        Assert.False(services.Commands.CanExecute("view.toggleFormCode"));
        Assert.False(services.Commands.CanExecute("view.formAsText"));
        await services.Commands.ExecuteAsync("view.toggleFormCode");
        Assert.Same(document, services.Documents.ActiveDocument);
        Assert.DoesNotContain(document.TextAreaMenu!(), i => i.CommandId == "view.toggleFormCode");

        (services, document) = await OpenUnitAsync(phase: 5);
        var form = Assert.IsType<FormDocumentViewModel>(document);
        Assert.True(services.Commands.CanExecute("view.toggleFormCode"));
        Assert.True(services.Commands.CanExecute("view.formAsText"));
        Assert.Contains(form.TextAreaMenu!(), i => i.CommandId == "view.toggleFormCode");
        await services.Commands.ExecuteAsync("view.formAsText");
        Assert.Equal(DocumentView.Text, form.ActiveView);
    }
}
