// covers: UIF-745, UIF-748
// needs: desktop
program Ctl_ShapeBevel;
{ U-067, controls-standard.md sections 15 and 16: TShape defaults (65 x 65, white solid brush, black 1-px solid pen,
  stRectangle) and TBevel defaults (50 x 50, bsBox, bsLowered); Paint captured with WM_PRINTCLIENT into a memory
  bitmap: the UIF-745 rectangle for each shape type (squares and circles centred on min(W, H), rounded corners and
  ellipses leave the corners, wide and zero-width pens), pen and brush changes repaint; the UIF-748 lines of every
  bevel shape in both styles (bsBox corners: bottom/right drawn over top/left; bsFrame etched; bsSpacer draws
  nothing); streaming of Brush/Pen sub-properties, Shape and Style. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, ExtCtrls, TestKit;

type
  TShapeForm = class(TForm)
  published
    Disc: TShape;
    Rule: TBevel;
    procedure DiscMouseDown(Sender: TObject; Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
  end;

{$FORM Ctl_ShapeBevel.form}

var
  Bmp: TBitmap;
  F: TForm;

procedure TShapeForm.DiscMouseDown(Sender: TObject; Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
begin
end;

procedure Capture(AForm: TForm);
begin
  Bmp.Canvas.Brush.Color := clBlack;
  Bmp.Canvas.FillRect(Rect(0, 0, Bmp.Width, Bmp.Height));
  SendMessageW(AForm.Handle, WM_PRINTCLIENT, WPARAM(Bmp.Canvas.Handle), PRF_CLIENT or PRF_ERASEBKGND);
end;

{ the bounding box 'L,T,R,B' (R, B exclusive) of the pixels of colour C, 'none' without any }
function Box(C: TColor): string;
var
  X, Y, L, T, R, B: Integer;
begin
  C := ColorToRGB(C);
  L := MaxInt;
  T := MaxInt;
  R := -1;
  B := -1;
  for Y := 0 to Bmp.Height - 1 do
    for X := 0 to Bmp.Width - 1 do
      if Bmp.Canvas.Pixels[X, Y] = C then
      begin
        if X < L then
          L := X;
        if Y < T then
          T := Y;
        if X > R then
          R := X;
        if Y > B then
          B := Y;
      end;
  if R < 0 then
    Result := 'none'
  else
    Result := Format('%d,%d,%d,%d', [L, T, R + 1, B + 1]);
end;

{ pixels of colour C inside (X1, Y1) .. (X2 - 1, Y2 - 1) }
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

{ the pixel of the control at (10, 10) in its own coordinates }
function Px(X, Y: Integer): TColor;
begin
  Result := Bmp.Canvas.Pixels[10 + X, 10 + Y];
end;

function Is_(C: TColor; X, Y: Integer): Boolean;
begin
  Result := Px(X, Y) = ColorToRGB(C);
end;

procedure ShapeTests;
var
  S: TShape;
  B: TBrush;
begin
  S := TShape.Create(F);

  Section('TShape defaults');
  CheckEqS('65x65', Format('%dx%d', [S.Width, S.Height]), 'size 65 x 65');
  CheckEqB(True, S.Shape = stRectangle, 'Shape stRectangle');
  CheckEqB(True, S.Brush.Color = clWhite, 'Brush white');
  CheckEqB(True, S.Brush.Style = bsSolid, 'Brush solid');
  CheckEqB(True, S.Pen.Color = clBlack, 'Pen black');
  CheckEqI(1, S.Pen.Width, 'Pen width 1');
  CheckEqB(True, S.Pen.Style = psSolid, 'Pen solid');
  Check(GetClass('TShape') <> nil, 'registered');

  Section('TShape Paint (UIF-745)');
  S.SetBounds(10, 10, 30, 20);
  S.Parent := F;
  S.Brush.Color := clRed;
  Capture(F);
  CheckEqS('10,10,40,30', Box(clBlack), 'stRectangle: the pen on the control edges');
  CheckEqS('11,11,39,29', Box(clRed), 'stRectangle: brush inside');
  CheckEqI(28 * 18, Count(clRed, 0, 0, 300, 200), 'stRectangle: interior filled');
  CheckEqI(2 * 30 + 2 * 18, Count(clBlack, 0, 0, 300, 200), 'stRectangle: 1-px outline');
  S.Pen.Width := 0;
  Capture(F);
  CheckEqS('10,10,40,30', Box(clBlack), 'pen width 0: same rectangle');
  S.Pen.Width := 3;
  Capture(F);
  CheckEqS('10,10,40,30', Box(clBlack), 'pen width 3: X = 1, W = Width - 2, outline inside the control');
  Check(Is_(clRed, 15, 10), 'pen width 3: interior');
  CheckEqI(0, Count(clRed, 10, 10, 13, 30), 'pen width 3: three columns of pen at the left');
  S.Pen.Width := 1;
  S.Shape := stSquare;
  Capture(F);
  CheckEqS('15,10,35,30', Box(clBlack), 'stSquare: S = min(W, H), centred horizontally');
  S.SetBounds(10, 10, 20, 30);
  Capture(F);
  CheckEqS('10,15,30,35', Box(clBlack), 'stSquare: centred vertically');
  S.SetBounds(10, 10, 30, 20);
  S.Shape := stCircle;
  Capture(F);
  CheckEqS('15,10,35,30', Box(clBlack), 'stCircle: the square of side S');
  Check(not Is_(clBlack, 5, 0) and not Is_(clRed, 5, 0), 'stCircle: corner of the square left out');
  S.Shape := stEllipse;
  Capture(F);
  CheckEqS('10,10,40,30', Box(clBlack), 'stEllipse: the whole control');
  Check(not Is_(clBlack, 0, 0) and not Is_(clRed, 0, 0), 'stEllipse: corner left out');
  Check(Is_(clBlack, 15, 0), 'stEllipse: touches the top edge');
  Check(Is_(clRed, 15, 10), 'stEllipse: filled');
  S.Shape := stRoundRect;
  Capture(F);
  CheckEqS('10,10,40,30', Box(clBlack), 'stRoundRect: the whole control');
  Check(not Is_(clBlack, 0, 0), 'stRoundRect: rounded corner');
  Check(Is_(clBlack, 15, 0) and Is_(clBlack, 0, 10), 'stRoundRect: straight edges');
  S.Shape := stRoundSquare;
  Capture(F);
  CheckEqS('15,10,35,30', Box(clBlack), 'stRoundSquare: the square of side S');
  Check(not Is_(clBlack, 5, 0), 'stRoundSquare: rounded corner');

  Section('TShape pen and brush');
  S.Shape := stRectangle;
  S.Pen.Color := clBlue;
  Capture(F);
  CheckEqS('10,10,40,30', Box(clBlue), 'Pen.Color change repaints');
  S.Brush.Style := bsClear;
  Capture(F);
  CheckEqS('none', Box(clRed), 'bsClear: interior not filled');
  B := TBrush.Create;
  try
    B.Color := clLime;
    S.Brush := B;
    Check(S.Brush <> B, 'Brush assigned, not shared');
    CheckEqB(True, S.Brush.Color = clLime, 'Brush.Color copied');
  finally
    B.Free;
  end;
  Capture(F);
  CheckEqS('11,11,39,29', Box(clLime), 'assigned brush used');
  S.Free;
end;

procedure BevelTests;
var
  V: TBevel;
  Shadow, Light: TColor;
  X: Integer;
  RowOk: Boolean;
begin
  Shadow := clBtnShadow;
  Light := clBtnHighlight;
  V := TBevel.Create(F);

  Section('TBevel defaults');
  CheckEqS('50x50', Format('%dx%d', [V.Width, V.Height]), 'size 50 x 50');
  CheckEqB(True, V.Shape = bsBox, 'Shape bsBox');
  CheckEqB(True, V.Style = bsLowered, 'Style bsLowered');
  Check(GetClass('TBevel') <> nil, 'registered');

  Section('TBevel Paint (UIF-748)');
  V.SetBounds(10, 10, 30, 20);
  V.Parent := F;
  Capture(F);
  Check(Is_(Shadow, 0, 0) and Is_(Shadow, 5, 0) and Is_(Shadow, 0, 5), 'bsBox lowered: top and left in shadow');
  Check(Is_(Light, 5, 19) and Is_(Light, 29, 5), 'bsBox lowered: bottom and right in highlight');
  Check(Is_(Light, 29, 0) and Is_(Light, 0, 19), 'bsBox: bottom/right drawn over the corners');
  Check(Is_(clAqua, 5, 5), 'bsBox: inside not painted');
  CheckEqI(47, Count(Shadow, 10, 10, 40, 30), 'bsBox: 47 shadow pixels');
  CheckEqI(49, Count(Light, 10, 10, 40, 30), 'bsBox: 49 highlight pixels');
  V.Style := bsRaised;
  Capture(F);
  Check(Is_(Light, 0, 0) and Is_(Shadow, 29, 19), 'bsRaised: colours swapped');
  CheckEqI(47, Count(Light, 10, 10, 40, 30), 'bsRaised: 47 highlight pixels');
  V.Style := bsLowered;
  V.Shape := bsFrame;
  Capture(F);
  Check(Is_(Shadow, 0, 0) and Is_(Shadow, 28, 0) and Is_(Shadow, 0, 18), 'bsFrame: outer rectangle in shadow');
  Check(Is_(Light, 1, 1) and Is_(Light, 29, 19) and Is_(Light, 29, 1) and Is_(Light, 1, 19),
    'bsFrame: rectangle (1, 1, W, H) in highlight');
  Check(Is_(Light, 1, 18) and Is_(Light, 28, 1), 'bsFrame: highlight drawn last');
  Check(Is_(Shadow, 28, 18), 'bsFrame: inner corner of the shadow rectangle');
  Check(Is_(clAqua, 5, 5), 'bsFrame: inside not painted');
  V.Shape := bsTopLine;
  Capture(F);
  CheckEqI(30, Count(Shadow, 10, 10, 40, 11), 'bsTopLine: y = 0 in shadow');
  CheckEqI(30, Count(Light, 10, 11, 40, 12), 'bsTopLine: y = 1 in highlight');
  CheckEqI(30 * 18, Count(clAqua, 10, 12, 40, 30), 'bsTopLine: nothing else');
  V.Shape := bsBottomLine;
  Capture(F);
  CheckEqI(30, Count(Shadow, 10, 28, 40, 29), 'bsBottomLine: y = H - 2 in shadow');
  CheckEqI(30, Count(Light, 10, 29, 40, 30), 'bsBottomLine: y = H - 1 in highlight');
  CheckEqI(30 * 18, Count(clAqua, 10, 10, 40, 28), 'bsBottomLine: nothing else');
  V.Shape := bsLeftLine;
  Capture(F);
  CheckEqI(20, Count(Shadow, 10, 10, 11, 30), 'bsLeftLine: x = 0 in shadow');
  CheckEqI(20, Count(Light, 11, 10, 12, 30), 'bsLeftLine: x = 1 in highlight');
  CheckEqI(28 * 20, Count(clAqua, 12, 10, 40, 30), 'bsLeftLine: nothing else');
  V.Shape := bsRightLine;
  Capture(F);
  CheckEqI(20, Count(Shadow, 38, 10, 39, 30), 'bsRightLine: x = W - 2 in shadow');
  CheckEqI(20, Count(Light, 39, 10, 40, 30), 'bsRightLine: x = W - 1 in highlight');
  CheckEqI(28 * 20, Count(clAqua, 10, 10, 38, 30), 'bsRightLine: nothing else');
  V.Shape := bsSpacer;
  Capture(F);
  CheckEqI(30 * 20, Count(clAqua, 10, 10, 40, 30), 'bsSpacer: nothing drawn');
  V.Free;
  Capture(F);
  RowOk := True;
  for X := 0 to 29 do
    if not Is_(clAqua, X, 0) then
      RowOk := False;
  Check(RowOk, 'freed bevel: area repainted with the form colour');
end;

procedure StreamingTests;
var
  SF: TShapeForm;
begin
  Section('streaming');
  SF := TShapeForm.Create(nil);
  try
    CheckEqS('10,10,40,30', Format('%d,%d,%d,%d', [SF.Disc.Left, SF.Disc.Top, SF.Disc.Width, SF.Disc.Height]),
      'shape bounds');
    CheckEqB(True, SF.Disc.Brush.Color = clLime, 'Brush.Color');
    CheckEqB(True, SF.Disc.Pen.Color = clBlue, 'Pen.Color');
    CheckEqI(2, SF.Disc.Pen.Width, 'Pen.Width');
    CheckEqB(True, SF.Disc.Shape = stCircle, 'Shape stCircle');
    Check(Assigned(SF.Disc.OnMouseDown), 'OnMouseDown bound');
    CheckEqB(True, SF.Rule.Shape = bsTopLine, 'bevel Shape');
    CheckEqB(True, SF.Rule.Style = bsRaised, 'bevel Style');
    Capture(SF);
    Check(Count(clLime, 10, 10, 50, 40) > 0, 'loaded shape painted with its brush');
    CheckEqI(100, Count(clBtnHighlight, 60, 10, 160, 11), 'loaded bevel: raised top line');
    CheckEqI(100, Count(clBtnShadow, 60, 11, 160, 12), 'loaded bevel: second line');
  finally
    SF.Free;
  end;
end;

begin
  Application.Initialize;
  Bmp := TBitmap.Create;
  Bmp.SetSize(300, 200);
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 300;
    F.ClientHeight := 200;
    F.Color := clWhite;
    ShapeTests;
    F.Color := clAqua;
    BevelTests;
  finally
    F.Free;
  end;
  StreamingTests;
  Bmp.Free;
  Done;
end.
