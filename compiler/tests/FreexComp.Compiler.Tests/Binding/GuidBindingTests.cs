using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-299 (com-interfaces.md §2): interface type identifiers as <c>TGUID</c> values, GUID constants, pointer ↔ interface casts.</summary>
[Trait("Phase", "8")]
public sealed class GuidBindingTests
{
    /// <summary>A <c>System</c> with <c>TGUID</c> (ABI-096) that also declares equality, so <c>=</c>/<c>&lt;&gt;</c> on GUIDs bind.</summary>
    private const string SystemSource = """
        unit System;
        interface
        type
          TGUID = packed record
            D1: Cardinal;
            D2: Word;
            D3: Word;
            D4: array[0..7] of Byte;
            class operator Equal(const A, B: TGUID): Boolean;
            class operator NotEqual(const A, B: TGUID): Boolean;
          end;
          TObject = class
          end;
        var Input, Output: Text;
        implementation
        class operator TGUID.Equal(const A, B: TGUID): Boolean; begin Result := A.D1 = B.D1; end;
        class operator TGUID.NotEqual(const A, B: TGUID): Boolean; begin Result := A.D1 <> B.D1; end;
        end.
        """;

    private const string Types = """
        type
          IPlain = interface end;
          IFoo = interface ['{01234567-89AB-CDEF-1021-324354657687}'] end;

        """;

    private static BindingResult BindWithGuid(string text)
    {
        var types = new BuiltInTypes();
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", SystemSource)), types);
        Assert.Empty(Format(system.Diagnostics));
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, Types + text));
        Assert.Empty(tree.Diagnostics);
        return Binder.BindCompilationUnit(tree, types, system);
    }

    private static string[] Diagnostics(string text) => Format(BindWithGuid(text).Diagnostics);

    [Fact]
    [Trait("Req", "LANG-680")]
    public void InterfaceIdentifierIsItsGuidWhereAGuidIsRequired()
    {
        Assert.Empty(Diagnostics("""
            procedure ByConst(const G: TGUID); begin end;
            procedure ByValue(G: TGUID); begin end;
            procedure Pick(N: Integer); overload; begin end;
            procedure Pick(const G: TGUID); overload; begin end;
            function Id: TGUID; begin Result := IFoo; end;
            const C: TGUID = IFoo;
            var V: TGUID = IFoo; G: TGUID; B: Boolean;
            begin
              ByConst(IFoo); ByValue(IFoo); Pick(IFoo); Pick(1);
              G := IFoo;
              B := G = IFoo;
              B := IFoo <> G;
              B := (G = (IFoo)) or (C = V);
            end.
            """));
    }

    [Fact]
    [Trait("Req", "LANG-680")]
    public void InterfaceWithoutGuid_E2655()
    {
        Assert.Equal(
            [
                "E2655(5,18): Interface 'IPlain' has no GUID",
                "E2655(8,8): Interface 'IPlain' has no GUID",
                "E2655(9,8): Interface 'IPlain' has no GUID",
                "E2655(10,12): Interface 'IPlain' has no GUID",
            ],
            Diagnostics("""
                procedure Take(const G: TGUID); begin end;
                const C: TGUID = IPlain;
                var G: TGUID; B: Boolean;
                begin
                  G := IPlain;
                  Take(IPlain);
                  B := G = IPlain;
                end.
                """));
    }

    [Fact]
    [Trait("Req", "LANG-680")]
    public void InterfaceIdentifierElsewhereStaysE2009()
    {
        Assert.Equal(
            [
                "E2009(7,8): Variable or constant expected but type 'IFoo' found",
                "E2009(8,8): Variable or constant expected but type 'IFoo' found",
                "E2009(9,11): Variable or constant expected but type 'IFoo' found",
            ],
            Diagnostics("""
                procedure TakeVar(var G: TGUID); begin end;
                var N: Integer; P: Pointer;
                begin
                  N := IFoo;
                  P := IFoo;
                  TakeVar(IFoo);
                end.
                """));
    }

    [Fact]
    [Trait("Req", "LANG-681")]
    public void GuidConstantFromString()
    {
        var result = BindWithGuid("""
            const
              A: TGUID = '{0000010C-0000-0000-C000-000000000046}';
              B: TGUID = '{0000010C-0000-0000-C000}';
              C: TGUID = 5;
            var G: TGUID;
            begin
              G := '{0000010C-0000-0000-C000-000000000046}';
            end.
            """);

        Assert.Equal(
            [
                "E2650(6,14): Invalid GUID string",
                "E2010(7,14): Incompatible types: 'Integer' and 'TGUID'",
                "E2010(10,8): Incompatible types: 'string' and 'TGUID'",
            ],
            Format(result.Diagnostics));
    }

    [Fact]
    [Trait("Req", "LANG-682")]
    public void PointerInterfaceCasts()
    {
        Assert.Equal(
            [
                "E2040(10,8): Invalid typecast from 'TObject' to 'IFoo'",
                "E2040(11,8): Invalid typecast from 'IPlain' to 'IFoo'",
                "E2040(12,8): Invalid typecast from 'PInt' to 'IFoo'",
                "E2040(13,8): Invalid typecast from 'Integer' to 'IFoo'",
            ],
            Diagnostics("""
                type PInt = ^Integer;
                var F: IFoo; P: Pointer; O: TObject; I: IPlain; T: PInt; N: Integer;
                begin
                  P := Pointer(F);
                  F := IFoo(P);
                  F := IFoo(Pointer(I));
                  F := IFoo(O);
                  F := IFoo(I);
                  F := IFoo(T);
                  F := IFoo(N);
                end.
                """));
    }
}
