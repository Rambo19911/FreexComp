// covers: RTL-1140, RTL-1141, RTL-1142
// phase: 8
program Win_CommCtrlImageList;
{ ImageList_Replace, ImageList_SetOverlayImage and INDEXTOOVERLAYMASK (CommCtrl, task R-249) against the system:
  a masked 32-bit 4 x 4 image list gets two cells, cell 1 is replaced by another colour and read back by drawing it
  into a 32-bit memory bitmap; cell 0 (left half opaque red, right half transparent) becomes overlay 1 and is drawn
  over cell 1 through the overlay bits of the draw flags. The cell images have no alpha, so the masks decide.
  Needs no desktop. }
uses Windows, CommCtrl, TestKit;

type
  PPixels = ^TPixels;
  TPixels = array[0..15] of Cardinal;

const
  Red = $FF0000;     // DIB pixel order: $00RRGGBB
  Green = $00FF00;
  Blue = $0000FF;
  White = $FFFFFF;
  { monochrome masks, rows padded to 16 bits: bit 1 = transparent }
  HalfBits: array[0..7] of Byte = ($30, 0, $30, 0, $30, 0, $30, 0);
  FullBits: array[0..7] of Byte = (0, 0, 0, 0, 0, 0, 0, 0);

function MakeBitmap(DC: HDC; var Bits: Pointer): HBITMAP;
var
  Info: TBitmapInfo;
begin
  FillChar(Info, SizeOf(Info), 0);
  Info.bmiHeader.biSize := SizeOf(TBitmapInfoHeader);
  Info.bmiHeader.biWidth := 4;
  Info.bmiHeader.biHeight := -4;
  Info.bmiHeader.biPlanes := 1;
  Info.bmiHeader.biBitCount := 32;
  Info.bmiHeader.biCompression := BI_RGB;
  Bits := nil;
  Result := CreateDIBSection(DC, @Info, DIB_RGB_COLORS, Bits, 0, 0);
end;

{ a 4 x 4 cell: columns 0..1 in Left, columns 2..3 in Right }
function MakeCell(DC: HDC; Left, Right: Cardinal): HBITMAP;
var
  Bits: Pointer;
  P: PPixels;
  I: Integer;
begin
  Result := MakeBitmap(DC, Bits);
  P := PPixels(Bits);
  for I := 0 to 15 do
    if (I mod 4) < 2 then
      P^[I] := Left
    else
      P^[I] := Right;
end;

procedure Clear(P: PPixels);
var
  I: Integer;
begin
  for I := 0 to 15 do
    P^[I] := White;
end;

function Px(P: PPixels; X, Y: Integer): Integer;
begin
  Result := Integer(P^[Y * 4 + X] and $00FFFFFF);
end;

var
  DC: HDC;
  IL: HIMAGELIST;
  DstBmp, Overlay, GreenCell, BlueCell, HalfMask, FullMask: HBITMAP;
  Old: HGDIOBJ;
  DstBits: Pointer;
  Dst: PPixels;

begin
  Section('setup');
  DC := CreateCompatibleDC(0);
  DstBmp := MakeBitmap(DC, DstBits);
  Check(DstBmp <> 0, 'a 32-bit 4 x 4 target bitmap');
  Dst := PPixels(DstBits);
  Old := SelectObject(DC, DstBmp);
  Overlay := MakeCell(DC, Red, 0);
  GreenCell := MakeCell(DC, Green, Green);
  BlueCell := MakeCell(DC, Blue, Blue);
  HalfMask := CreateBitmap(4, 4, 1, 1, @HalfBits[0]);
  FullMask := CreateBitmap(4, 4, 1, 1, @FullBits[0]);
  Check((Overlay <> 0) and (GreenCell <> 0) and (BlueCell <> 0), 'three cell images');
  Check((HalfMask <> 0) and (FullMask <> 0), 'two masks');
  IL := ImageList_Create(4, 4, ILC_COLOR32 or ILC_MASK, 2, 2);
  Check(IL <> 0, 'a masked 32-bit 4 x 4 image list');
  CheckEqI(0, ImageList_Add(IL, Overlay, HalfMask), 'cell 0 added');
  CheckEqI(1, ImageList_Add(IL, GreenCell, FullMask), 'cell 1 added');

  Section('ImageList_Replace');
  Clear(Dst);
  Check(ImageList_Draw(IL, 1, DC, 0, 0, ILD_NORMAL), 'cell 1 drawn');
  GdiFlush;
  CheckEqI(Green, Px(Dst, 0, 0), 'cell 1 is green before the replace');
  Check(ImageList_Replace(IL, 1, BlueCell, FullMask), 'ImageList_Replace');
  CheckEqI(2, ImageList_GetImageCount(IL), 'the replace keeps the count');
  Clear(Dst);
  Check(ImageList_Draw(IL, 1, DC, 0, 0, ILD_NORMAL), 'the replaced cell drawn');
  GdiFlush;
  CheckEqI(Blue, Px(Dst, 0, 0), 'the replaced cell is blue (top left)');
  CheckEqI(Blue, Px(Dst, 3, 3), 'the replaced cell is blue (bottom right)');
  Clear(Dst);
  Check(ImageList_Draw(IL, 0, DC, 0, 0, ILD_NORMAL), 'cell 0 drawn');
  GdiFlush;
  CheckEqI(Red, Px(Dst, 0, 0), 'cell 0 is red where its mask is opaque');
  CheckEqI(White, Px(Dst, 3, 0), 'cell 0 is transparent where its mask is set');

  Section('ImageList_SetOverlayImage');
  CheckEqI($100, INDEXTOOVERLAYMASK(1), 'INDEXTOOVERLAYMASK(1)');
  CheckEqI(ILD_OVERLAYMASK, INDEXTOOVERLAYMASK(15), 'INDEXTOOVERLAYMASK(15) fills ILD_OVERLAYMASK');
  Check(ImageList_SetOverlayImage(IL, 0, 1), 'ImageList_SetOverlayImage');
  Clear(Dst);
  Check(ImageList_Draw(IL, 1, DC, 0, 0, ILD_NORMAL or INDEXTOOVERLAYMASK(1)), 'cell 1 drawn with overlay 1');
  GdiFlush;
  CheckEqI(Red, Px(Dst, 0, 0), 'the overlay pixels are visible (top left)');
  CheckEqI(Red, Px(Dst, 1, 3), 'the overlay pixels are visible (bottom, column 1)');
  CheckEqI(Blue, Px(Dst, 3, 0), 'cell 1 shows through the transparent part of the overlay');
  CheckEqI(Blue, Px(Dst, 2, 3), 'cell 1 shows through (bottom, column 2)');
  Clear(Dst);
  Check(ImageList_Draw(IL, 1, DC, 0, 0, ILD_NORMAL), 'cell 1 drawn without overlay bits');
  GdiFlush;
  CheckEqI(Blue, Px(Dst, 0, 0), 'no overlay without the overlay bits');

  Section('cleanup');
  Check(ImageList_Destroy(IL), 'ImageList_Destroy');
  SelectObject(DC, Old);
  Check(DeleteObject(Overlay) and DeleteObject(GreenCell) and DeleteObject(BlueCell), 'the cell images are deleted');
  Check(DeleteObject(HalfMask) and DeleteObject(FullMask), 'the masks are deleted');
  Check(DeleteObject(DstBmp) and DeleteDC(DC), 'the target is deleted');
  Done;
end.
