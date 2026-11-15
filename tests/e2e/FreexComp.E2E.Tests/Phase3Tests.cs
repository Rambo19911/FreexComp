using Avalonia.Headless.XUnit;
using FreexComp.Ide.Tests;
using FreexComp.Ide.TestSupport;

namespace FreexComp.E2E.Tests;

/// <summary>
/// The Phase 3 exit check (ROADMAP §3, docs/spec/ide/testing.md §5.5): phase3-build-run.md §11 steps 1–6 through the
/// headless shell with the real build engine, the real LLVM toolchain and a real process (console mode "output") — no
/// fakes for the compiler area's parts. The scenario body is shared with <see cref="Phase3AcceptanceTests"/>.
/// </summary>
[Trait("Phase", "3")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class Phase3Tests
{
    [AvaloniaFact(Timeout = 120000)]
    public async Task E2E_Phase3_BuildErrorNavigateFixRun()
    {
        using var temp = new TempDirectory();
        await using var h = await Phase3Scenario.StartAsync(realBuild: true, o => o.Root = temp.Combine("ide"));
        Assert.StartsWith(temp.Path, h.TempDir, StringComparison.OrdinalIgnoreCase);

        await Phase3Scenario.BuildErrorNavigateFixRunAsync(h);

        // The real linker produced the program in the project's output folder.
        Assert.True(File.Exists(Path.Combine(Phase3Scenario.OutputDir(h), "HelloErr.exe")));
    }
}
