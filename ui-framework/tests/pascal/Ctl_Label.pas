// covers: UIF-610, UIF-611, UIF-612
// needs: desktop
program Ctl_Label;
{ U-053, controls-standard.md section 1: TLabel defaults (34 x 15, AutoSize, ParentColor, ParentFont, Transparent
  without csOpaque); Paint (UIF-610) captured with WM_PRINTCLIENT into a memory bitmap: Transparent leaves the
  background, opaque fills with Color, Alignment and Layout place the text, WordWrap breaks lines, ShowAccelChar
  False draws the ampersand, a disabled label draws in clGrayText; the accelerator focuses FocusControl
  (UIF-611); freeing FocusControl clears the reference (UIF-612). }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, TestKit;

type
  TFocusBox = class(TCustomControl)
  public
    constructor Create(AOwner: TComponent); override;
  end;

var
  F: TForm;
  L: TLabel;
  Bmp: TBitmap;

constructor TFocusBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  TabStop := True;
  SetBounds(0, 0, 20, 20);
end;

procedure Capture;
var
  DC: HDC;
begin
  DC := Bmp.Canvas.Handle;
  SendMessageW(F.Handle, WM_PRINTCLIENT, WPARAM(DC), PRF_CLIENT or PRF_ERASEBKGND);
end;

{ the number of pixels of colour C inside (X1, Y1) .. (X2 - 1, Y2 - 1) }
function Count(C: TColor; X1, Y1, X2, Y2: Integer): Integer;
var
  X, Y: Integer;
begin
  Result := 0;
  C := ColorToRGB(C);
  for Y := Y1 to Y2 - 1 do
    for X := X1 to X2 - 1 do
      if Bmp.Canvas.Pixels[X, Y] = C then
        Inc(Result);
end;

{ the right-most column holding a pixel of colour C, -1 if none }
function RightMost(C: TColor; X1, Y1, X2, Y2: Integer): Integer;
var
  X, Y: Integer;
begin
  Result := -1;
  C := ColorToRGB(C);
  for Y := Y1 to Y2 - 1 do
    for X := X1 to X2 - 1 do
      if (Bmp.Canvas.Pixels[X, Y] = C) and (X > Result) then
        Result := X;
end;

procedure Run;
var
  A, B: TFocusBox;
  R1, R2: Integer;
begin
  Application.Initialize;
  Bmp := TBitmap.Create;
  Bmp.SetSize(200, 160);
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 200;
    F.ClientHeight := 160;
    F.Color := clWhite;
    F.Font.Height := -40;
    F.Font.Style := [fsBold];
    F.Font.Color := clBlack;
    L := TLabel.Create(F);

    Section('defaults');
    CheckEqI(34, L.Width, 'Width 34');
    CheckEqI(15, L.Height, 'Height 15');
    CheckEqB(True, L.AutoSize, 'AutoSize');
    CheckEqB(True, L.Alignment = taLeftJustify, 'Alignment');
    CheckEqB(True, L.Layout = tlTop, 'Layout');
    CheckEqB(True, L.ShowAccelChar, 'ShowAccelChar');
    CheckEqB(True, L.Transparent, 'Transparent');
    CheckEqB(False, L.WordWrap, 'WordWrap');
    CheckEqB(True, L.ParentColor, 'ParentColor');
    CheckEqB(True, L.ParentFont, 'ParentFont');
    CheckEqB(True, L.FocusControl = nil, 'FocusControl');
    CheckEqB(False, csOpaque in L.ControlStyle, 'transparent: not opaque');
    CheckEqS('', L.Caption, 'Caption empty');
    Check(GetClass('TLabel') <> nil, 'registered');
    L.Transparent := False;
    CheckEqB(True, csOpaque in L.ControlStyle, 'not transparent: opaque');
    L.Transparent := True;

    Section('Paint (UIF-610)');
    L.Parent := F;
    L.AutoSize := False;
    L.SetBounds(10, 10, 100, 100);
    L.Caption := 'M';
    Capture;
    Check(Count(clBlack, 10, 10, 110, 110) > 0, 'text drawn in the font colour');
    CheckEqI(0, Count(clBlack, 0, 0, 200, 10), 'nothing above the label');
    CheckEqB(True, Bmp.Canvas.Pixels[105, 105] = clWhite, 'transparent: background of the parent');
    L.Transparent := False;
    L.Color := clRed;
    Capture;
    CheckEqB(True, Bmp.Canvas.Pixels[105, 105] = clRed, 'opaque: filled with Color');
    CheckEqB(True, Bmp.Canvas.Pixels[5, 5] = clWhite, 'outside unchanged');
    L.Transparent := True;
    L.Alignment := taRightJustify;
    Capture;
    CheckEqI(0, Count(clBlack, 10, 10, 60, 110), 'right-justified: left half empty');
    Check(Count(clBlack, 60, 10, 110, 110) > 0, 'right-justified: text in the right half');
    L.Alignment := taCenter;
    Capture;
    CheckEqI(0, Count(clBlack, 10, 10, 35, 110), 'centred: left quarter empty');
    CheckEqI(0, Count(clBlack, 85, 10, 110, 110), 'centred: right quarter empty');
    L.Alignment := taLeftJustify;
    L.Layout := tlBottom;
    Capture;
    CheckEqI(0, Count(clBlack, 10, 10, 110, 55), 'tlBottom: upper half empty');
    Check(Count(clBlack, 10, 55, 110, 110) > 0, 'tlBottom: text at the bottom');
    L.Layout := tlCenter;
    Capture;
    CheckEqI(0, Count(clBlack, 10, 10, 110, 30), 'tlCenter: top empty');
    CheckEqI(0, Count(clBlack, 10, 90, 110, 110), 'tlCenter: bottom empty');
    L.Layout := tlTop;
    L.Caption := 'MM MM';
    Capture;
    CheckEqI(0, Count(clBlack, 10, 60, 110, 110), 'no WordWrap: one line');
    L.WordWrap := True;
    Capture;
    Check(Count(clBlack, 10, 60, 110, 110) > 0, 'WordWrap: second line');
    L.WordWrap := False;
    L.Caption := '&M';
    Capture;
    R1 := RightMost(clBlack, 10, 10, 110, 110);
    L.ShowAccelChar := False;
    Capture;
    R2 := RightMost(clBlack, 10, 10, 110, 110);
    Check(R2 > R1, 'ShowAccelChar False: the ampersand is drawn');
    L.ShowAccelChar := True;
    L.Caption := 'M';
    L.Enabled := False;
    Capture;
    CheckEqI(0, Count(clBlack, 10, 10, 110, 110), 'disabled: no text in the font colour');
    Check(Count(clGrayText, 10, 10, 110, 110) > 0, 'disabled: clGrayText');
    L.Enabled := True;

    Section('accelerator (UIF-611, UIF-612)');
    A := TFocusBox.Create(F);
    A.Name := 'A';
    A.SetBounds(120, 10, 20, 20);
    A.Parent := F;
    B := TFocusBox.Create(F);
    B.Name := 'B';
    B.SetBounds(150, 10, 20, 20);
    B.Parent := F;
    L.Caption := '&Name';
    F.Show;
    F.ActiveControl := A;
    A.SetFocus;
    CheckEqI(0, L.Perform(CM_DIALOGCHAR, Ord('N'), 0), 'no FocusControl: not consumed');
    L.FocusControl := B;
    CheckEqI(0, L.Perform(CM_DIALOGCHAR, Ord('X'), 0), 'other character: not consumed');
    L.ShowAccelChar := False;
    CheckEqI(0, L.Perform(CM_DIALOGCHAR, Ord('N'), 0), 'ShowAccelChar False: not consumed');
    L.ShowAccelChar := True;
    L.Enabled := False;
    CheckEqI(0, L.Perform(CM_DIALOGCHAR, Ord('N'), 0), 'disabled: not consumed');
    L.Enabled := True;
    CheckEqB(True, F.ActiveControl = A, 'focus unchanged so far');
    CheckEqI(1, L.Perform(CM_DIALOGCHAR, Ord('n'), 0), 'accelerator consumed');
    CheckEqB(True, F.ActiveControl = B, 'FocusControl focused');
    F.ActiveControl := A;
    A.SetFocus;
    PostMessageW(A.Handle, WM_SYSCHAR, Ord('n'), 1);
    Application.ProcessMessages;
    CheckEqB(True, F.ActiveControl = B, 'Alt+N through the form focuses FocusControl');
    B.Free;
    CheckEqB(True, L.FocusControl = nil, 'freed FocusControl cleared');
  finally
    F.Free;
    Bmp.Free;
  end;
end;

begin
  Run;
  Done;
end.
