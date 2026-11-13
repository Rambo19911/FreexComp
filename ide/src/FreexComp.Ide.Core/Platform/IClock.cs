namespace FreexComp.Ide.Core.Platform;

/// <summary>
/// Time and timers for Core (IDE-ARCH-085): all debouncing and delays go through this, so tests can advance
/// time with <c>ManualClock.Advance</c>.
/// </summary>
public interface IClock
{
    /// <summary>Current time; <see cref="DateTimeOffset.LocalDateTime"/> gives the local wall clock.</summary>
    DateTimeOffset Now { get; }

    /// <summary>Runs <paramref name="callback"/> once on the UI thread after <paramref name="delay"/>; dispose to cancel.</summary>
    IDisposable Schedule(TimeSpan delay, Action callback);
}
