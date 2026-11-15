using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ToolWindows;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Layout;

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
    /// <param name="toolWindowPhase">Tool windows up to this phase register (default <see cref="ImplementedPhase"/>); tests
    /// use it to drive the Phase 3 tool windows before the implemented phase switches (I-127).</param>
    public static IdeServices Build(StartupOptions options, IPlatformServices platform, IReadOnlyList<ToolWindowDefinition>? toolWindows = null,
        int? toolWindowPhase = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(platform);

        // IDE-ARCH-015 step 4: synchronous load of the user files before anything reads them.
        // IDE-P1-012: --safe starts from the defaults and writes none of the user files.
        var safe = options.Safe;
        var settings = new SettingsService(platform.UserPaths, platform.FileSystem, platform.Clock, platform.Log, SettingsCatalog.All);
        if (safe)
            settings.LoadSafeMode();
        else
            settings.Load();
        var theme = new ThemeService(settings, platform.ThemeHost); // step 5: theme applied
        var keymap = new KeymapService(platform.UserPaths, platform.FileSystem, platform.Clock, platform.Log);
        if (safe)
            keymap.LoadSafeMode();
        else
            keymap.Load();
        var recent = new RecentItemsService(platform.UserPaths, platform.FileSystem, platform.Clock, platform.Log, safe);
        recent.Load();

        var status = new StatusService(platform.Clock);
        var focus = new FocusTracker();
        var state = new IdeStateMachine(platform.Log); // IDE-P3-001, phase3-build-run.md §2
        var commands = new CommandService(keymap, platform.Dispatcher, platform.Clock, platform.Log, platform.Dialogs, status, focus) { IdeState = state };
        foreach (var definition in commands.Definitions.Where(d => d.Scope == CommandScope.Focus))
        {
            commands.RegisterHandler(definition.Id, new FocusCommandHandler(definition.Id, focus)); // commands.md §3
        }

        var layout = new LayoutService(platform.UserPaths, platform.FileSystem, platform.Clock, platform.Log, status, commands);
        if (safe)
            layout.LoadSafeMode();
        else
            layout.Load(); // step 7: UI state (small file, synchronous)
        var toolWindowService = new ToolWindowService(commands, focus, platform.Clock, layout, toolWindowPhase ?? ImplementedPhase); // tool windows register here (IDE-ARCH-100)
        theme.RegisterCommands(commands);
        ToolBarRowModel.RegisterCommands(commands, settings, ImplementedPhase);
        ViewModels.StatusBarViewModel.RegisterCommands(commands, settings);
        var languages = new LanguageRegistry();
        ProjectService? projects = null; // created after the documents it uses; the prompts ask it lazily
        var prompts = new FilePrompts(platform.FileDialogs, platform.FileSystem, layout, settings, platform.UserPaths,
            projectDirectory: () => projects?.Project?.Directory, isProjectMember: path => projects?.IsMember(path) == true);
        var toolchain = new ToolchainLocator(settings, platform.FileSystem, platform.ToolchainEnvironment); // IDE-P3-010
        OptionsViewModel.RegisterCommands(commands, settings, keymap, layout, platform.Dialogs, prompts, theme, ImplementedPhase, toolchain, platform.FileSystem);
        ShellCommands.RegisterCommands(commands, platform.Shell, platform.FileSystem, platform.UserPaths, platform.Log);
        var documents = new DocumentService(platform.FileSystem, platform.TextBuffers, languages, settings, status, focus, platform.Dialogs, prompts);
        documents.Layout = layout;
        documents.Clock = platform.Clock;
        documents.Clipboard = platform.Clipboard;
        documents.Shell = platform.Shell;
        documents.RegisterCommands(commands);
        var help = new HelpService(platform.Dialogs, platform.UserPaths, platform.Clipboard, layout, documents, keymap);
        help.RegisterCommands(commands);
        help.ToolchainText = () => toolchain.Resolve().LlvmBinDir is { } llvm
            && ToolchainLocator.ValidateFolder(platform.FileSystem, llvm, ToolchainLocator.LlvmFiles).Valid
            ? "LLVM — " + llvm // the cached clang version joins with the process launcher (I-104)
            : null;
        var fileChanges = new FileChangeService(documents, platform.FileSystem, platform.FileWatchers, platform.Clock, platform.Dispatcher,
            settings, status, platform.Log, commands);
        documents.WriteGuard = fileChanges.BeginOwnWrite;
        var recovery = new RecoveryService(documents, platform.FileSystem, platform.UserPaths, platform.Clock, settings, platform.Log,
            safeMode: safe); // IDE-P1-012
        var find = new FindService(layout, status, platform.Clock, documents);
        find.RegisterCommands(commands);
        var navigation = new NavigationService(documents, platform.Dialogs, status, platform.FileSystem);
        navigation.RegisterCommands(commands);
        projects =new ProjectService(platform.FileSystem, documents, platform.Dialogs, status, platform.Log, prompts, settings,
            ProjectTemplates.LoadEmbedded(platform.Log), layout, platform.Clock, platform.Shell);
        projects.RegisterCommands(commands);
        projects.FileChanges = fileChanges; // IDE-P1-117
        projects.WriteGuard = fileChanges.BeginOwnWrite;
        documents.ProjectOpener = path => projects.OpenAsync(path);
        var projectManager = new ViewModels.ProjectManagerViewModel(projects, documents, platform.FileSystem, platform.Dialogs, commands,
            platform.Log, ImplementedPhase);
        projectManager.RegisterCommands();
        projects.SelectedFolder = () => projectManager.SelectedFolderPath;
        projects.SelectedFiles = () => focus.Current.ToolWindowName == ViewModels.ProjectManagerViewModel.ToolName
            ? projectManager.SelectedFilePaths
            : null;
        projects.RenameInPlace = projectManager.BeginRename;
        documents.SelectedNodePaths = () => focus.Current.ToolWindowName == ViewModels.ProjectManagerViewModel.ToolName
            ? projectManager.SelectedNodePaths
            : null;
        var session = new SessionService(platform.FileSystem, documents, projects, projectManager, layout, platform.UserPaths,
            platform.Clock, platform.Log, safe);
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
            if (opened.NeedsMessage)
                await platform.Dialogs.ShowMessageAsync(new MessageRequest(opened.MessageId!, opened.Args));
        });
        documents.WelcomeFactory = () => new ViewModels.WelcomeDocumentViewModel(recent, commands, settings, platform.FileSystem, platform.Clock, state);
        documents.ProjectNewUnit = async () =>
        {
            if (projects.Project is null)
                return false;
            await projects.AddNewUnitAsync(); // a failure is reported there; no untitled unit instead
            return true;
        };
        var output = new OutputService(platform.Clock, settings);
        var outputView = new ViewModels.OutputViewModel(output, settings, platform.Clipboard, documents, navigation, status, platform.FileSystem);
        outputView.RegisterCommands(commands);
        var application = new ApplicationService(documents, projects, session, recovery, settings, keymap, platform.Dialogs, platform.Clock,
            platform.Log);
        application.RegisterCommands(commands);
        EditorCommandRoute.Register(commands, ImplementedPhase, () => documents.ActiveDocument?.CommandTarget); // I-158
        commands.RegisterLaterPhaseStubs(ImplementedPhase);

        // IDE-ARCH-100: production tool windows, then the extra ones; an extra one with a production name replaces it.
        var extras = toolWindows ?? [];
        foreach (var definition in (ToolWindowDefinition[])[projectManager.Definition, outputView.Definition])
        {
            if (!extras.Any(e => e.Name == definition.Name))
                toolWindowService.Register(definition);
        }

        foreach (var definition in extras)
        {
            toolWindowService.Register(definition);
        }

        var services = new IdeServices
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
            FileChanges = fileChanges,
            Recovery = recovery,
            Projects = projects,
            Session = session,
            RecentItems = recent,
            Find = find,
            Navigation = navigation,
            Application = application,
            Help = help,
            State = state,
            Toolchain = toolchain,
            Output = output,
            Processes = platform.Processes,
        };
        VerifyCommands(commands, platform.Log); // I-091: every Phase 1 command has its handler since I-161
        return services;
    }

    /// <summary>
    /// IDE-ARCH-040 / I-091: every catalog command has a handler (later-phase stubs count). A missing one is a programming
    /// error: it is logged and the start-up fails with the exception (in tests, every test that composes the graph fails).
    /// </summary>
    /// <exception cref="InvalidOperationException">A catalog command has no handler.</exception>
    public static void VerifyCommands(ICommandService commands, ILog log)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(log);
        try
        {
            commands.VerifyAllHandled();
        }
        catch (InvalidOperationException e)
        {
            log.Error("commands", "Start-up check failed: " + e.Message, e);
            throw;
        }

        log.Info("commands", $"All {commands.Definitions.Count} catalog commands have a handler.");
    }
}
