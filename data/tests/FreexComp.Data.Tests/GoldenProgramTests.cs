namespace FreexComp.Data.Tests;

/// <summary>
/// The golden theory over <c>data/tests/programs/*.pas</c> (testing.md §1, DAT-400, DAT-401): one row per program,
/// display name = program name, <c>Req</c> traits from its <c>// covers:</c> header.
/// </summary>
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Integration")]
public sealed class GoldenProgramTests
{
    public static IEnumerable<TheoryDataRow<string>> Programs() => DataCorpus.Rows();

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task Program(string name)
    {
        var program = DataCorpus.Discover().Single(p => p.Name == name);

        var failure = await GoldenProgramRunner.RunAsync(program, TestContext.Current.CancellationToken);

        Assert.True(failure is null, failure);
    }
}
