using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Services;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary><c>run.parameters</c> (dialogs.md §22) and <c>project.options</c> (§8): the dialogs over the open project.</summary>
public static class ProjectDialogCommands
{
    public static void Register(ICommandService commands, IProjectService projects, IDialogService dialogs, IFileDialogService pickers,
        IdeFileSystem fileSystem, Layout.ILayoutService layout)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(dialogs);
        // UI-DLG-220: disabled without a project or when it is not runnable.
        commands.RegisterHandler("run.parameters", new Handler(
            () => projects.Definition is { AppType: not ("library" or "package") },
            () => dialogs.ShowAsync(new RunParametersViewModel(projects, pickers, fileSystem))));
        commands.RegisterHandler("project.options", new Handler(
            () => projects.Definition is not null,
            () => dialogs.ShowAsync(new ProjectOptionsViewModel(projects, dialogs, pickers, fileSystem, layout))));
    }

    private sealed class Handler(Func<bool> canExecute, Func<Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute();
    }
}
