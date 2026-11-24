using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Commands;

/// <summary>Availability of single commands (commands.md §2).</summary>
[Trait("Phase", "8")]
public sealed class CommandTests
{
    /// <summary>IDE-CFG-010: <c>project.configurationManager</c> needs an open project and the Idle state; it opens the dialog.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-010")]
    public Task ConfigurationManager_EnabledOnlyWithProjectAndIdle() => TestUiThread.RunAsync(async () =>
    {
        var bed = new BuildTestBed();
        var commands = bed.Services.Commands;
        var definition = CommandCatalog.All.Single(d => d.Id == "project.configurationManager");
        Assert.Equal((8, "Configuration Manager…"), (definition.Phase, commands.Get("project.configurationManager").Title));
        Assert.False(commands.CanExecute("project.configurationManager")); // no project

        await bed.OpenHelloAsync();
        Assert.True(commands.CanExecute("project.configurationManager"));

        var held = bed.Engine.Enqueue(new FakeBuild([], FakeBuildEngine.Result(BuildTestBed.ProjectPath, true)).Held());
        var running = bed.Services.Build.BuildAsync(BuildKind.Build);
        await held.Reached;
        Assert.Equal(IdeState.Building, bed.Services.State.Current.State);
        Assert.False(commands.CanExecute("project.configurationManager"));
        held.Release();
        await running;
        Assert.True(commands.CanExecute("project.configurationManager"));

        // It opens the dialog on the open project; Cancel leaves the project alone.
        ConfigurationManagerViewModel? shown = null;
        bed.Dialogs.Script(ConfigurationManagerViewModel.DialogName, async dialog =>
        {
            shown = (ConfigurationManagerViewModel)dialog;
            await dialog.PressCancelAsync();
        });
        var before = bed.Services.Projects.Definition;
        await commands.ExecuteAsync("project.configurationManager");
        Assert.NotNull(shown);
        Assert.Equal(["Debug", "Release"], shown.Rows.Select(r => r.Name));
        Assert.Same(before, bed.Services.Projects.Definition);
    });
}
