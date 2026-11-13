using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ToolWindows;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// Platform inputs of a test composition; every property starts as a fresh fake and may be replaced.
/// The user directory is a fresh folder per composition (IDE-TEST-003); with the default
/// <see cref="InMemoryFileSystem"/> nothing reaches the disk.
/// </summary>
public sealed class TestCompositionOptions
{
    public IFileSystem FileSystem { get; set; } = new InMemoryFileSystem();
    public IClock Clock { get; set; } = new ManualClock();
    public IUiDispatcher Dispatcher { get; set; } = new ImmediateDispatcher();
    public IClipboardService Clipboard { get; set; } = new FakeClipboard();
    public IShellService? Shell { get; set; }
    public IFileWatcherFactory FileWatchers { get; set; } = new ManualFileWatcherFactory();
    public IDialogService Dialogs { get; set; } = new FakeDialogService();
    public ILog Log { get; set; } = new RecordingLog();
    public IThemeHost ThemeHost { get; set; } = new FakeThemeHost();

    /// <summary><c>--user-dir</c> of the composition.</summary>
    public string UserDir { get; set; } =
        Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"), "user");

    /// <summary>Command-line arguments other than <c>--user-dir</c>.</summary>
    public List<string> Arguments { get; } = [];

    /// <summary>Tool windows registered after the production ones, in order (IDE-ARCH-100).</summary>
    public List<ToolWindowDefinition> ToolWindows { get; } = [];
}

/// <summary>Builds the production service graph from fakes (IDE-ARCH-013, IDE-TEST-020).</summary>
public static class TestComposition
{
    public static IdeServices Build(Action<TestCompositionOptions>? configure = null)
    {
        var o = new TestCompositionOptions();
        configure?.Invoke(o);

        var options = StartupOptions.Parse([.. o.Arguments, StartupOptions.UserDirOption, o.UserDir]);

        // The environment variable is ignored and the OS folders are never asked for (IDE-TEST-003).
        var paths = UserPaths.Create(options.UserDir, _ => null, _ => throw new InvalidOperationException("Real user folder requested."));
        var platform = new PlatformServices(
            paths,
            o.Log,
            o.FileSystem,
            o.Clock,
            o.Dispatcher,
            o.Clipboard,
            o.Shell ?? new FakeShellService(o.FileSystem),
            o.FileWatchers,
            o.Dialogs,
            o.ThemeHost);

        var services = ServiceGraph.Build(options, platform);
        foreach (var definition in o.ToolWindows)
        {
            services.ToolWindows.Register(definition);
        }

        return services;
    }
}
