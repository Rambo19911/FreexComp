// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1346
// phase: 8
program Gfx_WicImageDraw;
{ U-148, wic-image.md section 2: drawing a TWICImage. The image with alpha is blended over a white and over a
  black 32-bit bitmap: opaque blocks unchanged, the half-transparent block equals the source-over blend computed
  here (within 1 per channel), the transparent block leaves the background; a stretched draw fills its rectangle
  and nothing else; Transparent has no influence. An image without alpha is copied like an opaque bitmap, with
  the canvas' CopyMode. An empty image and an empty rectangle draw nothing. Through Canvas.Draw and StretchDraw;
  memory bitmaps only, no desktop. }
uses
  Windows, Types, SysUtils, Classes, Graphics, TestKit, WicTestKit;

{ a colour as $00BBGGRR from red, green, blue }
function Colour(R, G, B: Integer): TColor;
begin
  Result := TColor(R or (G shl 8) or (B shl 16));
end;

{ source-over of a premultiplied channel S with alpha A over D }
function Over(S, A, D: Integer): Integer;
begin
  Result := S + (D * (255 - A) + 127) div 255;
end;

procedure CheckColour(Expected, Actual: TColor; Tolerance: Integer; const Name: string);
begin
  CheckQuad(Cardinal(Expected), Cardinal(Actual), Tolerance, Name);
end;

function NewCanvasBitmap(W, H: Integer; Background: TColor): TBitmap;
begin
  Result := TBitmap.Create;
  Result.PixelFormat := pf32bit;
  Result.SetSize(W, H);
  Result.Canvas.Brush.Color := Background;
  Result.Canvas.FillRect(Rect(0, 0, W, H));
end;

procedure CheckBlend(Background: TColor; const Name: string);
var
  Image: TWICImage;
  Dest: TBitmap;
  D: Integer;
  Half: TColor;
begin
  Section('the image with alpha over ' + Name + ' (UIF-1346)');
  D := Integer(Background) and $FF;                        { white or black: the three channels are equal }
  Half := Colour(Over(0, 128, D), Over(0, 128, D), Over(128, 128, D));
  Image := TWICImage.Create;
  Dest := NewCanvasBitmap(40, 30, Background);
  try
    Image.LoadFromFile(Fixture('alpha.png'));
    Dest.Canvas.Draw(2, 3, Image);
    CheckColour(clRed, Dest.Canvas.Pixels[6, 6], 0, 'the opaque red block');
    CheckColour(clLime, Dest.Canvas.Pixels[14, 6], 0, 'the opaque green block');
    CheckColour(Half, Dest.Canvas.Pixels[6, 12], 1, 'the half-transparent block is blended');
    CheckColour(Background, Dest.Canvas.Pixels[14, 12], 0, 'the transparent block leaves the background');
    CheckColour(clRed, Dest.Canvas.Pixels[2, 3], 0, 'the first pixel of the image');
    CheckColour(Background, Dest.Canvas.Pixels[1, 3], 0, 'left of the image');
    CheckColour(Background, Dest.Canvas.Pixels[2, 2], 0, 'above the image');
    CheckColour(clLime, Dest.Canvas.Pixels[17, 3], 0, 'the last pixel of the first row');
    CheckColour(Background, Dest.Canvas.Pixels[18, 3], 0, 'right of the image');
    CheckColour(Half, Dest.Canvas.Pixels[2, 14], 1, 'the last row');
    CheckColour(Background, Dest.Canvas.Pixels[2, 15], 0, 'below the image');

    Dest.Canvas.FillRect(Rect(0, 0, 40, 30));
    Image.Transparent := True;
    Dest.Canvas.Draw(2, 3, Image);
    CheckColour(clRed, Dest.Canvas.Pixels[6, 6], 0, 'Transparent = True: the red block');
    CheckColour(Half, Dest.Canvas.Pixels[6, 12], 1, 'Transparent = True: the blend is the same');
    CheckColour(Background, Dest.Canvas.Pixels[14, 12], 0, 'Transparent = True: the transparent block');
    Image.Transparent := False;

    Dest.Canvas.FillRect(Rect(0, 0, 40, 30));
    Dest.Canvas.StretchDraw(Rect(4, 2, 36, 26), Image);
    CheckColour(clRed, Dest.Canvas.Pixels[12, 8], 0, 'stretched: the red block');
    CheckColour(clLime, Dest.Canvas.Pixels[28, 8], 0, 'stretched: the green block');
    CheckColour(Half, Dest.Canvas.Pixels[12, 20], 1, 'stretched: the half-transparent block');
    CheckColour(Background, Dest.Canvas.Pixels[28, 20], 0, 'stretched: the transparent block');
    CheckColour(clRed, Dest.Canvas.Pixels[4, 2], 0, 'stretched: the first pixel of the rectangle');
    CheckColour(clLime, Dest.Canvas.Pixels[35, 2], 0, 'stretched: the last pixel of the first row');
    CheckColour(Half, Dest.Canvas.Pixels[4, 25], 1, 'stretched: the last row');
    CheckColour(Background, Dest.Canvas.Pixels[3, 2], 0, 'stretched: left of the rectangle');
    CheckColour(Background, Dest.Canvas.Pixels[36, 2], 0, 'stretched: right of the rectangle');
    CheckColour(Background, Dest.Canvas.Pixels[4, 1], 0, 'stretched: above the rectangle');
    CheckColour(Background, Dest.Canvas.Pixels[4, 26], 0, 'stretched: below the rectangle');
  finally
    Dest.Free;
    Image.Free;
  end;
end;

procedure CheckOpaque;
var
  Image: TWICImage;
  Dest: TBitmap;
begin
  Section('an image without alpha is copied like a bitmap (UIF-1346)');
  Image := TWICImage.Create;
  Dest := NewCanvasBitmap(40, 30, clWhite);
  try
    Image.LoadFromFile(Fixture('plain.bmp'));
    CheckEqB(False, Image.HasAlpha, 'HasAlpha');
    Dest.Canvas.Draw(2, 3, Image);
    CheckColour(clRed, Dest.Canvas.Pixels[6, 6], 0, 'the red block');
    CheckColour(clLime, Dest.Canvas.Pixels[14, 6], 0, 'the green block');
    CheckColour(clBlue, Dest.Canvas.Pixels[6, 12], 0, 'the blue block');
    CheckColour(clYellow, Dest.Canvas.Pixels[14, 12], 0, 'the yellow block');
    CheckColour(clWhite, Dest.Canvas.Pixels[18, 15], 0, 'outside the image');
    Dest.Canvas.FillRect(Rect(0, 0, 40, 30));
    Dest.Canvas.StretchDraw(Rect(4, 2, 36, 26), Image);
    CheckColour(clRed, Dest.Canvas.Pixels[4, 2], 0, 'stretched: the first pixel');
    CheckColour(clLime, Dest.Canvas.Pixels[35, 2], 0, 'stretched: the last pixel of the first row');
    CheckColour(clBlue, Dest.Canvas.Pixels[4, 25], 0, 'stretched: the first pixel of the last row');
    CheckColour(clYellow, Dest.Canvas.Pixels[35, 25], 0, 'stretched: the last pixel');
    CheckColour(clWhite, Dest.Canvas.Pixels[36, 26], 0, 'stretched: outside the rectangle');
    Dest.Canvas.FillRect(Rect(0, 0, 40, 30));
    Dest.Canvas.CopyMode := cmNotSrcCopy;
    Dest.Canvas.Draw(2, 3, Image);
    CheckColour(Colour(0, 255, 255), Dest.Canvas.Pixels[6, 6], 0, 'the canvas'' CopyMode applies: red is inverted');
    CheckColour(Colour(0, 0, 255), Dest.Canvas.Pixels[14, 12], 0, 'the canvas'' CopyMode applies: yellow is inverted');
    Dest.Canvas.CopyMode := cmSrcCopy;
  finally
    Dest.Free;
    Image.Free;
  end;
end;

procedure CheckNothing;
var
  Image, Blank: TWICImage;
  Dest: TBitmap;
  X, Y, Changed: Integer;
begin
  Section('an empty image and an empty rectangle draw nothing (UIF-1346)');
  Image := TWICImage.Create;
  Blank := TWICImage.Create;
  Dest := NewCanvasBitmap(20, 16, clWhite);
  try
    Image.LoadFromFile(Fixture('alpha.png'));
    Dest.Canvas.Draw(0, 0, Blank);
    Dest.Canvas.StretchDraw(Rect(0, 0, 20, 16), Blank);
    Dest.Canvas.StretchDraw(Rect(5, 5, 5, 12), Image);
    Dest.Canvas.StretchDraw(Rect(5, 5, 12, 5), Image);
    Dest.Canvas.StretchDraw(Rect(12, 12, 5, 5), Image);
    Changed := 0;
    for Y := 0 to 15 do
      for X := 0 to 19 do
        if Dest.Canvas.Pixels[X, Y] <> clWhite then
          Inc(Changed);
    CheckEqI(0, Changed, 'no pixel of the destination changed');
  finally
    Dest.Free;
    Blank.Free;
    Image.Free;
  end;
end;

begin
  CheckBlend(clWhite, 'white');
  CheckBlend(clBlack, 'black');
  CheckOpaque;
  CheckNothing;
  Done;
end.
