using System.Text.Json;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Dialogs;

/// <summary>Creating a configuration from Project Options (phase8-configurations.md §3, I-247): derived by default, a copy on request.</summary>
[Trait("Phase", "8")]
public sealed class NewConfigurationTests
{
    internal const string Configurations = """
        , "searchPaths": ["lib"], "defines": ["APP"],
          "configurations": {
            "Debug": { "optimization": 0, "debugInfo": true, "defines": ["DEBUG"], "outputDir": "bin/Debug" },
            "Release": { "optimization": 2, "debugInfo": false, "defines": ["RELEASE"], "searchPaths": ["rel"], "outputDir": "bin/Release", "rtti": "published", "x-note": { "kept": [1, 2] } },
            "Staging": { "inherits": "Release", "defines": ["STAGE"], "outputDir": "out/stage", "x-tag": 7 } },
          "activeConfiguration": "Debug"
        """;

    internal static ProjectOptionsViewModel Options(BuildTestBed bed) =>
        new(bed.Services.Projects, bed.Dialogs, bed.Services.FileDialogs, bed.Fs, bed.Services.Layout);

    internal static JsonElement SavedConfigurations(BuildTestBed bed) =>
        JsonDocument.Parse(bed.Text(BuildTestBed.ProjectPath)).RootElement.GetProperty("configurations");

    /// <summary>Scripts the sub-dialog: name, source, the Inherit check box (null = leave the default), then Create.</summary>
    internal static void ScriptNew(BuildTestBed bed, string name, string basedOn, bool? inherit)
    {
        bed.Dialogs.Script(NewConfigurationViewModel.DialogName, async dialog =>
        {
            var sub = (NewConfigurationViewModel)dialog;
            sub.ConfigName = name;
            sub.CopyFrom = basedOn;
            if (inherit is { } value)
                sub.Inherit = value;
            await sub.Create.PressAsync();
        });
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-005")]
    [Trait("Req", "IDE-CFG-006")]
    public Task InheritMode_WritesOnlyInherits() => TestUiThread.RunAsync(InheritModeOnUiThread);

    private static async Task InheritModeOnUiThread()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync(extra: Configurations);
        var options = Options(bed);
        options.SelectedConfiguration = "Release";

        // IDE-CFG-005: the source is the selected configuration and Inherit is checked by default.
        NewConfigurationViewModel? seen = null;
        bed.Dialogs.Script(NewConfigurationViewModel.DialogName, async dialog =>
        {
            seen = (NewConfigurationViewModel)dialog;
            Assert.Equal(("Release", true), (seen.CopyFrom, seen.Inherit));
            seen.ConfigName = "Release-Demo";
            await seen.Create.PressAsync();
        });
        await options.NewConfigurationAsync();
        Assert.NotNull(seen);
        Assert.Equal(new NewConfigurationResult("Release-Demo", "Release", true), seen.Result);
        Assert.Equal("Release-Demo", options.SelectedConfiguration);
        // The new configuration shows what it inherits; its output folder is its own.
        Assert.Equal((2, false, "", "bin/Release-Demo"), (options.Optimization, options.DebugInfo, options.ConfigDefines, options.OutputDir));

        await options.Ok.PressAsync();
        var entry = SavedConfigurations(bed).GetProperty("Release-Demo");
        Assert.Equal("""{"inherits":"Release"}""", JsonSerializer.Serialize(entry));
        Assert.Contains("\"Release-Demo\": {\n      \"inherits\": \"Release\"\n    }", bed.Text(BuildTestBed.ProjectPath), StringComparison.Ordinal);
        var effective = FreexComp.Build.ConfigurationResolver.Resolve(bed.Services.Projects.Definition!, "Release-Demo");
        Assert.Equal((2, false, "bin/Release-Demo"), (effective.Optimization, effective.DebugInfo, effective.OutputDir));
        Assert.Equal(["APP", "RELEASE"], effective.Defines);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-007")]
    public Task CopyMode_CopiesOwnValuesLinkAndUnknownKeys() => TestUiThread.RunAsync(CopyModeOnUiThread);

    private static async Task CopyModeOnUiThread()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync(extra: Configurations);
        var options = Options(bed);

        // A root configuration with the default folder: every own value and the unknown key; the folder follows the new name.
        ScriptNew(bed, "Release2", "Release", inherit: false);
        await options.NewConfigurationAsync();
        Assert.Equal((2, false, "RELEASE", "bin/Release2"), (options.Optimization, options.DebugInfo, options.ConfigDefines, options.OutputDir));

        // A derived configuration with its own folder: the link, the own values and the folder text are copied unchanged.
        ScriptNew(bed, "Staging2", "Staging", inherit: false);
        await options.NewConfigurationAsync();
        Assert.Equal(("STAGE", "out/stage"), (options.ConfigDefines, options.OutputDir));

        await options.Ok.PressAsync();
        var saved = SavedConfigurations(bed);
        Assert.Equal(["Debug", "Release", "Staging", "Release2", "Staging2"], saved.EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            """{"optimization":2,"debugInfo":false,"defines":["RELEASE"],"searchPaths":["rel"],"outputDir":"bin/Release2","rtti":"published","x-note":{"kept":[1,2]}}""",
            JsonSerializer.Serialize(saved.GetProperty("Release2")));
        Assert.Equal("""{"inherits":"Release","defines":["STAGE"],"outputDir":"out/stage","x-tag":7}""", JsonSerializer.Serialize(saved.GetProperty("Staging2")));
        // The sources are untouched.
        Assert.Equal("""{"inherits":"Release","defines":["STAGE"],"outputDir":"out/stage","x-tag":7}""", JsonSerializer.Serialize(saved.GetProperty("Staging")));
        Assert.Equal("bin/Release", saved.GetProperty("Release").GetProperty("outputDir").GetString());
    }
}
