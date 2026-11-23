namespace FreexComp.Debugger;

/// <summary>The complete breakpoint set of one source file.</summary>
public sealed record SourceBreakpoints(string Path, IReadOnlyList<BreakpointSpec> Breakpoints);

/// <summary>One line breakpoint as the IDE defines it.</summary>
public sealed record BreakpointSpec
{
    /// <summary>Chosen by the IDE, unique within the session, greater than 0.</summary>
    public required int Id { get; init; }

    /// <summary>1-based line.</summary>
    public required int Line { get; init; }

    /// <summary>Disabled breakpoints are kept but never sent to the adapter.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Pascal expression; null or empty means no condition.</summary>
    public string? Condition { get; init; }

    /// <summary>Hit-count rule; null means none.</summary>
    public HitCountSpec? HitCount { get; init; }

    /// <summary>Non-null makes this a log point (does not stop).</summary>
    public string? LogMessage { get; init; }
}

/// <summary>How a hit count is compared.</summary>
public enum HitCountMode { Equal, GreaterOrEqual, Multiple }

/// <summary>A hit-count rule; <paramref name="Value"/> is at least 1.</summary>
public sealed record HitCountSpec(HitCountMode Mode, int Value);

/// <summary>State of a breakpoint in the session.</summary>
public enum BreakpointState { Pending, Verified, Invalid, Disabled }

/// <summary>Status of one breakpoint, identified by the IDE's id.</summary>
public sealed record BreakpointStatus(
    int Id, BreakpointState State, int ActualLine, int HitCount, string? Message);
