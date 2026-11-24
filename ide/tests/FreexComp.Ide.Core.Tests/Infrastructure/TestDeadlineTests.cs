using System.Reflection;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Infrastructure;

/// <summary>I-205: waits are bounded by the test's own timeout, not by a fixed wall-clock time.</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Unit")]
public sealed class TestDeadlineTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-013")]
    public void RunningTestIsRegisteredWithItsTimeout()
    {
        var deadline = CurrentTest.Deadline;
        Assert.Same(deadline, CurrentTest.Deadline); // registered before the test ran, not made up on each look-up
        Assert.Equal(TimeSpan.FromSeconds(30), deadline.Timeout);
        Assert.InRange(deadline.Remaining, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30) - TestDeadline.Margin);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-013")]
    public void RemainingIsTheTimeoutLessMarginAndElapsed()
    {
        var elapsed = TimeSpan.FromSeconds(10);
        var deadline = new TestDeadline(TimeSpan.FromSeconds(30), elapsed: () => elapsed);
        Assert.Equal(TimeSpan.FromSeconds(18), deadline.Remaining);
        Assert.False(deadline.IsExpired);
        elapsed = TimeSpan.FromSeconds(29);
        Assert.Equal(TimeSpan.Zero, deadline.Remaining);
        Assert.True(deadline.IsExpired);
        Assert.Equal(TestDeadline.DefaultTimeout, new TestDeadline(TimeSpan.Zero).Timeout); // no timeout declared
    }

    /// <summary>
    /// I-222: a test that starts the scripted debug adapter (a real process on a shared, loaded machine: Phase 6,
    /// Category Integration, asynchronous) declares the safety-net timeout <see cref="TestDeadline.DebugTestTimeout"/> —
    /// its waits are bounded by that deadline and by nothing shorter.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-013")]
    public void DebugSessionTestsUseTheSafetyNetTimeout()
    {
        static bool Has(MethodInfo method, string name, string value) =>
            method.GetCustomAttributes<TraitAttribute>(inherit: true).Concat(method.DeclaringType!.GetCustomAttributes<TraitAttribute>(inherit: true))
                .Any(t => t.Name == name && t.Value == value);
        var methods = typeof(TestDeadlineTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<FactAttribute>(inherit: true).Any() && typeof(Task).IsAssignableFrom(m.ReturnType)
                && Has(m, "Phase", "6") && Has(m, "Category", "Integration")).ToList();
        Assert.True(methods.Count >= 10, $"only {methods.Count} debug session tests seen");
        var other = methods
            .Select(m => (m, m.GetCustomAttributes<FactAttribute>(inherit: true).Single().Timeout))
            .Where(x => x.Timeout != TestDeadline.DebugTestTimeout)
            .Select(x => $"{x.m.DeclaringType!.Name}.{x.m.Name}: {x.Timeout} ms").ToList();
        Assert.Empty(other);
        Assert.Equal(TimeSpan.FromSeconds(120), TimeSpan.FromMilliseconds(TestDeadline.DebugTestTimeout));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-013")]
    public Task ExpiredWaitsNameTheirStep() => TestUiThread.RunAsync(() => ExpiredWaitsNameTheirStepOnUiThread()); // I-209

    private async Task ExpiredWaitsNameTheirStepOnUiThread()
    {
        var deadline = new TestDeadline(TimeSpan.FromSeconds(30), elapsed: () => TimeSpan.FromSeconds(28));
        var never = new TaskCompletionSource<int>();
        using var closed = new SemaphoreSlim(0);
        using var gate = new ManualResetEventSlim(false);

        var e = await Assert.ThrowsAsync<TimeoutException>(() => deadline.WaitAsync(never.Task, "the scan result"));
        Assert.Equal("Timed out waiting for: the scan result (28.0 s into the test, timeout 30 s)", e.Message);
        Assert.Contains("the plain task", (await Assert.ThrowsAsync<TimeoutException>(() => deadline.WaitAsync((Task)never.Task, "the plain task"))).Message, StringComparison.Ordinal);
        Assert.Contains("the semaphore", (await Assert.ThrowsAsync<TimeoutException>(() => deadline.WaitAsync(closed, "the semaphore"))).Message, StringComparison.Ordinal);
        Assert.Contains("the held scan", Assert.Throws<TimeoutException>(() => deadline.Wait(closed, "the held scan")).Message, StringComparison.Ordinal);
        Assert.Contains("the gate", Assert.Throws<TimeoutException>(() => deadline.Wait(gate, "the gate")).Message, StringComparison.Ordinal);

        // Signals that are already there are no timeout, however little time is left.
        Assert.Equal(7, await deadline.WaitAsync(Task.FromResult(7), "done"));
        closed.Release();
        deadline.Wait(closed, "released");
        gate.Set();
        deadline.Wait(gate, "set");
    }
}
