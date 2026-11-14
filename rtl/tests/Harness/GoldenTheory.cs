namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// The golden theories of RTL-908: one method per phase, a program is a row of the theory selected by its
/// <c>// phase: N</c> marker (default 2). Row display name <c>&lt;group&gt;/&lt;TestName&gt;</c> (RTL-900).
/// Methods for phases 4, 7 and 8 are added with the first program of that phase (xUnit fails a theory without rows).
/// </summary>
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Integration")]
public sealed class GoldenTheory
{
    public static IEnumerable<TheoryDataRow<string>> Phase2() => RtlCorpus.Rows(RtlCorpus.Discover(RtlSetup.TestsDirectory), 2);

    [Theory]
    [Trait("Phase", "2")]
    [MemberData(nameof(Phase2))]
    public Task Golden_Phase2(string id) => RunAsync(id);

    private static async Task RunAsync(string id)
    {
        var @case = RtlCorpus.Discover(RtlSetup.TestsDirectory).Single(c => c.Id == id);

        var failure = await GoldenRunner.RunAsync(@case, RtlSetup.Default, TestContext.Current.CancellationToken);

        Assert.True(failure is null, failure);
    }
}
