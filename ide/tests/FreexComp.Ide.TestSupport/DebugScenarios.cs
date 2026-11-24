using System.Text.Json;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// Building blocks of scenario files for the scripted debug adapter (the debugger's testing specification §3), as JSON
/// text: steps answer one request each, in order per command. At every stop the session asks for the threads and the
/// call stack (the stop's location), and the IDE's debug views (I-142, I-143) ask for the call stack of the current
/// thread once more and for the scopes of its top frame: the stop helpers script all of it.
/// </summary>
public static class DebugScenarios
{
    /// <summary>A step: the thread list the session asks for at every stop.</summary>
    public const string Threads = """{ "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } }""";

    /// <summary>Events: the program exits by itself with <paramref name="exitCode"/>.</summary>
    public static string Exit(int exitCode = 0) =>
        $$"""{ "event": "exited", "body": { "exitCode": {{exitCode}} } }, { "event": "terminated" }""";

    /// <summary>A step: the thread list of a stop; a thread is its id and name.</summary>
    public static string ThreadList(params (int Id, string Name)[] threads) =>
        $$"""{ "on": "threads", "respond": { "threads": [ {{string.Join(", ", threads.Select(t => $$"""{ "id": {{t.Id}}, "name": {{Json(t.Name)}} }"""))}} ] } }""";

    /// <summary>An event: the program stopped; <paramref name="more"/> adds members to the body (<c>, "hitBreakpointIds": [1]</c>).</summary>
    public static string StoppedEvent(string reason = "breakpoint", string more = "", int threadId = 1) =>
        $$"""{ "event": "stopped", "body": { "reason": "{{reason}}", "threadId": {{threadId}}, "allThreadsStopped": true{{more}} } }""";

    /// <summary>A step: the call stack of a stop, innermost first; each frame is a routine at a line of a source file.</summary>
    public static string Stack(params (string Routine, string Path, int Line)[] frames) => Stack(100, frames);

    /// <summary>As <see cref="Stack(ValueTuple{string, string, int}[])"/>, the adapter's frame ids counting from <paramref name="firstId"/> (another thread's frames).</summary>
    public static string Stack(int firstId, params (string Routine, string Path, int Line)[] frames) => $$"""
        { "on": "stackTrace", "respond": { "totalFrames": {{frames.Length}}, "stackFrames": [ {{string.Join(", ", frames.Select((f, i) =>
            $$"""{ "id": {{firstId + i}}, "name": {{Json(f.Routine)}}, "line": {{f.Line}}, "column": 1, "source": { "name": {{Json(Path.GetFileName(f.Path))}}, "path": {{Json(f.Path)}} } }"""))}} ] } }
        """;

    /// <summary>A step: the call stack of a stop, one frame <c>Run</c> at <paramref name="line"/> of <paramref name="path"/>.</summary>
    public static string Stack(string path, int line) => Stack(("Run", path, line));

    /// <summary>A step: the scopes of a frame as the adapter lists them — none (a routine without variables).</summary>
    public const string NoScopes = """{ "on": "scopes", "respond": { "scopes": [] } }""";

    /// <summary>
    /// Steps: what a stop at <paramref name="frames"/> asks for — the threads and the call stack for the session, the call
    /// stack and the scopes of the top frame for the IDE's Call Stack and Locals (<paramref name="scopes"/>: the steps
    /// that answer the latter; default <see cref="NoScopes"/>; <paramref name="threads"/>: the step that answers the
    /// thread list; default <see cref="Threads"/>).
    /// </summary>
    public static string AtStop((string Routine, string Path, int Line)[] frames, string? scopes = null, string? threads = null) => $$"""
        {{threads ?? Threads}},
        {{Stack(frames)}},
        {{Stack(frames)}},
        {{scopes ?? NoScopes}}
        """;

    /// <summary>Steps: the program stops right after the configuration, at <paramref name="line"/> of <paramref name="path"/>.</summary>
    public static string StopAtStart(string path, int line, string stoppedMore = "") => $$"""
        { "on": "configurationDone", "respond": {}, "thenEvents": [ {{StoppedEvent(more: stoppedMore)}} ] },
        {{AtStop([("Run", path, line)])}}
        """;

    /// <summary>Steps: a step request (<c>next</c>, <c>stepIn</c>, <c>stepOut</c>) that stops at <paramref name="line"/> of <paramref name="path"/>.</summary>
    public static string StepTo(string command, string path, int line) => $$"""
        { "on": "{{command}}", "respond": {}, "thenEvents": [ {{StoppedEvent("step")}} ] },
        {{AtStop([("Run", path, line)])}}
        """;

    /// <summary>A step: <c>continue</c>; <paramref name="thenEvents"/> follow the answer (none = the program keeps running).</summary>
    public static string Continue(string thenEvents = "") =>
        $$"""{ "on": "continue", "respond": { "allThreadsContinued": true }, "thenEvents": [ {{thenEvents}} ] }""";

    /// <summary>
    /// A minimal type-information sidecar (the debug-info sidecar interface) for a program with one unit and one routine
    /// <c>Run</c> without variables: enough for the session to evaluate constant expressions while stopped in it. A test
    /// writes it next to the program as <c>&lt;program&gt;.pdi</c>.
    /// </summary>
    public static string TypeInfo(string program, string unitPath) => $$"""
        { "formatVersion": 1, "program": {{Json(program)}}, "container": "codeview",
          "abi": { "pointerSize": 8, "raiseHookSymbol": "System._Raise", "pascalExceptionCode": 3763355987, "exceptionMessageField": "FMessage",
                   "unicodeString": { "lengthOffset": -12, "lengthSize": 4, "lengthUnit": "chars", "charSize": 2 },
                   "ansiString": { "lengthOffset": -12, "lengthSize": 4, "codePageOffset": -8, "codePageSize": 2 },
                   "dynArray": { "lengthOffset": -8, "lengthSize": 8 },
                   "vmt": { "classNameOffset": 16, "classNameKind": "pointerToUnicodeString", "parentOffset": 0, "parentKind": "pointerToVmt" },
                   "set": { "bitOrder": "lsb0" } },
          "types": [ { "id": 1, "name": "Integer", "kind": "integer", "size": 4, "signed": true }, { "id": 2, "name": "Boolean", "kind": "boolean", "size": 1 } ],
          "units": [ { "name": {{Json(Path.GetFileNameWithoutExtension(unitPath))}}, "file": {{Json(unitPath)}}, "globals": [],
                       "routines": [ { "displayName": "Run", "linkageName": "Run", "file": {{Json(unitPath)}}, "line": 1, "bodyLine": 1, "endLine": 999,
                                       "self": null, "vars": [] } ] } ] }
        """;

    /// <summary>A JSON string literal.</summary>
    public static string Json(string text) => JsonSerializer.Serialize(text);
}
