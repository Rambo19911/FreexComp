// covers: UIF-544, UIF-545
// opc: -U ..\..\src
program Gfx_BitmapStreams;
{ U-009, graphics.md UIF-544/545: TBitmap.SaveToStream writes an uncompressed bottom-up BMP with a 40-byte
  info header and zero row padding (byte-exact for 2 x 2 at 24 and 32 bits); LoadFromStream round-trips
  24/32-bit images and reads BI_RGB 1/4/8-bit (palette expanded to 24-bit), bottom-up and top-down,
  BI_BITFIELDS 32-bit with the standard masks, V4/V5 headers; unsupported formats raise EInvalidGraphic
  "Unsupported bitmap format", a bad signature or truncated data "Bitmap image is not valid". The BMP
  fixtures are built byte by byte in memory. }
uses
  Windows, SysUtils, Classes, Graphics, TestKit;

var
  S: TMemoryStream;

procedure W8(V: Byte);
begin
  S.WriteBuffer(V, 1);
end;

procedure W16(V: Word);
begin
  S.WriteBuffer(V, 2);
end;

procedure W32(V: Cardinal);
begin
  S.WriteBuffer(V, 4);
end;

procedure FileHeader(FileSize, OffBits: Cardinal);
begin
  W16($4D42);
  W32(FileSize);
  W32(0);
  W32(OffBits);
end;

{ the 40 bytes every info header starts with }
procedure InfoHeader(HeaderSize: Cardinal; Width, Height: Integer; Bits: Word; Compression, ClrUsed: Cardinal);
begin
  W32(HeaderSize);
  W32(Cardinal(Width));
  W32(Cardinal(Height));
  W16(1);
  W16(Bits);
  W32(Compression);
  W32(0);
  W32(0);
  W32(0);
  W32(ClrUsed);
  W32(0);
end;

procedure Quad(B, G, R: Byte);
begin
  W8(B);
  W8(G);
  W8(R);
  W8(0);
end;

function StreamHex(M: TMemoryStream): string;
var
  P: PByte;
  I: Integer;
begin
  Result := '';
  P := PByte(M.Memory);
  for I := 0 to M.Size - 1 do
  begin
    if I > 0 then
      Result := Result + ' ';
    Result := Result + IntToHex(P^, 2);
    Inc(P);
  end;
end;

function PixelHex(B: TBitmap; X, Y: Integer): string;
var
  P: PByte;
  I, N: Integer;
begin
  if B.PixelFormat = pf24bit then
    N := 3
  else
    N := 4;
  P := PByte(B.ScanLine[Y]);
  Inc(P, X * N);
  Result := '';
  for I := 0 to N - 1 do
  begin
    if I > 0 then
      Result := Result + ' ';
    Result := Result + IntToHex(P^, 2);
    Inc(P);
  end;
end;

procedure SetPixelBytes(B: TBitmap; X, Y: Integer; const Bytes: array of Byte);
var
  P: PByte;
  I: Integer;
begin
  P := PByte(B.ScanLine[Y]);
  Inc(P, X * Length(Bytes));
  for I := 0 to High(Bytes) do
  begin
    P^ := Bytes[I];
    Inc(P);
  end;
end;

function Load: TBitmap;
begin
  S.Position := 0;
  Result := TBitmap.Create;
  Result.LoadFromStream(S);
end;

procedure ExpectLoadError(const Message, Name: string);
var
  B: TBitmap;
begin
  B := TBitmap.Create;
  try
    Expect('EInvalidGraphic', Message);
    try
      S.Position := 0;
      B.LoadFromStream(S);
      Check(False, Name);
    except
      on E: Exception do
        CheckRaises(Name, E.ClassName, E.Message);
    end;
  finally
    B.Free;
  end;
end;

procedure CheckSaveBytes;
var
  B: TBitmap;
begin
  Section('byte-exact save (UIF-545)');
  B := TBitmap.Create;
  try
    B.SetSize(2, 2);
    SetPixelBytes(B, 0, 0, [$01, $02, $03, $04]);
    SetPixelBytes(B, 1, 0, [$05, $06, $07, $08]);
    SetPixelBytes(B, 0, 1, [$09, $0A, $0B, $0C]);
    SetPixelBytes(B, 1, 1, [$0D, $0E, $0F, $10]);
    S.Clear;
    B.SaveToStream(S);
    CheckEqS('42 4D 46 00 00 00 00 00 00 00 36 00 00 00 ' +
      '28 00 00 00 02 00 00 00 02 00 00 00 01 00 20 00 00 00 00 00 10 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 ' +
      '09 0A 0B 0C 0D 0E 0F 10 01 02 03 04 05 06 07 08', StreamHex(S), '2 x 2 at 32 bits, bottom row first');
    B.PixelFormat := pf24bit;
    S.Clear;
    B.SaveToStream(S);
    CheckEqS('42 4D 46 00 00 00 00 00 00 00 36 00 00 00 ' +
      '28 00 00 00 02 00 00 00 02 00 00 00 01 00 18 00 00 00 00 00 10 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 ' +
      '09 0A 0B 0D 0E 0F 00 00 01 02 03 05 06 07 00 00', StreamHex(S), '2 x 2 at 24 bits, padding bytes 0');
  finally
    B.Free;
  end;
end;

procedure CheckRoundTrip;
var
  A, B: TBitmap;
  X, Y: Integer;
  Same: Boolean;
begin
  Section('round trip');
  A := TBitmap.Create;
  try
    A.SetSize(5, 3);
    for Y := 0 to 2 do
      for X := 0 to 4 do
        SetPixelBytes(A, X, Y, [X * 40, Y * 80, X + Y, X * 50 + Y]);
    S.Clear;
    A.SaveToStream(S);
    B := Load;
    try
      Check(B.PixelFormat = pf32bit, '32-bit loads as pf32bit');
      CheckEqI(5, B.Width, '32-bit width');
      CheckEqI(3, B.Height, '32-bit height');
      Same := True;
      for Y := 0 to 2 do
        for X := 0 to 4 do
          if PixelHex(A, X, Y) <> PixelHex(B, X, Y) then
            Same := False;
      CheckEqB(True, Same, '32-bit pixels incl. alpha identical');
    finally
      B.Free;
    end;
    A.PixelFormat := pf24bit;
    S.Clear;
    A.SaveToStream(S);
    CheckEqI(54 + 16 * 3, S.Size, '24-bit file size with stride 16');
    B := Load;
    try
      Check(B.PixelFormat = pf24bit, '24-bit loads as pf24bit');
      Same := True;
      for Y := 0 to 2 do
        for X := 0 to 4 do
          if PixelHex(A, X, Y) <> PixelHex(B, X, Y) then
            Same := False;
      CheckEqB(True, Same, '24-bit pixels identical');
    finally
      B.Free;
    end;
  finally
    A.Free;
  end;
end;

procedure CheckPalettised;
var
  B: TBitmap;
  I: Integer;
begin
  Section('1-bit');
  { 3 x 2 bottom-up, palette (red, blue); bottom row 1 0 1, top row 0 1 1; rows padded to 4 bytes }
  S.Clear;
  FileHeader(14 + 40 + 8 + 8, 14 + 40 + 8);
  InfoHeader(40, 3, 2, 1, BI_RGB, 0);
  Quad($00, $00, $FF);
  Quad($FF, $00, $00);
  W8($A0); W8(0); W8(0); W8(0);
  W8($60); W8(0); W8(0); W8(0);
  B := Load;
  try
    Check(B.PixelFormat = pf24bit, 'palettised input expands to pf24bit');
    CheckEqI(3, B.Width, 'width');
    CheckEqI(2, B.Height, 'height');
    CheckEqS('00 00 FF', PixelHex(B, 0, 0), 'top-left = index 0 (red)');
    CheckEqS('FF 00 00', PixelHex(B, 1, 0), 'top-middle = index 1 (blue)');
    CheckEqS('FF 00 00', PixelHex(B, 2, 0), 'top-right = index 1');
    CheckEqS('FF 00 00', PixelHex(B, 0, 1), 'bottom-left = index 1');
    CheckEqS('00 00 FF', PixelHex(B, 1, 1), 'bottom-middle = index 0');
  finally
    B.Free;
  end;

  Section('4-bit with biClrUsed');
  { 3 x 1, three palette entries, pixels 2 0 1 }
  S.Clear;
  FileHeader(14 + 40 + 12 + 4, 14 + 40 + 12);
  InfoHeader(40, 3, 1, 4, BI_RGB, 3);
  Quad($10, $20, $30);
  Quad($40, $50, $60);
  Quad($70, $80, $90);
  W8($20); W8($10); W8(0); W8(0);
  B := Load;
  try
    CheckEqS('70 80 90', PixelHex(B, 0, 0), 'pixel 0 = entry 2');
    CheckEqS('10 20 30', PixelHex(B, 1, 0), 'pixel 1 = entry 0');
    CheckEqS('40 50 60', PixelHex(B, 2, 0), 'pixel 2 = entry 1');
  finally
    B.Free;
  end;

  Section('8-bit top-down');
  { 2 x 2, negative height: first row stored is the top row }
  S.Clear;
  FileHeader(14 + 40 + 1024 + 8, 14 + 40 + 1024);
  InfoHeader(40, 2, -2, 8, BI_RGB, 0);
  Quad($01, $02, $03);
  Quad($04, $05, $06);
  Quad($07, $08, $09);
  for I := 3 to 255 do
    Quad(0, 0, 0);
  W8(1); W8(2); W8(0); W8(0);
  W8(0); W8(1); W8(0); W8(0);
  B := Load;
  try
    CheckEqI(2, B.Height, 'top-down height is positive');
    CheckEqS('04 05 06', PixelHex(B, 0, 0), 'top row first');
    CheckEqS('07 08 09', PixelHex(B, 1, 0), 'top row second');
    CheckEqS('01 02 03', PixelHex(B, 0, 1), 'bottom row first');
  finally
    B.Free;
  end;
end;

procedure CheckBitfieldsAndHeaders;
var
  B: TBitmap;
  I: Integer;
begin
  Section('BI_BITFIELDS 32-bit');
  S.Clear;
  FileHeader(14 + 40 + 12 + 8, 14 + 40 + 12);
  InfoHeader(40, 2, 1, 32, BI_BITFIELDS, 0);
  W32($00FF0000);
  W32($0000FF00);
  W32($000000FF);
  W32($80112233);
  W32($00445566);
  B := Load;
  try
    Check(B.PixelFormat = pf32bit, 'bitfields load as pf32bit');
    CheckEqS('33 22 11 80', PixelHex(B, 0, 0), 'standard masks: B G R A bytes');
    CheckEqS('66 55 44 00', PixelHex(B, 1, 0), 'second pixel');
  finally
    B.Free;
  end;

  Section('V5 header');
  S.Clear;
  FileHeader(14 + 124 + 4, 14 + 124);
  InfoHeader(124, 1, 1, 24, BI_RGB, 0);
  for I := 1 to (124 - 40) div 4 do
    W32($DEADBEEF);
  W8($AA); W8($BB); W8($CC); W8(0);
  B := Load;
  try
    CheckEqI(1, B.Width, 'V5 width');
    CheckEqS('AA BB CC', PixelHex(B, 0, 0), 'V5 extra fields ignored');
  finally
    B.Free;
  end;

  Section('V4 header with bitfields in the header');
  S.Clear;
  FileHeader(14 + 108 + 4, 14 + 108);
  InfoHeader(108, 1, 1, 32, BI_BITFIELDS, 0);
  W32($00FF0000);
  W32($0000FF00);
  W32($000000FF);
  W32($FF000000);
  for I := 1 to (108 - 56) div 4 do
    W32(0);
  W32($7F010203);
  B := Load;
  try
    CheckEqS('03 02 01 7F', PixelHex(B, 0, 0), 'V4 bitfields pixel');
  finally
    B.Free;
  end;
end;

procedure CheckErrors;
begin
  Section('errors');
  S.Clear;
  ExpectLoadError('Bitmap image is not valid', 'empty stream');
  S.Clear;
  W16($4D43);
  W32(70);
  W32(0);
  W32(54);
  InfoHeader(40, 1, 1, 24, BI_RGB, 0);
  W32(0);
  ExpectLoadError('Bitmap image is not valid', 'bad signature');
  S.Clear;
  FileHeader(54 + 16, 54);
  InfoHeader(40, 2, 2, 24, BI_RGB, 0);
  W32(0);
  W32(0);
  ExpectLoadError('Bitmap image is not valid', 'truncated pixel data');
  S.Clear;
  FileHeader(54, 54);
  W32(40);
  W32(1);
  ExpectLoadError('Bitmap image is not valid', 'truncated info header');
  S.Clear;
  FileHeader(54 + 4, 54);
  InfoHeader(40, 1, 1, 8, 1, 0);
  W32(0);
  ExpectLoadError('Unsupported bitmap format', 'RLE8');
  S.Clear;
  FileHeader(54 + 4, 54);
  InfoHeader(40, 1, 1, 16, BI_RGB, 0);
  W32(0);
  ExpectLoadError('Unsupported bitmap format', '16 bits per pixel');
  S.Clear;
  FileHeader(14 + 12 + 4, 14 + 12);
  W32(12);
  W16(1);
  W16(1);
  W16(1);
  W16(24);
  W32(0);
  ExpectLoadError('Unsupported bitmap format', 'OS/2 core header');
  S.Clear;
  FileHeader(14 + 40 + 12 + 4, 14 + 40 + 12);
  InfoHeader(40, 1, 1, 32, BI_BITFIELDS, 0);
  W32($0000F800);
  W32($000007E0);
  W32($0000001F);
  W32(0);
  ExpectLoadError('Unsupported bitmap format', 'non-standard bitfield masks');
  S.Clear;
  FileHeader(54 + 4, 54);
  InfoHeader(40, 1, 1, 24, 4, 0);
  W32(0);
  ExpectLoadError('Unsupported bitmap format', 'JPEG in BMP');
end;

begin
  S := TMemoryStream.Create;
  try
    CheckSaveBytes;
    CheckRoundTrip;
    CheckPalettised;
    CheckBitfieldsAndHeaders;
    CheckErrors;
  finally
    S.Free;
  end;
  Done;
end.
