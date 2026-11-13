using Avalonia.Threading;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Platform;

/// <summary><see cref="IUiDispatcher"/> over Avalonia's UI-thread dispatcher (IDE-ARCH-080).</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action, UiPriority priority = UiPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);
        Dispatcher.UIThread.Post(action, priority == UiPriority.Background ? DispatcherPriority.Background : DispatcherPriority.Normal);
    }
}
