using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Input;
using FreexComp.Ide.Platform;
using FreexComp.Ide.TestSupport;
using CoreGesture = FreexComp.Ide.Core.Commands.KeyGesture;

namespace FreexComp.Ide.Tests.Infrastructure;

/// <summary>Options of <see cref="HeadlessHarness.StartAsync"/> (testing.md §2.1).</summary>
public sealed class HarnessOptions
{
    /// <summary>Command-line arguments; <c>--user-dir</c> is added by the harness.</summary>
    public List<string> CommandLineArgs { get; } = [];

    /// <summary>Relative path (under <see cref="HeadlessHarness.TempDir"/>) → UTF-8 content, written before start.</summary>
    public Dictionary<string, string> InitialFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Setting key → value, written to <c>settings.json</c> before start. Preset: <c>run.consoleMode = output</c>.</summary>
    public Dictionary<string, object> Settings { get; } = new(StringComparer.Ordinal) { ["run.consoleMode"] = "output" };

    public Size WindowSize { get; set; } = new(1440, 900); // IDE-TEST-018

    /// <summary>Extra tool windows, registered before the main window is created (IDE-ARCH-100).</summary>
    public List<Core.ToolWindows.ToolWindowDefinition> ToolWindows { get; } = [];

    public double Scale { get; set; } = 1.0;
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
    private static readonly TimeSpan WaitStep = TimeSpan.FromMilliseconds(10);

    private readonly int _bindingErrorStart;
    private readonly RecordingLog _log;
    private readonly ManualFileWatcherFactory _watchers;
    private readonly List<string> _unhandled = [];
    private bool _errorExpected;
    private bool _disposed;

    private HeadlessHarness(HarnessOptions options)
    {
        _bindingErrorStart = BindingErrorSink.Instance.Count;
        Root = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        TempDir = Path.Combine(Root, "work");
        UserDir = Path.Combine(Root, "user");
        Directory.CreateDirectory(TempDir);
        WriteInitialFiles(options);
        WriteSettings(options);

        FileSystem = new RecordingFileSystem(new PhysicalFileSystem());
        Clock = new ManualClock(DateTimeOffset.Now);
        Messages = new MessageScript();
        _log = new RecordingLog();
        _watchers = new ManualFileWatcherFactory();

        Dispatcher.UIThread.UnhandledException += OnUnhandledException;
        Services = TestComposition.Build(o =>
        {
            o.FileSystem = FileSystem;
            o.Clock = Clock;
            o.Dispatcher = new AvaloniaUiDispatcher();
            o.Dialogs = Messages;
            o.Log = _log;
            o.FileWatchers = _watchers;
            o.UserDir = UserDir;
            o.ThemeHost = new AvaloniaThemeHost(Application.Current!);
            o.Arguments.AddRange(options.CommandLineArgs);
            o.ToolWindows.AddRange(options.ToolWindows);
        });

        Window = CompositionRoot.CreateMainWindow(Services);
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

    public static Task<HeadlessHarness> StartAsync(Action<HarnessOptions>? configure = null)
    {
        var options = new HarnessOptions();
        configure?.Invoke(options);
        return Task.FromResult(new HeadlessHarness(options));
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

    /// <summary>
    /// Loops { run dispatcher jobs; advance the clock 10 ms; yield } until <paramref name="condition"/> holds, for at
    /// most 5 s of wall time (IDE-TEST-013).
    /// </summary>
    public async Task WaitForAsync(Func<bool> condition, string description)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            if (watch.Elapsed > WaitLimit)
            {
                throw new HarnessAssertionException($"Timed out waiting for: {description}");
            }

            Clock.Advance(WaitStep);
            await Task.Yield();
        }
    }

    /// <summary>The test expects an Error log line; dispose then does not fail on it.</summary>
    public void ExpectLoggedError() => _errorExpected = true;

    /// <summary>Writes a file under <see cref="TempDir"/> as another program would, and raises the watcher event.</summary>
    public void WriteFileExternally(string relativePath, string content)
    {
        var path = Path.Combine(TempDir, relativePath);
        var existed = File.Exists(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        _watchers.Raise(new FileChange(existed ? FileChangeKind.Changed : FileChangeKind.Created, path));
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
            failures.AddRange(_unhandled.Select(e => "Unhandled dispatcher exception: " + e));
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
        var control = Find<Control>(automationId);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException($"'{automationId}' is not in the main window.");
        for (var i = 0; i < count; i++)
        {
            Window.MouseDown(point, button);
            Window.MouseUp(point, button);
        }

        Dispatcher.UIThread.RunJobs();
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
