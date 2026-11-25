using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ToolWindows;
using FreexComp.Ide.Core.ViewModels;

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
    public IFileDialogService FileDialogs { get; set; } = new FakeFileDialogService();
    public ILog Log { get; set; } = new RecordingLog();
    public IThemeHost ThemeHost { get; set; } = new FakeThemeHost();
    public IProcessLauncher Processes { get; set; } = new FakeProcessLauncher();

    /// <summary>The build engine (IDE-P3-031): the scripted fake by default.</summary>
    public FreexComp.Build.IBuildEngine BuildEngine { get; set; } = new FakeBuildEngine();

    /// <summary>The installed platforms (IDE-PLAT-001): the product's catalogue by default; <see cref="TestPlatformCatalog"/> adds a second one.</summary>
    public FreexComp.Ide.Core.Projects.IPlatformCatalog PlatformCatalog { get; set; } = FreexComp.Ide.Core.Projects.BuiltInPlatformCatalog.Instance;

    /// <summary>
    /// The debug sessions (Phase 6): real sessions over the scripted adapter once a scenario is set
    /// (<see cref="FakeAdapterSessions.UseScenario"/>); without one a start fails as with a missing adapter.
    /// </summary>
    public FakeAdapterSessions DebugSessions { get; set; } = new();

    /// <summary>Core buffers by default; the headless harness uses the AvaloniaEdit factory like production.</summary>
    public ITextBufferFactory TextBuffers { get; set; } = new StringTextBufferFactory();

    /// <summary><c>--user-dir</c> of the composition.</summary>
    public string UserDir { get; set; } =
        Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"), "user");

    /// <summary>
    /// The automatic toolchain search (IDE-SET-050): by default no <c>FREEXCOMP_HOME</c>, no <c>PATH</c> and an IDE directory
    /// under the user directory, so a test never finds the real toolchain by accident (IDE-P1-001).
    /// </summary>
    public Core.Build.ToolchainEnvironment? ToolchainEnvironment { get; set; }

    /// <summary>Command-line arguments other than <c>--user-dir</c>.</summary>
    public List<string> Arguments { get; } = [];

    /// <summary>The directory relative command-line paths resolve against (IDE-P1-010); null = the current directory.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Tool windows registered after the production ones, in order (IDE-ARCH-100); one with the name of a production
    /// tool window (a sample <c>ProjectManager</c>) is registered instead of it.
    /// </summary>
    public List<ToolWindowDefinition> ToolWindows { get; } = [];

    /// <summary>Tool windows up to this phase register (testing.md §2.1 "ImplementedPhase override"); null = the product's phase.</summary>
    public int? ImplementedPhase { get; set; }
}

/// <summary>Builds the production service graph from fakes (IDE-ARCH-013, IDE-TEST-020).</summary>
public static class TestComposition
{
    public static IdeServices Build(Action<TestCompositionOptions>? configure = null)
    {
        var o = new TestCompositionOptions();
        configure?.Invoke(o);
        if (o.BuildEngine is FakeBuildEngine { FileSystem: null } fakeEngine)
            fakeEngine.FileSystem = o.FileSystem; // successful fake builds leave their executable (IDE-P3-081)

        var options = StartupOptions.Parse([.. o.Arguments, StartupOptions.UserDirOption, o.UserDir], o.WorkingDirectory);

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
            o.FileDialogs,
            o.ThemeHost,
            o.TextBuffers)
        {
            ToolchainEnvironment = o.ToolchainEnvironment ?? new Core.Build.ToolchainEnvironment(null, Path.Combine(o.UserDir, "ide"), []),
            Processes = o.Processes,
            BuildEngine = o.BuildEngine,
            DebugSessions = o.DebugSessions,
            PlatformCatalog = o.PlatformCatalog,
        };

        var services = ServiceGraph.Build(options, platform, o.ToolWindows, o.ImplementedPhase);

        // I-210: the test settles the background work it started before it ends: language-service requests (structure,
        // diagnostics, highlighting; a request queued after all earlier ones completes once they have posted their results)
        // missing-file checks, exits of the programs run, the folding of a large document and the Welcome page's checks.
        var worker = services.CodeModel.Worker;
        TestUiThread.RegisterWork(() => worker.IsIdle, () => worker.RunAsync("test.settle", static _ => 0));
        var projectManager = services.ProjectManager;
        TestUiThread.RegisterWork(() => projectManager.MissingFileChecks.IsCompleted, () => projectManager.MissingFileChecks);
        var run = services.Run;
        TestUiThread.RegisterWork(() => run.ExitsPosted, run.WhenExitsPostedAsync);
        if (services.Debug is { } debug) // Phase 6: a session the test left is ended; its events and breakpoint requests settle
            TestUiThread.RegisterWork(() => debug.IsIdle, debug.ShutdownAsync);
        services.Documents.DocumentOpened += (_, e) =>
        {
            if (e.Document is TextDocumentViewModel { Folding: var folding })
                TestUiThread.RegisterWork(() => folding.Computation.IsCompleted, () => folding.Computation);
            else if (e.Document is WelcomeDocumentViewModel welcome) // I-221: its recent-project existence checks
                TestUiThread.RegisterWork(() => welcome.MissingChecks.IsCompleted, () => welcome.MissingChecks);
        };
        return services;
    }
}
