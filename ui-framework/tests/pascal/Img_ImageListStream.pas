// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1472, UIF-1473, UIF-1474
// phase: 8
program Img_ImageListStream;
{ U-165, phase8/image-list.md section 7: the byte layout of the defined binary property Bitmap - the header
  fields of a 2-cell list, bottom-up premultiplied pixels; write, read into a second list, write again gives the
  same bytes and the same cells; a Phase 4 value (the one Img_ImageList writes) reads into the same cells and is
  written back byte for byte; a 24-bit value and a 32-bit value with all alpha 0 give opaque cells; a value
  narrower than Width gives none; a damaged value raises EInvalidGraphic and changes nothing; the same through an
  inline .form text; a 24-bit list round-trips its reduced pixels; the shared fixture
  fixtures/imagelists/alpha4.bin equals the writer's output (run with --write-fixture to make it again). Needs no
  desktop: the store alone is read and written. }
uses
  Windows, SysUtils, Classes, Graphics, ImgList, TestKit, WicTestKit, ImgTestKit;

const
  { written by the Phase 4 writer for Img_ImageList's two cells (red, blue; pixel (1, 1) transparent) }
  Phase4Value =
    '424D36080000000000003600000028000000200000001000000001002000000000000008000000000000000000000000' +
    '0000000000000000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FF0000FFFF000000000000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFFFF0000FF00000000FF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000' +
    'FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF00' +
    '00FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FFFF0000FF';
type
  { a filer that writes the defined binary property into Bytes, or reads it from Bytes }
  TStreamFiler = class(TFiler)
  public
    Reading: Boolean;
    Names: string;
    Bytes: TMemoryStream;
    procedure DefineProperty(const Name: string; ReadData: TReaderProc; WriteData: TWriterProc;
      HasData: Boolean); override;
    procedure DefineBinaryProperty(const Name: string; ReadData, WriteData: TStreamProc;
      HasData: Boolean); override;
  end;

  TListAccess = class(TImageList)
  public
    procedure Define(Filer: TFiler);
  end;

procedure TStreamFiler.DefineProperty(const Name: string; ReadData: TReaderProc; WriteData: TWriterProc;
  HasData: Boolean);
begin
end;

procedure TStreamFiler.DefineBinaryProperty(const Name: string; ReadData, WriteData: TStreamProc;
  HasData: Boolean);
begin
  Names := Names + Name + '(' + BoolToStr(HasData, True) + ');';
  if Reading then
  begin
    Bytes.Position := 0;
    ReadData(Bytes);
  end
  else if HasData then
    WriteData(Bytes);
end;

procedure TListAccess.Define(Filer: TFiler);
begin
  DefineProperties(Filer);
end;

var
  Changes: TChangeCounter;

{ the value the list writes (empty when it stores none) }
function Written(L: TImageList): TMemoryStream;
var
  F: TStreamFiler;
begin
  Result := TMemoryStream.Create;
  F := TStreamFiler.Create(nil, 0);
  try
    F.Bytes := Result;
    TListAccess(L).Define(F);
  finally
    F.Free;
  end;
end;

procedure ReadInto(L: TImageList; Bytes: TMemoryStream);
var
  F: TStreamFiler;
begin
  F := TStreamFiler.Create(nil, 0);
  try
    F.Reading := True;
    F.Bytes := Bytes;
    TListAccess(L).Define(F);
  finally
    F.Free;
  end;
end;

function Hex(M: TMemoryStream; From, Count: Integer): string;
var
  P: PByte;
  I: Integer;
begin
  Result := '';
  P := PByte(NativeUInt(M.Memory) + NativeUInt(From));
  for I := 1 to Count do
  begin
    Result := Result + IntToHex(P^, 2);
    Inc(P);
  end;
end;

function FromHex(const S: string): TMemoryStream;
var
  I: Integer;
  B: Byte;
begin
  Result := TMemoryStream.Create;
  I := 1;
  while I < Length(S) do
  begin
    B := Byte(StrToInt('$' + Copy(S, I, 2)));
    Result.WriteBuffer(B, 1);
    Inc(I, 2);
  end;
  Result.Position := 0;
end;

{ the hex of a value in 64-character lines, as a .form holds it }
function FormHex(M: TMemoryStream): string;
var
  S: string;
  I: Integer;
begin
  S := Hex(M, 0, M.Size);
  Result := '';
  for I := 1 to Length(S) do
  begin
    Result := Result + S[I];
    if (I mod 64 = 0) and (I < Length(S)) then
      Result := Result + #10'    ';
  end;
end;

function SameCells(A, B: TImageList): Boolean;
var
  I, X, Y: Integer;
begin
  Result := (A.Count = B.Count) and (A.Width = B.Width) and (A.Height = B.Height);
  if Result then
    for I := 0 to A.Count - 1 do
      for Y := 0 to A.Height - 1 do
        for X := 0 to A.Width - 1 do
          if CellQuad(A, I, X, Y) <> CellQuad(B, I, X, Y) then
            Result := False;
end;

{ pixel (X, Y) of the image as the file holds it: rows bottom-up, 4 bytes each, no padding }
function FilePixel(M: TMemoryStream; ImageWidth, ImageHeight, X, Y: Integer): string;
begin
  Result := Hex(M, 54 + ((ImageHeight - 1 - Y) * ImageWidth + X) * 4, 4);
end;

procedure CheckHeader;
var
  L: TImageList;
  B: TBitmap;
  W: TMemoryStream;
begin
  Section('header and pixels (UIF-1472)');
  L := TImageList.Create(nil);
  try
    W := Written(L);
    try
      CheckEqI(0, W.Size, 'an empty list writes nothing');
    finally
      W.Free;
    end;
    B := FourBlocks(2, 0, True);
    try
      L.Add(B, nil);
    finally
      B.Free;
    end;
    W := Written(L);
    try
      CheckEqI(54 + 4 * 2 * 16 * 16, W.Size, 'size 54 + 4 x Count x Width x Height');
      CheckEqS('424D', Hex(W, 0, 2), 'signature BM');
      CheckEqS('36080000', Hex(W, 2, 4), 'bfSize 2102');
      CheckEqS('00000000', Hex(W, 6, 4), 'reserved words 0');
      CheckEqS('36000000', Hex(W, 10, 4), 'bfOffBits 54');
      CheckEqS('28000000', Hex(W, 14, 4), 'biSize 40');
      CheckEqS('20000000', Hex(W, 18, 4), 'biWidth = Count x Width');
      CheckEqS('10000000', Hex(W, 22, 4), 'biHeight = Height, positive');
      CheckEqS('0100', Hex(W, 26, 2), 'biPlanes 1');
      CheckEqS('2000', Hex(W, 28, 2), 'biBitCount 32');
      CheckEqS('00000000', Hex(W, 30, 4), 'biCompression 0');
      CheckEqS('00080000', Hex(W, 34, 4), 'biSizeImage');
      CheckEqS('00000000000000000000000000000000', Hex(W, 38, 16), 'the remaining four fields 0');
      CheckEqS('0000FFFF', FilePixel(W, 32, 16, 4, 0), 'top row is the last file row: opaque red');
      CheckEqS('80000080', FilePixel(W, 32, 16, 12, 0), 'blue 128 premultiplied (clamped on the way in)');
      CheckEqS('00010001', FilePixel(W, 32, 16, 4, 12), 'green 1 premultiplied');
      CheckEqS('00000000', FilePixel(W, 32, 16, 12, 12), 'transparent: all four bytes 0');
      CheckEqS('80000080', FilePixel(W, 32, 16, 20, 4), 'second cell side by side');
      CheckEqS('00000000', FilePixel(W, 32, 16, 20, 15), 'first file row is the bottom row');
    finally
      W.Free;
    end;
  finally
    L.Free;
  end;
end;

procedure CheckRoundTrip;
var
  L, M: TImageList;
  B: TBitmap;
  W, W2: TMemoryStream;
begin
  Section('round trip (UIF-1473, UIF-1474)');
  L := TImageList.Create(nil);
  M := TImageList.Create(nil);
  B := FourBlocks(3, 1, False);
  W := nil;
  W2 := nil;
  try
    L.Add(B, nil);
    M.Add(B, nil);
    M.Add(B, nil);
    M.OnChange := Changes.Changed;
    Changes.Count := 0;
    W := Written(L);
    ReadInto(M, W);
    CheckEqI(1, Changes.Count, 'one Change after reading');
    CheckEqI(3, M.Count, 'the list is emptied first');
    CheckEqB(True, SameCells(L, M), 'identical cells');
    CheckEqB(False, M.HandleAllocated, 'reading needs no handle');
    W2 := Written(M);
    CheckEqB(True, SameBytes(W, W2), 'written again: identical bytes');
  finally
    L.Free;
    M.Free;
    B.Free;
    W.Free;
    W2.Free;
  end;
end;

procedure CheckPhase4;
var
  L: TImageList;
  V, W: TMemoryStream;
begin
  Section('a Phase 4 value (UIF-1474)');
  L := TImageList.Create(nil);
  V := FromHex(Phase4Value);
  W := nil;
  try
    ReadInto(L, V);
    CheckEqI(2, L.Count, 'two cells');
    CheckEqS('FFFF0000', QuadText(CellQuad(L, 0, 0, 0)), 'cell 0 opaque red');
    CheckEqS('00000000', QuadText(CellQuad(L, 0, 1, 1)), 'cell 0 transparent pixel');
    CheckEqS('FF0000FF', QuadText(CellQuad(L, 1, 15, 15)), 'cell 1 opaque blue');
    CheckEqS('00000000', QuadText(CellQuad(L, 1, 1, 1)), 'cell 1 transparent pixel');
    W := Written(L);
    CheckEqB(True, SameBytes(V, W), 'written back byte for byte');
  finally
    L.Free;
    V.Free;
    W.Free;
  end;
end;

procedure CheckOpaqueValues;
var
  L: TImageList;
  B: TBitmap;
  V: TMemoryStream;
begin
  Section('opaque values (UIF-1473)');
  L := TImageList.Create(nil);
  B := TBitmap.Create;
  V := TMemoryStream.Create;
  try
    B.PixelFormat := pf24bit;
    B.SetSize(32, 16);
    B.Canvas.Brush.Color := clRed;
    B.Canvas.FillRect(Rect(0, 0, 32, 16));
    B.Canvas.Pixels[17, 2] := clBlue;
    B.SaveToStream(V);
    ReadInto(L, V);
    CheckEqI(2, L.Count, '24-bit value: two cells');
    CheckEqS('FFFF0000', QuadText(CellQuad(L, 0, 0, 0)), '24-bit value: opaque');
    CheckEqS('FF0000FF', QuadText(CellQuad(L, 1, 1, 2)), '24-bit value: second cell');

    B.PixelFormat := pf32bit;
    B.Canvas.Brush.Color := clLime;
    B.Canvas.FillRect(Rect(0, 0, 32, 16));
    V.Clear;
    B.SaveToStream(V);
    ReadInto(L, V);
    CheckEqI(2, L.Count, 'all-zero alpha value: two cells');
    CheckEqS('FF00FF00', QuadText(CellQuad(L, 1, 9, 9)), 'all-zero alpha value: opaque');

    B.SetSize(8, 16);
    V.Clear;
    B.SaveToStream(V);
    ReadInto(L, V);
    CheckEqI(0, L.Count, 'a value narrower than Width gives no cell');
  finally
    L.Free;
    B.Free;
    V.Free;
  end;
end;

procedure TryRead(L: TImageList; const Bytes, Name: string);
var
  V: TMemoryStream;
begin
  V := FromHex(Bytes);
  try
    try
      ReadInto(L, V);
      Check(False, Name + ' raises');
    except
      on E: Exception do
        CheckEqS('EInvalidGraphic: Bitmap image is not valid', E.ClassName + ': ' + E.Message, Name);
    end;
  finally
    V.Free;
  end;
end;

procedure CheckDamaged;
var
  L: TImageList;
  B: TBitmap;
begin
  Section('damaged values (UIF-1473, UIF-1482)');
  L := TImageList.Create(nil);
  B := FourBlocks(2, 0, False);
  try
    L.Add(B, nil);
    L.OnChange := Changes.Changed;
    Changes.Count := 0;
    TryRead(L, '424D3608000000000000', 'a truncated header');
    TryRead(L, '58594D5A0000000000000000000000000000', 'no BMP signature');
    TryRead(L, Copy(Phase4Value, 1, 200), 'truncated pixels');
    CheckEqI(2, L.Count, 'the cells are kept');
    CheckEqS(QuadText(BlockBlue), QuadText(CellQuad(L, 0, 12, 4)), 'the pixels are kept');
    CheckEqI(0, Changes.Count, 'no Change');
  finally
    L.Free;
    B.Free;
  end;
end;

procedure CheckForm;
var
  L, M: TImageList;
  B: TBitmap;
  W: TMemoryStream;
begin
  Section('inline .form (UIF-1474)');
  L := TImageList.Create(nil);
  M := TImageList.Create(nil);
  B := FourBlocks(2, 2, True);
  W := nil;
  try
    L.Add(B, nil);
    W := Written(L);
    ReadComponentFromText('object M: TImageList'#10'  Height = 16'#10'  Width = 16'#10 +
      '  Bitmap = {'#10'    ' + FormHex(W) + #10'  }'#10'end'#10, M, 'M.form');
    CheckEqI(2, M.Count, 'two cells from the form');
    CheckEqB(True, SameCells(L, M), 'identical cells');
  finally
    L.Free;
    M.Free;
    B.Free;
    W.Free;
  end;
end;

procedure Check24Bit;
var
  L, M: TImageList;
  B: TBitmap;
  W, W2: TMemoryStream;
begin
  Section('a 24-bit list (UIF-1465, UIF-1474)');
  L := TImageList.Create(nil);
  M := TImageList.Create(nil);
  B := FourBlocks(1, 0, False);
  W := nil;
  W2 := nil;
  try
    L.ColorDepth := cd24Bit;
    M.ColorDepth := cd24Bit;
    L.Add(B, nil);
    W := Written(L);
    CheckEqS('FF0000FF', FilePixel(W, 16, 16, 12, 4), 'alpha 128 written opaque, un-premultiplied');
    CheckEqS('00000000', FilePixel(W, 16, 16, 4, 12), 'alpha 1 written transparent');
    ReadInto(M, W);
    CheckEqB(True, SameCells(L, M), 'identical cells');
    W2 := Written(M);
    CheckEqB(True, SameBytes(W, W2), 'identical bytes');
  finally
    L.Free;
    M.Free;
    B.Free;
    W.Free;
    W2.Free;
  end;
end;

procedure CheckFixture;
var
  L: TImageList;
  B: TBitmap;
  W, F: TMemoryStream;
  Path: string;
begin
  Section('the shared fixture alpha4.bin (UIF-1485)');
  Path := IncludeTrailingPathDelimiter(GetEnvironmentVariable('FXC_UI_TESTS_DIR')) + 'fixtures\imagelists\alpha4.bin';
  L := TImageList.Create(nil);
  B := FourBlocks(4, 0, False);
  W := nil;
  F := nil;
  try
    L.Add(B, nil);
    CheckEqI(4, L.Count, 'four cells in four arrangements');
    W := Written(L);
    if ParamStr(1) = '--write-fixture' then
      W.SaveToFile(Path);
    F := FileBytes(Path);
    CheckEqI(54 + 4 * 4 * 16 * 16, F.Size, 'fixture size');
    CheckEqB(True, SameBytes(W, F), 'the writer produces exactly the fixture');
  finally
    L.Free;
    B.Free;
    W.Free;
    F.Free;
  end;
end;

begin
  Changes := TChangeCounter.Create;
  CheckHeader;
  CheckRoundTrip;
  CheckPhase4;
  CheckOpaqueValues;
  CheckDamaged;
  CheckForm;
  Check24Bit;
  CheckFixture;
  Changes.Free;
  Done;
end.
