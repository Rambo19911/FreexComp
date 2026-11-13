using Avalonia.Threading;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Platform;

/// <summary>Wall clock whose timers run on the UI thread (IDE-ARCH-085).</summary>
public sealed class DispatcherClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return DispatcherTimer.RunOnce(callback, delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
    }
}
