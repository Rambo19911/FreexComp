using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Input;
using FreexComp.Ide.Platform;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Text;
using FreexComp.Ide.Views;
using CoreGesture = FreexComp.Ide.Core.Commands.KeyGesture;

namespace FreexComp.Ide.Tests.Infrastructure;

/// <summary>Options of <see cref="HeadlessHarness.StartAsync"/> (testing.md §2.1).</summary>
public sealed class HarnessOptions
{
    /// <summary>
    /// Command-line arguments; <c>--user-dir</c> is added by the harness. Relative paths resolve against
    /// <see cref="HeadlessHarness.TempDir"/> (the process's current directory in production, IDE-P1-010).
    /// </summary>
    public List<string> CommandLineArgs { get; } = [];

    /// <summary>Message id → button id, scripted before start so that start-up message boxes are answered.</summary>
    public Dictionary<string, string> MessageAnswers { get; } = new(StringComparer.Ordinal);

    /// <summary>Relative path (under <see cref="HeadlessHarness.TempDir"/>) → UTF-8 content, written before start.</summary>
    public Dictionary<string, string> InitialFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Relative path under <see cref="HeadlessHarness.UserDir"/> (<c>roaming/uistate.json</c>) → bytes, written before start.</summary>
    public Dictionary<string, byte[]> UserFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Setting key → value, written to <c>settings.json</c> before start. Presets: <c>run.consoleMode = output</c>;
    /// <c>environment.showWelcomeOnStartup = false</c> so the document area starts empty (remove it to start like a
    /// first run).
    /// </summary>
    public Dictionary<string, object> Settings { get; } = new(StringComparer.Ordinal)
    {
        ["run.consoleMode"] = "output",
        ["environment.showWelcomeOnStartup"] = false,
    };

    /// <summary>
    /// The per-test temp root (must not exist yet); null = a new folder under <c>%TEMP%\FreexComp.Tests</c>. Set it when
    /// seeded user files must name absolute paths under <see cref="HeadlessHarness.TempDir"/> (<c>&lt;Root&gt;\work</c>).
    /// </summary>
    public string? Root { get; set; }

    public Size WindowSize { get; set; } = new(1440, 900); // IDE-TEST-018

    /// <summary>Extra tool windows, registered before the main window is created (IDE-ARCH-100).</summary>
    public List<Core.ToolWindows.ToolWindowDefinition> ToolWindows { get; } = [];

    public double Scale { get; set; } = 1.0;

    /// <summary>Tool windows up to this phase register (testing.md §2.1); null = the product's implemented phase.</summary>
    public int? ImplementedPhase { get; set; }

    /// <summary>
    /// D-088: the test component metadata (<c>TestData/metadata</c>) is copied beside the IDE directory of the composition
    /// (<c>&lt;UserDir&gt;\ide\ui-framework\design</c>, DSG-TP-001 step 3) and the start-up sequence loads it; false: no
    /// index anywhere under the root (DSG-TP-004).
    /// </summary>
    public bool ComponentMetadata { get; set; } = true;

    /// <summary>
    /// D-088: when set, every read of a metadata file waits for this event (DSG-TP-003: the load is still running when
    /// <see cref="HeadlessHarness.StartAsync"/> returns); without it start-up waits for the load to finish.
    /// </summary>
    public ManualResetEventSlim? MetadataGate { get; set; }

    /// <summary>
    /// Watch with the real <c>FileSystemWatcher</c> (<see cref="FileSystemWatcherFactory"/>) instead of the manual one;
    /// <see cref="HeadlessHarness.WriteFileExternally"/> then raises nothing itself.
    /// </summary>
    public bool RealFileWatchers { get; set; }

    /// <summary>
    /// Build with the real engine (<c>FreexComp.Build.BuildEngine</c> over the harness file system) and start real processes
    /// (<see cref="FreexComp.Ide.Platform.Windows.WindowsProcessLauncher"/>) instead of <see cref="HeadlessHarness.Build"/> and
    /// <see cref="HeadlessHarness.Processes"/> (testing.md category <c>Toolchain</c>); the toolchain is then searched like the
    /// product does from its own folder (the test assembly's folder inside the checkout, IDE-SET-050).
    /// </summary>
    public bool RealBuild { get; set; }

    /// <summary>
    /// With <see cref="RealBuild"/>: wraps the real engine before the IDE gets it (F09-001: a test records the
    /// <c>BuildEvent</c> stream and the result the IDE received).
    /// </summary>
    public Func<FreexComp.Build.IBuildEngine, FreexComp.Build.IBuildEngine>? WrapBuildEngine { get; set; }

    /// <summary>The installed platforms (IDE-PLAT-001); null = the product's catalogue.</summary>
    public Core.Projects.IPlatformCatalog? PlatformCatalog { get; set; }

    /// <summary>The deadline of the harness's waits; null = the running test's own (the harness's self-tests pass one with a clock of their own).</summary>
    public TestDeadline? Deadline { get; set; }
}

/// <summary>Thrown by <see cref="HeadlessHarness.DisposeAsync"/> when a test left a failure behind (IDE-TEST-014).</summary>
public sealed class HarnessAssertionException(string message) : Exception(message);

/// <summary>
/// Starts the IDE shell headless with real services over a fake platform (IDE-TEST-011…017): real disk under a
/// per-test temp root (seen through a <see cref="RecordingFileSystem"/>), <see cref="ManualClock"/>, the Avalonia
/// dispatcher, scripted message boxes. Dispose checks for leftover failures and deletes the temp root.
/// </summary>
public sealed class HeadlessHarness : IAsyncDisposable
{
    private static readonly TimeSpan WaitStep = TimeSpan.FromMilliseconds(10);

    private readonly int _bindingErrorStart;
    private readonly TestDeadline _deadline;
    private string? _timedOutWait; // I-258: the first wait that used up the test's time
    private readonly RecordingLog _log;
    private readonly ManualFileWatcherFactory _watchers;
    private readonly bool _realWatchers;
    private readonly List<string> _unhandled = [];
    private MainWindow? _window;
    private bool _errorExpected;
    private bool _unhandledExpected;
    private bool _disposed;
    private readonly ManualResetEventSlim? _metadataGate;

    private HeadlessHarness(HarnessOptions options)
    {
        _bindingErrorStart = BindingErrorSink.Instance.Count;
        _deadline = options.Deadline ?? CurrentTest.Deadline;
        Root = options.Root ?? Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        TempDir = Path.Combine(Root, "work");
        UserDir = Path.Combine(Root, "user");
        Directory.CreateDirectory(TempDir);
        WriteInitialFiles(options);
        var metadataFolder = Path.Combine(UserDir, "ide", "ui-framework", "design");
        WriteSettings(options, metadataFolder);
        if (options.ComponentMetadata)
            CopyTestMetadata(metadataFolder);
        _metadataGate = options.MetadataGate;

        FileSystem = new RecordingFileSystem(new PhysicalFileSystem());
        if (options.MetadataGate is { } gate)
        {
            FileSystem.BeforeRead = path =>
            {
                if (path.StartsWith(metadataFolder, StringComparison.OrdinalIgnoreCase))
                    gate.Wait(_deadline.Remaining); // held on the background thread of the load (DSG-TP-003)
            };
        }
        Clock = new ManualClock(DateTimeOffset.Now);
        var clipboard = new FakeClipboard();
        Messages = new MessageScript(new WindowDialogService(() => _window, clipboard));
        foreach (var (messageId, buttonId) in options.MessageAnswers)
        {
            Messages.Answer(messageId, buttonId);
        }

        _log = new RecordingLog();
        _watchers = new ManualFileWatcherFactory();
        _realWatchers = options.RealFileWatchers;
        RealBuild = options.RealBuild;
        FileDialogs = new FakeFileDialogService();

        Dispatcher.UIThread.UnhandledException += OnUnhandledException;
        Services = TestComposition.Build(o =>
        {
            o.FileSystem = FileSystem;
            o.Clock = Clock;
            o.Dispatcher = new AvaloniaUiDispatcher();
            o.Dialogs = Messages;
            o.FileDialogs = FileDialogs;
            o.Clipboard = clipboard;
            o.Log = _log;
            o.FileWatchers = options.RealFileWatchers ? new FileSystemWatcherFactory() : _watchers;
            o.UserDir = UserDir;
            o.ThemeHost = new AvaloniaThemeHost(Application.Current!);
            o.TextBuffers = new AvaloniaEditTextBufferFactory();
            o.Arguments.AddRange(options.CommandLineArgs);
            o.WorkingDirectory = TempDir;
            o.ToolWindows.AddRange(options.ToolWindows);
            o.ImplementedPhase = options.ImplementedPhase;
            // I-395: the console host is the IDE executable beside the test assembly, not this process (the test host).
            o.Processes = options.RealBuild ? new FreexComp.Ide.Platform.Windows.WindowsProcessLauncher(Clock, ConsoleHostExecutable) : Processes;
            if (options.RealBuild)
                o.ToolchainEnvironment = new Core.Build.ToolchainEnvironment(null, AppContext.BaseDirectory, []);
            FreexComp.Build.IBuildEngine real = new FreexComp.Build.BuildEngine(new Core.Projects.CompilerFileSystemAdapter(FileSystem));
            o.BuildEngine = options.RealBuild ? options.WrapBuildEngine?.Invoke(real) ?? real : Build;
            o.DebugSessions = DebugSessions;
            if (options.PlatformCatalog is { } catalog)
                o.PlatformCatalog = catalog;
        });

        Status = new StatusBarDriver(this);
        Window = _window = CompositionRoot.CreateMainWindow(Services);
        Window.Width = options.WindowSize.Width;
        Window.Height = options.WindowSize.Height;
        Window.Show();
        if (options.Scale != 1.0)
        {
            Window.SetRenderScaling(options.Scale);
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The per-test temp root holding <see cref="TempDir"/> and <see cref="UserDir"/>.</summary>
    public string Root { get; }

    public string TempDir { get; }

    public string UserDir { get; }

    public MainWindow Window { get; }

    /// <summary>The main window and every window it owns (dialogs), also through a dialog (a message box over a dialog, D-112).</summary>
    public IReadOnlyList<Window> AllWindows => [Window, .. Owned(Window)];

    /// <summary>The windows <paramref name="owner"/> owns, each followed by the windows it owns in turn.</summary>
    private static IEnumerable<Window> Owned(Window owner) => owner.OwnedWindows.SelectMany(w => Owned(w).Prepend(w));

    public IdeServices Services { get; }

    public ManualClock Clock { get; }

    public MessageScript Messages { get; }

    /// <summary>Started with <see cref="HarnessOptions.RealBuild"/>: <see cref="Build"/> and <see cref="Processes"/> are unused.</summary>
    public bool RealBuild { get; }

    /// <summary>The scripted build engine (IDE-P3-031).</summary>
    public FakeBuildEngine Build { get; } = new();

    /// <summary>The scripted process launcher (IDE-P3-094).</summary>
    public FakeProcessLauncher Processes { get; } = new();

    /// <summary>
    /// Phase 6 (testing.md §2.1 "UseFakeDebugAdapter"): the debug sessions of the test; with a scenario
    /// (<see cref="FakeAdapterSessions.UseScenario"/>) they run over the debugger area's scripted adapter.
    /// </summary>
    public FakeAdapterSessions DebugSessions { get; } = new();

    /// <summary>The IDE executable next to the test assembly (the project reference copies its apphost): the console host of <see cref="HarnessOptions.RealBuild"/>.</summary>
    public static string ConsoleHostExecutable => Path.ChangeExtension(typeof(FreexComp.Ide.Platform.Windows.ConsoleHost).Assembly.Location, ".exe");

    /// <summary>The scripted OS pickers (IDE-TEST-015); an unscripted picker request fails the test.</summary>
    public FakeFileDialogService FileDialogs { get; }

    /// <summary>The status-bar fields (testing.md §2.1).</summary>
    public StatusBarDriver Status { get; }

    /// <summary>The services' log lines.</summary>
    public RecordingLog Log => _log;

    /// <summary>Every file-system call the services made.</summary>
    public RecordingFileSystem FileSystem { get; }

    /// <summary>Nearest ancestor-or-self of the focused element that has an AutomationId.</summary>
    public string? FocusedAutomationId
    {
        get
        {
            for (var v = Window.FocusManager?.GetFocusedElement() as Visual; v is not null; v = v.GetVisualParent())
            {
                if (v is StyledElement s && AutomationIds.Get(s) is { Length: > 0 } id)
                {
                    return id;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// I-205: the wall time the test has left before its own timeout; every harness wait is bounded by it and by nothing
    /// else, so a loaded machine slows a wait down instead of failing it.
    /// </summary>
    public TestDeadline Deadline => _deadline;

    /// <summary>IDE-ARCH-015 step 9 (recovery, start-up content), started after the window is shown as in production.</summary>
    public Task Startup { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Starts the shell and runs step 9 until it completes or shows a dialog (a start-up message box that is not
    /// scripted through <see cref="HarnessOptions.MessageAnswers"/>; drive it, then await <see cref="Startup"/>).
    /// </summary>
    public static async Task<HeadlessHarness> StartAsync(Action<HarnessOptions>? configure = null)
    {
        var options = new HarnessOptions();
        configure?.Invoke(options);
        var h = new HeadlessHarness(options);
        h.Startup = StartupSequence.RunAsync(h.Services);
        // I-195: real disk work (settings, session, project, toolchain search), not driven by the manual clock.
        while (!h.Startup.IsCompleted && !h.Window.OwnedWindows.Any(w => w.IsVisible))
        {
            if (h._deadline.IsExpired)
            {
                throw new HarnessAssertionException(h._deadline.Expired("the start-up sequence").Message);
            }

            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        if (h.Startup.IsCompleted)
        {
            await h.Startup; // its exception, if any
        }

        // D-088: the component metadata load the start-up sequence started (unless the test holds it).
        while (h._metadataGate is null && h.Services.Metadata is { Loading.IsCompleted: false })
        {
            if (h._deadline.IsExpired)
            {
                throw new HarnessAssertionException(h._deadline.Expired("the component metadata load").Message);
            }

            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        Dispatcher.UIThread.RunJobs();
        return h;
    }

    /// <exception cref="InvalidOperationException">No such element; the message lists similar ids.</exception>
    public T Find<T>(string automationId) where T : Control
    {
        if (TryFind<T>(automationId) is { } found)
        {
            return found;
        }

        var head = automationId.Split('.')[0];
        var similar = AllWithId().Select(c => AutomationIds.Get(c)!)
            .Where(id => id.StartsWith(head, StringComparison.OrdinalIgnoreCase) || automationId.StartsWith(id, StringComparison.OrdinalIgnoreCase))
            .Distinct().Order(StringComparer.Ordinal).Take(30).ToList();
        throw new InvalidOperationException(
            $"No {typeof(T).Name} with AutomationId '{automationId}'. Similar ids: {(similar.Count == 0 ? "(none)" : string.Join(", ", similar))}");
    }

    public T? TryFind<T>(string automationId) where T : Control =>
        AllWithId().OfType<T>().FirstOrDefault(c => AutomationIds.Get(c) == automationId);

    public IReadOnlyList<Control> FindAll(string automationIdPrefix) =>
        [.. AllWithId().Where(c => AutomationIds.Get(c)!.StartsWith(automationIdPrefix, StringComparison.Ordinal))];

    public async Task ExecuteAsync(string commandId, string? argument = null)
    {
        await Services.Commands.ExecuteAsync(commandId, argument);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// One gesture of UI-KEY-001 per call: <c>"Ctrl+Shift+S"</c>, <c>"F8"</c>, <c>"Enter"</c>; a chord
    /// (<c>"Ctrl+K, Ctrl+F"</c>, UI-KEY-006) is pressed as its two keys.
    /// </summary>
    public void Press(string gesture)
    {
        var parsed = CoreGesture.Parse(gesture);
        if (parsed.Second is { } second)
        {
            Press(parsed.First.ToString());
            Press(second.ToString());
            return;
        }

        var (key, modifiers) = KeyMapping.ToAvalonia(parsed);
        var raw = ToRaw(modifiers);
        // Letters and digits carry the character they type, as from a keyboard (access keys match on it).
        var symbol = parsed.Key is [var c] && char.IsLetterOrDigit(c)
            ? (modifiers.HasFlag(KeyModifiers.Shift) ? c.ToString().ToUpperInvariant() : c.ToString().ToLowerInvariant())
            : null;
        Window.KeyPress(key, raw, PhysicalKey.None, symbol);
        Window.KeyRelease(key, raw, PhysicalKey.None, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Text input to the focused element.</summary>
    public void Type(string text)
    {
        Window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    public void Click(string automationId) => ClickAt(automationId, MouseButton.Left, 1);

    public void DoubleClick(string automationId) => ClickAt(automationId, MouseButton.Left, 2);

    public void RightClick(string automationId) => ClickAt(automationId, MouseButton.Right, 1);

    public void MiddleClick(string automationId) => ClickAt(automationId, MouseButton.Middle, 1);

    /// <summary>Moves the pointer to the element's centre (hover).</summary>
    public void Hover(string automationId)
    {
        Window.MouseMove(CenterOf(automationId));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Left-button drag from the element's centre by (<paramref name="dx"/>, <paramref name="dy"/>) DIP.</summary>
    public void Drag(string automationId, double dx, double dy)
    {
        var start = CenterOf(automationId);
        Window.MouseDown(start, MouseButton.Left);
        Window.MouseMove(start + new Point(dx / 2, dy / 2));
        Window.MouseMove(start + new Point(dx, dy));
        Window.MouseUp(start + new Point(dx, dy), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Drags <paramref name="paths"/> from the OS file manager onto the element's centre and drops them (enter, over,
    /// drop), as the OS file manager does.
    /// </summary>
    public async Task DropFilesAsync(string automationId, params string[] paths)
    {
        var data = await DragFilesOverAsync(automationId, paths);
        Window.DragDrop(CenterOf(automationId), RawDragEventType.Drop, data, AllDragEffects, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private const DragDropEffects AllDragEffects = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;

    /// <summary>
    /// Drags <paramref name="paths"/> from the OS file manager over the element's centre without dropping (enter,
    /// over); <see cref="DragLeave"/> takes them away again. Returns the dragged data.
    /// </summary>
    public async Task<IDataTransfer> DragFilesOverAsync(string automationId, params string[] paths)
    {
        var point = CenterOf(automationId);
        var data = new DataTransfer();
        foreach (var path in paths)
        {
            var file = await Window.StorageProvider.TryGetFileFromPathAsync(new Uri(path))
                ?? throw new InvalidOperationException($"No storage item for {path}");
            data.Add(DataTransferItem.CreateFile(file));
        }

        Window.DragDrop(point, RawDragEventType.DragEnter, data, AllDragEffects, RawInputModifiers.None);
        Window.DragDrop(point, RawDragEventType.DragOver, data, AllDragEffects, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        return data;
    }

    /// <summary>The dragged files leave the window (the user drags them away or cancels).</summary>
    public void DragLeave(IDataTransfer data)
    {
        Window.DragDrop(new Point(-1, -1), RawDragEventType.DragLeave, data, AllDragEffects, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Loops { run dispatcher jobs; advance the clock 10 ms; yield } until <paramref name="condition"/> holds (IDE-TEST-013).
    /// I-205: bounded only by the test's own timeout (<see cref="Deadline"/>), not by a fixed wall-clock time: a
    /// condition that waits for real work (the OS, a background scan, the real toolchain) is slowed down by a loaded
    /// machine, not failed; a condition that never holds fails with <paramref name="description"/> before the timeout.
    /// </summary>
    public async Task WaitForAsync(Func<bool> condition, string description)
    {
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            if (_deadline.IsExpired)
            {
                var expired = _deadline.Expired(description).Message;
                _timedOutWait ??= expired;
                throw new HarnessAssertionException(expired);
            }

            Clock.Advance(WaitStep);
            await Task.Yield();
        }
    }

    /// <summary>Waits until the dialog <c>Dialog.&lt;dialogName&gt;</c> is open over the main window (IDE-TEST-016).</summary>
    public async Task<DialogDriver> WaitForDialogAsync(string dialogName)
    {
        var id = AutomationIds.Dialog(dialogName);
        DialogWindow? Open() => Owned(Window).OfType<DialogWindow>().LastOrDefault(w => w.IsVisible && !w.Model.IsClosed && AutomationIds.Get(w) == id);
        await WaitForAsync(() => Open() is not null, $"dialog {dialogName} open");
        return new DialogDriver(Open()!, dialogName);
    }

    /// <summary>The test expects an Error log line; dispose then does not fail on it.</summary>
    public void ExpectLoggedError() => _errorExpected = true;

    /// <summary>The test raises unhandled dispatcher exceptions on purpose (IDE-ARCH-091); dispose then does not fail on them.</summary>
    public void ExpectUnhandledException() => _unhandledExpected = true;

    /// <summary>Writes a file under <see cref="TempDir"/> as another program would, and raises the watcher event.</summary>
    public void WriteFileExternally(string relativePath, string content)
    {
        var path = Path.Combine(TempDir, relativePath);
        var existed = File.Exists(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (!_realWatchers)
        {
            _watchers.Raise(new FileChange(existed ? FileChangeKind.Changed : FileChangeKind.Created, path));
        }

        Dispatcher.UIThread.RunJobs();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var failures = new List<string>();
        try
        {
            // Phase 6: a debug session the test left is ended; its events are applied before the window goes.
            if (Services.Debug is { IsIdle: false } debug)
            {
                var shutdown = debug.ShutdownAsync();
                while (!shutdown.IsCompleted && !_deadline.IsExpired)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Yield();
                }

                if (!shutdown.IsCompleted)
                    failures.Add(_deadline.Expired("the debug session's end").Message);
            }

            Dispatcher.UIThread.RunJobs();
            failures.AddRange(BindingErrorSink.Instance.Since(_bindingErrorStart).Select(e => "Binding error: " + e));
            if (!_unhandledExpected)
            {
                failures.AddRange(_unhandled.Select(e => "Unhandled dispatcher exception: " + e));
            }

            failures.AddRange(Owned(Window).Select(w => $"Dialog still open: {w.Title}"));
            failures.AddRange(Messages.Unanswered.Select(r => $"Unanswered message box: {r.MessageId}"));
            if (!_errorExpected)
            {
                failures.AddRange(_log.Lines.Where(l => l.StartsWith("ERROR ", StringComparison.Ordinal)).Select(l => "Logged error: " + l));
            }

            // Let pending saves finish before the user directory is removed.
            await Services.Settings.FlushAsync();
            await Services.Keymap.FlushAsync();
            await Services.Recovery.WhenIdle(); // a recovery copy on its way (the idle copy, IDE-P1-126)
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= OnUnhandledException;
            Services.ProjectManager.Dispose(); // I-339: a missing-file check in flight is dropped before its folders are deleted
            foreach (var dialog in Owned(Window).Reverse().ToList()) // the innermost first
            {
                dialog.Close(); // a dialog window closes through its Cancel (UI-DLG-004)
            }

            Window.Close();
            Dispatcher.UIThread.RunJobs();
            DebugSessions.Dispose();
            DeleteRoot();
        }

        if (failures.Count > 0)
        {
            // I-258: this exception replaces the one of the test body while that propagates through "await using"; the
            // wait that failed there is the cause, what dispose found is what it left behind.
            if (_timedOutWait is not null)
            {
                failures.Insert(0, _timedOutWait);
            }

            throw new HarnessAssertionException(string.Join(Environment.NewLine, failures));
        }
    }

    private static RawInputModifiers ToRaw(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Control) ? RawInputModifiers.Control : RawInputModifiers.None)
        | (modifiers.HasFlag(KeyModifiers.Shift) ? RawInputModifiers.Shift : RawInputModifiers.None)
        | (modifiers.HasFlag(KeyModifiers.Alt) ? RawInputModifiers.Alt : RawInputModifiers.None);

    private IEnumerable<Control> AllWithId() =>
        AllWindows
            .SelectMany(w => new Visual[] { w }.Concat(w.GetVisualDescendants()))
            .SelectMany(v => v is Avalonia.Controls.Primitives.Popup { IsOpen: true, Child: { } child } // the content lives in its own root
                ? new Visual[] { v, child }.Concat(child.GetVisualDescendants())
                : [v])
            .OfType<Control>()
            .Where(c => !string.IsNullOrEmpty(AutomationIds.Get(c)));

    private void ClickAt(string automationId, MouseButton button, int count)
    {
        var point = CenterOf(automationId);
        for (var i = 0; i < count; i++)
        {
            Window.MouseDown(point, button);
            Window.MouseUp(point, button);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private Point CenterOf(string automationId)
    {
        var control = Find<Control>(automationId);
        return control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException($"'{automationId}' is not in the main window.");
    }

    private void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _unhandled.Add(e.Exception.ToString());
        e.Handled = true;
    }

    private void WriteInitialFiles(HarnessOptions options)
    {
        foreach (var (relative, content) in options.InitialFiles)
        {
            var path = Path.Combine(TempDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }

    /// <summary>The test metadata files (not the <c>broken</c> cases) into <paramref name="folder"/>.</summary>
    private static void CopyTestMetadata(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "metadata"), "*.json"))
        {
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        }
    }

    private void WriteSettings(HarnessOptions options, string metadataFolder)
    {
        var roaming = Path.Combine(UserDir, "roaming");
        Directory.CreateDirectory(roaming);
        var values = new Dictionary<string, object> { ["formatVersion"] = 1 };
        foreach (var (key, value) in options.Settings)
        {
            values[key] = value;
        }

        // The design folders stay inside the root (IDE-TEST-003): without a setting, DSG-TP-001 walks up from the IDE
        // directory. The data-access folder is absent; without test metadata the framework index is absent where the
        // harness would have put it.
        // Its parent is the data-access library directory (IDE-SET-050), a library source root (IDE-LIB-001): a folder
        // of its own, so that the files of the test are not library source.
        values.TryAdd("toolchain.dataDesignDir", Path.Combine(Root, "no-data", "design"));
        if (!options.ComponentMetadata)
            values.TryAdd("toolchain.frameworkDesignDir", metadataFolder);

        File.WriteAllText(Path.Combine(roaming, "settings.json"), JsonSerializer.Serialize(values));
        foreach (var (relative, content) in options.UserFiles)
        {
            var path = Path.Combine(UserDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }
    }

    private void DeleteRoot()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Left for the OS temp cleanup; never hides the test's own result.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
