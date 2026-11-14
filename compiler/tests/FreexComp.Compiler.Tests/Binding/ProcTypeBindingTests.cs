using FreexComp.Compiler.Types;
using Xunit;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Plain procedural types, M2.2 subset (C-161; LANG-480, 482…484, LANG-462).</summary>
[Trait("Phase", "2")]
public sealed class ProcTypeBindingTests
{
    private const string Header = "type TF = function(X: Integer): Integer; TP = procedure; ";

    [Fact]
    [Trait("Req", "LANG-480")]
    public void ProceduralType_IsAPointerSizedTypeWithItsSignature()
    {
        var (result, _) = Bind("type TF = function(const A: string; var B: Integer): Boolean; stdcall; begin end.");
        Assert.Empty(result.Diagnostics);
        var type = Assert.IsType<ProceduralTypeSymbol>(result.Declarations.Scope.LookupLocal("TF"));
        Assert.Equal(TypeKind.Procedural, type.TypeKind);
        Assert.Equal(8, type.Size);
        Assert.Equal(8, type.Alignment);
        Assert.Equal(["A", "B"], type.Signature.Parameters.Select(p => p.Name));
        Assert.Equal("Boolean", type.Signature.ReturnType!.Name);
    }

    [Theory]
    [Trait("Req", "LANG-480")]
    [Trait("Req", "LANG-482")]
    [Trait("Req", "LANG-462")]
    [InlineData("function Twice(X: Integer): Integer; begin Result := X end; var F: TF; begin F := Twice; F := @Twice; F := nil; end.")]
    [InlineData("function Twice(X: Integer): Integer; begin Result := X end; var F, G: TF; N: Integer; begin F := Twice; G := F; N := F(1) + G(2); end.")]
    [InlineData("var F: TF; B: Boolean; begin F := nil; B := (F = nil) or Assigned(F) or (F <> F); end.")]
    [InlineData("procedure Q; begin end; var P: TP; begin P := Q; P; P(); end.")]
    [InlineData("type TS = procedure; stdcall; procedure Q; begin end; var P: TP; S: TS; begin S := Q; P := S; end.")]
    [InlineData("type TG = function: Integer; function K: Integer; begin Result := 1 end; var G: TG; N: Integer; begin G := K; N := G + G(); end.")]
    [InlineData("type TR = record F: TF; end; var R: TR; A: array[0..1] of TF; N: Integer; begin N := R.F(1) + A[0](2); end.")]
    [InlineData("function Apply(F: TF): Integer; begin Result := F(3) end; function Twice(X: Integer): Integer; begin Result := X end; var N: Integer; begin N := Apply(Twice) + Apply(@Twice); end.")]
    [InlineData("var F: TF; P: Pointer; N: NativeInt; begin P := Pointer(F); F := TF(P); N := NativeInt(F); end.")]
    public void Subset_BindsWithoutDiagnostics(string body) => Assert.Empty(BindDiagnostics(Header + body));

    /// <summary>
    /// C-102 / LANG-480, 483: <c>class static</c> methods (unqualified inside the class, qualified, with <c>@</c>) and overloaded
    /// routines (the overload compatible with the target type; E2131 when none) as procedural values; instance methods are E2010.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-480")]
    [Trait("Req", "LANG-483")]
    [InlineData("type TC = class class function S(X: Integer): Integer; static; class function Get: TF; static; end; " +
        "class function TC.S(X: Integer): Integer; begin Result := X end; class function TC.Get: TF; begin Result := S end; " +
        "var F: TF; begin F := TC.S; F := @TC.S; F := TC.Get; end.", null)]
    [InlineData("function O(X: string): Integer; overload; begin Result := 0 end; function O(X: Integer): Integer; overload; begin Result := X end; " +
        "function Apply(F: TF): Integer; begin Result := F(1) end; var F: TF; N: Integer; begin F := O; F := @O; N := Apply(O); end.", null)]
    [InlineData("function O(X: string): Integer; overload; begin Result := 0 end; function O(X: Byte): Integer; overload; begin Result := X end; " +
        "var F: TF; begin F := O; end.", "E2131: No overload of 'O' matches procedural type 'TF'")]
    [InlineData("type TC = class function M(X: Integer): Integer; end; function TC.M(X: Integer): Integer; begin Result := X end; " +
        "var F: TF; begin F := TC.M; end.", "E2010: Incompatible types: 'function(Integer): Integer of object' and 'TF'")]
    public void MethodAndOverloadSources(string body, string? expected) => AssertDiagnostics(body, expected);

    /// <summary>C-103 / LANG-481: method pointers from instance and class methods, copies, nil, Assigned, calls; E2010 for other sources.</summary>
    [Theory]
    [Trait("Req", "LANG-481")]
    [InlineData("var C: TC; E, G: TE; begin E := C.M; E := @C.M; E := TC.K; E := C.K; G := E; E := nil; if Assigned(E) or (E = nil) or (nil <> G) then E(1); end.", null)]
    [InlineData("procedure TC.N; var E: TE; begin E := M; E := K; E(2) end; var C: TC; begin end.", null)]
    [InlineData("procedure P(X: Integer); begin end; var E: TE; begin E := P; end.", "E2010: Incompatible types: 'procedure(Integer)' and 'TE'")]
    [InlineData("var E: TE; begin E := TC.M; end.", "E2010: Incompatible types: 'procedure(Integer) of object' and 'TE'")]
    [InlineData("var C: TC; F: TF; begin F := C.M; end.", "E2010: Incompatible types: 'procedure(Integer) of object' and 'TF'")]
    public void MethodPointers(string body, string? expected) =>
        AssertDiagnostics("type TE = procedure(X: Integer) of object; TC = class procedure M(X: Integer); class procedure K(X: Integer); procedure N; end; " +
            "procedure TC.M(X: Integer); begin end; class procedure TC.K(X: Integer); begin end; " + (body.StartsWith("procedure TC.N") ? "" : "procedure TC.N; begin end; ") + body, expected);

    /// <summary>
    /// C-207 / LANG-481: <c>=</c>/<c>&lt;&gt;</c> between two method pointers of compatible types bind (operand type the method pointer,
    /// lowered to Code and Data compares); other operators and incompatible types are E2011.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-481")]
    [InlineData("var C: TC; E, G: TE; H: TH; B: Boolean; begin E := C.M; G := E; B := E = G; B := (E <> G) or (E = H); end.", null)]
    [InlineData("var E: TE; F: TF2; B: Boolean; begin B := E = F; end.", "E2011: Operator '=' not applicable to operand type(s) 'TE' and 'TF2'")]
    [InlineData("var E, G: TE; B: Boolean; begin B := E < G; end.", "E2011: Operator '<' not applicable to operand type(s) 'TE' and 'TE'")]
    public void MethodPointerEquality(string body, string? expected) =>
        AssertDiagnostics("type TE = procedure(X: Integer) of object; TH = procedure(Y: Integer) of object; TF2 = procedure(S: string) of object; " +
            "TC = class procedure M(X: Integer); end; procedure TC.M(X: Integer); begin end; " + body, expected);

    /// <summary>C-207 / LANG-481: a method pointer bound to <c>E = G</c> keeps both operands; <c>Intf.M</c> is a method pointer whose Data is the interface.</summary>
    [Fact]
    [Trait("Req", "LANG-481")]
    public void MethodPointerEquality_BoundTree()
    {
        var (result, _) = Bind(Header + "type TE = procedure(X: Integer) of object; var E, G: TE; B: Boolean; begin B := E = G end.");
        Assert.Empty(Format(result.Diagnostics));
        Assert.Equal("(block (:= B:Boolean (Equal:Boolean E:TE G:TE)))", BoundDump.Of(result.Bodies[^1]));
    }

    /// <summary>
    /// C-207: <c>Intf.M</c> (also with <c>@</c>, also an inherited interface method) where a method pointer is expected; a signature
    /// mismatch is E2010, an overloaded name without a match E2131.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-481")]
    [Trait("Req", "LANG-664")]
    [InlineData("var I: IFoo; E: TE; begin E := I.Bar; E := @I.Bar; E := I.Baz; E(1) end.", null)]
    [InlineData("var I: IFoo; E: TE; begin E := I.Qux; end.", "E2010: Incompatible types: 'function: Integer of object' and 'TE'")]
    [InlineData("var I: IFoo; E: TE; begin E := I.Ov; end.", "E2131: No overload of 'Ov' matches procedural type 'TE'")]
    public void InterfaceMethodPointer(string body, string? expected) =>
        AssertDiagnostics("type TE = procedure(X: Integer) of object; IBase = interface procedure Baz(X: Integer); end; " +
            "IFoo = interface(IBase) procedure Bar(X: Integer); function Qux: Integer; procedure Ov(S: string); overload; procedure Ov(B: Byte); overload; end; " + body, expected);

    private static void AssertDiagnostics(string body, string? expected)
    {
        var diagnostics = BindDiagnostics(Header + body).Select(d => System.Text.RegularExpressions.Regex.Replace(d, @"\(\d+,\d+\)", ""));
        Assert.Equal(expected is null ? [] : [expected], diagnostics);
    }

    [Theory]
    [Trait("Req", "LANG-483")]
    [InlineData("procedure Q(X: Word); begin end; var P: procedure(X: Integer); begin P := Q; end.",
        "E2010(1,132): Incompatible types: 'procedure(Word)' and 'procedure(X: Integer)'")]
    [InlineData("function Twice(X: Integer): Integer; begin Result := X end; var P: TP; begin P := Twice; end.",
        "E2010(1,140): Incompatible types: 'function(Integer): Integer' and 'TP'")]
    [InlineData("procedure Q(var X: Integer); begin end; var F: procedure(X: Integer); begin F := @Q; end.",
        "E2010(1,139): Incompatible types: 'procedure(var Integer)' and 'procedure(X: Integer)'")]
    [InlineData("var F: TF; P: TP; begin P := F; end.", "E2010(1,87): Incompatible types: 'TF' and 'TP'")]
    public void IncompatibleSignature_IsE2010(string body, string expected) => Assert.Equal([expected], BindDiagnostics(Header + body));

    [Theory]
    [Trait("Req", "LANG-484")]
    [InlineData("var P: TP; N: Integer; begin N := P(); end.", "E2034(1,92): Expression has no value (procedure call used as a value)")]
    [InlineData("var F: TF; N: Integer; begin N := F(); end.", "E2110(1,92): Not enough actual parameters for 'TF'")]
    [InlineData("var F: TF; N: Integer; begin N := F(1, 2); end.", "E2111(1,97): Too many actual parameters for 'TF'")]
    public void CallArguments_AreChecked(string body, string expected) => Assert.Equal([expected], BindDiagnostics(Header + body));

    [Fact]
    [Trait("Req", "LANG-451")]
    public void NestedRoutine_CannotBeAssigned() =>
        Assert.Equal(["E2130(1,125): Local procedure/function 'Inner' cannot be assigned to a procedural variable"],
            BindDiagnostics(Header + "procedure Outer; procedure Inner; begin end; var P: TP; begin P := Inner; end; begin end."));
}
