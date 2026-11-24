using FreexComp.Ide.Tests.EndToEnd;

namespace FreexComp.E2E.Tests;

/// <summary>
/// Phase 7 cross-area check (data task A-071, ADR 004): the data-access CRUD sample is copied to a temporary folder, built
/// (Debug) with the real build engine and toolchain and run with <c>--dump-layout</c>; the runtime layout of its form
/// equals the designer layout engine's layout of <c>MainForm.form</c> over the shipped framework and data-access
/// metadata. The scenario body is shared with the IDE area's <see cref="DumpLayoutEqualityTests"/>.
/// </summary>
[Trait("Phase", "7")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class Phase7Tests
{
    [Fact(Timeout = 120000)]
    public Task Layout_CrudForm_DesignerEqualsRuntime() => DataLayoutScenario.CrudFormEqualsRuntimeAsync();
}
