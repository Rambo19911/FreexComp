using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Build toolbar combos and the status-bar fields of Phase 3 (I-103; toolbars.md §2.2, main-window.md §5).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class BuildToolbarTests
{
    /// <summary>UI-TB-020/021, IDE-PM-090: the combo follows activeConfiguration, switches it, and is disabled while not Idle.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TB-020")]
    [Trait("Req", "UI-TB-021")]
    [Trait("Req", "IDE-PM-090")]
    public async Task ConfigurationComboSwitchesActiveConfiguration()
    {
        await using var h = await BuildScenario.StartAsync();
        var combo = h.Find<ComboBox>("ToolBar.Build.Configuration");
        var platform = h.Find<ComboBox>("ToolBar.Build.Platform");
        Assert.True(combo.IsEnabled);
        Assert.Equal(["Debug", "Release"], combo.ItemsSource!.Cast<string>());
        Assert.Equal("Debug", combo.SelectedItem);
        Assert.Equal("Win64", platform.SelectedItem);
        Assert.Equal("Debug | Win64", h.Status.Field("Configuration"));

        combo.SelectedItem = "Release";
        await h.WaitForAsync(() => h.Services.Projects.Project?.ActiveConfiguration == "Release", "configuration switched");
        Assert.Contains("\"activeConfiguration\": \"Release\"", File.ReadAllText(BuildScenario.ProjectPath(h)), StringComparison.Ordinal);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Release | Win64", h.Status.Field("Configuration"));

        // The build uses the active configuration; the combos are disabled while Building.
        var held = h.Build.Enqueue(FakeBuildEngine.Success(BuildScenario.ProjectPath(h), BuildScenario.ExePath(h)).Held());
        h.Press("Ctrl+Shift+B");
        await h.WaitForAsync(() => held.Request is not null, "build started");
        Assert.Equal("Release", held.Request!.Configuration);
        Dispatcher.UIThread.RunJobs();
        Assert.False(combo.IsEnabled);
        Assert.False(platform.IsEnabled);
        held.Release();
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "build finished");
        Dispatcher.UIThread.RunJobs();
        Assert.True(combo.IsEnabled);

        // Without a project both combos are empty and disabled.
        Assert.True(await h.Services.Projects.CloseAsync());
        Dispatcher.UIThread.RunJobs();
        Assert.False(combo.IsEnabled);
        Assert.Null(combo.SelectedItem);
        Assert.Null(h.Status.Field("Configuration"));
    }
}
