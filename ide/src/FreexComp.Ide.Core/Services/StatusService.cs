using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Services;

/// <summary>
/// Minimal <see cref="IStatusService"/> for the composition (I-012): holds the transient message until its duration
/// elapses. Status-bar fields and progress arrive with I-027. UI thread only.
/// </summary>
public sealed class StatusService(IClock clock) : IStatusService
{
    private IDisposable? _expiry;

    public event EventHandler? Changed;

    /// <summary>The transient message shown in the message field, or null.</summary>
    public string? Message { get; private set; }

    public void ShowMessage(string text, TimeSpan duration)
    {
        _expiry?.Dispose();
        Message = text;
        Changed?.Invoke(this, EventArgs.Empty);
        _expiry = clock.Schedule(duration, () =>
        {
            _expiry = null;
            Message = null;
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }
}
