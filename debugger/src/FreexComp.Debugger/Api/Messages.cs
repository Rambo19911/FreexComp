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
    public const string LibraryNotFound = "Library not found: ";
    public const string MissingCapability = "Debug adapter lacks a required capability: ";
    public const string AdapterNotReady = "Debug adapter did not become ready.";
    public const string LaunchTimedOut = "The program did not start in time.";
    public const string LaunchCancelled = "Cancelled";
    public const string SessionTerminated = "The debug session was terminated.";
    public const string SessionEnded = "The debug session has ended.";
    public const string MissingPythonRuntime = "The debugger needs its bundled Python runtime, which was not found next to the toolchain.";
    public const string InternalErrorPrefix = "Internal debugger error: ";

    public const string NotBoundYet = "Not bound yet";
    public const string DuplicateBreakpoint = "Duplicate breakpoint";

    public const string StoppedAtEntry = "Stopped at program entry";
    public const string StepCompleted = "Step completed";
    public const string Paused = "Paused";
    public const string HardCodedBreakpoint = "Hard-coded breakpoint";
    public const string ExceptionNotificationUnavailable = "Exception notification is unavailable (adapter lacks function breakpoints).";
    public const string AdapterNotResponding = "Debug adapter is not responding.";
    public const string BreakpointHit = "Breakpoint";
    public const string NotAvailable = "<not available>";
    public const string StaleFrame = "The stack frame belongs to an earlier stop.";

    // ---- expressions (Pascal values §9) ----
    public const string ExpressionExpected = "Expression expected";
    public const string IdentifierExpected = "Identifier expected";
    public const string InvalidStringLiteral = "Invalid string literal";
    public const string SyntaxError = "Syntax error";
    public const string FunctionCallsNotSupported = "Function calls are not supported in expressions";
    public const string DivisionByZero = "Division by zero";
    public const string NilReference = "Access through nil reference";
    public const string UntypedDereference = "Cannot dereference an untyped pointer";
    public const string InvalidTypecast = "Invalid typecast";
    public const string StringTooLong = "String too long to evaluate";
    public const string BooleanExpected = "Boolean expression expected";
    public const string SetElementOutOfRange = "Set element out of range";
    public const string SetElementsDiffer = "Set elements must have the same type";
    public const string NoTypeInfo = "Type information is not available";
    public const string MemoryWriteIncomplete = "Memory write was incomplete.";
    public const string HitCountTooSmall = "Hit count must be at least 1";

    public static string AtColumn(string message, int column) => message + " at column " + column;
    public static string Unexpected(string token) => "Unexpected '" + token + "'";
    public static string Expected(string token) => "'" + token + "' expected";
    public static string InvalidNumber(string text) => "Invalid number '" + text + "'";
    public static string NotSupportedInExpressions(string construct, int column) =>
        "'" + construct + "' is not supported in debugger expressions at column " + column;
    public static string UndeclaredIdentifier(string name) => "Undeclared identifier '" + name + "'";
    public static string NotAvailableVariable(string name) => "'" + name + "' is not available";
    public static string TypeIsNotValue(string name) => "'" + name + "' is a type, not a value";
    public static string OperatorNotApplicable(string op, string type) => "Operator '" + op + "' not applicable to " + type;
    public static string OperatorNotApplicable(string op, string left, string right) =>
        "Operator '" + op + "' not applicable to " + left + " and " + right;
    public static string BuiltinNotApplicable(string name, string type) => "'" + name + "' is not applicable to " + type;
    public static string WrongArgumentCount(string name) => "Wrong number of arguments for '" + name + "'";
    public static string IndexOutOfBounds(long index, long low, long high) => "Index " + index + " out of bounds (" + low + ".." + high + ")";
    public static string ValueOutOfRange(string type) => "Value out of range for " + type;
    public static string IncompatibleTypes(string source, string target) => "Incompatible types: " + source + " and " + target;
    public static string FormatSuffixNotSupported(int count) => "Format suffix '," + count + "' is not supported";
    public static string NotModifiable(string name) => "'" + name + "' cannot be modified.";
    public static string UnknownFormatSpecifier(string spec) => "Unknown format specifier '" + spec + "'";

    // ---- visualisers (visualizers.md DBG-509 ... DBG-512) ----
    public static string VisualizerFailed(string id, string typeName, string reason) =>
        "Visualizer '" + id + "' failed for " + typeName + ": " + reason
        + (reason.EndsWith('.') || reason.EndsWith('!') || reason.EndsWith('?') ? "" : ".") + " Raw values are shown.";
    public static string VisualizerTurnedOff(string id, int failures) =>
        "Visualizer '" + id + "' was turned off for this debug session after " + failures + " failures.";
    public static string VisualizerChildFailed(string reason) => "<visualizer failed: " + reason + ">";
    public static string VisualizerTimedOut(TimeSpan limit) =>
        "timed out after " + limit.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " s";
    public const string VisualizerReadBudgetExceeded = "read budget exceeded";
    public const string VisualizerSecondPointerLevel = "more than one pointer level in a nested summary";
    public const string VisualizerReturnedNothing = "the visualiser returned nothing";
    public static string VisualizerNoField(string field, string typeName) => "no field '" + field + "' in " + typeName;
    public const string VisualizerCallEnded = "the visualiser call has ended";

    // ---- breakpoints, run to line (features §2, §4) ----
    public static string ConditionError(string message) => "Breakpoint condition error: " + message;
    public static string BreakpointMessage(string fileName, int line, string message) => "Breakpoint " + fileName + ":" + line + ": " + message;
    public static string LogError(string message) => "<error: " + message + ">";
    public static string NoCodeAt(string fileName, int line) => "No code at " + fileName + ":" + line + ".";
    public const string RunToLineCompleted = "Run to line completed";
    public const string StaleHandle = "The value belongs to an earlier stop.";

    public static string TypeInfoMissing(string path) => "Type information file not found: " + path + ". Values are shown unformatted.";
    public static string TypeInfoInvalid(string path, string detail) => "Type information file is invalid: " + path + ": " + detail;
    public static string UnreadableMemory(ulong address) => "<unreadable memory at " + Address(address) + ">";
    public static string InvalidString(ulong address) => "<invalid string at " + Address(address) + ">";
    public static string InvalidArray(ulong address) => "<invalid array at " + Address(address) + ">";
    public static string MemoryUnreadable(ulong address, int count) => "Memory at " + Address(address) + " (" + count + " bytes) cannot be read.";

    /// <summary>"$" and 16 upper-case hexadecimal digits (DBG-153).</summary>
    public static string Address(ulong address) => "$" + address.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"Exception EClass: message"; without a message "Exception EClass"; without a class name "Exception" (DBG-245).</summary>
    public static string ExceptionStop(string className, string message) =>
        "Exception" + (className.Length > 0 ? " " + className : "") + (message.Length > 0 ? ": " + message : "");

    public static string OsExceptionAt(string text, ulong address) => text + " at " + Address(address);
    public static string UnknownOsException(uint code) => "Exception $" + code.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

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
