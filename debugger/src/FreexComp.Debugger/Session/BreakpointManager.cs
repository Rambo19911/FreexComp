using FreexComp.Debugger.Dap;

namespace FreexComp.Debugger.Session;

/// <summary>
/// The breakpoint table of a session (DAP usage §4.1): per source file the complete set the IDE defined, with the
/// state the adapter reported. Used only by the session loop.
/// </summary>
internal sealed class BreakpointManager
{
    /// <summary>One IDE breakpoint and what is known about it.</summary>
    internal sealed class Entry(BreakpointSpec spec)
    {
        public BreakpointSpec Spec { get; } = spec;
        public BreakpointState State { get; set; }
        public int ActualLine { get; set; } = spec.Line;
        public int HitCount { get; set; }
        public string? Message { get; set; }

        /// <summary>The adapter's breakpoint id, once the adapter has answered for this breakpoint.</summary>
        public int? AdapterId { get; set; }

        public BreakpointStatus Status => new(Spec.Id, State, ActualLine, HitCount, Message);

        /// <summary>Disabled and invalid (duplicate) breakpoints are never sent (DBG-061).</summary>
        public bool IsSent => Spec.Enabled && State != BreakpointState.Invalid;

        public void SetUnbound(string? message)
        {
            State = BreakpointState.Pending;
            ActualLine = Spec.Line;
            Message = message;
        }
    }

    /// <summary>The breakpoints of one source file.</summary>
    internal sealed class Source(string path)
    {
        public string Path { get; } = path;
        public List<Entry> Entries { get; set; } = [];

        /// <summary>True while the adapter holds a non-empty set for this file (then an empty set must be sent once, DBG-063).</summary>
        public bool KnownToAdapter { get; set; }

        public IReadOnlyList<BreakpointStatus> Statuses => [.. Entries.Select(e => e.Status)];
    }

    private readonly Dictionary<string, Source> _sources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sources in the order they are sent during launch: ordinal-ignore-case path order (DBG-054).</summary>
    public IReadOnlyList<Source> Sources => [.. _sources.Values.OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Replaces the complete set of one file. Hit counters of ids that persist are kept; every enabled breakpoint
    /// starts <c>Pending</c> until the adapter answers; of two enabled breakpoints on one line the later wins.
    /// </summary>
    public Source Set(string path, IReadOnlyList<BreakpointSpec> specs)
    {
        if (!_sources.TryGetValue(path, out var source))
            _sources.Add(path, source = new Source(path));

        var previous = source.Entries;
        var entries = new List<Entry>(specs.Count);
        foreach (var spec in specs)
        {
            var entry = new Entry(spec) { State = spec.Enabled ? BreakpointState.Pending : BreakpointState.Disabled };
            if (previous.FirstOrDefault(e => e.Spec.Id == spec.Id) is { } old)
                entry.HitCount = old.HitCount;
            if (spec.Enabled && entries.LastOrDefault(e => e.IsSent && e.Spec.Line == spec.Line) is { } earlier)
            {
                earlier.State = BreakpointState.Invalid;
                earlier.Message = Messages.DuplicateBreakpoint;
            }

            entries.Add(entry);
        }

        source.Entries = entries;
        return source;
    }

    /// <summary>Forgets a file that has no breakpoints left and that the adapter no longer knows.</summary>
    public void DropIfEmpty(Source source)
    {
        if (source.Entries.Count == 0 && !source.KnownToAdapter)
            _sources.Remove(source.Path);
    }

    /// <summary>Response entries map to the sent entries by array index (DBG-062).</summary>
    public static void ApplyResponse(IReadOnlyList<Entry> sent, IReadOnlyList<DapBreakpoint> response)
    {
        for (var i = 0; i < sent.Count; i++)
        {
            if (i < response.Count)
                Apply(sent[i], response[i]);
            else
                sent[i].SetUnbound(Messages.NotBoundYet);
        }
    }

    /// <summary>A failed request makes every breakpoint that was sent <c>Invalid</c> with the error text (DBG-054).</summary>
    public static void ApplyFailure(IReadOnlyList<Entry> sent, string message)
    {
        foreach (var entry in sent)
        {
            entry.State = BreakpointState.Invalid;
            entry.ActualLine = entry.Spec.Line;
            entry.Message = message;
            entry.AdapterId = null;
        }
    }

    public static void Apply(Entry entry, DapBreakpoint reported)
    {
        entry.AdapterId = reported.Id ?? entry.AdapterId;
        if (reported.Verified)
        {
            entry.State = BreakpointState.Verified;
            entry.ActualLine = reported.Line is > 0 ? reported.Line.Value : entry.Spec.Line;
            entry.Message = null;
        }
        else
        {
            entry.SetUnbound(string.IsNullOrEmpty(reported.Message) ? Messages.NotBoundYet : reported.Message);
        }
    }

    public Entry? FindByAdapterId(int adapterId) =>
        _sources.Values.SelectMany(s => s.Entries).FirstOrDefault(e => e.IsSent && e.AdapterId == adapterId);

    /// <summary>The verified breakpoints bound to a location (used when a stop names no breakpoint ids, DBG-071).</summary>
    public IEnumerable<Entry> FindByLocation(string path, int line) =>
        _sources.TryGetValue(path, out var source)
            ? source.Entries.Where(e => e.IsSent && e.State == BreakpointState.Verified && e.ActualLine == line)
            : [];
}
