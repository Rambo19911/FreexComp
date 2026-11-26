// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
unit ImgTestKit;
{ Shared by the image-list tests of phase8/image-list.md section 13: the four blocks of a 16 x 16 cell (opaque
  red, blue with alpha 128, green with alpha 1, transparent) as canonical (premultiplied) pixels, 32-bit source
  bitmaps that hold them in rotated arrangements, the source-over blend a drawn pixel must equal, and pixels of
  cells read back through GetAlphaBitmap. Pixels are $AARRGGBB values (bytes blue, green, red, alpha). }

interface

uses
  Windows, SysUtils, Classes, Graphics, ImgList, TestKit, WicTestKit;

const
  BlockRed = Cardinal($FFFF0000);
  BlockBlue = Cardinal($80000080);
  BlockGreen = Cardinal($01000100);
  BlockClear = Cardinal($00000000);

procedure SetQuad(B: TBitmap; X, Y: Integer; V: Cardinal);
{ the canonical pixel at (X, Y) of a 16 x 16 cell in arrangement A: quadrant q (top left, top right, bottom left,
  bottom right) holds block (q + A) mod 4 of red, blue, green, transparent }
function BlockPixel(A, X, Y: Integer): Cardinal;
{ a pf32bit bitmap of Cells cells side by side, cell i in arrangement (First + i) mod 4. Raw writes source values
  the store must make canonical: colour bytes above the alpha byte, and a colour under alpha 0. }
function FourBlocks(Cells, First: Integer; Raw: Boolean): TBitmap;
{ the source-over blend of canonical pixel P on the opaque colour Background ($00RRGGBB): c + (255 - a) x bg / 255 }
function Blend(P, Background: Cardinal): Cardinal;
{ pixel (X, Y) of cell Index, read with GetAlphaBitmap ($DEADBEEF when it returns False) }
function CellQuad(L: TCustomImageList; Index, X, Y: Integer): Cardinal;
{ a pf32bit W x H bitmap whose pixels are Background ($00RRGGBB, alpha byte 0) }
function Surface(W, H: Integer; Background: Cardinal): TBitmap;
{ compares the three colour bytes only, each within Tolerance }
procedure CheckRGB(Expected, Actual: Cardinal; Tolerance: Integer; const Name: string);
{ the centres of the four quadrants of a cell drawn at (X0, Y0) of S equal the blends of arrangement A }
procedure CheckDrawnBlocks(S: TBitmap; X0, Y0, A: Integer; Background: Cardinal; const Name: string);

implementation

procedure SetQuad(B: TBitmap; X, Y: Integer; V: Cardinal);
var
  P: PByte;
begin
  P := PByte(B.ScanLine[Y]);
  Inc(P, X * 4);
  PCardinal(P)^ := V;
end;

function BlockOf(A, X, Y: Integer): Integer;
var
  Q: Integer;
begin
  Q := 0;
  if X >= 8 then
    Q := 1;
  if Y >= 8 then
    Q := Q + 2;
  Result := (Q + A) mod 4;
end;

function BlockPixel(A, X, Y: Integer): Cardinal;
begin
  case BlockOf(A, X, Y) of
    0: Result := BlockRed;
    1: Result := BlockBlue;
    2: Result := BlockGreen;
  else
    Result := BlockClear;
  end;
end;

function FourBlocks(Cells, First: Integer; Raw: Boolean): TBitmap;
var
  I, X, Y: Integer;
  V: Cardinal;
begin
  Result := TBitmap.Create;
  Result.PixelFormat := pf32bit;
  Result.SetSize(16 * Cells, 16);
  for I := 0 to Cells - 1 do
    for Y := 0 to 15 do
      for X := 0 to 15 do
      begin
        V := BlockPixel((First + I) mod 4, X, Y);
        if Raw then
          case BlockOf((First + I) mod 4, X, Y) of
            1: V := Cardinal($800000FF);                   { blue 255 over alpha 128 }
            2: V := Cardinal($0100FF00);                   { green 255 over alpha 1 }
            3: V := Cardinal($00123456);                   { colour under alpha 0 }
          end;
        SetQuad(Result, 16 * I + X, Y, V);
      end;
end;

function Blend(P, Background: Cardinal): Cardinal;
var
  I, C, A, Bg: Integer;
begin
  Result := 0;
  A := Integer(P shr 24);
  for I := 0 to 2 do
  begin
    C := Integer((P shr (I * 8)) and $FF);
    Bg := Integer((Background shr (I * 8)) and $FF);
    C := C + ((255 - A) * Bg + 127) div 255;
    Result := Result or (Cardinal(C) shl (I * 8));
  end;
end;

function CellQuad(L: TCustomImageList; Index, X, Y: Integer): Cardinal;
var
  B: TBitmap;
begin
  B := TBitmap.Create;
  try
    if L.GetAlphaBitmap(Index, B) then
      Result := Quad(B, X, Y)
    else
      Result := Cardinal($DEADBEEF);
  finally
    B.Free;
  end;
end;

function Surface(W, H: Integer; Background: Cardinal): TBitmap;
var
  X, Y: Integer;
begin
  Result := TBitmap.Create;
  Result.PixelFormat := pf32bit;
  Result.SetSize(W, H);
  for Y := 0 to H - 1 do
    for X := 0 to W - 1 do
      SetQuad(Result, X, Y, Background);
end;

procedure CheckRGB(Expected, Actual: Cardinal; Tolerance: Integer; const Name: string);
var
  I, D: Integer;
  Ok: Boolean;
begin
  Ok := True;
  for I := 0 to 2 do
  begin
    D := Integer((Expected shr (I * 8)) and $FF) - Integer((Actual shr (I * 8)) and $FF);
    if (D > Tolerance) or (D < -Tolerance) then
      Ok := False;
  end;
  if Ok then
    Check(True, Name)
  else
    CheckEqS(QuadText(Expected and $FFFFFF), QuadText(Actual and $FFFFFF), Name);
end;

procedure CheckDrawnBlocks(S: TBitmap; X0, Y0, A: Integer; Background: Cardinal; const Name: string);
begin
  CheckRGB(Blend(BlockPixel(A, 4, 4), Background), Quad(S, X0 + 4, Y0 + 4), 1, Name + ': top left');
  CheckRGB(Blend(BlockPixel(A, 12, 4), Background), Quad(S, X0 + 12, Y0 + 4), 1, Name + ': top right');
  CheckRGB(Blend(BlockPixel(A, 4, 12), Background), Quad(S, X0 + 4, Y0 + 12), 1, Name + ': bottom left');
  CheckRGB(Blend(BlockPixel(A, 12, 12), Background), Quad(S, X0 + 12, Y0 + 12), 1, Name + ': bottom right');
end;

end.
