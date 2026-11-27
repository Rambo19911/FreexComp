using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Services;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// <c>run.parameters</c> (dialogs.md §22), <c>project.options</c> and <c>project.resources</c> (§8) and <c>project.configurationManager</c>
/// (phase8-configurations.md §4): the dialogs over the open project.
/// </summary>
public static class ProjectDialogCommands
{
    public static void Register(ICommandService commands, IProjectService projects, IDialogService dialogs, IFileDialogService pickers,
        IdeFileSystem fileSystem, Layout.ILayoutService layout, IStatusService status)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(dialogs);
        // UI-DLG-220: disabled without a project or when it can never run; a library runs through its host application,
        // which is set here (IDE-TGT-012).
        commands.RegisterHandler("run.parameters", new Handler(
            () => projects.Definition is { AppType: not "package" },
            () => dialogs.ShowAsync(new RunParametersViewModel(projects, pickers, fileSystem))));
        commands.RegisterHandler("project.options", new Handler(
            () => projects.Definition is not null,
            () => dialogs.ShowAsync(new ProjectOptionsViewModel(projects, dialogs, pickers, fileSystem, layout))));
        // UI-DLG-330: Project Options opened on the page Resources.
        commands.RegisterHandler("project.resources", new Handler(
            () => projects.Definition is not null,
            () => dialogs.ShowAsync(new ProjectOptionsViewModel(projects, dialogs, pickers, fileSystem, layout, "Resources"))));
        // IDE-CFG-010: with a project; the Idle state is the rule of IdeCommandAvailability.
        commands.RegisterHandler("project.configurationManager", new Handler(
            () => projects.Definition is not null,
            () => dialogs.ShowAsync(new ConfigurationManagerViewModel(projects, dialogs, status, layout))));
    }

    private sealed class Handler(Func<bool> canExecute, Func<Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute();
    }
}
