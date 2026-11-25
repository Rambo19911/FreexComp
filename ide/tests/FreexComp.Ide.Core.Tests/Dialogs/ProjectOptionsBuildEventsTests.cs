using System.Text.Json;
using FreexComp.Build;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Dialogs;

/// <summary>
/// The Project Options page <c>Build Events</c> (phase8 build-events.md §3, card F09-007): the working copy of the command
/// lists, per configuration, and what reaches the project file. No command is run here.
/// </summary>
[Trait("Phase", "8")]
public sealed class ProjectOptionsBuildEventsTests
{
    private const string Plain = """, "events": { "preBuild": [ "echo pre" ], "postBuild": [ "echo post" ] }""";

    /// <summary>Debug and Staging have no commands of their own, Release has, Demo inherits Release, Deep inherits Demo.</summary>
    private const string Chain = """
        , "configurations": {
            "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
            "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Release",
                         "events": { "postBuild": [ "sign \"$(OutputPath)\"" ], "postBuildWhen": "always", "x-keep": true } },
            "Demo": { "inherits": "Release" },
            "Deep": { "inherits": "Demo" } },
          "events": { "preBuild": [ "echo pre" ], "postBuild": [ "echo post" ] }
        """;

    private static readonly string NL = Environment.NewLine;

    private static async Task<(BuildTestBed Bed, ProjectOptionsViewModel Options)> OpenAsync(string extra, Action<TestCompositionOptions>? configure = null)
    {
        var bed = new BuildTestBed(configure);
        await bed.OpenHelloAsync(extra: extra);
        return (bed, Page(NewConfigurationTests.Options(bed)));
    }

    private static ProjectOptionsViewModel Page(ProjectOptionsViewModel options)
    {
        options.SelectedPage = options.Pages.Single(p => p.Id == "BuildEvents");
        return options;
    }

    /// <summary>What the configuration bar does when the user picks a configuration (null = All configurations).</summary>
    private static void Choose(ProjectOptionsViewModel options, string? configuration)
    {
        if (configuration is not null)
            options.SelectedConfiguration = configuration;
        options.SearchPathsAllConfigurations = configuration is null;
    }

    private static JsonElement Saved(BuildTestBed bed) => JsonDocument.Parse(bed.Text(BuildTestBed.ProjectPath)).RootElement;

    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The element as compact JSON, members in file order.</summary>
    private static string Json(JsonElement element) => JsonSerializer.Serialize(element, Relaxed);

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-EVT-010")]
    [Trait("Req", "IDE-EVT-012")]
    [Trait("Req", "IDE-EVT-013")]
    [Trait("Req", "IDE-EVT-019")]
    public Task EditsRoundTripThroughTheProjectFile() => TestUiThread.RunAsync(async () =>
    {
        var (bed, options) = await OpenAsync(Plain);

        // The page is editable and shows the stored commands, one per line.
        Assert.True(options.SelectedPage.PerConfiguration);
        Assert.Equal(("echo pre", "echo post", false, false), (options.PreBuild, options.PostBuild, options.PreBuildReadOnly, options.PostBuildReadOnly));
        Assert.Equal(PostBuildWhen.Success, options.PostBuildWhen);
        Assert.False(options.Apply.IsEnabled);

        // One line is one command; empty and blank lines are dropped; nothing is trimmed.
        var typed = "tools\\gen.exe  --out src\\Gen.pas " + NL + NL + "   \t" + NL + "  echo second" + NL;
        options.PreBuild = typed;
        Assert.Equal(typed, options.PreBuild); // the text stays as typed while the dialog is open
        Assert.True(options.Apply.IsEnabled);
        await options.Apply.PressAsync();
        Assert.Equal("""{"preBuild":["tools\\gen.exe  --out src\\Gen.pas ","  echo second"],"postBuild":["echo post"]}""", Json(Saved(bed).GetProperty("events")));
        Assert.Contains("\"events\": {\n    \"preBuild\": [\n      \"tools\\\\gen.exe  --out src\\\\Gen.pas \",\n      \"  echo second\"\n    ],\n    \"postBuild\": [\n      \"echo post\"\n    ]\n  }",
            bed.Text(BuildTestBed.ProjectPath).ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.False(options.Apply.IsEnabled);
        Assert.Equal("tools\\gen.exe  --out src\\Gen.pas " + NL + "  echo second", options.PreBuild); // what was stored

        // postBuildWhen: "always" is written; chosen back, the project is built with "success" again.
        options.PostBuildWhen = PostBuildWhen.Always;
        options.PostBuild = "";
        await options.Apply.PressAsync();
        Assert.Equal("""{"preBuild":["tools\\gen.exe  --out src\\Gen.pas ","  echo second"],"postBuild":[],"postBuildWhen":"always"}""",
            Json(Saved(bed).GetProperty("events")));
        options.PostBuildWhen = PostBuildWhen.Success;
        await options.Ok.PressAsync();
        Assert.Equal(PostBuildWhen.Success, bed.Services.Projects.Definition!.Events.PostBuildWhen);
        var reloaded = ProjectSerializer.LoadProject(BuildTestBed.ProjectPath, new Core.Projects.CompilerFileSystemAdapter(bed.Fs)).Value!;
        Assert.Equal(PostBuildWhen.Success, reloaded.Events.PostBuildWhen);
        Assert.NotEqual("always", Saved(bed).GetProperty("events").TryGetProperty("postBuildWhen", out var when) ? when.GetString() : null);

        // A project whose file never had the key: choosing "always" and then "success" again writes no key.
        var (bed2, options2) = await OpenAsync(Plain);
        options2.PostBuildWhen = PostBuildWhen.Always;
        options2.PostBuildWhen = PostBuildWhen.Success;
        options2.PostBuild = "echo post" + NL + "echo more";
        await options2.Ok.PressAsync();
        Assert.Equal("""{"preBuild":["echo pre"],"postBuild":["echo post","echo more"]}""", Json(Saved(bed2).GetProperty("events")));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-EVT-012")]
    [Trait("Req", "IDE-EVT-019")]
    public Task UntouchedPageLeavesTheFileByteIdentical() => TestUiThread.RunAsync(async () =>
    {
        // A blank entry (the editor would drop it) and a member the dialog does not know.
        const string odd = """, "events": { "preBuild": [ "echo a", "", "  ", "echo b" ], "x-own": { "n": 1 }, "postBuild": [ "echo post" ] }""";
        var (reference, _) = await OpenAsync(odd);
        var definition = reference.Services.Projects.Definition!;
        Assert.True(await reference.Services.Projects.ApplyOptionsAsync(definition with { Run = definition.Run with { Arguments = "-q" } }));
        var expected = reference.Text(BuildTestBed.ProjectPath);

        var (bed, options) = await OpenAsync(odd);
        Assert.Equal("echo a" + NL + NL + "  " + NL + "echo b", options.PreBuild); // shown, also the entries the editor would drop
        options.SelectedPage = options.Pages.Single(p => p.Id == "Run");
        options.Run.Arguments = "-q";
        Page(options);
        options.PostBuild = "echo post"; // writing the text the field already shows is not an edit
        await options.Ok.PressAsync();

        Assert.Equal(expected, bed.Text(BuildTestBed.ProjectPath));
        Assert.Equal("""{"preBuild":["echo a","","  ","echo b"],"x-own":{"n":1},"postBuild":["echo post"]}""", Json(Saved(bed).GetProperty("events")));

        // Nothing changed at all: OK writes nothing.
        var (silent, untouched) = await OpenAsync(odd);
        var before = silent.Text(BuildTestBed.ProjectPath);
        _ = untouched.PreBuild;
        await untouched.Ok.PressAsync();
        Assert.Equal(before, silent.Text(BuildTestBed.ProjectPath));

        // Editing one list leaves the other one and the unknown member as they are.
        var (bed3, options3) = await OpenAsync(odd);
        options3.PostBuild = "echo done";
        await options3.Ok.PressAsync();
        Assert.Equal("""{"preBuild":["echo a","","  ","echo b"],"x-own":{"n":1},"postBuild":["echo done"]}""", Json(Saved(bed3).GetProperty("events")));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-EVT-011")]
    [Trait("Req", "IDE-EVT-014")]
    public Task OwnCommandsOfAConfiguration_CheckCopiesUncheckRemoves() => TestUiThread.RunAsync(async () =>
    {
        var (bed, options) = await OpenAsync(Chain);
        var original = bed.Text(BuildTestBed.ProjectPath);

        // IDE-EVT-011: the bar offers All configurations, the initial selection; New… and Delete need a configuration.
        Assert.True(options.ConfigurationBarEnabled);
        Assert.True(options.HasAllConfigurationsChoice);
        Assert.True(options.SearchPathsAllConfigurations);
        Assert.False(options.ConfigurationSelected);
        Assert.False(options.CanDeleteConfiguration);
        Assert.False(options.ShowOwnEvents);
        Assert.Null(options.EventsInheritedText);
        Assert.Equal(("echo pre", "echo post", false), (options.PreBuild, options.PostBuild, options.PreBuildReadOnly));

        // A configuration without its own commands: the commands of the project, read-only, the source named.
        Choose(options, "Debug");
        Assert.True(options.ConfigurationSelected);
        Assert.True(options.ShowOwnEvents);
        Assert.False(options.OwnEvents);
        Assert.Equal(("echo pre", "echo post", PostBuildWhen.Success), (options.PreBuild, options.PostBuild, options.PostBuildWhen));
        Assert.Equal((true, true, false, false), (options.PreBuildReadOnly, options.PostBuildReadOnly, options.PostBuildWhenEnabled, options.CanInsertMacro));
        Assert.Equal("This configuration uses the commands of All configurations.", options.EventsInheritedText);
        options.PreBuild = "del /q *"; // a read-only field takes no text
        options.PostBuildWhen = PostBuildWhen.Always;
        Assert.Equal(("echo pre", PostBuildWhen.Success), (options.PreBuild, options.PostBuildWhen));
        Assert.False(options.Apply.IsEnabled);

        // Its own commands: editable, no inherited line.
        Choose(options, "Release");
        Assert.True(options.OwnEvents);
        Assert.Null(options.EventsInheritedText);
        Assert.Equal(("", "sign \"$(OutputPath)\"", PostBuildWhen.Always), (options.PreBuild, options.PostBuild, options.PostBuildWhen));
        Assert.Equal((false, false, true, true), (options.PreBuildReadOnly, options.PostBuildReadOnly, options.PostBuildWhenEnabled, options.CanInsertMacro));

        // A derived configuration: the whole value of the nearest ancestor that has its own (nothing is merged).
        Choose(options, "Deep");
        Assert.False(options.OwnEvents);
        Assert.Equal(("", "sign \"$(OutputPath)\"", PostBuildWhen.Always), (options.PreBuild, options.PostBuild, options.PostBuildWhen));
        Assert.Equal("This configuration uses the commands of Release.", options.EventsInheritedText);
        var effective = ProjectOptions.GetEffectiveEvents(options.Build(), "Deep");
        Assert.Equal((options.PostBuild, options.PostBuildWhen), (string.Join(NL, effective.PostBuild), effective.PostBuildWhen));

        // The inherited view follows the working copy of its source.
        Choose(options, "Release");
        options.PreBuild = "echo release";
        Choose(options, "Deep");
        Assert.Equal("echo release", options.PreBuild);

        // Checking copies what was shown and makes the fields editable; the build does the same as before until an edit.
        options.OwnEvents = true;
        Assert.True(options.OwnEvents);
        Assert.Null(options.EventsInheritedText);
        Assert.Equal(("echo release", "sign \"$(OutputPath)\"", PostBuildWhen.Always), (options.PreBuild, options.PostBuild, options.PostBuildWhen));
        Assert.Equal((false, false, true, true), (options.PreBuildReadOnly, options.PostBuildReadOnly, options.PostBuildWhenEnabled, options.CanInsertMacro));
        options.PostBuild = "echo deep";
        await options.Apply.PressAsync();
        var saved = Saved(bed).GetProperty("configurations");
        Assert.Equal("""{"inherits":"Demo","events":{"preBuild":["echo release"],"postBuild":["echo deep"],"postBuildWhen":"always"}}""", Json(saved.GetProperty("Deep")));
        Assert.Equal("""{"preBuild":["echo release"],"postBuild":["sign \"$(OutputPath)\""],"postBuildWhen":"always","x-keep":true}""",
            Json(saved.GetProperty("Release").GetProperty("events"))); // the member the dialog does not know is kept
        Assert.False(saved.GetProperty("Demo").TryGetProperty("events", out _));
        Assert.False(saved.GetProperty("Debug").TryGetProperty("events", out _));

        // Unchecking removes the key without a question; the fields show the inherited commands again.
        options.OwnEvents = false;
        Assert.Empty(bed.Dialogs.Requests);
        Assert.Equal(("echo release", "sign \"$(OutputPath)\"", true), (options.PreBuild, options.PostBuild, options.PreBuildReadOnly));
        Assert.Equal("This configuration uses the commands of Release.", options.EventsInheritedText);
        await options.Ok.PressAsync();
        Assert.Equal("""{"inherits":"Demo"}""", Json(Saved(bed).GetProperty("configurations").GetProperty("Deep")));

        // Cancel restores: the working copy is dropped, the file keeps the configuration's commands.
        var (bed2, options2) = await OpenAsync(Chain);
        Choose(options2, "Release");
        options2.OwnEvents = false;
        Assert.Equal(("echo pre", "echo post"), (options2.PreBuild, options2.PostBuild));
        Assert.Equal("This configuration uses the commands of All configurations.", options2.EventsInheritedText);
        Assert.Null(options2.Build().Configurations["Release"].Events);
        await options2.Buttons.Single(b => b.Id == "Cancel").PressAsync();
        Assert.Equal(original, bed2.Text(BuildTestBed.ProjectPath));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-EVT-015")]
    public Task InsertMacroGoesToTheLastFocusedList() => TestUiThread.RunAsync(async () =>
    {
        var (_, options) = await OpenAsync(Chain);

        // One item per macro of the engine, in its order, as written in a command, with its description.
        Assert.Equal(EventMacros.Names, options.EventMacroItems.Select(m => m.Name));
        Assert.Equal(
            [
                ("$(ProjectDir)", "Folder of the project file"), ("$(ProjectName)", "Name of the project"), ("$(Config)", "Configuration being built"),
                ("$(Platform)", "Target platform"), ("$(OutputDir)", "Folder of the built file"), ("$(OutputPath)", "Full path of the built file"),
            ],
            options.EventMacroItems.Select(m => (m.Text, m.Description)));

        // Initially the Before build list; at the caret.
        Assert.Equal(EventsField.PreBuild, options.LastFocusedEventsField);
        Assert.True(options.CanInsertMacro);
        Assert.Equal(5 + "$(ProjectDir)".Length, options.InsertMacro("ProjectDir", 5, 5));
        Assert.Equal("echo $(ProjectDir)pre", options.PreBuild);
        Assert.Equal("echo post", options.PostBuild);
        Assert.True(options.Apply.IsEnabled);

        // The list that had the focus last; the selection is replaced; the caret comes after the inserted text.
        options.LastFocusedEventsField = EventsField.PostBuild;
        Assert.Equal(5 + "$(OutputPath)".Length, options.InsertMacro("OutputPath", 5, 9));
        Assert.Equal("echo $(OutputPath)", options.PostBuild);
        Assert.Equal("echo $(ProjectDir)pre", options.PreBuild);
        Assert.Equal("$(Config)".Length, options.InsertMacro("Config", 0, 0));
        Assert.Equal("$(Config)echo $(OutputPath)", options.PostBuild);
        var caret = options.InsertMacro("Platform", 999, 999); // a position past the end is the end
        Assert.EndsWith("$(Platform)", options.PostBuild, StringComparison.Ordinal);
        Assert.Equal(options.PostBuild.Length, caret);
        Assert.Null(options.PostBuildError);
        Assert.Null(options.InsertMacro("NoSuchMacro", 0, 0)); // not a macro: nothing is inserted
        Assert.StartsWith("$(Config)", options.PostBuild, StringComparison.Ordinal);

        // Disabled while the field is read-only (a configuration without its own commands).
        Choose(options, "Debug");
        Assert.False(options.CanInsertMacro);
        Assert.Null(options.InsertMacro("Config", 0, 0));
        Choose(options, "Release");
        Assert.True(options.CanInsertMacro);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-EVT-016")]
    public Task UnknownMacroBlocksOkAndApply() => TestUiThread.RunAsync(async () =>
    {
        var (bed, options) = await OpenAsync(Chain);
        var before = bed.Text(BuildTestBed.ProjectPath);
        var page = options.SelectedPage;
        Assert.False(page.HasError);

        // Not errors: the escape, text that is not a name, an environment variable, a macro in another letter case.
        options.PreBuild = "echo $$(OutDir) $(Get-Date) $($x) %PATH% $(outputpath) $(1x) $" + NL + "echo $(Config)";
        Assert.Null(options.PreBuildError);
        Assert.True(options.Ok.IsEnabled);
        Assert.True(options.Apply.IsEnabled);

        // The first unknown reference, as typed.
        options.PreBuild = "echo $(Config)" + NL + "copy $(OutDir)x $(TargetPath) $(outdir)";
        Assert.Equal("Unknown macro $(OutDir). Choose one from Insert Macro, or write $$( for the text $(.", options.PreBuildError);
        Assert.Null(options.PostBuildError);
        Assert.True(page.HasError);
        Assert.False(options.IsValid);
        Assert.False(options.Ok.IsEnabled);
        Assert.False(options.Apply.IsEnabled);
        await options.Ok.PressAsync();
        await options.Apply.PressAsync();
        Assert.False(options.IsClosed);
        Assert.Equal(before, bed.Text(BuildTestBed.ProjectPath));

        // The error belongs to its list: it is not shown for another configuration, but the page keeps its dot.
        Choose(options, "Release");
        Assert.Null(options.PreBuildError);
        Assert.True(page.HasError);
        options.PostBuild = "echo $(Nope)";
        Assert.Equal("Unknown macro $(Nope). Choose one from Insert Macro, or write $$( for the text $(.", options.PostBuildError);
        options.PostBuild = "echo ok";
        Assert.Null(options.PostBuildError);
        Assert.True(page.HasError); // the project-level list is still wrong

        Choose(options, null);
        options.PreBuild = "echo $(Config)";
        Assert.Null(options.PreBuildError);
        Assert.False(page.HasError);
        Assert.True(options.Ok.IsEnabled);
        Assert.True(options.Apply.IsEnabled);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-EVT-017")]
    public Task ListWithControlCharacterIsReadOnlyAndPreserved() => TestUiThread.RunAsync(async () =>
    {
        // A line break inside one command, a control character, U+007F; a tab alone is no control character (CMP-1259).
        const string odd = """, "events": { "preBuild": [ "echo a\necho b", "x\u0001y\u007Fz", "t\tu" ], "postBuild": [ "copy\ta b" ] }""";
        var (bed, options) = await OpenAsync(odd);

        Assert.True(options.PreBuildReadOnly);
        Assert.Equal("echo a↵echo b" + NL + "x·y·z" + NL + "t→u", options.PreBuild);
        Assert.Equal("A command in this list contains a control character. The list cannot be edited here and the build will not run it.", options.PreBuildWarning);
        Assert.Null(options.PreBuildError);
        Assert.False(options.CanInsertMacro); // the Before build list had the focus last
        options.PreBuild = "echo replaced";
        Assert.StartsWith("echo a↵", options.PreBuild, StringComparison.Ordinal);
        Assert.False(options.SelectedPage.HasError); // a warning, not an error: the other pages can still be applied

        // The other list stays editable (a tab is typed text there).
        Assert.False(options.PostBuildReadOnly);
        Assert.Null(options.PostBuildWarning);
        Assert.Equal("copy\ta b", options.PostBuild);
        options.LastFocusedEventsField = EventsField.PostBuild;
        Assert.True(options.CanInsertMacro);
        options.PostBuild = "copy\ta b" + NL + "echo done";
        await options.Ok.PressAsync();

        var events = Saved(bed).GetProperty("events");
        Assert.Equal(["echo a\necho b", "x\u0001y\u007Fz", "t\tu"], events.GetProperty("preBuild").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["copy\ta b", "echo done"], events.GetProperty("postBuild").EnumerateArray().Select(e => e.GetString()));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-EVT-018")]
    [Trait("Req", "IDE-EVT-021")]
    [Trait("Req", "IDE-EVT-023")]
    public Task ApplyingNeverTouchesTheTrustList() => TestUiThread.RunAsync(async () =>
    {
        // Every access to the trust list fails while the dialog is in use.
        RecordingFileSystem? recording = null;
        var guarded = false;
        var (bed, _) = await OpenAsync(Plain, o => o.FileSystem = recording = new RecordingFileSystem(o.FileSystem)
        {
            FailWhen = call => guarded && call.Contains("trust.json", StringComparison.OrdinalIgnoreCase),
        });
        var trustFile = Path.Combine(bed.Services.UserPaths.RoamingDirectory, "trust.json");
        bed.Services.Trust.Store.TrustProject(BuildTestBed.ProjectPath, ProjectTrust.CommandsHash(bed.Services.Projects.Definition!));
        var trusted = bed.Fs.ReadAllBytes(trustFile);
        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build)); // trusted: no question
        Assert.Empty(bed.Dialogs.Requests);
        var settingsBefore = bed.Fs.EnumerateFiles(bed.Services.UserPaths.RoamingDirectory, "*").Order().ToList();

        guarded = true;
        var calls = recording!.Calls.Count;
        var options = Page(NewConfigurationTests.Options(bed));
        options.PostBuild = "echo post" + NL + "del /q out";
        options.PostBuildWhen = PostBuildWhen.Always;
        await options.Apply.PressAsync();
        Choose(options, "Debug");
        options.OwnEvents = true;
        options.PreBuild = "echo debug";
        await options.Ok.PressAsync();
        Assert.True(options.IsClosed);
        guarded = false;

        Assert.DoesNotContain(recording.Calls.Skip(calls), c => c.Contains("trust.json", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(trusted, bed.Fs.ReadAllBytes(trustFile));
        Assert.Equal(["echo post", "del /q out"], bed.Services.Projects.Definition!.Events.PostBuild);
        // IDE-EVT-023: nothing about events is stored outside the project file.
        Assert.Equal(settingsBefore, bed.Fs.EnumerateFiles(bed.Services.UserPaths.RoamingDirectory, "*").Order().ToList());

        // The next build asks about the commands the user typed a minute ago, with the note (IDE-TRUST-005); the IDE never
        // sends Events = Run.
        TrustProjectViewModel? prompt = null;
        bed.Dialogs.Script(TrustProjectViewModel.DialogName, vm =>
        {
            prompt = (TrustProjectViewModel)vm;
            return prompt.Buttons.Single(b => b.Id == "Cancel").PressAsync();
        });
        Assert.Null(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.NotNull(prompt);
        Assert.Equal(TrustState.Changed, Assert.Single(prompt.Projects).State);
        Assert.Contains(prompt.Rows, r => r is { Kind: TrustRowKind.Command, ListName: "Before build (Debug)", Text: "echo debug" });
        Assert.DoesNotContain(bed.Engine.Requests, r => r.Events == EventsPolicy.Run);
        Assert.Equal(trusted, bed.Fs.ReadAllBytes(trustFile));
    });
}
