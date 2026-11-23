using Avalonia.Headless.XUnit;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// layout-engine.md §9 (DSG-LAY-060…063): the designer's C# layout engine and the framework's run-time layout agree on the
/// framework's sample projects and on a form designed from scratch in the IDE.
/// </summary>
[Trait("Phase", "5")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class DumpLayoutEqualityTests
{
    /// <summary>The Phase 4 sample projects of sample-apps.md (folder under <c>ui-framework/samples</c>, project file).</summary>
    public static TheoryData<string, string> Samples() => new()
    {
        { "hello-form", "Hello.pproj" },
        { "layout-gallery", "Gallery.pproj" },
    };

    /// <summary>
    /// DSG-LAY-060/061: each sample is built (Debug) and run with <c>--dump-layout</c>; every form of the runtime dump equals
    /// the designer engine's run-time layout of the sample's form file of that name (with its <c>.steps</c> sidecar).
    /// DSG-LAY-062: the samples and the hook exist, so the test runs; a missing toolchain fails it (testing.md rule 5).
    /// </summary>
    [Theory(Timeout = 120000)]
    [MemberData(nameof(Samples))]
    [Trait("Req", "DSG-LAY-060")]
    [Trait("Req", "DSG-LAY-061")]
    [Trait("Req", "DSG-LAY-062")]
    public async Task DesignerLayoutEqualsRuntimeLayout(string sample, string projectFile)
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(LayoutRoundTrip.FrameworkDir, "samples", sample);
        Assert.True(File.Exists(Path.Combine(source, projectFile)), "sample project missing: " + Path.Combine(source, projectFile));
        var projectDir = Path.Combine(temp.Path, sample);
        LayoutRoundTrip.CopyFolder(source, projectDir);

        var exe = await LayoutRoundTrip.BuildAsync(Path.Combine(projectDir, projectFile));
        var runtime = await LayoutRoundTrip.RunDumpAsync(exe, temp.Path);
        Assert.NotEmpty(runtime.Forms);
        var designer = LayoutRoundTrip.DesignerDump(projectDir, runtime);
        LayoutRoundTrip.AssertEqual(designer, runtime, sample);
    }

    /// <summary>
    /// DSG-LAY-063: a form designed from scratch in a headless designer session (new <c>gui-pascal</c> project, controls
    /// placed and edited, form resized by its handle), saved, built and run with <c>--dump-layout</c>: the runtime layout
    /// equals the designer's.
    /// </summary>
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "DSG-LAY-063")]
    public async Task DesignedFormRoundTrip()
    {
        await using var h = await Phase5Scenario.StartAsync();
        await Phase5Scenario.DesignSaveBuildCompareAsync(h, handler: false);
    }
}
