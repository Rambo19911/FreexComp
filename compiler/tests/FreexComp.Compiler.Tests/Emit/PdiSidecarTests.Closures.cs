using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Tests.Emit;

// C-414 (codegen.md CMP-1470…1472; debug-info-sidecar.md §5): the debug description of the variables anonymous methods capture —
// in the body that owns the routine closure through the hidden local $closure, in an anonymous method through its frame
// parameter $frame and one hidden slot $frame<k> per outer frame of its frame chain.
public sealed partial class PdiSidecarTests
{
    /// <summary>A <c>DILocalVariable</c> of one function: its node text and the <c>#dbg_declare</c> records naming it.</summary>
    private sealed record DebugVariable(string Name, string Node, string[] Declares);

    /// <summary>The function whose linkage name ends with <paramref name="nameEnd"/>: its <c>define</c> line to its closing brace.</summary>
    private static string Function(string ir, string nameEnd)
    {
        var lines = ir.ReplaceLineEndings("\n").Split('\n');
        var header = new Regex("^define [^\n]*@\"?[^\"(\\s]*" + Regex.Escape(nameEnd) + "\"?\\(");
        var start = Assert.Single(Enumerable.Range(0, lines.Length), i => header.IsMatch(lines[i]));
        return string.Join('\n', lines[start..(Array.IndexOf(lines, "}", start) + 1)]);
    }

    /// <summary>The <c>DILocalVariable</c> nodes of <paramref name="function"/>'s subprogram, in node order.</summary>
    private static DebugVariable[] DebugVariables(string ir, string function)
    {
        var subprogram = Regex.Match(function, @"^define [^\n]* !dbg !(\d+) \{").Groups[1].Value;
        Assert.NotEqual("", subprogram);
        return [.. Regex.Matches(ir.ReplaceLineEndings("\n"), @"^!(\d+) = (!DILocalVariable\(name: ""([^""]*)""[^\n]*, scope: !" + subprogram + @",[^\n]*\))$", RegexOptions.Multiline)
            .Select(m => new DebugVariable(m.Groups[3].Value, m.Groups[2].Value,
                [.. function.Split('\n').Where(l => l.Contains($", !{m.Groups[1].Value}, !DIExpression(", StringComparison.Ordinal)).Select(l => l.Trim())]))];
    }

    /// <summary>The variable <paramref name="name"/> has one node and one record: on <paramref name="address"/> with <paramref name="expression"/>.</summary>
    private static DebugVariable AssertDeclared(DebugVariable[] variables, string name, string address, string expression)
    {
        var variable = Assert.Single(variables, v => v.Name == name);
        var declare = Assert.Single(variable.Declares);
        Assert.StartsWith($"#dbg_declare(ptr {address}, !", declare, StringComparison.Ordinal);
        Assert.Contains($", !DIExpression({expression}), !", declare, StringComparison.Ordinal);
        return variable;
    }

    /// <summary>The location form of a variable in a frame instance: the slot holds the instance, the field lies at <paramref name="field"/>.</summary>
    private static string InFrame(FieldSymbol field) =>
        "DW_OP_deref, DW_OP_plus_uconst, " + field.Offset.ToString(CultureInfo.InvariantCulture);

    private static ClosureInfo ClosureOf(BindingResult result, string routine) =>
        result.Routines.Single(r => r.Routine.Name == routine && r.Routine is not MethodSymbol { IsAnonymous: true }).Routine.Closure!;

    private static FieldSymbol Field(ClosureInfo frame, string name) => frame.Fields.Single(f => f.Key.Name == name).Value;

    /// <summary>The name an anonymous method ends with, as display name and as linkage name: its frame class and its own name.</summary>
    private static string NameOf(MethodSymbol anonymous) => anonymous.AnonymousFrame!.Class.Name + "." + anonymous.Name;

    private static JsonObject RoutineEnding(JsonObject unit, string displayNameEnd) =>
        unit["routines"]!.AsArray().Single(r => r!["displayName"]!.GetValue<string>().EndsWith(displayNameEnd, StringComparison.Ordinal))!.AsObject();

    private const string CapturedLocalSource = """
        program P;
        type TF = reference to function: Integer;

        function Run(Seed: Integer): TF;
        var
          Before: Integer;
          Count: Integer; // count
          After: Integer;
        begin
          Before := Seed;
          Count := 0;
          After := Before;
          Result := function: Integer begin Count := Count + 1; Result := Count end;
          Count := Count + After;
        end;

        var G: TF;
        begin
          G := Run(1);
        end.
        """;

    /// <summary>
    /// CMP-1470, CMP-1472 (1): a captured classic local keeps its one <c>DILocalVariable</c> (name, line, type) and its place and kind
    /// in <c>vars[]</c>; its record stands on the hidden local <c>$closure</c> — the instance, plus the offset of its field — and
    /// nothing describes the stack slot the body no longer uses.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1470")]
    [Trait("Req", "CMP-1472")]
    public void CapturedLocalOfRoutineFrame()
    {
        var (ir, unit, result) = Emit(CapturedLocalSource, ClosureSystemSource);
        Assert.Equal(["Seed param -..-", "Result result -..-", "Before local -..-", "Count local -..-", "After local -..-"], Vars(Routine(unit, "Run")));

        var run = Function(ir, "P.Run");
        var variables = DebugVariables(ir, run);
        var count = AssertDeclared(variables, "Count", "%$closure.addr", InFrame(Field(ClosureOf(result, "Run"), "Count")));
        Assert.Contains($"line: {LineOf(CapturedLocalSource, "// count")}, ", count.Node, StringComparison.Ordinal);
        Assert.DoesNotContain("arg:", count.Node, StringComparison.Ordinal);
        Assert.DoesNotContain("flags:", count.Node, StringComparison.Ordinal);
        Assert.DoesNotContain("#dbg_declare(ptr %Count.addr,", run, StringComparison.Ordinal);
        AssertDeclared(variables, "Before", "%Before.addr", "");
        AssertDeclared(variables, "After", "%After.addr", "");
    }

    private const string CapturedParameterSource = """
        program P;
        type
          TRec = record A, B, C: Int64; end;
          TF = reference to function: Integer;

        function Run(Plain, N: Integer; const R: TRec; const K: TRec): TF;
        begin
          Result := function: Integer begin Result := N + Integer(R.A) end;
          N := N + Plain + Integer(K.A);
        end;

        var G: TF; V: TRec;
        begin
          G := Run(1, 2, V, V);
        end.
        """;

    /// <summary>
    /// CMP-1470, CMP-1472 (1): a captured value parameter and a captured <c>const</c> record parameter the ABI passes by address keep
    /// their <c>arg:</c>; the one record of each stands on <c>$closure</c> (the frame holds the value) — none on the spill slot,
    /// none on a slot holding the received address — and the sidecar kind is <c>param</c>, never <c>varParam</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1470")]
    [Trait("Req", "CMP-1472")]
    public void CapturedParameterIsDescribedOnTheFrame()
    {
        var (ir, unit, result) = Emit(CapturedParameterSource, ClosureSystemSource);
        Assert.Equal(["Plain param -..-", "N param -..-", "R param -..-", "K varParam -..-", "Result result -..-"], Vars(Routine(unit, "Run")));

        var closure = ClosureOf(result, "Run");
        var run = Function(ir, "P.Run");
        var variables = DebugVariables(ir, run);
        Assert.Contains("name: \"N\", arg: 3, ", AssertDeclared(variables, "N", "%$closure.addr", InFrame(Field(closure, "N"))).Node, StringComparison.Ordinal);
        Assert.Contains("name: \"R\", arg: 4, ", AssertDeclared(variables, "R", "%$closure.addr", InFrame(Field(closure, "R"))).Node, StringComparison.Ordinal);
        Assert.DoesNotContain("#dbg_declare(ptr %N.addr,", run, StringComparison.Ordinal);
        Assert.DoesNotContain("%R.ref", run, StringComparison.Ordinal);

        // the parameters no anonymous method names stay where they were
        Assert.Contains("name: \"Plain\", arg: 2, ", AssertDeclared(variables, "Plain", "%Plain.addr", "").Node, StringComparison.Ordinal);
        Assert.Contains("name: \"K\", arg: 5, ", AssertDeclared(variables, "K", "%K.ref", "DW_OP_deref").Node, StringComparison.Ordinal);
    }

    private const string CapturedResultSource = """
        program P;
        type
          TP = reference to procedure;
          TBig = record A, B, C: Int64; end;

        function Small: Integer;
        var F: TP;
        begin
          Result := 1;
          F := procedure begin Result := Result + 1 end;
          F();
        end;

        function Big: TBig;
        var F: TP;
        begin
          Result.A := 1;
          F := procedure begin Result.B := 2 end;
          F();
        end;

        begin
          Small;
          Big;
        end.
        """;

    /// <summary>
    /// CMP-1470: a captured <c>Result</c> — returned in a register or in the caller's storage (ABI-081) — is described on the frame
    /// (the body works on the field until it returns), with the sidecar kind <c>result</c> at its place.
    /// </summary>
    [Theory]
    [InlineData("Small")]
    [InlineData("Big")]
    [Trait("Req", "CMP-1470")]
    public void CapturedResultIsDescribedOnTheFrame(string routine)
    {
        var (ir, unit, result) = Emit(CapturedResultSource, ClosureSystemSource);
        Assert.Equal(["Result result -..-", "F local -..-"], Vars(Routine(unit, routine)));

        var body = Function(ir, "P." + routine);
        var variables = DebugVariables(ir, body);
        var node = AssertDeclared(variables, "Result", "%$closure.addr", InFrame(Field(ClosureOf(result, routine), "Result"))).Node;
        Assert.DoesNotContain("arg:", node, StringComparison.Ordinal);
        Assert.DoesNotContain("%Result.ref", body, StringComparison.Ordinal);
        Assert.DoesNotContain("#dbg_declare(ptr %Result.addr,", body, StringComparison.Ordinal);
        AssertDeclared(variables, "F", "%F.addr", "");
    }

    private const string FrameRecordSource = """
        program P;
        type TF = reference to function: Integer;

        function Run(N: Integer): TF;
        var Count: Integer;
          procedure Bump;
          begin
            Count := Count + N;
          end;
        begin
          Count := 0;
          Bump;
          Result := function: Integer begin Result := Count end;
        end;

        var G: TF;
        begin
          G := Run(1);
        end.
        """;

    /// <summary>
    /// CMP-1470: when a nested routine uses the closure, <c>$closure</c> is a field of the body's frame record (CMP-345): the
    /// record is the address — the field holding the instance, the instance, the variable's field.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1470")]
    public void ClosureLocalInAFrameRecord()
    {
        var (ir, unit, result) = Emit(FrameRecordSource, ClosureSystemSource);
        Assert.Equal(["N param -..-", "Result result -..-", "Count local -..-"], Vars(Routine(unit, "Run")));

        var run = Function(ir, "P.Run");
        var variables = DebugVariables(ir, run);
        var holder = Assert.Single(Assert.Single(variables, v => v.Name == "$closure").Declares);
        var at = Regex.Match(holder, @"^#dbg_declare\(ptr %\$frame, !\d+, !DIExpression\(DW_OP_plus_uconst, (\d+)\), ");
        Assert.True(at.Success, holder);
        AssertDeclared(variables, "Count", "%$frame", $"DW_OP_plus_uconst, {at.Groups[1].Value}, {InFrame(Field(ClosureOf(result, "Run"), "Count"))}");
        Assert.DoesNotContain("#dbg_declare(ptr %Count.addr,", run, StringComparison.Ordinal);

        // N is used by the nested routine only: a field of the record itself (CMP-422), as before
        Assert.Matches(@"^#dbg_declare\(ptr %\$frame, !\d+, !DIExpression\(DW_OP_plus_uconst, \d+\), ", Assert.Single(Assert.Single(variables, v => v.Name == "N").Declares));
    }

    private const string AnonymousSource = """
        program P;
        type TF = reference to function(Delta: Integer): Integer;

        function Run(Start: Integer): TF; // run
        var
          Count: Integer; // count
          Unused: Integer;
        begin
          Count := 0;
          Unused := 0;
          Result := function(Delta: Integer): Integer
            var Twice: Integer;
            begin
              Twice := Delta * 2;
              Count := Count + Twice;
              Result := Count + Start;
            end;
        end;

        var G: TF;
        begin
          G := Run(1);
        end.
        """;

    /// <summary>
    /// CMP-1471 (1, 3), CMP-1472 (2, 3): in an anonymous method the frame parameter is the artificial <c>$frame</c> (argument 1, not
    /// in the sidecar); after the method's own variables come the captured variables of the owner — every source variable of the
    /// frame, in field order, kind <c>local</c>, no <c>arg</c>, no line range — each declared on the spilled frame parameter with
    /// the offset of its field. A routine has no <c>Self</c>: the entry's <c>self</c> is null.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1471")]
    [Trait("Req", "CMP-1472")]
    public void AnonymousMethodListsCapturedVariables()
    {
        var (ir, unit, result) = Emit(AnonymousSource, ClosureSystemSource);
        var closure = ClosureOf(result, "Run");
        Assert.Equal(["Count", "Start"], closure.Fields.Keys.Select(s => s.Name).Order(StringComparer.Ordinal));
        var name = NameOf(closure.Methods.Single().Method);
        var routine = RoutineEnding(unit, name);
        Assert.Equal(["Delta param -..-", "Result result -..-", "Twice local -..-", .. closure.Fields.Keys.Select(s => s.Name + " local -..-")], Vars(routine));
        Assert.Null(routine["self"]);

        var anonymous = Function(ir, name);
        var variables = DebugVariables(ir, anonymous);
        var frame = AssertDeclared(variables, "$frame", "%Self.addr", "").Node;
        Assert.Contains("name: \"$frame\", arg: 1, ", frame, StringComparison.Ordinal);
        Assert.EndsWith("flags: DIFlagArtificial | DIFlagObjectPointer)", frame, StringComparison.Ordinal);
        Assert.DoesNotContain(variables, v => v.Name == "Self");
        Assert.Contains("name: \"Delta\", arg: 2, ", AssertDeclared(variables, "Delta", "%Delta.addr", "").Node, StringComparison.Ordinal);
        AssertDeclared(variables, "Twice", "%Twice.addr", "");

        var count = AssertDeclared(variables, "Count", "%Self.addr", InFrame(Field(closure, "Count"))).Node;
        Assert.Contains($"line: {LineOf(AnonymousSource, "// count")}, ", count, StringComparison.Ordinal);
        var start = AssertDeclared(variables, "Start", "%Self.addr", InFrame(Field(closure, "Start"))).Node;
        Assert.Contains($"line: {LineOf(AnonymousSource, "// run")}, ", start, StringComparison.Ordinal);
        Assert.All((string[])[count, start], node =>
        {
            Assert.DoesNotContain("arg:", node, StringComparison.Ordinal);
            Assert.DoesNotContain("flags:", node, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(variables, v => v.Name == "Unused");
        Assert.DoesNotContain("$frame2", anonymous, StringComparison.Ordinal);

        // the owner lists each of them once, at its own place (CMP-1472 (1))
        Assert.Equal(["Start param -..-", "Result result -..-", "Count local -..-", "Unused local -..-"], Vars(Routine(unit, "Run")));
    }

    private const string MethodSource = """
        program P;
        type
          TF = reference to function: Integer;
          TCounter = class
            FBase: Integer;
            function Make(N: Integer): TF;
            function MakePlain(N: Integer): TF;
          end;

        function TCounter.Make(N: Integer): TF;
        begin
          Result := function: Integer begin Result := FBase + N end;
        end;

        function TCounter.MakePlain(N: Integer): TF;
        begin
          Result := function: Integer begin Result := N end;
        end;

        begin
        end.
        """;

    /// <summary>
    /// CMP-1471 (3), CMP-1472 (2, 3): the <c>Self</c> of the enclosing method that an anonymous method captured is the variable
    /// <c>Self</c> of the anonymous method — kind <c>self</c>, artificial, not the object pointer of the function, read through the
    /// frame — and the routine entry's <c>self</c> is its class; an anonymous method that captured no <c>Self</c> has <c>self</c>
    /// null. In the method itself <c>Self</c> stays on its parameter slot (CMP-1470).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1471")]
    [Trait("Req", "CMP-1472")]
    public void AnonymousMethodInAMethod_SelfIsTheEnclosingObject()
    {
        var (ir, unit, result) = Emit(MethodSource, ClosureSystemSource);
        var make = Routine(unit, "TCounter.Make");
        var counter = make["self"]!.GetValue<int>();
        var closure = ClosureOf(result, "Make");
        var name = NameOf(closure.Methods.Single().Method);
        var routine = RoutineEnding(unit, name);
        Assert.Equal(counter, routine["self"]!.GetValue<int>());
        Assert.Equal(["Result result -..-", .. closure.Fields.Keys.Select(s => s.Name + (s.Name == "Self" ? " self -..-" : " local -..-"))], Vars(routine));
        Assert.Contains("Self self -..-", Vars(routine));
        Assert.Equal(counter, routine["vars"]!.AsArray().Single(v => v!["name"]!.GetValue<string>() == "Self")!["type"]!.GetValue<int>());

        var anonymous = Function(ir, name);
        var variables = DebugVariables(ir, anonymous);
        var self = AssertDeclared(variables, "Self", "%Self.addr", InFrame(Field(closure, "Self"))).Node;
        Assert.EndsWith("flags: DIFlagArtificial)", self, StringComparison.Ordinal);
        Assert.DoesNotContain("arg:", self, StringComparison.Ordinal);
        AssertDeclared(variables, "$frame", "%Self.addr", "");
        AssertDeclared(variables, "N", "%Self.addr", InFrame(Field(closure, "N")));

        // no captured Self: no variable, self null
        var plainName = NameOf(ClosureOf(result, "MakePlain").Methods.Single().Method);
        var plain = RoutineEnding(unit, plainName);
        Assert.Null(plain["self"]);
        Assert.Equal(["Result result -..-", "N local -..-"], Vars(plain));
        Assert.DoesNotContain(DebugVariables(ir, Function(ir, plainName)), v => v.Name == "Self");

        // the method: Self on its parameter slot, the object pointer; the captured parameter on the frame
        Assert.Equal(["Self self -..-", "N param -..-", "Result result -..-"], Vars(make));
        var method = DebugVariables(ir, Function(ir, "P.TCounter.Make"));
        Assert.EndsWith("flags: DIFlagArtificial | DIFlagObjectPointer)", AssertDeclared(method, "Self", "%Self.addr", "").Node, StringComparison.Ordinal);
        AssertDeclared(method, "N", "%$closure.addr", InFrame(Field(closure, "N")));
    }

    private const string NestedSource = """
        program P;
        type
          TF = reference to function: Integer;
          TP = reference to procedure;

        function Run(Start: Integer): TF;
        var Count: Integer;
        begin
          Count := 0;
          Result := function: Integer
            var Mid: TF;
            begin
              Mid := function: Integer
                var
                  Twice: Integer;
                  Log: TP;
                begin
                  Twice := 2;
                  Log := procedure begin Count := Count + Start + Twice end;
                  Log();
                  Result := Count;
                end;
              Result := Mid();
            end;
        end;

        var G: TF;
        begin
          G := Run(1);
        end.
        """;

    /// <summary>
    /// CMP-1471 (2, 3, 5): an anonymous method nested in others reaches the outer frames through one hidden slot per frame — filled
    /// once in the entry block at the <c>scopeLine</c> location — and describes their variables on it; a frame of the chain that
    /// holds no source variable gets no slot; the variables its own closure holds follow CMP-1470.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1471")]
    public void NestedAnonymousMethod_ReachesOuterFrames()
    {
        var (ir, unit, result) = Emit(NestedSource, ClosureSystemSource);
        var run = ClosureOf(result, "Run");
        var outer = run.Methods.Single().Method;                    // function: Integer … Mid
        var middle = outer.Closure!.Methods.Single().Method;        // function: Integer … Twice, Log
        var inner = middle.Closure!.Methods.Single().Method;        // procedure … Count := Count + Start + Twice
        Assert.Empty(outer.Closure.Fields);
        string[] ofRun = [.. run.Fields.Keys.Select(s => s.Name + " local -..-")];
        Assert.Equal(["Count local -..-", "Start local -..-"], ofRun.Order(StringComparer.Ordinal));

        // the middle one runs on the closure of the outer one, which holds only its link: slot of frame 2 = the closure of Run
        var body = Function(ir, NameOf(middle));
        var variables = DebugVariables(ir, body);
        Assert.Equal(["Result result -..-", "Twice local -..-", "Log local -..-", .. ofRun], Vars(RoutineEnding(unit, NameOf(middle))));
        var location = Regex.Match(AssertDeclared(variables, "$frame", "%Self.addr", "").Declares[0], @", (!\d+)\)$").Groups[1].Value;
        AssertDeclared(variables, "Count", "%$frame2", InFrame(Field(run, "Count")));
        AssertDeclared(variables, "Start", "%$frame2", InFrame(Field(run, "Start")));
        AssertDeclared(variables, "Twice", "%$closure.addr", InFrame(Field(middle.Closure, "Twice")));       // CMP-1470 in its own body
        AssertDeclared(variables, "Log", "%Log.addr", "");
        var entry = body[..body.IndexOf("#dbg_declare(", StringComparison.Ordinal)];
        Assert.Contains($"  %$frame2 = alloca ptr, align 8, !dbg {location}\n", entry, StringComparison.Ordinal);
        var store = Regex.Match(entry, @"^  store ptr (%[\w.$]+), ptr %\$frame2, align 8, !dbg (!\d+)$", RegexOptions.Multiline);
        Assert.True(store.Success, entry);
        Assert.Equal(location, store.Groups[2].Value);
        // the stored instance is the $outer of the frame parameter
        var load = Regex.Match(entry, "^  " + Regex.Escape(store.Groups[1].Value) + @" = load ptr, ptr (%[\w.$]+), align 8, !dbg (!\d+)$", RegexOptions.Multiline);
        Assert.True(load.Success, entry);
        Assert.Equal(location, load.Groups[2].Value);
        var outerOffset = outer.Closure.Outer!.Offset.ToString(CultureInfo.InvariantCulture);
        Assert.Matches("(?m)^  " + Regex.Escape(load.Groups[1].Value) + @" = getelementptr inbounds i8, ptr %Self, i64 " + outerOffset + ", !dbg " + Regex.Escape(location) + "$", entry);
        Assert.DoesNotContain("$frame3", body, StringComparison.Ordinal);

        // the inner one: frame 1 holds Twice, frame 2 (the closure of the outer one) nothing, frame 3 is the closure of Run
        body = Function(ir, NameOf(inner));
        variables = DebugVariables(ir, body);
        Assert.Equal(["Twice local -..-", .. ofRun], Vars(RoutineEnding(unit, NameOf(inner))));
        AssertDeclared(variables, "Twice", "%Self.addr", InFrame(Field(middle.Closure, "Twice")));
        AssertDeclared(variables, "Count", "%$frame3", InFrame(Field(run, "Count")));
        AssertDeclared(variables, "Start", "%$frame3", InFrame(Field(run, "Start")));
        Assert.DoesNotContain("$frame2", body, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(body, @"^  store ptr %[\w.$]+, ptr %\$frame3, align 8, !dbg !\d+$", RegexOptions.Multiline));

        // the outer one runs on the closure of Run: no further frame, no slot
        body = Function(ir, NameOf(outer));
        variables = DebugVariables(ir, body);
        Assert.Equal(["Result result -..-", "Mid local -..-", .. ofRun], Vars(RoutineEnding(unit, NameOf(outer))));
        AssertDeclared(variables, "Count", "%Self.addr", InFrame(Field(run, "Count")));
        Assert.DoesNotMatch(@"\$frame\d", body);
    }

    private const string InlineSource = """
        program P;
        type TF = reference to function: Integer;

        procedure Run(P: Integer);
        var Fs: array[0..2] of TF;
        begin
          for var I := 0 to 2 do // loop
          begin
            var N := I * 10; // inline
            Fs[I] := function: Integer begin Result := N + I + P end;
            N := N + 1;
          end; // loop end
        end;

        begin
          Run(1);
        end.
        """;

    /// <summary>
    /// CMP-1471 (3): captured inline and <c>for var</c> variables of the owner live in block frames (CMP-1183, CMP-1410); an
    /// anonymous method written in their scope lists them through those frames — its own frame first, then the frames around it —
    /// without a line range; the owner keeps their CMP-1184 / CMP-1412 description.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1471")]
    public void AnonymousMethod_SeesCapturedInlineVariables()
    {
        var (ir, unit, result) = Emit(InlineSource, ClosureSystemSource);
        var run = ClosureOf(result, "Run");
        var ofN = Assert.Single(run.Blocks, b => b.Fields.Keys.Any(s => s.Name == "N"));
        var ofI = Assert.Single(run.Blocks, b => b.Fields.Keys.Any(s => s.Name == "I"));
        Assert.NotSame(ofN, ofI);
        var method = ofN.Methods.Single().Method;
        var name = NameOf(method);
        Assert.Equal(["Result result -..-", "N local -..-", "I local -..-", "P local -..-"], Vars(RoutineEnding(unit, name)));

        var body = Function(ir, name);
        var variables = DebugVariables(ir, body);
        Assert.Contains($"line: {LineOf(InlineSource, "// inline")}, ", AssertDeclared(variables, "N", "%Self.addr", InFrame(Field(ofN, "N"))).Node, StringComparison.Ordinal);
        Assert.Contains($"line: {LineOf(InlineSource, "// loop")}, ", AssertDeclared(variables, "I", "%$frame2", InFrame(Field(ofI, "I"))).Node, StringComparison.Ordinal);
        AssertDeclared(variables, "P", "%$frame3", InFrame(Field(run, "P")));

        // the owner: the inline variables through the hidden locals of their frames, with their ranges; the parameter on $closure
        int L(string marker) => LineOf(InlineSource, "// " + marker);
        Assert.Equal(["P param -..-", "Fs local -..-", $"I local {L("loop")}..{L("loop end")}", $"N local {L("inline")}..{L("loop end")}"], Vars(Routine(unit, "Run")));
        var owner = DebugVariables(ir, Function(ir, "P.Run"));
        AssertDeclared(owner, "N", $"%{ofN.Local.Name}.addr", InFrame(Field(ofN, "N")));
        AssertDeclared(owner, "I", $"%{ofI.Local.Name}.addr", InFrame(Field(ofI, "I")));
        AssertDeclared(owner, "P", "%$closure.addr", InFrame(Field(run, "P")));

        // a unit-level body (C-320): the same through the block frames of the main block; the DBG-142 number of a later
        // declaration belongs to the main block, the anonymous method sees one variable of that name
        (ir, unit, result) = Emit(ProgramBlockCaptureSource, ClosureSystemSource);
        var blocks = Assert.Single(result.Bodies).Closure!.Blocks;
        ofN = Assert.Single(blocks, b => b.Fields.Keys.Any(s => s.Name == "N"));
        var ofM = Assert.Single(blocks, b => b.Fields.Keys.Any(s => s.Name == "M"));
        var shadow = Assert.Single(blocks, b => b.Fields.Keys.Any(s => s.Name == "m"));
        name = NameOf(ofM.Methods.Single().Method);
        Assert.Equal(["Result result -..-", "M local -..-", "N local -..-"], Vars(RoutineEnding(unit, name)));
        variables = DebugVariables(ir, Function(ir, name));
        AssertDeclared(variables, "M", "%Self.addr", InFrame(Field(ofM, "M")));
        AssertDeclared(variables, "N", "%$frame2", InFrame(Field(ofN, "N")));
        name = NameOf(shadow.Methods.Single().Method);
        Assert.Equal(["Result result -..-", "m local -..-", "N local -..-"], Vars(RoutineEnding(unit, name)));
        variables = DebugVariables(ir, Function(ir, name));
        AssertDeclared(variables, "m", "%Self.addr", InFrame(Field(shadow, "m")));
        AssertDeclared(variables, "N", "%$frame2", InFrame(Field(ofN, "N")));
        Assert.DoesNotContain(variables, v => v.Name is "M" or "m$2");
    }

    private const string LinkSource = """
        program P;
        type TF = reference to function: Integer;

        function Run(Start: Integer): Integer;
        var
          Count: Integer;
          Keep: TF;

          procedure Nested(Step: Integer);
          begin
            Keep := function: Integer begin Count := Count + Step + Start; Result := Count end;
          end;

        begin
          Count := 0;
          Nested(2);
          Result := Keep();
        end;

        begin
          Run(1);
        end.
        """;

    /// <summary>
    /// CMP-1471 (2, 3): an anonymous method written in a nested routine runs on that routine's closure, which keeps a link
    /// <c>$outer.&lt;name&gt;</c> to the closure of the enclosing routine (C-230): the frame behind the link is frame 2 of the
    /// chain. The enclosing routine reaches its own closure through its frame record (CMP-1470).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1470")]
    [Trait("Req", "CMP-1471")]
    public void AnonymousMethodInANestedRoutine_ReachesTheOwnerThroughItsLink()
    {
        var (ir, unit, result) = Emit(LinkSource, ClosureSystemSource);
        var run = ClosureOf(result, "Run");
        var nested = ClosureOf(result, "Nested");
        var link = nested.Links[run].Plain;
        Assert.Equal("$outer.Run", link.Name);
        var name = NameOf(nested.Methods.Single().Method);
        Assert.Equal(["Result result -..-", "Step local -..-", .. run.Fields.Keys.Select(s => s.Name + " local -..-")], Vars(RoutineEnding(unit, name)));
        Assert.Equal(["Count", "Start"], run.Fields.Keys.Select(s => s.Name).Order(StringComparer.Ordinal));

        var body = Function(ir, name);
        var variables = DebugVariables(ir, body);
        AssertDeclared(variables, "Step", "%Self.addr", InFrame(Field(nested, "Step")));
        AssertDeclared(variables, "Count", "%$frame2", InFrame(Field(run, "Count")));
        AssertDeclared(variables, "Start", "%$frame2", InFrame(Field(run, "Start")));
        Assert.Contains($" = getelementptr inbounds i8, ptr %Self, i64 {link.Offset.ToString(CultureInfo.InvariantCulture)}, !dbg ", body, StringComparison.Ordinal);

        // the nested routine owns the closure the method runs on; the enclosing routine's closure is a field of its frame record
        AssertDeclared(DebugVariables(ir, Function(ir, "P.Run.Nested")), "Step", "%$closure.addr", InFrame(Field(nested, "Step")));
        var owner = DebugVariables(ir, Function(ir, "P.Run"));
        var at = Regex.Match(Assert.Single(Assert.Single(owner, v => v.Name == "$closure").Declares), @"DIExpression\(DW_OP_plus_uconst, (\d+)\), ").Groups[1].Value;
        AssertDeclared(owner, "Count", "%$frame", $"DW_OP_plus_uconst, {at}, {InFrame(Field(run, "Count"))}");
        AssertDeclared(owner, "Start", "%$frame", $"DW_OP_plus_uconst, {at}, {InFrame(Field(run, "Start"))}");
    }

    private const string HidingSource = """
        program P;
        type
          TF = reference to function: Integer;
          TG = reference to function(N: Integer): Integer;

        function Own(N, X: Integer): TG;
        var Other: TF;
        begin
          Other := function: Integer begin Result := N end;
          Result := function(N: Integer): Integer // own
            begin
              Result := N + X;
            end;
        end;

        function Near(X: Integer): TF;
        var Other: TF;
        begin
          Other := function: Integer begin Result := X end;
          Result := function: Integer
            var
              x: Integer; // near
              Reader: TF;
            begin
              x := 5;
              Reader := function: Integer begin Result := x end;
              Result := Reader();
            end;
        end;

        begin
        end.
        """;

    /// <summary>
    /// CMP-1471 (4): of several variables with one name (compared without case) an anonymous method describes the one its source
    /// text means — its own before a captured one, the nearer frame before the farther.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1471")]
    public void AnonymousMethod_HiddenNamesAreListedOnce()
    {
        var (ir, unit, result) = Emit(HidingSource, ClosureSystemSource);

        // its own parameter N hides the captured parameter N of Own
        var own = ClosureOf(result, "Own");
        Assert.Equal(["N", "X"], own.Fields.Keys.Select(s => s.Name).Order(StringComparer.Ordinal));
        var second = own.Methods[1].Method;
        var name = NameOf(second);
        Assert.Equal(["N param -..-", "Result result -..-", "X local -..-"], Vars(RoutineEnding(unit, name)));
        var variables = DebugVariables(ir, Function(ir, name));
        Assert.Contains("name: \"N\", arg: 2, ", AssertDeclared(variables, "N", "%N.addr", "").Node, StringComparison.Ordinal);
        AssertDeclared(variables, "X", "%Self.addr", InFrame(Field(own, "X")));

        // the reader: frame 1 (the closure of the anonymous method around it) holds x, frame 2 (the closure of Near) holds X
        var near = ClosureOf(result, "Near");
        var around = near.Methods[1].Method;
        var reader = around.Closure!.Methods.Single().Method;
        name = NameOf(reader);
        Assert.Equal(["Result result -..-", "x local -..-"], Vars(RoutineEnding(unit, name)));
        variables = DebugVariables(ir, Function(ir, name));
        var x = AssertDeclared(variables, "x", "%Self.addr", InFrame(Field(around.Closure, "x"))).Node;
        Assert.Contains($"line: {LineOf(HidingSource, "// near")}, ", x, StringComparison.Ordinal);
        Assert.DoesNotContain(variables, v => v.Name == "X");
    }

    /// <summary>The owner bodies here have captured variables in block frames only (CMP-1184, CMP-1412): nothing of them is a field of a routine closure.</summary>
    private const string BlockOwnerSource = """
        program P;
        type TF = reference to function: Integer;

        procedure Run(const Values: array of Integer);
        var Fs: array[0..2] of TF;
        begin
          for var I := 0 to 2 do
          begin
            var N := I * 10;
            Fs[I] := function: Integer begin Result := N + I end;
          end;
          for var X in Values do
            Fs[0] := function: Integer begin Result := X end;
        end;

        begin
          Run([1, 2]);
        end.
        """;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, NewLine = "\n" };

    /// <summary>The module's IR followed by its sidecar fragment as IR comment lines: one text to compare.</summary>
    private static string WithSidecar(string ir, JsonNode sidecar) =>
        ir.ReplaceLineEndings("\n") + string.Concat(sidecar.ToJsonString(Indented).Split('\n').Select(l => "; " + l + "\n"));

    /// <summary>One body: its function, the variables of its subprogram and its sidecar entry (as comment lines).</summary>
    private static string BodyText(string ir, JsonObject unit, string linkage, string displayName)
    {
        var function = Function(ir, linkage);
        return function + "\n" + string.Concat(DebugVariables(ir, function).Select(v => v.Node + "\n")) + WithSidecar("", Routine(unit, displayName));
    }

    private static string EmitWithoutDebugInfo(string source)
    {
        var writer = new LlvmTextWriter();
        var types = new BuiltInTypes();
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", ClosureSystemSource)), types);
        var result = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(Path, source)), types, system);
        Assert.Null(IrEmitter.EmitUnit(Lowerer.Lower(result), writer));
        return writer.GetText().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// CMP-1472 (4), guard — the snapshots were written by the compiler as it was before C-414: a module without anonymous methods
    /// keeps its IR and its sidecar byte for byte; so does an owner body whose captured variables all live in block frames
    /// (CMP-1184, CMP-1412); in the owner of <see cref="CapturedInlineVariableThroughBlockFrame"/> the one record of the captured
    /// parameter <c>P</c> moved to the frame (CMP-1470) and every other line is the same; and without <c>-g</c> a module with
    /// nested anonymous methods is unchanged: no <c>$frame&lt;k&gt;</c> slot, no load for one.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1472")]
    public void BodiesWithoutClosures_Unchanged()
    {
        var (ir, unit, _) = Emit(RangesSource);
        Snapshot.Verify("ClosureDebug_NoClosures", WithSidecar(ir, unit.Root));

        (ir, unit, var result) = Emit(BlockOwnerSource, ClosureSystemSource);
        Assert.Empty(ClosureOf(result, "Run").Fields);
        Snapshot.Verify("ClosureDebug_BlockFrameOwner", BodyText(ir, unit, "P.Run", "Run"));

        (ir, unit, result) = Emit(CaptureSource, ClosureSystemSource);
        var p = InFrame(Field(ClosureOf(result, "Run"), "P"));
        var owner = BodyText(ir, unit, "P.Run", "Run");
        var before = Regex.Replace(owner, @"#dbg_declare\(ptr %\$closure\.addr, (!\d+), !DIExpression\(" + p + @"\), ", "#dbg_declare(ptr %P.addr, $1, !DIExpression(), ");
        Snapshot.Verify("ClosureDebug_CapturedInlineOwner", before);

        var plain = EmitWithoutDebugInfo(NestedSource);
        Assert.DoesNotMatch(@"\$frame\d", plain);
        Snapshot.Verify("ClosureDebug_NoDebugInfo", plain);
    }
}
