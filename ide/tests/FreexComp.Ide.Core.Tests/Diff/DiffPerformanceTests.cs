using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Diff;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Diff;

/// <summary>I-329, IDE-DIFF-009: compute time of a large diff.</summary>
[Trait("Phase", "8")]
public sealed class DiffPerformanceTests
{
    /// <summary>Spec budget (IDE-DIFF-009), for optimized builds.</summary>
    private const double BudgetMilliseconds = 1000;

    /// <summary>Two 20 000-line texts with every tenth line changed (10 %, spread through the file), Myers and Patience.</summary>
    [Fact(Timeout = 60000)]
    [Trait("Category", "Performance")]
    [Trait("Req", "IDE-DIFF-009")]
    public void TwentyThousandLinesUnderOneSecond()
    {
        var left = new StringBuilder();
        var right = new StringBuilder();
        for (var i = 0; i < 20_000; i++)
        {
            var line = string.Create(CultureInfo.InvariantCulture, $"    Values[{i}] := Compute(Item{i % 97}, {i * 7});");
            left.Append(line).Append("\r\n");
            right.Append(i % 10 == 5 ? line.Replace("Compute", "Evaluate", StringComparison.Ordinal) : line).Append("\r\n");
        }

        var (leftInput, rightInput) = (new DiffInput(left.ToString()), new DiffInput(right.ToString()));
        var budget = PerformanceBudget.Of(BudgetMilliseconds, typeof(DiffService).Assembly);
        foreach (var algorithm in new[] { DiffAlgorithm.Myers, DiffAlgorithm.Patience })
        {
            DiffResult? result = null;
            var fastest = PerformanceBudget.MinThreadCpu(
                () => result = DiffService.Compute(leftInput, rightInput, new DiffOptions(algorithm), TestContext.Current.CancellationToken), runs: 3);

            Assert.Equal(2000, result!.ChangeCount);
            TestContext.Current.TestOutputHelper?.WriteLine(budget.Describe($"{algorithm}: diffing 2 × 20 000 lines took", fastest));
            Assert.True(budget.IsMetBy(fastest), budget.Describe($"{algorithm}: diffing 2 × 20 000 lines took", fastest));
        }
    }
}
