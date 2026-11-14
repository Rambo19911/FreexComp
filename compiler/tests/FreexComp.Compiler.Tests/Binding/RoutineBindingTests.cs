using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Routine declarations, parameters, calls, <c>Result</c>, <c>forward</c>, recursion (routines.md LANG-400…421, 441).</summary>
[Trait("Phase", "2")]
public sealed class RoutineBindingTests
{
    private static BindingResult BindOk(string text)
    {
        var (result, _) = Bind(text);
        Assert.Empty(Format(result.Diagnostics));
        return result;
    }

    private static RoutineSymbol Routine(BindingResult result, string name) =>
        result.Unit.ImplementationMembers.Concat(result.Unit.InterfaceMembers).OfType<RoutineSymbol>().Single(r => r.Name == name);

    private static string BodyOf(BindingResult result, string name) =>
        BoundDump.Of(result.Routines.Single(b => b.Routine.Name == name).Body);

    private static string MainBody(BindingResult result) => BoundDump.Of(Assert.Single(result.Bodies));

    /// <summary>Diagnostics of <c>program T; {text}</c>, "CODE(line,col): message".</summary>
    private static string[] Errors(string text) => BindDiagnostics("program T; " + text);

    // ------------------------------------------------------------------ declarations, parameters (LANG-400, §3)

    [Fact]
    [Trait("Req", "LANG-400")]
    public void Procedure_DeclaresARoutineSymbolWithParameters()
    {
        var result = BindOk("program T; procedure P(a: Integer; const b: Byte; var c, d: Char; out e: Boolean); begin end; begin end.");
        var p = Routine(result, "P");

        Assert.Equal(SymbolKind.Routine, p.Kind);
        Assert.Null(p.ReturnType);
        Assert.Equal(
            [("a", ParameterMode.Value, "Integer", 0), ("b", ParameterMode.Const, "Byte", 1), ("c", ParameterMode.Var, "Char", 2),
             ("d", ParameterMode.Var, "Char", 3), ("e", ParameterMode.Out, "Boolean", 4)],
            p.Parameters.Select(x => (x.Name, x.Mode, x.Type.Name, x.Ordinal)));
        Assert.All(p.Parameters, x => Assert.Same(p, x.ContainingSymbol));
        Assert.Equal("procedure T.P(a: Integer; const b: Byte; var c: Char; var d: Char; out e: Boolean)", p.ToDisplayString());
        Assert.Equal("param var c: Char", p.Parameters[2].ToDisplayString());
        Assert.False(p.IsNested);
    }

    /// <summary>
    /// C-202 / routines.md §3: untyped <c>var</c>/<c>const</c>/<c>out</c> parameters take a variable of any type; inside the
    /// callee they are used through <c>@a</c>, a variable typecast <c>T(a)</c> (any size, managed included) or as untyped arguments.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-420")]
    [Trait("Req", "LANG-421")]
    [InlineData("procedure P(var A; const B; out C); begin end; var X: Integer; S: string; R: record F: Byte end; Q: ^Integer; " +
        "const K: Integer = 1; begin P(X, S, R); P(S, K, Q^); P(R.F, X, X) end.", null)]
    [InlineData("procedure P(var A); var I: Integer; begin I := Integer(A); Integer(A) := I + 1; Move(A, I, 4); P(A) end; begin end.", null)]
    [InlineData("procedure P(const A); var Q: Pointer; S: string; begin Q := @A; S := string(A); P(A); FillChar(Q^, 1, 0) end; begin end.", null)]
    [InlineData("procedure P(out A); begin Pointer(A) := nil end; begin end.", null)]
    [InlineData("procedure P(var A); begin end; begin P(1) end.", "E2112(1,51): Constant object cannot be passed as var parameter")]
    [InlineData("procedure P(const A); begin end; var X: Integer; begin P(X + 1) end.", "E2033(1,69): Cannot take the address of this expression")]
    [InlineData("procedure P(var A); var I: Integer; begin I := A end; begin end.", "E2010(1,59): Incompatible types: 'untyped' and 'Integer'")]
    [InlineData("procedure P(const A); begin Integer(A) := 1 end; begin end.", "E2053(1,40): Left side cannot be assigned to")]
    [InlineData("procedure P(const A); procedure Q(var B); begin end; begin Q(A) end; begin end.", "E2112(1,73): Constant object cannot be passed as var parameter")]
    [InlineData("procedure P(A); begin end; begin end.", "E2118(1,24): Untyped parameter 'A' must be declared var, const or out")]
    public void UntypedParameters(string text, string? expected)
    {
        Assert.Equal(expected is null ? [] : [expected], Errors(text));
    }

    [Fact]
    [Trait("Req", "LANG-410")]
    public void Function_HasAResultVariable()
    {
        var result = BindOk("program T; function F(n: Integer): Int64; begin Result := n * 2 end; begin end.");

        Assert.Equal("function T.F(n: Integer): Int64", Routine(result, "F").ToDisplayString());
        Assert.Equal("(block (:= Result:Int64 (conv Int64 (Multiply:Integer n:Integer 2:Integer))))", BodyOf(result, "F"));
    }

    [Fact]
    [Trait("Req", "LANG-308")]
    public void FunctionName_OnTheLeft_IsTheResult() =>
        Assert.Equal("(block (:= Result:Integer 1:Integer))", BodyOf(BindOk("program T; function F: Integer; begin F := 1 end; begin end."), "F"));

    [Fact]
    [Trait("Req", "LANG-403")]
    public void DirectRecursion()
    {
        var result = BindOk("program T; function Fact(n: Integer): Int64; begin if n <= 1 then Result := 1 else Result := n * Fact(n - 1) end; begin end.");

        Assert.Equal(
            "(block (if (LessOrEqual:Boolean n:Integer 1:Integer) (:= Result:Int64 1:Int64) " +
            "(:= Result:Int64 (Multiply:Int64 (conv Int64 n:Integer) (call Fact:Int64 (Subtract:Integer n:Integer 1:Integer))))))",
            BodyOf(result, "Fact"));
    }

    [Fact]
    [Trait("Req", "LANG-312")]
    public void ParameterlessFunction_IsCalledWithoutParentheses() =>
        Assert.Equal(
            "(block (:= i:Integer (call F:Integer)) (:= i:Integer (call F:Integer)) (call F))",
            MainBody(BindOk("program T; function F: Integer; begin Result := 1 end; var i: Integer; begin i := F; i := F(); F end.")));

    [Fact]
    [Trait("Req", "LANG-420")]
    public void Call_ConvertsValueArguments_AndPassesVariablesByReference() =>
        Assert.Equal(
            "(block (call P (conv Integer b:Byte) 7:Byte b:Byte i:Integer))",
            MainBody(BindOk("program T; procedure P(a: Integer; const k: Byte; var v: Byte; out o: Integer); begin end; " +
                "var b: Byte; i: Integer; begin P(b, 7, b, i) end.")));

    [Fact]
    [Trait("Req", "LANG-420")]
    public void Parameters_AreVisibleInTheBody_AndVarParametersAreAssignable() =>
        Assert.Equal(
            "(block (:= v:Integer a:Integer) (:= o:Integer (Add:Integer v:Integer 1:Integer)) (call Q v:Integer))",
            BodyOf(BindOk("program T; procedure Q(var x: Integer); begin end; " +
                "procedure P(a: Integer; var v: Integer; out o: Integer); begin v := a; o := v + 1; Q(v) end; begin end."), "P"));

    [Fact]
    [Trait("Req", "LANG-307")]
    public void FunctionCallAsStatement_DiscardsTheResult() =>
        Assert.Equal("(block (call F 1:Integer))", MainBody(BindOk("program T; function F(x: Integer): Integer; begin Result := x end; begin F(1) end.")));

    [Theory]
    [InlineData("procedure P(a); begin end; begin end.", "E2118(1,24): Untyped parameter 'a' must be declared var, const or out")]
    [InlineData("procedure P(a, A: Integer); begin end; begin end.", "E2004(1,27): Identifier redeclared: 'A'")]
    [InlineData("function F(Result: Integer): Integer; begin end; begin end.", "E2004(1,23): Identifier redeclared: 'Result'")]
    [InlineData("function F; begin end; begin end.", "E2102(1,21): Function needs a result type")]
    [InlineData("procedure P(var a); begin a := 1 end; begin end.", "E2010(1,43): Incompatible types: 'Integer' and 'untyped'")]   // C-202 (was E2900 until untyped parameters)
    [InlineData("procedure P(a, b: Integer = 1); begin end; begin end.", "E2115(1,40): Default value not allowed for a parameter group")]   // C-105
    [InlineData("procedure P; virtual; begin end; begin end.", "E1114(1,25): Directive 'virtual' is not allowed here")]
    [InlineData("procedure P(x: Integer); begin end; procedure Q; begin P end; begin end.",
        "E2110(1,67): Not enough actual parameters for 'P'")]
    [InlineData("procedure P; begin end; var i: Integer; begin i := P end.", "E2034(1,63): Expression has no value (procedure call used as a value)")]
    public void DeclarationAndCallErrors(string text, string expected) => Assert.Equal([expected], Errors(text));

    // ------------------------------------------------------------------ LANG-402: locals

    [Fact]
    [Trait("Req", "LANG-402")]
    public void Locals_MayShadowOuterNames_ButNotParametersOrResult()
    {
        Assert.Equal(
            ["E2004(1,65): Identifier redeclared: 'a'", "E2004(1,74): Identifier redeclared: 'Result'"],
            Errors("var x: Integer; function F(a: Integer): Integer; var a: Byte; Result: Byte; x: Char; begin end; begin end."));
    }

    [Fact]
    [Trait("Req", "LANG-402")]
    public void Locals_AreLocalVariablesOfTheRoutine()
    {
        var result = BindOk("program T; var x: Integer; procedure P; var x: Byte; i: Integer; begin for i := 1 to 2 do x := i end; begin end.");

        Assert.Equal("(block (for i:Integer to 1:Integer 2:Integer (:= x:Byte (conv Byte i:Integer))))", BodyOf(result, "P"));
        var local = result.Routines.Single().Scope.LookupLocal("x");
        Assert.Equal((SymbolKind.Local, (Symbol)Routine(result, "P")), (local!.Kind, local.ContainingSymbol!));
    }

    [Fact]
    [Trait("Req", "LANG-361")]
    public void For_InARoutine_NeedsALocalOfThatRoutine() =>
        Assert.Equal(["E2065(1,51): For-loop control variable must be a simple local variable of ordinal type"],
            Errors("var g: Integer; procedure P; begin for g := 1 to 2 do ; end; begin end."));

    [Fact]
    [Trait("Req", "CMP-507")]
    public void Undeclared_IsReportedOncePerNamePerRoutine() =>
        Assert.Equal(
            ["E2003(1,31): Undeclared identifier 'X'", "E2003(1,60): Undeclared identifier 'X'", "E2003(1,73): Undeclared identifier 'X'"],
            Errors("procedure A; begin X; x end; procedure B; begin X end; begin X; X end."));

    // ------------------------------------------------------------------ Result, Exit (LANG-410, 413)

    [Fact]
    [Trait("Req", "LANG-413")]
    public void ExitWithAValue_InAFunction() =>
        Assert.Equal("(block (exit 1:Int64))",
            BodyOf(BindOk("program T; function F: Int64; begin Exit(1) end; begin end."), "F"));

    [Theory]
    [InlineData("procedure P; begin Exit(1) end; begin end.", "E2111(1,36): Too many actual parameters for 'Exit'")]
    [InlineData("procedure P; begin Result := 1 end; begin end.", "E2132(1,31): 'Result' is not available in a procedure")]
    [InlineData("function F: Byte; begin Exit(300) end; begin end.", "E2012(1,41): Constant value 300 is out of range for type 'Byte'")]
    public void ResultAndExitErrors(string text, string expected) => Assert.Equal([expected], Errors(text));

    // ------------------------------------------------------------------ forward, interface, definitions (LANG-400, 401, 403)

    [Fact]
    [Trait("Req", "LANG-403")]
    public void Forward_AllowsMutualRecursion_AndTheDefinitionCompletesTheSameSymbol()
    {
        var result = BindOk(
            "program T;\nfunction IsOdd(n: Integer): Boolean; forward;\n" +
            "function IsEven(n: Integer): Boolean; begin if n = 0 then Result := True else Result := IsOdd(n - 1) end;\n" +
            "function IsOdd; begin if n = 0 then Result := False else Result := IsEven(n - 1) end;\nbegin end.");

        var isOdd = Routine(result, "IsOdd");
        Assert.Equal([2, 4], isOdd.Locations.Select(l => l.LineSpan.Start.Line + 1));
        Assert.Single(result.Unit.ImplementationMembers.OfType<RoutineSymbol>(), r => r.Name == "IsOdd");
        Assert.Equal(
            "(block (if (Equal:Boolean n:Integer 0:Integer) (:= Result:Boolean False:Boolean) (:= Result:Boolean (call IsEven:Boolean (Subtract:Integer n:Integer 1:Integer)))))",
            BodyOf(result, "IsOdd"));
    }

    [Fact]
    [Trait("Req", "LANG-401")]
    public void Definition_MayRepeatTheMatchingHeading() =>
        BindOk("program T; procedure P(var a: Integer; b: Byte); forward; procedure P(var a: Integer; b: Byte); begin a := b end; begin end.");

    [Fact]
    [Trait("Req", "LANG-400")]
    public void UnitInterfaceRoutine_IsDefinedInTheImplementation()
    {
        var result = BindOk("unit U; interface function Twice(x: Integer): Integer; implementation function Twice(x: Integer): Integer; " +
            "begin Result := x * 2 end; initialization Twice(1) end.");

        Assert.Equal("U.Twice", Routine(result, "Twice").LinkName);
        Assert.Empty(result.Unit.ImplementationMembers.OfType<RoutineSymbol>());
        Assert.Equal("(block (:= Result:Integer (Multiply:Integer x:Integer 2:Integer)))", BodyOf(result, "Twice"));
    }

    [Theory]
    [Trait("Req", "LANG-401")]
    [InlineData("procedure P(a: Integer); forward; procedure P(b: Integer); begin end; begin end.", "E2101(1,56): Declaration of 'P' differs from its previous declaration")]
    [InlineData("procedure P(a: Integer); forward; procedure P(var a: Integer); begin end; begin end.", "E2101(1,56): Declaration of 'P' differs from its previous declaration")]
    [InlineData("procedure P(a: Integer); forward; procedure P(a: Byte); begin end; begin end.", "E2101(1,56): Declaration of 'P' differs from its previous declaration")]
    [InlineData("procedure P(a: Integer); forward; procedure P; begin end; procedure P(a, b: Integer); begin end; begin end.",
        "E2120(1,80): Previous declaration of 'P' was not marked with the 'overload' directive")]
    [InlineData("function F: Integer; forward; function F: Byte; begin end; begin end.", "E2101(1,51): Declaration of 'F' differs from its previous declaration")]
    [InlineData("procedure P; forward; begin end.", "E2100(1,22): Unsatisfied forward or external declaration: 'P'")]
    public void DefinitionErrors(string text, string expected) => Assert.Equal([expected], Errors(text));

    [Fact]
    [Trait("Req", "LANG-400")]
    public void UnitInterfaceRoutine_WithoutDefinition_IsE2100() =>
        Assert.Equal(["E2100(1,29): Unsatisfied forward or external declaration: 'P'"],
            BindDiagnostics("unit U; interface procedure P; implementation end."));

    // ------------------------------------------------------------------ var/out arguments (LANG-421)

    [Theory]
    [Trait("Req", "LANG-421")]
    [InlineData("var i: Integer; begin V(1) end.", "E2112(1,112): Constant object cannot be passed as var parameter")]
    [InlineData("const K = 1; begin V(K) end.", "E2112(1,109): Constant object cannot be passed as var parameter")]
    [InlineData("var i: Integer; begin V(i + 1) end.", "E2113(1,112): Variable required")]
    [InlineData("var b: Byte; begin V(b) end.", "E2010(1,109): Incompatible types: 'Byte' and 'Integer'")]
    [InlineData("var i: Integer; begin for i := 1 to 2 do V(i) end.", "E2053(1,131): Left side cannot be assigned to")]
    [InlineData("procedure C(const c: Integer); begin V(c) end; begin end.", "E2112(1,127): Constant object cannot be passed as var parameter")]
    [InlineData("procedure C(const c: Integer); begin c := 1 end; begin end.", "E2053(1,125): Left side cannot be assigned to")]
    [InlineData("var b: Byte; begin V(b, b) end.", "E2111(1,112): Too many actual parameters for 'V'")]
    [InlineData("var b: Boolean; begin W(b) end.", "E2010(1,112): Incompatible types: 'Boolean' and 'Integer'")]
    public void ArgumentErrors(string text, string expected) =>
        Assert.Equal([expected], Errors("procedure V(var x: Integer); begin end; procedure W(x: Integer); begin end; " + text));

    // ------------------------------------------------------------------ overload sets (LANG-440, 441)

    [Fact]
    [Trait("Req", "LANG-441")]
    public void OverloadSet_CallPicksTheOnlyApplicableCandidate()
    {
        var result = BindOk("program T; procedure P(x: Integer); overload; begin end; procedure P(c: Char); overload; begin end; " +
            "begin P(1); P('a') end.");

        Assert.Equal("(block (call P 1:Integer) (call P 'a':Char))", MainBody(result));
        var calls = result.Bodies[0].Statements.Cast<BoundCallStatement>().Select(c => c.Routine.Parameters[0].Type.Name);
        Assert.Equal(["Integer", "Char"], calls);
        Assert.Equal(["T.P$Integer", "T.P$Char"], result.Unit.ImplementationMembers.OfType<RoutineSymbol>().Select(r => r.LinkName));
    }

    [Fact]
    [Trait("Req", "LANG-441")]
    public void OverloadSet_ContinuesOutward_WhileAllCandidatesAreOverloaded()
    {
        var result = BindOk("program T; procedure P(x: Integer); overload; begin end; " +
            "procedure Q; procedure P(c: Char); overload; begin end; begin P(1); P('a') end; begin end.");

        var calls = result.Routines.Single(r => r.Routine.Name == "Q").Body.Statements.Cast<BoundCallStatement>();
        Assert.Equal(["T.P$Integer", "T.Q.P$Char"], calls.Select(c => c.Routine.LinkName));
    }

    [Theory]
    [Trait("Req", "LANG-440")]
    [InlineData("procedure P; begin end; procedure P(x: Integer); begin end; begin end.", "E2120(1,46): Previous declaration of 'P' was not marked with the 'overload' directive")]
    [InlineData("procedure P(x: Integer); overload; begin end; procedure P(y: Integer); overload; begin end; begin end.", "E2121(1,68): Ambiguous overload declaration of 'P'")]
    [InlineData("var P: Integer; procedure P; begin end; begin end.", "E2004(1,38): Identifier redeclared: 'P'")]
    [InlineData("procedure P(x: Integer); overload; begin end; procedure P(c: Char); overload; begin end; begin P(True) end.",
        "E2123(1,107): There is no overloaded version of 'P' that can be called with these arguments")]
    public void OverloadErrors(string text, string expected) => Assert.Equal([expected], Errors(text));

    // ------------------------------------------------------------------ external, link names (LANG-463, 470; CMP-360)

    [Fact]
    [Trait("Req", "LANG-463")]
    public void External_HasNoBody_AndLinksToTheImportName()
    {
        var result = BindOk("program T; procedure Sleep(ms: Cardinal); stdcall; external 'kernel32.dll'; " +
            "function Tick: Cardinal; external 'kernel32' name 'GetTickCount'; begin Sleep(Tick) end.");

        var sleep = Routine(result, "Sleep");
        Assert.Equal((true, "Sleep", "kernel32.dll"), (sleep.IsExternal, sleep.LinkName, sleep.LibraryName));
        Assert.Equal(("GetTickCount", "kernel32"), (Routine(result, "Tick").LinkName, Routine(result, "Tick").LibraryName));
        Assert.Empty(result.Routines);
    }

    [Fact]
    [Trait("Req", "LANG-470")]
    public void LinkNames()
    {
        var result = BindOk("program T; procedure P; public name 'p_sym'; begin end; procedure Outer; procedure Inner; begin end; begin Inner end; " +
            "procedure Put(const s: Integer; var b: Byte); overload; begin end; procedure Put; overload; begin end; begin end.");

        Assert.Equal("p_sym", Routine(result, "P").LinkName);
        Assert.Equal(["T.Put$c:Integer,v:Byte", "T.Put$"], result.Unit.ImplementationMembers.OfType<RoutineSymbol>().Where(r => r.Name == "Put").Select(r => r.LinkName));
        var inner = (RoutineSymbol)result.Routines.Single(r => r.Routine.Name == "Outer").Scope.LookupLocal("Inner")!;
        Assert.Equal(("T.Outer.Inner", true), (inner.LinkName, inner.IsNested));
        Assert.Contains(result.Routines, r => ReferenceEquals(r.Routine, inner));
    }

    // ------------------------------------------------------------------ lazy body binding (CMP-114)

    [Fact]
    [Trait("Req", "CMP-114")]
    public void RoutineBody_CanBeBoundOnItsOwn()
    {
        var (result, _) = Bind("program T; procedure A; begin X end; procedure B; var i: Integer; begin i := 1 end; begin end.");

        var body = Binder.BindRoutineBody(result, Routine(result, "B"));
        Assert.Empty(body.Diagnostics);
        Assert.Equal("(block (:= i:Integer 1:Integer))", BoundDump.Of(body.Body));
        Assert.Equal(["E2003(1,31): Undeclared identifier 'X'"], Format(Binder.BindRoutineBody(result, Routine(result, "A")).Diagnostics));
    }
}
