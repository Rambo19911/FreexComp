using Avalonia.Headless.XUnit;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests;

/// <summary>
/// The Phase 1 acceptance scenario (phase1-shell.md §14) through the headless shell; the steps are in
/// <see cref="Phase1Scenario"/>, which the cross-area E2E test runs as well.
/// </summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class Phase1AcceptanceTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-071")]
    [Trait("Req", "IDE-PM-023")]
    [Trait("Req", "IDE-PM-040")]
    [Trait("Req", "IDE-P1-085")]
    [Trait("Req", "IDE-P1-113")]
    [Trait("Req", "IDE-PM-011")]
    [Trait("Req", "IDE-WEL-005")]
    [Trait("Req", "UI-WIN-072")]
    [Trait("Req", "IDE-SET-101")]
    public async Task CreateEditSaveReopenProject()
    {
        // 1. An empty user directory (no settings, no recent items).
        await using var h = await HeadlessHarness.StartAsync(o => o.Settings.Clear());
        await Phase1Scenario.CreateEditSaveReopenAsync(h);
    }
}
