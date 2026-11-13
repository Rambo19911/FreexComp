using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.TestSupport;

/// <summary><see cref="IStatusService"/> that records transient messages with their duration.</summary>
public sealed class RecordingStatusService : IStatusService
{
    public List<(string Text, TimeSpan Duration)> Messages { get; } = [];

    public void ShowMessage(string text, TimeSpan duration) => Messages.Add((text, duration));
}
