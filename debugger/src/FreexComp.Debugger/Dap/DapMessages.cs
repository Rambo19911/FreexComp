using System.Text.Json;

namespace FreexComp.Debugger.Dap;

// Typed records for exactly the DAP JSON we send and consume (DAP usage specification). Property order is the
// order on the wire; names are camel-cased by DapJson; null properties are omitted; unknown incoming ones are ignored.

// ---- request arguments ----

/// <summary>Constant <c>initialize</c> arguments (DBG-050).</summary>
internal sealed record InitializeArguments
{
    public string ClientID { get; } = "freexcomp";
    public string ClientName { get; } = "FreexComp";
    public string AdapterID { get; } = "lldb-dap";
    public string Locale { get; } = "en-US";
    public bool LinesStartAt1 { get; } = true;
    public bool ColumnsStartAt1 { get; } = true;
    public string PathFormat { get; } = "path";
    public bool SupportsVariableType { get; } = true;
    public bool SupportsVariablePaging { get; } = false;
    /// <summary>True in terminal mode only (DBG-541).</summary>
    public bool SupportsRunInTerminalRequest { get; init; } = false;
    public bool SupportsMemoryReferences { get; } = true;
    public bool SupportsProgressReporting { get; } = false;
    public bool SupportsInvalidatedEvent { get; } = false;
    public bool SupportsMemoryEvent { get; } = false;
    public bool SupportsArgsCanBeInterpretedByShell { get; } = false;
    public bool SupportsStartDebuggingRequest { get; } = false;
    public bool SupportsANSIStyling { get; } = false;
}

/// <summary><c>launch</c> arguments (§3.1, DBG-057): no other property is ever sent.</summary>
internal sealed record LaunchArguments
{
    public required string Program { get; init; }
    public required IReadOnlyList<string> Args { get; init; }
    public required string Cwd { get; init; }
    /// <summary>Complete environment, or null (omitted: the debuggee inherits the adapter's environment).</summary>
    public IReadOnlyDictionary<string, string>? Env { get; init; }
    public required bool StopOnEntry { get; init; }
    public bool EnableAutoVariableSummaries { get; } = false;
    public bool EnableSyntheticChildDebugging { get; } = false;
    public bool DisplayExtendedBacktrace { get; } = false;
    public string CommandEscapePrefix { get; } = "`";
    public IReadOnlyList<string> InitCommands { get; } = ["settings set target.process.thread.step-avoid-regexp \"\""];
    public IReadOnlyList<IReadOnlyList<string>> SourceMap { get; } = [];
    /// <summary>The debuggee's stdin, stdout, stderr (null: not redirected); omitted when the adapter relays the output (DBG-058).</summary>
    public IReadOnlyList<string?>? Stdio { get; init; }
    /// <summary><c>externalTerminal</c> in terminal mode (DBG-542: the adapter asks for the console with <c>runInTerminal</c>); else omitted.</summary>
    public string? Console { get; init; }
}

internal sealed record DapSource
{
    public string? Name { get; init; }
    public string? Path { get; init; }
    public int? SourceReference { get; init; }
}

/// <summary>Only <c>line</c> is ever sent (DBG-060).</summary>
internal sealed record DapSourceBreakpoint(int Line);

internal sealed record SetBreakpointsArguments(DapSource Source, IReadOnlyList<DapSourceBreakpoint> Breakpoints)
{
    public bool SourceModified { get; } = false;
}

internal sealed record DapFunctionBreakpoint(string Name);

internal sealed record SetFunctionBreakpointsArguments(IReadOnlyList<DapFunctionBreakpoint> Breakpoints);

/// <summary>Always sent without filters (§4.3).</summary>
internal sealed record SetExceptionBreakpointsArguments
{
    public IReadOnlyList<string> Filters { get; } = [];
}

internal sealed record StackTraceArguments(int ThreadId, int StartFrame, int Levels);

internal sealed record ScopesArguments(int FrameId);

internal sealed record VariablesArguments(int VariablesReference);

/// <summary>"Address of" evaluation; the context is always <c>watch</c> (DBG-082, DBG-084).</summary>
internal sealed record EvaluateArguments(string Expression, int FrameId)
{
    public string Context { get; } = "watch";
}

/// <summary>The offset is always 0 (DBG-086).</summary>
internal sealed record ReadMemoryArguments
{
    public ReadMemoryArguments(string memoryReference, int count)
    {
        MemoryReference = memoryReference;
        Count = count;
    }

    public string MemoryReference { get; }
    public int Offset { get; } = 0;
    public int Count { get; }
}

internal sealed record WriteMemoryArguments
{
    public WriteMemoryArguments(string memoryReference, string data)
    {
        MemoryReference = memoryReference;
        Data = data;
    }

    public string MemoryReference { get; }
    public int Offset { get; } = 0;
    public bool AllowPartial { get; } = false;
    /// <summary>Base64.</summary>
    public string Data { get; }
}

internal sealed record SetVariableArguments(int VariablesReference, string Name, string Value);

/// <summary>Arguments of <c>continue</c>, <c>next</c>, <c>stepIn</c>, <c>stepOut</c> and <c>pause</c> (§6).</summary>
internal sealed record ThreadArguments(int ThreadId);

/// <summary><c>terminateDebuggee</c> is omitted when null (DBG-095, missing capability).</summary>
internal sealed record DisconnectArguments(bool? TerminateDebuggee);

// ---- reverse requests ----

/// <summary>
/// Body of the answer to an accepted <c>runInTerminal</c> request (DBG-545): the process that owns the console.
/// <c>processId</c> is never sent - the session does not know the command's process.
/// </summary>
internal sealed record RunInTerminalResponseBody(int ShellProcessId);

// ---- response bodies ----

internal sealed record DapBreakpoint
{
    public int? Id { get; init; }
    public bool Verified { get; init; }
    public int? Line { get; init; }
    public string? Message { get; init; }
}

internal sealed record SetBreakpointsResponseBody
{
    public IReadOnlyList<DapBreakpoint> Breakpoints { get; init; } = [];
}

internal sealed record DapThread(int Id, string? Name);

internal sealed record ThreadsResponseBody
{
    public IReadOnlyList<DapThread> Threads { get; init; } = [];
}

internal sealed record DapStackFrame
{
    public int Id { get; init; }
    public string? Name { get; init; }
    public DapSource? Source { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public string? InstructionPointerReference { get; init; }
    /// <summary>Number or string; compare through <see cref="DapJson.IdText"/>.</summary>
    public JsonElement? ModuleId { get; init; }
}

internal sealed record StackTraceResponseBody
{
    public IReadOnlyList<DapStackFrame> StackFrames { get; init; } = [];
    public int? TotalFrames { get; init; }
}

internal sealed record DapScope
{
    public string Name { get; init; } = "";
    public string? PresentationHint { get; init; }
    public int VariablesReference { get; init; }
}

internal sealed record ScopesResponseBody
{
    public IReadOnlyList<DapScope> Scopes { get; init; } = [];
}

internal sealed record DapVariable
{
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public string? Type { get; init; }
    public int VariablesReference { get; init; }
    public string? EvaluateName { get; init; }
    public string? MemoryReference { get; init; }
}

internal sealed record VariablesResponseBody
{
    public IReadOnlyList<DapVariable> Variables { get; init; } = [];
}

internal sealed record EvaluateResponseBody
{
    public string Result { get; init; } = "";
    public string? Type { get; init; }
    public int VariablesReference { get; init; }
    public string? MemoryReference { get; init; }
}

internal sealed record ReadMemoryResponseBody
{
    public string? Address { get; init; }
    /// <summary>Base64; missing means the whole range is unreadable (DBG-087).</summary>
    public string? Data { get; init; }
    public int? UnreadableBytes { get; init; }
}

internal sealed record WriteMemoryResponseBody
{
    public int? BytesWritten { get; init; }
}

internal sealed record ContinueResponseBody
{
    public bool? AllThreadsContinued { get; init; }
}

// ---- event bodies ----

internal sealed record StoppedEventBody
{
    public string Reason { get; init; } = "";
    public string? Description { get; init; }
    public string? Text { get; init; }
    public int? ThreadId { get; init; }
    public IReadOnlyList<int>? HitBreakpointIds { get; init; }
}

internal sealed record ExitedEventBody
{
    public int ExitCode { get; init; }
}

internal sealed record ThreadEventBody
{
    public string Reason { get; init; } = "";
    public int ThreadId { get; init; }
}

internal sealed record OutputEventBody
{
    public string? Category { get; init; }
    public string Output { get; init; } = "";
}

internal sealed record BreakpointEventBody
{
    public string Reason { get; init; } = "";
    public DapBreakpoint Breakpoint { get; init; } = new();
}

internal sealed record DapModule
{
    /// <summary>Number or string; compare through <see cref="DapJson.IdText"/>.</summary>
    public JsonElement? Id { get; init; }
    public string? Name { get; init; }
    public string? Path { get; init; }
    public string? SymbolStatus { get; init; }
}

internal sealed record ModuleEventBody
{
    public string Reason { get; init; } = "";
    public DapModule Module { get; init; } = new();
}

internal sealed record ProcessEventBody
{
    public string? Name { get; init; }
    public int? SystemProcessId { get; init; }
}

/// <summary>Body of the <c>capabilities</c> event; merged shallowly into the stored capabilities (DBG-051).</summary>
internal sealed record CapabilitiesEventBody
{
    public Dictionary<string, JsonElement> Capabilities { get; init; } = [];
}
