using Avalonia;
using Avalonia.Controls;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Platform;

namespace FreexComp.Ide;

/// <summary>
/// Production composition (IDE-ARCH-010, IDE-ARCH-015): creates the OS/Avalonia platform services and builds the
/// service graph with the same code the tests use (<see cref="ServiceGraph"/>).
/// </summary>
public static class CompositionRoot
{
    /// <summary>
    /// Steps 2–3 of start-up: user paths and log, then the other platform services.
    /// <paramref name="mainWindow"/> is asked for lazily (the clipboard needs a window; the graph is built first).
    /// </summary>
    public static PlatformServices CreatePlatform(StartupOptions options, Func<TopLevel?> mainWindow)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(mainWindow);

        var paths = UserPaths.FromProcess(options.UserDir);
        var log = new FileLog(paths.LogDirectory, () => DateTime.Now, FileLog.MinimumLevelFrom(Environment.GetEnvironmentVariable(FileLog.LevelVariable)));
        return new PlatformServices(
            paths,
            log,
            new PhysicalFileSystem(),
            new DispatcherClock(),
            new AvaloniaUiDispatcher(),
            new AvaloniaClipboard(mainWindow),
            new WindowsShellService(),
            new FileSystemWatcherFactory(),
            new LoggingDialogService(log),
            new AvaloniaThemeHost(Application.Current ?? throw new InvalidOperationException("No Avalonia application.")));
    }

    /// <summary>Step 6 of start-up.</summary>
    public static IdeServices Build(StartupOptions options, IPlatformServices platform) => ServiceGraph.Build(options, platform);

    /// <summary>Step 7 of start-up; the only place that creates the main window (the test harness calls it too).</summary>
    public static MainWindow CreateMainWindow(IdeServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new MainWindow();
    }
}
