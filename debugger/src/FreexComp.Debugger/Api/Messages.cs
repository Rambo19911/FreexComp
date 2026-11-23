namespace FreexComp.Debugger;

/// <summary>All user-visible message strings produced by the library (English constants; architecture §9).</summary>
internal static class Messages
{
    public const string ProtocolErrorPrefix = "Protocol error: ";
    public const string MissingContentLength = "header without Content-Length";
    public const string InvalidContentLength = "Content-Length is not a number: ";
    public const string OversizeMessage = "message larger than 64 MiB: ";
    public const string HeaderTooLong = "header longer than 64 KiB";
    public const string MalformedJson = "malformed JSON: ";
    public const string MessageWithoutType = "message without type";
    public const string UnknownMessageType = "unknown message type: ";

    public const string AdapterExited = "Debug adapter exited unexpectedly.";
    public const string AdapterNotFound = "Debug adapter not found: ";
    public const string AdapterStartFailed = "Debug adapter could not be started: ";
    public const string ReverseRequestNotSupported = "not supported";
    public const string HandlerFailed = "Event handler failed: ";

    public const string ProgramNotFound = "Program not found: ";
    public const string MissingCapability = "Debug adapter lacks a required capability: ";
    public const string AdapterNotReady = "Debug adapter did not become ready.";
    public const string LaunchTimedOut = "The program did not start in time.";
    public const string LaunchCancelled = "Cancelled";
    public const string SessionTerminated = "The debug session was terminated.";
    public const string SessionEnded = "The debug session has ended.";
    public const string MissingPythonRuntime = "See docs/QUESTIONS.md: lldb-dap requires a Python runtime that is not installed.";
    public const string InternalErrorPrefix = "Internal debugger error: ";

    public const string NotBoundYet = "Not bound yet";
    public const string DuplicateBreakpoint = "Duplicate breakpoint";

    public const string StoppedAtEntry = "Stopped at program entry";
    public const string StepCompleted = "Step completed";
    public const string Paused = "Paused";
    public const string BreakpointHit = "Breakpoint";
    public const string StaleFrame = "The stack frame belongs to an earlier stop.";

    public static string AdapterExitedWithCode(int exitCode) => "Debug adapter exited unexpectedly (exit code " + exitCode + ").";
    public static string AdapterExitedDuringStart(int exitCode) => AdapterStartFailed + "it exited with code " + exitCode + ".";
    public static string BreakpointAt(string fileName, int line) => "Breakpoint at " + fileName + ":" + line;
    public static string StopDetailsUnavailable(string error) => "Stopped (details unavailable: " + error + ")";
    public static string ThreadName(int id) => "Thread " + id;
    public static string ProtocolError(string detail) => ProtocolErrorPrefix + detail;
    public static string RequestFailed(string command) => command + " failed";
    public static string RequestTimedOut(string command) => "Debug adapter did not answer '" + command + "' in time.";
    public static string InvalidState(string method, DebugState state) => method + " is not allowed in state " + state + ".";
}
