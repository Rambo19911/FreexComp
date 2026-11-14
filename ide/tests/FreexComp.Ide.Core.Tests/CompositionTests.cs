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

        // Later-phase commands are stubbed by the composition itself. Phase 1 commands whose tasks are not done yet
        // get explicit "not yet implemented" stubs here only, so that this test keeps checking the rest of the graph.
        foreach (var definition in services.Commands.Definitions.Where(d => d.Phase <= ServiceGraph.ImplementedPhase && !services.Commands.IsHandled(d.Id)))
        {
            services.Commands.RegisterHandler(definition.Id, NotYetImplemented.Instance);
        }

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
