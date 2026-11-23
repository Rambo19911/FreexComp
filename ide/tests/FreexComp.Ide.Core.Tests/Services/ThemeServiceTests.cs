using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Services;

[Trait("Phase", "1")]
public sealed class ThemeServiceTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-TOK-004")]
    [Trait("Req", "IDE-ARCH-015")]
    public void StoredThemeIsAppliedAtComposition()
    {
        var host = new FakeThemeHost();
        var services = Compose(host, "dark");

        Assert.Equal(ThemeKind.Dark, host.Requested);
        Assert.Equal(ThemeKind.Dark, services.Theme.ActualTheme);
        Assert.Equal("dark", services.Theme.RequestedTheme);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-021")]
    public Task ThemeCommandsSetTheSettingAndCheckTheActiveEntry() => TestUiThread.RunAsync(() => ThemeCommandsSetTheSettingAndCheckTheActiveEntryOnUiThread()); // I-209

    private async Task ThemeCommandsSetTheSettingAndCheckTheActiveEntryOnUiThread()
    {
        var host = new FakeThemeHost();
        var services = Compose(host, "light");
        var changes = 0;
        services.Theme.Changed += (_, _) => changes++;

        await services.Commands.ExecuteAsync("view.theme.dark");

        Assert.Equal("dark", services.Settings.Get(SettingsCatalog.AppearanceTheme));
        Assert.Equal(ThemeKind.Dark, host.Requested);
        Assert.Equal(ThemeKind.Dark, services.Theme.ActualTheme);
        Assert.Equal(1, changes);
        Assert.True(services.Commands.Get("view.theme.dark").IsChecked); // UI-MENU-007: exclusive group
        Assert.False(services.Commands.Get("view.theme.light").IsChecked);
        Assert.False(services.Commands.Get("view.theme.system").IsChecked);
        Assert.True(services.Commands.CanExecute("view.theme.light"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-021")]
    public Task FollowSystemTracksOsChanges() => TestUiThread.RunAsync(() => FollowSystemTracksOsChangesOnUiThread()); // I-209

    private async Task FollowSystemTracksOsChangesOnUiThread()
    {
        var host = new FakeThemeHost { SystemTheme = ThemeKind.Light };
        var services = Compose(host, "dark");
        var changes = 0;
        services.Theme.Changed += (_, _) => changes++;

        await services.Commands.ExecuteAsync("view.theme.system");
        Assert.Null(host.Requested);
        Assert.Equal(ThemeKind.Light, services.Theme.ActualTheme);

        host.SystemTheme = ThemeKind.Dark; // OS switches while the IDE runs
        Assert.Equal(ThemeKind.Dark, services.Theme.ActualTheme);
        Assert.Equal(2, changes);

        await services.Commands.ExecuteAsync("view.theme.dark");
        Assert.Equal(2, changes); // already dark: nothing to repaint
    }

    private static Composition.IdeServices Compose(FakeThemeHost host, string theme)
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\u\roaming");
        fs.WriteAllBytes(@"C:\u\roaming\settings.json", System.Text.Encoding.UTF8.GetBytes($"{{\"formatVersion\":1,\"appearance.theme\":\"{theme}\"}}"));
        return TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.UserDir = @"C:\u";
            o.ThemeHost = host;
        });
    }
}
