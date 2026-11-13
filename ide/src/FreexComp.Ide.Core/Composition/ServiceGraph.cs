using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;

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
        var keymap = new KeymapService(platform.UserPaths, platform.FileSystem, platform.Clock, platform.Log);
        keymap.Load();

        var status = new StatusService(platform.Clock);
        var commands = new CommandService(keymap, platform.Dispatcher, platform.Clock, platform.Log, platform.Dialogs, status);
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
            Settings = settings,
            Keymap = keymap,
            Status = status,
            Commands = commands,
        };
    }
}
