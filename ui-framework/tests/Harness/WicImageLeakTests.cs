using FreexComp.Rtl.Tests.Harness;

namespace FreexComp.UiFramework.Tests;

/// <summary>
/// wic-image.md UIF-1359 (card F05-052, acceptance 2): loading images through the imaging component of the operating
/// system leaks nothing. <c>ui-framework/tests/gui/Wic_NoLeak.pas</c> loads a valid image 1 000 times and a damaged
/// one 1 000 times after a warm-up and prints its GDI object count, kernel handle count and live allocation count
/// at both points; this fact compiles and runs it, waits for its end and compares the numbers.
/// </summary>
[Trait("Phase", "8")]
public sealed class WicImageLeakTests(ITestOutputHelper output)
{
    /// <summary>GDI objects and kernel handles may grow by this much (as UIF-576); allocations must be equal.</summary>
    public const int HandleTolerance = 2;

    public const int Loads = 1000;

    public static string SourcePath => Path.Combine(UiCorpus.AreaDirectory, "tests", "gui", "Wic_NoLeak.pas");

    public static string FixtureDirectory => Path.Combine(UiCorpus.AreaDirectory, "tests", "fixtures", "images");

    /// <summary>
    /// Null when the counts printed by the program show no leak and every load behaved as expected; otherwise what is wrong.
    /// </summary>
    public static string? Check(string stdout)
    {
        var names = new[] { "warm.gdi", "warm.handles", "warm.allocs", "end.gdi", "end.handles", "end.allocs", "loaded", "refused", "ms" };
        var values = names.ToDictionary(n => n, n => RichEditPerfTests.Value(stdout, n));
        var missing = names.Where(n => values[n] is null).ToList();
        if (missing.Count > 0)
        {
            return $"no line for {string.Join(", ", missing)} in:\n{stdout}";
        }

        int V(string name) => values[name]!.Value;
        var problems = new List<string>();
        if (V("loaded") != Loads || V("refused") != Loads)
        {
            problems.Add($"{V("loaded")} valid loads and {V("refused")} refused loads, expected {Loads} each");
        }

        if (V("end.gdi") - V("warm.gdi") > HandleTolerance)
        {
            problems.Add($"GDI objects {V("warm.gdi")} -> {V("end.gdi")}");
        }

        if (V("end.handles") - V("warm.handles") > HandleTolerance)
        {
            problems.Add($"kernel handles {V("warm.handles")} -> {V("end.handles")}");
        }

        if (V("end.allocs") != V("warm.allocs"))
        {
            problems.Add($"live allocations {V("warm.allocs")} -> {V("end.allocs")}");
        }

        return problems.Count == 0 ? null : string.Join("; ", problems) + $" after {2 * Loads} loads";
    }

    [Fact]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "UIF-1359")]
    public async Task ThousandLoadsLeakNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (exe, failure) = await LayoutHost.CompileAsync(SourcePath, ct);
        Assert.True(failure is null, failure);
        var work = Path.GetDirectoryName(exe!)!;
        try
        {
            var run = await LayoutHost.RunProcessAsync(exe!, GoldenRunner.CommandLine([FixtureDirectory], null), work, LayoutHost.RunLimit, ct);
            Assert.True(run.Stopped is null, run.Stopped);
            Assert.True(run.ExitCode == 0, $"exit code {run.ExitCode}\n{run.StdOut}{run.StdErr}");
            output.WriteLine("Wic_NoLeak: " + run.StdOut.ReplaceLineEndings(" ").Trim());
            var problem = Check(run.StdOut);
            Assert.True(problem is null, problem);
        }
        finally
        {
            GoldenRunner.TryDelete(work);
        }
    }

    /// <summary>The comparison of the fact above, without the toolchain.</summary>
    [Fact]
    public void ComparesTheCountsOfTheProgram()
    {
        const string Good = "warm.gdi=4\nwarm.handles=113\nwarm.allocs=54\nend.gdi=5\nend.handles=111\nend.allocs=54\nloaded=1000\nrefused=1000\nms=9437\n";
        Assert.Null(Check(Good));
        Assert.Null(Check(Good.Replace("\n", "\r\n", StringComparison.Ordinal)));
        Assert.Contains("GDI objects 4 -> 7", Check(Good.Replace("end.gdi=5", "end.gdi=7", StringComparison.Ordinal)));
        Assert.Contains("kernel handles 113 -> 116", Check(Good.Replace("end.handles=111", "end.handles=116", StringComparison.Ordinal)));
        Assert.Contains("live allocations 54 -> 55", Check(Good.Replace("end.allocs=54", "end.allocs=55", StringComparison.Ordinal)));
        Assert.Contains("999 valid loads", Check(Good.Replace("loaded=1000", "loaded=999", StringComparison.Ordinal)));
        Assert.Contains("no line for end.allocs", Check(Good.Replace("end.allocs=54\n", "", StringComparison.Ordinal)));
    }
}
