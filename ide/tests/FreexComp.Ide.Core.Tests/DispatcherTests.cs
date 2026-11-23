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
}
