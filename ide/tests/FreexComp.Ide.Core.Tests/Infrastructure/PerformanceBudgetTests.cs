using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Infrastructure;

/// <summary>
/// Self-tests of <see cref="PerformanceBudget"/> (TESTING.md lead decision I-089): budgets are for optimized builds,
/// ×3 for an unoptimized assembly under test and ×3 for <c>CI_SLOW=1</c>, both multiplying; the failure message names
/// the factor and the configuration.
/// </summary>
[Trait("Phase", "1")]
public sealed class PerformanceBudgetTests
{
    [Fact(Timeout = 30000)]
    public void OptimizedBuildKeepsSpecBudget()
    {
        var budget = PerformanceBudget.Of(150, optimized: true, ciSlow: false);

        Assert.Equal(TimeSpan.FromMilliseconds(150), budget.Limit);
        Assert.Equal(1, budget.Factor);
        Assert.Contains("optimized", budget.Configuration, StringComparison.Ordinal);
    }

    [Fact(Timeout = 30000)]
    public void UnoptimizedBuildTriplesBudget()
    {
        var budget = PerformanceBudget.Of(150, optimized: false, ciSlow: false);

        Assert.Equal(TimeSpan.FromMilliseconds(450), budget.Limit);
        Assert.Equal(3, budget.Factor);
        Assert.Contains("unoptimized", budget.Configuration, StringComparison.Ordinal);
    }

    [Fact(Timeout = 30000)]
    public void CiSlowAndUnoptimizedBothApply()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(450), PerformanceBudget.Of(150, optimized: true, ciSlow: true).Limit);
        var both = PerformanceBudget.Of(150, optimized: false, ciSlow: true);
        Assert.Equal(TimeSpan.FromMilliseconds(1350), both.Limit);
        Assert.Equal(9, both.Factor);
        Assert.Contains("CI_SLOW", both.Configuration, StringComparison.Ordinal);
    }

    /// <summary>The configuration is read from the assembly under test (DebuggableAttribute JIT-optimizer flag).</summary>
    [Fact(Timeout = 30000)]
    public void ConfigurationIsReadFromAssemblyUnderTest()
    {
#if DEBUG
        const bool optimized = false;
#else
        const bool optimized = true;
#endif
        Assert.Equal(optimized, PerformanceBudget.IsOptimized(typeof(FreexComp.Ide.Core.Languages.PascalClassifier).Assembly));
        Assert.Equal(optimized, PerformanceBudget.IsOptimized(typeof(FreexComp.Compiler.Syntax.Lexer).Assembly));
    }

    /// <summary>
    /// Work that takes more than 3 × 150 ms of thread CPU fails the tripled budget of an unoptimized build, and the
    /// failure message names the factor and the configuration.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Category", "Performance")]
    public void WorkBeyondTripledBudgetFails()
    {
        var budget = PerformanceBudget.Of(150, optimized: false, ciSlow: false);

        var median = PerformanceBudget.MedianThreadCpu(() => SpinThreadCpu(TimeSpan.FromMilliseconds(470)), runs: 1);

        Assert.False(budget.IsMetBy(median), $"{median.TotalMilliseconds:F0} ms must exceed {budget.Limit.TotalMilliseconds:F0} ms");
        var message = budget.Describe("median CPU time", median);
        Assert.Contains("budget 450 ms", message, StringComparison.Ordinal);
        Assert.Contains("150 ms × 3", message, StringComparison.Ordinal);
        Assert.Contains("unoptimized", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// I-192: the fastest of the measured runs is reported, so one run slowed down (here: made five times longer, as by
    /// load from another process) does not count, and the warm-up runs are not measured; nothing is under-counted.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Category", "Performance")]
    public void MinThreadCpuIgnoresASlowRun()
    {
        var calls = 0;
        var fastest = PerformanceBudget.MinThreadCpu(() =>
        {
            calls++;
            SpinThreadCpu(TimeSpan.FromMilliseconds(calls is 1 or 5 ? 100 : 20));
        }, runs: 3);

        Assert.Equal(5, calls); // two warm-ups, three measured
        Assert.InRange(fastest.TotalMilliseconds, 20, 99);
    }

    /// <summary>Burns CPU on the calling thread until it has used <paramref name="cpu"/> (thread CPU, not wall clock).</summary>
    private static void SpinThreadCpu(TimeSpan cpu)
    {
        var start = PerformanceBudget.CurrentThreadCpu();
        double sink = 1;
        while (PerformanceBudget.CurrentThreadCpu() - start < cpu)
        {
            for (int i = 0; i < 10_000; i++)
            {
                sink = Math.Sqrt(sink + i);
            }
        }

        GC.KeepAlive(sink);
    }
}
