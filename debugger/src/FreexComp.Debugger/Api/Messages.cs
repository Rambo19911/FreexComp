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

    public static string ProtocolError(string detail) => ProtocolErrorPrefix + detail;
    public static string RequestFailed(string command) => command + " failed";
    public static string RequestTimedOut(string command) => "Debug adapter did not answer '" + command + "' in time.";
    public static string InvalidState(string method, DebugState state) => method + " is not allowed in state " + state + ".";
}
