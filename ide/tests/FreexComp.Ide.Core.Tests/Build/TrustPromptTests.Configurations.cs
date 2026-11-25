using FreexComp.Build;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

/// <summary>The command lists of configurations in the trust prompt (phase8 build-events.md IDE-EVT-007, 008).</summary>
public sealed partial class TrustPromptTests
{
    private const string ConfigurationsWithEvents =
        """
        , "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
          "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Release",
                       "events": { "preBuild": [ "tools\\sign.cmd  \"$(OutputPath)\"" ], "postBuild": [ "echo r1", "echo r2 " ] } },
          "Empty": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Empty", "events": { } },
          "Nightly": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Nightly", "inherits": "Release",
                       "events": { "postBuild": [ "echo n" ] } } }
        """;

    private static async Task<BuildTestBed> StartWithConfigurationEventsAsync()
    {
        var bed = new BuildTestBed();
        bed.Engine.DefaultOutputPath = Exe;
        bed.Fs.CreateDirectory(BuildTestBed.Dir + @"\bin\Debug");
        await bed.OpenHelloAsync(extra: ConfigurationsWithEvents + Events);
        return bed;
    }

    /// <summary>IDE-EVT-007: project-level lists first, then the lists of each configuration that has its own; index j across lists.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-EVT-007")]
    [Trait("Req", "UI-DLG-298")]
    public Task ConfigurationCommandsAreListedWithTheirConfiguration() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartWithConfigurationEventsAsync();
        var shown = Script(bed, prompt => Press(prompt, "Cancel"));

        // The active configuration is Debug, which has no commands of its own: that plays no part.
        Assert.Null(await bed.Services.Build.BuildAsync(BuildKind.Build));

        var prompt = Assert.Single(shown);
        Assert.Equal(
        [
            new TrustRow(TrustRowKind.Project, 0, -1, "", BuildTestBed.ProjectPath),
            new TrustRow(TrustRowKind.Command, 0, 0, "Before build", @"tools\gen.exe --out  src\Gen.pas "),
            new TrustRow(TrustRowKind.Command, 0, 1, "After build", "copy /y a b"),
            new TrustRow(TrustRowKind.Command, 0, 2, "After build", "echo done"),
            new TrustRow(TrustRowKind.Command, 0, 3, "Before build (Release)", @"tools\sign.cmd  ""$(OutputPath)"""), // verbatim, not expanded
            new TrustRow(TrustRowKind.Command, 0, 4, "After build (Release)", "echo r1"),
            new TrustRow(TrustRowKind.Command, 0, 5, "After build (Release)", "echo r2 "),
            new TrustRow(TrustRowKind.Command, 0, 6, "After build (Nightly)", "echo n"), // "Empty" has no command: no row
        ], prompt.Rows);
        Assert.Equal("After build (Release): echo r1", prompt.Rows[5].AutomationName);
        Assert.Equal("  Before build (Release): " + prompt.Rows[4].Text, prompt.Rows[4].CopyAllLine); // the list names as shown
    });

    /// <summary>IDE-EVT-008: the stored hash is the one of the project file, configuration lists included; a changed list asks again.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-EVT-008")]
    public Task TrustStoresTheHashCoveringConfigurationLists() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartWithConfigurationEventsAsync();
        var shown = Script(bed, prompt => Press(prompt, "TrustProject"));

        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));

        var definition = bed.Services.Projects.Definition!;
        var entry = Assert.Single(bed.Services.Trust.Store.Read().Entries);
        Assert.Equal(new TrustEntry(TrustEntryKind.Project, BuildTestBed.ProjectPath, ProjectTrust.CommandsHash(definition)), entry);
        // Not the hash of the project-level lists alone: the configuration lists are part of it.
        Assert.NotEqual(ProjectTrust.CommandsHash(new Project { Events = definition.Events }), entry.CommandsHash);
        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.Single(shown); // trusted: silent
        Assert.All(bed.Engine.Requests, r => Assert.Equal(EventsPolicy.RequireTrust, r.Events));

        // Editing a command of a configuration (not the active one) asks again, with the note.
        var release = definition.Configurations["Release"];
        Assert.True(await bed.Services.Projects.ApplyOptionsAsync(definition with
        {
            Configurations = definition.Configurations.SetItem("Release",
                release with { Events = release.Events! with { PostBuild = ["echo r1", "del /q c"] } }),
        }));
        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.Equal(2, shown.Count);
        Assert.Equal(TrustState.Changed, Assert.Single(shown[1].Projects).State);
        Assert.Equal(TrustRowKind.Changed, shown[1].Rows[1].Kind);
        Assert.Contains(new TrustRow(TrustRowKind.Command, 0, 5, "After build (Release)", "del /q c"), shown[1].Rows);
        Assert.Equal(ProjectTrust.CommandsHash(bed.Services.Projects.Definition!), Assert.Single(bed.Services.Trust.Store.Read().Entries).CommandsHash);
        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.Equal(2, shown.Count);
    });

    /// <summary>
    /// IDE-EVT-008 ("what is stored is exactly what was shown"): a project file whose commands changed while the question was
    /// open is not trusted with commands nobody saw — the question stays open and shows what the file holds now.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-EVT-008")]
    public Task CommandsChangedWhileThePromptIsOpenAreShownBeforeTheyAreTrusted() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var presses = 0;
        var shown = Script(bed, async prompt =>
        {
            WriteProject(bed, BuildTestBed.ProjectPath, "Hello", Configurations + """, "events": { "postBuild": [ "del /q *" ] }""");
            await Press(prompt, "TrustProject");
            presses++;
            Assert.False(prompt.IsClosed);
            Assert.False(bed.Fs.FileExists(TrustFile(bed)));
            Assert.Equal(
            [
                new TrustRow(TrustRowKind.Project, 0, -1, "", BuildTestBed.ProjectPath),
                new TrustRow(TrustRowKind.Command, 0, 0, "After build", "del /q *"),
            ], prompt.Rows);
            await Press(prompt, "TrustProject");
            Assert.True(prompt.IsClosed);
        });

        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));

        Assert.Single(shown);
        Assert.Equal(1, presses);
        var onDisk = FreexComp.Build.ProjectSerializer.LoadProject(BuildTestBed.ProjectPath, new Core.Projects.CompilerFileSystemAdapter(bed.Fs)).Value!;
        Assert.Equal(ProjectTrust.CommandsHash(onDisk), Assert.Single(bed.Services.Trust.Store.Read().Entries).CommandsHash);
    });
}
