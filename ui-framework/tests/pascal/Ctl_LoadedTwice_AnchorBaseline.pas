// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-232, UIF-300
// phase: 8
program Ctl_LoadedTwice_AnchorBaseline;
{ U-158 (found by R-242, classes.md RTL-1097, RTL-1102): a frame instance whose outer file gives it another width
  than the frame's own file. The frame's children capture their anchor baselines in the Loaded call of the frame's
  reader, against the frame's own width; the outer lines change the width afterwards; a child reached by an
  override block gets a second Loaded from the outer reader. layout.md UIF-232 case 4: that second Loaded keeps
  the baseline of the first unless the block set the child's bounds (then they are the instance's bounds and are
  captured against the instance's client size), so reached and unreached anchored children move alike (right
  anchored: by the width difference; left and right: stretched); a child whose block sets Left stays where the
  block put it. UIF-300: a frame instance runs the layout of its tree at the end of its own Loaded, before the
  form scales the tree, so a resized instance on a form stored at another DPI ends with the children where the
  unscaled form has them, scaled. Resizing an instance afterwards moves every anchored child from its baseline.
  The frame stand-in is a TScrollBox descendant that reads its own resource like TFrame will (U-154). Needs no
  desktop: no form is shown. }
uses
  Windows, SysUtils, Classes, Controls, Forms, ExtCtrls, TestKit;

type
  TTestFrame = class(TScrollBox)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TAnchorFrame = class(TTestFrame)
  published
    RightA: TPanel;
    RightB: TPanel;
    RightC: TPanel;
    Stretch: TPanel;
  end;

  TAnchorForm = class(TForm)
  published
    Wide: TAnchorFrame;
    Same: TAnchorFrame;
  end;

  TScaledAnchorForm = class(TForm)
  published
    Wide: TAnchorFrame;
  end;

{$FORM Ctl_LoadedTwice_AnchorFrame.form}
{$FORM Ctl_LoadedTwice_AnchorBaseline.form}
{$FORM Ctl_LoadedTwice_AnchorScaled.form}

constructor TTestFrame.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  if ClassType <> TTestFrame then
    if not InitInheritedComponent(Self, TTestFrame, AOwner = nil) then
      raise EResNotFound.CreateFmt('Resource %s not found', [ClassName]);
end;

function B(C: TControl): string;
begin
  Result := IntToStr(C.Left) + ',' + IntToStr(C.Top) + ',' + IntToStr(C.Width) + ',' + IntToStr(C.Height);
end;

{ by edges, as ChangeScale (layout.md UIF-280) }
function ScaledH(L, W, P: Integer): string;
begin
  Result := IntToStr(LayoutMulDiv(L, P, 192)) + ',' + IntToStr(LayoutMulDiv(L + W, P, 192) - LayoutMulDiv(L, P, 192));
end;

function H(C: TControl): string;
begin
  Result := IntToStr(C.Left) + ',' + IntToStr(C.Width);
end;

var
  F: TAnchorForm;
  S: TScaledAnchorForm;
  Fr: TAnchorFrame;
  P: Integer;
begin
  RegisterClasses([TAnchorFrame]);

  Section('a frame in code keeps its own geometry');
  F := TAnchorForm.CreateNew(nil);
  Fr := TAnchorFrame.Create(F);
  CheckEqS('0,0,320,120', B(Fr), 'the frame''s own bounds');
  CheckEqS('280,0,30,20', B(Fr.RightA), 'RightA');
  Fr.Parent := F;
  Fr.Width := 360;
  CheckEqS('320,0,30,20', B(Fr.RightA), 'RightA follows a resize in code');
  CheckEqS('320,30,30,20', B(Fr.RightB), 'RightB follows a resize in code');
  CheckEqS('10,90,340,20', B(Fr.Stretch), 'Stretch follows a resize in code');
  F.Free;

  Section('an instance 80 px wider than its frame (UIF-232 case 4)');
  F := TAnchorForm.Create(nil);
  CheckEqS('0,0,400,120', B(F.Wide), 'the instance has the width of the outer file');
  CheckEqS('360,0,30,20', B(F.Wide.RightA), 'RightA (not reached) moves with the right edge');
  CheckEqS('360,30,30,20', B(F.Wide.RightB), 'RightB (reached, bounds not set) moves alike');
  CheckEqS('reached', F.Wide.RightB.Caption, 'the block of RightB was applied');
  CheckEqS('100,60,30,20', B(F.Wide.RightC), 'RightC stays where its block put it');
  CheckEqS('10,90,380,20', B(F.Wide.Stretch), 'Stretch (reached) stretches with the instance');
  CheckEqS('0,200,320,120', B(F.Same), 'an instance with the frame''s own size');
  CheckEqS('280,0,30,20', B(F.Same.RightA), 'its RightA does not move');
  CheckEqS('280,30,30,20', B(F.Same.RightB), 'its reached RightB does not move');
  CheckEqS('10,90,300,20', B(F.Same.Stretch), 'its reached Stretch keeps its width');

  Section('resizing the instance afterwards');
  F.Wide.Width := 500;
  CheckEqS('460,0,30,20', B(F.Wide.RightA), 'RightA');
  CheckEqS('460,30,30,20', B(F.Wide.RightB), 'RightB');
  CheckEqS('200,60,30,20', B(F.Wide.RightC), 'RightC from the baseline of its block');
  CheckEqS('10,90,480,20', B(F.Wide.Stretch), 'Stretch');
  F.Wide.Width := 400;
  CheckEqS('360,0,30,20', B(F.Wide.RightA), 'back: RightA');
  CheckEqS('360,30,30,20', B(F.Wide.RightB), 'back: RightB');
  CheckEqS('100,60,30,20', B(F.Wide.RightC), 'back: RightC');
  CheckEqS('10,90,380,20', B(F.Wide.Stretch), 'back: Stretch');
  F.Free;

  Section('an instance on a form stored at 192 dpi (UIF-300)');
  S := TScaledAnchorForm.Create(nil);
  P := S.PixelsPerInch;
  CheckEqS(ScaledH(0, 400, P), H(S.Wide), 'the instance: its outer width, scaled');
  CheckEqS(ScaledH(360, 30, P), H(S.Wide.RightA), 'RightA: moved, then scaled');
  CheckEqS(ScaledH(360, 30, P), H(S.Wide.RightB), 'RightB (reached): moved, then scaled');
  CheckEqS(ScaledH(360, 30, P), H(S.Wide.RightC), 'RightC (not reached): moved, then scaled');
  CheckEqS(ScaledH(10, 380, P), H(S.Wide.Stretch), 'Stretch (reached): stretched, then scaled');
  S.Free;
  Done;
end.
