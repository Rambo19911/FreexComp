using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.ViewModels;

/// <summary>
/// I-221: the Welcome page's background existence check (IDE-WEL-004) is work a test settles before it ends. Before, its
/// result was posted to the test UI thread after the test that opened the page had ended (seen under CPU load).
/// </summary>
public sealed class WelcomePageCheckTests
{
    private const string Gone = @"C:\gone\Gone.pproj";

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-WEL-004")]
    public async Task MissingCheckIsSettledBeforeTheTestEnds()
    {
        var deadline = CurrentTest.Deadline;
        var gate = new ManualResetEventSlim(); // not disposed: the check may still be leaving Wait
        WelcomeDocumentViewModel? page = null;
        await TestUiThread.RunAsync(() =>
        {
            RecordingFileSystem? recording = null;
            var services = TestComposition.Build(o => o.FileSystem = recording = new RecordingFileSystem(o.FileSystem));
            services.RecentItems.Add(RecentKind.Project, Gone);
            recording!.FailWith = call =>
            {
                if (call == "FileExists " + Gone)
                    gate.Wait(deadline.Remaining); // the check is held on its background thread
                return null;
            };
            page = services.Documents.ShowWelcomePage();
            Assert.True(page.IsCheckingMissing); // still running: the body returns before its result
            gate.Set();
            return Task.CompletedTask;
        });

        // The result was applied on the test UI thread before RunAsync completed, not posted after the test ended.
        Assert.True(page!.MissingChecks.IsCompleted);
        Assert.False(page.IsCheckingMissing);
        Assert.True(Assert.Single(page.Projects).IsMissing);
        Assert.Empty(TestUiThread.TakeLatePosts(nameof(WelcomePageCheckTests) + "." + nameof(MissingCheckIsSettledBeforeTheTestEnds)));
    }
}
