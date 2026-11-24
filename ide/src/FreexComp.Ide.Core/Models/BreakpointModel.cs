using FreexComp.Debugger;

namespace FreexComp.Ide.Core.Models;

/// <summary>
/// A source breakpoint as stored in the session (IDE-ARCH-077): the absolute path plus the members of the debugger's
/// <see cref="BreakpointSpec"/> without its id. <paramref name="HitCount"/> null = stop on every hit.
/// </summary>
public sealed record BreakpointInfo(string Path, int Line, bool Enabled = true, string? Condition = null, HitCountSpec? HitCount = null,
    string? LogMessage = null);

/// <summary>
/// One source breakpoint of the IDE (tool-windows.md §10, the debug-session interface §6.1): the user's definition and,
/// during a debug session, what the debugger reported for it. Changed only by <c>IBreakpointService</c>.
/// </summary>
public sealed class BreakpointModel
{
    /// <summary>The largest pass count of the Breakpoint dialog (dialogs.md §19).</summary>
    public const int MaxPassCount = 1_000_000;

    internal BreakpointModel(string path, int line)
    {
        Path = path;
        Line = line;
        StoredLine = line;
    }

    /// <summary>Absolute path of the source file.</summary>
    public string Path { get; }

    /// <summary>
    /// The 1-based line of the gutter marker (in an open document: where its anchor is now). During a debug session this
    /// is the line the debugger bound the breakpoint to (DBG-205); otherwise it equals <see cref="StoredLine"/>.
    /// </summary>
    public int Line { get; internal set; }

    /// <summary>
    /// The 1-based line of the user's definition, which the project session stores: a debug session does not change it
    /// (DBG-205), edits of an open document do.
    /// </summary>
    public int StoredLine { get; internal set; }

    public bool Enabled { get; internal set; } = true;

    /// <summary>The condition expression; null or empty = none.</summary>
    public string? Condition { get; internal set; }

    public HitCountSpec? HitCount { get; internal set; }

    /// <summary>Not editable in Phase 6; kept as read from the session.</summary>
    public string? LogMessage { get; internal set; }

    /// <summary>The id of the current debug session (1, 2, … in list order); 0 outside a session.</summary>
    public int Id { get; internal set; }

    /// <summary>The state the debugger reported; null outside a session.</summary>
    public BreakpointState? State { get; internal set; }

    /// <summary><c>BreakpointStatus.HitCount</c> of the current session; 0 outside a session.</summary>
    public int Hits { get; internal set; }

    /// <summary>The debugger's text for an invalid breakpoint, if any.</summary>
    public string? Message { get; internal set; }

    /// <summary>
    /// The number of hits to skip before stopping (the debug-session interface §6.1): n &gt; 0 ↔
    /// <c>HitCountSpec(GreaterOrEqual, n + 1)</c>; 0 for none and for the other modes.
    /// </summary>
    public int PassCount => PassCountOf(HitCount);

    public bool IsConditional => Condition is { Length: > 0 } || HitCount is not null;

    public BreakpointInfo ToInfo() => new(Path, StoredLine, Enabled, Condition, HitCount, LogMessage);

    /// <summary>The debugger's view of this breakpoint with the session id.</summary>
    public BreakpointSpec ToSpec() => new()
    {
        Id = Id,
        Line = Line,
        Enabled = Enabled,
        Condition = string.IsNullOrEmpty(Condition) ? null : Condition,
        HitCount = HitCount,
        LogMessage = LogMessage,
    };

    public static int PassCountOf(HitCountSpec? hitCount) =>
        hitCount is { Mode: HitCountMode.GreaterOrEqual, Value: > 1 } spec ? spec.Value - 1 : 0;

    /// <summary>Pass count n → the stored hit count: 0 ↔ none, n ↔ <c>GreaterOrEqual, n + 1</c>.</summary>
    public static HitCountSpec? HitCountOf(int passCount) =>
        passCount > 0 ? new HitCountSpec(HitCountMode.GreaterOrEqual, passCount + 1) : null;
}
