using FreexComp.Compiler.Binding;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// Flow analysis (CMP-115, LANG-390): W1040 for-variable after the loop, W1041 use before assignment, W1042 undefined
/// function result, H2001 unused local, H2002 dead store.
/// </summary>
[Trait("Phase", "2")]
public sealed class FlowAnalysisTests
{
    private const string Get = "procedure Get(var v: Integer); begin v := 1 end; procedure GetOut(out v: Integer); begin v := 1 end; ";

    /// <summary>"CODE(line,col)" of every flow diagnostic, in position order (the text must bind without errors).</summary>
    private static string[] Flow(string text)
    {
        var (result, _) = Bind(text);
        Assert.Empty(Format(result.Diagnostics));
        return [.. FlowAnalyzer.Analyze(result).Select(d => $"{d.Code}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1})")];
    }

    /// <summary><paramref name="code"/> at the position marked by <c>|</c> in <paramref name="needle"/> (first occurrence in the one-line <paramref name="text"/>).</summary>
    private static string At(string code, string text, string needle)
    {
        var index = text.IndexOf(needle.Replace("|", ""), StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{needle}' not found");
        return $"{code}(1,{index + needle.IndexOf('|') + 1})";
    }

    private static string Proc(string declarations, string body) => $"program T; {Get}procedure P(b: Boolean); {declarations} begin {body} end; begin P(True) end.";

    // ------------------------------------------------------------------ W1041

    [Fact]
    [Trait("Req", "LANG-390")]
    public void W1041_ReadBeforeAnyAssignment()
    {
        var text = Proc("var x, y: Integer;", "y := x; Writeln(y)");
        Assert.Equal([At("W1041", text, "y := |x")], Flow(text));
    }

    /// <summary>
    /// C-110 (LANG-380): the jumps of a goto are not followed, so nothing is reported for a variable read after a label (it may be
    /// assigned on a path through a goto), no store before a goto or a label is a dead store, and code after a label is reachable.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-380")]
    public void Goto_NoFalseWarningsAroundLabels()
    {
        Assert.Empty(Flow(Proc("label Use, Done; var x, y: Integer;",
            "y := 0; if b then begin x := 1; goto Use end; x := 2; goto Use; Writeln(y); Use: Writeln(x); y := 3; if b then goto Done; y := 4; Done: Writeln(y)")));
        var text = Proc("label L; var x: Integer;", "goto L; L: ; Writeln(x)");
        Assert.Empty(Flow(text));                                      // not analysed past a label: no W1041 either
    }

    [Fact]
    [Trait("Req", "LANG-390")]
    public void W1041_AssignedOnSomePathOnly()
    {
        var text = Proc("var x: Integer;", "if b then x := 1; Writeln(x)");
        Assert.Equal([At("W1041", text, "Writeln(|x)")], Flow(text));
    }

    [Fact]
    [Trait("Req", "LANG-390")]
    public void W1041_NotReported_WhenAssignedOnEveryPath()
    {
        Assert.Empty(Flow(Proc("var x: Integer;", "if b then x := 1 else x := 2; Writeln(x)")));
        Assert.Empty(Flow(Proc("var x: Integer;", "repeat x := 1 until b; Writeln(x)")));
        Assert.Empty(Flow(Proc("var x: Integer;", "case Ord(b) of 0: x := 1; 1: x := 2 else x := 3 end; Writeln(x)")));
        Assert.Empty(Flow(Proc("var x: Integer;", "while True do begin x := 1; Break end; Writeln(x)")));
    }

    [Fact]
    [Trait("Req", "LANG-390")]
    public void W1041_LoopsAndCaseWithoutElse()
    {
        var text = Proc("var x: Integer;", "while b do x := 1; Writeln(x)");
        Assert.Equal([At("W1041", text, "Writeln(|x)")], Flow(text));
        text = Proc("var x: Integer;", "case Ord(b) of 0: x := 1; 1: x := 2 end; Writeln(x)");
        Assert.Equal([At("W1041", text, "Writeln(|x)")], Flow(text));
        text = Proc("var x: Integer;", "while b do begin Writeln(x); x := 1 end");
        Assert.Equal([At("W1041", text, "Writeln(|x)")], Flow(text));
    }

    /// <summary>
    /// C-211 (LANG-390): a variable passed as <c>out</c> on the right of a short-circuit <c>and</c>/<c>or</c> counts as assigned after
    /// the whole expression (as if the right side always ran), so a read guarded by the result is no W1041 (a <c>var</c> argument
    /// keeps the warning, see <see cref="W1041_ShortCircuitRightOperandMayNotRun"/>).
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-390")]
    [InlineData("r := b and Fill(x); if r then Writeln(x)")]
    [InlineData("r := b or Fill(x); if not r then Writeln(x)")]
    [InlineData("r := b; if r and Fill(x) then Writeln(x); Writeln(x)")]
    [InlineData("r := b and (Fill(x) or b); if r then Writeln(x)")]
    public void OutArgumentInShortCircuitAnd_IsAssigned(string body) =>
        Assert.Empty(Flow(Proc("var x: Integer; r: Boolean; function Fill(out v: Integer): Boolean; begin v := 1; Result := True end;", body)));

    [Fact]
    [Trait("Req", "LANG-390")]
    public void W1041_VarAndOutArgumentsCountAsAssignment()
    {
        Assert.Empty(Flow(Proc("var x: Integer;", "Get(x); Writeln(x)")));
        Assert.Empty(Flow(Proc("var x: Integer;", "GetOut(x); Writeln(x)")));
    }

    [Fact]
    [Trait("Req", "LANG-390")]
    public void W1041_OncePerVariable_IncReads_SizeOfDoesNot()
    {
        var text = Proc("var x, y: Integer;", "Inc(x); Writeln(x); y := SizeOf(y); Writeln(y)");
        Assert.Equal([At("W1041", text, "Inc(|x)")], Flow(text));
    }

    [Fact]
    [Trait("Req", "LANG-390")]
    public void W1041_NotForGlobalsCapturedOrUnreachableReads()
    {
        Assert.Empty(Flow("program T; var g: Integer; begin Writeln(g) end."));
        Assert.Empty(Flow(Proc("var x: Integer; procedure Q; begin x := 1 end;", "Q; Writeln(x)")));
        Assert.Empty(Flow(Proc("var x: Integer;", "Exit; Writeln(x)")));
    }

    [Fact]
    [Trait("Req", "LANG-390")]
    public void W1041_ShortCircuitRightOperandMayNotRun()
    {
        var text = "program T; function F(var v: Integer): Boolean; begin v := 1; Result := True end; " +
            "procedure P(b: Boolean); var x: Integer; begin if b and F(x) then Writeln(x); Writeln(x) end; begin P(True) end.";
        Assert.Equal([At("W1041", text, "; Writeln(|x) end")], Flow(text));
    }

    // ------------------------------------------------------------------ W1042

    private static string Func(string body) =>
        $"program T; function F(b: Boolean): Integer; begin {body} end; begin Writeln(F(True)) end.";

    [Fact]
    [Trait("Req", "LANG-390")]
    public void W1042_ResultNeverOrNotAlwaysAssigned_AtTheEndOfTheBody()
    {
        var text = Func("");
        Assert.Equal([At("W1042", text, "begin  |end;")], Flow(text));
        text = Func("if b then Result := 1");
        Assert.Equal([At("W1042", text, "Result := 1 |end")], Flow(text));
        text = Func("if b then Exit; Result := 1");
        Assert.Equal([At("W1042", text, "Result := 1 |end")], Flow(text));
    }

    [Fact]
    [Trait("Req", "LANG-390")]
    public void W1042_NotReported_WhenEveryExitHasAValue()
    {
        Assert.Empty(Flow(Func("F := 1")));
        Assert.Empty(Flow(Func("if b then Exit(1); Result := 2")));
        Assert.Empty(Flow(Func("if b then Result := 1 else Halt")));
        Assert.Empty(Flow("program T; function F: Integer; procedure Q; begin Result := 1 end; begin Q end; begin Writeln(F) end."));
    }

    [Fact]
    [Trait("Req", "LANG-390")]
    public void W1041_ReadingResultBeforeAssignment() =>
        Assert.Equal(["W1041(1,49)"], Flow("program T; function F: Integer; begin Result := Result + 1 end; begin Writeln(F) end."));

    // ------------------------------------------------------------------ W1040

    [Fact]
    [Trait("Req", "LANG-390")]
    public void W1040_ForVariableReadAfterTheLoop()
    {
        var text = Proc("var i: Integer;", "for i := 1 to 3 do if b then Break; Writeln(i)");
        Assert.Equal([At("W1040", text, "Writeln(|i)")], Flow(text));
        text = "program T; var i: Integer; begin for i := 1 to 3 do ; Writeln(i) end.";
        Assert.Equal([At("W1040", text, "Writeln(|i)")], Flow(text));
    }

    [Fact]
    [Trait("Req", "LANG-390")]
    public void W1040_NotReported_InsideTheLoopOrAfterReassignment()
    {
        Assert.Empty(Flow(Proc("var i: Integer;", "for i := 1 to 3 do Writeln(i); i := 0; Writeln(i)")));
        Assert.Empty(Flow("program T; var i: Integer; begin for i := 3 downto 1 do Writeln(i) end."));
    }

    // ------------------------------------------------------------------ H2001

    [Fact]
    [Trait("Req", "LANG-390")]
    public void H2001_LocalNeverReferenced_AtItsDeclaration()
    {
        var text = Proc("var used, unused: Integer;", "used := 1; Writeln(used)");
        Assert.Equal([At("H2001", text, "|unused")], Flow(text));
    }

    [Fact]
    [Trait("Req", "LANG-390")]
    public void H2001_NotReported_ForNestedOrSizeOfUseParametersOrGlobals()
    {
        Assert.Empty(Flow(Proc("var x: Integer; procedure Q; begin Writeln(x) end;", "x := 1; Q")));
        Assert.Empty(Flow(Proc("var x: Integer;", "Writeln(SizeOf(x))")));
        Assert.Empty(Flow("program T; var g: Integer; procedure P(a: Integer); begin end; begin P(1) end."));
    }

    // ------------------------------------------------------------------ H2002

    [Fact]
    [Trait("Req", "LANG-390")]
    public void H2002_AssignedValueNeverRead()
    {
        var text = Proc("var x: Integer;", "x := 1");
        Assert.Equal([At("H2002", text, "|x := 1")], Flow(text));
        text = Proc("var x: Integer;", "x := 1; x := 2; Writeln(x)");
        Assert.Equal([At("H2002", text, "|x := 1")], Flow(text));
        text = Proc("var x: Integer;", "x := 1; GetOut(x); Writeln(x)");
        Assert.Equal([At("H2002", text, "|x := 1")], Flow(text));
    }

    [Fact]
    [Trait("Req", "LANG-390")]
    public void H2002_NotReported_WhenReadOnSomePath()
    {
        Assert.Empty(Flow(Proc("var x: Integer;", "x := 0; while b do begin Writeln(x); x := x + 1 end")));
        Assert.Empty(Flow(Proc("var x: Integer;", "x := 1; if b then x := 2; Writeln(x)")));
        Assert.Empty(Flow(Proc("var x: Integer;", "x := 1; Get(x)")));
        Assert.Empty(Flow(Proc("var x: Integer; procedure Q; begin Writeln(x) end;", "x := 1; Q")));
    }

    private static string WithClass(string body) => $"program T; type EX = class constructor Create; end; constructor EX.Create; begin end; procedure P(b: Boolean); var x: Integer; begin {body} end; begin P(True) end.";

    /// <summary>
    /// C-194 (from R-063): a handler may start at any point of the try part, so a value stored before or inside the try part
    /// and read only after the try statement is used: no H2002 — also when the try part always raises and every handler
    /// overwrites the variable (the defensive initialisation before a try statement).
    /// </summary>

    [Fact]
    [Trait("Req", "LANG-390")]
    [Trait("Req", "LANG-821")]
    public void AssignedInTryReadAfterExcept_NoHint()
    {
        Assert.Empty(Flow(Proc("var x: Integer;", "x := 0; try x := 1; Get(x); x := 2 except Writeln('e') end; Writeln(x)")));
        Assert.Empty(Flow(Proc("var x: Integer;", "x := 0; try Writeln('a'); x := 1 except Writeln('e') end; Writeln(x)")));
        Assert.Empty(Flow(WithClass("x := 0; try Writeln('a'); x := 1 except on E: EX do Writeln('e') end; Writeln(x)")));
        Assert.Empty(Flow(WithClass("x := 0; try Writeln('a'); x := 1 except on E: EX do Writeln('e') else Writeln('f') end; Writeln(x)")));
        Assert.Empty(Flow(WithClass("x := 0; try Writeln('a'); x := 1 except on EX do Writeln('e') end; Writeln(x)")));
        Assert.Empty(Flow(Proc("var x: Integer;", "try x := 1; Writeln('a') except x := 2 end; Writeln(x)")));
        Assert.Empty(Flow(Proc("var x: Integer;", "x := 1; try Writeln('a') finally Writeln('f') end; Writeln(x)")));
        Assert.Empty(Flow(WithClass("x := 0; try raise EX.Create except on E: EX do x := 1 end; Writeln(x)")));
        Assert.Empty(Flow(WithClass("x := 0; try raise EX.Create except x := 1 end; Writeln(x)")));
        Assert.Empty(Flow(WithClass("x := 0; try try raise EX.Create except raise end except on EX do x := 1 end; Writeln(x)")));
        // a store after the try statement is still a dead store
        var text = Proc("var x: Integer;", "try Writeln('a') except Writeln('e') end; x := 3");
        Assert.Equal([At("H2002", text, "|x := 3")], Flow(text));
    }

    /// <summary>
    /// C-177: taking the address of a local counts as a use for H2002 — stores before and after <c>@x</c> may be read
    /// through the pointer, so none of them is reported. Unrelated locals keep their H2002.
    /// </summary>
    /// <summary>
    /// C-062/C-168: <c>Str</c> and <c>Val</c> only write their result variables; <c>SetLength</c>/<c>Delete</c>/<c>Insert</c> and the
    /// atomics read and write their target (an unassigned target is W1041, like <c>Inc</c>).
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-390")]
    public void StringAndAtomicIntrinsics_ReadAndWriteTheirVariables()
    {
        Assert.Empty(Flow(Proc("var s: string; i, c: Integer;", "Val('1', i, c); Str(i:c, s); Writeln(s)")));
        Assert.Empty(Flow(Proc("var s: string; i: Integer;", "s := 'ab'; SetLength(s, 3); Delete(s, 1, 1); Insert('x', s, 1); i := 0; AtomicIncrement(i); Writeln(s, i)")));
        var text = Proc("var i: Integer;", "AtomicIncrement(i); Writeln(i)");
        Assert.Equal([At("W1041", text, "|i);")], Flow(text));
    }

    [Fact]
    [Trait("Req", "LANG-390")]
    public void AddressOfCountsAsUse()
    {
        const string decls = "var x: Integer; p: ^Integer;";
        Assert.Empty(Flow(Proc(decls, "x := 1; p := @x; Writeln(p^)")));
        Assert.Empty(Flow(Proc(decls, "x := 1; p := @x; p^ := 5")));
        Assert.Empty(Flow(Proc(decls, "p := @x; x := 2; Writeln(p^)")));
        Assert.Empty(Flow(Proc(decls, "x := 1; while b do begin p := @x; Writeln(p^); x := 3 end")));
        var text = Proc("var x, y: Integer; p: ^Integer;", "y := 1; x := 1; p := @x; Writeln(p^)");
        Assert.Equal([At("H2002", text, "|y := 1")], Flow(text));
    }

    // ------------------------------------------------------------------ switches (CMP-505) and errors

    [Fact]
    [Trait("Req", "CMP-505")]
    public void SwitchesAtTheReportedPosition_ApplyToFlowDiagnostics()
    {
        Assert.Empty(Flow("program T; {$HINTS OFF} procedure P; var x: Integer; begin end; begin P end."));
        Assert.Empty(Flow("program T; {$WARN USE_BEFORE_DEF OFF} procedure P; var x: Integer; begin Writeln(x) end; begin P end."));

        var (result, _) = Bind("program T; {$WARN W1041 ERROR} procedure P; var x: Integer; begin Writeln(x) end; begin P end.");
        var diagnostic = Assert.Single(FlowAnalyzer.Analyze(result));
        Assert.Equal(("W1041", DiagnosticSeverity.Error, "Variable 'x' might not have been initialized"), (diagnostic.Code, diagnostic.Severity, diagnostic.Message));
    }

    [Fact]
    [Trait("Req", "LANG-390")]
    public void BodiesWithErrors_AreNotAnalyzed()
    {
        var (result, _) = Bind("program T; procedure P; var x, y: Integer; begin y := z end; begin P end.");
        Assert.Equal(["E2003(1,55): Undeclared identifier 'z'"], Format(result.Diagnostics));
        Assert.Empty(FlowAnalyzer.Analyze(result));
    }

    // ------------------------------------------------------------------ H2003 (C-096, diagnostics.md lead decision)

    private static string[] UnusedPrivates(string text) => [.. Flow(text).Where(d => d.StartsWith("H2003", StringComparison.Ordinal))];

    [Fact]
    [Trait("Req", "CMP-500")]
    public void H2003_UnreferencedPrivateMembersOfAProgramClass()
    {
        var text = "program T; type TA = class private F: Integer; G: Integer; H: Integer; procedure M; procedure N; property P: Integer read G; "
            + "class var V: Integer; class var W: Integer; strict private S: Integer; public procedure Run; end; "
            + "procedure TA.M; begin end; procedure TA.N; begin end; procedure TA.Run; begin N; H := 1; W := H end; "
            + "var A: TA; begin A := nil; A.Run end.";
        Assert.Equal(
            [At("H2003", text, "private |F"), At("H2003", text, "procedure |M;"), At("H2003", text, "property |P"),
             At("H2003", text, "class var |V"), At("H2003", text, "|S: Integer")],
            UnusedPrivates(text));
    }

    [Fact]
    [Trait("Req", "CMP-500")]
    public void H2003_ReferencesFromOutsideTheClassAndThroughProperties_Count()
    {
        var text = "program T; type TA = class private F: Integer; function GetX: Integer; procedure SetX(V: Integer); public property X: Integer read GetX write SetX; "
            + "property Y: Integer read F; end; function TA.GetX: Integer; begin Result := 0 end; procedure TA.SetX(V: Integer); begin end; "
            + "type TB = class private Q: Integer; end; var A: TA; B: TB; begin A := nil; B := nil; B.Q := A.X; Writeln(B.Q) end.";
        Assert.Empty(UnusedPrivates(text));
    }

    [Fact]
    [Trait("Req", "CMP-500")]
    public void H2003_NotForVirtualOrOverrideMethods()
    {
        var text = "program T; type TA = class private procedure V; virtual; end; TB = class(TA) private procedure V; override; end; "
            + "procedure TA.V; begin end; procedure TB.V; begin end; var B: TB; begin B := nil; Writeln(B = nil) end.";
        Assert.Empty(UnusedPrivates(text));
    }

    [Fact]
    [Trait("Req", "CMP-500")]
    public void H2003_OnlyForClassesOfTheImplementationPart()
    {
        var text = "unit test; interface type TA = class private F: Integer; end; implementation type TB = class private G: Integer; end; end.";
        Assert.Equal([At("H2003", text, "private |G")], UnusedPrivates(text));
    }

    [Fact]
    [Trait("Req", "CMP-505")]
    public void H2003_FollowsTheHintSwitchAtTheDeclaration()
    {
        Assert.Empty(UnusedPrivates("program T; {$HINTS OFF} type TA = class private F: Integer; end; {$HINTS ON} var A: TA; begin A := nil; Writeln(A = nil) end."));
    }
}
