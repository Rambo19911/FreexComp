using System.Globalization;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Tests.ViewModels;

[Trait("Phase", "1")]
public sealed class RecentProjectRowTests
{
    [Theory(Timeout = 30000)]
    [Trait("Req", "IDE-WEL-003")]
    [InlineData("2026-11-14 14:02", "today 14:02")]
    [InlineData("2026-11-14 00:00", "today 00:00")]
    [InlineData("2026-11-13 23:59", "yesterday")]
    [InlineData("2026-11-12 08:00", "12 Nov")]
    [InlineData("2026-01-01 08:00", "1 Jan")]
    [InlineData("2025-12-31 23:00", "31 Dec 2025")]
    public void DateTextFollowsCalendarDays(string lastOpened, string expected)
    {
        var now = new DateTime(2026, 11, 14, 16, 30, 0);
        var culture = CultureInfo.InvariantCulture;

        Assert.Equal(expected, RecentProjectRow.FormatDate(DateTime.Parse(lastOpened, culture), now, culture));
    }
}
