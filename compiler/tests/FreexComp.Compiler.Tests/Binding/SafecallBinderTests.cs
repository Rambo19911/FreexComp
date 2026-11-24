using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-301 (com-interfaces.md §4): <c>safecall</c> is part of the signature (LANG-689) and forbidden on some routines (LANG-685).</summary>
[Trait("Phase", "8")]
public sealed class SafecallBinderTests
{
    /// <summary>
    /// The reasons of LANG-685 that source text can produce. An anonymous method has no directive position in the grammar (and a
    /// <c>reference to</c> type takes no convention), so its reason has no case.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-685")]
    [InlineData("type T = class constructor Create; safecall; end; constructor T.Create; begin end; begin end.", "E2656(1,36): 'safecall' is not allowed on a constructor")]
    [InlineData("type T = class destructor Done; safecall; end; destructor T.Done; begin end; begin end.", "E2656(1,33): 'safecall' is not allowed on a destructor")]
    [InlineData("type R = record X: Integer; constructor Make(A: Integer); safecall; end; constructor R.Make(A: Integer); begin end; begin end.",
        "E2656(1,59): 'safecall' is not allowed on a constructor")]
    [InlineData("type R = record X: Integer; class operator Add(const A, B: R): R; safecall; end; class operator R.Add(const A, B: R): R; begin Result := A end; begin end.",
        "E2656(1,67): 'safecall' is not allowed on an operator")]
    [InlineData("procedure Outer; procedure Inner; safecall; begin end; begin Inner end; begin end.", "E2656(1,35): 'safecall' is not allowed on a nested routine")]
    [InlineData("procedure P(A: Integer); varargs; safecall; external 'x.dll'; begin end.", "E2656(1,35): 'safecall' is not allowed on a varargs routine")]
    public void ForbiddenTargets_E2656(string source, string expected) => Assert.Equal([expected], BindDiagnostics(source));

    [Fact]
    [Trait("Req", "LANG-685")]
    public void AllowedTargets()
    {
        Assert.Empty(BindDiagnostics("""
            type
              TProc = procedure(A: Integer); safecall;
              TEvent = function: Integer of object; safecall;
              IFoo = interface
                procedure P; safecall;
                function F: Integer; safecall;
              end;
              TFoo = class(IFoo)
                procedure P; safecall;
                function F: Integer; safecall;
                function V: string; virtual; safecall;
                class function C: Integer; safecall;
              end;
              TBar = class(TFoo)
                function V: string; override; safecall;
              end;
              TRec = record
                X: Integer;
                function Get: Integer; safecall;
              end;
            procedure G(A: Integer); safecall; forward;
            procedure Ext; safecall; external 'x.dll';
            procedure TFoo.P; begin end;
            function TFoo.F: Integer; safecall; begin Result := 1 end;
            function TFoo.V: string; begin Result := '' end;
            class function TFoo.C: Integer; begin Result := 2 end;
            function TBar.V: string; begin Result := 'b' end;
            function TRec.Get: Integer; begin Result := X end;
            procedure G(A: Integer); begin end;
            var P: TProc; E: TEvent; O: TFoo;
            begin
              P := G;
              O := nil;
              E := O.F;
            end.
            """));
    }

    [Fact]
    [Trait("Req", "LANG-689")]
    public void SignatureMismatch_Forward_Override_InterfaceImpl_ProcType()
    {
        Assert.Equal(
            [
                "E2651(11,16): Missing implementation of interface method 'IFoo.F'",
                "E2621(15,14): Declaration of 'V' differs from the virtual method it overrides",
                "E2101(19,11): Declaration of 'G' differs from its previous declaration",
                "E2101(20,11): Declaration of 'H' differs from its previous declaration",
                "E2101(21,15): Declaration of 'F' differs from its previous declaration",
                "E2010(26,8): Incompatible types: 'TPlain' and 'TSafe'",
                "E2010(27,8): Incompatible types: 'procedure' and 'TSafe'",
                "E2010(28,8): Incompatible types: 'procedure' and 'TPlain'",
            ],
            BindDiagnostics("""
                type
                  TSafe = procedure; safecall;
                  TPlain = procedure; stdcall;
                  TOther = procedure; cdecl;
                  IFoo = interface
                    function F: Integer; safecall;
                  end;
                  TBase = class
                    function V: Integer; virtual; safecall;
                  end;
                  TFoo = class(IFoo)
                    function F: Integer;
                  end;
                  TDerived = class(TBase)
                    function V: Integer; override;
                  end;
                procedure G; safecall; forward;
                procedure H; stdcall; forward;
                procedure G; stdcall; begin end;
                procedure H; safecall; begin end;
                function TFoo.F: Integer; safecall; begin Result := 0 end;
                function TBase.V: Integer; begin Result := 0 end;
                function TDerived.V: Integer; begin Result := 0 end;
                var S: TSafe; P: TPlain; O: TOther;
                begin
                  S := P;
                  S := H;
                  P := G;
                  P := O;
                  O := H;
                end.
                """));
    }
}
