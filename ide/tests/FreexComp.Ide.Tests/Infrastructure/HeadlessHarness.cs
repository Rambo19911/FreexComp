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
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    /// <summary>
    /// I-195: the start-up sequence does real disk work (settings, session, project, toolchain search) and is not driven by the
    /// manual clock, so under parallel load it can take several seconds of wall time. It gets the most a non-toolchain test
    /// has (30 s, IDE-TEST-006) minus a margin, so a real hang still fails here with this message before the test timeout.
    /// </summary>
    private static readonly TimeSpan StartupLimit = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan WaitStep = TimeSpan.FromMilliseconds(10);

    private readonly int _bindingErrorStart;
    private readonly RecordingLog _log;
    private readonly ManualFileWatcherFactory _watchers;
    private readonly bool _realWatchers;
    private readonly List<string> _unhandled = [];
    private MainWindow? _window;
    private bool _errorExpected;
    private bool _unhandledExpected;
    private bool _disposed;

    private HeadlessHarness(HarnessOptions options)
    {
        _bindingErrorStart = BindingErrorSink.Instance.Count;
        Root = options.Root ?? Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        TempDir = Path.Combine(Root, "work");
        UserDir = Path.Combine(Root, "user");
        Directory.CreateDirectory(TempDir);
        WriteInitialFiles(options);
        WriteSettings(options);

        FileSystem = new RecordingFileSystem(new PhysicalFileSystem());
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
            o.Processes = options.RealBuild ? new FreexComp.Ide.Platform.Windows.WindowsProcessLauncher(Clock) : Processes;
            if (options.RealBuild)
                o.ToolchainEnvironment = new Core.Build.ToolchainEnvironment(null, AppContext.BaseDirectory, []);
            o.BuildEngine = options.RealBuild ? new FreexComp.Build.BuildEngine(new Core.Projects.CompilerFileSystemAdapter(FileSystem)) : Build;
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

    /// <summary>The main window and every window it owns (dialogs).</summary>
    public IReadOnlyList<Window> AllWindows => [Window, .. Window.OwnedWindows];

    public IdeServices Services { get; }

    public ManualClock Clock { get; }

    public MessageScript Messages { get; }

    /// <summary>Started with <see cref="HarnessOptions.RealBuild"/>: <see cref="Build"/> and <see cref="Processes"/> are unused.</summary>
    public bool RealBuild { get; }

    /// <summary>The scripted build engine (IDE-P3-031).</summary>
    public FakeBuildEngine Build { get; } = new();

    /// <summary>The scripted process launcher (IDE-P3-094).</summary>
    public FakeProcessLauncher Processes { get; } = new();

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
        var watch = Stopwatch.StartNew();
        while (!h.Startup.IsCompleted && !h.Window.OwnedWindows.Any(w => w.IsVisible))
        {
            if (watch.Elapsed > StartupLimit)
            {
                throw new HarnessAssertionException($"Timed out waiting for the start-up sequence ({watch.Elapsed.TotalSeconds:0.0} s)");
            }

            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        if (h.Startup.IsCompleted)
        {
            await h.Startup; // its exception, if any
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

    /// <summary>One gesture of UI-KEY-001 per call: <c>"Ctrl+Shift+S"</c>, <c>"F8"</c>, <c>"Enter"</c>.</summary>
    public void Press(string gesture)
    {
        var parsed = CoreGesture.Parse(gesture);
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
    /// Loops { run dispatcher jobs; advance the clock 10 ms; yield } until <paramref name="condition"/> holds, for at
    /// most 5 s of wall time (IDE-TEST-013), or <paramref name="limit"/> when the condition waits for the OS (a real
    /// file watcher).
    /// </summary>
    public async Task WaitForAsync(Func<bool> condition, string description, TimeSpan? limit = null)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            if (watch.Elapsed > (limit ?? WaitLimit))
            {
                throw new HarnessAssertionException($"Timed out waiting for: {description}");
            }

            Clock.Advance(WaitStep);
            await Task.Yield();
        }
    }

    /// <summary>Waits until the dialog <c>Dialog.&lt;dialogName&gt;</c> is open over the main window (IDE-TEST-016).</summary>
    public async Task<DialogDriver> WaitForDialogAsync(string dialogName)
    {
        var id = AutomationIds.Dialog(dialogName);
        DialogWindow? Open() => Window.OwnedWindows.OfType<DialogWindow>().LastOrDefault(w => w.IsVisible && !w.Model.IsClosed && AutomationIds.Get(w) == id);
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
            Dispatcher.UIThread.RunJobs();
            failures.AddRange(BindingErrorSink.Instance.Since(_bindingErrorStart).Select(e => "Binding error: " + e));
            if (!_unhandledExpected)
            {
                failures.AddRange(_unhandled.Select(e => "Unhandled dispatcher exception: " + e));
            }

            failures.AddRange(Window.OwnedWindows.Select(w => $"Dialog still open: {w.Title}"));
            failures.AddRange(Messages.Unanswered.Select(r => $"Unanswered message box: {r.MessageId}"));
            if (!_errorExpected)
            {
                failures.AddRange(_log.Lines.Where(l => l.StartsWith("ERROR ", StringComparison.Ordinal)).Select(l => "Logged error: " + l));
            }

            // Let pending saves finish before the user directory is removed.
            await Services.Settings.FlushAsync();
            await Services.Keymap.FlushAsync();
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= OnUnhandledException;
            foreach (var dialog in Window.OwnedWindows.ToList())
            {
                dialog.Close(); // a dialog window closes through its Cancel (UI-DLG-004)
            }

            Window.Close();
            Dispatcher.UIThread.RunJobs();
            DeleteRoot();
        }

        if (failures.Count > 0)
        {
            throw new HarnessAssertionException(string.Join(Environment.NewLine, failures));
        }
    }

    private static RawInputModifiers ToRaw(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Control) ? RawInputModifiers.Control : RawInputModifiers.None)
        | (modifiers.HasFlag(KeyModifiers.Shift) ? RawInputModifiers.Shift : RawInputModifiers.None)
        | (modifiers.HasFlag(KeyModifiers.Alt) ? RawInputModifiers.Alt : RawInputModifiers.None);

    private IEnumerable<Control> AllWithId() =>
        new[] { Window }.Concat(Window.OwnedWindows)
            .SelectMany(w => new Visual[] { w }.Concat(w.GetVisualDescendants()))
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

    private void WriteSettings(HarnessOptions options)
    {
        var roaming = Path.Combine(UserDir, "roaming");
        Directory.CreateDirectory(roaming);
        var values = new Dictionary<string, object> { ["formatVersion"] = 1 };
        foreach (var (key, value) in options.Settings)
        {
            values[key] = value;
        }

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
