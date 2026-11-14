using FreexComp.Ide.Core.Services;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Core.Layout;

namespace FreexComp.Ide.Core.Tests.Services;

[Trait("Phase", "1")]
public sealed class DialogHistoryTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-062")]
    public void HistoryIsMostRecentFirstWithoutDuplicatesAndAtMostTwenty()
    {
        var layout = TestComposition.Build().Layout;
        Assert.Empty(layout.GetDialogHistory("Find", "SearchText"));

        for (var i = 1; i <= 25; i++)
            layout.AddDialogHistory("Find", "SearchText", "t" + i);
        var history = layout.GetDialogHistory("Find", "SearchText");
        Assert.Equal(LayoutService.MaxHistory, history.Count);
        Assert.Equal(20, history.Count);
        Assert.Equal("t25", history[0]);
        Assert.Equal("t6", history[^1]);

        // Re-adding moves the entry to the front; the comparison is ordinal (case-sensitive).
        layout.AddDialogHistory("Find", "SearchText", "t10");
        layout.AddDialogHistory("Find", "SearchText", "T10");
        history = layout.GetDialogHistory("Find", "SearchText");
        Assert.Equal(["T10", "t10", "t25"], history.Take(3));
        Assert.Single(history, h => h == "t10");
        Assert.Equal(20, history.Count);

        // Fields and dialogs are kept apart.
        Assert.Empty(layout.GetDialogHistory("Find", "ReplaceText"));
    }
}
