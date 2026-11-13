using Avalonia;
using Avalonia.Controls;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Platform;
using FreexComp.Ide.Text;

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
        var clipboard = new AvaloniaClipboard(mainWindow);
        return new PlatformServices(
            paths,
            log,
            new PhysicalFileSystem(),
            new DispatcherClock(),
            new AvaloniaUiDispatcher(),
            clipboard,
            new WindowsShellService(),
            new FileSystemWatcherFactory(),
            new WindowDialogService(() => mainWindow() as Window, clipboard),
            new AvaloniaFileDialogService(mainWindow),
            new AvaloniaThemeHost(Application.Current ?? throw new InvalidOperationException("No Avalonia application.")),
            new AvaloniaEditTextBufferFactory());
    }

    /// <summary>Step 6 of start-up.</summary>
    public static IdeServices Build(StartupOptions options, IPlatformServices platform) => ServiceGraph.Build(options, platform);

    /// <summary>Step 7 of start-up; the only place that creates the main window (the test harness calls it too).</summary>
    public static MainWindow CreateMainWindow(IdeServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new MainWindow { DataContext = new MainWindowViewModel(services) };
    }
}
