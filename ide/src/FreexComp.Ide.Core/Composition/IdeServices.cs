using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ToolWindows;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Layout;

namespace FreexComp.Ide.Core.Composition;

/// <summary>
/// The whole service graph of one IDE process (IDE-ARCH-010/011): one property per service interface, every
/// service a singleton. Built only by <see cref="ServiceGraph.Build"/>.
/// </summary>
public sealed record IdeServices
{
    public required StartupOptions Options { get; init; }

    /// <summary>Toolbars and tool windows of phases up to this one are shown (<see cref="ServiceGraph.ImplementedPhase"/>; tests may raise it).</summary>
    public required int ImplementedPhase { get; init; }

    // Platform inputs (IDE-ARCH-012).
    public required IUserPaths UserPaths { get; init; }
    public required ILog Log { get; init; }
    public required IFileSystem FileSystem { get; init; }
    public required IClock Clock { get; init; }
    public required IUiDispatcher Dispatcher { get; init; }
    public required IClipboardService Clipboard { get; init; }
    public required IShellService Shell { get; init; }
    public required IFileWatcherFactory FileWatchers { get; init; }
    public required IDialogService Dialogs { get; init; }
    public required IFileDialogService FileDialogs { get; init; }
    public required IThemeHost ThemeHost { get; init; }
    public required ITextBufferFactory TextBuffers { get; init; }

    // Services (architecture.md §4), in construction order.
    public required ISettingsService Settings { get; init; }
    public required IThemeService Theme { get; init; }
    public required IKeymapService Keymap { get; init; }
    public required IStatusService Status { get; init; }
    public required ICommandService Commands { get; init; }
    public required IFocusTracker FocusTracker { get; init; }
    public required ILayoutService Layout { get; init; }
    public required IToolWindowService ToolWindows { get; init; }
    public required ILanguageRegistry Languages { get; init; }
    public required FilePrompts FilePrompts { get; init; }
    public required IDocumentService Documents { get; init; }
    public required IFileChangeService FileChanges { get; init; }
    public required IRecoveryService Recovery { get; init; }
    public required IProjectService Projects { get; init; }
    public required ISessionService Session { get; init; }
    public required IRecentItemsService RecentItems { get; init; }
    public required IFindService Find { get; init; }
    public required INavigationService Navigation { get; init; }

    /// <summary>Bookmarks (I-121, IDE-ED-110…114).</summary>
    public required BookmarkService Bookmarks { get; init; }
    public required IApplicationService Application { get; init; }
    public required IHelpService Help { get; init; }

    /// <summary>The IDE state (phase3-build-run.md §2); its read side is <see cref="Build.IIdeState"/>.</summary>
    public required Build.IdeStateMachine State { get; init; }

    /// <summary>The toolchain directories (IDE-P3-010, IDE-SET-050).</summary>
    public required Build.IToolchainLocator Toolchain { get; init; }

    /// <summary>The Output channels (IDE-P3-070).</summary>
    public required Build.IOutputService Output { get; init; }

    /// <summary>Starts programs for Run (IDE-P3-083).</summary>
    public required IProcessLauncher Processes { get; init; }

    /// <summary>The build engine (IDE-P3-030).</summary>
    public required FreexComp.Build.IBuildEngine BuildEngine { get; init; }

    /// <summary>The Messages model (IDE-P3-060…068).</summary>
    public required Build.MessagesService Messages { get; init; }

    /// <summary>Build, rebuild, clean (phase3-build-run.md §5).</summary>
    public required Build.BuildService Build { get; init; }

    /// <summary>Run, stop, restart (phase3-build-run.md §8–§9).</summary>
    public required Build.RunService Run { get; init; }

    /// <summary>The open quick-pick popup (Go to File, Command Palette; dialogs.md §13/§14).</summary>
    public required Popups.QuickPickHost QuickPick { get; init; }

    /// <summary>The project <c>Compilation</c> and the language-service worker (I-110).</summary>
    public required CodeModel.CodeModelService CodeModel { get; init; }

    /// <summary>The P3 classification layer of the open Pascal documents (I-112).</summary>
    public required CodeModel.SemanticHighlightService SemanticHighlights { get; init; }

    /// <summary>The Search Results tool window and Find in Files runner (I-123).</summary>
    public required ViewModels.SearchResultsViewModel SearchResults { get; init; }
}
