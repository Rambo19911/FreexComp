// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1475, UIF-1476, UIF-1477
// phase: 8
// needs: desktop
// opc: --app-type gui
program Img_ImageListDraw;
{ U-166, phase8/image-list.md section 6: every drawing style and image type of the Draw overload maps to its
  ImageList_DrawEx flags, background and foreground colour (each result compared with a direct ImageList_DrawEx
  call into a second bitmap), the short Draw uses the list's DrawingStyle and ImageType, BlendColor changes the
  dsSelected result, which with a colour is the framework's blend of UIF-1486 (U-167); BlendColor and ImageType are stored values that fire Change; a disabled alpha cell is the
  grey, halved conversion of its pixels composited over the background (within 1), style and image type ignored,
  itMask drawn as when enabled; a disabled cell without partial alpha keeps the 50 % blend with clBtnFace
  (UIF-566); an index outside the list draws nothing. Built as a GUI program: the common controls of version 6,
  which draw alpha, come with its manifest. }
uses
  Windows, SysUtils, Classes, CommCtrl, Graphics, ImgList, TestKit, WicTestKit, ImgTestKit;

const
  White = Cardinal($00FFFFFF);
  Black = Cardinal($00000000);
  StyleNames: array[TDrawingStyle] of string = ('dsFocus', 'dsSelected', 'dsNormal', 'dsTransparent');
  TypeNames: array[TImageType] of string = ('itImage', 'itMask');

{ the flags of UIF-1475, written out here }
function Flags(S: TDrawingStyle; T: TImageType): UINT;
begin
  case S of
    dsFocus: Result := ILD_FOCUS;
    dsSelected: Result := ILD_SELECTED;
    dsTransparent: Result := ILD_TRANSPARENT;
  else
    Result := ILD_NORMAL;
  end;
  if T = itMask then
    Result := Result or ILD_MASK;
end;

function RefOf(C: TColor; None: COLORREF): COLORREF;
begin
  if C = clNone then
    Result := None
  else
    Result := COLORREF(ColorToRGB(C));
end;

{ a list of two cells: 0 the four blocks (partial alpha), 1 an opaque lime square with a fuchsia frame of 3
  pixels made transparent by AddMasked (alpha 0 and 255 only) }
function NewList: TImageList;
var
  B: TBitmap;
begin
  Result := TImageList.Create(nil);
  B := FourBlocks(1, 0, False);
  try
    Result.Add(B, nil);
  finally
    B.Free;
  end;
  B := TBitmap.Create;
  try
    B.SetSize(16, 16);
    B.Canvas.Brush.Color := clFuchsia;
    B.Canvas.FillRect(Rect(0, 0, 16, 16));
    B.Canvas.Brush.Color := clLime;
    B.Canvas.FillRect(Rect(3, 3, 13, 13));
    Result.AddMasked(B, clFuchsia);
  finally
    B.Free;
  end;
end;

{ Draw with an explicit style and type against ImageList_DrawEx with the expected flags and colours }
procedure Compare(L: TImageList; Index: Integer; S: TDrawingStyle; T: TImageType; const Name: string);
var
  A, B: TBitmap;
begin
  A := Surface(20, 20, White);
  B := Surface(20, 20, White);
  try
    L.Draw(A.Canvas, 2, 3, Index, S, T);
    ImageList_DrawEx(L.Handle, Index, B.Canvas.Handle, 2, 3, 0, 0, RefOf(L.BkColor, CLR_NONE),
      RefOf(L.BlendColor, CLR_DEFAULT), Flags(S, T));
    CheckEqI(0, Differences(A, B), Name);
  finally
    A.Free;
    B.Free;
  end;
end;

{ U-167: the pixels of cell Index drawn with Style at (2, 3) on white that miss the blend of UIF-1486 with Color
  composited over white by more than 1 }
function FormulaMisses(L: TImageList; Index: Integer; Color: TColor; S: TDrawingStyle): Integer;
var
  A: TBitmap;
  X, Y, I, D: Integer;
  E, V: Cardinal;
begin
  Result := 0;
  A := Surface(20, 20, White);
  try
    L.Draw(A.Canvas, 2, 3, Index, S, itImage);
    for Y := 0 to 15 do
      for X := 0 to 15 do
      begin
        E := Blend(BlendedPixel(CellQuad(L, Index, X, Y), Color, S), White);
        V := Quad(A, 2 + X, 3 + Y);
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
  finally
    A.Free;
  end;
end;

{ the pixels differing between two Draw calls of the same cell }
function DrawnDifferences(L: TImageList; Index: Integer; S1: TDrawingStyle; T1: TImageType; S2: TDrawingStyle;
  T2: TImageType): Integer;
var
  A, B: TBitmap;
begin
  A := Surface(20, 20, White);
  B := Surface(20, 20, White);
  try
    L.Draw(A.Canvas, 2, 3, Index, S1, T1);
    L.Draw(B.Canvas, 2, 3, Index, S2, T2);
    Result := Differences(A, B);
  finally
    A.Free;
    B.Free;
  end;
end;

procedure CheckStyleMap;
var
  L: TImageList;
  S: TDrawingStyle;
  T: TImageType;
  I: Integer;
  A, B: TBitmap;
begin
  Section('style and image type map (UIF-1475)');
  L := NewList;
  try
    for I := 0 to 1 do
      for S := Low(TDrawingStyle) to High(TDrawingStyle) do
        for T := Low(TImageType) to High(TImageType) do
        begin
          L.BkColor := clNone;
          Compare(L, I, S, T, Format('cell %d, %s, %s, BkColor clNone', [I, StyleNames[S], TypeNames[T]]));
          L.BkColor := clYellow;
          Compare(L, I, S, T, Format('cell %d, %s, %s, BkColor clYellow', [I, StyleNames[S], TypeNames[T]]));
        end;
    L.BkColor := clNone;
    Check(DrawnDifferences(L, 1, dsNormal, itImage, dsSelected, itImage) > 0, 'dsSelected differs from dsNormal');
    Check(DrawnDifferences(L, 1, dsNormal, itImage, dsFocus, itImage) > 0, 'dsFocus differs from dsNormal');
    Check(DrawnDifferences(L, 1, dsNormal, itImage, dsNormal, itMask) > 0, 'itMask differs from itImage');
    L.BkColor := clYellow;
    Check(DrawnDifferences(L, 1, dsNormal, itImage, dsTransparent, itImage) > 0,
      'with a BkColor dsTransparent differs from dsNormal');
    L.BkColor := clNone;

    Section('the short Draw uses DrawingStyle and ImageType');
    L.DrawingStyle := dsSelected;
    L.ImageType := itMask;
    A := Surface(20, 20, White);
    B := Surface(20, 20, White);
    try
      L.Draw(A.Canvas, 2, 3, 1);
      ImageList_DrawEx(L.Handle, 1, B.Canvas.Handle, 2, 3, 0, 0, CLR_NONE, CLR_DEFAULT, ILD_SELECTED or ILD_MASK);
      CheckEqI(0, Differences(A, B), 'short Draw: dsSelected, itMask');
      L.DrawingStyle := dsNormal;
      L.ImageType := itImage;
      A.Free;
      A := Surface(20, 20, White);
      L.Draw(A.Canvas, 2, 3, 0);
      B.Free;
      B := Surface(20, 20, White);
      ImageList_DrawEx(L.Handle, 0, B.Canvas.Handle, 2, 3, 0, 0, CLR_NONE, CLR_DEFAULT, ILD_NORMAL);
      CheckEqI(0, Differences(A, B), 'short Draw: defaults dsNormal, itImage');
    finally
      A.Free;
      B.Free;
    end;
  finally
    L.Free;
  end;
end;

procedure CheckBlendColor;
var
  L: TImageList;
  C: TChangeCounter;
  A, B: TBitmap;
begin
  Section('BlendColor and ImageType (UIF-1475, UIF-1476)');
  L := NewList;
  C := TChangeCounter.Create;
  A := nil;
  B := nil;
  try
    CheckEqI(clNone, L.BlendColor, 'BlendColor default clNone');
    Check(L.ImageType = itImage, 'ImageType default itImage');
    L.OnChange := C.Changed;
    L.BlendColor := clRed;
    CheckEqI(1, C.Count, 'BlendColor change fires Change once');
    L.BlendColor := clRed;
    CheckEqI(1, C.Count, 'the same BlendColor fires nothing');
    L.ImageType := itMask;
    CheckEqI(2, C.Count, 'ImageType change fires Change once');
    L.ImageType := itMask;
    CheckEqI(2, C.Count, 'the same ImageType fires nothing');
    L.ImageType := itImage;
    L.OnChange := nil;
    CheckEqI(0, FormulaMisses(L, 1, clRed, dsSelected), 'BlendColor clRed: dsSelected with foreground red');
    CheckEqI(0, FormulaMisses(L, 0, clRed, dsSelected), 'BlendColor clRed: dsSelected of the alpha cell');
    A := Surface(20, 20, White);
    L.Draw(A.Canvas, 2, 3, 1, dsSelected, itImage);
    L.BlendColor := clBlue;
    CheckEqI(0, FormulaMisses(L, 1, clBlue, dsSelected), 'BlendColor clBlue: dsSelected with foreground blue');
    L.BlendColor := clNone;
    B := Surface(20, 20, White);
    L.Draw(B.Canvas, 2, 3, 1, dsSelected, itImage);
    Check(Differences(A, B) > 0, 'BlendColor changes the dsSelected result (clRed against clNone)');
    L.BlendColor := clBlue;
    B.Free;
    B := Surface(20, 20, White);
    L.Draw(B.Canvas, 2, 3, 1, dsNormal, itImage);
    A.Free;
    A := Surface(20, 20, White);
    L.BlendColor := clNone;
    L.Draw(A.Canvas, 2, 3, 1, dsNormal, itImage);
    CheckEqI(0, Differences(A, B), 'BlendColor does not change dsNormal');
  finally
    L.Free;
    C.Free;
    A.Free;
    B.Free;
  end;
end;

{ every pixel of the cell drawn disabled at (2, 3) on Background equals the converted pixel over it within 1 }
function DisabledMisses(S: TBitmap; A: Integer; Background: Cardinal): Integer;
var
  X, Y, I, D: Integer;
  E, V: Cardinal;
begin
  Result := 0;
  for Y := 0 to 15 do
    for X := 0 to 15 do
    begin
      E := Blend(DisabledPixel(BlockPixel(A, X, Y)), Background);
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

procedure CheckDisabled;
var
  L: TImageList;
  A, B: TBitmap;
  S: TDrawingStyle;
begin
  Section('disabled alpha cell (UIF-1477)');
  L := NewList;
  A := nil;
  B := nil;
  try
    CheckEqS('7F262626', QuadText(DisabledPixel(BlockRed)), 'the converted pixel of opaque red (computed here)');
    A := Surface(20, 20, White);
    L.Draw(A.Canvas, 2, 3, 0, False);
    CheckRGB(Blend(DisabledPixel(BlockRed), White), Quad(A, 6, 7), 1, 'on white: red block grey and halved');
    CheckRGB(Blend(DisabledPixel(BlockBlue), White), Quad(A, 14, 7), 1, 'on white: blue block grey and halved');
    CheckRGB(Blend(DisabledPixel(BlockGreen), White), Quad(A, 6, 15), 1, 'on white: green block');
    CheckRGB(White, Quad(A, 14, 15), 0, 'on white: transparent block');
    CheckEqI(0, DisabledMisses(A, 0, White), 'on white: every pixel within 1');
    CheckRGB(White, Quad(A, 1, 1), 0, 'on white: nothing outside the cell');
    B := Surface(20, 20, Black);
    L.Draw(B.Canvas, 2, 3, 0, False);
    CheckEqI(0, DisabledMisses(B, 0, Black), 'on black: every pixel within 1');
    for S := Low(TDrawingStyle) to High(TDrawingStyle) do
    begin
      B.Free;
      B := Surface(20, 20, White);
      L.Draw(B.Canvas, 2, 3, 0, S, itImage, False);
      CheckEqI(0, Differences(A, B), 'style ignored when disabled: ' + StyleNames[S]);
    end;
    A.Free;
    A := Surface(20, 20, White);
    B.Free;
    B := Surface(20, 20, White);
    L.Draw(A.Canvas, 2, 3, 0, dsNormal, itMask, False);
    ImageList_DrawEx(L.Handle, 0, B.Canvas.Handle, 2, 3, 0, 0, CLR_NONE, CLR_DEFAULT, ILD_NORMAL or ILD_MASK);
    CheckEqI(0, Differences(A, B), 'itMask disabled draws the mask as when enabled');

    Section('disabled cell without partial alpha (UIF-566)');
    for S := Low(TDrawingStyle) to High(TDrawingStyle) do
    begin
      A.Free;
      A := Surface(20, 20, White);
      B.Free;
      B := Surface(20, 20, White);
      L.Draw(A.Canvas, 2, 3, 1, S, itImage, False);
      ImageList_DrawEx(L.Handle, 1, B.Canvas.Handle, 2, 3, 0, 0, CLR_NONE, COLORREF(ColorToRGB(clBtnFace)),
        ILD_BLEND50);
      CheckEqI(0, Differences(A, B), 'ILD_BLEND50 against clBtnFace: ' + StyleNames[S]);
    end;
    A.Free;
    A := Surface(20, 20, White);
    L.Draw(A.Canvas, 2, 3, 1, False);
    CheckRGB(White, Quad(A, 3, 4), 0, 'the transparent frame stays transparent');
    Check((Quad(A, 9, 9) and $FFFFFF) <> $0000FF00, 'the lime body is blended, not drawn plain');
  finally
    L.Free;
    A.Free;
    B.Free;
  end;
end;

procedure CheckOutOfRange;
var
  L: TImageList;
  A, B: TBitmap;
begin
  Section('index outside the list (UIF-1475)');
  L := NewList;
  A := Surface(20, 20, White);
  B := Surface(20, 20, White);
  try
    L.Draw(A.Canvas, 0, 0, 2);
    L.Draw(A.Canvas, 0, 0, -1, dsNormal, itImage);
    L.Draw(A.Canvas, 0, 0, 2, dsSelected, itMask, False);
    L.Draw(A.Canvas, 0, 0, 5, False);
    CheckEqI(0, Differences(A, B), 'nothing drawn');
  finally
    L.Free;
    A.Free;
    B.Free;
  end;
end;

begin
  CheckStyleMap;
  CheckBlendColor;
  CheckDisabled;
  CheckOutOfRange;
  Done;
end.
