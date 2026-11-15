using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

[Trait("Phase", "3")]
public sealed class IdeStateMachineTests
{
    /// <summary>The arrows of the phase3-build-run.md §2 diagram.</summary>
    private static readonly HashSet<(IdeState, IdeState)> Drawn =
    [
        (IdeState.Idle, IdeState.Building),
        (IdeState.Building, IdeState.Idle),
        (IdeState.Building, IdeState.Running),
        (IdeState.Building, IdeState.DebugRunning),
        (IdeState.Running, IdeState.Idle),
        (IdeState.Idle, IdeState.DebugRunning),
        (IdeState.DebugRunning, IdeState.Paused),
        (IdeState.Paused, IdeState.DebugRunning),
        (IdeState.DebugRunning, IdeState.Idle),
        (IdeState.Paused, IdeState.Idle),
    ];

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-001")]
    [Trait("Req", "IDE-P3-002")]
    public void OnlyLegalTransitions()
    {
        foreach (var from in Enum.GetValues<IdeState>())
        {
            foreach (var to in Enum.GetValues<IdeState>())
            {
                var log = new RecordingLog();
                var machine = new IdeStateMachine(log);
                DriveTo(machine, from);
                var changes = 0;
                machine.Changed += (_, _) => changes++;

                var legal = machine.TryTransition(Info(to));

                var expected = Drawn.Contains((from, to));
                Assert.True(expected == legal, $"{from} -> {to}: expected {(expected ? "legal" : "illegal")}");
                Assert.Equal(expected ? to : from, machine.Current.State);
                Assert.Equal(expected ? 1 : 0, changes);
                Assert.Equal(expected ? 0 : 1, log.Lines.Count(l => l.StartsWith("ERROR state:", StringComparison.Ordinal)));
                Assert.Equal(expected, IdeStateMachine.IsLegal(from, to));
            }
        }
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-050")]
    public void CancelRequestStaysInBuildingAndForceIdleAlwaysWorks()
    {
        var machine = new IdeStateMachine(new RecordingLog());
        Assert.False(machine.RequestCancel()); // nothing to cancel while Idle
        Assert.True(machine.TryTransition(Info(IdeState.Building) with { BuildKind = BuildKind.Rebuild }));

        Assert.True(machine.RequestCancel());
        Assert.Equal(IdeState.Building, machine.Current.State);
        Assert.True(machine.Current.CancelRequested);
        Assert.Equal(BuildKind.Rebuild, machine.Current.BuildKind);

        machine.ForceIdle();
        Assert.Equal(IdeStateInfo.Idle, machine.Current);
    }

    /// <summary>IDE-P3-003's table, read row by row; commands outside it are free in every state.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-003")]
    [Trait("Req", "IDE-P3-004")]
    [Trait("Req", "IDE-PM-032")]
    public void CommandAvailabilityPerState()
    {
        string[] idleOnly =
        [
            "project.build", "project.rebuild", "project.clean", "project.checkSyntax", "project.options",
            "project.setConfiguration", "run.parameters", "run.startWithoutDebugging", "file.newProject",
            "project.addNewUnit", "project.addNewForm", "project.addExistingFile", "project.removeFile", "project.renameFile",
            "project.deleteFile",
        ];
        Expect(idleOnly, IdeState.Idle);
        Expect(["project.cancelBuild"], IdeState.Building);
        Expect(["run.start"], IdeState.Idle, IdeState.Paused);
        Expect(["run.stop", "run.restart"], IdeState.Running, IdeState.DebugRunning, IdeState.Paused);
        Expect(["file.openProject", "file.openRecentProject", "file.closeProject"], IdeState.Idle, IdeState.Running, IdeState.DebugRunning, IdeState.Paused);
        Expect(["file.exit", "file.save", "file.saveAll", "file.open", "file.close", "edit.undo", "search.find", "search.goToLine"],
            Enum.GetValues<IdeState>());

        // Through the command service: a handler that can always run is still blocked by the state, and its shortcut
        // then says so in the status bar (UI-CMD-012).
        var status = new RecordingStatusService();
        var machine = new IdeStateMachine(new RecordingLog());
        var keymap = new KeymapService(UserPaths.Create(@"C:\u", _ => null, _ => throw new InvalidOperationException()), new InMemoryFileSystem(),
            new ManualClock(), new RecordingLog());
        var commands = new CommandService(keymap, new ImmediateDispatcher(), new ManualClock(), new RecordingLog(), new FakeDialogService(), status,
            new FocusTracker()) { IdeState = machine };
        var build = new AlwaysHandler();
        commands.RegisterHandler("project.build", build);
        commands.RegisterHandler("file.save", new AlwaysHandler());
        var changed = 0;
        commands.Get("project.build").CanExecuteChanged += (_, _) => changed++;

        Assert.True(commands.CanExecute("project.build"));
        Assert.True(machine.TryTransition(Info(IdeState.Building)));
        Assert.Equal(1, changed); // state changes re-query the commands
        Assert.False(commands.CanExecute("project.build"));
        Assert.True(commands.CanExecute("file.save"));

        Assert.True(commands.HandleKey(KeyGesture.Parse("Ctrl+Shift+B"), CommandScope.Editor, null));
        Assert.Equal(0, build.Executed);
        var unavailable = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Status_CommandUnavailable,
            commands.Get("project.build").Title);
        Assert.Equal([unavailable], status.Messages.Select(m => m.Text));

        Assert.True(machine.TryTransition(Info(IdeState.Idle)));
        Assert.True(commands.HandleKey(KeyGesture.Parse("Ctrl+Shift+B"), CommandScope.Editor, null));
        Assert.Equal(1, build.Executed);
    }

    /// <summary>UI-WIN-033 and states-and-messages.md §9: the title and the status bar's state field follow the state.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-005")]
    public void TitleAndStatusStateFollowTheState()
    {
        var services = TestComposition.Build();
        var window = new MainWindowViewModel(services);
        var titles = new List<string>();
        window.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Title))
                titles.Add(window.Title);
        };

        Assert.Equal(Strings.Msg_State_Idle, window.StatusBar.StateText);
        Assert.Equal(StatusBarState.Idle, window.StatusBar.StateKind);

        Assert.True(services.State.TryTransition(Info(IdeState.Building) with { BuildKind = BuildKind.Clean }));
        Assert.Contains($"({Strings.Title_State_Building})", window.Title, StringComparison.Ordinal);
        Assert.Equal([window.Title], titles);
        Assert.Equal(StatusBarState.Building, window.StatusBar.StateKind);
        Assert.StartsWith(Strings.Msg_State_Cleaning.Split('{')[0], window.StatusBar.StateText, StringComparison.Ordinal);

        services.State.RequestCancel();
        Assert.Equal(Strings.Msg_State_Canceling, window.StatusBar.StateText);

        Assert.True(services.State.TryTransition(new IdeStateInfo(IdeState.Running, null, false, @"C:\p\Hello.exe", 42)));
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_State_Running, "Hello.exe"), window.StatusBar.StateText);
        Assert.Equal(StatusBarState.Running, window.StatusBar.StateKind);
        Assert.Contains($"({Strings.Title_State_Running})", window.Title, StringComparison.Ordinal);

        Assert.True(services.State.TryTransition(IdeStateInfo.Idle));
        Assert.Equal(Strings.Msg_State_Idle, window.StatusBar.StateText);
        Assert.DoesNotContain("(", window.Title, StringComparison.Ordinal);
    }

    /// <summary>IDE-WEL-005: Welcome rows are disabled while the IDE is not Idle.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-WEL-005")]
    public void WelcomeRowsFollowIdle()
    {
        var services = TestComposition.Build();
        var welcome = new WelcomeDocumentViewModel(services.RecentItems, services.Commands, services.Settings, services.FileSystem,
            services.Clock, services.State);
        var changes = new List<string?>();
        welcome.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.True(welcome.RowsEnabled);
        Assert.True(services.State.TryTransition(Info(IdeState.Building)));
        Assert.False(welcome.RowsEnabled);
        Assert.Contains(nameof(WelcomeDocumentViewModel.RowsEnabled), changes);
        Assert.True(services.State.TryTransition(Info(IdeState.Running)));
        Assert.False(welcome.RowsEnabled);
        Assert.True(services.State.TryTransition(IdeStateInfo.Idle));
        Assert.True(welcome.RowsEnabled);
    }

    private static void Expect(string[] commandIds, params IdeState[] availableIn)
    {
        foreach (var id in commandIds)
        {
            Assert.Contains(CommandCatalog.All, d => d.Id == id);
            foreach (var state in Enum.GetValues<IdeState>())
                Assert.True(availableIn.Contains(state) == IdeCommandAvailability.IsAvailable(id, state), $"{id} in {state}");
        }
    }

    private static IdeStateInfo Info(IdeState state) => new(state, state == IdeState.Building ? BuildKind.Build : null, false, null, null);

    private static void DriveTo(IdeStateMachine machine, IdeState state)
    {
        IdeState[] path = state switch
        {
            IdeState.Idle => [],
            IdeState.Building => [IdeState.Building],
            IdeState.Running => [IdeState.Building, IdeState.Running],
            IdeState.DebugRunning => [IdeState.DebugRunning],
            _ => [IdeState.DebugRunning, IdeState.Paused],
        };
        foreach (var step in path)
            Assert.True(machine.TryTransition(Info(step)));
    }

    private sealed class AlwaysHandler : ICommandHandler
    {
        public int Executed { get; private set; }

        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument)
        {
            Executed++;
            return Task.CompletedTask;
        }
    }
}
