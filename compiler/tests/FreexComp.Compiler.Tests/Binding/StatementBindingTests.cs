using FreexComp.Compiler.Binding;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Structured statements and the control intrinsics (expressions-statements.md LANG-353…364).</summary>
[Trait("Phase", "2")]
public sealed partial class StatementBindingTests
{
    private const string Variables = "var i, j: Integer; b: Byte; flag: Boolean; bb: ByteBool; ch: Char; d: Double; ";

    /// <summary>The dump of the program block of <c>Variables + begin {body} end.</c>; asserts no diagnostics.</summary>
    private static string Body(string body)
    {
        var (result, _) = Bind($"{Variables}begin {body} end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(Assert.Single(result.Bodies));
    }

    private static string[] Diagnostics(string body) => BindDiagnostics($"{Variables}begin {body} end.");

    // ------------------------------------------------------------------ LANG-353/354: compound, if

    [Fact]
    [Trait("Req", "LANG-353")]
    public void Compound_BindsStatementsInOrder_AndDropsEmptyOnes() =>
        Assert.Equal("(block (block (:= i:Integer 1:Integer) (:= j:Integer 2:Integer)))", Body("begin i := 1;; j := 2; end"));

    [Fact]
    [Trait("Req", "LANG-354")]
    public void If_BindsConditionAndBothBranches() =>
        Assert.Equal(
            "(block (if flag:Boolean (:= i:Integer 1:Integer) (:= i:Integer 2:Integer)))",
            Body("if flag then i := 1 else i := 2"));

    [Fact]
    [Trait("Req", "LANG-354")]
    public void If_WithoutElse_HasOneBranch() =>
        Assert.Equal("(block (if (Less:Boolean i:Integer j:Integer) (:= i:Integer j:Integer)))", Body("if i < j then i := j"));

    [Fact]
    [Trait("Req", "LANG-215")]
    public void Condition_OfCStyleBoolean_IsNormalisedToBoolean() =>
        Assert.Equal("(block (while (conv Boolean bb:ByteBool) (block)))", Body("while bb do begin end"));

    [Theory]
    [Trait("Req", "LANG-354")]
    [InlineData("if i then ;", "E2061(1,88): Boolean expression required but 'Integer' found")]
    [InlineData("while d do ;", "E2061(1,91): Boolean expression required but 'Double' found")]
    [InlineData("repeat until ch", "E2061(1,98): Boolean expression required but 'Char' found")]
    [InlineData("if 1 then ;", "E2061(1,88): Boolean expression required but 'Integer' found")]
    public void Condition_MustBeBoolean(string body, string expected) => Assert.Equal([expected], Diagnostics(body));

    [Fact]
    public void Condition_WithErrors_GivesNoFurtherDiagnostic() =>
        Assert.Equal(["E2003(1,88): Undeclared identifier 'x'"], Diagnostics("if x then i := 1"));

    // ------------------------------------------------------------------ LANG-360: while, repeat

    [Fact]
    [Trait("Req", "LANG-360")]
    public void While_BindsConditionAndBody() =>
        Assert.Equal("(block (while (Greater:Boolean i:Integer 0:Integer) (:= i:Integer (Subtract:Integer i:Integer 1:Integer))))",
            Body("while i > 0 do i := i - 1"));

    [Fact]
    [Trait("Req", "LANG-360")]
    public void Repeat_BindsBodyAsBlock_ThenCondition() =>
        Assert.Equal("(block (repeat (block (:= i:Integer 1:Integer) (:= j:Integer 2:Integer)) flag:Boolean))",
            Body("repeat i := 1; j := 2 until flag"));

    // ------------------------------------------------------------------ LANG-361: for

    [Fact]
    [Trait("Req", "LANG-361")]
    public void For_ConvertsBoundsToTheVariableType() =>
        Assert.Equal("(block (for b:Byte to 1:Byte (conv Byte i:Integer) (:= j:Integer (conv Integer b:Byte))))",
            Body("for b := 1 to i do j := b"));

    [Fact]
    [Trait("Req", "LANG-361")]
    public void For_DownTo() =>
        Assert.Equal("(block (for i:Integer downto j:Integer 0:Integer (block)))", Body("for i := j downto 0 do begin end"));

    [Fact]
    [Trait("Req", "LANG-361")]
    public void For_OrdinalCharVariable() =>
        Assert.Equal("(block (for ch:Char to 'a':Char 'z':Char (block)))", Body("for ch := 'a' to 'z' do begin end"));

    [Theory]
    [Trait("Req", "LANG-361")]
    [InlineData("for d := 1 to 2 do ;", "E2065(1,89): For-loop control variable must be a simple local variable of ordinal type")]
    [InlineData("for b := 0 to 256 do ;", "E2012(1,99): Constant value 256 is out of range for type 'Byte'")]
    [InlineData("for i := 1 to d do ;", "E2010(1,99): Incompatible types: 'Double' and 'Integer'")]
    public void For_Errors(string body, string expected) => Assert.Equal([expected], Diagnostics(body));

    /// <summary>C-395: a constant start or end bound outside a written <c>Integer</c> control variable's range is E2012 at the bound.</summary>
    [Theory]
    [Trait("Req", "LANG-361")]
    [InlineData("for i := 0 to 3000000000 do ;", "E2012(1,99): Constant value 3000000000 is out of range for type 'Integer'")]
    [InlineData("for i := 3000000000 to 1 do ;", "E2012(1,94): Constant value 3000000000 is out of range for type 'Integer'")]
    [InlineData("for i := 3000000000 downto 0 do ;", "E2012(1,94): Constant value 3000000000 is out of range for type 'Integer'")]
    [InlineData("for i := 0 downto -3000000000 do ;", "E2012(1,103): Constant value -3000000000 is out of range for type 'Integer'")]
    [InlineData("for i := 0 to High(Int64) do ;", "E2012(1,99): Constant value 9223372036854775807 is out of range for type 'Integer'")]
    [InlineData("for var k: Integer := 0 to 3000000000 do ;", "E2012(1,112): Constant value 3000000000 is out of range for type 'Integer'")]
    [InlineData("for var k: Integer := 3000000000 downto 0 do ;", "E2012(1,107): Constant value 3000000000 is out of range for type 'Integer'")]
    public void ForLoop_ConstantBoundOutOfRange_E2012(string body, string expected) => Assert.Equal([expected], Diagnostics(body));

    [Fact]
    [Trait("Req", "LANG-361")]
    public void For_ConstantAsVariable_IsE2065() =>
        Assert.Equal(["E2065(1,24): For-loop control variable must be a simple local variable of ordinal type"],
            BindDiagnostics("const K = 1; begin for K := 1 to 2 do ; end."));

    [Theory]
    [Trait("Req", "LANG-361")]
    [InlineData("for i := 1 to 2 do i := 3", "E2053(1,104): Left side cannot be assigned to")]
    [InlineData("for i := 1 to 2 do begin j := i; for i := 1 to 2 do ; end", "E2053(1,122): Left side cannot be assigned to")]
    public void For_AssigningTheVariableInsideTheLoop_IsE2053(string body, string expected) => Assert.Equal([expected], Diagnostics(body));

    [Fact]
    [Trait("Req", "LANG-361")]
    public void For_VariableIsAssignableAgainAfterTheLoop() =>
        Assert.Equal("(block (for i:Integer to 1:Integer 2:Integer (block)) (:= i:Integer 5:Integer))",
            Body("for i := 1 to 2 do begin end; i := 5"));

    // ------------------------------------------------------------------ LANG-355…357: case

    [Fact]
    [Trait("Req", "LANG-355")]
    public void Case_BindsLabelsArmsAndElse() =>
        Assert.Equal(
            "(block (case i:Integer (arm [1..5] (:= j:Integer 1:Integer)) (arm [9, 10] (:= j:Integer 2:Integer)) " +
            "(else (block (:= j:Integer 3:Integer) (:= j:Integer 4:Integer)))))",
            Body("case i of 1..5: j := 1; 9, 10: j := 2 else j := 3; j := 4 end"));

    [Fact]
    [Trait("Req", "LANG-355")]
    public void Case_LabelsAreConvertedToTheSelectorType() =>
        Assert.Equal("(block (case b:Byte (arm [200] (block))))", Body("case b of 200: begin end end"));

    [Fact]
    [Trait("Req", "LANG-355")]
    public void Case_OnCharAndBoolean() =>
        Assert.Equal("(block (case ch:Char (arm ['a'..'z', '_'] (block))) (case flag:Boolean (arm [True] (block)) (arm [False] (block))))",
            Body("case ch of 'a'..'z', '_': begin end end; case flag of True: begin end; False: begin end end"));

    [Theory]
    [Trait("Req", "LANG-355")]
    [InlineData("case d of 1: ; end", "E2062(1,90): Case selector must be of an ordinal type")]
    [InlineData("case i of 5..1: ; end", "E2063(1,95): Case label range: low bound exceeds high bound")]
    [InlineData("case i of 1, 2, 1: ; end", "E2064(1,101): Duplicate case label 1")]
    [InlineData("case i of 1..5: ; 3..7: ; end", "E2064(1,103): Duplicate case label 3..7")]
    [InlineData("case i of 'a': ; end", "E2010(1,95): Incompatible types: 'Char' and 'Integer'")]
    [InlineData("case b of 300: ; end", "E2012(1,95): Constant value 300 is out of range for type 'Byte'")]
    [InlineData("case i of j: ; end", "E2050(1,95): Constant expression expected")]
    public void Case_Errors(string body, string expected) => Assert.Equal([expected], Diagnostics(body));

    // ------------------------------------------------------------------ LANG-363/364: Break, Continue, Exit, Halt

    [Fact]
    [Trait("Req", "LANG-363")]
    public void BreakAndContinue_InsideLoops() =>
        Assert.Equal(
            "(block (while flag:Boolean (block (break) (continue))) (repeat (block (if flag:Boolean (break))) flag:Boolean) " +
            "(for i:Integer to 1:Integer 2:Integer (case i:Integer (arm [1] (continue)))))",
            Body("while flag do begin Break; Continue end; repeat if flag then break until flag; for i := 1 to 2 do case i of 1: Continue() end"));

    [Theory]
    [Trait("Req", "LANG-363")]
    [InlineData("Break", "E2066(1,85): 'Break' is only allowed inside a loop")]
    [InlineData("if flag then continue", "E2066(1,98): 'Continue' is only allowed inside a loop")]
    [InlineData("while flag do ; Break", "E2066(1,101): 'Break' is only allowed inside a loop")]
    [InlineData("while flag do Break(1)", "E2111(1,105): Too many actual parameters for 'Break'")]
    public void BreakAndContinue_Errors(string body, string expected) => Assert.Equal([expected], Diagnostics(body));

    [Fact]
    [Trait("Req", "LANG-363")]
    public void Exit_InTheProgramBody() => Assert.Equal("(block (if flag:Boolean (exit)) (exit))", Body("if flag then Exit; Exit()"));

    [Fact]
    [Trait("Req", "LANG-363")]
    public void Exit_WithAValue_OutsideAFunction_IsE2111() =>
        Assert.Equal(["E2111(1,90): Too many actual parameters for 'Exit'"], Diagnostics("Exit(1)"));

    [Fact]
    [Trait("Req", "LANG-364")]
    public void Halt_WithAndWithoutCode() =>
        Assert.Equal("(block (halt) (halt 3:Integer) (halt (conv Integer b:Byte)))", Body("Halt; Halt(3); Halt(b)"));

    [Theory]
    [Trait("Req", "LANG-364")]
    [InlineData("Halt(1, 2)", "E2111(1,93): Too many actual parameters for 'Halt'")]
    [InlineData("Halt(flag)", "E2010(1,90): Incompatible types: 'Boolean' and 'Integer'")]
    [InlineData("i := Halt", "E2034(1,90): Expression has no value (procedure call used as a value)")]
    public void Halt_Errors(string body, string expected) => Assert.Equal([expected], Diagnostics(body));

    [Fact]
    public void ControlIntrinsic_CanBeShadowedByAUserSymbol() =>
        Assert.Equal(["E2060(1,41): Statement expected, but expression of type 'Integer' found"],
            BindDiagnostics("var Break: Integer; begin while True do Break end."));

    [Fact]
    [Trait("Req", "LANG-363")]
    public void Unit_InitializationMayUseExit() =>
        Assert.Empty(BindDiagnostics("unit U; interface implementation initialization Exit; finalization Halt end."));
}
