// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1461, UIF-1462, UIF-1464, UIF-1465, UIF-1466, UIF-1471
// phase: 8
// needs: desktop
// opc: --app-type gui
program Img_ImageListAlpha;
{ U-165, phase8/image-list.md sections 4 and 5: a 16 x 16 cell of four blocks (opaque red, blue with alpha 128,
  green with alpha 1, transparent) added from a 32-bit bitmap (colour bytes above alpha clamped, a mask ignored)
  and from an image decoded from alpha.png; drawn by the native list (ImageList_Draw on Handle) and by Draw onto
  white and black 32-bit bitmaps, each block equal to the source-over blend computed here within 1 - the probe of
  the straight-alpha hand-over (UIF-1466); a 32-bit bitmap whose alpha bytes are all 0 is opaque; a 32-bit icon
  (straight alpha, premultiplied on the way in) and an icon with a 1-bit mask; an 8 x 8 icon fitted into a 16 x 16
  list by nearest neighbour; GetAlphaBitmap, GetBitmap on white, GetIcon read back; the reduction of a 24-bit list.
  Built as a GUI program: the common controls of version 6, which draw alpha, come with its manifest. }
uses
  Windows, SysUtils, Classes, CommCtrl, Graphics, ImgList, TestKit, WicTestKit, ImgTestKit;

const
  White = Cardinal($00FFFFFF);
  Black = Cardinal($00000000);

procedure CheckCell(L: TImageList; Index, A: Integer; const Name: string);
var
  B: TBitmap;
  X, Y, Bad: Integer;
begin
  B := TBitmap.Create;
  try
    CheckEqB(True, L.GetAlphaBitmap(Index, B), Name + ': GetAlphaBitmap');
    Bad := 0;
    for Y := 0 to 15 do
      for X := 0 to 15 do
        if Quad(B, X, Y) <> BlockPixel(A, X, Y) then
          Inc(Bad);
    CheckEqI(0, Bad, Name + ': every pixel canonical');
  finally
    B.Free;
  end;
end;

{ draws cell Index natively (ImageList_Draw) and with Draw, onto white and black }
procedure CheckDrawn(L: TImageList; Index, A: Integer; const Name: string);
var
  S: TBitmap;
begin
  S := Surface(16, 16, White);
  try
    ImageList_Draw(L.Handle, Index, S.Canvas.Handle, 0, 0, ILD_NORMAL);
    CheckDrawnBlocks(S, 0, 0, A, White, Name + ', native on white');
  finally
    S.Free;
  end;
  S := Surface(16, 16, Black);
  try
    ImageList_Draw(L.Handle, Index, S.Canvas.Handle, 0, 0, ILD_NORMAL);
    CheckDrawnBlocks(S, 0, 0, A, Black, Name + ', native on black');
  finally
    S.Free;
  end;
  S := Surface(20, 20, White);
  try
    L.Draw(S.Canvas, 2, 3, Index);
    CheckDrawnBlocks(S, 2, 3, A, White, Name + ', Draw on white');
  finally
    S.Free;
  end;
  S := Surface(20, 20, Black);
  try
    L.Draw(S.Canvas, 2, 3, Index);
    CheckDrawnBlocks(S, 2, 3, A, Black, Name + ', Draw on black');
  finally
    S.Free;
  end;
end;

procedure CheckBitmapSource;
var
  L: TImageList;
  B, Mask: TBitmap;
begin
  Section('alpha bitmap source (UIF-1461, UIF-1462, UIF-1466)');
  L := TImageList.Create(nil);
  B := FourBlocks(2, 0, True);
  Mask := Surface(32, 16, White);                         { all white: would hide everything if it were used }
  try
    CheckEqI(0, L.Add(B, Mask), 'Add returns 0');
    CheckEqI(2, L.Count, 'two cells');
    CheckEqB(False, L.HandleAllocated, 'no handle needed for the store');
    CheckCell(L, 0, 0, 'cell 0 (colour above alpha clamped, colour under alpha 0 cleared, mask ignored)');
    CheckCell(L, 1, 1, 'cell 1');
    CheckEqS('800000FF', QuadText(Quad(B, 12, 4)), 'the source bitmap is not modified');
    CheckDrawn(L, 0, 0, 'cell 0');
    CheckDrawn(L, 1, 1, 'cell 1');
    L.Clear;
    L.AddMasked(B, clBlue);
    CheckCell(L, 0, 0, 'AddMasked of an alpha source ignores the mask colour');
  finally
    L.Free;
    B.Free;
    Mask.Free;
  end;
end;

procedure CheckImageSource;
var
  L: TImageList;
  Image: TWICImage;
  B, S: TBitmap;
begin
  Section('image decoded from alpha.png (UIF-1348 pixels)');
  L := TImageList.Create(nil);
  Image := TWICImage.Create;
  B := TBitmap.Create;
  try
    Image.LoadFromFile(Fixture('alpha.png'));
    B.Assign(Image);
    CheckEqI(16, B.Width, 'decoded width 16');
    CheckEqI(12, B.Height, 'decoded height 12');
    L.Add(B, nil);
    CheckEqI(1, L.Count, 'one cell');
    CheckEqS(QuadText(BlockRed), QuadText(CellQuad(L, 0, 3, 3)), 'top left opaque red');
    CheckEqS('FF00FF00', QuadText(CellQuad(L, 0, 12, 3)), 'top right opaque green');
    CheckQuad(BlockBlue, CellQuad(L, 0, 3, 9), 1, 'bottom left blue, alpha 128, premultiplied');
    CheckEqS(QuadText(BlockClear), QuadText(CellQuad(L, 0, 12, 9)), 'bottom right transparent');
    CheckEqS(QuadText(BlockClear), QuadText(CellQuad(L, 0, 3, 13)), 'rows below the source are transparent');
    S := Surface(16, 16, White);
    try
      ImageList_Draw(L.Handle, 0, S.Canvas.Handle, 0, 0, ILD_NORMAL);
      CheckRGB(Blend(CellQuad(L, 0, 3, 9), White), Quad(S, 3, 9), 1, 'native blend of the blue block on white');
      CheckRGB(White, Quad(S, 12, 9), 0, 'transparent block leaves white');
      CheckRGB(White, Quad(S, 3, 14), 0, 'rows below the source leave white');
    finally
      S.Free;
    end;
    S := Surface(16, 16, Black);
    try
      L.Draw(S.Canvas, 0, 0, 0);
      CheckRGB(Blend(CellQuad(L, 0, 3, 9), Black), Quad(S, 3, 9), 1, 'Draw blend of the blue block on black');
      CheckRGB($0000FF00, Quad(S, 12, 3), 0, 'Draw of the opaque green block');
    finally
      S.Free;
    end;
  finally
    L.Free;
    Image.Free;
    B.Free;
  end;
end;

procedure CheckOpaqueSource;
var
  L: TImageList;
  B: TBitmap;
begin
  Section('32-bit bitmap with all alpha 0 (UIF-1462)');
  L := TImageList.Create(nil);
  B := TBitmap.Create;
  try
    B.PixelFormat := pf32bit;
    B.SetSize(16, 16);
    B.Canvas.Brush.Color := clRed;
    B.Canvas.FillRect(Rect(0, 0, 16, 16));
    CheckEqI(0, Integer(Quad(B, 5, 5) shr 24), 'GDI drawing leaves alpha 0');
    L.Add(B, nil);
    CheckEqS(QuadText(BlockRed), QuadText(CellQuad(L, 0, 5, 5)), 'the pixels are opaque');
  finally
    L.Free;
    B.Free;
  end;
end;

{ an icon of W x H: colour from Color (a pf32bit bitmap), AND mask from MaskBits (1 bit, word-aligned rows) }
function MakeIcon(Color: TBitmap; W, H: Integer; MaskBits: Pointer): TIcon;
var
  Info: TIconInfo;
begin
  FillChar(Info, SizeOf(Info), 0);
  Info.fIcon := True;
  Info.hbmMask := CreateBitmap(W, H, 1, 1, MaskBits);
  Info.hbmColor := Color.Handle;
  Result := TIcon.Create;
  Result.Handle := CreateIconIndirect(Info);
  DeleteObject(Info.hbmMask);
end;

function Attempt(L: TImageList; Op: Integer; Icon: TIcon): string;
begin
  Result := 'none';
  try
    case Op of
      0: L.GetIcon(L.Count, Icon);
      1: L.GetIcon(-1, Icon);
      2: L.GetIcon(0, nil);
    end;
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

procedure CheckIcons;
var
  L, L2: TImageList;
  B: TBitmap;
  Icon, Back: TIcon;
  Bits: array[0..31] of Byte;
  X, Y: Integer;
  V: Cardinal;
begin
  Section('icons (UIF-1464)');
  L := TImageList.Create(nil);
  B := TBitmap.Create;
  try
    { a 32-bit icon in straight alpha: blue 255 with alpha 128, green 255 with alpha 1 }
    B.PixelFormat := pf32bit;
    B.SetSize(16, 16);
    for Y := 0 to 15 do
      for X := 0 to 15 do
      begin
        V := BlockPixel(0, X, Y);
        if V = BlockBlue then
          V := Cardinal($800000FF)
        else if V = BlockGreen then
          V := Cardinal($0100FF00);
        SetQuad(B, X, Y, V);
      end;
    FillChar(Bits, SizeOf(Bits), 0);
    Icon := MakeIcon(B, 16, 16, @Bits);
    try
      CheckEqI(0, L.AddIcon(Icon), 'AddIcon of a 32-bit icon');
      CheckCell(L, 0, 0, '32-bit icon premultiplied');
      CheckDrawn(L, 0, 0, '32-bit icon');
    finally
      Icon.Free;
    end;

    { colour without alpha and a 1-bit mask whose top right quadrant is white }
    B.Canvas.Brush.Color := clLime;
    B.Canvas.FillRect(Rect(0, 0, 16, 16));
    for Y := 0 to 7 do
      Bits[Y * 2 + 1] := $FF;
    Icon := MakeIcon(B, 16, 16, @Bits);
    L2 := TImageList.Create(nil);
    try
      CheckEqI(1, L.AddIcon(Icon), 'AddIcon of a masked icon');
      CheckEqS('FF00FF00', QuadText(CellQuad(L, 1, 3, 3)), 'masked icon: black mask opaque');
      CheckEqS('00000000', QuadText(CellQuad(L, 1, 12, 3)), 'masked icon: white mask transparent');
      L2.Masked := False;
      CheckEqI(0, L2.AddIcon(Icon), 'Masked = False: AddIcon');
      CheckEqS('FF00FF00', QuadText(CellQuad(L2, 0, 12, 3)), 'Masked = False: the mask is ignored');
    finally
      Icon.Free;
      L2.Free;
    end;

    { an 8 x 8 icon whose pixel (x, y) has red 30 x + 10 and green 30 y + 10 }
    B.SetSize(8, 8);
    for Y := 0 to 7 do
      for X := 0 to 7 do
        SetQuad(B, X, Y, Cardinal($FF000000) or (Cardinal(30 * X + 10) shl 16) or (Cardinal(30 * Y + 10) shl 8));
    FillChar(Bits, SizeOf(Bits), 0);
    Icon := MakeIcon(B, 8, 8, @Bits);
    try
      CheckEqI(2, L.AddIcon(Icon), 'AddIcon of an 8 x 8 icon');
      CheckEqS('FF0A0A00', QuadText(CellQuad(L, 2, 0, 0)), 'nearest neighbour: (0, 0) from (0, 0)');
      CheckEqS('FF0A0A00', QuadText(CellQuad(L, 2, 1, 1)), 'nearest neighbour: (1, 1) from (0, 0)');
      CheckEqS('FF462800', QuadText(CellQuad(L, 2, 5, 3)), 'nearest neighbour: (5, 3) from (2, 1)');
      CheckEqS('FFDCDC00', QuadText(CellQuad(L, 2, 15, 15)), 'nearest neighbour: (15, 15) from (7, 7)');
    finally
      Icon.Free;
    end;

    Section('GetAlphaBitmap, GetBitmap, GetIcon (UIF-1471)');
    B.SetSize(3, 3);
    CheckEqB(False, L.GetAlphaBitmap(3, B), 'GetAlphaBitmap: invalid index');
    CheckEqI(3, B.Width, 'GetAlphaBitmap: invalid index leaves the image');
    CheckEqB(False, L.GetAlphaBitmap(-1, B), 'GetAlphaBitmap: negative index');
    CheckEqB(False, L.GetAlphaBitmap(0, nil), 'GetAlphaBitmap: nil image');
    B.PixelFormat := pf24bit;
    CheckEqB(True, L.GetAlphaBitmap(0, B), 'GetAlphaBitmap: valid');
    Check(B.PixelFormat = pf32bit, 'GetAlphaBitmap: pf32bit');
    CheckEqI(16, B.Width, 'GetAlphaBitmap: width');
    CheckEqI(16, B.Height, 'GetAlphaBitmap: height');
    CheckEqB(True, L.GetBitmap(0, B), 'GetBitmap: valid');
    B.PixelFormat := pf32bit;
    CheckEqS('00FF0000', QuadText(Quad(B, 4, 4)), 'GetBitmap: opaque red');
    CheckEqS(QuadText(Blend(BlockBlue, White)), QuadText(Quad(B, 12, 4)), 'GetBitmap: blue 128 over white');
    CheckEqS(QuadText(Blend(BlockGreen, White)), QuadText(Quad(B, 4, 12)), 'GetBitmap: green 1 over white');
    CheckEqS('00FFFFFF', QuadText(Quad(B, 12, 12)), 'GetBitmap: transparent is white');
    CheckEqB(False, L.GetBitmap(9, B), 'GetBitmap: invalid index');

    Back := TIcon.Create;
    L2 := TImageList.Create(nil);
    try
      L.GetIcon(0, Back);
      Check(Back.Handle <> 0, 'GetIcon: an icon');
      CheckEqI(16, Back.Width, 'GetIcon: width');
      L.GetIcon(2, Back);
      CheckEqI(16, Back.Height, 'GetIcon: a second call replaces the icon');
      L.GetIcon(0, Back);
      L2.AddIcon(Back);
      CheckQuad(BlockRed, CellQuad(L2, 0, 4, 4), 1, 'GetIcon round trip: red');
      CheckQuad(BlockBlue, CellQuad(L2, 0, 12, 4), 1, 'GetIcon round trip: blue 128');
      CheckQuad(BlockGreen, CellQuad(L2, 0, 4, 12), 1, 'GetIcon round trip: green 1');
      CheckQuad(BlockClear, CellQuad(L2, 0, 12, 12), 0, 'GetIcon round trip: transparent');
      CheckEqS('EInvalidOperation: Invalid ImageList Index', Attempt(L, 0, Back), 'GetIcon: index = Count');
      CheckEqS('EInvalidOperation: Invalid ImageList Index', Attempt(L, 1, Back), 'GetIcon: index -1');
      CheckEqS('EInvalidOperation: Invalid image size', Attempt(L, 2, Back), 'GetIcon: nil icon');
    finally
      Back.Free;
      L2.Free;
    end;
  finally
    L.Free;
    B.Free;
  end;
end;

procedure CheckReduction;
var
  L: TImageList;
  B, S: TBitmap;
begin
  Section('24-bit list (UIF-1465)');
  L := TImageList.Create(nil);
  B := FourBlocks(1, 0, False);
  try
    L.ColorDepth := cd24Bit;
    L.Add(B, nil);
    CheckEqS('FFFF0000', QuadText(CellQuad(L, 0, 4, 4)), 'opaque red stays');
    CheckEqS('FF0000FF', QuadText(CellQuad(L, 0, 12, 4)), 'alpha 128: opaque, colour un-premultiplied');
    CheckEqS('00000000', QuadText(CellQuad(L, 0, 4, 12)), 'alpha 1: transparent');
    CheckEqS('00000000', QuadText(CellQuad(L, 0, 12, 12)), 'alpha 0: transparent');
    S := Surface(16, 16, White);
    try
      ImageList_Draw(L.Handle, 0, S.Canvas.Handle, 0, 0, ILD_NORMAL);
      CheckRGB($000000FF, Quad(S, 12, 4), 0, 'native: the reduced blue block');
      CheckRGB($00FF0000, Quad(S, 4, 4), 0, 'native: red');
      CheckRGB(White, Quad(S, 4, 12), 0, 'native: the transparent alpha-1 block');
    finally
      S.Free;
    end;
  finally
    L.Free;
    B.Free;
  end;
end;

begin
  CheckBitmapSource;
  CheckImageSource;
  CheckOpaqueSource;
  CheckIcons;
  CheckReduction;
  Done;
end.
