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
        }, runs: 3, minRunCpu: TimeSpan.Zero); // one call per run: the call count is the run count

        Assert.Equal(5, calls); // two warm-ups, three measured
        Assert.InRange(fastest.TotalMilliseconds, 20, 99);
    }

    /// <summary>
    /// I-202: on a slow (loaded) machine the measured runs stop once the time limit has passed, so the test ends well
    /// inside its timeout; at least one run is always measured, and fewer runs can only make the fastest one slower.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Category", "Performance")]
    public void MinThreadCpuStopsAtTheTimeLimit()
    {
        var calls = 0;
        var elapsed = () => TimeSpan.FromSeconds(10 * calls); // each call "takes" 10 s of wall time
        PerformanceBudget.MinThreadCpu(() => calls++, runs: 7, timeLimit: TimeSpan.FromSeconds(25), elapsed: elapsed, minRunCpu: TimeSpan.Zero);
        Assert.Equal(3, calls); // two warm-ups (20 s), one measured run (30 s), then the limit

        calls = 0;
        var fastest = PerformanceBudget.MinThreadCpu(() => calls++, runs: 7, timeLimit: TimeSpan.Zero, elapsed: elapsed, minRunCpu: TimeSpan.Zero);
        Assert.Equal(3, calls); // the limit already passed: still one measured run
        Assert.NotEqual(TimeSpan.MaxValue, fastest);

        calls = 0;
        PerformanceBudget.MinThreadCpu(() => calls++, runs: 7, timeLimit: TimeSpan.FromSeconds(1000), elapsed: elapsed, minRunCpu: TimeSpan.Zero);
        Assert.Equal(9, calls); // a fast machine: all seven runs
    }

    /// <summary>
    /// I-206: thread CPU time advances in ticks of ~15.6 ms, so a single run of ~2 ms of work used to measure 0 (or a
    /// whole tick). Each run now repeats the action until it spans many ticks and divides by the count, so the
    /// per-call time is resolved: within ±50 % of the fastest wall-clock time of the same fixed workload.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Category", "Performance")]
    public void MinThreadCpuResolvesSubTickWork()
    {
        var (work, reference) = FixedWork(TimeSpan.FromMilliseconds(2));

        var measured = PerformanceBudget.MinThreadCpu(work);

        Assert.InRange(reference.TotalMilliseconds, 1.5, 10);
        Assert.InRange(measured.TotalMilliseconds, reference.TotalMilliseconds * 0.5, reference.TotalMilliseconds * 1.5);
    }

    /// <summary>I-206: an action of ~4 ms fails a 2 ms budget instead of measuring 0 and passing vacuously.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Category", "Performance")]
    public void SlowActionFailsShortBudget()
    {
        var (work, _) = FixedWork(TimeSpan.FromMilliseconds(4));
        var budget = PerformanceBudget.Of(2, optimized: true, ciSlow: false);

        var measured = PerformanceBudget.MinThreadCpu(work);

        Assert.False(budget.IsMetBy(measured), budget.Describe("a 4 ms action", measured));
    }

    /// <summary>
    /// A fixed amount of computation that takes at least <paramref name="target"/> of wall time on this machine, and
    /// its fastest wall-clock time over 20 calls (the fastest call is the one least interrupted, so it approximates the
    /// CPU time of the work itself).
    /// </summary>
    private static (Action Work, TimeSpan Reference) FixedWork(TimeSpan target)
    {
        var iterations = 1000;
        while (true)
        {
            var n = iterations;
            void Work()
            {
                double sink = 1;
                for (int i = 0; i < n; i++)
                {
                    sink = Math.Sqrt(sink + i);
                }

                GC.KeepAlive(sink);
            }

            Work();
            var fastest = TimeSpan.MaxValue;
            for (int r = 0; r < 20; r++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Work();
                if (watch.Elapsed < fastest)
                    fastest = watch.Elapsed;
            }

            if (fastest >= target)
                return (Work, fastest);
            iterations *= 2;
        }
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
