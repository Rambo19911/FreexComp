using FreexComp.Debugger.Expressions;
using FreexComp.Debugger.Tests.TestSupport;
using FreexComp.Debugger.Values;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// Scopes, handles, lazy children, raw and error nodes, <c>EvaluateName</c> (Pascal values §1, §8; DAP usage §5.4,
/// §5.5; features DBG-256).
/// </summary>
public sealed class ValueServiceTests
{
    private static readonly TimeSpan Safety = SessionHarness.Safety;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Line(VariableInfo info) => $"{info.Name} = {info.Value} : {info.TypeName} [{info.Kind}]";

    private static string[] Lines(IEnumerable<VariableInfo> infos) => [.. infos.Select(Line)];

    // ---- scopes ----

    /// <summary>The session stopped in <c>Factorial</c> on line 40; the adapter lists N and Result in its Locals scope.</summary>
    private static async Task<SessionHarness> StoppedInFactorialAsync(string moreSteps = "")
    {
        var harness = SessionHarness.Create($$"""
            { "variables": { "N": "0x7F3A6FFA00", "Result": "0x7F3A6FFA04" },
              "memory": [ { "address": "0x7F3A6FFA00", "hex": "0200000000000000" } ],
              "steps": [
                { "on": "configurationDone", "respond": {}, "thenEvents": [ { "event": "stopped", "body": { "reason": "step", "threadId": 1 } } ] },
                { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
                { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 524288, "name": "Factorial", "line": 40, "column": 5, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } } ] } },
                { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 524288, "name": "Factorial", "line": 40, "column": 5, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } } ] } }
                {{moreSteps}} ] }
            """);
        await harness.LaunchAsync();
        await harness.WaitForEventAsync("stopped");
        return harness;
    }

    private const string FactorialScopes = """
        , { "on": "scopes", "expectArguments": { "frameId": 524288 }, "respond": { "scopes": [
              { "name": "Locals", "presentationHint": "locals", "variablesReference": 1, "expensive": false },
              { "name": "Globals", "variablesReference": 2, "expensive": false },
              { "name": "Registers", "presentationHint": "registers", "variablesReference": 3, "expensive": false } ] } },
          { "on": "variables", "expectArguments": { "variablesReference": 1 }, "respond": { "variables": [
              { "name": "N", "value": "2", "type": "Integer", "variablesReference": 0, "evaluateName": "N" },
              { "name": "Result", "value": "0", "type": "Integer", "variablesReference": 0 } ] } },
          { "on": "variables", "expectArguments": { "variablesReference": 2 }, "respond": { "variables": [] } }
        """;

    private static async Task EndAsync(SessionHarness harness)
    {
        await harness.Session.TerminateAsync(Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("terminated");
        harness.AssertScenarioComplete();
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-079")]
    [Trait("Req", "DBG-256")]
    public async Task Scopes_SplitBySidecar()
    {
        await using var harness = await StoppedInFactorialAsync(FactorialScopes);
        var session = harness.Session;
        var frame = Assert.Single((await session.GetStackTraceAsync(1, ct: Ct).WaitAsync(Safety, Ct)).Frames);
        var sentBefore = harness.SentCommands().Count;

        var scopes = await session.GetScopesAsync(frame.Id, Ct).WaitAsync(Safety, Ct);

        // Always exactly three, in this order, with our own handles of the current stop; nothing is requested yet.
        Assert.Equal([ScopeKind.Arguments, ScopeKind.Locals, ScopeKind.Globals], scopes.Select(s => s.Kind));
        Assert.Equal(["Arguments", "Locals", "Globals"], scopes.Select(s => s.Name));
        Assert.All(scopes, scope => Assert.Equal(1, scope.Handle.Epoch));
        Assert.Equal(3, scopes.Select(s => s.Handle.Id).Distinct().Count());
        Assert.Equal(sentBefore, harness.SentCommands().Count);

        var arguments = await session.GetChildrenAsync(scopes[0].Handle, ct: Ct).WaitAsync(Safety, Ct);
        var locals = await session.GetChildrenAsync(scopes[1].Handle, ct: Ct).WaitAsync(Safety, Ct);
        var globals = await session.GetChildrenAsync(scopes[2].Handle, ct: Ct).WaitAsync(Safety, Ct);

        // The adapter has one Locals scope; parameters and locals are split by the type information.
        Assert.Equal(["N = 2 : Integer [Integer]"], Lines(arguments));
        Assert.Equal(["Result = 0 : Integer [Integer]"], Lines(locals));
        Assert.Empty(globals);
        var n = arguments[0];
        Assert.Null(n.Children);
        Assert.Equal(0, n.ChildCount);
        Assert.True(n.CanModify);
        Assert.Equal("N", n.EvaluateName);
        Assert.Equal(0x7F3A6FFA00ul, n.Address);

        // One "scopes" request and one "variables" request per adapter scope, each carrying only the reference (DBG-080).
        Assert.Equal("""{"frameId":524288}""", harness.SentArguments("scopes"));
        Assert.Equal(
            ["""{"variablesReference":1}""", """{"variablesReference":2}"""],
            harness.SentRequests("variables").Select(json => System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("arguments").GetRawText()));
        // Addresses come from "&Name" in the adapter's frame; values from memory.
        Assert.Contains(harness.SentRequests("evaluate"), json => json.Contains("\"expression\":\"&N\"", StringComparison.Ordinal));
        Assert.Contains("readMemory", harness.SentCommands());
        await EndAsync(harness);
    }

    [Fact]
    [Trait("Req", "DBG-256")]
    public async Task Scopes_Order_SelfFirst_ResultLast()
    {
        var test = ZooBed.Test();
        var speak = ZooBed.Speak();

        // Parameters in declaration order; the hidden upper bound of the open array is not a parameter of its own.
        Assert.Equal(["A", "V"], (await test.ScopeAsync(ScopeKind.Arguments)).Select(v => v.Name));
        var locals = await test.ScopeAsync(ScopeKind.Locals);
        Assert.Equal("B", locals[0].Name);
        Assert.Equal("Result", locals[^1].Name);
        Assert.Equal(["Counter = 5 : Integer [Integer]"], Lines(await test.ScopeAsync(ScopeKind.Globals)));

        Assert.Equal(["Self"], (await speak.ScopeAsync(ScopeKind.Arguments)).Select(v => v.Name));
        Assert.Equal(["Legs", "Result"], (await speak.ScopeAsync(ScopeKind.Locals)).Select(v => v.Name));
        // The open-array parameter takes its length from the hidden variable (DBG-164).
        Assert.Equal("A = (5, 6, 7) : array of Integer [DynamicArray]", Line((await test.ScopeAsync(ScopeKind.Arguments))[0]));
    }

    [Fact]
    [Trait("Req", "DBG-079")]
    public async Task Scopes_DemoLocals_AsTheScriptedTestExpects()
    {
        var bed = EvalBed.Demo();

        Assert.Empty(await bed.ScopeAsync(ScopeKind.Arguments));
        Assert.Empty(await bed.ScopeAsync(ScopeKind.Globals));
        var locals = await bed.ScopeAsync(ScopeKind.Locals);
        var pet = locals.Single(v => v.Name == "Pet");
        var total = locals.Single(v => v.Name == "PTotal");
        Assert.Equal(
        [
            "I = 3 : Integer [Integer]",
            "Total = 24 : Integer [Integer]",
            "Ch = 'A' : Char [Char]",
            "Ok = True : Boolean [Boolean]",
            "Color = clGreen : TColor [Enum]",
            "Colors = [clRed, clBlue] : TColors [Set]",
            "S = 'Héllo' : string [String]",
            "P = (X: 3; Y: 4) : TPoint [Record]",
            "Nums = (10, 20, 30) : array of Integer [DynamicArray]",
            "Pet = TDog($000001F3A2B41C60) : TDog [ClassInstance]",
            "PTotal = $0000007F3A6FFB04 : ^Integer [Pointer]",
        ], Lines(locals));
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 2, 3, 2, 1], locals.Select(v => v.ChildCount));
        Assert.Equal(["Name = 'Rex' : string [String]", "Legs = 4 : Integer [Integer]"], Lines(await bed.ChildrenAsync(pet.Children!.Value)));
        Assert.Equal(["^ = 24 : Integer [Integer]"], Lines(await bed.ChildrenAsync(total.Children!.Value)));
        // DBG-176: scalars and pointers with a known address; not strings, sets, structured values, instance references.
        Assert.Equal(["I", "Total", "Ch", "Ok", "Color", "PTotal"], locals.Where(v => v.CanModify).Select(v => v.Name));
    }

    // ---- handles and lazy children ----

    [Fact]
    [Trait("Req", "DBG-411")]
    [Trait("Req", "DBG-151")]
    public async Task Children_Lazy_Paged()
    {
        var bed = ZooBed.Test();
        var locals = await bed.ScopeAsync(ScopeKind.Locals);
        var big = locals.Single(v => v.Name == "Big250");
        var element200 = $"{ZooBed.Heap + 0x1010 + 4 * 200:X}+4";

        // The summary read the first elements only; no element beyond it has been touched.
        Assert.Equal("(0, 2, 4, 6, 8, 10, 12, 14, …) (Length: 250)", big.Value);
        Assert.Equal(250, big.ChildCount);
        Assert.DoesNotContain(element200, bed.Debuggee.Memory.Reads);

        var firstPage = await bed.ChildrenAsync(big.Children!.Value);
        Assert.Equal(100, firstPage.Count);                               // count 0 = one page (ChildPageSize)
        Assert.Equal("[0] = 0 : Integer [Integer]", Line(firstPage[0]));
        Assert.Equal("[99] = 198 : Integer [Integer]", Line(firstPage[99]));
        Assert.DoesNotContain(element200, bed.Debuggee.Memory.Reads);

        var lastPage = await bed.ChildrenAsync(big.Children.Value, start: 200);
        Assert.Equal(50, lastPage.Count);
        Assert.Equal("[200] = 400 : Integer [Integer]", Line(lastPage[0]));
        Assert.Contains(element200, bed.Debuggee.Memory.Reads);
        Assert.Equal(["[10]", "[11]", "[12]", "[13]", "[14]"], (await bed.ChildrenAsync(big.Children.Value, start: 10, count: 5)).Select(v => v.Name));
        Assert.Empty(await bed.ChildrenAsync(big.Children.Value, start: 250));
        Assert.Equal(250, (await bed.ChildrenAsync(big.Children.Value, start: -5, count: 1000)).Count);

        // Handles are ours, numbered from 1 in this stop; the same child keeps its handle.
        var points = locals.Single(v => v.Name == "Pts");
        var first = (await bed.ChildrenAsync(points.Children!.Value))[1];
        var again = (await bed.ChildrenAsync(points.Children.Value, start: 1, count: 1))[0];
        Assert.Equal("[1] = (X: 3; Y: 4) : TPoint [Record]", Line(first));
        Assert.Equal(first.Children, again.Children);
        Assert.Equal(["X = 3 : Integer [Integer]", "Y = 4 : Integer [Integer]"], Lines(await bed.ChildrenAsync(first.Children!.Value)));
        Assert.Equal(1, first.Children.Value.Epoch);
        Assert.True(bed.Values.GetScopes(bed.Frame)[0].Handle.Id > first.Children.Value.Id);

        // A custom page size.
        var small = ZooBed.Test(new ValueFormatOptions { ChildPageSize = 7 });
        Assert.Equal(7, (await small.ScopeAsync(ScopeKind.Locals)).Count);      // a scope is paged like any other node
        var sameArray = (await small.ChildrenAsync(small.Values.GetScopes(small.Frame)[1].Handle, 0, 1000)).Single(v => v.Name == "Big250");
        Assert.Equal(7, (await small.ChildrenAsync(sameArray.Children!.Value)).Count);
    }

    // ---- raw and error nodes ----

    [Fact]
    [Trait("Req", "DBG-081")]
    [Trait("Req", "DBG-150")]
    public async Task UnknownVariable_Raw()
    {
        var bed = EvalBed.Demo();
        var debuggee = bed.Debuggee;
        // Listed by the adapter, unknown to the type information: with the adapter's own children.
        debuggee.Listed[ScopeKind.Locals].Add(new RawVariable("Mystery", "{...}", "struct foo", Reference: 7));
        debuggee.RawChildren[7] = [new RawVariable("a", "1", "int"), new RawVariable("b", "{...}", "bar", Reference: 8)];
        debuggee.RawChildren[8] = [new RawVariable("c", "0x10", null)];
        // Known to the type information but not listed by the adapter.
        debuggee.Listed[ScopeKind.Locals].RemoveAll(v => v.Name == "Total");
        // Listed, but its address cannot be obtained.
        debuggee.Variables.Remove("S");

        var locals = await bed.ScopeAsync(ScopeKind.Locals);

        Assert.Equal("Total = <not available> : Integer [Error]", Line(locals.Single(v => v.Name == "Total")));
        Assert.Equal("S = <raw S> : ? [Raw]", Line(locals.Single(v => v.Name == "S")));
        var mystery = locals[^1];
        Assert.Equal("Mystery = {...} : struct foo [Raw]", Line(mystery));
        Assert.Equal(2, mystery.ChildCount);
        foreach (var node in new[] { locals.Single(v => v.Name == "Total"), locals.Single(v => v.Name == "S"), mystery })
        {
            Assert.Null(node.EvaluateName);      // raw and error nodes are not addressable (DBG-175)
            Assert.Null(node.Address);
            Assert.False(node.CanModify);
        }

        var children = await bed.ChildrenAsync(mystery.Children!.Value);
        Assert.Equal(["a = 1 : int [Raw]", "b = {...} : bar [Raw]"], Lines(children));
        Assert.Null(children[0].Children);
        Assert.Equal(["c = 0x10 :  [Raw]"], Lines(await bed.ChildrenAsync(children[1].Children!.Value)));
        // The other variables are decoded as usual: an error or raw node never spoils its siblings.
        Assert.Equal("Nums = (10, 20, 30) : array of Integer [DynamicArray]", Line(locals.Single(v => v.Name == "Nums")));
    }

    [Fact]
    [Trait("Req", "DBG-079")]
    public async Task NoTypeInformationForTheFrame_EverythingRawUnderLocals()
    {
        var debuggee = DemoMemory.Load();
        debuggee.Listed[ScopeKind.Globals].Add(new RawVariable("g", "7", "int"));
        // A frame without a routine (no type information for its code), and a session without any type information.
        foreach (var values in new[]
                 {
                     new ValueService(PdiFixture.LoadDbgDemo(), debuggee, new ValueFormatOptions(), canWriteMemory: true),
                     new ValueService(null, debuggee, new ValueFormatOptions(), canWriteMemory: true),
                 })
        {
            values.Reset(1);
            var scopes = values.GetScopes(new FrameContext(DemoMemory.AdapterFrameId, null, 71));

            Assert.Equal([ScopeKind.Arguments, ScopeKind.Locals, ScopeKind.Globals], scopes.Select(s => s.Kind));
            Assert.Empty(await values.GetChildrenAsync(scopes[0].Handle, 0, 0, Ct));
            var locals = await values.GetChildrenAsync(scopes[1].Handle, 0, 0, Ct);
            Assert.Equal(11, locals.Count);
            Assert.All(locals, v => Assert.Equal(ValueKind.Raw, v.Kind));
            Assert.Equal("Total = <raw Total> : ? [Raw]", Line(locals[1]));
            Assert.Equal(["g = 7 : int [Raw]"], Lines(await values.GetChildrenAsync(scopes[2].Handle, 0, 0, Ct)));
        }
    }

    [Fact]
    [Trait("Req", "DBG-081")]
    public async Task DollarNames_Hidden()
    {
        var bed = ZooBed.Test();
        var debuggee = bed.Debuggee;
        // Compiler temporaries the adapter lists (DBG-139), in any scope.
        debuggee.Listed[ScopeKind.Locals].Add(new RawVariable("$with1", "0x1", "ptr"));
        debuggee.Listed[ScopeKind.Locals].Add(new RawVariable("$except1", "0x2", "ptr"));
        debuggee.Listed[ScopeKind.Globals].Add(new RawVariable("$init", "0", "int"));
        debuggee.Listed[ScopeKind.Locals].Add(new RawVariable("Visible", "1", "int"));

        var names = (await bed.ScopeAsync(ScopeKind.Arguments)).Concat(await bed.ScopeAsync(ScopeKind.Locals))
            .Concat(await bed.ScopeAsync(ScopeKind.Globals)).Select(v => v.Name).ToList();

        Assert.DoesNotContain(names, name => name.Contains('$'));     // neither temporaries nor the open array's "A$high"
        Assert.Contains("Visible", names);
        Assert.Contains("A", names);

        // Without type information for the frame the rule is the same.
        var raw = new ValueService(null, debuggee, new ValueFormatOptions(), canWriteMemory: true);
        raw.Reset(1);
        var listed = await raw.GetChildrenAsync(raw.GetScopes(bed.Frame with { Routine = null })[1].Handle, 0, 1000, Ct);
        Assert.DoesNotContain(listed, v => v.Name.StartsWith('$'));
        Assert.Contains(listed, v => v.Name == "Visible");

        // A "$" name is not an identifier of the expression language either.
        var exception = await Assert.ThrowsAsync<DebuggerException>(() => bed.EvalAsync("$with1"));
        Assert.Equal(DebugErrorCode.ExpressionSyntax, exception.Code);
    }

    // ---- EvaluateName ----

    [Fact]
    [Trait("Req", "DBG-175")]
    public async Task EvaluateName_RoundTrips()
    {
        foreach (var bed in new[] { EvalBed.Demo(), ZooBed.Test(), ZooBed.Speak() })
        {
            var visited = 0;
            foreach (var kind in new[] { ScopeKind.Arguments, ScopeKind.Locals, ScopeKind.Globals })
            {
                foreach (var node in await bed.ScopeAsync(kind))
                    visited += await CheckAsync(bed, node, depth: 0);
            }

            Assert.True(visited > 3);
        }

        // The forms of §8.3.
        var demo = EvalBed.Demo();
        var locals = await demo.ScopeAsync(ScopeKind.Locals);
        async Task<string?> ChildNameAsync(string parent, int index) =>
            (await demo.ChildrenAsync(locals.Single(v => v.Name == parent).Children!.Value))[index].EvaluateName;
        Assert.Equal("Total", locals.Single(v => v.Name == "Total").EvaluateName);
        Assert.Equal("Nums[1]", await ChildNameAsync("Nums", 1));
        Assert.Equal("Pet.Name", await ChildNameAsync("Pet", 0));
        Assert.Equal("P.Y", await ChildNameAsync("P", 1));
        Assert.Equal("PTotal^", await ChildNameAsync("PTotal", 0));

        var zoo = ZooBed.Test();
        var zooLocals = await zoo.ScopeAsync(ScopeKind.Locals);
        async Task<string[]> ChildNamesAsync(string parent) =>
            [.. (await zoo.ChildrenAsync(zooLocals.Single(v => v.Name == parent).Children!.Value)).Select(v => v.EvaluateName ?? "<none>")];
        Assert.Equal(["ByColor[clRed]", "ByColor[clGreen]", "ByColor[clBlue]"], await ChildNamesAsync("ByColor"));
        Assert.Equal(["ByLetter['a']", "ByLetter['b']", "ByLetter['c']"], await ChildNamesAsync("ByLetter"));
        Assert.Equal(["Matrix[0]", "Matrix[1]"], await ChildNamesAsync("Matrix"));
    }

    /// <summary>Every addressable node's EvaluateName evaluates, in the same frame, to the same location and value.</summary>
    private static async Task<int> CheckAsync(EvalBed bed, VariableInfo node, int depth)
    {
        var visited = 0;
        if (node.EvaluateName is { } name)
        {
            var location = Assert.IsType<LocationValue>(await bed.ValueAsync(name));
            Assert.Equal(node.Address, location.Address);
            var again = await bed.EvalAsync(name);
            Assert.Equal(node.Value, again.Value);
            Assert.Equal(node.TypeName, again.TypeName);
            Assert.Equal(node.Kind, again.Kind);
            Assert.Equal(name, again.EvaluateName);
            Assert.Equal(node.CanModify, again.CanModify);
            visited++;
        }
        else
        {
            Assert.True(node.Kind is ValueKind.Raw or ValueKind.Error, $"{node.Name} has no EvaluateName");
        }

        if (node.Children is { } children && depth < 3)
        {
            foreach (var child in await bed.ChildrenAsync(children, 0, 12))
                visited += await CheckAsync(bed, child, depth + 1);
        }

        return visited;
    }

    // ---- stale handles ----

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "DBG-408")]
    public async Task StaleHandle_Throws()
    {
        await using var harness = await StoppedInFactorialAsync(FactorialScopes + """
            , { "on": "next", "respond": {}, "thenEvents": [ { "event": "stopped", "body": { "reason": "step", "threadId": 1 } } ] },
              { "on": "threads", "respond": { "threads": [ { "id": 1, "name": "main" } ] } },
              { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 524288, "name": "Factorial", "line": 41, "column": 1, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } } ] } },
              { "on": "stackTrace", "respond": { "stackFrames": [ { "id": 524288, "name": "Factorial", "line": 41, "column": 1, "source": { "path": "C:\\work\\DbgDemo\\DbgDemo.pas" } } ] } }
            """);
        var session = harness.Session;
        var frame = Assert.Single((await session.GetStackTraceAsync(1, ct: Ct).WaitAsync(Safety, Ct)).Frames);
        var old = await session.GetScopesAsync(frame.Id, Ct).WaitAsync(Safety, Ct);
        Assert.Single(await session.GetChildrenAsync(old[0].Handle, ct: Ct).WaitAsync(Safety, Ct));
        Assert.Empty(await session.GetChildrenAsync(old[2].Handle, ct: Ct).WaitAsync(Safety, Ct));

        // A handle that was never issued in this stop.
        var unknown = await Assert.ThrowsAsync<DebuggerException>(
            () => session.GetChildrenAsync(new VariableHandle(1, 999), ct: Ct).WaitAsync(Safety, Ct));
        Assert.Equal(DebugErrorCode.StaleReference, unknown.Code);

        await session.StepOverAsync(1, Ct).WaitAsync(Safety, Ct);
        await harness.WaitForEventAsync("stopped Step epoch=2");
        var newFrame = Assert.Single((await session.GetStackTraceAsync(1, ct: Ct).WaitAsync(Safety, Ct)).Frames);
        var fresh = await session.GetScopesAsync(newFrame.Id, Ct).WaitAsync(Safety, Ct);
        var sent = harness.SentCommands().Count;

        // Ids start at 1 again in the new stop, so the old handle differs only by its epoch: refused without a request.
        Assert.Equal(old[0].Handle.Id, fresh[0].Handle.Id);
        Assert.Equal(2, fresh[0].Handle.Epoch);
        foreach (var handle in new[] { old[0].Handle, old[1].Handle, new VariableHandle(3, 1) })
        {
            var exception = await Assert.ThrowsAsync<DebuggerException>(() => session.GetChildrenAsync(handle, ct: Ct).WaitAsync(Safety, Ct));
            Assert.Equal(DebugErrorCode.StaleReference, exception.Code);
            Assert.Equal("The value belongs to an earlier stop.", exception.Message);
        }

        Assert.Equal(sent, harness.SentCommands().Count);
        await EndAsync(harness);
    }
}
