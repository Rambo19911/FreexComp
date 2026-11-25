using System.Globalization;
using System.Text.RegularExpressions;
using FreexComp.Rtl.Tests.Harness;

namespace FreexComp.UiFramework.Tests;

/// <summary>
/// rich-edit.md §9 (card F05-038, acceptance 4): the rich text editor loads 5 MB of text within its budget.
/// <c>ui-framework/tests/perf/Perf_RichEditLoad.pas</c> writes the file, loads it five times after a warm-up into a
/// visible control and prints the character count and the median load as CPU time of its thread; this fact compiles
/// and runs it and compares the numbers. The result counts on a quiet machine only (TESTING.md rule 11).
/// </summary>
[Trait("Phase", "8")]
public sealed class RichEditPerfTests(ITestOutputHelper output)
{
    /// <summary>The budget of the card: 5 MB of text in at most 2 s.</summary>
    public const int BudgetMilliseconds = 2000;

    /// <summary>The characters the control holds: lines of 60 characters and a CR LF each, without the last line break.</summary>
    public const int ExpectedCharacters = 5 * 1024 * 1024 / 62 * 62 - 2;

    public static string SourcePath => Path.Combine(UiCorpus.AreaDirectory, "tests", "perf", "Perf_RichEditLoad.pas");

    /// <summary>TESTING.md (I-089): a slow machine (<c>CI_SLOW=1</c>) multiplies a budget by 3; the budget itself is never loosened.</summary>
    public static int Factor(string? ciSlow) => ciSlow == "1" ? 3 : 1;

    /// <summary>The value of <c>name=&lt;digits&gt;</c> on a line of its own in <paramref name="stdout"/>; null when absent.</summary>
    public static int? Value(string stdout, string name)
    {
        var match = Regex.Match(stdout, "^" + Regex.Escape(name) + @"=(\d+)\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    [Fact]
    [Trait("Kind", "Native")]
    [Trait("Category", "Performance")]
    [Trait("Category", "Toolchain")]
    [Trait("Category", "Foreground")]
    [Trait("Req", "UIF-1306")]
    public async Task LoadFiveMegabytesOfText()
    {
        Harness.RequireDesktop();
        var ct = TestContext.Current.CancellationToken;
        var (exe, failure) = await LayoutHost.CompileAsync(SourcePath, ct);
        Assert.True(failure is null, failure);
        var work = Path.GetDirectoryName(exe!)!;
        try
        {
            var data = Path.Combine(work, "data");
            Directory.CreateDirectory(data);
            var run = await LayoutHost.RunProcessAsync(exe!, GoldenRunner.CommandLine([data], null), work, LayoutHost.RunLimit, ct);
            Assert.True(run.Stopped is null, run.Stopped);
            Assert.True(run.ExitCode == 0, $"exit code {run.ExitCode}\n{run.StdOut}{run.StdErr}");

            var chars = Value(run.StdOut, "chars");
            var ms = Value(run.StdOut, "ms");
            Assert.True(chars is not null && ms is not null, "no chars= / ms= line in:\n" + run.StdOut);
            output.WriteLine($"Perf_RichEditLoad: chars={chars} ms={ms} (budget {BudgetMilliseconds} ms)");
            Assert.Equal(ExpectedCharacters, chars);

            var factor = Factor(Environment.GetEnvironmentVariable("CI_SLOW"));
            Assert.True(ms <= BudgetMilliseconds * factor,
                $"loading 5 MB of text took {ms} ms of thread CPU time (median of 5), budget {BudgetMilliseconds} ms x {factor} (CI_SLOW factor)");
        }
        finally
        {
            GoldenRunner.TryDelete(work);
        }
    }

    /// <summary>The parsing and the factor of the fact above, without the toolchain.</summary>
    [Fact]
    public void ReadsTheNumbersOfTheProgram()
    {
        Assert.Equal(5242842, ExpectedCharacters);
        Assert.Equal(5242842, Value("lines=84562\r\nchars=5242842\r\nms=312\r\n", "chars"));
        Assert.Equal(312, Value("lines=84562\nchars=5242842\nms=312\n", "ms"));
        Assert.Null(Value("chars=\nxms=3\n", "ms"));
        Assert.Equal(1, Factor(null));
        Assert.Equal(3, Factor("1"));
    }
}
