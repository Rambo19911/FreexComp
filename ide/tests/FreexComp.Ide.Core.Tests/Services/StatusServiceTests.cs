using FreexComp.Ide.Core.Services;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Services;

[Trait("Phase", "1")]
public sealed class StatusServiceTests
{
    [Fact(Timeout = 30000)]
    public void MessageClearsAfterItsDuration()
    {
        var clock = new ManualClock();
        var status = new StatusService(clock);

        status.ShowMessage("first", TimeSpan.FromSeconds(3));
        clock.Advance(TimeSpan.FromSeconds(2));
        status.ShowMessage("second", TimeSpan.FromSeconds(3)); // replaces and restarts the expiry
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("second", status.Message);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(status.Message);
    }
}
