using System.Text.Json;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// Building blocks of scenario files for the scripted debug adapter (the debugger's testing specification §3), as JSON
/// text: steps answer one request each, in order per command.
/// </summary>
public static class DebugScenarios
{
    /// <summary>A step: the thread list the session asks for at every stop.</summary>
    public const string Threads = """{ "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } }""";

    /// <summary>Events: the program exits by itself with <paramref name="exitCode"/>.</summary>
    public static string Exit(int exitCode = 0) =>
        $$"""{ "event": "exited", "body": { "exitCode": {{exitCode}} } }, { "event": "terminated" }""";

    /// <summary>An event: the program stopped; <paramref name="more"/> adds members to the body (<c>, "hitBreakpointIds": [1]</c>).</summary>
    public static string StoppedEvent(string reason = "breakpoint", string more = "") =>
        $$"""{ "event": "stopped", "body": { "reason": "{{reason}}", "threadId": 1, "allThreadsStopped": true{{more}} } }""";

    /// <summary>A step: the call stack of a stop, innermost first; each frame is a routine at a line of a source file.</summary>
    public static string Stack(params (string Routine, string Path, int Line)[] frames) => $$"""
        { "on": "stackTrace", "respond": { "totalFrames": {{frames.Length}}, "stackFrames": [ {{string.Join(", ", frames.Select((f, i) =>
            $$"""{ "id": {{100 + i}}, "name": {{Json(f.Routine)}}, "line": {{f.Line}}, "column": 1, "source": { "name": {{Json(Path.GetFileName(f.Path))}}, "path": {{Json(f.Path)}} } }"""))}} ] } }
        """;

    /// <summary>A step: the call stack of a stop, one frame <c>Run</c> at <paramref name="line"/> of <paramref name="path"/>.</summary>
    public static string Stack(string path, int line) => Stack(("Run", path, line));

    /// <summary>Steps: the program stops right after the configuration, at <paramref name="line"/> of <paramref name="path"/>.</summary>
    public static string StopAtStart(string path, int line, string stoppedMore = "") => $$"""
        { "on": "configurationDone", "respond": {}, "thenEvents": [ {{StoppedEvent(more: stoppedMore)}} ] },
        {{Threads}},
        {{Stack(path, line)}}
        """;

    /// <summary>Steps: a step request (<c>next</c>, <c>stepIn</c>, <c>stepOut</c>) that stops at <paramref name="line"/> of <paramref name="path"/>.</summary>
    public static string StepTo(string command, string path, int line) => $$"""
        { "on": "{{command}}", "respond": {}, "thenEvents": [ {{StoppedEvent("step")}} ] },
        {{Threads}},
        {{Stack(path, line)}}
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
