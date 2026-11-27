using System.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary>
/// I-339: the Project Manager's missing-file check (IDE-PM-103) ends with its project and with the view model. A check
/// still scanning on its background thread is dropped: nothing stays pending, the scan asks the file system nothing
/// more, and no result comes back to the UI thread later (before, the stale check stayed pending until its scan ended
/// and then posted its continuation — after the test that started it, when the thread pool was late).
/// </summary>
[Trait("Phase", "1")]
[Trait("Req", "IDE-PM-103")]
public sealed class MissingFileCheckLifetimeTests
{
    private const string Dir = @"C:\p\Hello";
    private const string ProjectPath = Dir + @"\Hello.pproj";
    private static readonly string[] Listed = ["Hello.pas", "Unit1.pas", "Unit2.pas", "Gone.pas"];

    /// <summary>
    /// Hello.pproj with four listed files, <c>Gone.pas</c> absent. Once the project is open and its checks have
    /// settled, one more check is started (main-window activation): the first <c>FileExists</c> of a listed file its
    /// scan asks off the UI thread waits for <see cref="Release"/>; every such question from then on is counted.
    /// Created on the test UI thread.
    /// </summary>
    private sealed class HeldScan
    {
        private readonly int _uiThread = Environment.CurrentManagedThreadId;
        private readonly TaskCompletionSource _scanning = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(); // not disposed: the scan may still be leaving Wait
        private Task _heldScan = Task.CompletedTask;
        private volatile bool _armed;
        private volatile bool _failScan; // the armed scan fails in the file system instead of being held
        private int _probes;

        public HeldScan()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var fs = new InMemoryFileSystem();
            fs.CreateDirectory(Dir);
            fs.WriteAllBytes(ProjectPath, Encoding.UTF8.GetBytes("""
                { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "activeConfiguration": "Debug",
                  "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" } },
                  "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" }, { "path": "Unit2.pas" }, { "path": "Gone.pas" } ] }
                """));
            fs.WriteAllBytes(Dir + @"\Hello.pas", Encoding.UTF8.GetBytes("program Hello;\nbegin\nend.\n"));
            fs.WriteAllBytes(Dir + @"\Unit1.pas", Encoding.UTF8.GetBytes("unit Unit1;\ninterface\nimplementation\nend.\n"));
            fs.WriteAllBytes(Dir + @"\Unit2.pas", Encoding.UTF8.GetBytes("unit Unit2;\ninterface\nimplementation\nend.\n"));

            var recording = new RecordingFileSystem(fs)
            {
                FailWith = call =>
                {
                    if (!_armed || Environment.CurrentManagedThreadId == _uiThread || !Listed.Any(f => call == $@"FileExists {Dir}\{f}"))
                        return null;
                    if (_failScan)
                        return new IOException("The device is not ready.");
                    if (Interlocked.Increment(ref _probes) == 1)
                    {
                        _scanning.TrySetResult();
                        _release.Wait(cancellation);
                    }

                    return null;
                },
            };
            Services = TestComposition.Build(o => o.FileSystem = recording);
        }

        public IdeServices Services { get; }

        public ProjectManagerViewModel Pm => Services.ProjectManager;

        /// <summary>Questions about listed files asked off the UI thread since the held check started.</summary>
        public int BackgroundProbes => Volatile.Read(ref _probes);

        public void Release() => _release.Set();

        /// <summary>Opens the project and runs one more check whose scan fails with an <see cref="IOException"/>.</summary>
        public async Task FailNextCheckAsync()
        {
            Assert.True(await Services.Projects.OpenAsync(ProjectPath));
            await Pm.MissingFileChecks;
            Assert.True(Pm.Find("Gone.pas")!.IsMissing);

            _failScan = true;
            _armed = true;
            Pm.OnWindowActivated();
            await Pm.MissingFileChecks;
        }

        /// <summary>
        /// Opens the project, starts one more check and, once that check is held in its scan, runs
        /// <paramref name="whileHeld"/>. The scan stays held afterwards; only a failed step lets it go, so that the test
        /// UI thread can settle the check that step left pending and the failure is reported, not a timeout.
        /// </summary>
        public async Task OpenAndHoldAsync(Func<Task> whileHeld)
        {
            Assert.True(await Services.Projects.OpenAsync(ProjectPath));
            await Pm.MissingFileChecks;
            Assert.True(Pm.Find("Gone.pas")!.IsMissing);

            _armed = true;
            Pm.OnWindowActivated(); // IDE-PM-103
            _heldScan = Pm.MissingFileScan;
            try
            {
                await CurrentTest.Deadline.WaitAsync(_scanning.Task, "the missing-file scan on its background thread");
                Assert.True(Pm.IsCheckingMissingFiles);
                Assert.False(Pm.MissingFileChecks.IsCompleted);
                await whileHeld();
            }
            catch
            {
                Release();
                throw;
            }
        }

        /// <summary>Lets the held scan go and returns once its thread has left the file system.</summary>
        public async Task ReleaseAndWaitForScanAsync()
        {
            Release();
            await CurrentTest.Deadline.WaitAsync(_heldScan, "the end of the held scan");
        }
    }

    /// <summary>
    /// The guard of I-339: closing the project leaves no pending missing-file check. The UI thread of the test then
    /// ends while the scan is still inside the file system (a late thread pool): the scan stops at the next file and
    /// posts nothing to the ended UI thread — the work that used to fail whichever test ended next.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task ClosedProjectLeavesNoPendingCheck()
    {
        HeldScan? held = null;
        try
        {
            await TestUiThread.RunAsync(async () =>
            {
                var h = held = new HeldScan();
                await h.OpenAndHoldAsync(async () =>
                {
                    var dropped = h.Pm.MissingFileChecks;

                    Assert.True(await h.Services.Projects.CloseAsync());

                    // Nothing pending the moment the project is closed, although the scan has not returned yet.
                    Assert.True(h.Pm.MissingFileChecks.IsCompleted);
                    Assert.True(dropped.IsCompleted);
                    Assert.False(h.Pm.IsCheckingMissingFiles);
                    Assert.False(h.Pm.HasProject);
                });
            });

            await held!.ReleaseAndWaitForScanAsync();
            Assert.Equal(1, held.BackgroundProbes); // the question it was held in was its last
            Assert.Empty(TestUiThread.TakeLatePosts(nameof(MissingFileCheckLifetimeTests) + "." + nameof(ClosedProjectLeavesNoPendingCheck)));
        }
        finally
        {
            held?.Release();
        }
    }

    /// <summary>
    /// Disposing the view model drops the check in flight — the end of a view model built outside a composition, and
    /// of every window — and no check starts afterwards; the scan posts nothing to the UI thread that ended meanwhile.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task DisposedProjectManagerLeavesNoPendingCheck()
    {
        HeldScan? held = null;
        try
        {
            await TestUiThread.RunAsync(async () =>
            {
                var h = held = new HeldScan();
                await h.OpenAndHoldAsync(() =>
                {
                    h.Pm.Dispose();

                    Assert.True(h.Pm.MissingFileChecks.IsCompleted);
                    Assert.False(h.Pm.IsCheckingMissingFiles);

                    // Neither a main-window activation nor an explicit request starts a check on the disposed view model.
                    h.Pm.OnWindowActivated();
                    Assert.True(h.Pm.CheckMissingFilesAsync().IsCompleted);
                    Assert.True(h.Pm.MissingFileChecks.IsCompleted);
                    Assert.False(h.Pm.IsCheckingMissingFiles);
                    return Task.CompletedTask;
                });
            });

            await held!.ReleaseAndWaitForScanAsync();
            Assert.Equal(1, held.BackgroundProbes);
            Assert.Empty(TestUiThread.TakeLatePosts(nameof(MissingFileCheckLifetimeTests) + "." + nameof(DisposedProjectManagerLeavesNoPendingCheck)));
        }
        finally
        {
            held?.Release();
        }
    }

    /// <summary>
    /// A newer check (main-window activation) replaces the one in flight: the held scan stops, only the newer result is
    /// applied, and awaiting the pending checks waits for it.
    /// </summary>
    [Fact(Timeout = 30000)]
    public Task NewerCheckReplacesTheOneInFlight() => TestUiThread.RunAsync(NewerCheckReplacesTheOneInFlightOnUiThread);

    private static async Task NewerCheckReplacesTheOneInFlightOnUiThread()
    {
        var held = new HeldScan();
        try
        {
            await held.OpenAndHoldAsync(async () =>
            {
                var first = held.Pm.MissingFileChecks;
                File(held, "Gone.pas"); // back on disk: only the newer check can see it

                held.Pm.OnWindowActivated();

                Assert.True(first.IsCompleted); // dropped
                Assert.True(held.Pm.IsCheckingMissingFiles); // the newer one runs
                await held.Pm.MissingFileChecks;
                Assert.False(held.Pm.IsCheckingMissingFiles);
                Assert.False(held.Pm.Find("Gone.pas")!.IsMissing);
                Assert.Null(held.Pm.MissingFilesBar);
                Assert.Equal(1 + Listed.Length, held.BackgroundProbes); // the held question and the newer scan's four
            });

            await held.ReleaseAndWaitForScanAsync();
            Assert.Equal(1 + Listed.Length, held.BackgroundProbes); // the held scan asked nothing more
            Assert.True(held.Pm.MissingFileChecks.IsCompleted);
            Assert.False(held.Pm.Find("Gone.pas")!.IsMissing);
        }
        finally
        {
            held.Release();
        }
    }

    /// <summary>
    /// A scan that fails in the file system comes back to the UI thread as before: one warning in the log, no file
    /// row marked missing, the check over.
    /// </summary>
    [Fact(Timeout = 30000)]
    public Task FailedScanIsLoggedAndMarksNothingMissing() => TestUiThread.RunAsync(FailedScanIsLoggedAndMarksNothingMissingOnUiThread);

    private static async Task FailedScanIsLoggedAndMarksNothingMissingOnUiThread()
    {
        var held = new HeldScan();
        var log = (RecordingLog)held.Services.Log;
        Assert.DoesNotContain(log.Lines, l => l.Contains("missing-file check failed", StringComparison.Ordinal));

        await held.FailNextCheckAsync();

        Assert.Single(log.Lines, l => l == "WARN project: missing-file check failed");
        Assert.False(held.Pm.IsCheckingMissingFiles);
        Assert.True(held.Pm.MissingFileChecks.IsCompleted);
        Assert.False(held.Pm.Find("Gone.pas")!.IsMissing);
        Assert.Null(held.Pm.MissingFilesBar);
    }

    private static void File(HeldScan held, string name) =>
        held.Services.FileSystem.WriteAllBytes(Dir + @"\" + name, Encoding.UTF8.GetBytes("unit Gone;\ninterface\nimplementation\nend.\n"));
}
