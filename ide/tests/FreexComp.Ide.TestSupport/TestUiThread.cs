using System.Reflection;
using System.Runtime.CompilerServices;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// I-208: a UI thread for Core tests. The test body runs on a dedicated thread with a single-thread synchronization
/// context, like the IDE's UI thread: awaits resume there, and an <see cref="ImmediateDispatcher"/> created on it runs work
/// posted from other threads (results of the code-model worker, applied through <c>IUiDispatcher.Post</c>) there too —
/// between the test's own steps, never at the same time. Without it such work ran on the posting pool thread, in
/// parallel with the test thread (a structure-view rebuild while a clock callback enumerated its rows).
/// I-210: before the test ends, the background work registered for it (<see cref="RegisterWork"/>: the code-model worker
/// of a <see cref="TestComposition"/>) is settled — awaited until idle and its results applied on this thread. Work posted
/// after the test ended never runs — not on the thread pool, where it ran unseen on the state of a finished test — but is
/// recorded under that test (<see cref="TakeLatePosts"/>); each test project fails its run when any test left such work
/// behind, so the test awaits the work it started instead.
/// </summary>
public static class TestUiThread
{
    private static readonly object LateGate = new();
    private static readonly List<(string Owner, string Work)> Late = [];
    private static readonly Dictionary<object, List<Work>> Registered = [];

    /// <summary>
    /// The id of the running test (set by the test project; null outside a test): work registered off a test UI thread,
    /// e.g. by a composition built in a test class constructor, is settled by the test UI threads that test runs.
    /// </summary>
    public static Func<string?> CurrentTest { get; set; } = static () => null;

    /// <summary>
    /// Registers background work that posts results to the test UI thread: before the test ends, while
    /// <paramref name="isIdle"/> is false, <paramref name="settleAsync"/> is awaited and its posts run. Registered for the
    /// caller's test UI thread, else the <see cref="CurrentTest"/>, else ignored (a test without test UI thread).
    /// </summary>
    public static void RegisterWork(Func<bool> isIdle, Func<Task> settleAsync)
    {
        ArgumentNullException.ThrowIfNull(isIdle);
        ArgumentNullException.ThrowIfNull(settleAsync);
        object? key = SynchronizationContext.Current as UiContext ?? (object?)CurrentTest();
        if (key is null)
            return;
        lock (Registered)
        {
            if (!Registered.TryGetValue(key, out var list))
                Registered[key] = list = [];
            list.Add(new Work(isIdle, settleAsync));
        }
    }

    /// <summary>Forgets the work registered under the test <paramref name="testId"/> (the test ended).</summary>
    public static void EndTest(string testId)
    {
        lock (Registered)
            Registered.Remove(testId);
    }

    /// <summary>The context of the test UI thread the caller runs on, or null on any other thread.</summary>
    public static SynchronizationContext? Current => SynchronizationContext.Current as UiContext;

    /// <summary>
    /// Completes on the caller's test UI thread once no posted work is queued there any more (I-209): the steps a real UI
    /// thread would have run before the next input, e.g. continuations of awaited worker tasks.
    /// </summary>
    public static async Task IdleAsync()
    {
        var context = SynchronizationContext.Current as UiContext
            ?? throw new InvalidOperationException("IdleAsync must be called on a test UI thread.");
        do
            await Task.Yield(); // queued behind everything posted so far
        while (context.HasPending);
    }

    /// <summary>
    /// Runs <paramref name="body"/> on a new test UI thread; completes as the body does (its exception, if any). The
    /// caller (<c>Class.Method</c>, from its source file and member) owns the work posted to the thread after the body.
    /// </summary>
    public static Task RunAsync(Func<Task> body, [CallerMemberName] string member = "", [CallerFilePath] string file = "")
    {
        ArgumentNullException.ThrowIfNull(body);
        var owner = Owner(file, member);
        var testId = CurrentTest();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var context = new UiContext(owner);
            SynchronizationContext.SetSynchronizationContext(context);
            Task task;
            try
            {
                task = RunAndSettleAsync(body, context, testId);
            }
            catch (Exception e)
            {
                task = Task.FromException(e);
            }

            task.ContinueWith(_ => context.Complete(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            context.RunUntilComplete();
            lock (Registered)
                Registered.Remove(context);
            if (task.IsFaulted)
                done.SetException(task.Exception!.InnerExceptions);
            else if (task.IsCanceled)
                done.SetCanceled();
            else
                done.SetResult();
        })
        {
            IsBackground = true,
            Name = "Test UI thread",
        };
        thread.Start();
        return done.Task;
    }

    private static async Task RunAndSettleAsync(Func<Task> body, UiContext context, string? testId)
    {
        try
        {
            await body();
        }
        finally
        {
            await SettleAsync(context, testId);
        }
    }

    /// <summary>
    /// Runs the posted work and awaits the registered work until all of it is idle and nothing is queued: a result applied
    /// here may start more work. Every registered worker posts its results before it reports idle.
    /// </summary>
    private static async Task SettleAsync(UiContext context, string? testId)
    {
        while (true)
        {
            do
                await Task.Yield(); // queued behind everything posted so far
            while (context.HasPending);

            List<Work> busy;
            lock (Registered)
            {
                busy = [.. new object?[] { context, testId }.OfType<object>()
                    .SelectMany(key => Registered.TryGetValue(key, out var list) ? list : [])
                    .Where(w => !w.IsIdle())];
            }

            if (busy.Count == 0 && !context.HasPending)
                return;
            foreach (var work in busy)
                await work.SettleAsync();
        }
    }

    /// <summary>Removes and returns the work posted after the end of <paramref name="owner"/> (<c>Class.Method</c>).</summary>
    public static IReadOnlyList<string> TakeLatePosts(string owner)
    {
        lock (LateGate)
        {
            var taken = Late.Where(l => l.Owner == owner).Select(l => l.Work).ToList();
            Late.RemoveAll(l => l.Owner == owner);
            return taken;
        }
    }

    /// <summary>Removes and returns all work posted after the end of its test, as <c>owner: work</c> lines.</summary>
    public static IReadOnlyList<string> TakeAllLatePosts()
    {
        lock (LateGate)
        {
            var taken = Late.Select(l => $"{l.Owner}: {l.Work}").ToList();
            Late.Clear();
            return taken;
        }
    }

    private static string Owner(string file, string member)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var dot = name.IndexOf('.', StringComparison.Ordinal); // a partial class file: BuildServiceTests.Build.cs
        return (dot < 0 ? name : name[..dot]) + "." + member;
    }

    /// <summary>
    /// The method a posted callback runs: an awaited continuation names its state machine, a dispatched action itself, a
    /// continuation task on the context's scheduler its delegate (read for this message only; the type name if hidden).
    /// </summary>
    private static string Describe(SendOrPostCallback callback, object? state)
    {
        if (state is Task task)
            state = typeof(Task).GetField("m_action", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(task) ?? state;
        Delegate work = state as Delegate ?? callback;
        var type = work.Target?.GetType() ?? work.Method.DeclaringType;
        return $"{type?.FullName}.{work.Method.Name}";
    }

    private sealed record Work(Func<bool> IsIdle, Func<Task> SettleAsync);

    /// <summary>Posted callbacks queue until the owning thread runs them; after the test they are recorded, never run.</summary>
    private sealed class UiContext(string owner) : SynchronizationContext
    {
        private readonly object _gate = new();
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
        private bool _completed;

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_gate)
            {
                if (!_completed)
                {
                    _queue.Enqueue((d, state));
                    Monitor.PulseAll(_gate);
                    return;
                }
            }

            lock (LateGate)
                Late.Add((owner, Describe(d, state))); // a late result after the test ended
        }

        public bool HasPending
        {
            get
            {
                lock (_gate)
                    return _queue.Count > 0;
            }
        }

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException("Send would block the owning thread.");

        public override SynchronizationContext CreateCopy() => this;

        public void Complete()
        {
            lock (_gate)
            {
                _completed = true;
                Monitor.PulseAll(_gate);
            }
        }

        /// <summary>Runs posted callbacks on the calling thread until <see cref="Complete"/> and the queue is empty.</summary>
        public void RunUntilComplete()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) item;
                lock (_gate)
                {
                    while (_queue.Count == 0 && !_completed)
                        Monitor.Wait(_gate);
                    if (_queue.Count == 0)
                        return;
                    item = _queue.Dequeue();
                }

                item.Callback(item.State);
            }
        }
    }
}
