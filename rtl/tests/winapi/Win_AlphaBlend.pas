// covers: RTL-1035
// phase: 8
program Win_AlphaBlend;
{ GdiAlphaBlend (gdi32) with BLENDFUNCTION: a premultiplied half-transparent red block blended onto a white 32-bit
  bitmap; the same block copied without per-pixel alpha; a fully transparent constant alpha. Needs no desktop. }
uses Windows, TestKit;

type
  PPixels = ^TPixels;
  TPixels = array[0..15] of Cardinal;

function MakeBitmap(DC: HDC; Width, Height: Integer; var Bits: Pointer): HBITMAP;
var
  Info: TBitmapInfo;
begin
  FillChar(Info, SizeOf(Info), 0);
  Info.bmiHeader.biSize := SizeOf(TBitmapInfoHeader);
  Info.bmiHeader.biWidth := Width;
  Info.bmiHeader.biHeight := -Height;
  Info.bmiHeader.biPlanes := 1;
  Info.bmiHeader.biBitCount := 32;
  Info.bmiHeader.biCompression := BI_RGB;
  Bits := nil;
  Result := CreateDIBSection(DC, @Info, DIB_RGB_COLORS, Bits, 0, 0);
end;

{ the colour channels of a pixel (the alpha byte is not compared) }
function Rgb(Pixel: Cardinal): Integer;
begin
  Result := Integer(Pixel and $00FFFFFF);
end;

var
  DstDC, SrcDC: HDC;
  DstBmp, SrcBmp: HBITMAP;
  OldDst, OldSrc: HGDIOBJ;
  DstBits, SrcBits: Pointer;
  Dst, Src: PPixels;
  Fn: TBlendFunction;
  I: Integer;
begin
  Section('GdiAlphaBlend (RTL-1035)');
  DstDC := CreateCompatibleDC(0);
  SrcDC := CreateCompatibleDC(0);
  DstBmp := MakeBitmap(DstDC, 4, 4, DstBits);
  SrcBmp := MakeBitmap(SrcDC, 2, 2, SrcBits);
  Check((DstBmp <> 0) and (SrcBmp <> 0), 'two 32-bit bitmaps');
  OldDst := SelectObject(DstDC, DstBmp);
  OldSrc := SelectObject(SrcDC, SrcBmp);
  Dst := PPixels(DstBits);
  Src := PPixels(SrcBits);
  for I := 0 to 15 do
    Dst^[I] := $FFFFFFFF;
  for I := 0 to 3 do
    Src^[I] := $80800000;          // alpha 128, red 128 (premultiplied), green 0, blue 0

  Fn.BlendOp := AC_SRC_OVER;
  Fn.BlendFlags := 0;
  Fn.SourceConstantAlpha := 255;
  Fn.AlphaFormat := AC_SRC_ALPHA;
  Check(GdiAlphaBlend(DstDC, 1, 1, 2, 2, SrcDC, 0, 0, 2, 2, Fn), 'blend with per-pixel alpha');
  GdiFlush;
  CheckEqI($FF7F7F, Rgb(Dst^[5]), 'a blended pixel: red 255, green 127, blue 127');
  CheckEqI($FF7F7F, Rgb(Dst^[10]), 'the opposite corner of the block');
  CheckEqI($FFFFFF, Rgb(Dst^[0]), 'a pixel outside the block stays white');
  CheckEqI($FFFFFF, Rgb(Dst^[11]), 'the pixel right of the block stays white');

  Fn.AlphaFormat := 0;
  Check(GdiAlphaBlend(DstDC, 0, 0, 1, 1, SrcDC, 0, 0, 1, 1, Fn), 'blend without per-pixel alpha');
  GdiFlush;
  CheckEqI($800000, Rgb(Dst^[0]), 'the source colour replaces the destination');
  CheckEqI($FFFFFF, Rgb(Dst^[1]), 'the pixel right of it stays white');

  Fn.SourceConstantAlpha := 0;
  Check(GdiAlphaBlend(DstDC, 2, 2, 2, 2, SrcDC, 0, 0, 2, 2, Fn), 'blend with a constant alpha of 0');
  GdiFlush;
  CheckEqI($FFFFFF, Rgb(Dst^[15]), 'a transparent source changes nothing');
  CheckEqI($FF7F7F, Rgb(Dst^[10]), 'the earlier result is kept');

  SelectObject(DstDC, OldDst);
  SelectObject(SrcDC, OldSrc);
  Check(DeleteObject(DstBmp) and DeleteObject(SrcBmp), 'the bitmaps are deleted');
  Check(DeleteDC(DstDC) and DeleteDC(SrcDC), 'the device contexts are deleted');
  Done;
end.
