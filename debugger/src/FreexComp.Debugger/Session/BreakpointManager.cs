using System.Text;
using FreexComp.Debugger.Dap;

namespace FreexComp.Debugger.Session;

/// <summary>
/// The breakpoint table of a session (DAP usage §4.1): per source file the complete set the IDE defined, with the
/// state the adapter reported; and what an arrival at a breakpoint comes to (features §2.2): conditions, hit counts
/// and log points are evaluated here, never by the adapter. Used only by the session loop.
/// </summary>
internal sealed class BreakpointManager
{
    /// <summary>One IDE breakpoint and what is known about it.</summary>
    internal sealed class Entry(BreakpointSpec spec, string sourcePath = "")
    {
        public BreakpointSpec Spec { get; } = spec;

        /// <summary>The source file the breakpoint belongs to.</summary>
        public string SourcePath { get; } = sourcePath;
        public BreakpointState State { get; set; }
        public int ActualLine { get; set; } = spec.Line;

        /// <summary>Arrivals at the location in this session, whether they stopped or not (DBG-410).</summary>
        public int HitCount { get; set; }

        /// <summary>Arrivals that passed the condition: what a hit-count rule counts (DBG-212).</summary>
        public int Passes { get; set; }
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

        /// <summary>The temporary run-to-line breakpoint, sent after the user's breakpoints of the file (DBG-065); not one of <see cref="Entries"/>.</summary>
        public Entry? Temporary { get; set; }

        /// <summary>What a <c>setBreakpoints</c> request for the file carries, in order.</summary>
        public List<Entry> Sent => [.. Entries.Where(e => e.IsSent), .. Temporary is { } temporary ? new[] { temporary } : []];

        /// <summary>True while the adapter holds a non-empty set for this file (then an empty set must be sent once, DBG-063).</summary>
        public bool KnownToAdapter { get; set; }

        public IReadOnlyList<BreakpointStatus> Statuses => [.. Entries.Select(e => e.Status)];
    }

    private readonly Dictionary<string, Source> _sources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sources in the order they are sent during launch: ordinal-ignore-case path order (DBG-054).</summary>
    public IReadOnlyList<Source> Sources => [.. _sources.Values.OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Replaces the complete set of one file. Counters of ids that persist are kept (DBG-207, DBG-214); every enabled
    /// breakpoint starts <c>Pending</c> until the adapter answers; of two enabled breakpoints on one line the later
    /// wins. A hit count below 1 makes the breakpoint <c>Invalid</c> (DBG-212), and so does, when
    /// <paramref name="conditionSyntaxError"/> is given (a session is running), a condition that does not parse
    /// (DBG-211); an invalid breakpoint is never sent.
    /// </summary>
    /// <param name="path">Normalised source path.</param>
    /// <param name="specs">The file's complete set.</param>
    /// <param name="conditionSyntaxError">The syntax error message of a condition, or null when it parses.</param>
    public Source Set(string path, IReadOnlyList<BreakpointSpec> specs, Func<string, string?>? conditionSyntaxError = null)
    {
        var source = GetOrAdd(path);
        var previous = source.Entries;
        var entries = new List<Entry>(specs.Count);
        foreach (var spec in specs)
        {
            var entry = new Entry(spec, path) { State = spec.Enabled ? BreakpointState.Pending : BreakpointState.Disabled };
            if (previous.FirstOrDefault(e => e.Spec.Id == spec.Id) is { } old)
                (entry.HitCount, entry.Passes) = (old.HitCount, old.Passes);
            if (spec.Enabled)
            {
                var invalid = spec.HitCount is { Value: < 1 } ? Messages.HitCountTooSmall
                    : HasCondition(spec) ? conditionSyntaxError?.Invoke(spec.Condition!)
                    : null;
                if (invalid is not null)
                    (entry.State, entry.Message) = (BreakpointState.Invalid, invalid);
            }

            if (entry.IsSent && entries.LastOrDefault(e => e.IsSent && e.Spec.Line == spec.Line) is { } earlier)
            {
                earlier.State = BreakpointState.Invalid;
                earlier.Message = Messages.DuplicateBreakpoint;
            }

            entries.Add(entry);
        }

        source.Entries = entries;
        return source;
    }

    /// <summary>The table entry of a file, made on first use (a run to a line of a file without breakpoints).</summary>
    public Source GetOrAdd(string path)
    {
        if (!_sources.TryGetValue(path, out var source))
            _sources.Add(path, source = new Source(path));
        return source;
    }

    /// <summary>Forgets a file that has no breakpoints left and that the adapter no longer knows.</summary>
    public void DropIfEmpty(Source source)
    {
        if (source.Entries.Count == 0 && source.Temporary is null && !source.KnownToAdapter)
            _sources.Remove(source.Path);
    }

    private static bool HasCondition(BreakpointSpec spec) => !string.IsNullOrWhiteSpace(spec.Condition);

    // ---- arrival processing (features §2.2) ----

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

    /// <summary>What an arrival at one or more breakpoints comes to.</summary>
    /// <param name="Stoppers">The breakpoints that yield a stop; empty: the arrival is resolved internally.</param>
    /// <param name="Description">The description of the stop when a condition failed to evaluate; null otherwise.</param>
    /// <param name="Outputs">Log-point lines and condition-error lines to raise, in order.</param>
    internal sealed record ArrivalOutcome(IReadOnlyList<Entry> Stoppers, string? Description, IReadOnlyList<OutputEventArgs> Outputs);

    /// <summary>
    /// An arrival at the given breakpoints, in ascending id order: count it; a condition that is false skips, one that
    /// cannot be evaluated stops (DBG-211); a hit-count rule that is not satisfied skips; a log point logs and skips;
    /// anything else stops. The stop is public if any breakpoint stops.
    /// </summary>
    /// <param name="hit">The IDE breakpoints the stop names.</param>
    /// <param name="condition">Evaluates a condition in the top frame; faults with an expression error.</param>
    /// <param name="logText">The text an expression contributes to a log message; faults with an expression error.</param>
    public static async Task<ArrivalOutcome> EvaluateArrivalAsync(
        IReadOnlyList<Entry> hit, Func<string, Task<bool>> condition, Func<string, Task<string>> logText)
    {
        var stoppers = new List<Entry>();
        var outputs = new List<OutputEventArgs>();
        string? description = null;
        foreach (var entry in hit.OrderBy(e => e.Spec.Id))
        {
            var spec = entry.Spec;
            entry.HitCount++;
            if (HasCondition(spec))
            {
                bool holds;
                try
                {
                    holds = await condition(spec.Condition!).ConfigureAwait(false);
                }
                catch (DebuggerException e) when (IsExpressionError(e))
                {
                    // Never silently skipped.
                    description ??= Messages.ConditionError(e.Message);
                    outputs.Add(new OutputEventArgs(OutputCategory.Debugger,
                        Messages.BreakpointMessage(Path.GetFileName(entry.SourcePath), entry.ActualLine, e.Message)));
                    stoppers.Add(entry);
                    continue;
                }

                if (!holds)
                    continue;
            }

            entry.Passes++;
            if (spec.HitCount is { } rule && !IsSatisfied(rule, entry.Passes))
                continue;
            if (spec.LogMessage is { } message)
            {
                outputs.Add(new OutputEventArgs(OutputCategory.LogPoint, await InterpolateAsync(message, logText).ConfigureAwait(false) + "\n"));
                continue;
            }

            stoppers.Add(entry);
        }

        return new ArrivalOutcome(stoppers, description, outputs);
    }

    private static bool IsSatisfied(HitCountSpec rule, int passes) => rule.Mode switch
    {
        HitCountMode.Equal => passes == rule.Value,
        HitCountMode.GreaterOrEqual => passes >= rule.Value,
        _ => rule.Value >= 1 && passes % rule.Value == 0,
    };

    private static bool IsExpressionError(DebuggerException e) =>
        e.Code is DebugErrorCode.ExpressionSyntax or DebugErrorCode.ExpressionEvaluation or DebugErrorCode.MemoryUnreadable;

    /// <summary>
    /// A log message (DBG-213): text outside braces is literal; <c>{expr}</c> is replaced by the expression's text;
    /// <c>{{</c> and <c>}}</c> are the braces themselves; an unterminated <c>{</c> is literal; an expression that
    /// fails is replaced by <c>&lt;error: message&gt;</c>.
    /// </summary>
    public static async Task<string> InterpolateAsync(string message, Func<string, Task<string>> logText)
    {
        var result = new StringBuilder(message.Length);
        for (var i = 0; i < message.Length; i++)
        {
            var c = message[i];
            if (c is '{' or '}' && i + 1 < message.Length && message[i + 1] == c)
            {
                result.Append(c);
                i++;
            }
            else if (c == '{' && message.IndexOf('}', i + 1) is var close and > 0)
            {
                var expression = message[(i + 1)..close];
                try
                {
                    result.Append(await logText(expression).ConfigureAwait(false));
                }
                catch (DebuggerException e) when (IsExpressionError(e))
                {
                    result.Append(Messages.LogError(e.Message));
                }

                i = close;
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>The temporary run-to-line breakpoint with this adapter id, if that is what the id names.</summary>
    public Entry? FindTemporary(int adapterId) =>
        _sources.Values.Select(s => s.Temporary).FirstOrDefault(t => t is not null && t.AdapterId == adapterId);

    public Entry? FindByAdapterId(int adapterId) =>
        _sources.Values.SelectMany(s => s.Entries).FirstOrDefault(e => e.IsSent && e.AdapterId == adapterId);

    /// <summary>The verified breakpoints bound to a location (used when a stop names no breakpoint ids, DBG-071).</summary>
    public IEnumerable<Entry> FindByLocation(string path, int line) =>
        _sources.TryGetValue(path, out var source)
            ? source.Entries.Where(e => e.IsSent && e.State == BreakpointState.Verified && e.ActualLine == line)
            : [];
}
