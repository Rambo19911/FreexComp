// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1358
// phase: 8
program Gfx_WicImageScale;
{ U-150, wic-image.md section 4: ScaleInto. The image with alpha scaled to four times its size and to half its
  size keeps the colours of its blocks at their centres (premultiplied, with alpha) and the destination bitmap is
  32-bit of the size asked for; the same size gives a pixel-identical copy; an image that was assigned from a
  bitmap scales too; the image itself is unchanged and the COM bracket leaves the thread as it found it; sizes
  below 1 and an empty image raise and leave the destination alone. Needs no desktop. }
uses
  Windows, ActiveX, Types, SysUtils, Classes, Graphics, TestKit, WicTestKit;

procedure CheckCentres(B: TBitmap; TL, TR, BL, BR: Cardinal; const Name: string);
var
  W, H: Integer;
begin
  W := B.Width;
  H := B.Height;
  CheckQuad(TL, Quad(B, W div 4, H div 4), 0, Name + ': top left block');
  CheckQuad(TR, Quad(B, 3 * W div 4, H div 4), 0, Name + ': top right block');
  CheckQuad(BL, Quad(B, W div 4, 3 * H div 4), 0, Name + ': bottom left block');
  CheckQuad(BR, Quad(B, 3 * W div 4, 3 * H div 4), 0, Name + ': bottom right block');
end;

procedure CheckScale;
var
  Image, FromBitmap: TWICImage;
  Dest, Same, Source: TBitmap;
  Counter: TChangeCounter;
  X, Y, Different: Integer;
  HR: HResult;
begin
  Section('scaling (UIF-1358)');
  Image := TWICImage.Create;
  FromBitmap := TWICImage.Create;
  Dest := TBitmap.Create;
  Same := TBitmap.Create;
  Source := TBitmap.Create;
  Counter := TChangeCounter.Create;
  try
    Image.LoadFromFile(Fixture('alpha.png'));
    Image.OnChange := Counter.Changed;
    Dest.PixelFormat := pf24bit;
    Dest.SetSize(3, 3);
    Image.ScaleInto(Dest, 64, 48);
    Check(Dest.PixelFormat = pf32bit, 'four times: the destination is 32-bit');
    CheckEqI(64, Dest.Width, 'four times: Width');
    CheckEqI(48, Dest.Height, 'four times: Height');
    CheckCentres(Dest, $FFFF0000, $FF00FF00, $80000080, $00000000, 'four times');
    CheckQuad($FFFF0000, Quad(Dest, 0, 0), 0, 'four times: the first pixel');
    CheckQuad($00000000, Quad(Dest, 63, 47), 0, 'four times: the last pixel');

    Image.ScaleInto(Dest, 8, 6);
    CheckEqI(8, Dest.Width, 'half: Width');
    CheckEqI(6, Dest.Height, 'half: Height');
    CheckCentres(Dest, $FFFF0000, $FF00FF00, $80000080, $00000000, 'half');

    Image.ScaleInto(Dest, 40, 4);
    CheckEqI(40, Dest.Width, 'another shape: Width');
    CheckEqI(4, Dest.Height, 'another shape: Height');
    CheckCentres(Dest, $FFFF0000, $FF00FF00, $80000080, $00000000, 'another shape');

    Image.ScaleInto(Dest, 16, 12);
    Same.Assign(Image);
    Different := 0;
    for Y := 0 to 11 do
      for X := 0 to 15 do
        if Quad(Dest, X, Y) <> Quad(Same, X, Y) then
          Inc(Different);
    CheckEqI(16, Dest.Width, 'the same size: Width');
    CheckEqI(12, Dest.Height, 'the same size: Height');
    CheckEqI(0, Different, 'the same size: every pixel is identical');

    CheckEqI(0, Counter.Count, 'the image fires no OnChange');
    CheckEqI(16, Image.Width, 'the image keeps its width');
    CheckEqI(12, Image.Height, 'the image keeps its height');
    CheckBlocks(Image, $FFFF0000, $FF00FF00, $80000080, $00000000, 0, 'the image keeps its pixels');

    HR := CoInitializeEx(nil, COINIT_MULTITHREADED);
    CheckEqI(S_OK, HR, 'after scaling the thread is not initialised (UIF-1341)');
    if Succeeded(HR) then
      CoUninitialize;

    Source.SetSize(4, 4);
    Source.Canvas.Brush.Color := clRed;
    Source.Canvas.FillRect(Rect(0, 0, 2, 4));
    Source.Canvas.Brush.Color := clBlue;
    Source.Canvas.FillRect(Rect(2, 0, 4, 4));
    FromBitmap.Assign(Source);
    FromBitmap.ScaleInto(Dest, 16, 16);
    CheckEqI(16, Dest.Width, 'an image assigned from a bitmap: Width');
    CheckQuad($FFFF0000, Quad(Dest, 3, 8), 0, 'an image assigned from a bitmap: the left half');
    CheckQuad($FF0000FF, Quad(Dest, 12, 8), 0, 'an image assigned from a bitmap: the right half');
  finally
    Counter.Free;
    Source.Free;
    Same.Free;
    Dest.Free;
    FromBitmap.Free;
    Image.Free;
  end;
end;

procedure CheckErrors;
var
  Image: TWICImage;
  Dest: TBitmap;

  procedure Raises(W, H: Integer; const Name: string);
  begin
    try
      Image.ScaleInto(Dest, W, H);
      Check(False, Name);
    except
      on E: Exception do
        CheckRaises(Name, E.ClassName, E.Message);
    end;
  end;

begin
  Section('errors (UIF-1358)');
  Image := TWICImage.Create;
  Dest := TBitmap.Create;
  try
    Dest.SetSize(7, 5);
    Expect('EInvalidGraphicOperation', 'The image is empty');
    Raises(8, 8, 'an empty image raises');
    Image.LoadFromFile(Fixture('alpha.png'));
    Expect('EInvalidGraphicOperation', 'Invalid bitmap size');
    Raises(0, 8, 'a width of 0 raises');
    Raises(8, 0, 'a height of 0 raises');
    Raises(-1, 8, 'a negative width raises');
    Raises(8, -3, 'a negative height raises');
    Image.Assign(nil);
    Raises(0, 0, 'the size is checked before the image');
    CheckEqI(7, Dest.Width, 'the destination keeps its width');
    CheckEqI(5, Dest.Height, 'the destination keeps its height');
  finally
    Dest.Free;
    Image.Free;
  end;
end;

begin
  CheckScale;
  CheckErrors;
  Done;
end.
