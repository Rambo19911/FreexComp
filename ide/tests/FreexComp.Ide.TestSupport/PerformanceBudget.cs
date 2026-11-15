using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// Time budgets for <c>Category=Performance</c> tests (TESTING.md lead decision I-089): the spec's numbers are for
/// optimized builds; the budget is tripled when an assembly under test is unoptimized (its
/// <see cref="DebuggableAttribute"/> disables the JIT optimizer, i.e. a Debug build) and tripled when <c>CI_SLOW=1</c>;
/// both multiply. Measurements are chosen so a loaded machine does not make them flaky: <see cref="MedianThreadCpu"/>
/// counts only the CPU time of the calling thread (other processes and threads do not add to it) and takes the median
/// of several runs after one unmeasured warm-up run (JIT); <see cref="Median"/> is for wall-clock samples of work that
/// spans threads.
/// </summary>
public static class PerformanceBudget
{
    /// <summary>Factor applied per condition (unoptimized build, <c>CI_SLOW=1</c>).</summary>
    public const double SlowFactor = 3;

    /// <summary>The budget of <paramref name="milliseconds"/> for work done by <paramref name="underTest"/> in this process.</summary>
    public static Budget Of(double milliseconds, params Assembly[] underTest)
    {
        if (underTest.Length == 0)
        {
            throw new ArgumentException("name the assemblies whose build configuration the budget depends on", nameof(underTest));
        }

        return Of(milliseconds, underTest.All(IsOptimized), Environment.GetEnvironmentVariable("CI_SLOW") == "1",
            string.Join(", ", underTest.Select(a => a.GetName().Name)));
    }

    /// <summary>The budget of <paramref name="milliseconds"/> for the given configuration.</summary>
    public static Budget Of(double milliseconds, bool optimized, bool ciSlow, string? assemblies = null)
    {
        double factor = (optimized ? 1 : SlowFactor) * (ciSlow ? SlowFactor : 1);
        var configuration = optimized ? "optimized build" : "unoptimized (Debug) build";
        if (assemblies is not null)
        {
            configuration += " of " + assemblies;
        }

        if (ciSlow)
        {
            configuration += ", CI_SLOW=1";
        }

        return new Budget(milliseconds, factor, configuration);
    }

    /// <summary>False when <paramref name="assembly"/> was built with the JIT optimizer disabled (Debug configuration).</summary>
    public static bool IsOptimized(Assembly assembly) =>
        assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };

    /// <summary>Runs <paramref name="action"/> once unmeasured, then <paramref name="runs"/> times; median of the calling thread's CPU time (user + kernel).</summary>
    public static TimeSpan MedianThreadCpu(Action action, int runs = 5)
    {
        action();
        var samples = new List<TimeSpan>(runs);
        for (int i = 0; i < runs; i++)
        {
            var before = CurrentThreadCpu();
            action();
            samples.Add(CurrentThreadCpu() - before);
        }

        return Median(samples);
    }

    /// <summary>
    /// I-192: the smallest CPU time of the calling thread over <paramref name="runs"/> runs, after two unmeasured warm-up
    /// runs, with a full garbage collection before each run so that collecting earlier garbage is not charged to it.
    /// Load from other processes only ever adds time to a run (cache and memory-bandwidth contention, frequency drops),
    /// so the fastest run is the measure least affected by it; the budget itself stays unchanged.
    /// </summary>
    public static TimeSpan MinThreadCpu(Action action, int runs = 7)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
        action();
        var best = TimeSpan.MaxValue;
        for (int i = 0; i < runs; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = CurrentThreadCpu();
            action();
            var sample = CurrentThreadCpu() - before;
            if (sample < best)
                best = sample;
        }

        return best;
    }

    /// <summary>Median of <paramref name="samples"/> (for wall-clock samples of work that spans threads and awaits).</summary>
    public static TimeSpan Median(IEnumerable<TimeSpan> samples)
    {
        var sorted = samples.Order().ToList();
        return sorted[sorted.Count / 2];
    }

    /// <summary>User + kernel time of the calling thread (Windows only: ADR 007).</summary>
    public static TimeSpan CurrentThreadCpu()
    {
        if (!GetThreadTimes(GetCurrentThread(), out _, out _, out var kernel, out var user))
        {
            throw new InvalidOperationException($"GetThreadTimes failed: {Marshal.GetLastPInvokeError()}");
        }

        return TimeSpan.FromTicks(kernel + user); // FILETIME units are 100 ns = TimeSpan ticks
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);
}

/// <summary>A time budget: the spec's <see cref="SpecMilliseconds"/> times <see cref="Factor"/> for <see cref="Configuration"/>.</summary>
public sealed record Budget(double SpecMilliseconds, double Factor, string Configuration)
{
    public TimeSpan Limit => TimeSpan.FromMilliseconds(SpecMilliseconds * Factor);

    public bool IsMetBy(TimeSpan measured) => measured < Limit;

    /// <summary>Failure text naming the measurement, the budget, the factor and the configuration.</summary>
    public string Describe(string what, TimeSpan measured) => string.Create(CultureInfo.InvariantCulture,
        $"{what} {measured.TotalMilliseconds:F0} ms, budget {Limit.TotalMilliseconds:F0} ms ({SpecMilliseconds:0.###} ms × {Factor:0.###}: {Configuration})");
}
