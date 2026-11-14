using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
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

    /// <summary>
    /// Step 7 of start-up; the only place that creates the main window (the test harness calls it too). The start-up
    /// content (command line, last project, Welcome page) follows in step 9, <see cref="StartupSequence.RunAsync"/>.
    /// </summary>
    public static MainWindow CreateMainWindow(IdeServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Input.ListBehaviors.Register(); // UI-A11Y-007 type-ahead, UI-TW-016 trimmed-cell tooltips
        Input.FieldBehaviors.Register(); // UI-A11Y-022 LabeledBy / HelpText
        var window = new MainWindow { DataContext = new MainWindowViewModel(services) };
        var application = services.Application;
        application.Shutdown = window.Close; // UI-WIN-072 (4): a programmatic close, not intercepted by the exit sequence

        // IDE-ARCH-091: an exception that reached the dispatcher is reported and the IDE keeps running (for this window's
        // lifetime, so the services of a closed shell never answer for a later one).
        void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            _ = application.ReportUnhandledAsync(e.Exception);
        }

        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        window.Closed += (_, _) => Dispatcher.UIThread.UnhandledException -= OnUnhandled;
        return window;
    }

    /// <summary>
    /// IDE-ARCH-091 process-wide hooks (production only): unobserved task exceptions are logged; a fatal error writes the
    /// recovery copies synchronously before the runtime ends the process.
    /// </summary>
    public static void AttachProcessErrorHandlers(IdeServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var application = services.Application;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            application.ReportUnobserved(e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            application.ReportFatal(e.ExceptionObject as Exception ?? new InvalidOperationException(Convert.ToString(e.ExceptionObject, CultureInfo.InvariantCulture)));
    }
}
