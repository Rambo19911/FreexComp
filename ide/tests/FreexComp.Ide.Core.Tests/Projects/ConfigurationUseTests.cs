using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.Core.Tests.CodeModel;
using FreexComp.Ide.Core.Tests.Debugging;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary>
/// Effective configuration values in the IDE (phase8-configurations.md §2, I-247): every consumer of a per-configuration
/// value sees the values a derived configuration inherits.
/// </summary>
[Trait("Phase", "8")]
public sealed class ConfigurationUseTests
{
    private const string Unit =
        "unit Unit1;\ninterface\nprocedure Run;\nimplementation\nvar\n  Count: Integer;\nprocedure Run;\nbegin\n" +
        "{$IFDEF FEATURE}\n  Count := 1;\n{$ELSE}\n  Count := 2;\n{$ENDIF}\nend;\nend.\n";

    private static string? ClassAt(IReadOnlyList<TokenClassSpan> spans, int offset) =>
        spans.FirstOrDefault(s => s.Offset <= offset && offset < s.Offset + s.Length).TokenClass;

    private static IReadOnlyList<TokenClassSpan> Drawn(TextDocumentViewModel doc) =>
        doc.CreateClassifier().Classify(doc.Buffer, new LineRange(1, doc.Buffer.LineCount));

    /// <summary>The active configuration is derived: a define set only in its parent activates the <c>{$IFDEF}</c> branch; removing it there refreshes.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-001")]
    [Trait("Req", "IDE-CFG-002")]
    public Task CodeModelAndInactiveCodeUseEffectiveDefines() => TestUiThread.RunAsync(CodeModelAndInactiveCodeUseEffectiveDefinesOnUiThread);

    private static async Task CodeModelAndInactiveCodeUseEffectiveDefinesOnUiThread()
    {
        var bed = await SemanticHighlightTests.StartAsync("""
            , "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": ["FEATURE"], "outputDir": "bin/Debug" },
                                  "Demo": { "inherits": "Debug" } },
              "activeConfiguration": "Demo"
            """);
        bed.File(BuildTestBed.UnitPath, Unit);
        var doc = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        var delay = TimeSpan.FromMilliseconds(bed.Services.Settings.Get(SettingsCatalog.EditorLiveDiagnosticsDelayMs));
        int feature = Unit.IndexOf("Count := 1", StringComparison.Ordinal), other = Unit.IndexOf("Count := 2", StringComparison.Ordinal);

        bed.Clock.Advance(delay);
        await bed.Services.SemanticHighlights.PendingFor(doc);
        Assert.Equal(doc.Buffer.Version, doc.Semantic.Version);
        Assert.NotEqual("syntax.inactive", ClassAt(Drawn(doc), feature)); // FEATURE comes from the parent
        Assert.Equal("syntax.inactive", ClassAt(Drawn(doc), other));
        Assert.Contains("FEATURE", bed.Services.CodeModel.Synchronize()!.Options.Defines);

        // An edit of the ancestor changes the effective values of the active configuration: the code model and the colouring follow.
        var definition = bed.Services.Projects.Definition!;
        var parent = definition.Configurations["Debug"] with { Defines = [] };
        Assert.True(await bed.Services.Projects.ApplyOptionsAsync(definition with { Configurations = definition.Configurations.SetItem("Debug", parent) }));
        bed.Clock.Advance(delay);
        await bed.Services.SemanticHighlights.PendingFor(doc);
        Assert.DoesNotContain("FEATURE", bed.Services.CodeModel.Synchronize()!.Options.Defines);
        Assert.Equal("syntax.inactive", ClassAt(Drawn(doc), feature));
        Assert.NotEqual("syntax.inactive", ClassAt(Drawn(doc), other));
    }

    /// <summary>IDE-P3-086 asks by the effective value: a configuration inheriting <c>debugInfo: false</c> asks, one inheriting <c>true</c> does not.</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-CFG-001")]
    public Task DebugInfoCheckUsesEffectiveValue() => TestUiThread.RunAsync(DebugInfoCheckUsesEffectiveValueOnUiThread);

    private static async Task DebugInfoCheckUsesEffectiveValueOnUiThread()
    {
        using var bed = new DebugTestBed();
        bed.File(bed.ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "console", "mainSource": "Hello.pas",
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                                  "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Debug" },
                                  "WithInfo": { "inherits": "Debug", "outputDir": "bin/Debug" },
                                  "NoInfo": { "inherits": "Release", "outputDir": "bin/Debug" } },
              "activeConfiguration": "NoInfo",
              "files": [ { "path": "Hello.pas" } ] }
            """);
        bed.File(bed.MainPath, "program Hello;\nbegin\nend.\n");
        Assert.True(await bed.Services.Projects.OpenAsync(bed.ProjectPath));
        bed.Sessions.UseScenario("{}");

        bed.Dialogs.Answer("Msg.Debug.NoDebugInfo", "Cancel");
        Assert.False(await bed.Debug.StartAsync());
        Assert.Equal(["NoInfo"], Assert.Single(bed.Dialogs.Requests).Args);
        Assert.Empty(bed.Sessions.Sessions);

        await bed.Services.Commands.ExecuteAsync("project.setConfiguration", "WithInfo");
        Assert.True(await bed.Debug.StartAsync());
        Assert.Single(bed.Dialogs.Requests); // no further question
        Assert.Equal("WithInfo", bed.Engine.Requests[^1].Configuration);
        await bed.Debug.StopAsync();
    }

    /// <summary>I-252: the user-code roots of a debug session are the effective search paths of the active configuration (CMP-1160).</summary>
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-CFG-001")]
    public Task DebugUserRootsUseEffectiveSearchPaths() => TestUiThread.RunAsync(DebugUserRootsUseEffectiveSearchPathsOnUiThread);

    private static async Task DebugUserRootsUseEffectiveSearchPathsOnUiThread()
    {
        using var bed = new DebugTestBed();
        bed.File(bed.ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "console", "mainSource": "Hello.pas", "searchPaths": ["lib"],
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "searchPaths": ["dbg"], "outputDir": "bin/Debug" },
                                  "Demo": { "inherits": "Debug", "searchPaths": ["../demo"], "outputDir": "bin/Debug" },
                                  "Alone": { "inherits": "Demo", "searchPaths": ["!", "alone"], "outputDir": "bin/Debug" } },
              "activeConfiguration": "Demo",
              "files": [ { "path": "Hello.pas" } ] }
            """);
        bed.File(bed.MainPath, "program Hello;\nbegin\nend.\n");
        Assert.True(await bed.Services.Projects.OpenAsync(bed.ProjectPath));
        bed.Sessions.UseScenario("{}");

        Assert.True(await bed.Debug.StartAsync());
        Assert.Equal([bed.Dir, Path.Combine(bed.Dir, "lib"), Path.Combine(bed.Dir, "dbg"), Path.GetFullPath(Path.Combine(bed.Dir, @"..\demo"))],
            bed.Sessions.Requested[^1].UserSourceRoots);
        await bed.Debug.StopAsync();

        // The reset marker of the active configuration discards the inherited roots.
        await bed.Services.Commands.ExecuteAsync("project.setConfiguration", "Alone");
        Assert.True(await bed.Debug.StartAsync());
        Assert.Equal([bed.Dir, Path.Combine(bed.Dir, "alone")], bed.Sessions.Requested[^1].UserSourceRoots);
        await bed.Debug.StopAsync();
    }

    /// <summary>Run, the Run Parameters preview and Open Output Folder use <c>bin/&lt;name&gt;</c> of the derived configuration.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-CFG-001")]
    [Trait("Req", "IDE-CFG-003")]
    public Task OutputPathsOfDerivedConfiguration() => TestUiThread.RunAsync(OutputPathsOfDerivedConfigurationOnUiThread);

    private static async Task OutputPathsOfDerivedConfigurationOnUiThread()
    {
        var shell = new FakeShellService();
        var bed = new BuildTestBed(o => o.Shell = shell);
        await bed.OpenHelloAsync(extra: """
            , "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "out/dbg" },
                                  "Demo": { "inherits": "Debug" } },
              "activeConfiguration": "Demo"
            """);
        var project = bed.Services.Projects.Project!;
        const string folder = BuildTestBed.Dir + @"\bin\Demo";

        Assert.Equal(folder + @"\Hello.exe", bed.Services.Run.PreviousExecutable(project));
        Assert.Equal(folder + @"\Hello.exe", RunParametersViewModel.ExecutablePath(bed.Services.Projects.Definition!, "Demo"));
        bed.Fs.CreateDirectory(folder);
        await bed.Services.Commands.ExecuteAsync("project.openOutputFolder");
        Assert.Equal(["OpenFolder " + folder], shell.Calls);

        // IDE-CFG-003: the lists of configurations show plain names in file order.
        Assert.Equal(["Debug", "Demo"], project.Configurations);
    }
}
