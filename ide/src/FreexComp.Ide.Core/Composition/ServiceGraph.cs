using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ToolWindows;

namespace FreexComp.Ide.Core.Composition;

/// <summary>
/// The construction code of the service graph, shared by production (<c>CompositionRoot</c>) and tests
/// (<c>TestComposition</c>) so both build exactly the same graph (IDE-ARCH-010, IDE-ARCH-013). Every service is
/// created with <c>new</c>, in dependency order; no container, no reflection.
/// </summary>
public static class ServiceGraph
{
    /// <summary>Commands of later phases get <c>CanExecute = false</c> stubs (UI-CMD-004).</summary>
    public const int ImplementedPhase = 1;

    public static IdeServices Build(StartupOptions options, IPlatformServices platform)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(platform);

        // IDE-ARCH-015 step 4: synchronous load of the user files before anything reads them.
        var settings = new SettingsService(platform.UserPaths, platform.FileSystem, platform.Clock, platform.Log, SettingsCatalog.All);
        settings.Load();
        var theme = new ThemeService(settings, platform.ThemeHost); // step 5: theme applied
        var keymap = new KeymapService(platform.UserPaths, platform.FileSystem, platform.Clock, platform.Log);
        keymap.Load();

        var status = new StatusService(platform.Clock);
        var focus = new FocusTracker();
        var commands = new CommandService(keymap, platform.Dispatcher, platform.Clock, platform.Log, platform.Dialogs, status, focus);
        foreach (var definition in commands.Definitions.Where(d => d.Scope == CommandScope.Focus))
        {
            commands.RegisterHandler(definition.Id, new FocusCommandHandler(definition.Id, focus)); // commands.md §3
        }

        var layout = new LayoutService(platform.UserPaths, platform.FileSystem, platform.Clock, platform.Log, status, commands);
        layout.Load(); // step 7: UI state (small file, synchronous)
        var toolWindows = new ToolWindowService(commands, focus, platform.Clock, layout, ImplementedPhase); // tool windows register here (IDE-ARCH-100)
        theme.RegisterCommands(commands);
        ToolBarRowModel.RegisterCommands(commands, settings, ImplementedPhase);
        ViewModels.StatusBarViewModel.RegisterCommands(commands, settings);
        var languages = new LanguageRegistry();
        ProjectService? projects = null; // created after the documents it uses; the prompts ask it lazily
        var prompts = new FilePrompts(platform.FileDialogs, platform.FileSystem, layout, settings, platform.UserPaths,
            projectDirectory: () => projects?.Project?.Directory, isProjectMember: path => projects?.IsMember(path) == true);
        var documents = new DocumentService(platform.FileSystem, platform.TextBuffers, languages, settings, status, focus, platform.Dialogs, prompts);
        documents.RegisterCommands(commands);
        projects = new ProjectService(platform.FileSystem, documents, platform.Dialogs, status, platform.Log, prompts, settings,
            ProjectTemplates.LoadEmbedded(platform.Log), layout, platform.Clock);
        projects.RegisterCommands(commands);
        documents.ProjectOpener = path => projects.OpenAsync(path);
        commands.RegisterLaterPhaseStubs(ImplementedPhase);

        return new IdeServices
        {
            Options = options,
            UserPaths = platform.UserPaths,
            Log = platform.Log,
            FileSystem = platform.FileSystem,
            Clock = platform.Clock,
            Dispatcher = platform.Dispatcher,
            Clipboard = platform.Clipboard,
            Shell = platform.Shell,
            FileWatchers = platform.FileWatchers,
            Dialogs = platform.Dialogs,
            FileDialogs = platform.FileDialogs,
            ThemeHost = platform.ThemeHost,
            TextBuffers = platform.TextBuffers,
            Settings = settings,
            Theme = theme,
            Keymap = keymap,
            Status = status,
            Commands = commands,
            FocusTracker = focus,
            Layout = layout,
            ToolWindows = toolWindows,
            Languages = languages,
            FilePrompts = prompts,
            Documents = documents,
            Projects = projects,
        };
    }
}
