using System.Buffers.Binary;
using System.Text;
using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// Exception notification (features §6): the raise hook (DBG-240 … DBG-246), the ignore list and run-time settings,
/// and the stops the adapter reports for operating-system exceptions (DBG-247 … DBG-249).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExceptionMonitorTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Raises = @"C:\work\Raises\Raises.pas";
    private const string SystemUnit = @"C:\rtl\System.pas";
    private const string SysUtilsUnit = @"C:\rtl\SysUtils.pas";
    private const uint PascalCode = 0xE0504153;

    // ---- the debuggee: type information and memory ----

    /// <summary>
    /// Type information of a program with <c>EFirst</c> and <c>ESecond</c> (both descend from <c>Exception</c>, whose
    /// message field is <c>FMessage</c>); <paramref name="describeHook"/> decides whether the raise routine is listed.
    /// </summary>
    private static string Pdi(bool describeHook = true) => $$"""
        { "formatVersion": 1, "program": "Raises", "container": "codeview", "abi": {{PdiFixture.Abi}},
          "types": [
            { "id": 1, "name": "TObject", "unit": "System", "kind": "class", "size": 8, "ancestor": null, "instanceSize": 8, "fields": [] },
            { "id": 2, "name": "string", "kind": "string", "size": 8, "flavor": "unicode" },
            { "id": 3, "name": "Exception", "unit": "SysUtils", "kind": "class", "size": 8, "ancestor": 1, "instanceSize": 16,
              "fields": [ { "name": "FMessage", "type": 2, "offset": 8, "visibility": "private" } ] },
            { "id": 4, "name": "EFirst", "unit": "Raises", "kind": "class", "size": 8, "ancestor": 3, "instanceSize": 16, "fields": [] },
            { "id": 5, "name": "ESecond", "unit": "Raises", "kind": "class", "size": 8, "ancestor": 3, "instanceSize": 16, "fields": [] },
            { "id": 6, "name": "Pointer", "kind": "pointer", "size": 8, "to": null } ],
          "units": [
            { "name": "Raises", "file": "C:\\work\\Raises\\Raises.pas", "globals": [],
              "routines": [
                { "displayName": "Go", "linkageName": "Raises.Go", "file": "C:\\work\\Raises\\Raises.pas",
                  "line": 8, "bodyLine": 9, "endLine": 21, "self": null, "vars": [] },
                { "displayName": "Raises", "linkageName": "_start", "file": "C:\\work\\Raises\\Raises.pas",
                  "line": 23, "bodyLine": 23, "endLine": 25, "self": null, "vars": [] } ] },
            { "name": "System", "file": "C:\\rtl\\System.pas", "globals": [],
              "routines": [ {{(describeHook ? """
                { "displayName": "_Raise", "linkageName": "System._Raise", "file": "C:\\rtl\\System.pas",
                  "line": 2982, "bodyLine": 2986, "endLine": 2997, "self": null,
                  "vars": [ { "name": "Obj", "kind": "param", "type": 6 }, { "name": "Addr", "kind": "param", "type": 6 } ] }
                """ : "")}} ] } ] }
        """;

    private const ulong ObjSlot1 = 0x50000;      // storage of the hook's first parameter at the first raise
    private const ulong ObjSlot2 = 0x50100;      // … at the second raise
    private const ulong First = 0x10000;         // the EFirst instance, message 'first'
    private const ulong Second = 0x10100;        // the ESecond instance, message 'second'

    private static string Region(ulong address, byte[] bytes) =>
        $$"""{ "address": "0x{{address:X}}", "hex": "{{Convert.ToHexString(bytes)}}" }""";

    private static byte[] Pointers(params ulong[] values)
    {
        var bytes = new byte[values.Length * 8];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(i * 8), values[i]);
        return bytes;
    }

    /// <summary>A string payload at <paramref name="payload"/> with its header (length as Int32 at payload - 12).</summary>
    private static string StringRegion(ulong payload, string text)
    {
        var bytes = new byte[12 + text.Length * 2];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, text.Length);
        Encoding.Unicode.GetBytes(text, bytes.AsSpan(12));
        return Region(payload - 12, bytes);
    }

    /// <summary>
    /// Two exception objects: the parameter slot holds the instance; the instance holds its VMT and the message; the
    /// VMT holds the parent VMT (+0) and the class name (+16).
    /// </summary>
    private static readonly string Memory = "[ " + string.Join(", ",
        Region(ObjSlot1, Pointers(First)),
        Region(ObjSlot2, Pointers(Second)),
        Region(First, Pointers(0x20000, 0x30010)),
        Region(Second, Pointers(0x20100, 0x30110)),
        Region(0x20000, Pointers(0x21000, 0, 0x40010)),      // VMT of EFirst
        Region(0x20100, Pointers(0x21000, 0, 0x40110)),      // VMT of ESecond
        Region(0x21000, Pointers(0, 0, 0x40210)),            // VMT of Exception
        StringRegion(0x30010, "first"),
        StringRegion(0x30110, "second"),
        StringRegion(0x40010, "EFirst"),
        StringRegion(0x40110, "ESecond"),
        StringRegion(0x40210, "Exception")) + " ]";

    // ---- scenario pieces ----

    private const string Capabilities = """
        { "supportsConfigurationDoneRequest": true, "supportsReadMemoryRequest": true, "supportsFunctionBreakpoints": true,
          "supportsWriteMemoryRequest": true, "supportTerminateDebuggee": true }
        """;

    private const string Threads = """{ "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } }""";

    private const string HookSet = """
        { "on": "setFunctionBreakpoints", "expectArguments": { "breakpoints": [ { "name": "System._Raise" } ] },
          "respond": { "breakpoints": [ { "id": 7, "verified": true } ] } }
        """;

    private const string HookHit = """{ "event": "stopped", "body": { "reason": "function breakpoint", "threadId": 1, "hitBreakpointIds": [7] } }""";

    private static string Frame(int id, string name, string? path, int line, string ip = "0x7FF6A1B21234") =>
        $$"""{ "id": {{id}}, "name": "{{name}}", "line": {{line}}, "column": 3, "instructionPointerReference": "{{ip}}"{{(path is null ? "" : $$""", "source": { "name": "{{Path.GetFileName(path)}}", "path": "{{path.Replace(@"\", @"\\")}}" }""")}} }""";

    private static string Stack(params string[] frames) =>
        $$"""{ "on": "stackTrace", "respond": { "totalFrames": {{frames.Length}}, "stackFrames": [ {{string.Join(", ", frames)}} ] } }""";

    private static readonly string HookFrame = Frame(100, "System._Raise", SystemUnit, 2986);
    private static readonly string HelperFrame = Frame(101, "SysUtils.RaiseHelper", SysUtilsUnit, 100);
    private static readonly string GoFrame12 = Frame(102, "Raises.Go", Raises, 12);
    private static readonly string GoFrame17 = Frame(102, "Raises.Go", Raises, 17);
    private static readonly string MainFrame = Frame(103, "_start", Raises, 24);

    /// <summary>The adapter's answer to "address of the hook's first parameter" in the hook frame.</summary>
    private static string AddressOfObj(ulong slot, int frameId = 100) => $$"""
        { "on": "evaluate", "expectArguments": { "expression": "&Obj", "frameId": {{frameId}}, "context": "watch" },
          "respond": { "result": "0x{{slot:x16}}", "type": "void **", "variablesReference": 0 } }
        """;

    private static string OsStop(string description) =>
        $$"""{ "event": "stopped", "body": { "reason": "exception", "description": "{{description}}", "threadId": 1 } }""";

    private static string Scenario(params string[] steps) =>
        $$"""{ "capabilities": {{Capabilities}}, "memory": {{Memory}}, "steps": [ {{string.Join(", ", steps)}} ] }""";

    private static string StoppedFirst(string @event) =>
        $$"""{ "on": "configurationDone", "respond": {}, "thenEvents": [ {{@event}} ] }""";

    private sealed class Bed : IAsyncDisposable
    {
        private readonly string _pdiPath;

        public Bed(string scenario, bool describeHook, Func<DebugSessionOptions, DebugSessionOptions>? configure)
        {
            _pdiPath = PdiFixture.WriteTemp("Raises.pdi", Pdi(describeHook));
            Harness = SessionHarness.Create(scenario, configure: configure);
        }

        public SessionHarness Harness { get; }

        public DebugSession Session => Harness.Session;

        public Task LaunchAsync(ExceptionSettings? exceptions = null) => Harness.LaunchAsync(new LaunchRequest
        {
            Program = SessionHarness.Program,
            TypeInfoPath = _pdiPath,
            Exceptions = exceptions ?? new ExceptionSettings(),
        });

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Harness.DisposeAsync();
            }
            finally
            {
                PdiFixture.DeleteDirectoryOf(_pdiPath);
            }
        }
    }

    private static Bed Create(string scenario, bool describeHook = true, Func<DebugSessionOptions, DebugSessionOptions>? configure = null) =>
        new(scenario, describeHook, configure);

    private static async Task EndAsync(Bed bed)
    {
        await bed.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await bed.Harness.WaitForEventAsync("terminated");   // the last event: everything before it has been delivered
        bed.Harness.AssertScenarioComplete();
    }

    // ---- G-054: the raise hook ----

    [Fact]
    [Trait("Req", "DBG-241")]
    [Trait("Req", "DBG-242")]
    [Trait("Req", "DBG-243")]
    [Trait("Req", "DBG-245")]
    public async Task Hook_Stop_PublishesLanguageException()
    {
        await using var bed = Create(Scenario(
            HookSet, StoppedFirst(HookHit), Threads, Stack(HookFrame, GoFrame12, MainFrame), AddressOfObj(ObjSlot1),
            """{ "on": "continue", "respond": {}, "expectArguments": { "threadId": 1 } }"""));

        await bed.LaunchAsync();

        Assert.Equal(
            $"stopped Exception epoch=1 thread=1 loc={Raises}:12:3 hits=[] desc=Exception EFirst: first",
            await bed.Harness.WaitForEventAsync("stopped"));
        Assert.Equal(new ExceptionInfo(ExceptionKind.Language, "EFirst", "first", 0, First), bed.Session.CurrentStop!.Exception);
        Assert.Equal(DebugState.Stopped, bed.Session.State);
        // The function breakpoint on the hook is part of the configuration phase, after the source breakpoints and
        // before the (empty) exception filters (DBG-054); its name is the symbol of the type information.
        Assert.Equal(
            ["initialize", "launch", "setFunctionBreakpoints", "setExceptionBreakpoints", "configurationDone", "threads", "stackTrace"],
            bed.Harness.SentCommands().Take(7));
        Assert.Equal("""{"breakpoints":[{"name":"System._Raise"}]}""", bed.Harness.SentArguments("setFunctionBreakpoints"));
        // The object comes from the hook's first parameter as the type information names it (DBG-242, path 1).
        Assert.Equal("""{"expression":"&Obj","frameId":100,"context":"watch"}""", bed.Harness.SentArguments("evaluate"));
        Assert.DoesNotContain("scopes", bed.Harness.SentCommands());

        // The program's own handlers run after a plain continue: the debugger does not interfere.
        await bed.Session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        Assert.Equal(DebugState.Running, bed.Session.State);
        await EndAsync(bed);
    }

    [Fact]
    [Trait("Req", "DBG-070")]
    [Trait("Req", "DBG-245")]
    public async Task Hook_Stop_ByTopFrameName_EmptyMessage()
    {
        // No breakpoint ids from the adapter: the top frame's name is the hook symbol (DBG-070). The object cannot
        // be read (the adapter knows no address): class name and message are empty.
        await using var bed = Create(Scenario(
            HookSet, StoppedFirst("""{ "event": "stopped", "body": { "reason": "breakpoint", "threadId": 1 } }"""),
            Threads, Stack(HookFrame, GoFrame12, MainFrame),
            """{ "on": "evaluate", "fail": "no variable named 'Obj' found in this frame" }""",
            """{ "on": "scopes", "respond": { "scopes": [ { "name": "Locals", "presentationHint": "locals", "variablesReference": 1 } ] } }"""));

        await bed.LaunchAsync();

        Assert.Equal(
            $"stopped Exception epoch=1 thread=1 loc={Raises}:12:3 hits=[] desc=Exception",
            await bed.Harness.WaitForEventAsync("stopped"));
        Assert.Equal(new ExceptionInfo(ExceptionKind.Language, "", "", 0, 0), bed.Session.CurrentStop!.Exception);
        await EndAsync(bed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "DBG-242")]
    public async Task RegisterFallback_Rcx(bool registerGroups)
    {
        // The type information does not describe the hook routine: the object is the first argument register of
        // frame 0, read from the adapter's Registers scope (name compared case-insensitively, value parsed as hex).
        // The pinned adapter lists register groups under that scope; a flat list works as well.
        const string registers = """
            [ { "name": "rax", "value": "0x0000000000000001", "variablesReference": 0 },
              { "name": "RCX", "value": "0x0000000000010000", "variablesReference": 0 } ]
            """;
        var registerSteps = registerGroups
            ? $$"""
                { "on": "variables", "expectArguments": { "variablesReference": 3 },
                  "respond": { "variables": [ { "name": "General Purpose Registers", "value": "", "variablesReference": 4 } ] } },
                { "on": "variables", "expectArguments": { "variablesReference": 4 }, "respond": { "variables": {{registers}} } }
                """
            : $$"""{ "on": "variables", "expectArguments": { "variablesReference": 3 }, "respond": { "variables": {{registers}} } }""";
        await using var bed = Create(Scenario(
            HookSet, StoppedFirst(HookHit), Threads,
            Stack(Frame(100, "System._Raise", null, 0), GoFrame12, MainFrame),
            """
            { "on": "scopes", "expectArguments": { "frameId": 100 }, "respond": { "scopes": [
                { "name": "Locals", "presentationHint": "locals", "variablesReference": 1 },
                { "name": "Globals", "variablesReference": 2 },
                { "name": "Registers", "presentationHint": "registers", "variablesReference": 3 } ] } }
            """,
            registerSteps), describeHook: false);

        await bed.LaunchAsync();

        Assert.Equal(
            $"stopped Exception epoch=1 thread=1 loc={Raises}:12:3 hits=[] desc=Exception EFirst: first",
            await bed.Harness.WaitForEventAsync("stopped"));
        Assert.Equal(new ExceptionInfo(ExceptionKind.Language, "EFirst", "first", 0, First), bed.Session.CurrentStop!.Exception);
        Assert.DoesNotContain("evaluate", bed.Harness.SentCommands());
        await EndAsync(bed);
    }

    [Theory]
    [InlineData(@"C:\work\Raises", Raises, 12)]      // first user frame below the hook and the runtime's raise helpers
    [InlineData(@"C:\elsewhere", SysUtilsUnit, 100)]   // no user frame: the first frame below the hook that has source
    [Trait("Req", "DBG-245")]
    public async Task Location_FirstUserFrame(string userRoot, string expectedPath, int expectedLine)
    {
        await using var bed = Create(
            Scenario(HookSet, StoppedFirst(HookHit), Threads, Stack(HookFrame, HelperFrame, GoFrame12, MainFrame), AddressOfObj(ObjSlot1)),
            configure: o => o with { UserSourceRoots = [userRoot] });

        await bed.LaunchAsync();

        Assert.Equal(
            $"stopped Exception epoch=1 thread=1 loc={expectedPath}:{expectedLine}:3 hits=[] desc=Exception EFirst: first",
            await bed.Harness.WaitForEventAsync("stopped"));
        await EndAsync(bed);
    }

    [Fact]
    [Trait("Req", "DBG-241")]
    public async Task LanguageExceptions_Off_NoFunctionBreakpoint()
    {
        // Notification switched off before the launch: no function breakpoint request at all (DAP usage §3).
        await using var bed = Create(Scenario());

        await bed.LaunchAsync(new ExceptionSettings { BreakOnLanguageExceptions = false });

        Assert.Equal(["initialize", "launch", "setExceptionBreakpoints", "configurationDone"], bed.Harness.SentCommands());
        await EndAsync(bed);
    }

    [Fact]
    [Trait("Req", "DBG-241")]
    public async Task NoFunctionBreakpointCapability_OutputOnce_NoRequest()
    {
        await using var bed = Create(
            """{ "capabilities": { "supportsConfigurationDoneRequest": true, "supportsReadMemoryRequest": true } }""");

        await bed.LaunchAsync();
        // Switching the notification "on" again changes nothing and says nothing more.
        await bed.Session.SetExceptionSettingsAsync(new ExceptionSettings { IgnoredClasses = [] }, Ct).WaitAsync(Safety, Ct);

        Assert.Equal(["initialize", "launch", "setExceptionBreakpoints", "configurationDone"], bed.Harness.SentCommands());
        await EndAsync(bed);
        Assert.Equal(
            ["output Debugger \"Exception notification is unavailable (adapter lacks function breakpoints).\""],
            bed.Harness.Events.Snapshot().Where(e => e.StartsWith("output", StringComparison.Ordinal)));
    }

    // ---- G-055: ignore list, settings at run time ----

    [Fact]
    [Trait("Req", "DBG-244")]
    [Trait("Req", "DBG-425")]
    public async Task Ignored_ContinuesSilently()
    {
        // EFirst is ignored (the list is compared case-insensitively): the session continues by itself. ESecond is
        // not on the list, although it has the same ancestor: no inheritance matching.
        await using var bed = Create(Scenario(
            HookSet, StoppedFirst(HookHit), Threads, Stack(HookFrame, GoFrame12, MainFrame), AddressOfObj(ObjSlot1),
            $$"""{ "on": "continue", "respond": {}, "expectArguments": { "threadId": 1 }, "thenEvents": [ {{HookHit}} ] }""",
            Threads, Stack(HookFrame, GoFrame17, MainFrame), AddressOfObj(ObjSlot2)));

        await bed.LaunchAsync(new ExceptionSettings { IgnoredClasses = ["efirst", "Exception"] });

        Assert.Equal(
            $"stopped Exception epoch=1 thread=1 loc={Raises}:17:3 hits=[] desc=Exception ESecond: second",
            await bed.Harness.WaitForEventAsync("stopped"));
        Assert.Equal(new ExceptionInfo(ExceptionKind.Language, "ESecond", "second", 0, Second), bed.Session.CurrentStop!.Exception);
        // The ignored raise produced no public event at all (DBG-425): the state went from Starting to Running when
        // the launch completed and the only stop is the second raise.
        Assert.Equal(
            ["state Idle->Starting", "state Starting->Running", "state Running->Stopped",
             $"stopped Exception epoch=1 thread=1 loc={Raises}:17:3 hits=[] desc=Exception ESecond: second"],
            bed.Harness.Events.Snapshot());
        Assert.Equal(1, ((IDebugSessionDiagnostics)bed.Session).InternalStopCount);
        await EndAsync(bed);
    }

    [Fact]
    [Trait("Req", "DBG-227")]
    [Trait("Req", "DBG-030")]
    public async Task Ignored_DuringStep_ReissuesStep()
    {
        // A raise of an ignored class during Step Over: the same step kind is issued again instead of a free run.
        await using var bed = Create(Scenario(
            HookSet,
            StoppedFirst("""{ "event": "stopped", "body": { "reason": "step", "threadId": 1 } }"""), Threads, Stack(GoFrame12, MainFrame),
            $$"""{ "on": "next", "respond": {}, "thenEvents": [ {{HookHit}} ] }""",
            Threads, Stack(HookFrame, GoFrame12, MainFrame), AddressOfObj(ObjSlot1),
            """{ "on": "next", "respond": {}, "expectArguments": { "threadId": 1 } }"""));
        await bed.LaunchAsync(new ExceptionSettings { IgnoredClasses = ["EFirst"] });
        await bed.Harness.WaitForEventAsync("stopped Step");

        await bed.Session.StepOverAsync(1, Ct).WaitAsync(Safety, Ct);
        await bed.Harness.WaitForResponsesAsync("next", 2);

        Assert.Equal(DebugState.Running, bed.Session.State);
        Assert.DoesNotContain("continue", bed.Harness.SentCommands());
        await EndAsync(bed);
        Assert.Single(bed.Harness.Events.Snapshot(), e => e.StartsWith("stopped", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "DBG-241")]
    [Trait("Req", "DBG-016")]
    public async Task Settings_Changed_ResendsFunctionBreakpoints()
    {
        await using var bed = Create(Scenario(
            HookSet,
            """{ "on": "setFunctionBreakpoints", "expectArguments": { "breakpoints": [] }, "respond": { "breakpoints": [] } }""",
            """
            { "on": "setFunctionBreakpoints", "expectArguments": { "breakpoints": [ { "name": "System._Raise" } ] },
              "respond": { "breakpoints": [ { "id": 9, "verified": true } ] },
              "thenEvents": [ { "event": "stopped", "body": { "reason": "function breakpoint", "threadId": 1, "hitBreakpointIds": [9] } } ] }
            """,
            Threads, Stack(Frame(100, "raise-thunk", null, 0), GoFrame12, MainFrame), AddressOfObj(ObjSlot1)));
        await bed.LaunchAsync();
        var session = bed.Session;

        // Off: the list of function breakpoints is sent empty (DAP usage §4.2).
        await session.SetExceptionSettingsAsync(new ExceptionSettings { BreakOnLanguageExceptions = false }, Ct).WaitAsync(Safety, Ct);
        // Only the ignore list changes: nothing is sent.
        await session.SetExceptionSettingsAsync(new ExceptionSettings { BreakOnLanguageExceptions = false, IgnoredClasses = ["ESecond"] }, Ct).WaitAsync(Safety, Ct);
        // On again: the hook is set anew, and its new breakpoint id is the one that identifies a hook stop.
        await session.SetExceptionSettingsAsync(new ExceptionSettings { IgnoredClasses = ["ESecond"] }, Ct).WaitAsync(Safety, Ct);

        Assert.Equal(
            $"stopped Exception epoch=1 thread=1 loc={Raises}:12:3 hits=[] desc=Exception EFirst: first",
            await bed.Harness.WaitForEventAsync("stopped"));
        Assert.Equal(
        [
            """{"breakpoints":[{"name":"System._Raise"}]}""", """{"breakpoints":[]}""", """{"breakpoints":[{"name":"System._Raise"}]}""",
        ], bed.Harness.SentRequests("setFunctionBreakpoints").Select(json => System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("arguments").GetRawText()));
        await EndAsync(bed);
    }

    [Fact]
    [Trait("Req", "DBG-401")]
    public async Task Settings_WhileIdle_Accepted_AfterEnd_InvalidState()
    {
        await using var bed = Create(Scenario());
        await bed.Session.SetExceptionSettingsAsync(new ExceptionSettings(), Ct).WaitAsync(Safety, Ct);
        await bed.LaunchAsync(new ExceptionSettings { BreakOnLanguageExceptions = false });
        await EndAsync(bed);

        var exception = await Assert.ThrowsAsync<DebuggerException>(
            () => bed.Session.SetExceptionSettingsAsync(new ExceptionSettings(), Ct).WaitAsync(Safety, Ct));

        Assert.Equal(DebugErrorCode.InvalidState, exception.Code);
    }

    // ---- G-056: operating-system exceptions ----

    private static readonly string FaultFrame = Frame(102, "Raises.Go", Raises, 12, "0x7FF6A1B21234");

    [Fact]
    [Trait("Req", "DBG-247")]
    [Trait("Req", "DBG-249")]
    public async Task Os_AccessViolation()
    {
        await using var bed = Create(Scenario(
            HookSet,
            StoppedFirst(OsStop("Exception 0xc0000005 encountered at address 0x7ff6a1b21234: Access violation writing location 0x00000000")),
            Threads, Stack(FaultFrame, MainFrame),
            """{ "on": "disconnect", "respond": {}, "expectArguments": { "terminateDebuggee": true } }"""));

        await bed.LaunchAsync();

        Assert.Equal(
            $"stopped Exception epoch=1 thread=1 loc={Raises}:12:3 hits=[] desc=Access violation at $00007FF6A1B21234",
            await bed.Harness.WaitForEventAsync("stopped"));
        Assert.Equal(
            new ExceptionInfo(ExceptionKind.Os, "", "Access violation", 0xC0000005, 0x7FF6A1B21234),
            bed.Session.CurrentStop!.Exception);

        // The adapter reports only exceptions that no handler takes and would mask the exception on resume: resuming
        // ends the debuggee, with the exception code as its exit code (DBG-247 as amended).
        await bed.Session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        await bed.Harness.WaitForEventAsync("terminated");

        Assert.Equal(unchecked((int)0xC0000005), bed.Session.ExitCode);
        Assert.Equal(
            ["state Stopped->Terminating", "state Terminating->Terminated", "terminated Exited -1073741819 null"],
            bed.Harness.Events.Snapshot().TakeLast(3));
        Assert.DoesNotContain("continue", bed.Harness.SentCommands());
        bed.Harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-247")]
    public async Task Os_StepFromFatalStop_AlsoTerminates()
    {
        await using var bed = Create(Scenario(
            HookSet, StoppedFirst(OsStop("Exception 0xc0000094 encountered at address 0x7ff6a1b21234")), Threads, Stack(FaultFrame, MainFrame)));
        await bed.LaunchAsync();
        Assert.EndsWith("desc=Integer division by zero at $00007FF6A1B21234", await bed.Harness.WaitForEventAsync("stopped"));

        await bed.Session.StepOverAsync(1, Ct).WaitAsync(Safety, Ct);

        Assert.Equal("terminated Exited -1073741676 null", await bed.Harness.WaitForEventAsync("terminated"));
        Assert.DoesNotContain("next", bed.Harness.SentCommands());
        bed.Harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-249")]
    public async Task Os_ThreadName_Ignored()
    {
        // The thread-naming exception is always continued silently.
        await using var bed = Create(Scenario(
            HookSet, StoppedFirst(OsStop("Exception 0x406d1388 encountered at address 0x7ffb00001234")), Threads,
            Stack(Frame(100, "KernelBase.dll`RaiseException", null, 0), GoFrame12, MainFrame),
            """{ "on": "continue", "respond": {}, "expectArguments": { "threadId": 1 } }"""));

        await bed.LaunchAsync();
        await bed.Harness.WaitForResponseAsync("continue");

        Assert.Equal(DebugState.Running, bed.Session.State);
        Assert.Null(bed.Session.CurrentStop);
        await EndAsync(bed);
        Assert.DoesNotContain(bed.Harness.Events.Snapshot(), e => e.StartsWith("stopped", StringComparison.Ordinal));
        Assert.Equal(1, ((IDebugSessionDiagnostics)bed.Session).InternalStopCount);
    }

    [Fact]
    [Trait("Req", "DBG-249")]
    public async Task Os_HardCodedBreakpoint_IsPause()
    {
        await using var bed = Create(Scenario(
            HookSet, StoppedFirst(OsStop("Exception 0x80000003 encountered at address 0x7ff6a1b21234")), Threads, Stack(FaultFrame, MainFrame),
            """{ "on": "continue", "respond": {} }"""));

        await bed.LaunchAsync();

        Assert.Equal(
            $"stopped Pause epoch=1 thread=1 loc={Raises}:12:3 hits=[] desc=Hard-coded breakpoint",
            await bed.Harness.WaitForEventAsync("stopped"));
        Assert.Null(bed.Session.CurrentStop!.Exception);
        // Not fatal: the program goes on.
        await bed.Session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        Assert.Equal(DebugState.Running, bed.Session.State);
        await EndAsync(bed);
    }

    [Fact]
    [Trait("Req", "DBG-248")]
    public async Task PascalCode_AfterHook_Suppressed()
    {
        // The hook reported the raise; nothing handles it, so the adapter stops again with the runtime's exception
        // code on the same thread. That is the same exception: no second stop. The program is about to die and a
        // resume would mask the exception (DBG-247 as amended): the debuggee is ended with the code as exit code.
        await using var bed = Create(Scenario(
            HookSet, StoppedFirst(HookHit), Threads, Stack(HookFrame, GoFrame12, MainFrame), AddressOfObj(ObjSlot1),
            $$"""{ "on": "continue", "respond": {}, "thenEvents": [ {{OsStop("Exception 0xe0504153 encountered at address 0x7ffbe9fd569a")}} ] }""",
            Threads, Stack(Frame(99, "KernelBase.dll`RaiseException", null, 0), HookFrame, GoFrame12, MainFrame),
            """{ "on": "disconnect", "respond": {}, "expectArguments": { "terminateDebuggee": true } }"""));
        await bed.LaunchAsync();
        await bed.Harness.WaitForEventAsync("stopped");

        await bed.Session.ContinueAsync(Ct).WaitAsync(Safety, Ct);

        Assert.Equal("terminated Exited -531611309 null", await bed.Harness.WaitForEventAsync("terminated"));
        Assert.Equal(unchecked((int)PascalCode), bed.Session.ExitCode);
        Assert.Equal(
            ["state Stopped->Running", "continued", "state Running->Terminating", "state Terminating->Terminated", "terminated Exited -531611309 null"],
            bed.Harness.Events.Snapshot().TakeLast(5));
        Assert.Single(bed.Harness.Events.Snapshot(), e => e.StartsWith("stopped", StringComparison.Ordinal));
        Assert.Single(bed.Harness.SentRequests("continue"));
        bed.Harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-246")]
    [Trait("Req", "DBG-248")]
    public async Task PascalCode_WithoutHook_LanguageStop()
    {
        // Notification through the hook is off: an unhandled Pascal exception is seen only as the runtime's
        // exception code. The object is read from the frame of the raise routine through the type information.
        await using var bed = Create(Scenario(
            StoppedFirst(OsStop("Exception 0xe0504153 encountered at address 0x7ffbe9fd569a")), Threads,
            Stack(Frame(99, "KernelBase.dll`RaiseException", null, 0, "0x7FFBE9FD569A"), Frame(100, "System._Raise", SystemUnit, 2990), GoFrame12, MainFrame),
            AddressOfObj(ObjSlot1)));

        await bed.LaunchAsync(new ExceptionSettings { BreakOnLanguageExceptions = false });

        Assert.Equal(
            $"stopped Exception epoch=1 thread=1 loc={Raises}:12:3 hits=[] desc=Exception EFirst: first",
            await bed.Harness.WaitForEventAsync("stopped"));
        Assert.Equal(new ExceptionInfo(ExceptionKind.Language, "EFirst", "first", 0, First), bed.Session.CurrentStop!.Exception);

        // Unhandled: resuming ends the debuggee.
        await bed.Session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        Assert.Equal("terminated Exited -531611309 null", await bed.Harness.WaitForEventAsync("terminated"));
        bed.Harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Req", "DBG-247")]
    public async Task BreakOnOs_False_Continues()
    {
        // DBG-247 as amended after the adapter spike: the adapter reports only exceptions that no handler takes, so
        // the stop is published whatever BreakOnOsExceptions says (the program is about to die), and what continues is
        // the session's handling of the user's Continue: the debuggee ends with the exception code.
        await using var bed = Create(Scenario(
            HookSet, StoppedFirst(OsStop("Exception 0xc00000fd encountered at address 0x7ff6a1b21234")), Threads, Stack(FaultFrame, MainFrame)));

        await bed.LaunchAsync(new ExceptionSettings { BreakOnOsExceptions = false });

        Assert.Equal(
            $"stopped Exception epoch=1 thread=1 loc={Raises}:12:3 hits=[] desc=Stack overflow at $00007FF6A1B21234",
            await bed.Harness.WaitForEventAsync("stopped"));
        await bed.Session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        Assert.Equal("terminated Exited -1073741571 null", await bed.Harness.WaitForEventAsync("terminated"));
        Assert.DoesNotContain("continue", bed.Harness.SentCommands());
        bed.Harness.AssertScenarioComplete();
    }

    // ---- the pure parts ----

    [Theory]
    [InlineData("Exception 0xc0000005 encountered at address 0x7ff6a1b21234", null, 0xC0000005u)]
    [InlineData("at address 0x7ff6a1b21234: code 0xC0000094", null, 0xC0000094u)]   // an address is not a code
    [InlineData(null, "Exception 0xe0504153 encountered", 0xE0504153u)]            // text when the description has none
    [InlineData("stopped", "no code here", 0u)]
    [InlineData(null, null, 0u)]
    [Trait("Req", "DBG-247")]
    public void ParseCode_FirstEightDigitToken(string? description, string? text, uint expected) =>
        Assert.Equal(expected, ExceptionMonitor.ParseCode(description, text));

    [Theory]
    [InlineData(0xC0000005u, "Access violation")]
    [InlineData(0xC0000094u, "Integer division by zero")]
    [InlineData(0xC0000095u, "Integer overflow")]
    [InlineData(0xC000008Cu, "Array bounds exceeded")]
    [InlineData(0xC000008Eu, "Floating-point division by zero")]
    [InlineData(0xC0000090u, "Invalid floating-point operation")]
    [InlineData(0xC0000091u, "Floating-point overflow")]
    [InlineData(0xC0000093u, "Floating-point underflow")]
    [InlineData(0xC00000FDu, "Stack overflow")]
    [InlineData(0xC000001Du, "Illegal instruction")]
    [InlineData(0xC0000096u, "Privileged instruction")]
    [InlineData(0xDEADBEEFu, "Exception $DEADBEEF")]
    [InlineData(0u, "Exception $00000000")]
    [Trait("Req", "DBG-249")]
    public void OsText_Table(uint code, string expected) => Assert.Equal(expected, ExceptionMonitor.OsText(code));

    [Theory]
    [InlineData("EAbort", true)]
    [InlineData("eabort", true)]
    [InlineData("EAbortX", false)]
    [InlineData("", false)]
    [Trait("Req", "DBG-244")]
    public void IsIgnored_DefaultList_ExactCaseInsensitive(string className, bool expected) =>
        Assert.Equal(expected, ExceptionMonitor.IsIgnored(new ExceptionSettings(), className));
}
