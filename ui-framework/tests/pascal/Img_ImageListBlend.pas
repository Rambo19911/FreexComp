// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1486
// phase: 8
// needs: desktop
// opc: --app-type gui
program Img_ImageListBlend;
{ U-167, phase8/image-list.md section 8a: on a 32-bit list dsSelected (50 %) and dsFocus (25 %) with a BlendColor
  are drawn by the framework: every pixel of an opaque cell, of a cell of alpha 128 and of a mixed cell equals the
  formula computed here composited over the background (within 1), for clRed and for clBlue, which differ; with a
  BkColor the cell rectangle is that colour under the blended pixels; clNone, itMask and a cd24Bit list still give
  the direct ImageList_DrawEx result; disabled drawing ignores the BlendColor; DrawOverlay with dsSelected blends
  the base and the overlay cell. Built as a GUI program (common controls of version 6). }
uses
  Windows, SysUtils, Classes, CommCtrl, Graphics, ImgList, TestKit, WicTestKit, ImgTestKit;

const
  White = Cardinal($00FFFFFF);
  Black = Cardinal($00000000);
  Opaque = Cardinal($FF3080C0);
  Half = Cardinal($80102040);
  Grey = Cardinal($FF808080);
  Magenta = Cardinal($FFFF00FF);
  StyleNames: array[TDrawingStyle] of string = ('dsFocus', 'dsSelected', 'dsNormal', 'dsTransparent');

{ a 16 x 16 cell of one canonical pixel }
procedure AddSolid(L: TImageList; V: Cardinal);
var
  B: TBitmap;
begin
  B := Surface(16, 16, V);
  try
    L.Add(B, nil);
  finally
    B.Free;
  end;
end;

{ cells: 0 opaque, 1 alpha 128, 2 the four blocks (opaque, 128, 1 and 0) }
function NewList: TImageList;
var
  B: TBitmap;
begin
  Result := TImageList.Create(nil);
  AddSolid(Result, Opaque);
  AddSolid(Result, Half);
  B := FourBlocks(1, 0, False);
  try
    Result.Add(B, nil);
  finally
    B.Free;
  end;
end;

function CellPixel(Index, X, Y: Integer): Cardinal;
begin
  case Index of
    0: Result := Opaque;
    1: Result := Half;
  else
    Result := BlockPixel(0, X, Y);
  end;
end;

{ the pixels of cell Index drawn at (2, 3) that miss the formula composited over Background by more than 1 }
function Misses(S: TBitmap; Index: Integer; Color: TColor; Style: TDrawingStyle; Background: Cardinal): Integer;
var
  X, Y, I, D: Integer;
  E, V: Cardinal;
begin
  Result := 0;
  for Y := 0 to 15 do
    for X := 0 to 15 do
    begin
      E := Blend(BlendedPixel(CellPixel(Index, X, Y), Color, Style), Background);
      V := Quad(S, 2 + X, 3 + Y);
      for I := 0 to 2 do
      begin
        D := Integer((E shr (I * 8)) and $FF) - Integer((V shr (I * 8)) and $FF);
        if (D > 1) or (D < -1) then
        begin
          Inc(Result);
          Break;
        end;
      end;
    end;
end;

function Drawn(L: TImageList; Index: Integer; Style: TDrawingStyle; Background: Cardinal): TBitmap;
begin
  Result := Surface(20, 20, Background);
  L.Draw(Result.Canvas, 2, 3, Index, Style, itImage);
end;

{ Draw of one style against ImageList_DrawEx with the list's colours and the flags of UIF-1475 }
function NativeDifferences(L: TImageList; Index: Integer; Style: TDrawingStyle; T: TImageType;
  Enabled: Boolean): Integer;
var
  A, B: TBitmap;
  Bk, Fg: COLORREF;
  Flags: UINT;
begin
  if L.BkColor = clNone then
    Bk := CLR_NONE
  else
    Bk := COLORREF(ColorToRGB(L.BkColor));
  if L.BlendColor = clNone then
    Fg := CLR_DEFAULT
  else
    Fg := COLORREF(ColorToRGB(L.BlendColor));
  if Style = dsSelected then
    Flags := ILD_SELECTED
  else
    Flags := ILD_FOCUS;
  if T = itMask then
    Flags := Flags or ILD_MASK;
  A := Surface(20, 20, White);
  B := Surface(20, 20, White);
  try
    L.Draw(A.Canvas, 2, 3, Index, Style, T, Enabled);
    ImageList_DrawEx(L.Handle, Index, B.Canvas.Handle, 2, 3, 0, 0, Bk, Fg, Flags);
    Result := Differences(A, B);
  finally
    A.Free;
    B.Free;
  end;
end;

procedure CheckFormula;
const
  Colors: array[0..1] of TColor = (clRed, clBlue);
  ColorNames: array[0..1] of string = ('clRed', 'clBlue');
var
  L: TImageList;
  S, T: TBitmap;
  I, C: Integer;
  St: TDrawingStyle;
begin
  Section('the blend formula (UIF-1486)');
  CheckEqS('FF984060', QuadText(BlendedPixel(Opaque, clRed, dsSelected)), 'opaque, clRed, 50 % (computed here)');
  CheckEqS('FF646090', QuadText(BlendedPixel(Opaque, clRed, dsFocus)), 'opaque, clRed, 25 % (computed here)');
  CheckEqS('80481020', QuadText(BlendedPixel(Half, clRed, dsSelected)), 'alpha 128, clRed, 50 % (computed here)');
  CheckEqS('80081060', QuadText(BlendedPixel(Half, clBlue, dsSelected)), 'alpha 128, clBlue, 50 % (computed here)');
  L := NewList;
  try
    for C := 0 to 1 do
    begin
      L.BlendColor := Colors[C];
      for St := dsFocus to dsSelected do
        for I := 0 to 2 do
        begin
          S := Drawn(L, I, St, White);
          try
            CheckEqI(0, Misses(S, I, Colors[C], St, White),
              Format('cell %d, %s, %s on white: every pixel within 1', [I, StyleNames[St], ColorNames[C]]));
            CheckRGB(White, Quad(S, 1, 1), 0,
              Format('cell %d, %s, %s: nothing outside the cell', [I, StyleNames[St], ColorNames[C]]));
          finally
            S.Free;
          end;
          S := Drawn(L, I, St, Black);
          try
            CheckEqI(0, Misses(S, I, Colors[C], St, Black),
              Format('cell %d, %s, %s on black: every pixel within 1', [I, StyleNames[St], ColorNames[C]]));
          finally
            S.Free;
          end;
        end;
    end;
    S := Drawn(L, 0, dsSelected, White);
    CheckRGB(BlendedPixel(Opaque, clBlue, dsSelected), Quad(S, 9, 9), 1, 'opaque cell, dsSelected, clBlue: the pixel');
    S.Free;
    S := Drawn(L, 1, dsFocus, White);
    CheckRGB(Blend(BlendedPixel(Half, clBlue, dsFocus), White), Quad(S, 9, 9), 1,
      'alpha 128 cell, dsFocus, clBlue: the pixel over white');
    S.Free;

    Section('the colour matters (UIF-1486)');
    for St := dsFocus to dsSelected do
      for I := 0 to 1 do
      begin
        L.BlendColor := clRed;
        S := Drawn(L, I, St, White);
        L.BlendColor := clBlue;
        T := Drawn(L, I, St, White);
        try
          Check(Differences(S, T) > 0, Format('cell %d, %s: clRed differs from clBlue', [I, StyleNames[St]]));
        finally
          S.Free;
          T.Free;
        end;
      end;
    L.BlendColor := clRed;
    S := Drawn(L, 0, dsSelected, White);
    T := Drawn(L, 0, dsFocus, White);
    try
      Check(Differences(S, T) > 0, 'dsSelected differs from dsFocus');
    finally
      S.Free;
      T.Free;
    end;

    Section('the short Draw and the other cases');
    L.DrawingStyle := dsSelected;
    S := Surface(20, 20, White);
    try
      L.Draw(S.Canvas, 2, 3, 2);
      CheckEqI(0, Misses(S, 2, clRed, dsSelected, White), 'short Draw with DrawingStyle dsSelected: the formula');
    finally
      S.Free;
    end;
    L.DrawingStyle := dsNormal;
    CheckEqI(0, NativeDifferences(L, 2, dsSelected, itMask, True), 'itMask, dsSelected, clRed: the native call');
    S := Surface(20, 20, White);
    T := Surface(20, 20, White);
    try
      L.Draw(S.Canvas, 2, 3, 2, dsSelected, itImage, False);
      L.BlendColor := clNone;
      L.Draw(T.Canvas, 2, 3, 2, dsSelected, itImage, False);
      CheckEqI(0, Differences(S, T), 'disabled: BlendColor ignored (UIF-1477)');
    finally
      S.Free;
      T.Free;
    end;
    for St := dsFocus to dsSelected do
      for I := 0 to 2 do
      begin
        L.BkColor := clNone;
        CheckEqI(0, NativeDifferences(L, I, St, itImage, True),
          Format('clNone: cell %d, %s equals ImageList_DrawEx with CLR_DEFAULT', [I, StyleNames[St]]));
        L.BkColor := clYellow;
        CheckEqI(0, NativeDifferences(L, I, St, itImage, True),
          Format('clNone, BkColor clYellow: cell %d, %s equals the native call', [I, StyleNames[St]]));
      end;
  finally
    L.Free;
  end;
end;

procedure CheckBkColor;
var
  L: TImageList;
  S: TBitmap;
  St: TDrawingStyle;
  Yellow: Cardinal;
begin
  Section('BkColor under the blended cell (UIF-1486)');
  Yellow := RGBOf(clYellow);
  L := NewList;
  try
    L.BlendColor := clRed;
    L.BkColor := clYellow;
    for St := dsFocus to dsSelected do
    begin
      S := Drawn(L, 2, St, White);
      try
        CheckEqI(0, Misses(S, 2, clRed, St, Yellow), StyleNames[St] + ': every pixel within 1 over the BkColor');
        CheckRGB(Yellow, Quad(S, 2 + 12, 3 + 12), 0, StyleNames[St] + ': the transparent block shows the BkColor');
        CheckRGB(Blend(BlendedPixel(BlockBlue, clRed, St), Yellow), Quad(S, 2 + 12, 3 + 4), 1,
          StyleNames[St] + ': the alpha 128 block blended over the BkColor');
        CheckRGB(White, Quad(S, 1, 1), 0, StyleNames[St] + ': nothing outside the cell');
      finally
        S.Free;
      end;
    end;
  finally
    L.Free;
  end;
end;

procedure Check24Bit;
var
  L: TImageList;
  St: TDrawingStyle;
  C: Integer;
const
  Colors: array[0..1] of TColor = (clRed, clBlue);
  ColorNames: array[0..1] of string = ('clRed', 'clBlue');
begin
  Section('a cd24Bit list keeps the native call (UIF-1475)');
  L := TImageList.Create(nil);
  try
    L.ColorDepth := cd24Bit;
    AddSolid(L, Opaque);
    for C := 0 to 1 do
    begin
      L.BlendColor := Colors[C];
      for St := dsFocus to dsSelected do
        CheckEqI(0, NativeDifferences(L, 0, St, itImage, True),
          Format('cd24Bit, %s, %s equals ImageList_DrawEx', [StyleNames[St], ColorNames[C]]));
    end;
  finally
    L.Free;
  end;
end;

procedure CheckOverlay;
var
  L: TImageList;
  S, T: TBitmap;
  X, Y, I, D, Miss: Integer;
  E, V, Ov: Cardinal;
begin
  Section('DrawOverlay with dsSelected (UIF-1486)');
  L := TImageList.Create(nil);
  try
    AddSolid(L, Grey);
    T := CornerCell(0, Magenta);
    try
      L.Add(T, nil);
    finally
      T.Free;
    end;
    CheckEqB(True, L.Overlay(1, 0), 'Overlay(1, 0)');
    L.BlendColor := clRed;
    S := Surface(20, 20, White);
    T := Surface(20, 20, White);
    try
      L.DrawOverlay(S.Canvas, 2, 3, 0, 0, dsSelected, itImage);
      CheckRGB(BlendedPixel(Magenta, clRed, dsSelected), Quad(S, 2 + 2, 3 + 2), 1, 'the overlay pixel blended');
      CheckRGB(BlendedPixel(Grey, clRed, dsSelected), Quad(S, 2 + 12, 3 + 12), 1, 'the base pixel blended');
      Miss := 0;
      for Y := 0 to 15 do
        for X := 0 to 15 do
        begin
          if (X < 6) and (Y < 6) then
            Ov := Magenta
          else
            Ov := 0;
          E := Blend(BlendedPixel(Ov, clRed, dsSelected), Blend(BlendedPixel(Grey, clRed, dsSelected), White));
          V := Quad(S, 2 + X, 3 + Y);
          for I := 0 to 2 do
          begin
            D := Integer((E shr (I * 8)) and $FF) - Integer((V shr (I * 8)) and $FF);
            if (D > 2) or (D < -2) then
            begin
              Inc(Miss);
              Break;
            end;
          end;
        end;
      CheckEqI(0, Miss, 'every pixel: overlay over base, both blended (within 2)');
      L.BlendColor := clBlue;
      L.DrawOverlay(T.Canvas, 2, 3, 0, 0, dsSelected, itImage);
      Check(Differences(S, T) > 0, 'DrawOverlay: clRed differs from clBlue');
    finally
      S.Free;
      T.Free;
    end;
  finally
    L.Free;
  end;
end;

begin
  CheckFormula;
  CheckBkColor;
  Check24Bit;
  CheckOverlay;
  Done;
end.
