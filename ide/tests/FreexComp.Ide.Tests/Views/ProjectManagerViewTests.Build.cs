using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

public sealed partial class ProjectManagerViewTests
{
    /// <summary>IDE-PM-102 (I-109): from P3 the root's context menu has the build and run commands; Enter opens Project Options.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-102")]
    public async Task RootContextMenuHasBuildAndRun()
    {
        await using var h = await BuildScenario.StartAsync();
        var pm = (ProjectManagerViewModel)h.Services.ToolWindows.Get(ProjectManagerViewModel.ToolName)!;
        Dispatcher.UIThread.RunJobs();
        pm.Select(pm.Find("$project")!);

        var menu = pm.ContextMenu();
        var ids = menu.Select(m => m.Command?.Id ?? "-").ToList();
        Assert.Equal(["project.build", "project.rebuild", "project.clean", "-", "run.start", "run.startWithoutDebugging", "-"], ids.Take(7));
        Assert.Equal("project.options", ids[^1]);
        Assert.Contains("project.openOutputFolder", ids);
        Assert.True(menu[0].Command!.CanExecute(null)); // project.build works on the open project

        // Enter on the root row runs project.options (still a stub until the dialog, I-107) instead of toggling the row.
        var executed = new List<string>();
        h.Services.Commands.Executed += (_, e) => executed.Add(e.CommandId);
        Assert.True(h.Services.ToolWindows.Show(ProjectManagerViewModel.ToolName, focus: true));
        Dispatcher.UIThread.RunJobs();
        var expanded = pm.Find("$project")!.IsExpanded;
        h.Press("Enter");
        await h.WaitForAsync(() => executed.Contains("projectManager.open"), "projectManager.open executed");
        Assert.Equal(expanded, pm.Find("$project")!.IsExpanded);
    }
}
