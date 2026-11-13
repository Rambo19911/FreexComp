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

    // Split so that this file does not match itself.
    [GeneratedRegex(@"\b(Thread\s*\.\s*Sleep|Task\s*\.\s*Delay)\s*" + @"\(")]
    private static partial Regex SleepCall();
}
