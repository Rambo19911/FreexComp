namespace FreexComp.Debugger;

/// <summary>Source of an output chunk.</summary>
public enum OutputCategory { Stdout, Stderr, Debugger, LogPoint }

/// <summary>Output text, delivered as received.</summary>
public sealed record OutputEventArgs(OutputCategory Category, string Text);

/// <summary>A public state transition.</summary>
public sealed record StateChangedEventArgs(DebugState OldState, DebugState NewState);

/// <summary>A breakpoint changed its status.</summary>
public sealed record BreakpointChangedEventArgs(BreakpointStatus Status);

/// <summary>What happened to a thread.</summary>
public enum ThreadChange { Started, Exited }

/// <summary>A thread started or exited.</summary>
public sealed record ThreadChangedEventArgs(int ThreadId, ThreadChange Change);

/// <summary>A module was loaded or changed.</summary>
public sealed record ModuleEventArgs(string Name, string Path, bool HasSymbols);

/// <summary>Why a session ended.</summary>
public enum TerminationCause { Exited, TerminatedByUser, AdapterCrashed, LaunchFailed, Disposed }

/// <summary>The session ended; always the last event of a session.</summary>
public sealed record TerminatedEventArgs(TerminationCause Cause, int? ExitCode, string? Message);
