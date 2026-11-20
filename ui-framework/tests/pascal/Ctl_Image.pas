// covers: UIF-740
// needs: desktop
program Ctl_Image;
{ U-066, controls-standard.md section 14: TImage defaults (105 x 105, no AutoSize, Center, Proportional, Stretch,
  Transparent); the destination rectangle rule UIF-740 checked through the pixels of the form captured with
  WM_PRINTCLIENT into a memory bitmap (plain, Center, Stretch, Proportional with and without Stretch, wide and tall
  pictures, a picture larger than the control is clipped, an empty picture draws nothing); Transparent sets the
  graphic's Transparent (also for a graphic assigned later); AutoSize takes the picture size (layout.md UIF-255),
  follows picture changes, keeps the size for an empty picture and never measures in dump mode; streaming with
  Picture.Data keeps the stored bounds (UIF-260) and draws the loaded picture. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, ExtCtrls, TestKit;

type
  TImageForm = class(TForm)
  published
    Photo: TImage;
    procedure PhotoClick(Sender: TObject);
  end;

{$FORM Ctl_Image.form}

var
  Bmp: TBitmap;

procedure TImageForm.PhotoClick(Sender: TObject);
begin
end;

procedure Capture(F: TForm);
begin
  Bmp.Canvas.Brush.Color := clBlack;
  Bmp.Canvas.FillRect(Rect(0, 0, Bmp.Width, Bmp.Height));
  SendMessageW(F.Handle, WM_PRINTCLIENT, WPARAM(Bmp.Canvas.Handle), PRF_CLIENT or PRF_ERASEBKGND);
end;

{ the bounding box 'L,T,R,B' (R, B exclusive) of the pixels of colour C in the capture, 'none' without any }
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

function Count(C: TColor): Integer;
var
  X, Y: Integer;
begin
  Result := 0;
  C := ColorToRGB(C);
  for Y := 0 to Bmp.Height - 1 do
    for X := 0 to Bmp.Width - 1 do
      if Bmp.Canvas.Pixels[X, Y] = C then
        Inc(Result);
end;

{ a solid picture of W x H in colour C }
procedure SetPicture(I: TImage; W, H: Integer; C: TColor);
var
  P: TBitmap;
begin
  P := TBitmap.Create;
  try
    P.SetSize(W, H);
    P.Canvas.Brush.Color := C;
    P.Canvas.FillRect(Rect(0, 0, W, H));
    I.Picture.Assign(P);
  finally
    P.Free;
  end;
end;

function BoundsStr(C: TControl): string;
begin
  Result := Format('%d,%d,%d,%d', [C.Left, C.Top, C.Width, C.Height]);
end;

procedure Run;
var
  F: TForm;
  I: TImage;
  P: TBitmap;
  IF_: TImageForm;
begin
  Application.Initialize;
  Bmp := TBitmap.Create;
  Bmp.SetSize(300, 200);
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 300;
    F.ClientHeight := 200;
    F.Color := clWhite;
    I := TImage.Create(F);

    Section('defaults');
    CheckEqS('105x105', Format('%dx%d', [I.Width, I.Height]), 'size 105 x 105');
    CheckEqB(False, I.AutoSize, 'AutoSize False');
    CheckEqB(False, I.Center, 'Center False');
    CheckEqB(False, I.Proportional, 'Proportional False');
    CheckEqB(False, I.Stretch, 'Stretch False');
    CheckEqB(False, I.Transparent, 'Transparent False');
    Check(I.Picture <> nil, 'Picture object exists');
    Check(I.Picture.Graphic = nil, 'Picture empty');
    Check(GetClass('TImage') <> nil, 'registered');

    Section('destination rectangle (UIF-740)');
    I.SetBounds(10, 10, 100, 60);
    I.Parent := F;
    Capture(F);
    CheckEqS('none', Box(clRed), 'empty picture: nothing drawn');
    SetPicture(I, 20, 10, clRed);
    CheckEqS('10,10,100,60', BoundsStr(I), 'AutoSize False: bounds kept');
    Capture(F);
    CheckEqS('10,10,30,20', Box(clRed), 'plain: picture size at the top-left');
    CheckEqI(200, Count(clRed), 'plain: 20 x 10 pixels');
    I.Center := True;
    Capture(F);
    CheckEqS('50,35,70,45', Box(clRed), 'Center: (W - w) div 2, (H - h) div 2');
    I.Center := False;
    I.Stretch := True;
    Capture(F);
    CheckEqS('10,10,110,70', Box(clRed), 'Stretch: the whole control');
    I.Proportional := True;
    Capture(F);
    CheckEqS('10,10,110,60', Box(clRed), 'Stretch + Proportional, wide picture: w = W, h = MulDiv(ph, W, pw)');
    I.Center := True;
    Capture(F);
    CheckEqS('10,15,110,65', Box(clRed), 'Stretch + Proportional + Center');
    SetPicture(I, 10, 40, clRed);
    Capture(F);
    CheckEqS('52,10,67,70', Box(clRed), 'tall picture: h = H, w = MulDiv(pw, H, ph), centred');
    I.Center := False;
    Capture(F);
    CheckEqS('10,10,25,70', Box(clRed), 'tall picture at the left');
    I.Stretch := False;
    SetPicture(I, 20, 10, clRed);
    Capture(F);
    CheckEqS('10,10,30,20', Box(clRed), 'Proportional, picture fits: not enlarged');
    SetPicture(I, 200, 50, clRed);
    Capture(F);
    CheckEqS('10,10,110,35', Box(clRed), 'Proportional, picture larger: shrunk to fit');
    I.Proportional := False;
    SetPicture(I, 200, 100, clRed);
    Capture(F);
    CheckEqS('10,10,110,70', Box(clRed), 'larger picture, no Stretch: clipped to the control');
    I.Picture.Graphic := nil;
    Capture(F);
    CheckEqS('none', Box(clRed), 'picture removed: nothing drawn');

    Section('Transparent');
    P := TBitmap.Create;
    try
      P.SetSize(20, 10);
      P.Canvas.Brush.Color := clLime;
      P.Canvas.FillRect(Rect(0, 0, 10, 10));
      P.Canvas.Brush.Color := clRed;
      P.Canvas.FillRect(Rect(10, 0, 20, 10));
      I.Picture.Assign(P);
      Capture(F);
      CheckEqI(100, Count(clLime), 'opaque: both halves drawn');
      I.Transparent := True;
      CheckEqB(True, I.Picture.Graphic.Transparent, 'sets the graphic''s Transparent');
      Capture(F);
      CheckEqI(0, Count(clLime), 'transparent: the bottom-left colour is left out');
      CheckEqS('20,10,30,20', Box(clRed), 'transparent: the other pixels drawn');
      I.Picture.Assign(P);
      CheckEqB(True, I.Picture.Graphic.Transparent, 'a new graphic takes Transparent');
      I.Transparent := False;
      CheckEqB(False, I.Picture.Graphic.Transparent, 'Transparent False reaches the graphic');
    finally
      P.Free;
    end;

    Section('AutoSize (UIF-255)');
    SetPicture(I, 20, 10, clRed);
    I.AutoSize := True;
    CheckEqS('10,10,20,10', BoundsStr(I), 'AutoSize True: the picture size');
    I.Picture.Bitmap.SetSize(30, 15);
    CheckEqS('10,10,30,15', BoundsStr(I), 'picture size change follows');
    I.Picture.Graphic := nil;
    CheckEqS('10,10,30,15', BoundsStr(I), 'empty picture: no change');
    LayoutDumpMode := True;
    try
      SetPicture(I, 40, 5, clRed);
      CheckEqS('10,10,30,15', BoundsStr(I), 'dump mode: no change (UIF-263)');
    finally
      LayoutDumpMode := False;
    end;
    I.AutoSize := False;
    I.AutoSize := True;
    CheckEqS('10,10,40,5', BoundsStr(I), 'outside dump mode again: the picture size');
  finally
    F.Free;
  end;

  Section('streaming');
  IF_ := TImageForm.Create(nil);
  try
    CheckEqS('10,10,50,40', BoundsStr(IF_.Photo), 'stored bounds win over AutoSize (UIF-260)');
    CheckEqB(True, IF_.Photo.AutoSize, 'AutoSize');
    CheckEqB(True, IF_.Photo.Center, 'Center');
    CheckEqB(True, IF_.Photo.Proportional, 'Proportional');
    CheckEqB(True, IF_.Photo.Stretch, 'Stretch');
    CheckEqB(False, IF_.Photo.Transparent, 'Transparent');
    Check(IF_.Photo.Picture.Graphic is TBitmap, 'Picture.Data read');
    CheckEqS('3x2', Format('%dx%d', [IF_.Photo.Picture.Width, IF_.Photo.Picture.Height]), 'picture size');
    Check(Assigned(IF_.Photo.OnClick), 'OnClick bound');
    Capture(IF_);
    CheckEqS('10,13,60,46', Box(clRed), 'loaded picture: Stretch + Proportional + Center');
    IF_.Photo.Picture.Bitmap.SetSize(4, 4);
    CheckEqS('10,10,4,4', BoundsStr(IF_.Photo), 'a picture change after loading auto-sizes');
  finally
    IF_.Free;
    Bmp.Free;
  end;
end;

begin
  Run;
  Done;
end.
