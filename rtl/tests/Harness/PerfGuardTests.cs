namespace FreexComp.Rtl.Tests.Harness;

/// <summary>
/// RTL-920 (testing.md §4): performance guards. Each program of <c>rtl/tests/perf/</c> is compiled at <c>-O1</c> (its
/// <c>// opc:</c> header) and checked like a golden program (stdout = <c>.out</c>); the wall time of its run is compared
/// with the bound. Until Phase 8 a missed bound is only written to the test output as a warning and does not fail.
/// </summary>
[Trait("Phase", "2")]
[Trait("Kind", "Golden")]
[Trait("Category", "Performance")]
[Trait("Category", "Toolchain")]
[Trait("Req", "RTL-920")]
public sealed class PerfGuardTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(2);

    [Fact]
    public Task Perf_Strings() => GuardAsync("Perf_Strings");

    [Fact]
    public Task Perf_Format() => GuardAsync("Perf_Format");

    [Fact]
    public Task Perf_FloatToStr() => GuardAsync("Perf_FloatToStr");

    private async Task GuardAsync(string name)
    {
        var source = Path.Combine(RtlSetup.TestsDirectory, "perf", name + ".pas");
        Assert.True(File.Exists(source), source + " does not exist");
        Assert.Contains("-O1", RtlCorpus.ReadHeader(File.ReadAllText(source)).OpcOptions);

        var elapsed = TimeSpan.Zero;
        var failure = await GoldenRunner.RunAsync(new RtlCase("perf/" + name, source), RtlSetup.Default,
            TestContext.Current.CancellationToken, t => elapsed = t);
        Assert.True(failure is null, failure);

        output.WriteLine($"{name}: run {elapsed.TotalMilliseconds:F0} ms (bound {Bound.TotalMilliseconds:F0} ms)");
        if (elapsed > Bound)
        {
            output.WriteLine($"WARNING {name}: the run took {elapsed.TotalMilliseconds:F0} ms, more than the bound (RTL-920; not failing before Phase 8)");
        }
    }
}
