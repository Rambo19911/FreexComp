using Avalonia.Threading;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Platform;

/// <summary>
/// Wall clock whose timers run on the UI thread (IDE-ARCH-085). <see cref="Schedule"/> may be called from any thread
/// (I-173: <c>SharingRetry</c> waits on the thread pool): a dispatcher timer only fires when it is started on the UI
/// thread, so a call from elsewhere posts the start there; disposing before it started cancels it.
/// </summary>
public sealed class DispatcherClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var due = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        if (Dispatcher.UIThread.CheckAccess())
            return DispatcherTimer.RunOnce(callback, due);

        var timer = new PostedTimer();
        Dispatcher.UIThread.Post(() => timer.Start(callback, due));
        return timer;
    }

    /// <summary>A timer started on the UI thread for a caller on another thread.</summary>
    private sealed class PostedTimer : IDisposable
    {
        private readonly Lock _gate = new();
        private IDisposable? _timer;
        private bool _disposed;

        public void Start(Action callback, TimeSpan due)
        {
            lock (_gate)
            {
                if (!_disposed)
                    _timer = DispatcherTimer.RunOnce(() => Fire(callback), due);
            }
        }

        private void Fire(Action callback)
        {
            lock (_gate)
            {
                if (_disposed)
                    return; // disposed on another thread after it fell due
            }

            callback();
        }

        public void Dispose()
        {
            IDisposable? timer;
            lock (_gate)
            {
                _disposed = true;
                timer = _timer;
                _timer = null;
            }

            if (timer is null)
                return;
            if (Dispatcher.UIThread.CheckAccess())
                timer.Dispose();
            else
                Dispatcher.UIThread.Post(timer.Dispose);
        }
    }
}
