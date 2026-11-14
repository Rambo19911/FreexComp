// covers: RTL-258
program SU_Encoding;
{ sysutils.md section 7.5: the standard encodings (singletons, names, preambles), GetBytes / GetString in every
  encoding including invalid input (U+FFFD, never an exception), GetString ranges (EEncodingError), GetByteCount,
  GetCharCount, GetBufferEncoding (BOM detection, given encoding, default), BytesOf and StringOf. }
uses
  SysUtils, TestKit;

var
  { TODO(compiler): a nil literal as a TBytes argument is E2900 ('Constant nil of type TBytes is not emitted yet') }
  NilBytes: TBytes;

function Hex(const B: TBytes): string;
const
  Digits = '0123456789ABCDEF';
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Length(B) - 1 do
  begin
    if I > 0 then
      Result := Result + ' ';
    Result := Result + Digits[B[I] shr 4 + 1] + Digits[B[I] and 15 + 1];
  end;
end;

{ the bytes of a space-separated hex text such as 'EF BB BF' }
function Bytes(const S: string): TBytes;
var
  I, N, V: Integer;
  C: Char;
begin
  SetLength(Result, (Length(S) + 1) div 3);
  N := 0;
  V := 0;
  for I := 1 to Length(S) + 1 do
  begin
    if I <= Length(S) then
      C := S[I]
    else
      C := ' ';
    if C = ' ' then
    begin
      Result[N] := V;
      N := N + 1;
      V := 0;
    end
    else if C <= '9' then
      V := V * 16 + Ord(C) - Ord('0')
    else
      V := V * 16 + Ord(C) - Ord('A') + 10;
  end;
end;

function Units(const S: string): string;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Length(S) do
  begin
    if I > 1 then
      Result := Result + ' ';
    Result := Result + IntToHex(Ord(S[I]), 4);
  end;
end;

procedure TestSingletons;
begin
  Section('singletons and names');
  Check(TEncoding.UTF8 = TEncoding.UTF8, 'UTF8 is a singleton');
  Check(TEncoding.Default = TEncoding.UTF8, 'Default = UTF8');
  Check(TEncoding.ANSI = TEncoding.UTF8, 'ANSI = UTF8');
  Check(TEncoding.Unicode = TEncoding.Unicode, 'Unicode is a singleton');
  Check(TEncoding.Unicode <> TEncoding.BigEndianUnicode, 'Unicode <> BigEndianUnicode');
  Check(TEncoding.ASCII = TEncoding.ASCII, 'ASCII is a singleton');
  CheckEqS('UTF-8', TEncoding.UTF8.EncodingName, 'UTF8 name');
  CheckEqS('UTF-16LE', TEncoding.Unicode.EncodingName, 'Unicode name');
  CheckEqS('UTF-16BE', TEncoding.BigEndianUnicode.EncodingName, 'BigEndianUnicode name');
  CheckEqS('US-ASCII', TEncoding.ASCII.EncodingName, 'ASCII name');
  CheckEqS('EF BB BF', Hex(TEncoding.UTF8.GetPreamble), 'UTF8 preamble');
  CheckEqS('FF FE', Hex(TEncoding.Unicode.GetPreamble), 'Unicode preamble');
  CheckEqS('FE FF', Hex(TEncoding.BigEndianUnicode.GetPreamble), 'BigEndianUnicode preamble');
  CheckEqS('', Hex(TEncoding.ASCII.GetPreamble), 'ASCII preamble');
end;

procedure TestEncode;
var
  S: string;
begin
  Section('GetBytes');
  S := 'A' + #$00E9 + #$20AC + #$D83D + #$DE00;
  CheckEqS('41 C3 A9 E2 82 AC F0 9F 98 80', Hex(TEncoding.UTF8.GetBytes(S)), 'UTF8');
  CheckEqS('41 00 E9 00 AC 20 3D D8 00 DE', Hex(TEncoding.Unicode.GetBytes(S)), 'Unicode');
  CheckEqS('00 41 00 E9 20 AC D8 3D DE 00', Hex(TEncoding.BigEndianUnicode.GetBytes(S)), 'BigEndianUnicode');
  CheckEqS('41 3F 3F 3F 3F', Hex(TEncoding.ASCII.GetBytes(S)), 'ASCII: each code unit above 127 is ?');
  CheckEqS('EF BF BD', Hex(TEncoding.UTF8.GetBytes(#$D800)), 'UTF8: lone surrogate');
  CheckEqI(0, Length(TEncoding.UTF8.GetBytes('')), 'UTF8 of empty');
  CheckEqI(10, TEncoding.UTF8.GetByteCount(S), 'UTF8 GetByteCount');
  CheckEqI(10, TEncoding.Unicode.GetByteCount(S), 'Unicode GetByteCount');
  CheckEqI(5, TEncoding.ASCII.GetByteCount(S), 'ASCII GetByteCount');
end;

procedure TestDecode;
begin
  Section('GetString');
  CheckEqS('0041 00E9 20AC D83D DE00', Units(TEncoding.UTF8.GetString(Bytes('41 C3 A9 E2 82 AC F0 9F 98 80'))),
    'UTF8');
  CheckEqS('0041 FFFD 0028 FFFD 0042', Units(TEncoding.UTF8.GetString(Bytes('41 C3 28 80 42'))), 'UTF8: invalid bytes');
  CheckEqS('0041 00E9 D83D DE00', Units(TEncoding.Unicode.GetString(Bytes('41 00 E9 00 3D D8 00 DE'))), 'Unicode');
  CheckEqS('0041 FFFD', Units(TEncoding.Unicode.GetString(Bytes('41 00 42'))), 'Unicode: odd trailing byte');
  CheckEqS('FFFD 0041', Units(TEncoding.Unicode.GetString(Bytes('00 D8 41 00'))), 'Unicode: lone high surrogate');
  CheckEqS('FFFD', Units(TEncoding.Unicode.GetString(Bytes('00 DC'))), 'Unicode: lone low surrogate');
  CheckEqS('0041 00E9', Units(TEncoding.BigEndianUnicode.GetString(Bytes('00 41 00 E9'))), 'BigEndianUnicode');
  CheckEqS('0041 FFFD 007F', Units(TEncoding.ASCII.GetString(Bytes('41 80 7F'))), 'ASCII: bytes above 127');
  CheckEqS('', TEncoding.UTF8.GetString(NilBytes), 'UTF8 of nil');
  CheckEqS('bc', TEncoding.ASCII.GetString(Bytes('61 62 63 64'), 1, 2), 'ASCII range');
  CheckEqS('bc', TEncoding.UTF8.GetString(Bytes('61 62 63 64'), 1, 2), 'UTF8 range');
  CheckEqS('b', TEncoding.Unicode.GetString(Bytes('61 00 62 00'), 2, 2), 'Unicode range');
  CheckEqS('', TEncoding.UTF8.GetString(Bytes('61 62'), 2, 0), 'empty range at the end');
  CheckEqI(5, TEncoding.UTF8.GetCharCount(Bytes('41 C3 A9 E2 82 AC F0 9F 98 80')), 'UTF8 GetCharCount');
  CheckEqI(2, TEncoding.Unicode.GetCharCount(Bytes('41 00 42 00')), 'Unicode GetCharCount');
end;

procedure CheckRange(Index, Count: Integer; const Name: string);
var
  Msg: string;
begin
  Msg := 'no exception';
  try
    TEncoding.UTF8.GetString(Bytes('61 62 63'), Index, Count);
  except
    on E: Exception do
      Msg := E.ClassName + ': ' + E.Message;
  end;
  CheckEqS('EEncodingError: Invalid buffer range', Msg, Name);
end;

procedure TestRanges;
begin
  Section('GetString ranges');
  CheckRange(-1, 1, 'negative index');
  CheckRange(0, -1, 'negative count');
  CheckRange(2, 2, 'past the end');
  CheckRange(4, 0, 'index past the end');
end;

procedure TestBufferEncoding;
var
  E: TEncoding;
begin
  Section('GetBufferEncoding');
  E := nil;
  CheckEqI(3, TEncoding.GetBufferEncoding(Bytes('EF BB BF 41'), E), 'UTF8 BOM length');
  Check(E = TEncoding.UTF8, 'UTF8 BOM detected');
  E := nil;
  CheckEqI(2, TEncoding.GetBufferEncoding(Bytes('FF FE 41 00'), E), 'UTF-16LE BOM length');
  Check(E = TEncoding.Unicode, 'UTF-16LE BOM detected');
  E := nil;
  CheckEqI(2, TEncoding.GetBufferEncoding(Bytes('FE FF 00 41'), E), 'UTF-16BE BOM length');
  Check(E = TEncoding.BigEndianUnicode, 'UTF-16BE BOM detected');
  E := nil;
  CheckEqI(0, TEncoding.GetBufferEncoding(Bytes('41 42'), E), 'no BOM');
  Check(E = TEncoding.Default, 'no BOM gives Default');
  E := nil;
  CheckEqI(0, TEncoding.GetBufferEncoding(Bytes('41 42'), E, TEncoding.ASCII), 'no BOM with a default');
  Check(E = TEncoding.ASCII, 'no BOM gives ADefault');
  E := nil;
  CheckEqI(0, TEncoding.GetBufferEncoding(NilBytes, E), 'empty buffer');
  Check(E = TEncoding.Default, 'empty buffer gives Default');
  E := nil;
  CheckEqI(0, TEncoding.GetBufferEncoding(Bytes('EF BB'), E), 'truncated UTF8 BOM');
  E := TEncoding.Unicode;
  CheckEqI(2, TEncoding.GetBufferEncoding(Bytes('FF FE 41 00'), E), 'given encoding, matching preamble');
  Check(E = TEncoding.Unicode, 'given encoding kept');
  E := TEncoding.UTF8;
  CheckEqI(0, TEncoding.GetBufferEncoding(Bytes('FF FE 41 00'), E), 'given encoding, other preamble');
  Check(E = TEncoding.UTF8, 'given encoding kept without its preamble');
  E := TEncoding.ASCII;
  CheckEqI(0, TEncoding.GetBufferEncoding(Bytes('EF BB BF'), E), 'given encoding without a preamble');
end;

procedure TestBytesOf;
begin
  Section('BytesOf, StringOf');
  CheckEqS('61 C3 A9', Hex(BytesOf('a' + #$00E9)), 'BytesOf');
  CheckEqS('0061 00E9', Units(StringOf(Bytes('61 C3 A9'))), 'StringOf');
  CheckEqI(0, Length(BytesOf('')), 'BytesOf of empty');
  CheckEqS('', StringOf(NilBytes), 'StringOf of nil');
end;

begin
  TestSingletons;
  TestEncode;
  TestDecode;
  TestRanges;
  TestBufferEncoding;
  TestBytesOf;
  Done;
end.
