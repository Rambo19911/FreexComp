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

    /// <param name="toolWindows">Tool windows registered after the production ones (tests, IDE-ARCH-100); one named like a
    /// production tool window is registered instead of it.</param>
    public static IdeServices Build(StartupOptions options, IPlatformServices platform, IReadOnlyList<ToolWindowDefinition>? toolWindows = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(platform);

        // IDE-ARCH-015 step 4: synchronous load of the user files before anything reads them.
        var settings = new SettingsService(platform.UserPaths, platform.FileSystem, platform.Clock, platform.Log, SettingsCatalog.All);
        settings.Load();
        var theme = new ThemeService(settings, platform.ThemeHost); // step 5: theme applied
        var keymap = new KeymapService(platform.UserPaths, platform.FileSystem, platform.Clock, platform.Log);
        keymap.Load();
        var recent = new RecentItemsService(platform.UserPaths, platform.FileSystem, platform.Clock, platform.Log);
        recent.Load();

        var status = new StatusService(platform.Clock);
        var focus = new FocusTracker();
        var commands = new CommandService(keymap, platform.Dispatcher, platform.Clock, platform.Log, platform.Dialogs, status, focus);
        foreach (var definition in commands.Definitions.Where(d => d.Scope == CommandScope.Focus))
        {
            commands.RegisterHandler(definition.Id, new FocusCommandHandler(definition.Id, focus)); // commands.md §3
        }

        var layout = new LayoutService(platform.UserPaths, platform.FileSystem, platform.Clock, platform.Log, status, commands);
        layout.Load(); // step 7: UI state (small file, synchronous)
        var toolWindowService = new ToolWindowService(commands, focus, platform.Clock, layout, ImplementedPhase); // tool windows register here (IDE-ARCH-100)
        theme.RegisterCommands(commands);
        ToolBarRowModel.RegisterCommands(commands, settings, ImplementedPhase);
        ViewModels.StatusBarViewModel.RegisterCommands(commands, settings);
        var languages = new LanguageRegistry();
        ProjectService? projects = null; // created after the documents it uses; the prompts ask it lazily
        var prompts = new FilePrompts(platform.FileDialogs, platform.FileSystem, layout, settings, platform.UserPaths,
            projectDirectory: () => projects?.Project?.Directory, isProjectMember: path => projects?.IsMember(path) == true);
        OptionsViewModel.RegisterCommands(commands, settings, keymap, layout, platform.Dialogs, prompts, theme, ImplementedPhase);
        AboutViewModel.RegisterCommands(commands, platform.Dialogs, platform.UserPaths, platform.Clipboard, layout);
        ShellCommands.RegisterCommands(commands, platform.Shell, platform.FileSystem, platform.UserPaths, platform.Log);
        var documents = new DocumentService(platform.FileSystem, platform.TextBuffers, languages, settings, status, focus, platform.Dialogs, prompts);
        documents.Layout = layout;
        documents.Clock = platform.Clock;
        documents.Clipboard = platform.Clipboard;
        documents.RegisterCommands(commands);
        KeyboardShortcutsDocument.RegisterCommands(commands, documents, keymap);
        projects = new ProjectService(platform.FileSystem, documents, platform.Dialogs, status, platform.Log, prompts, settings,
            ProjectTemplates.LoadEmbedded(platform.Log), layout, platform.Clock, platform.Shell);
        projects.RegisterCommands(commands);
        documents.ProjectOpener = path => projects.OpenAsync(path);
        var projectManager = new ViewModels.ProjectManagerViewModel(projects, documents, platform.FileSystem, platform.Dialogs, commands,
            platform.Log, ImplementedPhase);
        projectManager.RegisterCommands();
        projects.SelectedFolder = () => projectManager.SelectedFolderPath;
        projects.SelectedFiles = () => focus.Current.ToolWindowName == ViewModels.ProjectManagerViewModel.ToolName
            ? projectManager.SelectedFilePaths
            : null;
        projects.RenameInPlace = projectManager.BeginRename;
        var session = new SessionService(platform.FileSystem, documents, projects, projectManager, layout, platform.UserPaths,
            platform.Clock, platform.Log);
        projects.Session = session;
        projects.RecentItems = recent;
        // IDE-P1-130: a file document that is not a member of the open project joins the recent files.
        documents.DocumentOpened += (_, e) =>
        {
            if (e.Document.FilePath is { } path && !projects.IsMember(path))
                recent.Add(RecentKind.File, path);
        };
        recent.RegisterCommands(commands, platform.Dialogs, path => projects.OpenAsync(path), async path =>
        {
            var opened = await documents.OpenAsync(path);
            if (!opened.Success)
                await platform.Dialogs.ShowMessageAsync(new MessageRequest(opened.MessageId!, opened.Args));
        });
        documents.WelcomeFactory = () => new ViewModels.WelcomeDocumentViewModel(recent, commands, settings, platform.FileSystem, platform.Clock);
        documents.ProjectNewUnit = async () =>
        {
            if (projects.Project is null)
                return false;
            await projects.AddNewUnitAsync(); // a failure is reported there; no untitled unit instead
            return true;
        };
        commands.RegisterLaterPhaseStubs(ImplementedPhase);

        // IDE-ARCH-100: production tool windows, then the extra ones; an extra one with a production name replaces it.
        var extras = toolWindows ?? [];
        foreach (var definition in (ToolWindowDefinition[])[projectManager.Definition])
        {
            if (!extras.Any(e => e.Name == definition.Name))
                toolWindowService.Register(definition);
        }

        foreach (var definition in extras)
        {
            toolWindowService.Register(definition);
        }

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
            ToolWindows = toolWindowService,
            Languages = languages,
            FilePrompts = prompts,
            Documents = documents,
            Projects = projects,
            Session = session,
            RecentItems = recent,
        };
    }
}
