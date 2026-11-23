using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Build;

/// <summary>How an Output line is coloured (UI-TW-502).</summary>
public enum OutputKind
{
    Normal,
    Error,
    Command,
    Ide,
    Success,
    Failure,
}

/// <summary>One line of an Output channel.</summary>
public sealed record OutputLine(string Text, OutputKind Kind);

/// <summary>Output channel ids (IDE-P3-070); <c>debugger</c> joins in P6, <c>designer</c> in P5.</summary>
public static class OutputChannels
{
    public const string Build = "build";
    public const string Program = "program";

    /// <summary>From P5: component metadata problems (DSG-TP-005, tool-palette.md "Output channel").</summary>
    public const string Designer = "designer";
}

/// <summary>
/// The Output channels (IDE-P3-070): append-only line buffers limited to <c>output.maxLines</c> (UI-TW-505).
/// <see cref="Append"/> may be called from any thread; lines reach the channels — and <see cref="Changed"/> is raised —
/// at most every 50 ms, from the <see cref="IClock"/> callback (the UI thread in production).
/// </summary>
public interface IOutputService
{
    IReadOnlyList<string> Channels { get; }

    /// <summary>Appends <paramref name="text"/> (one or more lines: CRLF, LF and CR each end a line; ANSI sequences removed).</summary>
    /// <exception cref="ArgumentException">Unknown channel.</exception>
    void Append(string channel, string text, OutputKind kind);

    /// <summary>The lines of a channel as last flushed (UI thread).</summary>
    IReadOnlyList<OutputLine> GetLines(string channel);

    /// <summary>Empties one channel, including what is still waiting for the next flush.</summary>
    void Clear(string channel);

    /// <summary>A channel's lines changed (argument: the channel id).</summary>
    event EventHandler<string>? Changed;
}

/// <inheritdoc cref="IOutputService"/>
public sealed class OutputService : IOutputService
{
    private static readonly TimeSpan FlushDelay = TimeSpan.FromMilliseconds(50);

    private readonly IClock _clock;
    private readonly ISettingsService _settings;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Channel> _channels;
    private readonly List<(string Channel, OutputLine Line)> _pending = [];
    private IDisposable? _flush;

    /// <param name="designer">P5: the <c>designer</c> channel exists (IDE-P3-070).</param>
    public OutputService(IClock clock, ISettingsService settings, bool designer = false)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(settings);
        _clock = clock;
        _settings = settings;
        Channels = designer ? [OutputChannels.Build, OutputChannels.Program, OutputChannels.Designer] : [OutputChannels.Build, OutputChannels.Program];
        _channels = Channels.ToDictionary(c => c, _ => new Channel(), StringComparer.Ordinal);
    }

    public IReadOnlyList<string> Channels { get; }

    public event EventHandler<string>? Changed;

    public void Append(string channel, string text, OutputKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (channel is null || !_channels.ContainsKey(channel))
            throw new ArgumentException($"Unknown output channel '{channel}'.", nameof(channel));
        var lines = OutputLineSplitter.SplitLines(OutputLineSplitter.StripAnsi(text));
        lock (_gate)
        {
            foreach (var line in lines)
                _pending.Add((channel, new OutputLine(line, kind)));
            _flush ??= _clock.Schedule(FlushDelay, Flush);
        }
    }

    public IReadOnlyList<OutputLine> GetLines(string channel)
    {
        var c = _channels[channel];
        return c.Truncated ? [Notice(c.Limit), .. c.Lines] : [.. c.Lines];
    }

    public void Clear(string channel)
    {
        var c = _channels[channel];
        lock (_gate)
            _pending.RemoveAll(p => p.Channel == channel);
        var had = c.Lines.Count > 0 || c.Truncated;
        c.Lines.Clear();
        c.Truncated = false;
        if (had)
            Changed?.Invoke(this, channel);
    }

    private void Flush()
    {
        List<(string Channel, OutputLine Line)> batch;
        lock (_gate)
        {
            batch = [.. _pending];
            _pending.Clear();
            _flush = null;
        }

        var limit = _settings.Get(SettingsCatalog.OutputMaxLines);
        var changed = new List<string>();
        foreach (var (channel, line) in batch)
        {
            var c = _channels[channel];
            c.Lines.Add(line);
            if (c.Lines.Count > limit)
            {
                // UI-TW-505: drop the oldest 10 % of the limit at a time.
                var chunk = Math.Max(1, limit / 10);
                c.Lines.RemoveRange(0, Math.Min(c.Lines.Count, Math.Max(chunk, c.Lines.Count - limit)));
                c.Truncated = true;
                c.Limit = limit;
            }

            if (!changed.Contains(channel))
                changed.Add(channel);
        }

        foreach (var channel in changed)
            Changed?.Invoke(this, channel);
    }

    private static OutputLine Notice(int limit) =>
        new(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Output_Truncated, limit), OutputKind.Ide);

    private sealed class Channel
    {
        public List<OutputLine> Lines { get; } = [];

        public bool Truncated { get; set; }

        public int Limit { get; set; }
    }
}

/// <summary>
/// IDE-P3-073: turns the chunks of one program pipe into lines. CRLF, LF and a lone CR each end a line (a CR at the end of a
/// chunk waits for the next one); ANSI escape sequences are removed; a partial last line is emitted after 100 ms without
/// further data (so prompts without a line break appear) and at <see cref="Complete"/>. Thread-safe; <c>onLine</c> runs
/// on the writing thread or in the <see cref="IClock"/> callback.
/// </summary>
public sealed partial class OutputLineSplitter(IClock clock, Action<string> onLine)
{
    private static readonly TimeSpan PartialDelay = TimeSpan.FromMilliseconds(100);

    private readonly Lock _gate = new();
    private readonly StringBuilder _partial = new();
    private bool _heldCr;
    private IDisposable? _timer;

    public void Write(string chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var lines = new List<string>();
        lock (_gate)
        {
            foreach (var ch in chunk)
            {
                if (_heldCr)
                {
                    _heldCr = false;
                    lines.Add(Take());
                    if (ch == '\n')
                        continue; // CRLF
                }

                if (ch == '\r')
                    _heldCr = true;
                else if (ch == '\n')
                    lines.Add(Take());
                else
                    _partial.Append(ch);
            }

            _timer?.Dispose();
            _timer = _partial.Length > 0 || _heldCr ? clock.Schedule(PartialDelay, FlushPartial) : null;
        }

        Emit(lines);
    }

    /// <summary>The pipe ended: whatever is left becomes a line.</summary>
    public void Complete()
    {
        string? rest;
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            rest = _partial.Length > 0 || _heldCr ? Take() : null;
            _heldCr = false;
        }

        if (rest is not null)
            onLine(rest);
    }

    /// <summary>Removes <c>ESC [ … final byte</c> sequences.</summary>
    public static string StripAnsi(string text) => text.Contains('\x1B', StringComparison.Ordinal) ? Ansi().Replace(text, "") : text;

    /// <summary>Complete text split on CRLF, LF and lone CR (a trailing line break adds no empty line).</summary>
    public static IReadOnlyList<string> SplitLines(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        return lines.Length > 1 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    private void FlushPartial()
    {
        string line;
        lock (_gate)
        {
            _timer = null;
            if (_partial.Length == 0 && !_heldCr)
                return;
            _heldCr = false;
            line = Take();
        }

        onLine(line);
    }

    private string Take()
    {
        var line = StripAnsi(_partial.ToString());
        _partial.Clear();
        return line;
    }

    private void Emit(List<string> lines)
    {
        foreach (var line in lines)
            onLine(line);
    }

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex Ansi();
}
