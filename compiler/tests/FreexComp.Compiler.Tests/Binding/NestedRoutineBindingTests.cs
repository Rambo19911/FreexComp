using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Nested routines: scopes, the enclosing function's name, capture analysis (routines.md LANG-450…452, LANG-308, 402).</summary>
[Trait("Phase", "2")]
public sealed class NestedRoutineBindingTests
{
    private static BindingResult BindOk(string text)
    {
        var (result, _) = Bind(text);
        Assert.Empty(Format(result.Diagnostics));
        return result;
    }

    private static BoundRoutineBody Body(BindingResult result, string name) => result.Routines.Single(b => b.Routine.Name == name);

    private static string BodyOf(BindingResult result, string name) => BoundDump.Of(Body(result, name).Body);

    private static string[] Captured(BindingResult result, string name) =>
        [.. result.GetCapturedVariables(Body(result, name).Routine).Select(s => s.Name)];

    // ------------------------------------------------------------------ scopes (LANG-450, 402)

    [Fact]
    [Trait("Req", "LANG-450")]
    public void NestedRoutine_SeesOuterParametersLocalsAndResult_TwoLevels()
    {
        var result = BindOk("program T; function Outer(n: Integer): Integer; var total: Integer; " +
            "procedure Add(x: Integer); procedure Deep; begin total := total + n; Result := x end; begin Deep end; " +
            "begin total := 0; Add(2); Result := total end; begin end.");

        Assert.Equal("(block (:= total:Integer (Add:Integer total:Integer n:Integer)) (:= Result:Integer x:Integer))", BodyOf(result, "Deep"));
        var deep = Body(result, "Deep").Routine;
        Assert.True(deep.IsNested);
        Assert.Equal("T.Outer.Add.Deep", deep.LinkName);
        Assert.Equal(["Outer", "Add", "Deep"], result.Routines.Select(r => r.Routine.Name));
    }

    [Fact]
    [Trait("Req", "LANG-402")]
    public void NestedLocals_ShadowOuterNames()
    {
        var result = BindOk("program T; procedure Outer; var a: Integer; procedure Inner(a: Byte); var Outer: Char; begin Outer := 'x'; a := 1 end; " +
            "begin a := 2 end; begin end.");

        Assert.Equal("(block (:= Outer:Char 'x':Char) (:= a:Byte 1:Byte))", BodyOf(result, "Inner"));
    }

    [Fact]
    [Trait("Req", "LANG-402")]
    public void OuterLocal_DeclaredAfterTheNestedRoutine_IsNotVisibleThere() =>
        Assert.Equal(["E2003(1,52): Undeclared identifier 'x'"],
            BindDiagnostics("program T; procedure Outer; procedure Inner; begin x := 1 end; var x: Integer; begin x := 2 end; begin end."));

    [Fact]
    [Trait("Req", "LANG-402")]
    public void NestedRoutines_CallEachOtherInDeclarationOrderAndViaForward()
    {
        var result = BindOk("program T; procedure Outer; procedure B; forward; procedure A; begin B end; procedure B; begin A end; " +
            "begin A end; begin end.");

        Assert.Equal("(block (call B))", BodyOf(result, "A"));
        Assert.Equal("(block (call A))", BodyOf(result, "B"));
    }

    [Fact]
    [Trait("Req", "LANG-400")]
    public void NestedForward_WithoutDefinition_IsE2100() =>
        Assert.Equal(["E2100(1,39): Unsatisfied forward or external declaration: 'Inner'"],
            BindDiagnostics("program T; procedure Outer; procedure Inner; forward; begin end; begin end."));

    // ------------------------------------------------------------------ enclosing function name (LANG-308)

    [Fact]
    [Trait("Req", "LANG-308")]
    public void EnclosingFunctionName_OnTheLeft_IsItsResult()
    {
        var result = BindOk("program T; function Outer: Integer; procedure Inner; begin Outer := 5 end; begin Inner end; begin end.");

        var inner = Body(result, "Inner");
        var target = (BoundVariableExpression)((BoundAssignmentStatement)inner.Body.Statements[0]).Target;
        Assert.Same(Body(result, "Outer").Routine.ResultVariable, target.Variable);
        Assert.Equal("(block (:= Result:Integer 5:Integer))", BoundDump.Of(inner.Body));
    }

    [Fact]
    [Trait("Req", "LANG-308")]
    public void EnclosingFunctionName_TwoLevelsUp_AndInsideANestedFunction()
    {
        var result = BindOk("program T; function Outer: Integer; function Mid: Byte; procedure Deep; begin Outer := 1; Mid := 2 end; " +
            "begin Mid := 3; Outer := 4 end; begin Result := Mid end; begin end.");

        var deep = (BoundBlock)Body(result, "Deep").Body;
        Assert.Same(Body(result, "Outer").Routine.ResultVariable, ((BoundVariableExpression)((BoundAssignmentStatement)deep.Statements[0]).Target).Variable);
        Assert.Same(Body(result, "Mid").Routine.ResultVariable, ((BoundVariableExpression)((BoundAssignmentStatement)deep.Statements[1]).Target).Variable);
        Assert.Equal("(block (:= Result:Byte 3:Byte) (:= Result:Integer 4:Integer))", BodyOf(result, "Mid"));
    }

    [Fact]
    [Trait("Req", "LANG-308")]
    public void EnclosingFunctionName_InAnExpression_IsARecursiveCall() =>
        Assert.Equal("(block (:= Result:Integer (call Outer:Integer)))",
            BodyOf(BindOk("program T; function Outer: Integer; function Inner: Integer; begin Result := Outer end; begin Result := Inner end; begin end."), "Inner"));

    [Fact]
    [Trait("Req", "LANG-308")]
    public void EnclosingFunctionName_HiddenByANestedLocal_IsThatLocal() =>
        Assert.Equal("(block (:= Outer:Integer 1:Integer))",
            BodyOf(BindOk("program T; function Outer: Integer; procedure Inner; var Outer: Integer; begin Outer := 1 end; begin Result := 0 end; begin end."), "Inner"));

    [Fact]
    [Trait("Req", "LANG-410")]
    public void Result_InANestedProcedure_IsTheEnclosingFunctionsResult()
    {
        var result = BindOk("program T; function Outer: Integer; procedure Inner; begin Result := 7 end; begin Inner end; begin end.");

        var target = (BoundVariableExpression)((BoundAssignmentStatement)Body(result, "Inner").Body.Statements[0]).Target;
        Assert.Same(Body(result, "Outer").Routine.ResultVariable, target.Variable);
    }

    // ------------------------------------------------------------------ capture analysis (CMP-345)

    [Fact]
    [Trait("Req", "LANG-450")]
    public void CapturedVariables_AreTheOuterSymbolsUsedByNestedRoutines_InDeclarationOrder()
    {
        var result = BindOk("program T; function Outer(a, b: Integer; var c: Integer): Integer; var unused, x, y: Integer; " +
            "procedure Inner; var own: Integer; procedure Deep; begin own := a; y := 1 end; begin own := 0; x := own; Deep end; " +
            "begin unused := 0; Inner; Result := x + y + b + c end; begin end.");

        Assert.Equal(["a", "x", "y"], Captured(result, "Outer"));
        Assert.Equal(["own"], Captured(result, "Inner"));
        Assert.Empty(Captured(result, "Deep"));
        Assert.Equal(["x"], Body(result, "Inner").CapturedVariables.Select(s => s.Name));
        Assert.Equal(["own", "a", "y"], Body(result, "Deep").CapturedVariables.Select(s => s.Name));
    }

    [Fact]
    [Trait("Req", "LANG-450")]
    public void CapturedVariables_IncludeResult_AlsoViaTheFunctionName()
    {
        var result = BindOk("program T; function F: Integer; procedure P; begin F := 1 end; begin P end; " +
            "function G: Integer; procedure Q; begin Result := 2 end; begin Q end; begin end.");

        Assert.Equal(["Result"], Captured(result, "F"));
        Assert.Equal(["Result"], Captured(result, "G"));
    }

    [Fact]
    [Trait("Req", "LANG-450")]
    public void CapturedVariables_ExcludeGlobalsAndRoutinesWithoutNestedUse()
    {
        var result = BindOk("program T; var g: Integer; procedure Outer; var a: Integer; procedure Inner; begin g := 1; Inner end; " +
            "begin a := g; Inner end; begin end.");

        Assert.Empty(Captured(result, "Outer"));
        Assert.Empty(Body(result, "Inner").CapturedVariables);
    }

    [Fact]
    [Trait("Req", "LANG-903")]
    public void CapturedVariables_IgnoreNeverEvaluatedOperands()
    {
        var result = BindOk("program T; procedure Outer; var x: Integer; y: Byte; procedure Inner; var n: Integer; " +
            "begin n := SizeOf(x) + High(y) + Low(x * 2); n := SizeOf(y) + y end; begin x := 0; Inner end; begin end.");

        Assert.Equal(["y"], Captured(result, "Outer"));
        Assert.Equal(["y"], Body(result, "Inner").CapturedVariables.Select(s => s.Name));
    }

    // ------------------------------------------------------------------ read-only captures (LANG-452, 361)

    [Fact]
    [Trait("Req", "LANG-452")]
    public void OuterForVariable_IsReadOnlyInNestedRoutines()
    {
        Assert.Equal(["E2053(1,71): Left side cannot be assigned to"],
            BindDiagnostics("program T; procedure Outer; var i, s: Integer; procedure Inner; begin i := 0 end; " +
                "begin for i := 1 to 3 do Inner end; begin end."));
        Assert.Empty(BindDiagnostics("program T; procedure Outer; var i, s: Integer; procedure Inner; begin s := s + i end; " +
            "begin for i := 1 to 3 do Inner end; begin end."));
    }

    [Fact]
    [Trait("Req", "LANG-452")]
    public void OuterForVariable_CannotBePassedAsVarFromANestedRoutine() =>
        Assert.Equal(["E2053(1,116): Left side cannot be assigned to"],
            BindDiagnostics("program T; procedure Bump(var v: Integer); begin end; procedure Outer; var i: Integer; procedure Inner; begin Bump(i) end; " +
                "begin for i := 1 to 3 do Inner end; begin end."));

    [Fact]
    [Trait("Req", "LANG-452")]
    public void OuterConstParameter_IsReadOnlyInNestedRoutines() =>
        Assert.Equal(["E2053(1,70): Left side cannot be assigned to"],
            BindDiagnostics("program T; procedure Outer(const c: Integer); procedure Inner; begin c := 1 end; begin Inner end; begin end."));

    [Fact]
    [Trait("Req", "LANG-361")]
    public void OuterLocal_AsForVariableOfANestedRoutine_IsE2065() =>
        Assert.Equal(["E2065(1,72): For-loop control variable must be a simple local variable of ordinal type"],
            BindDiagnostics("program T; procedure Outer; var i: Integer; procedure Inner; begin for i := 1 to 2 do end; begin Inner end; begin end."));

    // ------------------------------------------------------------------ LANG-451

    [Fact]
    [Trait("Req", "LANG-451")]
    public void AddressOfANestedRoutine_IsE2130() =>
        Assert.Equal(["E2130(1,85): Local procedure/function 'Inner' cannot be assigned to a procedural variable"],
            BindDiagnostics("program T; procedure Outer; var p: Pointer; procedure Inner; begin end; begin p := @Inner end; begin end."));
}
