using FreexComp.Compiler.Lowering;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Lowering;

/// <summary>Bound tree → lowered tree (architecture.md §3.2, CMP-116): loops, <c>for</c> (CMP-321), <c>case</c> (CMP-322), intrinsics (CMP-412).</summary>
[Trait("Phase", "2")]
public sealed class LoweringTests
{
    private static LoweredUnit Lower(string text)
    {
        var (result, _) = Bind(text);
        Assert.Empty(Format(result.Diagnostics));
        return Lowerer.Lower(result);
    }

    /// <summary>The program block of <c>program T; var {globals}; begin {statements} end.</c>, without the outer block.</summary>
    private static string MainBody(string globals, string statements)
    {
        var body = Assert.Single(Lower($"program T; var {globals}; begin {statements} end.").Bodies);
        return string.Join(" ", body.Body.Statements.Select(LoweredDump.Of));
    }

    private static LoweredBody Routine(LoweredUnit unit, string name) => unit.Routines.Single(r => r.Routine!.Name == name);

    /// <summary>The statements of routine <paramref name="name"/> in <c>program T; {declarations} begin end.</c>.</summary>
    private static string Body(string declarations, string name = "F") =>
        string.Join(" ", Routine(Lower($"program T; {declarations} begin end."), name).Body.Statements.Select(LoweredDump.Of));

    private static string Locals(LoweredBody body) => string.Join(" ", body.Locals.Select(l => $"{l.Name}:{l.Type.Name}"));

    // ------------------------------------------------------------------ for (CMP-321, LANG-361)

    [Fact]
    [Trait("Req", "CMP-321")]
    public void For_To_LowersToTestedLoopWithIncrementAfterTheEqualityTest()
    {
        var unit = Lower("program T; var i, s: Integer; begin for i := 1 to 10 do s := s + i end.");
        var main = Assert.Single(unit.Bodies);

        Assert.Equal(
            "(block (:= $tmp1 1:Integer) (:= $tmp2 10:Integer) (if (LessOrEqual $tmp1 $tmp2) (block (:= @i $tmp1) " +
            "(loop for.1 (:= @s (Add:Integer @s @i)) (block (if (Equal @i $tmp2) (break for.1)) (:= @i (Add:Integer @i 1:Integer)))))))",
            LoweredDump.Of(Assert.Single(main.Body.Statements)));
        Assert.Equal("$tmp1:Integer $tmp2:Integer", Locals(main));
        Assert.Null(main.Routine);
    }

    [Fact]
    [Trait("Req", "CMP-321")]
    public void For_DownTo_TestsGreaterOrEqualAndDecrements() =>
        Assert.Equal(
            "(block (:= $tmp1 10:Integer) (:= $tmp2 @n) (if (GreaterOrEqual $tmp1 $tmp2) (block (:= @i $tmp1) " +
            "(loop for.1 (block) (block (if (Equal @i $tmp2) (break for.1)) (:= @i (Subtract:Integer @i 1:Integer)))))))",
            MainBody("i, n: Integer", "for i := 10 downto n do begin end"));

    /// <summary>Byte variable: the step is computed in the computation type and truncated back (no overflow at High).</summary>
    [Fact]
    [Trait("Req", "CMP-321")]
    public void For_ByteVariable_StepsInTheComputationType()
    {
        var unit = Lower("program T; procedure P; var b: Byte; begin for b := 0 to 255 do end; begin end.");
        var p = Routine(unit, "P");

        Assert.Equal(
            "(block (:= $tmp1 0:Byte) (:= $tmp2 255:Byte) (if (LessOrEqual $tmp1 $tmp2) (block (:= b $tmp1) (loop for.1 (block) " +
            "(block (if (Equal b $tmp2) (break for.1)) (:= b (conv Byte (Add:Integer (conv Integer b) 1:Integer))))))))",
            LoweredDump.Of(Assert.Single(p.Body.Statements)));
        Assert.Equal("b:Byte $tmp1:Byte $tmp2:Byte", Locals(p));
    }

    // ------------------------------------------------------------------ Write/Writeln (CMP-410; C-040)

    [Fact]
    [Trait("Req", "CMP-410")]
    public void Writeln_ToOutput_HelperPerArgumentThenLnEndAndIOCheck() =>
        Assert.Equal(
            "(block (call _WriteUStr (addr @System.Output) 'ab' 0:Integer) (call _WriteInt (addr @System.Output) (conv Int64 @i) @i) " +
            "(call _WriteLn (addr @System.Output)) (call _WriteEnd (addr @System.Output)) (call _IOCheck)) " +
            "(block (call _WriteLn (addr @System.Output)) (call _WriteEnd (addr @System.Output)))",
            MainBody("i: Integer", "Writeln('ab', i:i); {$I-} Writeln()"));

    // ------------------------------------------------------------------ checks (CMP-312, 314; C-039)

    [Fact]
    [Trait("Req", "CMP-314")]
    public void Checks_ForVariableUnderBothSwitches_BoundsCheckedStepNot()
    {
        var unit = Lower("program T; {$R+}{$Q+} procedure P(n: Integer); var b: Byte; begin for b := 1 to n do end; begin end.");

        Assert.Equal(
            "(block (:= $tmp1 1:Byte) (:= $tmp2 (conv Byte (range 0..255 n))) (if (LessOrEqual $tmp1 $tmp2) (block (:= b $tmp1) (loop for.1 (block) " +
            "(block (if (Equal b $tmp2) (break for.1)) (:= b (conv Byte (Add:Integer (conv Integer b) 1:Integer))))))))",
            LoweredDump.Of(Assert.Single(Routine(unit, "P").Body.Statements)));
    }

    [Fact]
    [Trait("Req", "CMP-314")]
    public void Checks_RangeBoundsAreIntersectedWithTheSourceType() =>
        Assert.Equal(
            "(:= Result (conv Integer (range 0..2147483647 c))) (:= Result (conv Integer c)) (:= Result (conv Integer b))",
            Body("{$R+} function F(c: Cardinal; b: Byte): Integer; begin Result := c; Result := Integer(c); Result := b end;"));

    [Fact]
    [Trait("Req", "CMP-312")]
    public void Checks_IncUnderQPlus_IsAnOverflowCheckedAdd_ChecksOff_Plain()
    {
        Assert.Equal("(:= i (Add!:Integer i 1:Integer)) (:= i (Negate!:Integer i))",
            Body("{$Q+} procedure F(var i: Integer); begin Inc(i); i := -i end;"));
        Assert.Equal("(:= i (Add:Integer i 1:Integer)) (:= i (Negate:Integer i))",
            Body("procedure F(var i: Integer); begin Inc(i); i := -i end;"));
    }

    [Fact]
    [Trait("Req", "LANG-363")]
    public void For_BreakAndContinue_TargetTheLoop_ContinueRunsTheStep() =>
        Assert.Contains(
            "(loop for.1 (block (if (Equal @i 2:Integer) (continue for.1)) (if (Equal @i 3:Integer) (break for.1))) (block (if (Equal @i $tmp2)",
            MainBody("i: Integer", "for i := 1 to 3 do begin if i = 2 then Continue; if i = 3 then Break end"));

    [Fact]
    [Trait("Req", "LANG-363")]
    public void For_Nested_HaveOwnLabelsAndTemporaries_BreakLeavesTheInnerLoop()
    {
        var text = MainBody("i, j: Integer", "for i := 1 to 2 do for j := i to 3 do Break");

        Assert.Contains("(:= $tmp3 @i) (:= $tmp4 3:Integer) (if (LessOrEqual $tmp3 $tmp4) (block (:= @j $tmp3) (loop for.2 (break for.2)", text);
        Assert.Contains("(if (Equal @i $tmp2) (break for.1))", text);
    }

    [Fact]
    [Trait("Req", "CMP-321")]
    public void For_CharAndBooleanVariables()
    {
        var text = MainBody("c: Char; f: Boolean", "for c := 'a' to 'z' do; for f := False to True do");

        Assert.Contains("(:= @c (conv Char (Add:Integer (conv Integer @c) 1:Integer)))", text);
        Assert.Contains("(:= $tmp3 False:Boolean) (:= $tmp4 True:Boolean)", text);
        Assert.Contains("(:= @f (conv Boolean (Add:Integer (conv Integer @f) 1:Integer)))", text);
    }

    // ------------------------------------------------------------------ while, repeat (LANG-360, CMP-319)

    [Fact]
    [Trait("Req", "CMP-319")]
    public void While_IsALoopWhoseBodyTestsFirst() =>
        Assert.Equal(
            "(loop while.1 (if (Less @i 3:Integer) (block (if (Equal @i 1:Integer) (continue while.1)) (:= @i (Add:Integer @i 1:Integer))) (break while.1)))",
            MainBody("i: Integer", "while i < 3 do begin if i = 1 then Continue; i := i + 1 end"));

    [Fact]
    [Trait("Req", "CMP-319")]
    public void Repeat_TestsInTheStep_SoContinueReachesTheTest() =>
        Assert.Equal(
            "(loop repeat.1 (block (:= @i (Add:Integer @i 1:Integer)) (continue repeat.1)) (if (GreaterOrEqual @i 3:Integer) (break repeat.1)))",
            MainBody("i: Integer", "repeat i := i + 1; Continue until i >= 3"));

    // ------------------------------------------------------------------ case (CMP-322, LANG-355…357)

    [Fact]
    [Trait("Req", "CMP-322")]
    public void Case_ValuesAndSmallRangesAreListed_LargeRangesKept_ElseIsTheDefault() =>
        Assert.Equal(
            "(switch @i (case [1] [] (:= @s 1:Integer)) (case [2 4 5 6] [] (:= @s 2:Integer)) (case [] [10..1000] (:= @s 3:Integer)) " +
            "(default (block (:= @s 0:Integer))))",
            MainBody("i, s: Integer", "case i of 1: s := 1; 2, 4..6: s := 2; 10..1000: s := 3 else s := 0 end"));

    [Theory]
    [InlineData("1..256", 256, 0)]
    [InlineData("1..257", 0, 1)]
    [InlineData("-5..5, 300..301", 13, 0)]
    [Trait("Req", "CMP-322")]
    public void Case_RangesUpTo256ValuesAreExpanded(string labels, int values, int ranges)
    {
        var unit = Lower($"program T; var i: Integer; begin case i of {labels}: i := 0 end end.");
        var section = Assert.Single(Assert.IsType<LSwitch>(Assert.Single(unit.Bodies).Body.Statements[0]).Sections);

        Assert.Equal((values, ranges), (section.Values.Length, section.Ranges.Length));
    }

    [Fact]
    [Trait("Req", "CMP-322")]
    public void Case_CharSelector_IsWidenedToInteger_NoElseNoDefault() =>
        Assert.Equal(
            "(switch (conv Integer @c) (case [97] [] (:= @c 'b':Char)) (case [120 121 122] [] (:= @c 'a':Char)))",
            MainBody("c: Char", "case c of 'a': c := 'b'; 'x'..'z': c := 'a' end"));

    [Theory]
    [InlineData("Byte", "(conv Integer @x)")]
    [InlineData("SmallInt", "(conv Integer @x)")]
    [InlineData("Integer", "@x")]
    [InlineData("Cardinal", "@x")]
    [InlineData("Int64", "@x")]
    [InlineData("UInt64", "@x")]
    [Trait("Req", "CMP-322")]
    public void Case_SelectorInItsComputationType(string type, string selector) =>
        Assert.StartsWith($"(switch {selector} (case [5]", MainBody("x: " + type, "case x of 5: x := 1 end"));

    [Fact]
    [Trait("Req", "CMP-322")]
    public void Case_BooleanSelector_LabelsAreOrdinals() =>
        Assert.Equal(
            "(switch (conv Integer @f) (case [1] [] (:= @f False:Boolean)) (case [0] [] (:= @f True:Boolean)))",
            MainBody("f: Boolean", "case f of True: f := False; False: f := True end"));

    // ------------------------------------------------------------------ intrinsics (CMP-412, intrinsics.md §1)

    [Theory]
    [InlineData("x: Integer", "Succ(x)", "(Add:Integer x 1:Integer)")]
    [InlineData("x: Integer", "Pred(x)", "(Subtract:Integer x 1:Integer)")]
    [InlineData("x: Int64", "Succ(x)", "(conv Integer (Add:Int64 x 1:Int64))")]
    [InlineData("x: Byte", "Pred(x)", "(conv Integer (conv Byte (Subtract:Integer (conv Integer x) 1:Integer)))")]
    [InlineData("x: Char", "Ord(x)", "(conv Integer x)")]
    [InlineData("x: Integer", "Ord(x)", "x")]
    [InlineData("x: Boolean", "Ord(x)", "(conv Integer x)")]
    [InlineData("x: Integer", "Ord(Chr(x))", "(conv Integer (conv Char x))")]
    [InlineData("x: Integer", "Ord(Odd(x))", "(conv Integer (NotEqual (And:Integer x 1:Integer) 0:Integer))")]
    [InlineData("x: Integer", "Abs(x)", "(let $v1 x (select (Less $v1 0:Integer) (Negate:Integer $v1) $v1))")]
    [InlineData("x: Byte", "Abs(x)", "(let $v1 (conv Integer x) (select (Less $v1 0:Integer) (Negate:Integer $v1) $v1))")]
    [InlineData("x: Integer", "Sqr(x)", "(let $v1 x (Multiply:Integer $v1 $v1))")]
    [InlineData("x: Integer", "Lo(x)", "(conv Integer (conv Byte x))")]
    [InlineData("x: Word", "Hi(x)", "(conv Integer (conv Byte (Shr:Integer (conv Integer x) 8:Integer)))")]
    [InlineData("x: Int64", "Hi(x)", "(conv Integer (conv Byte (Shr:Int64 x 8:Int64)))")]
    [InlineData("x: Integer", "Swap(x)",
        "(conv Integer (conv Word (let $v1 x (Or:Integer (Shl:Integer (And:Integer $v1 255:Integer) 8:Integer) (And:Integer (Shr:Integer $v1 8:Integer) 255:Integer)))))")]
    [InlineData("x: Integer", "Succ(5) + SizeOf(Int64) + High(Byte)", "269:Integer")]
    [Trait("Req", "CMP-412")]
    public void Intrinsic_ValueFunctions(string parameter, string expression, string expected) =>
        Assert.Equal($"(:= Result {expected})", Body($"function F({parameter}): Integer; begin Result := {expression} end;"));

    [Theory]
    [InlineData("x: Double", "Abs(x)", "(Abs:Double x)")]
    [InlineData("x: Double", "Sqr(x)", "(let $v1 x (Multiply:Double $v1 $v1))")]
    [Trait("Req", "CMP-412")]
    public void Intrinsic_RealAbsAndSqr(string parameter, string expression, string expected) =>
        Assert.Equal($"(:= Result {expected})", Body($"function F({parameter}): Double; begin Result := {expression} end;"));

    [Fact]
    [Trait("Req", "CMP-412")]
    public void Intrinsic_TempsAreNumberedPerBody() =>
        Assert.Equal(
            "(:= Result (Add:Integer (let $v1 x (Multiply:Integer $v1 $v1)) (let $v2 x (select (Less $v2 0:Integer) (Negate:Integer $v2) $v2))))",
            Body("function F(x: Integer): Integer; begin Result := Sqr(x) + Abs(x) end;"));

    [Theory]
    [InlineData("var x: Integer", "Inc(x)", "(:= x (Add:Integer x 1:Integer))")]
    [InlineData("var x: Integer", "Dec(x, 5)", "(:= x (Subtract:Integer x 5:Integer))")]
    [InlineData("var x: Byte", "Inc(x, 3)", "(:= x (conv Byte (Add:Integer (conv Integer x) 3:Integer)))")]
    [InlineData("var x: Int64; n: Integer", "Dec(x, n)", "(:= x (Subtract:Int64 x (conv Int64 n)))")]
    [InlineData("var x: Byte; n: Int64", "Inc(x, n)", "(:= x (conv Byte (Add:Integer (conv Integer x) (conv Integer n))))")]
    [InlineData("var x: Char", "Inc(x)", "(:= x (conv Char (Add:Integer (conv Integer x) 1:Integer)))")]
    [InlineData("var x: Cardinal", "Dec(x)", "(:= x (Subtract:Cardinal x 1:Cardinal))")]
    [Trait("Req", "CMP-412")]
    public void Intrinsic_IncDec_AssignInline(string parameter, string statement, string expected) =>
        Assert.Equal(expected, Body($"procedure F({parameter}); begin {statement} end;"));

    [Theory]
    [InlineData("RunError", "(block (call _RunError 0:Integer) (unreachable))")]
    [InlineData("RunError(204)", "(block (call _RunError 204:Integer) (unreachable))")]
    [InlineData("Halt", "(block (call _Halt 0:Integer) (unreachable))")]
    [InlineData("Halt(3)", "(block (call _Halt 3:Integer) (unreachable))")]
    [Trait("Req", "CMP-356")]
    public void Intrinsic_HaltAndRunError_CallNoReturnHelpers(string statement, string expected)
    {
        var body = Assert.Single(Lower($"program T; begin {statement} end.").Bodies);
        var call = Assert.IsType<LCallStatement>(Assert.IsType<LBlock>(body.Body.Statements[0]).Statements[0]);

        Assert.Equal(expected, LoweredDump.Of(body.Body.Statements[0]));
        var helper = Assert.IsType<LHelperCallee>(call.Callee).Helper;
        Assert.True(helper.IsNoReturn);
        Assert.Null(helper.ReturnType);
        Assert.Equal(["Integer"], helper.ParameterTypes.Select(t => t.Name));
        Assert.Equal(new Compiler.Emit.LinkName("System." + helper.Name, true), helper.LinkName);
    }

    // ------------------------------------------------------------------ routines, calls, exit (CMP-310)

    [Fact]
    [Trait("Req", "CMP-310")]
    public void Exit_WithValue_AssignsResultThenReturns() =>
        Assert.Equal(
            "(if (Less n 0:Integer) (block (:= Result 7:Integer) (return))) (:= Result n) (return)",
            Body("function F(n: Integer): Integer; begin if n < 0 then Exit(7); Result := n; Exit end;"));

    [Fact]
    [Trait("Req", "CMP-310")]
    public void Locals_AreResultThenDeclaredLocalsThenTemporaries_ParametersAreNotLocals()
    {
        var unit = Lower("program T; function F(n: Integer): Int64; var a, b: Byte; begin for a := 1 to 2 do b := a; Result := b end; begin end.");
        var f = Routine(unit, "F");

        Assert.Equal("Result:Int64 a:Byte b:Byte $tmp1:Byte $tmp2:Byte", Locals(f));
        Assert.All(f.Locals.Take(3), l => Assert.NotNull(l.Variable));
        Assert.All(f.Locals.Skip(3), l => Assert.Null(l.Variable));
    }

    [Fact]
    [Trait("Req", "CMP-310")]
    public void Calls_VarAndOutArgumentsPassTheirAddress()
    {
        var unit = Lower("program T; var i, s: Integer; procedure P(var a: Integer; out o: Integer; b: Byte; const c: Integer); begin a := b; o := c end; " +
            "function G(x: Integer): Integer; begin Result := x end; begin P(i, s, 2, G(i)); G(3) end.");

        Assert.Equal("(call P (addr @i) (addr @s) 2:Byte (call G:Integer @i)) (call G 3:Integer)",
            string.Join(" ", Assert.Single(unit.Bodies).Body.Statements.Select(LoweredDump.Of)));
        Assert.Equal("(:= a (conv Integer b)) (:= o c)", string.Join(" ", Routine(unit, "P").Body.Statements.Select(LoweredDump.Of)));
    }

    [Fact]
    public void Expressions_ConversionsOperatorsAndShortCircuit() =>
        Assert.Equal(
            "(if (ConditionalAnd:Boolean (Greater @i 0:Integer) (Not:Boolean @f)) (:= @q (Multiply:Int64 (conv Int64 (Negate:Integer @i)) @q)) (:= @f True:Boolean))",
            MainBody("i: Integer; q: Int64; f: Boolean", "if (i > 0) and not f then q := (-i) * q else f := True"));

    [Fact]
    public void UnitBodies_InitializationAndFinalizationAreLowered()
    {
        var unit = Lower("unit U; interface var X: Integer; implementation initialization X := 1; finalization X := 0; end.");

        Assert.Equal(["(:= @X 1:Integer)", "(:= @X 0:Integer)"], unit.Bodies.Select(b => string.Join(" ", b.Body.Statements.Select(LoweredDump.Of))));
        Assert.Equal("U", unit.Unit.Name);
    }

    /// <summary>
    /// CMP-345: a variable of an enclosing routine lowers to <see cref="LOuterRef"/>; the owner lists its captured variables in
    /// its <see cref="LoweredFrame"/> (scope order).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-345")]
    public void CapturedVariables_LowerToOuterReferences_AndTheOwnerGetsAFrame()
    {
        var unit = Lower("program T; procedure Outer(p: Integer); var n, m: Integer; procedure Inner; begin n := p end; begin Inner end; begin end.");
        var outer = Routine(unit, "Outer");
        var inner = Routine(unit, "Inner");

        Assert.Equal("(:= ^n ^p)", LoweredDump.Of(Assert.Single(inner.Body.Statements)));
        Assert.Equal(["p", "n"], outer.Frame!.Variables.Select(v => v.Name));
        Assert.Null(inner.Frame);
        Assert.Equal("(call Inner)", LoweredDump.Of(Assert.Single(outer.Body.Statements)));
    }

    /// <summary>A nested routine that itself has nested routines gets a frame (field 0 only) so the static link chain can be walked.</summary>
    [Fact]
    [Trait("Req", "CMP-345")]
    public void IntermediateNestedRoutine_GetsALinkOnlyFrame_TopLevelWithoutCapturesGetsNone()
    {
        var unit = Lower("program T; procedure A; procedure B; procedure C; begin end; begin C end; begin B end; begin end.");

        Assert.Null(Routine(unit, "A").Frame);
        Assert.Empty(Routine(unit, "B").Frame!.Variables);
        Assert.Null(Routine(unit, "C").Frame);
    }
}
