using Avalonia.Headless.XUnit;

namespace FreexComp.Ide.Tests;

/// <summary>Phase 3 acceptance with fakes (I-127, phase3-build-run.md §11 steps 1–6); the body is <see cref="Phase3Scenario"/>.</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class Phase3AcceptanceTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-065")]
    [Trait("Req", "IDE-P3-066")]
    public async Task BuildErrorNavigateFixRun()
    {
        await using var h = await Phase3Scenario.StartAsync(realBuild: false);
        await Phase3Scenario.BuildErrorNavigateFixRunAsync(h);
    }
}
