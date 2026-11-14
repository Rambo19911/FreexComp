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
}
