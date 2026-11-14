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
