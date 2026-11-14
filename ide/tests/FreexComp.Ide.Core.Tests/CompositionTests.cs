using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests;

[Trait("Phase", "1")]
public sealed class CompositionTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-010")]
    [Trait("Req", "IDE-ARCH-013")]
    [Trait("Req", "IDE-ARCH-040")]
    public void ProductionGraphBuildsWithFakes()
    {
        var services = TestComposition.Build();

        Assert.IsType<InMemoryFileSystem>(services.FileSystem);
        Assert.IsType<ManualClock>(services.Clock);
        Assert.IsType<ImmediateDispatcher>(services.Dispatcher);
        Assert.IsType<FakeClipboard>(services.Clipboard);
        Assert.IsType<FakeShellService>(services.Shell);
        Assert.IsType<ManualFileWatcherFactory>(services.FileWatchers);
        Assert.IsType<FakeDialogService>(services.Dialogs);
        Assert.IsType<RecordingLog>(services.Log);
        Assert.IsType<SettingsService>(services.Settings);
        Assert.IsType<KeymapService>(services.Keymap);
        Assert.IsType<CommandService>(services.Commands);
        Assert.NotNull(services.Status);
        Assert.IsType<FakeThemeHost>(services.ThemeHost);
        Assert.IsType<ThemeService>(services.Theme);

        // Later-phase commands are stubbed by the composition itself; since I-091 Build fails when any handler is missing.
        services.Commands.VerifyAllHandled();
    }

    [Fact(Timeout = 30000)]
    public void LaterPhaseCommandsAreStubbedByTheComposition()
    {
        var services = TestComposition.Build();

        // Since I-161 every P1 command has its real handler, so nothing is missing; P3 commands are stubs (disabled)
        // while a P1 command is not (file.showInExplorer runs for an existing folder).
        services.Commands.VerifyAllHandled();
        Assert.True(services.Commands.IsHandled("project.build"));
        Assert.False(services.Commands.CanExecute("project.build"));
        services.FileSystem.CreateDirectory(@"C:\shown");
        Assert.True(services.Commands.CanExecute("file.showInExplorer", @"C:\shown"));
    }

    /// <summary>I-091: the composition (production and tests alike) checks at start-up that every catalog command has a handler.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-040")]
    public void StartupVerifiesEveryCommandHasAHandler()
    {
        var log = new RecordingLog();

        var services = TestComposition.Build(o => o.Log = log);

        Assert.Contains($"INFO commands: All {services.Commands.Definitions.Count} catalog commands have a handler.", log.Lines);
        Assert.DoesNotContain(log.Lines, l => l.StartsWith("ERROR ", StringComparison.Ordinal));
    }

    /// <summary>I-091: a missing handler is logged as an error and fails the start-up (and so every test that composes).</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-040")]
    public void StartupFailsWhenACommandHasNoHandler()
    {
        var log = new RecordingLog();
        var clock = new ManualClock();
        var keymap = new KeymapService(UserPaths.Create(@"C:\u", _ => null, _ => throw new InvalidOperationException()), new InMemoryFileSystem(), clock, log);
        var commands = new CommandService(keymap, new ImmediateDispatcher(), clock, log, new FakeDialogService(), new RecordingStatusService(), new FocusTracker());
        commands.RegisterLaterPhaseStubs(ServiceGraph.ImplementedPhase);
        foreach (var definition in commands.Definitions.Where(d => !commands.IsHandled(d.Id) && d.Id != "file.save"))
        {
            commands.RegisterHandler(definition.Id, NotYetImplemented.Instance);
        }

        var error = Assert.Throws<InvalidOperationException>(() => ServiceGraph.VerifyCommands(commands, log));

        Assert.Equal("Commands without a handler: file.save", error.Message);
        Assert.Equal(["ERROR commands: Start-up check failed: Commands without a handler: file.save"], log.Lines);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-003")]
    [Trait("Req", "IDE-P1-011")]
    public void UserDirectoryIsAFreshFolderPerComposition()
    {
        var first = TestComposition.Build();
        var second = TestComposition.Build();
        var realRoaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FreexComp");
        var realLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreexComp");

        Assert.NotEqual(first.UserPaths.RoamingDirectory, second.UserPaths.RoamingDirectory);
        Assert.Equal(first.Options.UserDir, Path.GetDirectoryName(first.UserPaths.RoamingDirectory));
        Assert.StartsWith(Path.GetTempPath(), first.UserPaths.RoamingDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(realRoaming, first.UserPaths.RoamingDirectory, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(realLocal, first.UserPaths.LocalDirectory, StringComparer.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-013")]
    public void OptionsSubstituteFakesAndSettingsAreLoaded()
    {
        var fs = new InMemoryFileSystem();
        var clock = new ManualClock();
        var userDir = @"C:\users\test";
        fs.CreateDirectory(@"C:\users\test\roaming");
        fs.WriteAllBytes(@"C:\users\test\roaming\settings.json", "{\"formatVersion\":1,\"editor.tabWidth\":8}"u8.ToArray());

        var services = TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.Clock = clock;
            o.UserDir = userDir;
        });

        Assert.Same(fs, services.FileSystem);
        Assert.Same(clock, services.Clock);
        Assert.Equal(8, services.Settings.Get(SettingsCatalog.EditorTabWidth));
    }

    [Theory]
    [Trait("Req", "IDE-P1-011")]
    [InlineData(new[] { "--user-dir", @"C:\u" }, @"C:\u", new string[0])]
    [InlineData(new[] { "a.pproj", "--user-dir", @"C:\u", "b.pas" }, @"C:\u", new[] { "a.pproj", "b.pas" })]
    [InlineData(new[] { "a.pas" }, null, new[] { "a.pas" })]
    [InlineData(new[] { "--user-dir" }, null, new[] { "--user-dir" })]
    public void StartupOptionsParseUserDir(string[] args, string? userDir, string[] rest)
    {
        var options = StartupOptions.Parse(args);

        Assert.Equal(userDir, options.UserDir);
        Assert.Equal(rest, options.Arguments);
    }

    private sealed class NotYetImplemented : ICommandHandler
    {
        public static readonly NotYetImplemented Instance = new();

        public bool CanExecute(string? argument) => false;

        public Task ExecuteAsync(string? argument) => Task.CompletedTask;
    }
}
