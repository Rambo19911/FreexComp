using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Infrastructure;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class HarnessTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-011")]
    [Trait("Req", "IDE-TEST-012")]
    public async Task ShowsMainWindowAndFindsByAutomationId()
    {
        await using var h = await HeadlessHarness.StartAsync();

        Assert.True(h.Window.IsVisible);
        Assert.Same(h.Window, h.Find<Window>(AutomationIds.MainWindow));
        Assert.NotNull(h.Find<Control>(AutomationIds.DocumentArea));
        Assert.NotNull(h.Find<Control>(AutomationIds.StatusBar));
        Assert.Equal(1440, h.Window.Width); // IDE-TEST-018
        Assert.Equal(900, h.Window.Height);

        Assert.Null(h.TryFind<Control>("StatusBar.Nope"));
        var error = Assert.Throws<InvalidOperationException>(() => h.Find<Control>("StatusBar.Nope"));
        Assert.Contains("StatusBar", error.Message, StringComparison.Ordinal); // similar ids are listed
        Assert.Contains(h.FindAll("Status"), c => AutomationIds.Get(c) == AutomationIds.StatusBar);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-003")]
    public async Task NeverTouchesRealUserDirectories()
    {
        var realRoaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FreexComp");
        var realLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreexComp");
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string root;
        IReadOnlyList<string> calls;

        await using (var h = await HeadlessHarness.StartAsync())
        {
            root = h.Root;
            h.Services.Settings.Set(SettingsCatalog.EditorTabWidth, 7); // forces a settings write
            await h.WaitForAsync(() => File.Exists(Path.Combine(h.Services.UserPaths.RoamingDirectory, "settings.json")), "settings saved");
            calls = h.FileSystem.Calls;
        }

        Assert.NotEmpty(calls);
        foreach (var call in calls)
        {
            Assert.Contains(root, call, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(realRoaming, call, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(realLocal, call, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(appData, call, StringComparison.OrdinalIgnoreCase);
        }

        Assert.False(Directory.Exists(root)); // removed at dispose
    }

    [AvaloniaFact(Timeout = 30000)]
    public async Task OptionsSeedFilesSettingsAndArguments()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["src/a.pas"] = "unit a;";
            o.Settings["editor.tabWidth"] = 5;
            o.CommandLineArgs.Add("src/a.pas"); // relative to TempDir (I-085: start-up opens it)
        });

        Assert.Equal("unit a;", File.ReadAllText(Path.Combine(h.TempDir, "src", "a.pas")));
        Assert.Equal(5, h.Services.Settings.Get(SettingsCatalog.EditorTabWidth));
        Assert.Equal("output", h.Services.Settings.Get(SettingsCatalog.RunConsoleMode)); // harness preset
        Assert.Equal(["src/a.pas"], h.Services.Options.Arguments);
        Assert.Equal(Path.Combine(h.TempDir, "src", "a.pas"), h.Services.Documents.ActiveDocument?.FilePath);
        Assert.Equal(h.UserDir, h.Services.Options.UserDir);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-013")]
    public async Task WaitForAdvancesTheManualClock()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var fired = false;
        h.Clock.Schedule(TimeSpan.FromMilliseconds(200), () => fired = true);

        await h.WaitForAsync(() => fired, "timer fired");

        Assert.True(fired);
    }

    /// <summary>I-205: the harness's waits are bounded by the headless test's own timeout, registered before it ran.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-013")]
    public async Task WaitsAreBoundedByTheTestsOwnTimeout()
    {
        var registered = CurrentTest.Deadline;
        await using var h = await HeadlessHarness.StartAsync();

        Assert.Same(registered, h.Deadline);
        Assert.Equal(TimeSpan.FromSeconds(30), h.Deadline.Timeout);
        Assert.Equal(TestContext.Current.CancellationToken, h.Deadline.Token);
        Assert.InRange(h.Deadline.Remaining, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30) - TestDeadline.Margin);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-017")]
    public async Task PressTypeAndClickReachTheWindow()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var keys = new List<(Key, KeyModifiers)>();
        var text = "";
        var clicks = 0;
        h.Window.AddHandler(InputElement.KeyDownEvent, (_, e) => keys.Add((e.Key, e.KeyModifiers)), handledEventsToo: true);
        h.Window.AddHandler(InputElement.TextInputEvent, (_, e) => text += e.Text, handledEventsToo: true);
        h.Find<Control>(AutomationIds.DocumentArea).AddHandler(InputElement.PointerPressedEvent, (_, _) => clicks++, handledEventsToo: true);

        h.Press("Ctrl+Shift+S");
        h.Press("F8");
        h.Press("Enter");
        h.Type("ab");
        h.Click(AutomationIds.DocumentArea);

        Assert.Equal([(Key.S, KeyModifiers.Control | KeyModifiers.Shift), (Key.F8, KeyModifiers.None), (Key.Enter, KeyModifiers.None)], keys);
        Assert.Equal("ab", text);
        Assert.Equal(1, clicks);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-014")]
    [Trait("Req", "IDE-ARCH-055")]
    public async Task BindingErrorFailsDispose()
    {
        var h = await HeadlessHarness.StartAsync();
        var block = new TextBlock { DataContext = new object() };
        block.Bind(TextBlock.TextProperty, new ReflectionBinding("NoSuchProperty"));
        h.Find<Panel>(AutomationIds.StatusBar).Children.Add(block);
        Dispatcher.UIThread.RunJobs();

        var error = await Assert.ThrowsAsync<HarnessAssertionException>(() => h.DisposeAsync().AsTask());
        Assert.Contains("NoSuchProperty", error.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-014")]
    public async Task UnhandledDispatcherExceptionFailsDispose()
    {
        var h = await HeadlessHarness.StartAsync();
        Dispatcher.UIThread.Post(() => throw new InvalidOperationException("boom"));
        Dispatcher.UIThread.RunJobs();

        var error = await Assert.ThrowsAsync<HarnessAssertionException>(() => h.DisposeAsync().AsTask());
        Assert.Contains("boom", error.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-014")]
    public async Task LoggedErrorFailsDisposeUnlessExpected()
    {
        var h = await HeadlessHarness.StartAsync();
        h.Services.Log.Error("test", "first failure");
        var error = await Assert.ThrowsAsync<HarnessAssertionException>(() => h.DisposeAsync().AsTask());
        Assert.Contains("first failure", error.Message, StringComparison.Ordinal);

        await using var expected = await HeadlessHarness.StartAsync();
        expected.Services.Log.Error("test", "expected failure");
        expected.ExpectLoggedError();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-016")]
    public async Task MessagesAreAnsweredByScriptAndUnansweredOnesFailDispose()
    {
        await using (var h = await HeadlessHarness.StartAsync())
        {
            h.Messages.Answer("Msg.App.InternalError", "Continue");
            Assert.Equal("Continue", await h.Services.Dialogs.ShowMessageAsync(new MessageRequest("Msg.App.InternalError", [])));
        }

        // Without a script the real message box opens; left undriven, it fails dispose (IDE-TEST-016).
        var unanswered = await HeadlessHarness.StartAsync();
        var pending = unanswered.Services.Dialogs.ShowMessageAsync(new MessageRequest("Msg.File.NotFound", ["a.pas"]));
        await unanswered.WaitForDialogAsync("Message");
        Assert.False(pending.IsCompleted);
        var error = await Assert.ThrowsAsync<HarnessAssertionException>(() => unanswered.DisposeAsync().AsTask());
        Assert.Contains("Msg.File.NotFound", error.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact(Timeout = 30000)]
    public async Task WriteFileExternallyChangesDiskAndRaisesWatcherEvent()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["a.pas"] = "old");
        using var watcher = h.Services.FileWatchers.Watch(h.TempDir);
        var changes = new List<string>();
        watcher.Changed += (_, c) => changes.Add($"{c.Kind} {Path.GetFileName(c.Path)}");

        h.WriteFileExternally("a.pas", "new");
        h.WriteFileExternally("b.pas", "added");

        Assert.Equal("new", File.ReadAllText(Path.Combine(h.TempDir, "a.pas")));
        Assert.Equal(["Changed a.pas", "Created b.pas"], changes);
    }

    [AvaloniaFact(Timeout = 30000)]
    public async Task FocusedAutomationIdIsNearestAncestorWithId()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var box = new TextBox(); // no id of its own
        h.Find<Panel>(AutomationIds.StatusBar).Children.Add(box);
        Dispatcher.UIThread.RunJobs();
        box.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AutomationIds.StatusBar, h.FocusedAutomationId);
    }

    /// <summary>
    /// I-183: a key or click whose action closes the dialog later (an awaited create whose continuation arrives from the
    /// thread pool) can close it after the driver checked that it is still open: the headless release first runs the
    /// pending work, then needs the window. The driver then has nothing left to deliver and does not fail.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-016")]
    public async Task DialogDriverPressAndClickSurviveACloseDuringTheRelease()
    {
        await using var h = await HeadlessHarness.StartAsync();

        var running = h.Services.Commands.ExecuteAsync("file.newFile");
        var dlg = await h.WaitForDialogAsync("NewFile");
        var first = dlg.Window;
        dlg.BeforeRelease = () => Dispatcher.UIThread.Post(first.Close); // the continuation lands just before the release
        dlg.Press("F12");
        Assert.False(first.IsVisible);
        await running;

        running = h.Services.Commands.ExecuteAsync("file.newFile");
        dlg = await h.WaitForDialogAsync("NewFile");
        var second = dlg.Window;
        dlg.BeforeRelease = () => Dispatcher.UIThread.Post(second.Close);
        dlg.Click("Name");
        Assert.False(second.IsVisible);
        await running;
    }
}
