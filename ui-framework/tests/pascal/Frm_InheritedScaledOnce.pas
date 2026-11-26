// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1451
// phase: 8
program Frm_InheritedScaledOnce;
{ U-160, ui-framework/phase8/form-inheritance.md UIF-1451: the load pass of UIF-430 step 3 runs once per
  construction, after the last level of the chain. The ancestor's file is stored at PixelsPerInch = 144, the
  descendant's file has no PixelsPerInch line (the ancestor's design DPI stays in force), and the form is created
  with Scaled on: every bound - inherited, overridden by the descendant (First.Left, the root's ClientWidth) and new
  in the descendant (Second inside the inherited panel, Third at the root) - is scaled from 144 dpi to the run-time
  DPI exactly once, by edges (layout.md UIF-280); TForm.Loaded runs once. The expected values are computed with
  LayoutMulDiv from the form's PixelsPerInch after loading, as Ctl_LoadedTwice_AnchorScaled does; the odd stored
  values make a second scaling visible at every DPI other than 144. Needs no desktop: no form is shown. }
uses
  SysUtils, Classes, Controls, Forms, StdCtrls, ExtCtrls, TestKit;

var
  LoadedCount: Integer;

type
  TScaledBase = class(TForm)
  published
    Box: TPanel;
    First: TButton;
  protected
    procedure Loaded; override;
  end;

  TScaledChild = class(TScaledBase)
  published
    Second: TButton;
    Third: TButton;
  end;

{$FORM Frm_InheritedScaledOnceBase.form}
{$FORM Frm_InheritedScaledOnce.form}

procedure TScaledBase.Loaded;
begin
  LoadedCount := LoadedCount + 1;
  inherited Loaded;
end;

function B(C: TControl): string;
begin
  Result := IntToStr(C.Left) + ',' + IntToStr(C.Top) + ',' + IntToStr(C.Width) + ',' + IntToStr(C.Height);
end;

var
  P: Integer;

{ stored bounds at 144 dpi, scaled once by edges (UIF-280) }
function S(L, T, W, H: Integer): string;
begin
  Result := IntToStr(LayoutMulDiv(L, P, 144)) + ',' + IntToStr(LayoutMulDiv(T, P, 144)) + ',' +
    IntToStr(LayoutMulDiv(L + W, P, 144) - LayoutMulDiv(L, P, 144)) + ',' +
    IntToStr(LayoutMulDiv(T + H, P, 144) - LayoutMulDiv(T, P, 144));
end;

var
  F: TScaledChild;
  A: TScaledBase;
begin
  Section('the ancestor alone');
  LoadedCount := 0;
  A := TScaledBase.Create(nil);
  P := A.PixelsPerInch;
  CheckEqI(1, LoadedCount, 'one Loaded');
  CheckEqI(LayoutMulDiv(480, P, 144), A.ClientWidth, 'ClientWidth');
  CheckEqI(LayoutMulDiv(300, P, 144), A.ClientHeight, 'ClientHeight');
  CheckEqS(S(12, 12, 301, 151), B(A.Box), 'Box');
  CheckEqS(S(15, 15, 113, 38), B(A.First), 'First');
  A.Free;

  Section('the descendant without a PixelsPerInch line (UIF-1451)');
  LoadedCount := 0;
  F := TScaledChild.Create(nil);
  CheckEqI(P, F.PixelsPerInch, 'the run-time DPI');
  CheckEqI(1, LoadedCount, 'one Loaded for both levels');
  CheckEqI(LayoutMulDiv(601, P, 144), F.ClientWidth, 'ClientWidth (the descendant''s value), scaled once');
  CheckEqI(LayoutMulDiv(300, P, 144), F.ClientHeight, 'ClientHeight (inherited), scaled once');
  CheckEqS(S(12, 12, 301, 151), B(F.Box), 'Box (inherited), scaled once');
  CheckEqS(S(31, 15, 113, 38), B(F.First), 'First (Left overridden), scaled once');
  CheckEqS(S(151, 15, 113, 38), B(F.Second), 'Second (new inside the inherited panel), scaled once');
  CheckEqS(S(331, 61, 113, 38), B(F.Third), 'Third (new at the root), scaled once');
  F.Free;
  Done;
end.
