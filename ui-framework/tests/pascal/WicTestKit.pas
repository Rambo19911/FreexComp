// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
unit WicTestKit;
{ Shared by the image tests of wic-image.md: the path of a fixture image (ui-framework/tests/fixtures/images, found
  through the tests directory the harness passes in FXC_UI_TESTS_DIR), the bytes of a file, and pixels of 32-bit
  bitmaps as $AARRGGBB values with a comparison that allows a tolerance per channel. }

interface

uses
  SysUtils, Classes, Graphics, TestKit;

type
  TChangeCounter = class
    Count: Integer;
    procedure Changed(Sender: TObject);
  end;

function Fixture(const Name: string): string;
function FileBytes(const FileName: string): TMemoryStream;
function SameBytes(A, B: TMemoryStream): Boolean;
{ the pixel of a pf32bit bitmap as $AARRGGBB }
function Quad(B: TBitmap; X, Y: Integer): Cardinal;
{ the pixel of an image, read through a bitmap the image is assigned to (UIF-1348) }
function ImageQuad(Image: TWICImage; X, Y: Integer): Cardinal;
function QuadText(V: Cardinal): string;
procedure CheckQuad(Expected, Actual: Cardinal; Tolerance: Integer; const Name: string);
{ the four block centres of an image: top left, top right, bottom left, bottom right }
procedure CheckBlocks(Image: TWICImage; TL, TR, BL, BR: Cardinal; Tolerance: Integer; const Name: string);

implementation

procedure TChangeCounter.Changed(Sender: TObject);
begin
  Inc(Count);
end;

function Fixture(const Name: string): string;
begin
  Result := IncludeTrailingPathDelimiter(GetEnvironmentVariable('FXC_UI_TESTS_DIR')) + 'fixtures\images\' + Name;
end;

function FileBytes(const FileName: string): TMemoryStream;
begin
  Result := TMemoryStream.Create;
  Result.LoadFromFile(FileName);
  Result.Position := 0;
end;

function SameBytes(A, B: TMemoryStream): Boolean;
var
  P, Q: PByte;
  I: Integer;
begin
  Result := A.Size = B.Size;
  if not Result then
    Exit;
  P := PByte(A.Memory);
  Q := PByte(B.Memory);
  for I := 1 to A.Size do
  begin
    if P^ <> Q^ then
    begin
      Result := False;
      Exit;
    end;
    Inc(P);
    Inc(Q);
  end;
end;

function Quad(B: TBitmap; X, Y: Integer): Cardinal;
var
  P: PByte;
begin
  P := PByte(B.ScanLine[Y]);
  Inc(P, X * 4);
  Result := PCardinal(P)^;
end;

function ImageQuad(Image: TWICImage; X, Y: Integer): Cardinal;
var
  B: TBitmap;
begin
  B := TBitmap.Create;
  try
    B.Assign(Image);
    Result := Quad(B, X, Y);
  finally
    B.Free;
  end;
end;

function QuadText(V: Cardinal): string;
begin
  Result := IntToHex(Int64(V), 8);
end;

procedure CheckQuad(Expected, Actual: Cardinal; Tolerance: Integer; const Name: string);
var
  I, D: Integer;
  Ok: Boolean;
begin
  Ok := True;
  for I := 0 to 3 do
  begin
    D := Integer((Expected shr (I * 8)) and $FF) - Integer((Actual shr (I * 8)) and $FF);
    if (D > Tolerance) or (D < -Tolerance) then
      Ok := False;
  end;
  if Ok then
    Check(True, Name)
  else
    CheckEqS(QuadText(Expected), QuadText(Actual), Name);
end;

procedure CheckBlocks(Image: TWICImage; TL, TR, BL, BR: Cardinal; Tolerance: Integer; const Name: string);
var
  B: TBitmap;
  W, H: Integer;
begin
  B := TBitmap.Create;
  try
    B.Assign(Image);
    W := B.Width;
    H := B.Height;
    CheckQuad(TL, Quad(B, W div 4, H div 4), Tolerance, Name + ': top left block');
    CheckQuad(TR, Quad(B, 3 * W div 4, H div 4), Tolerance, Name + ': top right block');
    CheckQuad(BL, Quad(B, W div 4, 3 * H div 4), Tolerance, Name + ': bottom left block');
    CheckQuad(BR, Quad(B, 3 * W div 4, 3 * H div 4), Tolerance, Name + ': bottom right block');
  finally
    B.Free;
  end;
end;

end.
