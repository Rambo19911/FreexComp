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

namespace FreexComp.Ide.Core.Composition;

/// <summary>
/// The whole service graph of one IDE process (IDE-ARCH-010/011): one property per service interface, every
/// service a singleton. Built only by <see cref="ServiceGraph.Build"/>.
/// </summary>
public sealed record IdeServices
{
    public required StartupOptions Options { get; init; }

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
    public required IProjectService Projects { get; init; }
    public required ISessionService Session { get; init; }
    public required IRecentItemsService RecentItems { get; init; }
    public required IFindService Find { get; init; }
    public required INavigationService Navigation { get; init; }
}
