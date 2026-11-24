using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>app-manifest.md CMP-1113, CMP-1100 (task C-296): the <c>manifest</c> key → <see cref="CompilationOptions"/>.</summary>
public sealed partial class OptionsMappingTests
{
    private static Project WithManifest(string appType, string manifestKey) => Load(
        $$"""{ "formatVersion": 1, "name": "App", "mainSource": "App.pas", "appType": "{{appType}}"{{(manifestKey.Length > 0 ? ", \"manifest\": " + manifestKey : "")}} }""");

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1113")]
    public void ManifestObject_MapsToCompilationOptions()
    {
        var empty = ProjectOptions.ToCompilationOptions(WithManifest("gui", "{}"), "Debug");
        Assert.Equal("default", empty.Manifest);
        Assert.Equal(ManifestExecutionLevel.AsInvoker, empty.ManifestExecutionLevel);
        Assert.Equal(ManifestDpiAwareness.Default, empty.ManifestDpiAwareness);

        var both = ProjectOptions.ToCompilationOptions(
            WithManifest("gui", """{ "executionLevel": "requireAdministrator", "dpiAwareness": "system" }"""), "Release");
        Assert.Equal("default", both.Manifest);
        Assert.Equal(ManifestExecutionLevel.RequireAdministrator, both.ManifestExecutionLevel);
        Assert.Equal(ManifestDpiAwareness.System, both.ManifestDpiAwareness);

        var console = ProjectOptions.ToCompilationOptions(
            WithManifest("console", """{ "executionLevel": "highestAvailable", "dpiAwareness": "unaware" }"""), "Debug");
        Assert.Equal(ManifestExecutionLevel.HighestAvailable, console.ManifestExecutionLevel);
        Assert.Equal(ManifestDpiAwareness.Unaware, console.ManifestDpiAwareness);
        Assert.Equal(ManifestDpiAwareness.PerMonitorV2, ProjectOptions.ToCompilationOptions(
            WithManifest("console", """{ "dpiAwareness": "perMonitorV2" }"""), "Debug").ManifestDpiAwareness);
        Assert.Equal(ManifestExecutionLevel.AsInvoker, ProjectOptions.ToCompilationOptions(
            WithManifest("console", """{ "executionLevel": "asInvoker" }"""), "Debug").ManifestExecutionLevel);

        // customFile equals the string path form: relative to the project, or absolute
        var custom = ProjectOptions.ToCompilationOptions(WithManifest("gui", """{ "customFile": "res/app.manifest" }"""), "Debug");
        Assert.Equal(@"C:\p\Hello\res\app.manifest", custom.Manifest);
        Assert.Equal(custom.Manifest, ProjectOptions.ToCompilationOptions(WithManifest("gui", "\"res/app.manifest\""), "Debug").Manifest);
        Assert.Equal(ManifestExecutionLevel.AsInvoker, custom.ManifestExecutionLevel);
        Assert.Equal(ManifestDpiAwareness.Default, custom.ManifestDpiAwareness);
        Assert.Equal(@"C:\m\x.manifest", ProjectOptions.ToCompilationOptions(WithManifest("gui", """{ "customFile": "C:/m/x.manifest" }"""), "Debug").Manifest);

        // a library ignores the two settings; its custom file is passed on as a path
        var library = ProjectOptions.ToCompilationOptions(
            WithManifest("library", """{ "executionLevel": "requireAdministrator", "dpiAwareness": "system" }"""), "Debug");
        Assert.Equal(AppType.Library, library.AppType);
        Assert.Equal(ManifestExecutionLevel.AsInvoker, library.ManifestExecutionLevel);
        Assert.Equal(ManifestDpiAwareness.Default, library.ManifestDpiAwareness);
        Assert.Equal(@"C:\p\Hello\lib.manifest",
            ProjectOptions.ToCompilationOptions(WithManifest("library", """{ "customFile": "lib.manifest" }"""), "Debug").Manifest);

        // a model the loader would have rejected
        var p = WithManifest("gui", "{}");
        Assert.Throws<ArgumentException>(() => ProjectOptions.ToCompilationOptions(p with { ManifestSettings = new("admin", null, null) }, "Debug"));
        Assert.Throws<ArgumentException>(() => ProjectOptions.ToCompilationOptions(p with { ManifestSettings = new(null, "high", null) }, "Debug"));
        Assert.Throws<ArgumentException>(() => ProjectOptions.ToCompilationOptions(p with { Manifest = "none" }, "Debug"));
    }

    /// <summary>CMP-1100: no <c>manifest</c> key → "" for every application type, which the compiler reads as <c>default</c> for console and GUI.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1100")]
    [Trait("Req", "CMP-1113")]
    [InlineData("console")]
    [InlineData("gui")]
    public void ManifestAbsent_ConsoleIsDefault(string appType)
    {
        var o = ProjectOptions.ToCompilationOptions(WithManifest(appType, ""), "Debug");

        Assert.Equal("", o.Manifest);
        Assert.Equal(ManifestExecutionLevel.AsInvoker, o.ManifestExecutionLevel);
        Assert.Equal(ManifestDpiAwareness.Default, o.ManifestDpiAwareness);

        // (what the compiler makes of "": BuildEngineTests.ManifestAbsent_ConsoleProject_LinksTheGeneratedManifest)
        Assert.Equal("default", ProjectOptions.ToCompilationOptions(WithManifest(appType, "\"default\""), "Debug").Manifest);
    }
}
