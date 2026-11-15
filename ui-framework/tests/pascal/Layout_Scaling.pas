// covers: UIF-280, UIF-281, UIF-282, UIF-283, UIF-285
// needs: desktop
program Layout_Scaling;
{ U-049, layout.md section 9: ChangeScale scales bounds by edges (forms: client size, Left/Top unscaled),
  constraints, margins, padding and own fonts; ParentFont children follow the parent font; ScaleBy adds the
  baseline re-capture and the realign (UIF-283); M = D is a no-op; scaling back stays within 1 px (UIF-281);
  examples E30 and E31; load-time scaling from the stored PixelsPerInch to the form's DPI unless Scaled = False
  (UIF-282); WM_DPICHANGED scales, takes the suggested rectangle, adopts the real client size, broadcasts
  CM_DPICHANGED and fires OnAfterMonitorDpiChanged (UIF-285); Scaled = False only repositions. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, TestKit;

type
  TBox = class(TCustomControl)
  protected
    procedure WndProc(var Message: TMessage); override;
    procedure Resize; override;
  public
    DpiChanged: Integer;
    property Font;
    property ParentFont;
    property Padding;
  end;

  TScaleForm = class(TForm)
  published
    Box: TBox;
    Corner: TBox;
  end;

  TFixedForm = class(TForm)
  published
    Box: TBox;
  end;

  TLog = class
  public
    Text: string;
    procedure DpiChanged(Sender: TObject; OldDPI, NewDPI: Integer);
  end;

{$FORM Layout_Scaling.form}
{$FORM Layout_ScalingOff.form}

var
  Log: TLog;
  Resizes: Integer;

procedure TBox.WndProc(var Message: TMessage);
begin
  if Message.Msg = CM_DPICHANGED then
    Inc(DpiChanged);
  inherited WndProc(Message);
end;

procedure TBox.Resize;
begin
  Inc(Resizes);
  inherited Resize;
end;

procedure TLog.DpiChanged(Sender: TObject; OldDPI, NewDPI: Integer);
begin
  Text := Text + Format('dpi %d->%d;', [OldDPI, NewDPI]);
end;

function B(C: TControl): string;
begin
  Result := Format('%d,%d,%d,%d', [C.Left, C.Top, C.Width, C.Height]);
end;

function NewBox(AParent: TWinControl; L, T, W, H: Integer): TBox;
begin
  Result := TBox.Create(AParent);
  Result.Parent := AParent;
  Result.SetBounds(L, T, W, H);
end;

function Within1(A, Expected: Integer): Boolean;
begin
  Result := Abs(A - Expected) <= 1;
end;

procedure Run;
var
  F: TForm;
  Btn, A, Bx, Inherit, Anchored, Top: TBox;
  S: TScaleForm;
  X: TFixedForm;
  P, OldDpi, NewDpi: Integer;
  R, CR: TRect;
begin
  Log := TLog.Create;
  try
    Application.Initialize;

    Section('E30: 96 -> 120');
    F := TForm.CreateNew(nil);
    F.ClientWidth := 400;
    F.ClientHeight := 240;
    F.Font.Height := -12;
    F.Left := 7;
    F.Top := 9;
    Btn := NewBox(F, 10, 10, 75, 25);
    Inherit := NewBox(F, 0, 100, 20, 20);
    F.ScaleBy(120, 96);
    CheckEqI(500, F.ClientWidth, 'client width 500');
    CheckEqI(300, F.ClientHeight, 'client height 300');
    CheckEqS('13,13,93,31', B(Btn), 'Btn (13, 13, 93, 31)');
    CheckEqI(-15, F.Font.Height, 'form font height -15');
    CheckEqB(True, Inherit.ParentFont, 'ParentFont child keeps ParentFont');
    CheckEqI(-15, Inherit.Font.Height, 'ParentFont child follows');
    CheckEqI(7, F.Left, 'form Left not scaled');
    CheckEqI(9, F.Top, 'form Top not scaled');
    F.Free;

    Section('E31: 96 -> 144, adjacent controls stay adjacent');
    F := TForm.CreateNew(nil);
    F.ClientWidth := 200;
    F.ClientHeight := 100;
    A := NewBox(F, 0, 0, 33, 20);
    Bx := NewBox(F, 33, 0, 33, 20);
    F.ScaleBy(144, 96);
    CheckEqS('0,0,50,30', B(A), 'A (0, 0, 50, 30)');
    CheckEqS('50,0,49,30', B(Bx), 'B (50, 0, 49, 30)');
    F.ScaleBy(96, 144);
    Check(Within1(A.Left, 0) and Within1(A.Left + A.Width, 33) and Within1(A.Top + A.Height, 20), 'A back within 1 px');
    Check(Within1(Bx.Left, 33) and Within1(Bx.Left + Bx.Width, 66), 'B back within 1 px');
    CheckEqI(Bx.Left, A.Left + A.Width, 'still adjacent after the inverse');
    Check(Within1(F.ClientWidth, 200) and Within1(F.ClientHeight, 100), 'client back within 1 px');
    F.Free;

    Section('constraints, margins, padding, own font');
    F := TForm.CreateNew(nil);
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    Bx := NewBox(F, 8, 8, 100, 100);
    Bx.Constraints.MinWidth := 20;
    Bx.Constraints.MaxHeight := 200;
    Bx.Margins.SetBounds(3, 4, 5, 6);
    Bx.Padding.SetBounds(8, 0, 1, 2);
    Bx.Font.Height := -16;
    A := NewBox(Bx, 1, 1, 10, 10);
    F.ScaleBy(120, 96);
    CheckEqI(25, Bx.Constraints.MinWidth, 'MinWidth 25');
    CheckEqI(0, Bx.Constraints.MinHeight, 'zero constraint stays 0');
    CheckEqI(250, Bx.Constraints.MaxHeight, 'MaxHeight 250');
    CheckEqS('4,5,6,8', Format('%d,%d,%d,%d', [Bx.Margins.Left, Bx.Margins.Top, Bx.Margins.Right, Bx.Margins.Bottom]),
      'margins scaled');
    CheckEqS('10,0,1,3', Format('%d,%d,%d,%d', [Bx.Padding.Left, Bx.Padding.Top, Bx.Padding.Right, Bx.Padding.Bottom]),
      'padding scaled');
    CheckEqI(-20, Bx.Font.Height, 'own font scaled');
    CheckEqB(False, Bx.ParentFont, 'own font stays own');
    CheckEqS('1,1,13,13', B(A), 'grandchild scaled');
    CheckEqS('10,10,125,125', B(Bx), 'child scaled');
    F.ScaleBy(120, 120);
    CheckEqS('10,10,125,125', B(Bx), 'M = D: no change');
    CheckEqI(25, Bx.Constraints.MinWidth, 'M = D: constraints unchanged');
    F.Free;

    Section('UIF-283: baselines re-captured, realign');
    F := TForm.CreateNew(nil);
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    Anchored := NewBox(F, 300, 10, 50, 20);
    Anchored.Anchors := [akTop, akRight];
    Top := NewBox(F, 0, 0, 10, 30);
    Top.Align := alTop;
    F.ScaleBy(120, 96);
    CheckEqS('375,13,63,25', B(Anchored), 'anchored child scaled by edges');
    CheckEqS('0,0,500,38', B(Top), 'alTop child: scaled height, realigned width');
    F.ClientWidth := 520;
    CheckEqI(395, Anchored.Left, 'anchor follows from the scaled baseline');
    CheckEqI(520, Top.Width, 'alTop follows');
    F.Free;

    Section('load-time scaling (UIF-282)');
    Resizes := 0;
    Application.CreateForm(TScaleForm, S);
    CheckEqI(0, Resizes, 'no OnResize while loading and scaling');
    P := S.PixelsPerInch;
    CheckEqI(LayoutMulDiv(400, P, 192), S.ClientWidth, 'client width scaled from 192');
    CheckEqI(LayoutMulDiv(240, P, 192), S.ClientHeight, 'client height scaled from 192');
    CheckEqI(LayoutMulDiv(10, P, 192), S.Box.Left, 'Box left');
    CheckEqI(LayoutMulDiv(85, P, 192) - LayoutMulDiv(10, P, 192), S.Box.Width, 'Box width by edges');
    CheckEqI(LayoutMulDiv(300, P, 192), S.Corner.Left, 'Corner left');
    CheckEqI(LayoutMulDiv(230, P, 192) - LayoutMulDiv(200, P, 192), S.Corner.Height, 'Corner height by edges');
    CheckEqI(LayoutMulDiv(-12, P, 192), S.Font.Height, 'form font scaled');
    S.ClientWidth := S.ClientWidth + 10;
    S.ClientHeight := S.ClientHeight + 4;
    CheckEqI(LayoutMulDiv(300, P, 192) + 10, S.Corner.Left, 'Corner baseline from the scaled bounds (x)');
    CheckEqI(LayoutMulDiv(200, P, 192) + 4, S.Corner.Top, 'Corner baseline from the scaled bounds (y)');
    X := TFixedForm.Create(nil);
    CheckEqI(400, X.ClientWidth, 'Scaled = False: client as stored');
    CheckEqS('10,10,75,25', B(X.Box), 'Scaled = False: bounds as stored');
    X.Free;

    Section('WM_DPICHANGED (UIF-285)');
    F := TForm.CreateNew(nil);
    F.ClientWidth := 300;
    F.ClientHeight := 200;
    F.Font.Height := -12;
    F.OnAfterMonitorDpiChanged := Log.DpiChanged;
    Bx := NewBox(F, 10, 10, 75, 25);
    F.HandleNeeded;
    OldDpi := F.PixelsPerInch;
    NewDpi := OldDpi * 2;
    R := Rect(100, 120, 100 + 800, 120 + 600);
    SendMessageW(F.Handle, WM_DPICHANGED, MakeWParam(NewDpi, NewDpi), LPARAM(@R));
    CheckEqI(NewDpi, F.PixelsPerInch, 'PixelsPerInch takes the new DPI');
    CheckEqS('20,20,150,50', B(Bx), 'child scaled');
    CheckEqI(-24, F.Font.Height, 'font scaled');
    CheckEqS('100,120,800,600', B(F), 'suggested rectangle');
    Windows.GetClientRect(F.Handle, CR);
    CheckEqI(CR.Right, F.ClientWidth, 'real client width adopted');
    CheckEqI(CR.Bottom, F.ClientHeight, 'real client height adopted');
    CheckEqI(1, Bx.DpiChanged, 'CM_DPICHANGED broadcast');
    CheckEqS(Format('dpi %d->%d;', [OldDpi, NewDpi]), Log.Text, 'OnAfterMonitorDpiChanged');
    F.Free;

    Section('WM_DPICHANGED with Scaled = False');
    F := TForm.CreateNew(nil);
    F.Scaled := False;
    F.ClientWidth := 300;
    F.ClientHeight := 200;
    Bx := NewBox(F, 10, 10, 75, 25);
    F.HandleNeeded;
    OldDpi := F.PixelsPerInch;
    R := Rect(50, 60, 50 + 500, 60 + 400);
    SendMessageW(F.Handle, WM_DPICHANGED, MakeWParam(OldDpi * 2, OldDpi * 2), LPARAM(@R));
    CheckEqS('10,10,75,25', B(Bx), 'child not scaled');
    CheckEqS('50,60,500,400', B(F), 'repositioned to the suggested rectangle');
    F.Free;
  finally
    Log.Free;
  end;
end;

begin
  RegisterClass(TBox);
  Run;
  Done;
end.
