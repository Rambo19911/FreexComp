using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// <see cref="IUiDispatcher"/> that runs posted work inline; the test thread is the UI thread. I-208: created on a
/// <see cref="TestUiThread"/>, work posted from another thread (a worker result) is queued to that UI thread instead, as
/// the IDE's dispatcher does, so it never runs at the same time as the test's own steps.
/// </summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    private readonly SynchronizationContext? _ui = TestUiThread.Current;

    public bool CheckAccess() => _ui is null || SynchronizationContext.Current == _ui;

    public void Post(Action action, UiPriority priority = UiPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
            action();
        else
            _ui!.Post(_ => action(), null);
    }
}
