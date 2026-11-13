using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.TestSupport;

/// <summary><see cref="IStatusService"/> that records transient messages with their duration (they never expire).</summary>
public sealed class RecordingStatusService : IStatusService
{
    public event EventHandler? Changed;

    public List<(string Text, TimeSpan Duration)> Messages { get; } = [];

    public string? Message => Messages.Count == 0 ? null : Messages[^1].Text;

    public void ShowMessage(string text, TimeSpan duration)
    {
        Messages.Add((text, duration));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
