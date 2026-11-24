using System.Buffers.Binary;
using System.Text;
using FreexComp.Debugger.Session;
using FreexComp.Debugger.Tests.TestSupport;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// The ignore list matches the dynamic class of an exception or any of its ancestors (DBG-244 as amended, G-081):
/// the chain comes from the type information, for a class it does not describe from the parent pointers of the class
/// tables, and when neither gives a chain only the class itself is compared.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExceptionIgnoreTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Raises = @"C:\work\Raises\Raises.pas";
    private const string SystemUnit = @"C:\rtl\System.pas";

    // ---- the debuggee: type information and memory ----

    /// <summary>
    /// Described classes: <c>TObject</c> ← <c>Exception</c> ← <c>EFirst</c> ← <c>EChild</c>, and <c>ESecond</c> next to
    /// <c>EFirst</c>. The program also has classes the type information does not describe (see <see cref="Memory"/>).
    /// </summary>
    private static readonly string Pdi = $$"""
        { "formatVersion": 1, "program": "Raises", "container": "codeview", "abi": {{PdiFixture.Abi}},
          "types": [
            { "id": 1, "name": "TObject", "unit": "System", "kind": "class", "size": 8, "ancestor": null, "instanceSize": 8, "fields": [] },
            { "id": 2, "name": "string", "kind": "string", "size": 8, "flavor": "unicode" },
            { "id": 3, "name": "Exception", "unit": "SysUtils", "kind": "class", "size": 8, "ancestor": 1, "instanceSize": 16,
              "fields": [ { "name": "FMessage", "type": 2, "offset": 8, "visibility": "private" } ] },
            { "id": 4, "name": "EFirst", "unit": "Raises", "kind": "class", "size": 8, "ancestor": 3, "instanceSize": 16, "fields": [] },
            { "id": 5, "name": "ESecond", "unit": "Raises", "kind": "class", "size": 8, "ancestor": 3, "instanceSize": 16, "fields": [] },
            { "id": 6, "name": "Pointer", "kind": "pointer", "size": 8, "to": null },
            { "id": 7, "name": "EChild", "unit": "Raises", "kind": "class", "size": 8, "ancestor": 4, "instanceSize": 16, "fields": [] } ],
          "units": [
            { "name": "Raises", "file": "C:\\work\\Raises\\Raises.pas", "globals": [],
              "routines": [
                { "displayName": "Go", "linkageName": "Raises.Go", "file": "C:\\work\\Raises\\Raises.pas",
                  "line": 8, "bodyLine": 9, "endLine": 21, "self": null, "vars": [] },
                { "displayName": "Raises", "linkageName": "_start", "file": "C:\\work\\Raises\\Raises.pas",
                  "line": 23, "bodyLine": 23, "endLine": 25, "self": null, "vars": [] } ] },
            { "name": "System", "file": "C:\\rtl\\System.pas", "globals": [],
              "routines": [
                { "displayName": "_Raise", "linkageName": "System._Raise", "file": "C:\\rtl\\System.pas",
                  "line": 2982, "bodyLine": 2986, "endLine": 2997, "self": null,
                  "vars": [ { "name": "Obj", "kind": "param", "type": 6 }, { "name": "Addr", "kind": "param", "type": 6 } ] } ] } ] }
        """;

    // Storage of the hook's first parameter, one per exception object.
    private const ulong FirstSlot = 0x50000;
    private const ulong SecondSlot = 0x50100;
    private const ulong ChildSlot = 0x50200;
    private const ulong OtherSlot = 0x50300;     // EOther: not described; its class tables lead to Exception
    private const ulong LostSlot = 0x50400;      // ELost: not described; its parent class table cannot be read

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
    /// A parameter slot holds the instance; the instance holds its class table and the message (none here); a class
    /// table holds the parent class table (+0) and the class name (+16).
    /// </summary>
    private static readonly string Memory = "[ " + string.Join(", ",
        Region(FirstSlot, Pointers(0x10000)),
        Region(SecondSlot, Pointers(0x10100)),
        Region(ChildSlot, Pointers(0x10200)),
        Region(OtherSlot, Pointers(0x10300)),
        Region(LostSlot, Pointers(0x10400)),
        Region(0x10000, Pointers(0x20000, 0)),               // an EFirst
        Region(0x10100, Pointers(0x20100, 0)),               // an ESecond
        Region(0x10200, Pointers(0x20200, 0)),               // an EChild
        Region(0x10300, Pointers(0x20300, 0)),               // an EOther
        Region(0x10400, Pointers(0x20500, 0)),               // an ELost
        Region(0x20000, Pointers(0x21000, 0, 0x40010)),      // EFirst ← Exception
        Region(0x20100, Pointers(0x21000, 0, 0x40110)),      // ESecond ← Exception
        Region(0x20200, Pointers(0x20000, 0, 0x40210)),      // EChild ← EFirst
        Region(0x20300, Pointers(0x20400, 0, 0x40310)),      // EOther ← EMiddle
        Region(0x20400, Pointers(0x21000, 0, 0x40410)),      // EMiddle ← Exception
        Region(0x20500, Pointers(0x99000, 0, 0x40510)),      // ELost ← (nothing readable)
        Region(0x21000, Pointers(0x22000, 0, 0x40610)),      // Exception ← TObject
        Region(0x22000, Pointers(0, 0, 0x40710)),            // TObject
        StringRegion(0x40010, "EFirst"),
        StringRegion(0x40110, "ESecond"),
        StringRegion(0x40210, "EChild"),
        StringRegion(0x40310, "EOther"),
        StringRegion(0x40410, "EMiddle"),
        StringRegion(0x40510, "ELost"),
        StringRegion(0x40610, "Exception"),
        StringRegion(0x40710, "TObject")) + " ]";

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

    private const string Continue = """{ "on": "continue", "respond": {}, "expectArguments": { "threadId": 1 } }""";

    private static string Frame(int id, string name, string path, int line) =>
        $$"""{ "id": {{id}}, "name": "{{name}}", "line": {{line}}, "column": 3, "instructionPointerReference": "0x7FF6A1B21234", "source": { "name": "{{Path.GetFileName(path)}}", "path": "{{path.Replace(@"\", @"\\")}}" } }""";

    private static readonly string HookStack = $$"""
        { "on": "stackTrace", "respond": { "totalFrames": 3, "stackFrames": [
            {{Frame(100, "System._Raise", SystemUnit, 2986)}}, {{Frame(102, "Raises.Go", Raises, 12)}}, {{Frame(103, "_start", Raises, 24)}} ] } }
        """;

    private static string AddressOfObj(ulong slot) => $$"""
        { "on": "evaluate", "expectArguments": { "expression": "&Obj", "frameId": 100, "context": "watch" },
          "respond": { "result": "0x{{slot:x16}}", "type": "void **", "variablesReference": 0 } }
        """;

    /// <summary>The program stops in the raise routine with the object of <paramref name="slot"/>; then <paramref name="more"/>.</summary>
    private static string RaiseOf(ulong slot, params string[] more)
    {
        string[] steps =
        [
            HookSet, $$"""{ "on": "configurationDone", "respond": {}, "thenEvents": [ {{HookHit}} ] }""",
            Threads, HookStack, AddressOfObj(slot), .. more,
        ];
        return $$"""{ "capabilities": {{Capabilities}}, "memory": {{Memory}}, "steps": [ {{string.Join(", ", steps)}} ] }""";
    }

    private sealed class Bed(string scenario) : IAsyncDisposable
    {
        private readonly string _pdiPath = PdiFixture.WriteTemp("Raises.pdi", Pdi);

        public SessionHarness Harness { get; } = SessionHarness.Create(scenario);

        public DebugSession Session => Harness.Session;

        public Task LaunchAsync(params string[] ignored) => Harness.LaunchAsync(new LaunchRequest
        {
            Program = SessionHarness.Program,
            TypeInfoPath = _pdiPath,
            Exceptions = new ExceptionSettings { IgnoredClasses = ignored },
        });

        /// <summary>The raise was resolved by the session itself: resumed, nothing published.</summary>
        public async Task AssertIgnoredAsync(int resumes = 1)
        {
            await Harness.WaitForResponsesAsync("continue", resumes);
            Assert.Equal(DebugState.Running, Session.State);
            await EndAsync();
            Assert.Equal(resumes - 1, Harness.Events.Snapshot().Count(e => e.StartsWith("stopped", StringComparison.Ordinal)));
        }

        public async Task EndAsync()
        {
            await Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
            await Harness.WaitForEventAsync("terminated");   // the last event: everything before it has been delivered
            Harness.AssertScenarioComplete();
        }

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

    // ---- the chain of the type information ----

    [Theory]
    [InlineData("EFirst")]       // the parent
    [InlineData("exception")]    // an ancestor further up; the list is compared case-insensitively
    [InlineData("TObject")]      // the root
    [InlineData("echild")]       // the class itself, as before
    [Trait("Req", "DBG-244")]
    public async Task DescendantOfIgnoredClass_IsIgnored(string entry)
    {
        await using var bed = new Bed(RaiseOf(ChildSlot, Continue));

        await bed.LaunchAsync("ENever", entry);

        await bed.AssertIgnoredAsync();
        Assert.Equal(1, ((IDebugSessionDiagnostics)bed.Session).InternalStopCount);
    }

    [Theory]
    [InlineData(SecondSlot, "EFirst", "ESecond")]    // a sibling of the listed class
    [InlineData(FirstSlot, "EChild", "EFirst")]      // the ancestor of a listed class: matching goes upwards only
    [InlineData(FirstSlot, "EFirs", "EFirst")]       // a whole name, not a prefix
    [Trait("Req", "DBG-244")]
    public async Task UnrelatedClass_StillStops(ulong slot, string entry, string raisedClass)
    {
        await using var bed = new Bed(RaiseOf(slot));

        await bed.LaunchAsync(entry);

        Assert.Equal(
            $"stopped Exception epoch=1 thread=1 loc={Raises}:12:3 hits=[] desc=Exception {raisedClass}",
            await bed.Harness.WaitForEventAsync("stopped"));
        Assert.Equal(raisedClass, bed.Session.CurrentStop!.Exception!.ClassName);
        Assert.DoesNotContain("continue", bed.Harness.SentCommands());
        await bed.EndAsync();
    }

    // ---- a class the type information does not describe ----

    [Theory]
    [InlineData("EMiddle")]      // the parent, known only from the class tables
    [InlineData("Exception")]    // where the class tables reach the described classes
    [InlineData("tobject")]      // … and on from there
    [InlineData("EOther")]       // the class itself
    [Trait("Req", "DBG-244")]
    public async Task UndescribedClass_ParentWalk_IsIgnored(string entry)
    {
        await using var bed = new Bed(RaiseOf(OtherSlot, Continue));

        await bed.LaunchAsync(entry);

        await bed.AssertIgnoredAsync();
    }

    [Fact]
    [Trait("Req", "DBG-244")]
    public async Task NoClassInfo_FallsBackToExactMatch()
    {
        // ELost is not described and its parent class table cannot be read: there is no chain, so only its own name
        // counts. The same raise comes twice.
        await using var bed = new Bed(RaiseOf(
            LostSlot,
            $$"""{ "on": "continue", "respond": {}, "expectArguments": { "threadId": 1 }, "thenEvents": [ {{HookHit}} ] }""",
            Threads, HookStack, AddressOfObj(LostSlot), Continue));

        // Entries that would match an ancestor do not apply: the raise stops.
        await bed.LaunchAsync("Exception", "TObject");
        Assert.Equal(
            $"stopped Exception epoch=1 thread=1 loc={Raises}:12:3 hits=[] desc=Exception ELost",
            await bed.Harness.WaitForEventAsync("stopped"));

        // Its own name (case-insensitive) does: the second raise is resolved by the session.
        await bed.Session.SetExceptionSettingsAsync(new ExceptionSettings { IgnoredClasses = ["elost"] }, Ct).WaitAsync(Safety, Ct);
        await bed.Session.ContinueAsync(Ct).WaitAsync(Safety, Ct);
        await bed.AssertIgnoredAsync(resumes: 2);
    }

    // ---- the rule itself ----

    [Theory]
    [InlineData(new[] { "EChild", "EFirst", "Exception", "TObject" }, "EFIRST", true)]
    [InlineData(new[] { "EChild", "EFirst", "Exception", "TObject" }, "ESecond", false)]
    [InlineData(new[] { "EChild" }, "echild", true)]
    [InlineData(new[] { "EChild" }, "Exception", false)]
    [InlineData(new[] { "" }, "", false)]                 // a class name that could not be read is never ignored
    [InlineData(new string[0], "Exception", false)]
    [Trait("Req", "DBG-244")]
    public void IsIgnored_ClassOrAncestor(string[] classes, string entry, bool expected) =>
        Assert.Equal(expected, ExceptionMonitor.IsIgnored(new ExceptionSettings { IgnoredClasses = [entry] }, classes));
}
