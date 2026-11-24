using FreexComp.Debugger.Expressions;
using FreexComp.Debugger.Tests.TestSupport;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// Which variables of a routine a stop sees: inline variables by the line range of the type information, later
/// declarations of a name (<c>Name$2</c>) under the plain name, and the variables of the routine while stopped in its
/// outlined finally part. The type information is what the compiler writes for the test program <c>InlineScope</c>,
/// plus a loop variable and a variable of the outermost statement list.
/// </summary>
public sealed class EvaluatorTests
{
    private const string File = @"C:\work\InlineScope\InlineScope.pas";
    private const ulong Stack = 0x30000;

    private static readonly string[] Names = ["N", "Total", "Tmp", "Tmp$2", "Fin", "I", "Late"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PdiFile Pdi() => PdiFixture.Parse($$"""
        { "formatVersion": 1, "program": "InlineScope", "container": "codeview", "abi": {{PdiFixture.Abi}},
          "types": [ {{ValueTestBed.Types}} ],
          "units": [ { "name": "InlineScope", "file": {{Json(File)}}, "globals": [],
            "routines": [
              { "displayName": "InlineScope", "linkageName": "$main", "file": {{Json(File)}}, "line": 1, "bodyLine": 29, "endLine": 31,
                "self": null, "vars": [] },
              { "displayName": "Run", "linkageName": "InlineScope.Run", "file": {{Json(File)}}, "line": 3, "bodyLine": 6, "endLine": 27,
                "self": null, "vars": [
                  { "name": "N", "kind": "param", "type": {{ValueTestBed.Integer}} },
                  { "name": "Total", "kind": "local", "type": {{ValueTestBed.Integer}} },
                  { "name": "Tmp", "kind": "local", "type": {{ValueTestBed.Integer}}, "fromLine": 10, "toLine": 12 },
                  { "name": "Tmp$2", "kind": "local", "type": {{ValueTestBed.String}}, "fromLine": 15, "toLine": 17 },
                  { "name": "Fin", "kind": "local", "type": {{ValueTestBed.Integer}}, "fromLine": 21, "toLine": 23 },
                  { "name": "I", "kind": "local", "type": {{ValueTestBed.Integer}}, "fromLine": 24, "toLine": 25 },
                  { "name": "Late", "kind": "local", "type": {{ValueTestBed.Integer}}, "fromLine": 26 } ] },
              { "displayName": "Run.$fin1", "linkageName": "InlineScope.Run.$fin1", "parent": "InlineScope.Run", "file": {{Json(File)}},
                "line": 20, "bodyLine": 20, "endLine": 23, "self": null, "vars": [] },
              { "displayName": "Run.$fin1.$fin2", "linkageName": "InlineScope.Run.$fin1.$fin2", "parent": "InlineScope.Run.$fin1",
                "file": {{Json(File)}}, "line": 22, "bodyLine": 22, "endLine": 22, "self": null, "vars": [] },
              { "displayName": "Outer", "linkageName": "InlineScope.Outer", "file": {{Json(File)}}, "line": 40, "bodyLine": 46, "endLine": 48,
                "self": null, "vars": [ { "name": "X", "kind": "local", "type": {{ValueTestBed.Integer}} } ] },
              { "displayName": "Inner", "linkageName": "InlineScope.Outer.Inner", "parent": "InlineScope.Outer", "file": {{Json(File)}},
                "line": 42, "bodyLine": 43, "endLine": 44, "self": null, "vars": [] },
              { "displayName": "Lost.$fin1", "linkageName": "InlineScope.Lost.$fin1", "parent": "InlineScope.Lost", "file": {{Json(File)}},
                "line": 50, "bodyLine": 50, "endLine": 52, "self": null, "vars": [] } ] } ] }
        """);

    private static string Json(string text) => System.Text.Json.JsonSerializer.Serialize(text);

    /// <summary>Stopped in <c>Run</c> on <paramref name="line"/>; the adapter lists and locates every variable of the routine, as it does.</summary>
    private static EvalBed At(int line)
    {
        var debuggee = new FakeDebuggee();
        debuggee.Memory.Map(Stack, 0x40 * Names.Length);
        for (var index = 0; index < Names.Length; index++)
            debuggee.Add(Names[index], Stack + (ulong)(0x40 * index));
        ulong Of(string name) => Stack + (ulong)(0x40 * Array.IndexOf(Names, name));
        debuggee.Memory.PutInt32(Of("N"), 3).PutInt32(Of("Total"), 9).PutInt32(Of("Tmp"), 6).PutInt32(Of("Fin"), 100)
            .PutInt32(Of("I"), 1).PutInt32(Of("Late"), 5);
        debuggee.Memory.PutPointer(Of("Tmp$2"), 0x50020).PutUnicodeString(0x50020, "two");
        return new EvalBed(Pdi(), debuggee, "Run", line);
    }

    private static async Task<string[]> LocalsAsync(EvalBed bed) =>
        [.. (await bed.ScopeAsync(ScopeKind.Locals)).Select(v => $"{v.Name} = {v.Value} : {v.TypeName}")];

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DBG-142")]
    public async Task InlineVariable_VisibleOnlyWithinLineRange_NumberedNameDisplaysPlain()
    {
        const string Total = "Total = 9 : Integer", First = "Tmp = 6 : Integer", Second = "Tmp = 'two' : string";

        // Line of the stop -> the rows of Locals. The range is inclusive at both ends: the declaration line and the
        // closing line of the statement list still show the variable.
        (int Line, string[] Locals)[] rows =
        [
            (7, [Total]), (9, [Total]),
            (10, [Total, First]), (11, [Total, First]), (12, [Total, First]),
            (13, [Total]), (14, [Total]),
            (15, [Total, Second]), (16, [Total, Second]), (17, [Total, Second]),
            (18, [Total]), (20, [Total]),
            (21, [Total, "Fin = 100 : Integer"]), (23, [Total, "Fin = 100 : Integer"]),
            (24, [Total, "I = 1 : Integer"]), (25, [Total, "I = 1 : Integer"]),    // a loop variable: the lines of the loop
            (26, [Total, "Late = 5 : Integer"]), (27, [Total, "Late = 5 : Integer"]),   // outermost list: no last line
        ];
        foreach (var (line, locals) in rows)
        {
            var bed = At(line);
            Assert.Equal(locals, await LocalsAsync(bed));
            Assert.Equal(["N = 3 : Integer"], (await bed.ScopeAsync(ScopeKind.Arguments)).Select(v => $"{v.Name} = {v.Value} : {v.TypeName}"));
        }

        // An expression sees the same: the plain name is the declaration in scope, read from its own storage.
        var first = At(11);
        Assert.Equal("6 : Integer", await first.ResultAsync("Tmp"));
        Assert.Contains("addr 524288:Tmp", first.Debuggee.Requests);
        Assert.DoesNotContain("addr 524288:Tmp$2", first.Debuggee.Requests);
        Assert.Equal("12 : Integer", await first.ResultAsync("tmp + TMP"));   // case-insensitive

        var second = At(16);
        var value = await second.EvalAsync("Tmp");
        Assert.Equal(("Tmp", "'two'", "string"), (value.Name, value.Value, value.TypeName));
        Assert.Equal("Tmp", value.EvaluateName);
        Assert.Contains("addr 524288:Tmp$2", second.Debuggee.Requests);
        Assert.DoesNotContain("addr 524288:Tmp", second.Debuggee.Requests);
        Assert.Equal("3 : Integer", await second.ResultAsync("Length(Tmp)"));
        Assert.Equal("Operator '+' not applicable to string and Integer", await second.ErrorAsync("Tmp + 1"));

        // The node of the list names itself plainly, and its expression leads back to the same storage.
        var listed = (await second.ScopeAsync(ScopeKind.Locals)).Single(v => v.Name == "Tmp");
        Assert.Equal("Tmp", listed.EvaluateName);
        Assert.Equal(Stack + (0x40 * 3), listed.Address);

        // Outside every range the name is not declared, and the numbered spelling is never an identifier.
        foreach (var line in (int[])[7, 9, 13, 14, 18, 27])
        {
            var bed = At(line);
            Assert.Equal("Undeclared identifier 'Tmp'", await bed.ErrorAsync("Tmp"));
            Assert.Null(Evaluator.FindVariable(bed.Frame, "Tmp"));
        }

        Assert.Equal("Undeclared identifier 'Fin'", await At(20).ErrorAsync("Fin"));
        Assert.Equal("Undeclared identifier 'Late'", await At(25).ErrorAsync("Late"));
        foreach (var line in (int[])[11, 16])
        {
            var syntax = await Assert.ThrowsAsync<DebuggerException>(() => At(line).EvalAsync("Tmp$2"));
            Assert.Equal(DebugErrorCode.ExpressionSyntax, syntax.Code);
        }

        // The filter and the display name by themselves.
        var variables = first.Frame.Routine!.Variables.ToDictionary(v => v.Name);
        Assert.True(Evaluator.IsVisible(variables["Total"], 1));
        Assert.Equal((false, true, true, false), (Evaluator.IsVisible(variables["Tmp$2"], 14), Evaluator.IsVisible(variables["Tmp$2"], 15),
            Evaluator.IsVisible(variables["Tmp$2"], 17), Evaluator.IsVisible(variables["Tmp$2"], 18)));
        Assert.Equal((false, true, true), (Evaluator.IsVisible(variables["Late"], 25), Evaluator.IsVisible(variables["Late"], 26),
            Evaluator.IsVisible(variables["Late"], 1000)));
        Assert.Equal("Tmp", Evaluator.DisplayName("Tmp$2"));
        Assert.Equal("Tmp", Evaluator.DisplayName("Tmp$12"));
        Assert.Equal("Tmp", Evaluator.DisplayName("Tmp"));
        Assert.Equal("A$high", Evaluator.DisplayName("A$high"));
        Assert.Equal("$2", Evaluator.DisplayName("$2"));
        Assert.Equal("Tmp$", Evaluator.DisplayName("Tmp$"));
    }

    /// <summary>
    /// The code of a finally part is a subprogram of its own (<c>Routine.$fin1</c>, with <c>parent</c>) that lists no
    /// variables: a stop inside it sees the variables of the routine, those declared in the part included. The lines
    /// of the part are also lines of the routine (where it calls the part), told apart by the adapter's frame name.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DBG-134")]
    public void OutlinedFinallyPart_SeesTheVariablesOfItsRoutine()
    {
        var pdi = Pdi();
        PdiRoutine Routine(string displayName) => pdi.Units[0].Routines.Single(r => r.DisplayName == displayName);
        var run = Routine("Run");

        // By line alone (DBG-134): the part.
        Assert.Same(Routine("Run.$fin1"), pdi.FindRoutine(File, 21));
        Assert.Same(Routine("Run.$fin1.$fin2"), pdi.FindRoutine(File, 22));

        // By line and the adapter's frame name.
        Assert.Same(Routine("Run.$fin1"), pdi.FindRoutine(File, 21, "Run::Run.$fin1"));
        Assert.Same(Routine("Run.$fin1"), pdi.FindRoutine(File, 21, "Run.$fin1"));
        Assert.Same(run, pdi.FindRoutine(File, 20, "Run"));                        // the routine calling its finally part
        Assert.Same(Routine("Run.$fin1"), pdi.FindRoutine(File, 22, "Run::Run.$fin1"));   // the part calling the nested one
        Assert.Same(Routine("Run.$fin1.$fin2"), pdi.FindRoutine(File, 22, "Run::Run.$fin1::Run.$fin1.$fin2"));
        Assert.Same(run, pdi.FindRoutine(File, 22, "Run"));
        Assert.Same(Routine("Run.$fin1"), pdi.FindRoutine(File, 21, null));        // no name: the line decides
        Assert.Same(Routine("Run.$fin1"), pdi.FindRoutine(File, 21, ""));
        Assert.Same(run, pdi.FindRoutine(File, 11, "anything"));                   // not a part: the line decides
        Assert.Same(Routine("Inner"), pdi.FindRoutine(File, 43, "Outer"));         // a nested routine is not a part
        Assert.Null(pdi.FindRoutine(File, 100, "Run"));

        // Whose variables a stop sees.
        Assert.Same(run, pdi.VariableOwner(Routine("Run.$fin1")));
        Assert.Same(run, pdi.VariableOwner(Routine("Run.$fin1.$fin2")));
        Assert.Same(run, pdi.VariableOwner(run));
        Assert.Same(Routine("Inner"), pdi.VariableOwner(Routine("Inner")));
        Assert.Same(Routine("Lost.$fin1"), pdi.VariableOwner(Routine("Lost.$fin1")));   // its routine is not described

        // With the routine as the frame's routine and the line of the stop, the variable of the part is in scope.
        var frame = new FrameContext(7, pdi.VariableOwner(pdi.FindRoutine(File, 22, "Run::Run.$fin1")!), 22);
        Assert.Equal("Fin", Evaluator.FindVariable(frame, "Fin")?.Name);
        Assert.Equal("Total", Evaluator.FindVariable(frame, "Total")?.Name);
        Assert.Null(Evaluator.FindVariable(frame, "Tmp"));
    }
}
