using System.Text.RegularExpressions;
using FreexComp.Ide.Core.Tests.Infrastructure;

namespace FreexComp.Ide.Core.Tests;

[Trait("Phase", "1")]
public sealed partial class TestHygieneTests
{
    /// <summary>IDE-TEST-004: no real waiting in ide test sources (the harness's WaitForAsync yields instead).</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-004")]
    public void NoSleepsInTests()
    {
        var testsRoot = Path.Combine(SpecTables.RepoRootPath(), "ide", "tests");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(testsRoot, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (SleepCall().IsMatch(lines[i]))
                {
                    offenders.Add($"{relative}({i + 1}): {lines[i].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Sleeping in tests (IDE-TEST-004):\n" + string.Join("\n", offenders));
    }

    [Theory]
    [InlineData("Thread" + ".Sleep(100);", true)]
    [InlineData("await Task" + ".Delay(50);", true)]
    [InlineData("await Task" + ".Delay (TimeSpan.FromSeconds(1));", true)]
    [InlineData("await Task.Yield();", false)]
    [InlineData("var delay = TimeSpan.FromSeconds(1);", false)]
    public void SleepPatternMatches(string line, bool matches) => Assert.Equal(matches, SleepCall().IsMatch(line));

    /// <summary>
    /// I-205: no wait in ide test sources is bounded by a fixed wall-clock time: under a full-solution run (every test
    /// project at once) a fixed 5–20 s failed work that was still progressing. Waits are bounded by the test's own
    /// timeout through <see cref="FreexComp.Ide.TestSupport.TestDeadline"/>, which names the step on expiry.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-013")]
    public void NoFixedWallClockWaits()
    {
        var testsRoot = Path.Combine(SpecTables.RepoRootPath(), "ide", "tests");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(testsRoot, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (FixedWait().IsMatch(lines[i]) && !lines[i].TrimStart().StartsWith("[InlineData(", StringComparison.Ordinal))
                    offenders.Add($"{relative}({i + 1}): {lines[i].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0, "Waits bounded by a fixed wall-clock time (I-205, use TestDeadline):\n" + string.Join("\n", offenders));
    }

    [Theory]
    [InlineData("await done.Task.WaitAsync(TimeSpan.FromSeconds(10), token);", true)]
    [InlineData("await done.Task.WaitAsync(Limit, TestContext.Current.CancellationToken);", true)]
    [InlineData("Gate.Wait(TimeSpan.FromSeconds(20));", true)]
    [InlineData("Release.Wait(Limit);", true)]
    [InlineData("using var safety = DispatcherTimer.RunOnce(() => frame.Continue = false, Timeout);", true)]
    [InlineData("cts.CancelAfter(TimeSpan.FromSeconds(5));", true)]
    [InlineData("if (watch.Elapsed > WaitLimit)", true)]
    [InlineData("await folded.Task.WaitAsync(BackgroundRunBound, TestContext.Current.CancellationToken);", true)]
    [InlineData("await deadline.WaitAsync(done.Task, \"scan finished\");", false)]
    [InlineData("deadline.Wait(Gate, \"gate opened\");", false)]
    [InlineData("await key.WaitForExitAsync(token);", false)]
    [InlineData("never.Wait();", false)]
    [InlineData("clock.Advance(TimeSpan.FromSeconds(1));", false)]
    [InlineData("Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), \"save did not finish\");", true)]
    [InlineData("Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), \"gone\"); // a measured budget, not a wait", false)]
    public void FixedWaitPatternMatches(string line, bool matches) => Assert.Equal(matches, FixedWait().IsMatch(line));

    // Split so that this file does not match itself.
    [GeneratedRegex(@"\b(Thread\s*\.\s*Sleep|Task\s*\.\s*Delay)\s*" + @"\(")]
    private static partial Regex SleepCall();

    /// <summary>
    /// A wait or timer call given a literal <c>TimeSpan</c> or a fixed limit field, or a stopwatch compared with one; a
    /// line that says it is "not a wait" (an assertion of a measured budget) is exempt.
    /// </summary>
    [GeneratedRegex(@"^(?!.*not a wait)(.*\.(WaitAsync|Wait|WaitOne|TryTake|CancelAfter|RunOnce)\s*\(.*(TimeSpan\s*\.\s*From|\b(\w*(Limit|Bound|Budget)|Timeout)\b(?!\s*\())"
        + @"|.*\bElapsed\s*[<>]=?.*(TimeSpan\s*\.\s*From|\w*Limit\b))")]
    private static partial Regex FixedWait();
}
