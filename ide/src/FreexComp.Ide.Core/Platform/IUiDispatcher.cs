namespace FreexComp.Ide.Core.Platform;

public enum UiPriority
{
    /// <summary>Ordinary UI work.</summary>
    Normal,

    /// <summary>After pending input and rendering (e.g. coalesced CanExecute re-query, IDE-ARCH-044).</summary>
    Background,
}

/// <summary>The single UI thread (IDE-ARCH-080), without Avalonia types.</summary>
public interface IUiDispatcher
{
    /// <summary>True when called on the UI thread.</summary>
    bool CheckAccess();

    /// <summary>Queues <paramref name="action"/> on the UI thread; may be called from any thread.</summary>
    void Post(Action action, UiPriority priority = UiPriority.Normal);
}
