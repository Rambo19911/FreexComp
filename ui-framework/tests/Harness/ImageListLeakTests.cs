using FreexComp.Rtl.Tests.Harness;

namespace FreexComp.UiFramework.Tests;

/// <summary>
/// phase8/image-list.md UIF-1483 (card F05-062a): editing and drawing an image list leaks nothing.
/// <c>ui-framework/tests/gui/Img_NoLeak.pas</c> runs 1 000 cycles of insert, replace, replace by an icon, move, delete,
/// icon and bitmap read-back, enabled and disabled drawing of an alpha cell and of an overlay on one list with a
/// handle after a warm-up, and prints its GDI object, USER object and live allocation counts at both points; this
/// fact compiles and runs it, waits for its end and compares the numbers.
/// </summary>
[Trait("Phase", "8")]
public sealed class ImageListLeakTests(ITestOutputHelper output)
{
    /// <summary>GDI and USER objects may grow by this much (as UIF-576); allocations must be equal.</summary>
    public const int HandleTolerance = 2;

    public const int Cycles = 1000;

    public static string SourcePath => Path.Combine(UiCorpus.AreaDirectory, "tests", "gui", "Img_NoLeak.pas");

    /// <summary>Null when the counts printed by the program show no leak and every cycle behaved; otherwise what is wrong.</summary>
    public static string? Check(string stdout)
    {
        var names = new[] { "warm.gdi", "warm.user", "warm.allocs", "end.gdi", "end.user", "end.allocs", "cycles", "ms" };
        var values = names.ToDictionary(n => n, n => RichEditPerfTests.Value(stdout, n));
        var missing = names.Where(n => values[n] is null).ToList();
        if (missing.Count > 0)
        {
            return $"no line for {string.Join(", ", missing)} in:\n{stdout}";
        }

        int V(string name) => values[name]!.Value;
        var problems = new List<string>();
        if (V("cycles") != Cycles)
        {
            problems.Add($"{V("cycles")} cycles as expected, expected {Cycles}");
        }

        if (V("end.gdi") - V("warm.gdi") > HandleTolerance)
        {
            problems.Add($"GDI objects {V("warm.gdi")} -> {V("end.gdi")}");
        }

        if (V("end.user") - V("warm.user") > HandleTolerance)
        {
            problems.Add($"USER objects {V("warm.user")} -> {V("end.user")}");
        }

        if (V("end.allocs") != V("warm.allocs"))
        {
            problems.Add($"live allocations {V("warm.allocs")} -> {V("end.allocs")}");
        }

        return problems.Count == 0 ? null : string.Join("; ", problems) + $" after {Cycles} cycles";
    }

    [Fact]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "UIF-1483")]
    public async Task ThousandEditsLeakNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (exe, failure) = await LayoutHost.CompileAsync(SourcePath, ct);
        Assert.True(failure is null, failure);
        var work = Path.GetDirectoryName(exe!)!;
        try
        {
            var run = await LayoutHost.RunProcessAsync(exe!, "", work, LayoutHost.RunLimit, ct);
            Assert.True(run.Stopped is null, run.Stopped);
            Assert.True(run.ExitCode == 0, $"exit code {run.ExitCode}\n{run.StdOut}{run.StdErr}");
            output.WriteLine("Img_NoLeak: " + run.StdOut.ReplaceLineEndings(" ").Trim());
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
        const string Good = "warm.gdi=6\nwarm.user=1\nwarm.allocs=40\nend.gdi=7\nend.user=1\nend.allocs=40\ncycles=1000\nms=812\n";
        Assert.Null(Check(Good));
        Assert.Null(Check(Good.Replace("\n", "\r\n", StringComparison.Ordinal)));
        Assert.Contains("GDI objects 6 -> 9", Check(Good.Replace("end.gdi=7", "end.gdi=9", StringComparison.Ordinal)));
        Assert.Contains("USER objects 1 -> 4", Check(Good.Replace("end.user=1", "end.user=4", StringComparison.Ordinal)));
        Assert.Contains("live allocations 40 -> 41", Check(Good.Replace("end.allocs=40", "end.allocs=41", StringComparison.Ordinal)));
        Assert.Contains("999 cycles", Check(Good.Replace("cycles=1000", "cycles=999", StringComparison.Ordinal)));
        Assert.Contains("no line for end.allocs", Check(Good.Replace("end.allocs=40\n", "", StringComparison.Ordinal)));
    }
}
