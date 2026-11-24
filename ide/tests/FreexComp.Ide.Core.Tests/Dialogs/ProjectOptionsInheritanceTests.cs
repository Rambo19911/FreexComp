using FreexComp.Build;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Dialogs;

/// <summary>
/// Project Options on a derived configuration (phase8-configurations.md §5, I-249): effective values, the inherited and
/// own states, Revert, the defines fields, the shared-folder warning and a file that stays byte-identical.
/// </summary>
[Trait("Phase", "8")]
public sealed class ProjectOptionsInheritanceTests
{
    private static async Task<(BuildTestBed Bed, ProjectOptionsViewModel Options)> OpenAsync(string configuration)
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync(extra: ConfigurationManagerTests.Chain);
        var options = NewConfigurationTests.Options(bed);
        options.SelectedConfiguration = configuration;
        return (bed, options);
    }

    private static string Entry(BuildTestBed bed, string name) =>
        System.Text.Json.JsonSerializer.Serialize(NewConfigurationTests.SavedConfigurations(bed).GetProperty(name));

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-020")]
    [Trait("Req", "IDE-CFG-021")]
    public Task DerivedConfiguration_ShowsEffectiveValuesAndStates() => TestUiThread.RunAsync(async () =>
    {
        var (bed, options) = await OpenAsync("Profiling");
        Assert.Equal("Inherited from {0}", Strings.Msg_ProjectOptions_ValueInherited);
        Assert.Equal("Set in this configuration", Strings.Msg_ProjectOptions_ValueOwn);

        // Profiling: optimization comes from Release (through Release-Demo), debug information is its own.
        Assert.True(options.IsDerived);
        Assert.Equal("(Profiling, inherits Release-Demo)", options.ConfigurationNote);
        Assert.Equal((2, false, "Inherited from Release"), (options.Optimization, options.OptimizationIsOwn, options.OptimizationHelpText));
        Assert.Equal((true, true, "Set in this configuration"), (options.DebugInfo, options.DebugInfoIsOwn, options.DebugInfoHelpText));

        options.SelectedConfiguration = "Release-Demo";
        Assert.Equal("(Release-Demo, inherits Release)", options.ConfigurationNote);
        Assert.Equal((2, false, "Inherited from Release"), (options.Optimization, options.OptimizationIsOwn, options.OptimizationHelpText));
        Assert.Equal((false, false, "Inherited from Release"), (options.DebugInfo, options.DebugInfoIsOwn, options.DebugInfoHelpText));

        // The chain member that supplies the value is named: a child of Profiling gets debug information from Profiling.
        NewConfigurationTests.ScriptNew(bed, "Deep", "Profiling", inherit: true);
        await options.NewConfigurationAsync();
        Assert.Equal("(Deep, inherits Profiling)", options.ConfigurationNote);
        Assert.Equal((2, "Inherited from Release"), (options.Optimization, options.OptimizationHelpText));
        Assert.Equal((true, false, "Inherited from Profiling"), (options.DebugInfo, options.DebugInfoIsOwn, options.DebugInfoHelpText));

        // The values are those of the working copy: an edit of an ancestor shows at once.
        options.SelectedConfiguration = "Release";
        options.Optimization = 1;
        options.SelectedConfiguration = "Deep";
        Assert.Equal((1, false), (options.Optimization, options.OptimizationIsOwn));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-022")]
    [Trait("Req", "IDE-CFG-023")]
    public Task EditingMakesOwn_RevertRestoresInherited() => TestUiThread.RunAsync(async () =>
    {
        var (bed, options) = await OpenAsync("Release-Demo");
        var notified = new List<string?>();
        options.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // Writing the value the field already shows is not a change.
        options.Optimization = 2;
        Assert.False(options.OptimizationIsOwn);
        Assert.False(options.Apply.IsEnabled);

        // A change makes the value its own — also when a later change returns to the parent's value.
        options.Optimization = 0;
        Assert.Equal((0, true, Strings.Msg_ProjectOptions_ValueOwn), (options.Optimization, options.OptimizationIsOwn, options.OptimizationHelpText));
        Assert.Contains(nameof(ProjectOptionsViewModel.OptimizationIsOwn), notified);
        options.Optimization = 2;
        Assert.Equal((2, true), (options.Optimization, options.OptimizationIsOwn));
        options.DebugInfo = true;
        Assert.Equal((true, true), (options.DebugInfo, options.DebugInfoIsOwn));

        // Switching the configuration or the page never changes a state.
        options.SelectedConfiguration = "Debug";
        options.SelectedPage = options.Pages.Single(p => p.Id == "Output");
        options.SelectedConfiguration = "Release-Demo";
        Assert.Equal((true, true), (options.OptimizationIsOwn, options.DebugInfoIsOwn));

        await options.Apply.PressAsync();
        Assert.False(options.Apply.IsEnabled);
        Assert.Equal("""{"inherits":"Release","optimization":2,"debugInfo":true,"defines":["DEMO"]}""", Entry(bed, "Release-Demo"));

        // Revert removes the own value: the parent's effective value shows again and Apply becomes enabled.
        options.RevertOptimization();
        Assert.Equal((2, false, "Inherited from Release"), (options.Optimization, options.OptimizationIsOwn, options.OptimizationHelpText));
        Assert.True(options.Apply.IsEnabled);
        options.RevertDebugInfo();
        Assert.Equal((false, false), (options.DebugInfo, options.DebugInfoIsOwn));
        options.RevertDebugInfo(); // nothing to revert
        await options.Ok.PressAsync();
        Assert.Equal("""{"inherits":"Release","defines":["DEMO"]}""", Entry(bed, "Release-Demo"));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-020")]
    [Trait("Req", "IDE-CFG-021")]
    public Task RootConfiguration_UnchangedBehaviour() => TestUiThread.RunAsync(async () =>
    {
        var (bed, options) = await OpenAsync("Release");
        Assert.False(options.IsDerived);
        Assert.Equal("(Release)", options.ConfigurationNote);
        Assert.Equal((2, false, null), (options.Optimization, options.OptimizationIsOwn, options.OptimizationHelpText));
        Assert.Equal((false, false, null), (options.DebugInfo, options.DebugInfoIsOwn, options.DebugInfoHelpText));
        Assert.Equal((false, null, null), (options.ConfigDefinesIsOwn, options.ConfigDefinesHelpText, options.DefinesHint));
        Assert.Equal(("RELEASE", "APP; RELEASE"), (options.ConfigDefines, options.EffectiveDefines));

        // No state to revert.
        options.RevertOptimization();
        options.RevertDebugInfo();
        options.RevertConfigDefines();
        Assert.Equal((2, false, "RELEASE"), (options.Optimization, options.DebugInfo, options.ConfigDefines));
        Assert.False(options.Apply.IsEnabled);

        // Editing works as in Phases 3–7 and writes every key.
        options.Optimization = 1;
        options.DebugInfo = true;
        Assert.Equal((false, false), (options.OptimizationIsOwn, options.DebugInfoIsOwn));
        await options.Ok.PressAsync();
        Assert.Equal("""{"optimization":1,"debugInfo":true,"defines":["RELEASE"],"searchPaths":["rel"],"outputDir":"bin/Release","x-note":1}""", Entry(bed, "Release"));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-024")]
    public Task Defines_OwnInheritedEffective_AndResetMarker() => TestUiThread.RunAsync(async () =>
    {
        var (bed, options) = await OpenAsync("Release-Demo");
        Assert.Equal("Added to the inherited defines. Start with ! to discard the inherited ones.", Strings.ProjectOptions_DefinesInheritHint);

        // Own entries, what was in effect before this configuration, and the resolved list.
        Assert.Equal(("DEMO", true, Strings.Msg_ProjectOptions_ValueOwn), (options.ConfigDefines, options.ConfigDefinesIsOwn, options.ConfigDefinesHelpText));
        Assert.Equal(("APP; RELEASE", "APP; RELEASE; DEMO", Strings.ProjectOptions_DefinesInheritHint), (options.InheritedDefines, options.EffectiveDefines, options.DefinesHint));

        // The reset marker as the first entry is valid and discards the inherited defines; anywhere else it is an invalid symbol.
        options.SelectedConfiguration = "Profiling";
        Assert.Equal(("!;PROFILE", null, "APP; RELEASE; DEMO", "PROFILE"), (options.ConfigDefines, options.ConfigDefinesError, options.InheritedDefines, options.EffectiveDefines));
        options.ConfigDefines = "PROFILE; !";
        Assert.Equal(string.Format(Strings.Msg_ProjectOptions_DefineInvalid, "!"), options.ConfigDefinesError);
        Assert.False(options.IsValid);
        options.ConfigDefines = " ! ; PROFILE; TRACE";
        Assert.Equal((null, "PROFILE; TRACE", true), (options.ConfigDefinesError, options.EffectiveDefines, options.IsValid));
        options.ProjectDefines = "!";
        Assert.Equal(string.Format(Strings.Msg_ProjectOptions_DefineInvalid, "!"), options.ProjectDefinesError); // not in the project-level list
        options.ProjectDefines = "APP";

        // Revert empties the own list.
        options.SelectedConfiguration = "Release-Demo";
        options.RevertConfigDefines();
        Assert.Equal(("", false, "Inherited from Release", "APP; RELEASE"), (options.ConfigDefines, options.ConfigDefinesIsOwn, options.ConfigDefinesHelpText, options.EffectiveDefines));
        Assert.True(options.Apply.IsEnabled);

        // Nothing inherited: a dash; the base level is named when no ancestor has defines.
        options.SelectedConfiguration = "Release";
        options.ConfigDefines = "";
        options.SelectedConfiguration = "Release-Demo";
        Assert.Equal(("APP", "Inherited from the project"), (options.InheritedDefines, options.ConfigDefinesHelpText));
        options.ProjectDefines = "";
        Assert.Equal(("—", ""), (options.InheritedDefines, options.EffectiveDefines));

        await options.Ok.PressAsync();
        Assert.Equal("""{"inherits":"Release-Demo","debugInfo":true,"defines":["!","PROFILE","TRACE"]}""", Entry(bed, "Profiling"));
        Assert.Equal(["PROFILE", "TRACE"], ConfigurationResolver.Resolve(bed.Services.Projects.Definition!, "Profiling").Defines);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-025")]
    [Trait("Req", "IDE-CFG-026")]
    [Trait("Req", "IDE-CFG-007")]
    public Task SharedOutputFolderWarning() => TestUiThread.RunAsync(async () =>
    {
        var (bed, options) = await OpenAsync("Release-Demo");

        // IDE-CFG-025: never inherited — the default bin/<name>; the output file and the intermediate folder follow.
        Assert.Equal(("bin/Release-Demo", null, null), (options.OutputDir, options.OutputDirError, options.OutputDirWarning));
        Assert.Equal((BuildTestBed.Dir + @"\bin\Release-Demo\Hello.exe", BuildTestBed.Dir + @"\obj\Release-Demo\"), (options.OutputFile, options.ObjDir));

        // The same folder as another configuration (full paths, ignoring case): a warning that does not block OK.
        options.OutputDir = @"BIN\Release\";
        Assert.Equal("Configuration {0} uses the same output folder.", Strings.Msg_ProjectOptions_OutputDirShared);
        Assert.Equal((null, "Configuration Release uses the same output folder."), (options.OutputDirError, options.OutputDirWarning));
        Assert.True(options.IsValid);
        Assert.True(options.Ok.IsEnabled);
        Assert.False(options.Pages.Single(p => p.Id == "Output").HasError);
        options.SelectedConfiguration = "Release";
        Assert.Equal("Configuration Release-Demo uses the same output folder.", options.OutputDirWarning);
        options.SelectedConfiguration = "Debug";
        Assert.Null(options.OutputDirWarning);
        options.SelectedConfiguration = "Release-Demo";
        options.OutputDir = "out/demo";
        Assert.Null(options.OutputDirWarning);
        options.OutputDir = "a|b"; // an invalid folder is an error, not a warning
        Assert.Equal((Strings.Msg_ProjectOptions_OutputDirInvalid, null), (options.OutputDirError, options.OutputDirWarning));
        options.OutputDir = "out/demo";

        // IDE-CFG-007: a copy of a configuration with a folder of its own keeps the folder text and warns.
        NewConfigurationTests.ScriptNew(bed, "Demo2", "Release-Demo", inherit: false);
        await options.NewConfigurationAsync();
        Assert.Equal(("out/demo", "Configuration Release-Demo uses the same output folder."), (options.OutputDir, options.OutputDirWarning));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-027")]
    public Task ApplyWithoutChange_FileByteIdentical() => TestUiThread.RunAsync(async () =>
    {
        // A canonical file with derived configurations (every key combination of the fixture, an unknown key, the reset marker).
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync(extra: ConfigurationManagerTests.Chain);
        Assert.True(await bed.Services.Projects.ApplyOptionsAsync(bed.Services.Projects.Definition! with { Run = new RunSettings("-x", "") }));
        var canonical = bed.Text(BuildTestBed.ProjectPath);
        Assert.Contains("\"Release-Demo\": {\n      \"inherits\": \"Release\",\n      \"defines\": [\n        \"DEMO\"\n      ]\n    }", canonical, StringComparison.Ordinal);

        // OK without touching a field writes nothing.
        var options = NewConfigurationTests.Options(bed);
        foreach (var name in options.ConfigurationNames)
            options.SelectedConfiguration = name; // looking at every configuration is not a change
        await options.Ok.PressAsync();
        Assert.Equal(canonical, bed.Text(BuildTestBed.ProjectPath));

        // Applying a change elsewhere rewrites the file: every configuration keeps exactly its own keys.
        options = NewConfigurationTests.Options(bed);
        options.Run.Arguments = "-y";
        await options.Apply.PressAsync();
        Assert.Equal(canonical.Replace("\"arguments\": \"-x\"", "\"arguments\": \"-y\"", StringComparison.Ordinal), bed.Text(BuildTestBed.ProjectPath));
        options.Run.Arguments = "-x";
        await options.Ok.PressAsync();
        Assert.Equal(canonical, bed.Text(BuildTestBed.ProjectPath));

        // The same after an edit that is taken back by Revert.
        options = NewConfigurationTests.Options(bed);
        options.SelectedConfiguration = "Release-Demo";
        options.Optimization = 0;
        options.RevertOptimization();
        options.Run.Arguments = "-y";
        await options.Apply.PressAsync();
        options.Run.Arguments = "-x";
        await options.Ok.PressAsync();
        Assert.Equal(canonical, bed.Text(BuildTestBed.ProjectPath));
    });
}
