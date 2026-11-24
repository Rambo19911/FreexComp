using FreexComp.Compiler.Lowering;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Lowering;

/// <summary>
/// C-306 (inline-variables.md LANG-560…565, CMP-1180…1182): where the managed variables of a declaration scope are finalised —
/// in the scope's region (<c>(scope body cleanup)</c> in the dump) or in the routine's cleanup scope.
/// </summary>
[Trait("Phase", "8")]
public sealed class InlineScopeLoweringTests
{
    private static LoweredBody Routine(string declarations, string name = "F")
    {
        var (result, _) = Bind($"program T; {declarations} begin end.");
        Assert.Empty(Format(result.Diagnostics));
        return Lowerer.Lower(result).Routines.Single(r => r.Routine!.Name == name);
    }

    private static string Body(LoweredBody body) => string.Join(" ", body.Body.Statements.Select(LoweredDump.Of));

    private static string Cleanup(LoweredBody body) => body.Cleanup is { } cleanup ? LoweredDump.Of(cleanup) : "none";

    /// <summary>LANG-565: variables of the routine's outermost statement list have no region; the epilogue finalises them.</summary>
    [Fact]
    [Trait("Req", "LANG-565")]
    [Trait("Req", "CMP-1180")]
    public void OutermostListUsesRoutineCleanup()
    {
        var body = Routine("procedure F(const a: string); begin var s: string; s := a; var t := s; end;");

        Assert.DoesNotContain("(scope", Body(body));
        Assert.StartsWith("(zero (addr s)) (zero (addr t)) ", Body(body));
        Assert.Equal("(block (call _UStrClr (addr s)) (call _UStrClr (addr t)))", Cleanup(body));
    }

    /// <summary>CMP-1180: a scope without managed variables is lowered as before — no region, no cleanup.</summary>
    [Fact]
    [Trait("Req", "CMP-1180")]
    [Trait("Req", "CMP-1182")]
    public void NonManagedScopeHasNoRegion()
    {
        var body = Routine("procedure F(c: Boolean); begin if c then begin var n: Integer; n := 1; var p: Pointer := nil; end; for var i := 1 to 3 do begin var k := i; end end;");

        Assert.DoesNotContain("(scope", Body(body));
        Assert.Equal("none", Cleanup(body));
    }

    /// <summary>
    /// CMP-1180, 1181: the managed variables of a nested list share one region that starts at the first managed declaration and
    /// clears them in reverse order; they are zeroed in the prologue but absent from the routine's cleanup, which keeps the others.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1180")]
    [Trait("Req", "CMP-1181")]
    [Trait("Req", "LANG-563")]
    public void ScopeVariablesNotInRoutineExitList()
    {
        var body = Routine("procedure F(c: Boolean; const a: string); var r: string; begin r := a; if c then begin var n := 1; var s: string; s := a; var t: string; t := s; end end;");

        var text = Body(body);
        Assert.StartsWith("(zero (addr r)) (zero (addr s)) (zero (addr t)) ", text);
        const string Region =
            "(block (:= n 1:Integer) (scope (block (call _UStrAsg (addr s) a) (block (call _UStrAsg (addr t) s))) " +
            "(block (call _UStrClr (addr t)) (call _UStrClr (addr s)))))";
        Assert.True(text.Contains(Region, StringComparison.Ordinal), text);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, @"\(scope "));
        Assert.Equal("(block (call _UStrClr (addr r)))", Cleanup(body));
    }

    /// <summary>LANG-564: the variable of <c>for var … in</c> is finalised when the loop is left; the loop's hidden temporaries are not.</summary>
    [Fact]
    [Trait("Req", "LANG-564")]
    public void ForVarVariableIsFinalisedWithTheLoop()
    {
        var body = Routine("type TA = array of string; procedure F(const a: TA); begin for var s in a do begin end end;");

        Assert.Contains("(scope ", Body(body));
        Assert.EndsWith(" (block (call _UStrClr (addr s))))", Body(body));
        Assert.DoesNotContain("(addr s)", Cleanup(body));
    }

    /// <summary>
    /// LANG-560: the <c>until</c> condition is inside the scope — it is evaluated in the region, into a flag tested after the
    /// finalisation; <c>Continue</c> leaves the one-pass loop around the statements, so the condition still runs.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-560")]
    public void RepeatConditionIsInsideTheRegion()
    {
        var body = Body(Routine("procedure F(const a: string); begin repeat var s: string; if a = '' then Continue; s := a; until s <> ''; end;"));

        Assert.Contains("(loop repeat.1 (scope (block (loop repeat.scope.2 ", body);
        Assert.Contains("(break repeat.scope.2)) (call _UStrAsg (addr s) a)", body);                 // Continue
        Assert.Contains("(block (call _UStrClr (addr s)))) (if $tmp1 (break repeat.1))", body);      // finalised, then the test
        Assert.DoesNotContain("(continue", body);
    }

    /// <summary>
    /// LANG-560, 561: a variable declared in a finally list is storage of the routine — in its frame record, where a
    /// <c>try … finally</c> nested in that finally part reaches it — and the finally part's region finalises it.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-560")]
    [Trait("Req", "LANG-561")]
    public void FinallyListVariableLivesInTheRoutine()
    {
        const string Source = "procedure Use(const a: string); begin end; " +
            "procedure F(const a: string); begin try Use(a) finally var s: string; s := a; try Use(a) finally Use(s) end end end;";
        var (result, _) = Bind($"program T; {Source} begin end.");
        Assert.Empty(Format(result.Diagnostics));
        var unit = Lowerer.Lower(result);
        var routine = unit.Routines.Single(r => r.Routine!.Name == "F");
        var part = unit.Routines.Single(r => r.Routine!.Name == "$fin1" && ReferenceEquals(r.Routine.ContainingSymbol, routine.Routine));

        Assert.Contains(routine.Locals, l => l.Name == "s");
        Assert.Contains(routine.Frame!.Variables, v => v.Name == "s");
        Assert.DoesNotContain(part.Locals, l => l.Name == "s");
        Assert.DoesNotContain("(addr s)", Cleanup(routine));
        Assert.Contains("(scope ", Body(part));

        var writer = new FreexComp.Compiler.Emit.LlvmTextWriter();
        FreexComp.Compiler.Emit.IrEmitter.EmitUnit(unit, writer);               // threw: the frame variable had no local
        Assert.Contains("define internal void @\"T.F.$fin1.$fin1\"", writer.GetText());
    }
}
