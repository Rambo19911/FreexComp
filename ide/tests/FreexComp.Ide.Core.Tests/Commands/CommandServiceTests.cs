using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Commands;

[Trait("Phase", "1")]
public sealed class CommandServiceTests
{
    private readonly ManualClock _clock = new();
    private readonly RecordingLog _log = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly RecordingStatusService _status = new();
    private readonly QueuedDispatcher _dispatcher = new();

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-040")]
    [Trait("Req", "UI-CMD-004")]
    public void EveryCommandHasHandler()
    {
        var service = Create();

        var missing = Assert.Throws<InvalidOperationException>(service.VerifyAllHandled);
        Assert.Contains("file.save", missing.Message, StringComparison.Ordinal);

        service.RegisterHandler("file.save", new TestHandler());
        Assert.Throws<InvalidOperationException>(() => service.RegisterHandler("file.save", new TestHandler()));
        Assert.Throws<InvalidOperationException>(() => service.RegisterHandler("file.nope", new TestHandler()));

        service.RegisterLaterPhaseStubs(implementedPhase: 0); // stubs for everything not yet handled
        service.VerifyAllHandled();

        Assert.True(service.CanExecute("file.save"));
        Assert.False(service.CanExecute("run.start")); // stub: CanExecute = false
        Assert.Equal(CommandCatalog.All.Count, service.Definitions.Count);
    }

    [Fact(Timeout = 30000)]
    public void LaterPhaseStubsLeaveCurrentPhaseUnhandled()
    {
        var service = Create();

        service.RegisterLaterPhaseStubs(implementedPhase: 1);

        var error = Assert.Throws<InvalidOperationException>(service.VerifyAllHandled);
        Assert.Contains("file.save", error.Message, StringComparison.Ordinal);       // P1: needs a real handler
        Assert.DoesNotContain("project.build", error.Message, StringComparison.Ordinal); // P3: stubbed
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-046")]
    public async Task ReentrantExecutionIsBlocked()
    {
        var service = Create();
        var save = new TestHandler { Pending = new TaskCompletionSource() };
        var stop = new TestHandler { Pending = new TaskCompletionSource() };
        service.RegisterHandler("file.save", save);
        service.RegisterHandler("run.stop", stop);

        var first = service.ExecuteAsync("file.save");
        Assert.False(service.CanExecute("file.save"));
        Assert.False(service.Get("file.save").CanExecute(null));
        await service.ExecuteAsync("file.save"); // no-op while the first one runs
        Assert.Equal(1, save.Calls);

        var stopFirst = service.ExecuteAsync("run.stop");
        Assert.True(service.CanExecute("run.stop")); // exempt
        _ = service.ExecuteAsync("run.stop");
        Assert.Equal(2, stop.Calls);

        save.Pending.SetResult();
        await first;
        Assert.True(service.CanExecute("file.save"));
        stop.Pending.SetResult();
        await stopFirst;
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-045")]
    [Trait("Req", "UI-MSG-005")]
    public async Task HandlerExceptionShowsInternalError()
    {
        var service = Create();
        var handler = new TestHandler { Throw = () => new InvalidOperationException("boom") };
        service.RegisterHandler("file.save", handler);
        _dialogs.Answer("Msg.App.InternalError", "Continue");
        var executed = new List<CommandExecutedEventArgs>();
        service.Executed += (_, e) => executed.Add(e);

        await service.ExecuteAsync("file.save"); // does not throw

        var request = Assert.Single(_dialogs.Requests);
        Assert.Equal("Msg.App.InternalError", request.MessageId);
        Assert.Contains("boom", request.Details, StringComparison.Ordinal);
        Assert.Contains(_log.Lines, l => l.StartsWith("ERROR commands: file.save failed", StringComparison.Ordinal));
        Assert.IsType<InvalidOperationException>(Assert.Single(executed).Exception);
        Assert.True(service.CanExecute("file.save"));

        // Identical error within 10 s: logged only.
        _clock.Advance(TimeSpan.FromSeconds(9));
        await service.ExecuteAsync("file.save");
        Assert.Single(_dialogs.Requests);
        Assert.Equal(2, _log.Lines.Count(l => l.StartsWith("ERROR", StringComparison.Ordinal)));

        _clock.Advance(TimeSpan.FromSeconds(11));
        await service.ExecuteAsync("file.save");
        Assert.Equal(2, _dialogs.Requests.Count);

        // Cancellation is silent.
        handler.Throw = () => new OperationCanceledException();
        await service.ExecuteAsync("file.save");
        Assert.Equal(2, _dialogs.Requests.Count);
        Assert.Equal(3, _log.Lines.Count(l => l.StartsWith("ERROR", StringComparison.Ordinal)));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-CMD-012")]
    [Trait("Req", "UI-KEY-007")]
    public void UnavailableShortcutShowsStatus()
    {
        var service = Create();
        var build = new TestHandler { Enabled = false, BlockedByIdeState = true };
        var save = new TestHandler { Enabled = false };
        var indent = new TestHandler { Enabled = false };
        service.RegisterHandler("project.build", build);
        service.RegisterHandler("file.save", save);
        service.RegisterHandler("edit.indent", indent);

        // Disabled because of the IDE state: consumed, status for 3 s.
        Assert.True(service.HandleKey(KeyGesture.Parse("Ctrl+Shift+B"), CommandScope.Editor, null));
        Assert.Equal([("Build is not available now", TimeSpan.FromSeconds(3))], _status.Messages);
        Assert.Equal(0, build.Calls);

        // Disabled for another reason: consumed silently.
        Assert.True(service.HandleKey(KeyGesture.Parse("Ctrl+S"), CommandScope.Editor, null));
        Assert.Single(_status.Messages);

        // Tab, Enter, Escape, Delete, Insert, plain arrows fall through when disabled.
        Assert.False(service.HandleKey(KeyGesture.Parse("Tab"), CommandScope.Editor, null));

        // Unbound gesture: not handled.
        Assert.False(service.HandleKey(KeyGesture.Parse("Ctrl+B"), CommandScope.Editor, null));

        // Enabled: executed.
        save.Enabled = true;
        Assert.True(service.HandleKey(KeyGesture.Parse("Ctrl+S"), CommandScope.Global, null));
        Assert.Equal(1, save.Calls);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-044")]
    public void InvalidateCanExecuteIsCoalesced()
    {
        var service = Create();
        service.RegisterHandler("file.save", new TestHandler());
        var command = service.Get("file.save");
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        service.InvalidateCanExecute();
        service.InvalidateCanExecute();
        service.InvalidateCanExecute();
        Assert.Equal(1, _dispatcher.Pending);
        Assert.Equal(0, raised);

        _dispatcher.RunAll();
        Assert.Equal(1, raised);
        Assert.Same(command, service.Get("file.save"));
        Assert.Equal("Save", command.Title);
        Assert.Equal("Ctrl+S", command.GestureText);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-066")]
    [Trait("Req", "UI-KEY-023")]
    public void KeymapChangesApplyToKeysAndGestureText()
    {
        var keymap = CreateKeymap();
        var service = new CommandService(keymap, _dispatcher, _clock, _log, _dialogs, _status, new FocusTracker());
        var save = new TestHandler();
        service.RegisterHandler("file.save", save);
        var command = service.Get("file.save");

        keymap.SetGestures("file.save", [KeyGesture.Parse("Ctrl+Shift+F12")]);

        Assert.Equal("Ctrl+Shift+F12", command.GestureText);
        Assert.False(service.HandleKey(KeyGesture.Parse("Ctrl+S"), CommandScope.Global, null));
        Assert.True(service.HandleKey(KeyGesture.Parse("Ctrl+Shift+F12"), CommandScope.Global, null));
        Assert.Equal(1, save.Calls);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-CMD-010")]
    [Trait("Req", "UI-CMD-011")]
    [Trait("Req", "IDE-ARCH-042")]
    public async Task FocusRoutingFallsBackToActiveDocument()
    {
        var services = TestComposition.Build();
        var focus = services.FocusTracker;
        var document = new RecordingTarget("edit.undo", "edit.redo", "edit.copy");
        var messages = new RecordingTarget("edit.copy"); // a tool window that copies but has no undo

        Assert.False(services.Commands.CanExecute("edit.copy")); // (5) nothing focused

        focus.Update(new FocusState(CommandScope.Editor, null, document, IsTextInput: false));
        await services.Commands.ExecuteAsync("edit.undo");
        Assert.Equal(["edit.undo"], document.Executed); // (2) the focused editor

        focus.Update(new FocusState(CommandScope.ToolList, "Messages", messages, IsTextInput: false));
        await services.Commands.ExecuteAsync("edit.copy");
        await services.Commands.ExecuteAsync("edit.undo");
        Assert.Equal(["edit.copy"], messages.Executed); // (4) the tool window's own target
        Assert.Equal(["edit.undo", "edit.undo"], document.Executed); // UI-CMD-011: undo goes to the active document
        Assert.Same(document, focus.ActiveDocumentTarget);

        focus.Update(new FocusState(CommandScope.ToolList, "Output", null, IsTextInput: false));
        Assert.False(services.Commands.CanExecute("edit.copy")); // no target, no fallback for copy
        Assert.True(services.Commands.CanExecute("edit.redo"));

        focus.Update(new FocusState(CommandScope.Global, null, null, IsTextInput: true));
        Assert.False(services.Commands.CanExecute("edit.undo")); // (1) the text input's native behavior
        Assert.False(services.Commands.HandleKey(KeyGesture.Parse("Ctrl+Z")));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-043")]
    [Trait("Req", "UI-KEY-004")]
    public void LocalKeysOfTheFocusedControlAreNotRouted()
    {
        var services = TestComposition.Build();
        // tools.openLogFolder: an always-enabled Global command with a visible effect (the fake shell records OpenFolder),
        // given Alt+Left (search.navigateBack's gesture, I-060). Every P1 command has a handler since I-161.
        var shell = (FakeShellService)services.Shell;
        services.Keymap.SetGestures("search.navigateBack", []);
        services.Keymap.SetGestures("tools.openLogFolder", [KeyGesture.Parse("Alt+Left")]);

        services.FocusTracker.SetLocalKeys([KeyGesture.Parse("Alt+Left")]);
        Assert.False(services.Commands.HandleKey(KeyGesture.Parse("Alt+Left")));

        services.FocusTracker.SetLocalKeys([]);
        Assert.True(services.Commands.HandleKey(KeyGesture.Parse("Alt+Left")));
        Assert.Equal(["OpenFolder " + services.UserPaths.LogDirectory], shell.Calls);
    }

    private CommandService Create() =>
        new(CreateKeymap(), _dispatcher, _clock, _log, _dialogs, _status, new FocusTracker());

    private KeymapService CreateKeymap() =>
        new(Core.Settings.UserPaths.Create(@"C:\u", _ => null, _ => throw new InvalidOperationException()), new InMemoryFileSystem(), _clock, _log);

    private sealed class TestHandler : ICommandHandler
    {
        public bool Enabled { get; set; } = true;

        public bool BlockedByIdeState { get; set; }

        public TaskCompletionSource? Pending { get; set; }

        public Func<Exception>? Throw { get; set; }

        public int Calls { get; private set; }

        public bool CanExecute(string? argument) => Enabled;

        public bool IsBlockedByIdeState(string? argument) => BlockedByIdeState;

        public Task ExecuteAsync(string? argument)
        {
            Calls++;
            if (Throw is not null)
            {
                return Task.FromException(Throw());
            }

            return Pending?.Task ?? Task.CompletedTask;
        }
    }

    private sealed class RecordingTarget(params string[] handled) : ICommandTarget
    {
        public List<string> Executed { get; } = [];

        public bool CanExecute(string commandId, string? argument) => handled.Contains(commandId);

        public Task<bool> ExecuteAsync(string commandId, string? argument)
        {
            Executed.Add(commandId);
            return Task.FromResult(true);
        }
    }
}
