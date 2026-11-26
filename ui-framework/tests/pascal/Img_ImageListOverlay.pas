// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1479, UIF-1480, UIF-1481
// phase: 8
// needs: desktop
// opc: --app-type gui
program Img_ImageListOverlay;
{ U-166, phase8/image-list.md section 8: Overlay sets an entry before and after the native list exists, clears
  it with -1, refuses an invalid index and a list without a mask; DrawOverlay shows the overlay's opaque pixels
  over the base cell (an unset entry draws the base only); the table follows the cells through a middle insert
  (a rebuild of the native list), an insert at the end, Move both ways, Delete of a cell an entry points at and of
  one below it, Clear and an emptying property change; disabled DrawOverlay of alpha cells draws both cells with
  the grey, halved conversion. Built as a GUI program (common controls of version 6). }
uses
  Windows, SysUtils, Classes, CommCtrl, Graphics, ImgList, TestKit, WicTestKit, ImgTestKit;

const
  White = Cardinal($00FFFFFF);
  Grey = Cardinal($FF808080);
  Magenta = Cardinal($FFFF00FF);
  Cyan = Cardinal($FF00FFFF);
  HalfMagenta = Cardinal($80800080);

{ the colour at (2, 2) (the overlay corner) and at (12, 12) (base only) of cell Index drawn with overlay Ov }
function Shown(L: TImageList; Index: Integer; Ov: TOverlay): string;
var
  S: TBitmap;
begin
  S := Surface(16, 16, White);
  try
    L.DrawOverlay(S.Canvas, 0, 0, Index, Ov);
    Result := QuadText(Quad(S, 2, 2) and $FFFFFF) + ' ' + QuadText(Quad(S, 12, 12) and $FFFFFF);
  finally
    S.Free;
  end;
end;

const
  ShowsGrey = '00808080 00808080';
  ShowsMagenta = '00FF00FF 00808080';
  ShowsCyan = '0000FFFF 00808080';

procedure AddCell(L: TImageList; Body, Corner: Cardinal);
var
  B: TBitmap;
begin
  B := CornerCell(Body, Corner);
  try
    L.Add(B, nil);
  finally
    B.Free;
  end;
end;

procedure InsertCells(L: TImageList; Index, N: Integer);
var
  B: TBitmap;
  X, Y: Integer;
begin
  B := TBitmap.Create;
  try
    B.PixelFormat := pf32bit;
    B.SetSize(16 * N, 16);
    for Y := 0 to 15 do
      for X := 0 to 16 * N - 1 do
        SetQuad(B, X, Y, Grey);
    L.Insert(Index, B, nil);
  finally
    B.Free;
  end;
end;

{ cells: 0 grey, 1 a magenta corner, 2 a cyan corner (transparent elsewhere) }
procedure Fill(L: TImageList);
begin
  AddCell(L, Grey, Grey);
  AddCell(L, 0, Magenta);
  AddCell(L, 0, Cyan);
end;

procedure CheckOverlay;
var
  L: TImageList;
  C: TChangeCounter;
begin
  Section('Overlay (UIF-1479)');
  L := TImageList.Create(nil);
  C := TChangeCounter.Create;
  try
    Fill(L);
    L.OnChange := C.Changed;
    CheckEqB(True, L.Overlay(1, 0), 'Overlay(1, 0) without a handle');
    CheckEqI(1, C.Count, 'one Change');
    CheckEqB(False, L.HandleAllocated, 'no handle made by Overlay');
    CheckEqB(True, L.Overlay(2, 14), 'Overlay(2, 14), the last entry');
    CheckEqI(2, C.Count, 'one Change each');
    CheckEqS(ShowsMagenta, Shown(L, 0, 0), 'DrawOverlay: the new native list got entry 0');
    CheckEqS(ShowsCyan, Shown(L, 0, 14), 'DrawOverlay: entry 14');
    CheckEqS(ShowsGrey, Shown(L, 0, 3), 'an unset entry draws the base only');
    CheckEqB(False, L.Overlay(3, 1), 'index = Count refused');
    CheckEqB(False, L.Overlay(-2, 1), 'index -2 refused');
    CheckEqI(2, C.Count, 'a refused call fires nothing');
    CheckEqS(ShowsGrey, Shown(L, 0, 1), 'a refused call sets nothing');
    CheckEqB(True, L.Overlay(-1, 14), 'Overlay(-1, 14) clears');
    CheckEqI(3, C.Count, 'clearing fires Change');
    CheckEqS(ShowsGrey, Shown(L, 0, 14), 'a cleared entry draws the base only');
    CheckEqB(True, L.Overlay(2, 14), 'set again with a handle');
    CheckEqS(ShowsCyan, Shown(L, 0, 14), 'the native list got it');
    CheckEqS('0000FFFF 00FFFFFF', Shown(L, 1, 14), 'an overlay over a cell with transparent pixels');
    L.OnChange := nil;

    Section('table kept right (UIF-1481)');
    InsertCells(L, 1, 2);                              { [G, G, G, M, C] }
    CheckEqI(5, L.Count, 'middle insert of two cells');
    CheckEqS(ShowsMagenta, Shown(L, 0, 0), 'after the rebuild: entry 0 raised to 3');
    CheckEqS(ShowsCyan, Shown(L, 1, 14), 'after the rebuild: entry 14 raised to 4');
    L.Move(3, 0);                                      { [M, G, G, G, C] }
    CheckEqS(ShowsMagenta, Shown(L, 1, 0), 'Move(3, 0): entry 0 follows its cell');
    CheckEqS(ShowsCyan, Shown(L, 1, 14), 'Move(3, 0): entry 14 unchanged');
    L.Move(0, 2);                                      { [G, G, M, G, C] }
    CheckEqS(ShowsMagenta, Shown(L, 0, 0), 'Move(0, 2): entry 0 follows its cell');
    L.Move(4, 1);                                      { [G, C, G, M, G] }
    CheckEqS(ShowsMagenta, Shown(L, 0, 0), 'Move(4, 1): entry 0 raised by one');
    CheckEqS(ShowsCyan, Shown(L, 0, 14), 'Move(4, 1): entry 14 follows its cell');
    L.Delete(0);                                       { [C, G, M, G] }
    CheckEqS(ShowsMagenta, Shown(L, 1, 0), 'Delete below: entry 0 lowered');
    CheckEqS(ShowsCyan, Shown(L, 1, 14), 'Delete below: entry 14 lowered');
    L.Delete(2);                                       { [C, G, G] }
    CheckEqS(ShowsGrey, Shown(L, 1, 0), 'Delete of the overlay cell clears its entry');
    CheckEqS(ShowsCyan, Shown(L, 1, 14), 'Delete above: entry 14 unchanged');
    AddCell(L, 0, Magenta);                            { [C, G, G, M] }
    CheckEqS(ShowsCyan, Shown(L, 1, 14), 'insert at the end keeps the table');
    CheckEqB(True, L.Overlay(3, 0), 'set entry 0 again');
    CheckEqS(ShowsMagenta, Shown(L, 1, 0), 'entry 0 at the new cell');
    L.Clear;
    Fill(L);
    CheckEqS(ShowsGrey, Shown(L, 0, 0), 'Clear clears entry 0');
    CheckEqS(ShowsGrey, Shown(L, 0, 14), 'Clear clears entry 14');
    CheckEqB(True, L.Overlay(1, 0), 'set before an emptying change');
    L.Height := 32;
    L.Height := 16;
    Fill(L);
    CheckEqS(ShowsGrey, Shown(L, 0, 0), 'an emptying property change clears the table');
  finally
    L.Free;
    C.Free;
  end;
end;

procedure CheckUnmasked;
var
  L: TImageList;
  C: TChangeCounter;
begin
  Section('a list without a mask');
  L := TImageList.Create(nil);
  C := TChangeCounter.Create;
  try
    L.Masked := False;
    Fill(L);
    L.OnChange := C.Changed;
    CheckEqB(False, L.Overlay(1, 0), 'Masked = False: Overlay refused');
    CheckEqI(0, C.Count, 'no Change');
    L.OnChange := nil;
    CheckEqS(ShowsGrey, Shown(L, 0, 0), 'DrawOverlay draws the base only');
  finally
    L.Free;
    C.Free;
  end;
end;

procedure CheckAlpha;
var
  L: TImageList;
  B, S: TBitmap;
begin
  Section('overlays of alpha cells (UIF-1480)');
  L := TImageList.Create(nil);
  B := FourBlocks(1, 0, False);
  try
    L.Add(B, nil);
    AddCell(L, 0, HalfMagenta);
    CheckEqB(True, L.Overlay(1, 0), 'Overlay of a half-transparent cell');
    S := Surface(20, 20, White);
    try
      L.DrawOverlay(S.Canvas, 2, 3, 0, 0);
      CheckRGB(Blend(HalfMagenta, Blend(BlockRed, White)), Quad(S, 4, 5), 2, 'enabled: overlay blended over red');
      CheckRGB(Blend(BlockBlue, White), Quad(S, 14, 7), 1, 'enabled: base only outside the overlay');
    finally
      S.Free;
    end;
    S := Surface(20, 20, White);
    try
      L.DrawOverlay(S.Canvas, 2, 3, 0, 0, False);
      CheckRGB(Blend(DisabledPixel(HalfMagenta), Blend(DisabledPixel(BlockRed), White)), Quad(S, 4, 5), 2,
        'disabled: both cells converted');
      CheckRGB(Blend(DisabledPixel(BlockBlue), White), Quad(S, 14, 7), 1, 'disabled: base only outside the overlay');
      CheckRGB(White, Quad(S, 14, 15), 0, 'disabled: transparent block');
    finally
      S.Free;
    end;
    S := Surface(20, 20, White);
    try
      L.DrawOverlay(S.Canvas, 2, 3, 0, 5, dsNormal, itImage, False);
      CheckRGB(Blend(DisabledPixel(BlockRed), White), Quad(S, 4, 5), 1, 'disabled, unset entry: base only');
    finally
      S.Free;
    end;
  finally
    L.Free;
    B.Free;
  end;
end;

begin
  CheckOverlay;
  CheckUnmasked;
  CheckAlpha;
  Done;
end.
