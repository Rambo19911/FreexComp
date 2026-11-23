using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests;

/// <summary>I-209: the test dispatcher never runs a cross-thread post inline on the posting (worker) thread.</summary>
public sealed class DispatcherTests
{
    [Fact(Timeout = 30000)]
    public async Task CrossThreadPostNeverRunsInlineOnPoolThread()
    {
        // On a test UI thread a worker's post is queued to that thread (I-208).
        await TestUiThread.RunAsync(async () =>
        {
            var dispatcher = new ImmediateDispatcher();
            var uiThread = Environment.CurrentManagedThreadId;
            var ran = new TaskCompletionSource<(int Thread, bool PoolThread)>(TaskCreationOptions.RunContinuationsAsynchronously);
            await Task.Run(() =>
            {
                Assert.False(dispatcher.CheckAccess());
                dispatcher.Post(() => ran.SetResult((Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread)));
            });
            var (thread, poolThread) = await ran.Task;
            Assert.Equal(uiThread, thread);
            Assert.False(poolThread);
        });

        // Elsewhere it refuses: a post from another thread throws instead of mutating state on that thread.
        var plain = new ImmediateDispatcher();
        var inline = false;
        Exception? refused = null;
        var worker = new Thread(() =>
        {
            try
            {
                Assert.False(plain.CheckAccess());
                plain.Post(() => inline = true);
            }
            catch (Exception e)
            {
                refused = e;
            }
        });
        worker.Start();
        worker.Join();

        Assert.IsType<InvalidOperationException>(refused);
        Assert.False(inline);
        Assert.True(plain.CheckAccess()); // the creating thread still runs posts inline
        plain.Post(() => inline = true);
        Assert.True(inline);

        // A dispatcher built before the test UI thread (a test class constructor) is adopted by it on first use.
        var early = new ImmediateDispatcher();
        await TestUiThread.RunAsync(async () =>
        {
            Assert.True(early.CheckAccess());
            var uiThread = Environment.CurrentManagedThreadId;
            var ran = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            await Task.Run(() => early.Post(() => ran.SetResult(Environment.CurrentManagedThreadId)));
            Assert.Equal(uiThread, await ran.Task);
        });
    }

    /// <summary>
    /// I-210: work posted to a test UI thread after its test ended (a late worker result) never runs — before, it went to
    /// the thread pool and ran there, unseen, on the state of a finished test — and is recorded under the test that left it
    /// behind, so the run can fail on it (<see cref="TestUiThread.TakeLatePosts"/>).
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task LatePostAfterDisposeIsNotRunOnPool()
    {
        SynchronizationContext? ended = null;
        ImmediateDispatcher? dispatcher = null;
        await TestUiThread.RunAsync(() =>
        {
            ended = TestUiThread.Current;
            dispatcher = new ImmediateDispatcher();
            return Task.CompletedTask;
        });

        var ran = 0;
        await Task.Run(() =>
        {
            ended!.Post(_ => Interlocked.Increment(ref ran), null);
            dispatcher!.Post(LateDispatcherWork);
        }, TestContext.Current.CancellationToken);

        var late = TestUiThread.TakeLatePosts(nameof(DispatcherTests) + "." + nameof(LatePostAfterDisposeIsNotRunOnPool));
        Assert.Equal(2, late.Count); // both recorded the moment they were posted, neither queued anywhere
        Assert.Contains(late, l => l.Contains(nameof(LateDispatcherWork), StringComparison.Ordinal)); // names the posted work
        Assert.Empty(TestUiThread.TakeLatePosts(nameof(DispatcherTests) + "." + nameof(LatePostAfterDisposeIsNotRunOnPool)));
        Assert.Equal(0, Volatile.Read(ref ran));
        Assert.False(s_lateDispatcherWorkRan);
    }

    /// <summary>
    /// I-210: background work registered for the test (a composition's code-model worker) is settled before the test
    /// ends: a request still running when the body returns has its result applied on the test UI thread, not dropped late.
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task RegisteredWorkIsSettledBeforeTheTestEnds()
    {
        var applied = new List<int>();
        await TestUiThread.RunAsync(() =>
        {
            var worker = new FreexComp.Ide.Core.CodeModel.CodeModelWorker();
            TestUiThread.RegisterWork(() => worker.IsIdle, () => worker.RunAsync("settle", static _ => 0));
            var gate = new ManualResetEventSlim(); // not disposed: the worker may still be leaving Wait
            _ = ApplyAsync(worker, gate, applied);
            Assert.False(worker.IsIdle); // still running when the body returns
            gate.Set();
            return Task.CompletedTask;
        });

        Assert.Equal([1, 2], applied); // the result and the request it started, both applied before RunAsync completed
        Assert.Empty(TestUiThread.TakeLatePosts(nameof(DispatcherTests) + "." + nameof(RegisteredWorkIsSettledBeforeTheTestEnds)));

        static async Task ApplyAsync(FreexComp.Ide.Core.CodeModel.CodeModelWorker worker, ManualResetEventSlim gate, List<int> applied)
        {
            applied.Add(await worker.RunAsync("first", _ =>
            {
                gate.Wait(TestContext.Current.CancellationToken);
                return 1;
            }));
            applied.Add(await worker.RunAsync("second", static _ => 2));
        }
    }

    private static bool s_lateDispatcherWorkRan;

    private static void LateDispatcherWork() => s_lateDispatcherWorkRan = true;
}
