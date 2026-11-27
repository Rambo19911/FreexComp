using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-120 / C-121: method reference types, anonymous methods, closures and forwarders (LANG-720…725, CMP-380).</summary>
[Trait("Phase", "2")]
[Trait("Req", "LANG-720")]
public sealed class AnonymousMethodBindingTests
{
    private const string SystemSource = """
        unit System;
        interface
        type
          TObject = class constructor Create; end;
          IInterface = interface end;
          TInterfacedObject = class(TObject, IInterface) end;
        var Input, Output: Text;
        implementation
        constructor TObject.Create; begin end;
        end.
        """;

    private static BindingResult BindWithClosures(string text)
    {
        var types = new BuiltInTypes();
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", SystemSource)), types);
        Assert.Empty(system.Diagnostics);
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, text));
        Assert.Empty(tree.Diagnostics);
        return Binder.BindCompilationUnit(tree, types, system);
    }

    private static string[] Codes(BindingResult result) => [.. result.Diagnostics.Select(d => d.Code)];

    [Fact]
    public void ReferenceType_IsAnInterfaceWithInvoke()
    {
        var result = BindWithClosures("program P; type TF = reference to function(X: Integer): string; begin end.");

        Assert.Empty(Format(result.Diagnostics));
        var type = Assert.IsType<InterfaceTypeSymbol>(result.Scope.Lookup("TF"));
        Assert.True(type.IsMethodReference);
        Assert.Same(result.Types.IInterface, type.Parent);
        Assert.Equal("Invoke", type.Invoke.Name);
        Assert.Equal(0, type.Invoke.VmtSlot);                                     // the stub IInterface has no methods
        Assert.Same(result.Types.String, type.Invoke.ReturnType);
        Assert.True(type.IsManaged);
    }

    [Fact]
    [Trait("Req", "LANG-721")]
    public void CapturedParameter_BecomesAClosureField()
    {
        var result = BindWithClosures("""
            program P;
            type TF = reference to function(X: Integer): Integer;
            function MakeAdder(N: Integer): TF;
            var Unused: Integer;
            begin
              Unused := 0;
              Result := function(X: Integer): Integer begin Result := X + N end;
              if Unused = 0 then Exit;
            end;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var maker = (RoutineSymbol)result.Scope.Lookup("MakeAdder")!;
        var closure = Assert.IsType<ClosureInfo>(maker.Closure);
        Assert.Same(result.Types.TInterfacedObject, closure.Class.Parent);
        Assert.Equal(["N"], closure.Fields.Keys.Select(k => k.Name));
        var implemented = Assert.Single(closure.Class.Interfaces);
        Assert.True(implemented.Interface.IsAnonymousMethod);
        Assert.Contains(result.Routines, r => r.Routine is MethodSymbol { IsAnonymous: true });
        Assert.Contains(closure.Class, ClosureInfo.Of(result.Unit));
    }

    [Theory]
    [Trait("Req", "LANG-723")]
    [InlineData("type TP = procedure; var V: TP; begin V := procedure begin end end.", "E2010")]                   // not a plain procedural type
    [InlineData("type TF = reference to function: Integer; var V: TF; begin V := function: string begin Result := '' end end.", "E2010")]
    public void Incompatible_AnonymousMethods_AreE2010(string text, string code) =>
        Assert.Equal([code], Codes(BindWithClosures("program P; " + text)));

    [Fact]
    [Trait("Req", "LANG-725")]
    public void NestedRoutine_AsReference_IsE2130()
    {
        var result = BindWithClosures("""
            program P;
            type TR = reference to procedure;
            procedure Outer;
            var V: TR;
              procedure Inner; begin end;
            begin V := Inner; V end;
            begin end.
            """);

        Assert.Equal(["E2130"], Codes(result));
    }

    [Fact]
    [Trait("Req", "LANG-485")]
    public void GlobalRoutine_AsReference_UsesAForwarder()
    {
        var result = BindWithClosures("""
            program P;
            type TF = reference to function(X: Integer): Integer;
            function Twice(X: Integer): Integer; begin Result := X * 2 end;
            var F: TF;
            begin F := Twice; F(1) end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        Assert.Single(result.Unit.Forwarders);
        Assert.Contains(result.Routines, r => r.Routine.Name == "#forward1");
    }

    [Fact]
    [Trait("Req", "LANG-721")]
    public void CapturedForVariable_IsReadOnly()
    {
        var result = BindWithClosures("""
            program P;
            type TR = reference to procedure;
            procedure Run;
            var I: Integer; V: TR;
            begin
              for I := 1 to 2 do V := procedure begin I := 5 end;
              V;
            end;
            begin end.
            """);

        Assert.Equal(["E2053"], Codes(result));
    }

    /// <summary>LANG-422 (C-120, for Generics.Collections): an interface parameter may default to nil.</summary>
    [Fact]
    [Trait("Req", "LANG-422")]
    public void InterfaceParameter_DefaultsToNil() =>
        Assert.Empty(Format(BindWithClosures("program P; procedure Q(const I: IInterface = nil); begin end; begin Q end.").Diagnostics));

    [Fact]
    [Trait("Req", "LANG-721")]
    public void CapturedResult_BecomesAClosureField()
    {
        // C-230: was E2900 (CapturedResult_IsUnsupported) until Result could be captured
        var result = BindWithClosures("""
            program P;
            type TR = reference to procedure;
            function F: Integer;
            var V: TR;
            begin
              V := procedure begin Result := 1 end;
              V;
            end;
            begin end.
            """);

        Assert.Empty(Codes(result));
        var f = (RoutineSymbol)result.Scope.Lookup("F")!;
        Assert.Equal(["Result"], f.Closure!.Fields.Keys.Select(k => k.Name));
    }

    [Fact]
    [Trait("Req", "LANG-725")]
    public void CaptureThroughNestedRoutine_MovesToTheOwnersClosure()
    {
        // C-230: an anonymous method inside a nested routine using the outer routine's variable (was E2900)
        var result = BindWithClosures("""
            program P;
            type TF = reference to function: Integer;
            function Outer: TF;
            var Base: Integer;
              function Make(Step: Integer): TF;
              begin
                Result := function: Integer begin Result := Base + Step end;
              end;
            begin
              Base := 1;
              Result := Make(2);
            end;
            begin end.
            """);

        Assert.Empty(Codes(result));
        var outer = (RoutineSymbol)result.Scope.Lookup("Outer")!;
        var make = result.Routines.Single(r => r.Routine.Name == "Make");
        Assert.Equal(["Base"], outer.Closure!.Fields.Keys.Select(k => k.Name));
        Assert.Empty(outer.Closure.Class.Interfaces);
        Assert.Equal(["Step"], make.Routine.Closure!.Fields.Keys.Select(k => k.Name));
        var link = Assert.Single(make.Routine.Closure.Links.Keys);                        // C-308: keyed by the target frame
        Assert.Same(outer, link.Owner);
        Assert.Same(outer.Closure, link);
        Assert.Contains(make.CapturedVariables, s => s.Name == "Base");       // the frames carry Outer's closure to Make
    }

    [Fact]
    [Trait("Req", "LANG-721")]
    public void AnonymousMethods_InConstructorAndDestructor_AreAccepted()
    {
        // C-230: was E2900
        var result = BindWithClosures("""
            program P;
            type
              TR = reference to procedure;
              TC = class
                F: Integer;
                constructor Create;
                destructor Done;
              end;
            constructor TC.Create;
            var V: TR;
            begin
              V := procedure begin F := 1 end;
              V;
            end;
            destructor TC.Done;
            var V: TR;
            begin
              V := procedure begin F := 2 end;
              V;
            end;
            begin end.
            """);

        Assert.Empty(Codes(result));
    }

    /// <summary>The declarations every capture case starts with (C-413).</summary>
    private const string CapturePrefix =
        "program P; type TProc = reference to procedure; procedure Use(I: Integer); begin end; procedure UseP(P: Pointer); begin end; ";

    /// <summary>
    /// LANG-970 (C-413): what an anonymous method cannot capture — E2720 with the name, once per variable and outermost
    /// anonymous-method expression, at the first evaluated reference (each <c>^</c> marks an expected position). Rows 1–10 are the
    /// rows of the golden <c>m8/ErrAnonCapture</c>; the others are the remaining paths of the rule.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-970")]
    // rows 1–5: var, out, open-array (value and const) and untyped parameters
    [InlineData("procedure P1(var X: Integer); var F: TProc; begin F := procedure begin ^X := 1 end end;", "X")]
    [InlineData("procedure P2(out X: Integer); var F: TProc; begin X := 0; F := procedure begin ^X := 2 end end;", "X")]
    [InlineData("procedure P3(A: array of Integer); var F: TProc; begin F := procedure begin Use(^A[0]) end end;", "A")]
    [InlineData("procedure P4(const A: array of string); var F: TProc; begin F := procedure begin Use(Length(^A)) end end;", "A")]
    [InlineData("procedure P5(const X); var F: TProc; begin F := procedure begin UseP(@^X) end end;", "X")]
    // row 6: named twice in one anonymous method
    [InlineData("procedure P6(var X: Integer); var F: TProc; begin F := procedure begin ^X := X + 1 end end;", "X")]
    // row 7: named only by an anonymous method nested in the outer one
    [InlineData("procedure P7(var X: Integer); var F: TProc; begin F := procedure var G: TProc; begin G := procedure begin ^X := 7 end end end;", "X")]
    // row 8: two anonymous-method expressions
    [InlineData("procedure P8(var X: Integer); var F, G: TProc; begin F := procedure begin ^X := 8 end; G := procedure begin ^X := 9 end end;", "X")]
    // rows 9, 10: Self of a record method, implied by a field and written
    [InlineData("type TRec = record Value: Integer; procedure M; end; procedure TRec.M; var F: TProc; begin F := procedure begin Use(^Value) end end;", "Self")]
    [InlineData("type TRec = record Value: Integer; procedure N; end; procedure TRec.N; var F: TProc; begin F := procedure begin Use(^Self.Value) end end;", "Self")]
    // Self implied by a property and by an instance method, then written: one variable, one diagnostic
    [InlineData("type TRec = record Value: Integer; function Get: Integer; property V: Integer read Value; procedure M; end; function TRec.Get: Integer; begin Result := Value end; "
        + "procedure TRec.M; var F: TProc; begin F := procedure begin Use(^V); Use(Get); Use(Self.Value) end end;", "Self")]
    // Self of a helper for a type that is not a class
    [InlineData("type TH = record helper for Integer procedure M; end; procedure TH.M; var F: TProc; begin F := procedure begin Use(^Self) end end;", "Self")]
    // through a nested routine declared inside the anonymous method, and again in the anonymous method itself
    [InlineData("procedure P(var X: Integer); var F: TProc; begin F := procedure procedure Inner; begin ^X := 1; X := 2 end; begin Inner; X := 3 end end;", "X")]
    // an anonymous method inside a nested routine names the var parameter of the routine enclosing it (C-230 path)
    [InlineData("procedure P(var X: Integer); var F: TProc; procedure Inner; begin F := procedure begin ^X := 1 end end; begin Inner end;", "X")]
    // an operand that is not evaluated captures nothing, the evaluated reference after it does
    [InlineData("procedure P(var X: Integer); var F: TProc; begin F := procedure begin Use(SizeOf(X)); Use(High(X)); ^X := 1 end end;", "X")]
    // High of an open array is read at run time, Low is a constant
    [InlineData("procedure P(const A: array of Integer); var F: TProc; begin F := procedure begin Use(Low(A)); Use(High(^A)) end end;", "A")]
    // two variables in one anonymous method: one diagnostic each
    [InlineData("procedure P(var X: Integer; out Y: Integer); var F: TProc; begin Y := 0; F := procedure begin ^X := 1; ^Y := X; X := Y end end;", "X,Y")]
    public void Capture_Rejected(string declarations, string names)
    {
        var marked = CapturePrefix + declarations + " begin end.";
        var expected = new List<string>();
        var name = names.Split(',');
        for (var at = marked.IndexOf('^'); at >= 0; at = marked.IndexOf('^'))
        {
            marked = marked.Remove(at, 1);
            expected.Add($"E2720(1,{at + 1}): Parameter '{name[Math.Min(expected.Count, name.Length - 1)]}' cannot be captured by an anonymous method");
        }

        Assert.Equal(expected, Format(BindWithClosures(marked).Diagnostics));
    }

    /// <summary>
    /// LANG-970 (C-413): what can be captured — no diagnostic, and the frame of the owner <c>Q</c> gets exactly the fields
    /// <paramref name="fields"/> (the copies of value and <c>const</c> parameters, <c>Self</c> of a class). An operand that is never
    /// evaluated and a class var of a record need no field.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-970")]
    [InlineData("procedure Q(V: Integer); var F: TProc; begin F := procedure begin Use(V) end end;", "V")]
    [InlineData("procedure Q(const S: string); var F: TProc; begin F := procedure begin Use(Length(S)) end end;", "S")]
    [InlineData("type TRec = record Value: Integer; end; procedure Q(const R: TRec); var F: TProc; begin F := procedure begin Use(R.Value) end end;", "R")]
    [InlineData("procedure Q(var X: Integer); var F: TProc; begin F := procedure begin Use(SizeOf(X)) end end;", "")]
    [InlineData("procedure Q(var X: Integer; const A: array of Integer); var F: TProc; begin F := procedure begin Use(High(X)); Use(Low(X)); Use(Low(A)) end end;", "")]
    [InlineData("type TObj = class FValue: Integer; procedure Q; end; procedure TObj.Q; var F: TProc; begin F := procedure begin FValue := FValue + 1; Use(Self.FValue) end end;", "Self")]
    [InlineData("type TRec = record class var Count: Integer; procedure Q; end; procedure TRec.Q; var F: TProc; begin F := procedure begin Count := Count + 1 end end;", "")]
    // a record constructor's Self is the constructed result (LANG-730): captured as Result is
    [InlineData("type TRec = record Value: Integer; constructor Q(V: Integer); end; constructor TRec.Q(V: Integer); var F: TProc; begin F := procedure begin Value := V end end;", "Result,V")]
    // a var parameter of a nested routine declared inside the anonymous method belongs to no enclosing body
    [InlineData("procedure Q; var F: TProc; begin F := procedure var Z: Integer; procedure Inner(var Y: Integer); begin Y := 1 end; begin Inner(Z) end end;", "")]
    public void Capture_Allowed(string declarations, string fields)
    {
        var result = BindWithClosures(CapturePrefix + declarations + " begin end.");

        Assert.Empty(Format(result.Diagnostics));
        var closure = Assert.IsType<ClosureInfo>(result.Routines.Single(r => r.Routine.Name == "Q").Routine.Closure);
        Assert.Equal(fields, string.Join(",", closure.Fields.Keys.Select(k => k.Name).Order(StringComparer.Ordinal)));
    }
}
