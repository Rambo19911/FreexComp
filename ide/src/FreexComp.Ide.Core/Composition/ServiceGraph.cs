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
    public const int ImplementedPhase = 7;

    /// <param name="toolWindows">Tool windows registered after the production ones (tests, IDE-ARCH-100); one named like a
    /// production tool window is registered instead of it.</param>
    /// <param name="toolWindowPhase">Tool windows up to this phase register (default <see cref="ImplementedPhase"/>); tests
    /// use it to drive the Phase 3 tool windows before the implemented phase switches (I-127).</param>
    public static IdeServices Build(StartupOptions options, IPlatformServices platform, IReadOnlyList<ToolWindowDefinition>? toolWindows = null,
        int? toolWindowPhase = null, IReadOnlyList<Refactoring.IRefactoring>? refactorings = null)
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
        keymap.SetPreset(settings.Get(SettingsCatalog.KeyboardPreset)); // I-253: the built-in key map under the user's keys
        settings.Changed += (_, e) =>
        {
            if (e.Key == SettingsCatalog.KeyboardPreset.Name)
                keymap.SetPreset(settings.Get(SettingsCatalog.KeyboardPreset));
        };
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

        var layout = new LayoutService(platform.UserPaths, platform.FileSystem, platform.Clock, platform.Log, status, commands,
            debugProfile: (toolWindowPhase ?? ImplementedPhase) >= 6); // I-133
        if (safe)
            layout.LoadSafeMode();
        else
            layout.Load(); // step 7: UI state (small file, synchronous)
        var toolWindowService = new ToolWindowService(commands, focus, platform.Clock, layout, toolWindowPhase ?? ImplementedPhase); // tool windows register here (IDE-ARCH-100)
        theme.RegisterCommands(commands);
        ToolBarRowModel.RegisterCommands(commands, settings, toolWindowPhase ?? ImplementedPhase); // view.toolbar.designer from P5 (I-130)
        ViewModels.StatusBarViewModel.RegisterCommands(commands, settings);
        var languages = new LanguageRegistry();
        ProjectService? projects = null; // created after the documents it uses; the prompts ask it lazily
        var prompts = new FilePrompts(platform.FileDialogs, platform.FileSystem, layout, settings, platform.UserPaths,
            projectDirectory: () => projects?.Project?.Directory, isProjectMember: path => projects?.IsMember(path) == true);
        var toolchain = new ToolchainLocator(settings, platform.FileSystem, platform.ToolchainEnvironment); // IDE-P3-010
        var librarySource = new LibrarySourceService(toolchain, platform.FileSystem); // IDE-LIB-001
        var templates = new Editing.CodeTemplateService(platform.UserPaths, platform.FileSystem); // I-118
        var trust = new TrustService(platform.UserPaths, platform.FileSystem, platform.Dialogs, platform.FileDialogs, platform.Clipboard, layout); // I-267
        OptionsViewModel.RegisterCommands(commands, settings, keymap, layout, platform.Dialogs, prompts, theme, toolWindowPhase ?? ImplementedPhase, toolchain,
            platform.FileSystem, templates, trust.Store); // the phase override enables later pages in tests (I-132); the trust list: I-268
        ShellCommands.RegisterCommands(commands, platform.Shell, platform.FileSystem, platform.UserPaths, platform.Log);
        var documents = new DocumentService(platform.FileSystem, platform.TextBuffers, languages, settings, status, focus, platform.Dialogs, prompts)
        {
            ImplementedPhase = toolWindowPhase ?? ServiceGraph.ImplementedPhase, // I-213
        };
        documents.Layout = layout;
        documents.Clock = platform.Clock;
        documents.Clipboard = platform.Clipboard;
        documents.Shell = platform.Shell;
        documents.RegisterCommands(commands);
        var help = new HelpService(platform.Dialogs, platform.UserPaths, platform.Clipboard, layout, documents, keymap, platform.FileSystem,
            Path.Combine(platform.ToolchainEnvironment.IdeDirectory, "help")); // I-257: the pages shipped next to the IDE
        help.RegisterCommands(commands);
        var compare = new CompareService(documents, platform.FileSystem, platform.TextBuffers, languages, layout, status, platform.Dialogs,
            platform.FileDialogs, prompts, platform.Clipboard);
        compare.RegisterCommands(commands, toolWindowPhase ?? ImplementedPhase); // I-330: IDE-DIFF-027, IDE-DIFF-028
        help.ToolchainText = () => toolchain.Resolve().LlvmBinDir is { } llvm
            && ToolchainLocator.ValidateFolder(platform.FileSystem, llvm, ToolchainLocator.LlvmFiles).Valid
            ? "LLVM — " + llvm // the cached clang version joins with the process launcher (I-104)
            : null;
        var fileChanges = new FileChangeService(documents, platform.FileSystem, platform.FileWatchers, platform.Clock, platform.Dispatcher,
            settings, status, platform.Log, commands);
        documents.WriteGuard = fileChanges.BeginOwnWrite;
        var recovery = new RecoveryService(documents, platform.FileSystem, platform.UserPaths, platform.Clock, settings, platform.Log,
            safeMode: safe, compare: compare); // IDE-P1-012; Show Differences of a recovered document (IDE-P1-125)
        var find = new FindService(layout, status, platform.Clock, documents);
        find.RegisterCommands(commands);
        var navigation = new NavigationService(documents, platform.Dialogs, status, platform.FileSystem);
        navigation.RegisterCommands(commands);
        projects =new ProjectService(platform.FileSystem, documents, platform.Dialogs, status, platform.Log, prompts, settings,
            // gui-pascal is the designer's template (code-sync.md §11): listed from Phase 5 on.
            // library-pascal is delivered in Phase 8 (IDE-TGT-015): not listed by a composition of an earlier phase.
            [.. ProjectTemplates.LoadEmbedded(platform.Log).Where(t => (t.AppType != "gui" || (toolWindowPhase ?? ImplementedPhase) >= 5)
                && (t.AppType != "library" || (toolWindowPhase ?? ImplementedPhase) >= 7))],
            layout, platform.Clock, platform.Shell) { PlatformCatalog = platform.PlatformCatalog };
        projects.RegisterCommands(commands);
        var groups = new ProjectGroupService(projects, platform.FileSystem, documents, platform.Dialogs, status, platform.Log)
        {
            RecentItems = recent,
            Prompts = prompts,
        }; // IDE-PM-151
        groups.RegisterCommands(commands);
        projects.FileChanges = fileChanges; // IDE-P1-117
        projects.WriteGuard = fileChanges.BeginOwnWrite;
        documents.ProjectOpener = path => projects.OpenAsync(path);
        // IDE-LIB-002, IDE-LIB-006: library source that is not a member of the open project opens protected.
        documents.IsProtectedLibrarySource = path => librarySource.IsLibrarySource(path) && !projects.IsMember(path);
        documents.FormPathOf = projects.FormPathOf; // designer README §3: the project's form entry names the companion
        var libraryCopy = new LibraryCopyService(platform.FileSystem, documents, projects, librarySource, state, platform.Dialogs, status,
            platform.Log); // IDE-LIB-026
        libraryCopy.RegisterCommands(commands);
        documents.LibraryCopyAction = libraryCopy.CreateBarAction; // IDE-LIB-037
        var projectManager = new ViewModels.ProjectManagerViewModel(projects, documents, platform.FileSystem, platform.Dialogs, commands,
            platform.Log, toolWindowPhase ?? ImplementedPhase);
        projectManager.Group = groups;
        projectManager.RegisterCommands();
        groups.SelectedRoot = () => focus.Current.ToolWindowName == ViewModels.ProjectManagerViewModel.ToolName
            ? projectManager.SelectedMemberPath
            : null;
        projects.ListedMemberSelected = () => focus.Current.ToolWindowName == ViewModels.ProjectManagerViewModel.ToolName
            && projectManager.IsListedSelection;
        projects.SelectedFolder = () => projectManager.SelectedFolderPath;
        projects.SelectedFiles = () => focus.Current.ToolWindowName == ViewModels.ProjectManagerViewModel.ToolName
            ? projectManager.SelectedFilePaths
            : null;
        projects.RenameInPlace = projectManager.BeginRename;
        projects.RemovableFolder = () => focus.Current.ToolWindowName == ViewModels.ProjectManagerViewModel.ToolName && projectManager.CanRemoveSelectedFolder
            ? projectManager.RemoveSelectedFolderAsync
            : null;
        projectManager.Shell = platform.Shell;
        ShellCommands.RegisterConsoleCommand(commands, platform.Shell, platform.FileSystem, platform.Dialogs,
            () => focus.Current.ToolWindowName == ViewModels.ProjectManagerViewModel.ToolName
                ? projectManager.SelectedNodeDirectory
                : Path.GetDirectoryName(documents.ActiveDocument?.FilePath));
        documents.SelectedNodePaths = () => focus.Current.ToolWindowName == ViewModels.ProjectManagerViewModel.ToolName
            ? projectManager.SelectedNodePaths
            : null;
        var session = new SessionService(platform.FileSystem, documents, projects, projectManager, layout, platform.UserPaths,
            platform.Clock, platform.Log, safe);
        projects.Session = session;
        var bookmarks = new BookmarkService(documents, navigation, status, platform.FileSystem); // I-121, I-315
        bookmarks.RegisterCommands(commands);
        session.Bookmarks = bookmarks;
        // I-331: the local history of saved files (F11-059), where its phase-8 delivery applies.
        var history = new History.LocalHistoryService(platform.FileSystem, projects, settings, platform.Clock, platform.Dispatcher, status, platform.Log);
        if (Phase8Features.IsAvailable("setting:" + SettingsCatalog.HistoryEnabled.Name, 8, toolWindowPhase ?? ImplementedPhase))
            documents.History = history;
        projects.RecentItems = recent;
        // IDE-P1-130: a file document that is not a member of the open project joins the recent files.
        documents.DocumentOpened += (_, e) =>
        {
            if (e.Document.FilePath is { } path && !projects.IsMember(path))
                recent.Add(RecentKind.File, path);
        };
        recent.RegisterCommands(commands, platform.Dialogs, path => projects.OpenAsync(path), async path =>
        {
            var opened = await documents.OpenInDefaultViewAsync(path); // I-218
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
        var debugPhase = (toolWindowPhase ?? ImplementedPhase) >= 6;
        var output = new OutputService(platform.Clock, settings, designer: (toolWindowPhase ?? ImplementedPhase) >= 5, debugger: debugPhase);
        var outputView = new ViewModels.OutputViewModel(output, settings, platform.Clipboard, documents, navigation, status, platform.FileSystem);
        outputView.RegisterCommands(commands);
        outputView.Content.FindSource = find.For; // UI-TW-500: Ctrl+F in the Output
        find.FocusedText = () => focus.Current.ToolWindowName == ViewModels.OutputViewModel.ToolName ? outputView.Content : null;
        var messages = new MessagesService(documents, navigation, platform.FileSystem, status, layout) { IsProjectFile = projects.IsMember };
        templates.Start(platform.FileWatchers, platform.Clock, platform.Dispatcher, messages, platform.Log); // IDE-SET-081…083 (I-325)
        // I-337: the built-in Pascal formatter (IDE-ED-154…156) and format-on-save (IDE-SET-084), where its phase-8 delivery applies.
        var formatter = new Editing.FormatterService(platform.FileSystem, messages, status, () => documents.ActiveDocument?.VisibleText);
        if (Phase8Features.IsAvailable(Editing.FormatterService.FormatDocumentCommandId, 8, toolWindowPhase ?? ImplementedPhase))
        {
            formatter.RegisterCommands(commands);
            documents.Formatter = formatter;
        }
        var engine = platform.BuildEngine;
        var build = new BuildService(engine, projects, documents, settings, state, toolchain, output, messages, platform.Clock,
            platform.Dispatcher, platform.Dialogs, status, platform.Log, platform.Processes, trust)
        {
            OpenToolchainOptions = () => commands.ExecuteAsync("tools.options", OptionsViewModel.ToolchainPathsPage),
            Group = groups, // IDE-PM-170, 171
        };
        build.BuildFinished += (_, _) => groups.Refresh(); // IDE-PM-152: a build may have changed what the listed members show
        var codeModel = new CodeModel.CodeModelService(projects, documents, toolchain, platform.FileSystem); // I-110
        build.CheckSyntax = codeModel.GetProjectDiagnosticsAsync;
        build.DeployDatabaseLibrary = new DatabaseLibraryDeployer(platform.FileSystem, toolchain, codeModel.GetProjectUnitNamesAsync).DeployAsync; // I-225, I-280
        projects.UnitRename = new CodeModel.UnitRenameAnalyzer(codeModel, toolchain, librarySource); // I-236
        var refactoring = new Refactoring.RefactoringService(documents, projects, librarySource, platform.FileSystem, platform.Dialogs, status,
            platform.Clock, platform.Log, codeModel); // I-347
        // IDE-REFAC-007: the preview of an operation is the dialog RefactoringPreview (I-350).
        refactoring.Preview = edit => platform.Dialogs.ShowAsync(new RefactoringPreviewViewModel(edit, documents.ActiveDocument?.FilePath,
            platform.TextBuffers, languages, layout, platform.Clipboard));
        refactoring.RegisterCommands(commands, toolWindowPhase ?? ImplementedPhase); // I-349 refactor.undo, I-351 edit.quickActions
        // IDE-REFAC-033: the code actions of the language service are quick actions. IDE-REFAC-030: the refactorings — a
        // card that delivers one registers it here; a composition may add its own.
        refactoring.Register(new Refactoring.LanguageServiceQuickActions(codeModel, platform.FileSystem));
        foreach (var extra in refactorings ?? [])
            refactoring.Register(extra);
        var liveDiagnostics = new CodeModel.LiveDiagnosticsService(documents, codeModel, settings, platform.Clock, platform.Dispatcher); // I-111
        projects.ProjectChanged += (_, _) => liveDiagnostics.OnProjectChanged(projects.Definition); // IDE-ED-151 (I-324)
        var semanticHighlights = new CodeModel.SemanticHighlightService(documents, codeModel, settings, platform.Clock, platform.Dispatcher); // I-112
        projects.ProjectChanged += (_, _) => semanticHighlights.OnProjectChanged(projects.Definition); // I-247
        var goToDeclaration = new CodeModel.GoToDeclarationService(documents, projects, codeModel, navigation, status);
        goToDeclaration.RegisterCommands(commands); // I-117
        goToDeclaration.AttachDeclarationLinks(); // I-322
        new CodeModel.CompletionController(documents, projects, codeModel, settings, platform.Clock, status).RegisterCommands(commands); // I-114
        var parameterInfo = new CodeModel.ParameterInfoController(documents, projects, codeModel, settings, platform.Clock); // I-116
        parameterInfo.GoToDeclaration = goToDeclaration.GoAsync; // UI-ED-134 (I-317): the hover's Go to Declaration link
        parameterInfo.RegisterCommands(commands);
        new CodeModel.CodeTemplateController(documents, templates, settings).RegisterCommands(commands);
        build.RegisterCommands(commands);
        var structureView = new ViewModels.StructureViewModel(documents, platform.Clock, platform.Dispatcher, codeModel.Worker, layout, navigation); // I-124
        structureView.RegisterCommands(commands);
        var messagesView = new ViewModels.MessagesViewModel(messages, settings, platform.Clipboard);
        messagesView.RegisterCommands(commands);
        var findInFiles = new FindInFilesService(documents, projects, platform.FileSystem, settings, platform.Clock); // I-122
        var searchResults = new ViewModels.SearchResultsViewModel(findInFiles, documents, projects, navigation, platform.Dialogs, platform.FileDialogs,
            platform.FileSystem, layout, status, platform.Clipboard, platform.Dispatcher); // I-123
        searchResults.RegisterCommands(commands, toolWindowService, toolWindowPhase ?? ImplementedPhase); // I-354: searchResults.pin
        // I-353 (F13-016a): Find References lists its result in Search Results as a result set of kind References.
        var findReferences = new CodeModel.FindReferencesService(documents, projects, codeModel, librarySource, status, platform.Clock, searchResults);
        if (Phase8Features.IsAvailable(CodeModel.FindReferencesService.CommandId, 8, toolWindowPhase ?? ImplementedPhase))
            findReferences.RegisterCommands(commands);
        // UI-TW-603: F8 steps through Search Results while it is the more recently activated of the two windows.
        var stepSearchResults = false;
        build.ShowMessages = () =>
        {
            stepSearchResults = false;
            toolWindowService.Show(ViewModels.MessagesViewModel.ToolName, focus: false); // UI-TW-409
        };
        focus.Changed += (_, _) =>
        {
            if (focus.Current.ToolWindowName == ViewModels.SearchResultsViewModel.ToolName)
                stepSearchResults = true;
            else if (focus.Current.ToolWindowName == ViewModels.MessagesViewModel.ToolName)
                stepSearchResults = false;
        };
        searchResults.ResultsShown += (_, _) => stepSearchResults = true; // IDE-FREF-003: shown without the focus
        build.ShowBuildOutput = () => outputView.SelectChannel(OutputChannels.Build); // UI-TW-501
        // UI-TW-405: F8 / Shift+F8 from anywhere; the Messages tab is selected without taking focus.
        foreach (var (id, forward) in (ReadOnlySpan<(string, bool)>)[("search.nextMessage", true), ("search.previousMessage", false)])
        {
            commands.RegisterHandler(id, new MessageStepHandler(messages, forward,
                () => toolWindowService.IsRegistered(ViewModels.MessagesViewModel.ToolName)
                    && toolWindowService.Show(ViewModels.MessagesViewModel.ToolName, focus: false),
                () => stepSearchResults && searchResults.HasMatches ? searchResults : null));
        }
        var run = new RunService(build, projects, settings, state, toolchain, output, platform.Processes, platform.FileSystem, platform.Clock,
            platform.Dispatcher, platform.Dialogs, status, platform.Log)
        {
            OpenRunParameters = () => commands.ExecuteAsync("run.parameters"),
        };
        run.ShowProgramOutput = () =>
        {
            outputView.SelectChannel(OutputChannels.Program); // UI-TW-501 with output.autoShowOnRun
            toolWindowService.Show(ViewModels.OutputViewModel.ToolName, focus: false);
        };
        run.RegisterCommands(commands, debugger: debugPhase);
        // Phase 6 (I-134, I-139, I-140): breakpoints and the debug session; run.start / stop / restart belong to the debug service.
        Debugging.BreakpointService? breakpoints = null;
        Debugging.DebugService? debug = null;
        if (debugPhase)
        {
            breakpoints = new Debugging.BreakpointService(documents, projects, platform.Dialogs, platform.FileDialogs, platform.FileSystem, status,
                platform.Dispatcher, platform.Log);
            breakpoints.RegisterCommands(commands);
            session.Breakpoints = breakpoints;
            debug = new Debugging.DebugService(platform.DebugSessions, run, projects, settings, state, toolchain, output, breakpoints, documents,
                navigation, platform.Clock, platform.Dispatcher, platform.Dialogs, status, platform.Log)
            {
                OpenProjectOptions = () => commands.ExecuteAsync("project.options"),
                UiState = layout,
                Clipboard = platform.Clipboard, // UI-DLG-327
                SelectProgramOutput = () => outputView.SelectChannel(OutputChannels.Program),
                ShowWatches = () => toolWindowService.Show(ViewModels.WatchesViewModel.ToolName, focus: false), // UI-DLG-200
                LibrarySource = librarySource, // IDE-LIB-022
            };
            debug.RegisterCommands(commands);
            breakpoints.ValidateCondition = debug.ConditionProblemAsync;
            session.Watches = debug.Watches; // IDE-ARCH-076
            session.Debug = debug; // IDE-LIB-020
            parameterInfo.DebugValue = debug.HoverValueAsync; // UI-ED-130: "= value" in the hover tooltip while paused
        }

        ProjectDialogCommands.Register(commands, projects, platform.Dialogs, platform.FileDialogs, platform.FileSystem, layout, status); // I-107, I-108, I-248
        ProjectDependenciesViewModel.RegisterCommand(commands, groups, projects, platform.FileSystem, platform.Dialogs, layout,
            () => groups.SelectedRoot?.Invoke()); // IDE-PM-175
        projects.BeforeClose = async () => (debug is null || await debug.ConfirmStopAsync(exiting: false)) && await run.ConfirmStopAsync(exiting: false); // IDE-P3-093
        projects.ProjectChanged += (_, _) =>
        {
            if (projects.Project is null)
            {
                messages.Clear(); // IDE-P3-067
                breakpoints?.Load([]); // UI-TW-800: breakpoints belong to the project (its session file has them)
                debug?.Watches.Load([]); // UI-TW-1100: and so do the watches
                if (debug is not null)
                    debug.StepIntoLibrarySource = false; // IDE-LIB-020: and the library-stepping choice
            }
        };
        var application = new ApplicationService(documents, projects, session, recovery, settings, keymap, platform.Dialogs, platform.Clock,
            platform.Log);
        application.RegisterCommands(commands);
        // IDE-P3-093, then IDE-PM-156: the unsaved group is asked about before the unsaved documents.
        application.BeforeExit = async () => (debug is null || await debug.ConfirmStopAsync(exiting: true)) && await run.ConfirmStopAsync(exiting: true)
            && await groups.ConfirmCloseAsync();
        application.AfterSavePrompt = build.CancelForExitAsync; // file.exit while Building (phase3-build-run.md §2)
        Designer.DesignerShell? designerShell = null; // Phase 5 on (below); the palette lists its verbs
        var quickPick = new Popups.QuickPickHost(); // I-125, I-126 (Phase 3 commands: available from the implemented phase 3)
        projectManager.ShowQuickPick = quickPick.Show; // UI-TW-124
        projectManager.IsIdle = () => state.Current.State == Models.IdeState.Idle; // IDE-PM-120
        var popupsAvailable = (toolWindowPhase ?? ImplementedPhase) >= 3;
        commands.RegisterHandler("search.goToFile", new DelegateHandler(() => popupsAvailable, () =>
            quickPick.Show(new Popups.GoToFileViewModel(documents, projects, navigation, layout, platform.Dialogs))));
        commands.RegisterHandler("search.commandPalette", new DelegateHandler(() => popupsAvailable, () =>
            quickPick.Show(new Popups.CommandPaletteViewModel(commands, layout, focus.Current, toolWindowPhase ?? ImplementedPhase,
                designerShell?.ActiveDesigner?.Surface?.Verbs)))); // D-111: the selection's designer verbs (DSG-SUR-191)
        // D-077: component metadata and component.reloadMetadata from Phase 5 on; StartupSequence starts the load (D-088).
        Designer.ComponentMetadataService? metadata = null;
        Designer.ObjectInspectorViewModel? objectInspector = null;
        Designer.ToolPaletteViewModel? toolPalette = null;
        Designer.ProjectAncestorCatalog? frames = null;
        if ((toolWindowPhase ?? ImplementedPhase) >= 5)
        {
            frames = Designer.ProjectAncestorCatalog.ForIde(projects, documents, platform.FileSystem, output); // DSG-FD-103, 104
            var projectFrames = frames;
            projects.InheritableClasses = projectFrames.Inheritable; // DSG-FD-123: the New Inherited Form dialog (D-132)
            metadata = new Designer.ComponentMetadataService(documents, platform.FileSystem, settings, status, platform.Log, platform.ToolchainEnvironment,
                platform.Dialogs) { Output = output }; // D-088: loaded by the start-up sequence (DSG-TP-003)
            metadata.RegisterCommands(commands);
            metadata.CatalogChanged += (_, _) => projectFrames.Refresh(); // the frames are read with the metadata
            designerShell = new Designer.DesignerShell(commands, documents, settings, layout.Layout, toolWindowPhase ?? ImplementedPhase,
                platform.Dialogs, layout); // D-080, I-130; D-097: the verbs' editors (their sizes: D-112)
            designerShell.ShowProblems = (form, problems) =>
            {
                messages.ShowFormDiagnostics(problems, problem => // D-108: the form's problems only
                {
                    documents.Activate(form); // DSG-FD-071: a row opens the form's Text view (or selects the object)
                    form.Designer.GoToProblem(problem);
                });
                toolWindowService.Show(ViewModels.MessagesViewModel.ToolName, focus: false);
            };
            var shell = designerShell;
            var inspector = new Designer.ObjectInspectorViewModel(status, settings, platform.Dialogs, prompts); // D-081; I-148: the file-name editor
            objectInspector = inspector;
            Designer.ObjectInspectorCommands.Register(commands, inspector, platform.Clipboard);
            session.ObjectInspector = inspector; // D-109: page, categories, expanded rows, divider ratio in session.json
            shell.ActiveDesignerChanged += (_, _) => inspector.Bind(shell.ActiveDesigner);
            inspector.Bind(shell.ActiveDesigner);
            var palette = new Designer.ToolPaletteViewModel(metadata.Catalog, settings, platform.FileSystem, platform.Log); // D-083
            toolPalette = palette;
            palette.RegisterCommands(commands);
            palette.HasProject = () => projects.Project is not null; // DSG-TP-061
            projects.ProjectChanged += (_, _) => palette.Refresh();
            metadata.CatalogChanged += (_, catalog) => palette.SetCatalog(catalog);
            shell.ActiveDesignerChanged += (_, _) => palette.Bind(shell.ActiveDesigner);
            palette.Bind(shell.ActiveDesigner);
        }

        // I-133: the debug tool windows, the Debug profile (UI-WIN-012 (b) overrides the designer's rule (a)) and toolbar context.
        Debugging.DebugShell? debugShell = null;
        if (debug is not null && breakpoints is not null)
        {
            var activeDesign = designerShell;
            debugShell = new Debugging.DebugShell(commands, debug, breakpoints, settings, layout, documents, navigation, platform.Clipboard,
                platform.Dialogs, () => activeDesign?.IsDesignActive == true);
            var holder = debugShell;
            designerShell?.AutoSwitchSuspended = () => holder.HoldsProfile;
        }

        // I-316: the Bookmarks window and its commands (F11-045), where its phase-8 delivery applies.
        var bookmarksView = new ViewModels.BookmarksViewModel(commands, bookmarks, toolWindowService);
        if (Phase8Features.IsAvailable("toolwindow:" + ViewModels.BookmarksViewModel.ToolName, 8, toolWindowPhase ?? ImplementedPhase))
            bookmarksView.RegisterCommands();

        // I-328: the Templates window and its commands (F11-052), where its phase-8 delivery applies.
        var templatesView = new ViewModels.TemplatesViewModel(commands, templates, documents);
        if (Phase8Features.IsAvailable("toolwindow:" + ViewModels.TemplatesViewModel.ToolName, 8, toolWindowPhase ?? ImplementedPhase))
            templatesView.RegisterCommands();

        // I-332: the History window and its commands (F11-060), where its phase-8 delivery applies.
        var historyView = new ViewModels.HistoryViewModel(commands, history, documents, layout, languages, platform.TextBuffers, status,
            platform.Dialogs, platform.Clock, platform.Clipboard);
        if (Phase8Features.IsAvailable("toolwindow:" + ViewModels.HistoryViewModel.ToolName, 8, toolWindowPhase ?? ImplementedPhase))
            historyView.RegisterCommands();

        // A test phase below the product's (testing.md §2.1) gives the later commands the stubs of UI-CMD-004 as before I-207.
        EditorCommandRoute.Register(commands, toolWindowPhase ?? ImplementedPhase, () => documents.ActiveDocument?.CommandTarget); // I-158
        commands.RegisterLaterPhaseStubs(toolWindowPhase ?? ImplementedPhase);

        // IDE-ARCH-100: production tool windows, then the extra ones; an extra one with a production name replaces it.
        var extras = toolWindows ?? [];
        ToolWindowDefinition[] production = [projectManager.Definition, structureView.Definition, messagesView.Definition, outputView.Definition, searchResults.Definition];
        if (objectInspector is not null)
            production = [.. production, objectInspector.Definition];
        if (toolPalette is not null)
            production = [.. production, toolPalette.Definition];
        if (debugShell is not null)
            production = [.. production, .. debugShell.ToolWindows];
        production = [.. production, new ViewModels.DataExplorerViewModel().Definition]; // I-137: registers from Phase 7 (UI-TW-1401)
        production = [.. production, bookmarksView.Definition]; // I-316: registers where delivered (UI-TW-006)
        production = [.. production, templatesView.Definition]; // I-328: registers where delivered (UI-TW-006)
        production = [.. production, historyView.Definition]; // I-332: registers where delivered (UI-TW-006)
        foreach (var definition in production)
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
            ImplementedPhase = toolWindowPhase ?? ImplementedPhase,
            UserPaths = platform.UserPaths,
            Trust = trust,
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
            Groups = groups,
            Session = session,
            RecentItems = recent,
            Find = find,
            Navigation = navigation,
            Bookmarks = bookmarks,
            History = history,
            Templates = templates,
            Application = application,
            Help = help,
            State = state,
            Toolchain = toolchain,
            LibrarySource = librarySource,
            Output = output,
            Processes = platform.Processes,
            BuildEngine = engine,
            PlatformCatalog = platform.PlatformCatalog,
            Messages = messages,
            Build = build,
            Run = run,
            QuickPick = quickPick,
            SearchResults = searchResults,
            FindReferences = findReferences,
            ProjectManager = projectManager,
            CodeModel = codeModel,
            Refactoring = refactoring,
            SemanticHighlights = semanticHighlights,
            LiveDiagnostics = liveDiagnostics,
            Metadata = metadata,
            Frames = frames,
            DesignerShell = designerShell,
            Breakpoints = breakpoints,
            Debug = debug,
            DebugShell = debugShell,
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

    private sealed class DelegateHandler(Func<bool> canExecute, Action execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument)
        {
            execute();
            return Task.CompletedTask;
        }
    }

    /// <summary><c>search.nextMessage</c> / <c>search.previousMessage</c> over the Messages rows (IDE-P3-066).</summary>
    private sealed class MessageStepHandler(MessagesService messages, bool forward, Func<bool> showTab,
        Func<ViewModels.SearchResultsViewModel?> searchResults) : ICommandHandler
    {
        public bool CanExecute(string? argument) => searchResults() is not null || messages.Items.Any(i => i.IsNavigable && !i.IsSummary);

        public async Task ExecuteAsync(string? argument)
        {
            if (searchResults() is { } results)
            {
                await results.NavigateNextAsync(forward);
                return;
            }

            showTab();
            await messages.NavigateNextAsync(forward);
        }
    }
}
