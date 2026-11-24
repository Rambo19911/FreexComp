using System.Buffers.Binary;
using System.Text;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// A program that raises exceptions, for the IDE's exception tests (I-144, I-145), as building blocks of
/// scripted-adapter scenarios: type information with the classes <c>EFirst</c> and <c>ESecond</c> (both descend from
/// <c>Exception</c>) and the raise routine <c>System._Raise</c>, on which the session sets a function breakpoint
/// (debugger features §6), and the memory of one instance of each class — <c>EFirst</c> with the message
/// <c>first</c>, <c>ESecond</c> with <c>second</c>.
/// </summary>
public static class DebugRaises
{
    /// <summary>The runtime unit of the raise routine: not user code, so the stop is shown in the frame below it.</summary>
    public const string SystemUnit = @"C:\rtl\System.pas";

    /// <summary>The storage of the raise routine's first parameter when <c>EFirst</c> is raised.</summary>
    public const ulong FirstSlot = 0x50000;

    /// <summary>… when <c>ESecond</c> is raised.</summary>
    public const ulong SecondSlot = 0x50100;

    private const ulong First = 0x10000;
    private const ulong Second = 0x10100;

    /// <summary>
    /// The type-information sidecar: the routine <c>Go</c> covers every line of <paramref name="unitPath"/>; a test writes
    /// it as <c>&lt;program&gt;.pdi</c>.
    /// </summary>
    public static string TypeInfo(string unitPath) => $$"""
        { "formatVersion": 1, "program": "Raises", "container": "codeview",
          "abi": { "pointerSize": 8, "raiseHookSymbol": "System._Raise", "pascalExceptionCode": 3763355987, "exceptionMessageField": "FMessage",
                   "unicodeString": { "lengthOffset": -12, "lengthSize": 4, "lengthUnit": "chars", "charSize": 2 },
                   "ansiString": { "lengthOffset": -12, "lengthSize": 4, "codePageOffset": -8, "codePageSize": 2 },
                   "dynArray": { "lengthOffset": -8, "lengthSize": 8 },
                   "vmt": { "classNameOffset": 16, "classNameKind": "pointerToUnicodeString", "parentOffset": 0, "parentKind": "pointerToVmt" },
                   "set": { "bitOrder": "lsb0" } },
          "types": [
            { "id": 1, "name": "TObject", "unit": "System", "kind": "class", "size": 8, "ancestor": null, "instanceSize": 8, "fields": [] },
            { "id": 2, "name": "string", "kind": "string", "size": 8, "flavor": "unicode" },
            { "id": 3, "name": "Exception", "unit": "SysUtils", "kind": "class", "size": 8, "ancestor": 1, "instanceSize": 16,
              "fields": [ { "name": "FMessage", "type": 2, "offset": 8, "visibility": "private" } ] },
            { "id": 4, "name": "EFirst", "unit": "Raises", "kind": "class", "size": 8, "ancestor": 3, "instanceSize": 16, "fields": [] },
            { "id": 5, "name": "ESecond", "unit": "Raises", "kind": "class", "size": 8, "ancestor": 3, "instanceSize": 16, "fields": [] },
            { "id": 6, "name": "Pointer", "kind": "pointer", "size": 8, "to": null } ],
          "units": [
            { "name": {{DebugScenarios.Json(Path.GetFileNameWithoutExtension(unitPath))}}, "file": {{DebugScenarios.Json(unitPath)}}, "globals": [],
              "routines": [ { "displayName": "Go", "linkageName": "Go", "file": {{DebugScenarios.Json(unitPath)}}, "line": 1, "bodyLine": 1, "endLine": 999,
                              "self": null, "vars": [] } ] },
            { "name": "System", "file": {{DebugScenarios.Json(SystemUnit)}}, "globals": [],
              "routines": [ { "displayName": "_Raise", "linkageName": "System._Raise", "file": {{DebugScenarios.Json(SystemUnit)}},
                              "line": 2982, "bodyLine": 2986, "endLine": 2997, "self": null,
                              "vars": [ { "name": "Obj", "kind": "param", "type": 6 }, { "name": "Addr", "kind": "param", "type": 6 } ] } ] } ] }
        """;

    /// <summary>
    /// The scenario member <c>"memory"</c>: each parameter slot holds its instance; an instance holds its VMT and its
    /// message; a VMT holds the parent VMT (+0) and the class name (+16).
    /// </summary>
    public static string Memory => "\"memory\": [ " + string.Join(", ",
        Region(FirstSlot, Pointers(First)),
        Region(SecondSlot, Pointers(Second)),
        Region(First, Pointers(0x20000, 0x30010)),
        Region(Second, Pointers(0x20100, 0x30110)),
        Region(0x20000, Pointers(0x21000, 0, 0x40010)),
        Region(0x20100, Pointers(0x21000, 0, 0x40110)),
        Region(0x21000, Pointers(0, 0, 0x40210)),
        StringRegion(0x30010, "first"),
        StringRegion(0x30110, "second"),
        StringRegion(0x40010, "EFirst"),
        StringRegion(0x40110, "ESecond"),
        StringRegion(0x40210, "Exception")) + " ]";

    /// <summary>The scenario member <c>"capabilities"</c> an exception-notifying session needs (function breakpoints).</summary>
    public const string Capabilities = """
        "capabilities": { "supportsConfigurationDoneRequest": true, "supportsReadMemoryRequest": true, "supportsFunctionBreakpoints": true,
                          "supportsWriteMemoryRequest": true, "supportTerminateDebuggee": true }
        """;

    /// <summary>A step: the session's function breakpoint on the raise routine (adapter id 7).</summary>
    public const string HookSet = """
        { "on": "setFunctionBreakpoints", "expectArguments": { "breakpoints": [ { "name": "System._Raise" } ] },
          "respond": { "breakpoints": [ { "id": 7, "verified": true } ] } }
        """;

    /// <summary>An event: the program stopped in the raise routine.</summary>
    public const string HookHit = """{ "event": "stopped", "body": { "reason": "function breakpoint", "threadId": 1, "hitBreakpointIds": [7] } }""";

    /// <summary>The frames of a raise in <c>Go</c> on <paramref name="line"/> of <paramref name="unitPath"/>: the raise routine on top.</summary>
    public static (string Routine, string Path, int Line)[] Frames(string unitPath, int line) =>
        [("System._Raise", SystemUnit, 2986), ("Go", unitPath, line)];

    /// <summary>A step: the adapter's answer to "address of the raise routine's first parameter".</summary>
    public static string AddressOfObj(ulong slot) => $$"""
        { "on": "evaluate", "expectArguments": { "expression": "&Obj" }, "respond": { "result": "0x{{slot:x16}}", "type": "void **", "variablesReference": 0 } }
        """;

    /// <summary>
    /// Steps: what the session asks at a raise before it knows the class — the threads, the call stack and the address of
    /// the object. A raise of an ignored class asks no more (the session continues by itself).
    /// </summary>
    public static string AtRaise(string unitPath, int line, ulong slot) => $$"""
        {{DebugScenarios.Threads}},
        {{DebugScenarios.Stack(Frames(unitPath, line))}},
        {{AddressOfObj(slot)}}
        """;

    /// <summary>Steps: a raise that stops — <see cref="AtRaise"/> and what the IDE's Call Stack and Locals ask at the stop.</summary>
    public static string AtRaiseStop(string unitPath, int line, ulong slot) => $$"""
        {{AtRaise(unitPath, line, slot)}},
        {{DebugScenarios.Stack(Frames(unitPath, line))}},
        {{DebugScenarios.NoScopes}}
        """;

    private static string Region(ulong address, byte[] bytes) =>
        $$"""{ "address": "0x{{address:X}}", "hex": "{{Convert.ToHexString(bytes)}}" }""";

    private static byte[] Pointers(params ulong[] values)
    {
        var bytes = new byte[values.Length * 8];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(i * 8), values[i]);
        return bytes;
    }

    /// <summary>A string payload at <paramref name="payload"/> with its header (the length as Int32 at payload - 12).</summary>
    private static string StringRegion(ulong payload, string text)
    {
        var bytes = new byte[12 + (text.Length * 2)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, text.Length);
        Encoding.Unicode.GetBytes(text, bytes.AsSpan(12));
        return Region(payload - 12, bytes);
    }
}
