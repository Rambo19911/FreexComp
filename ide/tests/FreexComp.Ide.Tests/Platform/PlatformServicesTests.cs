using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Platform;
using FreexComp.Ide.Text;

namespace FreexComp.Ide.Tests.Platform;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class PlatformServicesTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [AvaloniaFact]
    [Trait("Req", "IDE-ARCH-012")]
    public void DispatcherPostsAndRuns()
    {
        IUiDispatcher dispatcher = new AvaloniaUiDispatcher();
        var order = new List<string>();

        Assert.True(dispatcher.CheckAccess());
        dispatcher.Post(() => order.Add("background"), UiPriority.Background);
        dispatcher.Post(() => order.Add("normal"));
        Assert.Empty(order); // queued, not run inline

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["normal", "background"], order);
    }

    [AvaloniaFact]
    [Trait("Req", "IDE-ARCH-012")]
    [Trait("Req", "IDE-ARCH-080")]
    public async Task DispatcherAcceptsPostsFromOtherThreads()
    {
        IUiDispatcher dispatcher = new AvaloniaUiDispatcher();
        var ran = new TaskCompletionSource<bool>();

        var access = await Task.Run(() =>
        {
            dispatcher.Post(() => ran.SetResult(dispatcher.CheckAccess()));
            return dispatcher.CheckAccess();
        });

        Assert.False(access);
        Assert.True(await ran.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    [Trait("Req", "IDE-ARCH-085")]
    public void ClockScheduleFiresAndDisposeCancels()
    {
        IClock clock = new DispatcherClock();
        var fired = new List<string>();
        var frame = new DispatcherFrame();
        using var safety = DispatcherTimer.RunOnce(() => frame.Continue = false, Timeout);

        var before = DateTimeOffset.Now;
        Assert.InRange(clock.Now, before.AddMinutes(-1), before.AddMinutes(1));

        var cancelled = clock.Schedule(TimeSpan.FromMilliseconds(1), () => fired.Add("cancelled"));
        cancelled.Dispose();
        clock.Schedule(TimeSpan.FromMilliseconds(20), () =>
        {
            fired.Add("due");
            frame.Continue = false;
        });

        Dispatcher.UIThread.PushFrame(frame);

        Assert.Equal(["due"], fired);
    }

    /// <summary>
    /// I-173: Core schedules from the thread pool too (<c>SharingRetry</c> after an awaited settings/keymap save); the
    /// callback still runs on the UI thread, and disposing from the pool cancels.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-085")]
    public async Task ClockAcceptsSchedulesFromOtherThreads()
    {
        IClock clock = new DispatcherClock();
        var fired = new List<(string Name, bool OnUiThread)>();
        var frame = new DispatcherFrame();
        using var safety = DispatcherTimer.RunOnce(() => frame.Continue = false, Timeout);

        await Task.Run(() =>
        {
            clock.Schedule(TimeSpan.FromMilliseconds(1), () => fired.Add(("cancelled", Dispatcher.UIThread.CheckAccess()))).Dispose();
            clock.Schedule(TimeSpan.FromMilliseconds(20), () =>
            {
                fired.Add(("due", Dispatcher.UIThread.CheckAccess()));
                frame.Continue = false;
            });
        }, TestContext.Current.CancellationToken);

        Dispatcher.UIThread.PushFrame(frame);

        Assert.Equal([("due", true)], fired);
    }

    [AvaloniaFact]
    public async Task ClipboardValidatesArgumentsAndToleratesMissingWindow()
    {
        IClipboardService clipboard = new AvaloniaClipboard(() => null);

        await Assert.ThrowsAsync<ArgumentNullException>(() => clipboard.SetTextAsync(null!));
        Assert.Null(await clipboard.GetTextAsync());
        await clipboard.SetTextAsync("x"); // no window yet: ignored
        await clipboard.SetTextAsync("x", new Dictionary<string, string> { [IClipboardService.LineFormat] = "x" });
        Assert.Null(await clipboard.GetFormatAsync(IClipboardService.LineFormat));
    }

    /// <summary>I-170: the line format goes to the window's clipboard with the text; a plain copy replaces it.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-038")]
    public async Task ClipboardCarriesTheLineFormat()
    {
        var window = new Avalonia.Controls.Window();
        window.Show();
        try
        {
            IClipboardService clipboard = new AvaloniaClipboard(() => window);
            await clipboard.SetTextAsync("two\r\n", new Dictionary<string, string> { [IClipboardService.LineFormat] = "two\r\n" });
            Assert.Equal("two\r\n", await clipboard.GetTextAsync());
            Assert.Equal("two\r\n", await clipboard.GetFormatAsync(IClipboardService.LineFormat));
            await clipboard.SetTextAsync("other");
            Assert.Equal("other", await clipboard.GetTextAsync());
            Assert.Null(await clipboard.GetFormatAsync(IClipboardService.LineFormat));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact(Timeout = 30000)]
    public void ShellValidatesArguments()
    {
        IShellService shell = new WindowsShellService();
        var missing = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"), "nothing.pas");

        Assert.Throws<ArgumentException>(() => shell.OpenFolder(""));
        Assert.Throws<ArgumentException>(() => shell.RevealFile(" "));
        Assert.Throws<ArgumentException>(() => shell.MoveToRecycleBin(""));
        Assert.Throws<ArgumentException>(() => shell.MoveToRecycleBin("relative.pas"));
        Assert.False(shell.OpenFolder(Path.GetDirectoryName(missing)!));
        Assert.False(shell.RevealFile(missing));
        Assert.False(shell.MoveToRecycleBin(missing));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-118")]
    public async Task FileWatcherReportsCreatedFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            IFileWatcherFactory factory = new FileSystemWatcherFactory();
            var target = Path.Combine(dir, "a.pas");
            var created = new TaskCompletionSource<FileChange>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var watcher = factory.Watch(dir))
            {
                watcher.Changed += (_, change) =>
                {
                    if (change.Kind == FileChangeKind.Created)
                    {
                        created.TrySetResult(change);
                    }
                };

                Assert.Equal(dir, watcher.Directory);
                File.WriteAllBytes(target, [1]);

                var change = await created.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
                Assert.Equal(target, change.Path, ignoreCase: true);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    [Trait("Req", "IDE-ARCH-010")]
    [Trait("Req", "IDE-TEST-003")]
    public void ProductionPlatformBuildsWithTempUserDir()
    {
        var userDir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        var options = StartupOptions.Parse(["--user-dir", userDir]);
        var window = new Window();
        var platform = CompositionRoot.CreatePlatform(options, () => window);
        try
        {
            var services = CompositionRoot.Build(options, platform);

            Assert.Equal(Path.Combine(userDir, "roaming"), services.UserPaths.RoamingDirectory);
            Assert.IsType<AvaloniaUiDispatcher>(services.Dispatcher);
            Assert.IsType<DispatcherClock>(services.Clock);
            Assert.IsType<AvaloniaClipboard>(services.Clipboard);
            Assert.IsType<WindowsShellService>(services.Shell);
            Assert.IsType<FileSystemWatcherFactory>(services.FileWatchers);
            Assert.IsType<FileLog>(services.Log);
            Assert.IsType<PhysicalFileSystem>(services.FileSystem);
            Assert.IsType<AvaloniaThemeHost>(services.ThemeHost);
            Assert.IsType<AvaloniaEditTextBufferFactory>(services.TextBuffers); // IDE-ARCH-031
            Assert.Equal(SettingsCatalog.EditorTabWidth.Default, services.Settings.Get(SettingsCatalog.EditorTabWidth));
        }
        finally
        {
            (platform.Log as IDisposable)?.Dispose();
            if (Directory.Exists(userDir))
            {
                Directory.Delete(userDir, recursive: true);
            }
        }
    }
}
