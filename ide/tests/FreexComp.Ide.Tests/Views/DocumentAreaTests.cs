using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FreexComp.Build;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>main-window.md §3 (document area) and §5 (status bar visibility).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class DocumentAreaTests
{
    private static async Task<DocumentViewModel> OpenAsync(HeadlessHarness h, string name) =>
        (await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, name))).Value!;

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-021")]
    public async Task NewTabOpensRightOfTheActiveTab()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            foreach (var name in new[] { "A.pas", "B.pas", "C.pas", "D.pas" })
                o.InitialFiles[name] = "";
        });
        var a = await OpenAsync(h, "A.pas");
        var b = await OpenAsync(h, "B.pas");
        var c = await OpenAsync(h, "C.pas");
        h.Services.Documents.Activate(a);
        var d = await OpenAsync(h, "D.pas");
        Assert.Equal([a, d, b, c], h.Services.Documents.Documents);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-022")]
    public async Task WelcomePageIsAClosableDocumentWithoutModifiedState()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.Settings.Clear());
        var welcome = Assert.IsType<WelcomeDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Assert.False(welcome.IsModified);
        Assert.NotNull(h.TryFind<Control>(AutomationIds.DocumentTab("Welcome")));
        Assert.Null(h.TryFind<Control>(AutomationIds.DocumentTab("Welcome") + ".Modified"));

        await h.ExecuteAsync("file.close");
        Assert.Empty(h.Services.Documents.Documents);
        Assert.Empty(h.Messages.Requests);
        Assert.True(h.Find<Control>(AutomationIds.DocumentAreaEmpty).IsEffectivelyVisible);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-023")]
    public async Task ClosingTheLastDocumentKeepsTheProject()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["P.pproj"] = ProjectSerializer.Save(ProjectSerializer.CreateDefault("P", "P.pas", "console"));
            o.InitialFiles["P.pas"] = "program P;\r\n\r\nbegin\r\nend.\r\n";
        });
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "P.pproj")));
        await OpenAsync(h, "P.pas");
        Assert.NotEmpty(h.Services.Documents.Documents);
        Assert.True(await h.Services.Documents.CloseAllAsync());
        await h.WaitForAsync(() => h.Services.Documents.Documents.Count == 0, "all closed");

        Assert.Equal("P", h.Services.Projects.Project?.Name);
        Assert.True(h.Find<Control>(AutomationIds.DocumentAreaEmpty).IsEffectivelyVisible);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-044")]
    public async Task StatusBarCanBeHidden()
    {
        await using var h = await HeadlessHarness.StartAsync();
        Assert.True(h.Find<Control>(AutomationIds.StatusBar).IsEffectivelyVisible);

        await h.ExecuteAsync("view.statusBar");
        Assert.False(h.Services.Settings.Get(SettingsCatalog.AppearanceShowStatusBar));
        Assert.False(h.TryFind<Control>(AutomationIds.StatusBar)?.IsEffectivelyVisible ?? false);

        await h.ExecuteAsync("view.statusBar");
        Assert.True(h.Services.Settings.Get(SettingsCatalog.AppearanceShowStatusBar));
        Assert.True(h.Find<Control>(AutomationIds.StatusBar).IsEffectivelyVisible);
    }
}
