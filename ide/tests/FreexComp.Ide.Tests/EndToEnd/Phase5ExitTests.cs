using Avalonia.Headless.XUnit;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// The Phase 5 exit check of docs/ROADMAP.md §3 (designer testing.md §5.4): through the headless harness a <c>gui-pascal</c>
/// project is created, controls are placed and edited on the design surface, an event handler is added (code sync), the
/// form is saved, built with the real build engine and run with <c>--dump-layout</c>; the runtime layout equals the
/// designer's (the single-scenario form of <see cref="DumpLayoutEqualityTests.DesignedFormRoundTrip"/>).
/// </summary>
[Trait("Phase", "5")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class Phase5ExitTests
{
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "DSG-LAY-063")]
    [Trait("Req", "DSG-CS-092")]
    public async Task E2E_Phase5_DesignSaveCompile_LayoutIdentical()
    {
        await using var h = await Phase5Scenario.StartAsync();
        await Phase5Scenario.DesignSaveBuildCompareAsync(h, handler: true);
    }
}
