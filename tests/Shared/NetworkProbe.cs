using System.Collections.Concurrent;
using System.Diagnostics.Tracing;

namespace FreexComp.Tests.Shared;

/// <summary>
/// NET-020: records every event of the runtime's networking event sources while alive. <see cref="Events"/> empty ⇔ no
/// managed socket was created, no name resolved, no HTTP request or TLS handshake started in this process meanwhile.
/// Linked into the lead's test projects (policy and end-to-end).
/// </summary>
internal sealed class NetworkProbe : EventListener
{
    private static readonly HashSet<string> Sources = new(StringComparer.Ordinal)
    {
        "System.Net.Sockets",
        "System.Net.NameResolution",
        "System.Net.Http",
        "System.Net.Security",
        "Private.InternalDiagnostics.System.Net.Sockets",
    };

    private readonly ConcurrentQueue<(string Source, string Event)> _events = new();
    private volatile bool _recording;

    public NetworkProbe() => _recording = true;

    /// <summary>(source name, event name) of every event recorded so far.</summary>
    public IReadOnlyList<(string Source, string Event)> Events => [.. _events];

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        // Called for sources created before and after this listener; the constructor of the base class may call it
        // before this instance's fields are set, so only the source name decides here.
        if (Sources.Contains(eventSource.Name))
            EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (_recording && eventData.EventSource is { } source && Sources.Contains(source.Name))
            _events.Enqueue((source.Name, eventData.EventName ?? eventData.EventId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    public override void Dispose()
    {
        _recording = false;
        base.Dispose();
    }
}
