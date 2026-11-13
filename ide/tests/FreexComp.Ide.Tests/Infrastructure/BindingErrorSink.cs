using System.Text.RegularExpressions;
using Avalonia.Logging;

namespace FreexComp.Ide.Tests.Infrastructure;

/// <summary>
/// Avalonia log sink of the headless test application: records binding warnings and errors (IDE-ARCH-055) so that
/// <see cref="HeadlessHarness"/> can fail the test that produced them.
/// </summary>
public sealed partial class BindingErrorSink : ILogSink
{
    public static readonly BindingErrorSink Instance = new();

    private readonly object _gate = new();
    private readonly List<string> _entries = [];

    private BindingErrorSink()
    {
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Entries recorded since <paramref name="start"/> (a former <see cref="Count"/>).</summary>
    public IReadOnlyList<string> Since(int start)
    {
        lock (_gate)
        {
            return [.. _entries.Skip(start)];
        }
    }

    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area == LogArea.Binding;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Log(level, area, source, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (!IsEnabled(level, area))
        {
            return;
        }

        var index = 0;
        var text = Placeholder().Replace(messageTemplate, m => index < propertyValues.Length ? $"{propertyValues[index++]}" : m.Value);
        lock (_gate)
        {
            _entries.Add($"{level} {area}: {text} (source: {source})");
        }
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Placeholder();
}
