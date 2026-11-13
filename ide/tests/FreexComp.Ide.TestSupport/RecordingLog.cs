using FreexComp.Ide.Core.Logging;

namespace FreexComp.Ide.TestSupport;

/// <summary><see cref="ILog"/> that keeps every entry as <c>"LEVEL category: message"</c>. Thread-safe.</summary>
public sealed class RecordingLog : ILog
{
    private readonly object _gate = new();
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
            {
                return [.. _lines];
            }
        }
    }

    public bool IsEnabled(LogLevel level) => true;

    public void Write(LogLevel level, string category, string message, Exception? exception = null)
    {
        var text = level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO",
            LogLevel.Warning => "WARN",
            _ => "ERROR",
        };

        lock (_gate)
        {
            _lines.Add($"{text} {category}: {message}");
        }
    }
}
