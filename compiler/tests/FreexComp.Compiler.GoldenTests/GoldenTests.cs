namespace FreexComp.Compiler.GoldenTests;

/// <summary>
/// The golden theory (CMP-901…904, compiler-cli.md §5): one case per test of <c>tests/compiler/**</c>, at <c>-O0</c>
/// and (trait <c>Opt=O2</c>) at <c>-O2</c>. Phase 2 exit check: <c>--filter "Phase=2&amp;Kind=Golden"</c>.
/// </summary>
[Trait("Phase", "2")]
[Trait("Kind", "Golden")]
[Trait("Category", "Toolchain")]
public sealed class GoldenTests
{
    public static IEnumerable<TheoryDataRow<string, string>> Cases() => GoldenCorpus.Rows(GoldenCorpus.Discover(GoldenSetup.CorpusDirectory));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Golden(string id, string optimization)
    {
        var @case = GoldenCorpus.Discover(GoldenSetup.CorpusDirectory).Single(c => c.Id == id);

        var failure = await GoldenRunner.RunAsync(@case, optimization == "O2", GoldenSetup.Default, TestContext.Current.CancellationToken);

        Assert.True(failure is null, failure);
    }
}
