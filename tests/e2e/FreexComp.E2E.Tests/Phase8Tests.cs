using Avalonia.Headless.XUnit;
using FreexComp.Ide.Tests.EndToEnd;

namespace FreexComp.E2E.Tests;

/// <summary>
/// Phase 8 cross-area check (lead task L-015, card F02-004): with "Step Into Library Source" on, Step Into on a call into
/// the runtime library stops in the protected library document; with it off, the call is stepped over. Real build engine,
/// toolchain and debug adapter. The scenario body is shared with the IDE area's <c>Phase8LibraryTests</c>.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class Phase8Tests
{
    [AvaloniaFact(Timeout = 180000)]
    public Task E2E_Phase8_StepIntoLibrarySource() => LibrarySteppingScenario.StepIntoSysUtilsAsync();
}
