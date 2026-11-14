using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Infrastructure;

/// <summary>The fake clock under the threading Core code uses in tests (I-173).</summary>
[Trait("Phase", "1")]
public sealed class ManualClockTests
{
    /// <summary>
    /// Core code schedules from the thread pool (e.g. <c>SharingRetry</c> after an awaited save) while the test thread
    /// advances: no timer is lost, none runs twice, a disposed one never runs, and <see cref="ManualClock.Advance"/>
    /// never sees a half-updated timer list (the I-173 flake: a NullReferenceException inside <c>Advance</c>).
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task ScheduleFromAnotherThreadWhileAdvancing()
    {
        const int Count = 20000;
        var clock = new ManualClock();
        int fired = 0, cancelledFired = 0;
        var scheduling = Task.Run(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                clock.Schedule(TimeSpan.FromMilliseconds(1), () => Interlocked.Increment(ref fired));
                clock.Schedule(TimeSpan.FromDays(3650),() => Interlocked.Increment(ref cancelledFired)).Dispose();
            }
        }, TestContext.Current.CancellationToken);

        while (!scheduling.IsCompleted)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        await scheduling;
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(Count, fired);
        Assert.Equal(0, cancelledFired);
    }
}
