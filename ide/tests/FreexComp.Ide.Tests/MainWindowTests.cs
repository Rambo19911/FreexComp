using Avalonia.Headless.XUnit;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests;

public sealed class MainWindowTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "1")]
    [Trait("Kind", "Ui")]
    public async Task MainWindowShowsWithAppIcon()
    {
        await using var h = await HeadlessHarness.StartAsync();

        Assert.True(h.Window.IsVisible);
        Assert.NotNull(h.Window.Icon); // BR-001
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "1")]
    [Trait("Kind", "Ui")]
    [Trait("Req", "UI-WIN-030")]
    public async Task TitleIsProductNameFromResources()
    {
        await using var h = await HeadlessHarness.StartAsync();

        Assert.Equal(Core.Resources.Strings.App_ProductName, h.Window.Title);
        Assert.Equal("FreexComp", h.Window.Title); // "No project, no document"
    }
}
