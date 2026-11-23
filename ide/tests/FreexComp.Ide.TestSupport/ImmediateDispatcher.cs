using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// <see cref="IUiDispatcher"/> that runs posted work inline; the test thread is the UI thread. I-208: on a
/// <see cref="TestUiThread"/>, work posted from another thread (a worker result) is queued to that UI thread instead, as
/// the IDE's dispatcher does, so it never runs at the same time as the test's own steps. I-209: its UI thread is the
/// test UI thread it was created on, else the first test UI thread that uses it (a composition built in a test class
/// constructor), else the creating thread; a post from any other thread with no test UI thread to queue to throws instead
/// of running inline there, so a test combining a background worker with this dispatcher must run on
/// <see cref="TestUiThread"/>.
/// </summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private SynchronizationContext? _ui = TestUiThread.Current;

    public bool CheckAccess()
    {
        var ui = Volatile.Read(ref _ui);
        if (ui is null && TestUiThread.Current is { } current)
            ui = Interlocked.CompareExchange(ref _ui, current, null) ?? current; // adopted by the test UI thread
        return ui is null ? Environment.CurrentManagedThreadId == _owner : SynchronizationContext.Current == ui;
    }

    public void Post(Action action, UiPriority priority = UiPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
            action();
        else if (Volatile.Read(ref _ui) is { } ui)
            ui.Post(_ => action(), null);
        else
            throw new InvalidOperationException(
                "Cross-thread post to an ImmediateDispatcher outside a test UI thread; run the test on TestUiThread.RunAsync (I-209).");
    }
}
