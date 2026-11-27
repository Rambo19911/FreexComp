// phase: 8
// covers: RTL-1204
program NetEnc_Base64Streams;
{ NetEncoding, Base64 stream forms and the 1 MB round trip (rtl/phase8/encodings-and-hashes.md RTL-1204, vectors
  6.2 D): 1 048 576 pseudo-random bytes through the string, byte-array and stream forms of the three shared
  instances give the same text, of the stated length, and decode back to the same bytes; probe streams record the
  largest block asked of Read and handed to Write; reading starts at the current position of the input, writing at
  the current position of the output; an empty input writes nothing; a nil stream raises; a strict object reports
  the position of a foreign byte in a stream. }
{$POINTERMATH ON}
uses
  SysUtils, Classes, NetEncoding, TestKit;

const
  Size = 1048576;

type
  { records the largest Count passed to Read and to Write }
  TProbeStream = class(TMemoryStream)
  public
    MaxRead, MaxWrite: Integer;
    function Read(var Buffer; Count: Integer): Integer; override;
    function Write(const Buffer; Count: Integer): Integer; override;
  end;

function TProbeStream.Read(var Buffer; Count: Integer): Integer;
begin
  if Count > MaxRead then
    MaxRead := Count;
  Result := inherited Read(Buffer, Count);
end;

function TProbeStream.Write(const Buffer; Count: Integer): Integer;
begin
  if Count > MaxWrite then
    MaxWrite := Count;
  Result := inherited Write(Buffer, Count);
end;

var
  Data: TBytes;

{ True when Stream holds exactly the Count bytes at P }
function Holds(Stream: TMemoryStream; P: Pointer; Count: Integer): Boolean;
begin
  Result := (Stream.Size = Count) and ((Count = 0) or CompareMem(Stream.Memory, P, Count));
end;

{ the Count bytes of Stream from Offset as text, one character per byte }
function TextAt(Stream: TMemoryStream; Offset, Count: Integer): string;
var
  I: Integer;
begin
  SetLength(Result, Count);
  for I := 0 to Count - 1 do
    Result[I + 1] := Char((PByte(Stream.Memory) + Offset + I)^);
end;

{ appends S to Stream, one byte per character }
procedure PutText(Stream: TStream; const S: string);
var
  I: Integer;
  B: Byte;
begin
  for I := 1 to Length(S) do
  begin
    B := Byte(Ord(S[I]));
    Stream.WriteBuffer(B, 1);
  end;
end;

{ True when no line of S but the last is other than 76 characters long, every separator is CR LF and S does not end
  with one; Lines receives the number of lines }
function LinesOf76(const S: string; var Lines: Integer): Boolean;
var
  I, Len: Integer;
begin
  Result := True;
  Lines := 0;
  I := 1;
  while I <= Length(S) do
  begin
    Len := 0;
    while (I + Len <= Length(S)) and (S[I + Len] <> #13) and (S[I + Len] <> #10) do
      Inc(Len);
    Inc(Lines);
    I := I + Len;
    if I <= Length(S) then
    begin
      Result := Result and (Len = 76) and (S[I] = #13) and (I + 2 <= Length(S)) and (S[I + 1] = #10);
      I := I + 2;
    end;
  end;
end;

{ S receives the text of Data }
procedure RoundTrip(Enc: TNetEncoding; const Name: string; TextLength: Integer; var S: string);
var
  Coded, Back: TBytes;
  I: Integer;
  Same: Boolean;
  Src, Mid, Dst: TProbeStream;
begin
  S := Enc.EncodeBytesToString(Data);
  CheckEqI(TextLength, Length(S), Name + ': length of the text');
  Back := Enc.DecodeStringToBytes(S);
  Check((Length(Back) = Size) and CompareMem(@Back[0], @Data[0], Size), Name + ': DecodeStringToBytes gives the bytes back');
  Coded := Enc.Encode(Data);
  Same := Length(Coded) = Length(S);
  if Same then
    for I := 0 to Length(Coded) - 1 do
      if Coded[I] <> Ord(S[I + 1]) then
      begin
        Same := False;
        Break;
      end;
  Check(Same, Name + ': the byte-array form is the text, one byte per character');
  Back := Enc.Decode(Coded);
  Check((Length(Back) = Size) and CompareMem(@Back[0], @Data[0], Size), Name + ': Decode(bytes) gives the bytes back');

  Src := TProbeStream.Create;
  Mid := TProbeStream.Create;
  Dst := TProbeStream.Create;
  Src.WriteBuffer(Data[0], Size);
  Src.Position := 0;
  CheckEqI(TextLength, Enc.Encode(Src, Mid), Name + ': Encode(streams) returns the bytes written');
  Check(Holds(Mid, @Coded[0], Length(Coded)), Name + ': the stream form writes the bytes of the byte-array form');
  CheckEqI(Size, Src.Position, Name + ': the input was read to its end');
  Mid.Position := 0;
  CheckEqI(Size, Enc.Decode(Mid, Dst), Name + ': Decode(streams) returns the bytes written');
  Check(Holds(Dst, @Data[0], Size), Name + ': and gives the bytes back');
  Check((Src.MaxRead > 0) and (Src.MaxRead <= 65536), Name + ': encoding reads at most 65536 bytes at a time');
  Check((Mid.MaxWrite > 0) and (Mid.MaxWrite <= 131072), Name + ': encoding writes at most 131072 bytes at a time');
  Check((Mid.MaxRead > 0) and (Mid.MaxRead <= 65536), Name + ': decoding reads at most 65536 bytes at a time');
  Check((Dst.MaxWrite > 0) and (Dst.MaxWrite <= 131072), Name + ': decoding writes at most 131072 bytes at a time');
  Src.Free;
  Mid.Free;
  Dst.Free;
end;

procedure Run;
var
  I, Lines: Integer;
  S: string;
  Src, Dst: TMemoryStream;
  Strict: TBase64StringEncoding;
  Narrow: TBase64Encoding;
  Probe: TProbeStream;
begin
  RandSeed := 131;
  SetLength(Data, Size);
  for I := 0 to Size - 1 do
    Data[I] := Byte(Random(256));

  Section('1 MB through Base64String');
  RoundTrip(TNetEncoding.Base64String, 'Base64String', 1398104, S);
  CheckEqS('==', Copy(S, Length(S) - 1, 2), 'the text ends with two pad characters');
  Check(Pos(#13, S) + Pos(#10, S) = 0, 'no line separator');

  Section('1 MB through Base64');
  RoundTrip(TNetEncoding.Base64, 'Base64', 1434896, S);
  Check(LinesOf76(S, Lines), 'every line but the last is 76 characters, CR LF between lines, none at the end');
  CheckEqI(18397, Lines, 'lines');

  Section('1 MB through Base64URL');
  RoundTrip(TNetEncoding.Base64URL, 'Base64URL', 1398102, S);
  Check(Pos('=', S) + Pos('+', S) + Pos('/', S) + Pos(#13, S) + Pos(#10, S) = 0, 'none of = + / CR LF');
  Check((Pos('-', S) > 0) and (Pos('_', S) > 0), 'the URL-safe characters occur');

  Section('a long separator is written in blocks too');
  Narrow := TBase64Encoding.Create(1, '        '#13#10);
  Src := TMemoryStream.Create;
  Probe := TProbeStream.Create;
  Src.WriteBuffer(Data[0], 60000);
  Src.Position := 0;
  CheckEqI(80000 + 79999 * 10, Narrow.Encode(Src, Probe), 'one character per line: bytes written');
  Check(Probe.MaxWrite <= 131072, 'at most 131072 bytes at a time');
  Probe.Position := 0;
  Src.Clear;
  CheckEqI(60000, Narrow.Decode(Probe, Src), 'decoded again');
  Check(Holds(Src, @Data[0], 60000), 'to the same bytes');
  Narrow.Free;
  Probe.Free;

  Section('positions (RTL-1204)');
  Src.Clear;
  Dst := TMemoryStream.Create;
  PutText(Src, 'abcdefghij');
  Src.Position := 3;
  Dst.WriteBuffer(Data[0], 2);
  CheckEqI(12, TNetEncoding.Base64String.Encode(Src, Dst), 'an input at position 3: bytes written');
  CheckEqI(10, Src.Position, 'the input ends where reading ended');
  CheckEqI(14, Dst.Size, 'the output was written at its position');
  CheckEqI(14, Dst.Position, 'the output position is behind the text');
  CheckEqS('ZGVmZ2hpag==', TextAt(Dst, 2, 12), 'bytes 3 to the end were encoded');
  Dst.Position := 2;
  Src.Position := 4;
  CheckEqI(7, TNetEncoding.Base64String.Decode(Dst, Src), 'decoding from position 2 writes at position 4');
  CheckEqI(11, Src.Size, 'the size of the output');
  CheckEqS('abcddefghij', TextAt(Src, 0, 11), 'the decoded bytes follow the first four');

  Section('an empty input');
  Dst.Clear;
  CheckEqI(0, TNetEncoding.Base64.Encode(Src, Dst), 'Encode at the end of the input returns 0');
  CheckEqI(0, TNetEncoding.Base64.Decode(Src, Dst), 'Decode at the end of the input returns 0');
  Src.Clear;
  CheckEqI(0, TNetEncoding.Base64URL.Encode(Src, Dst), 'Encode of an empty stream returns 0');
  CheckEqI(0, TNetEncoding.Base64URL.Decode(Src, Dst), 'Decode of an empty stream returns 0');
  CheckEqI(0, Dst.Size, 'nothing was written');

  Section('nil streams (RTL-1219)');
  Expect('EArgumentNilException', 'Argument must not be nil');
  try TNetEncoding.Base64.Encode(nil, Dst); except on E: Exception do CheckRaises('Encode(nil, Output)', E.ClassName, E.Message); end;
  try TNetEncoding.Base64.Encode(Src, nil); except on E: Exception do CheckRaises('Encode(Input, nil)', E.ClassName, E.Message); end;
  try TNetEncoding.Base64.Decode(nil, Dst); except on E: Exception do CheckRaises('Decode(nil, Output)', E.ClassName, E.Message); end;
  try TNetEncoding.Base64.Decode(Src, nil); except on E: Exception do CheckRaises('Decode(Input, nil)', E.ClassName, E.Message); end;

  Section('a strict object reports the position of a byte in a stream');
  Strict := TBase64StringEncoding.Create(0, '', bdStrict);
  PutText(Src, StringOfChar('A', 69999) + '*' + StringOfChar('A', 30000));
  Src.Position := 0;
  Expect('EConvertError', 'Invalid Base64 character at position 70000');
  try Strict.Decode(Src, Dst); except on E: Exception do CheckRaises('byte 70000 is *', E.ClassName, E.Message); end;
  Check(Dst.Size <= 52497, 'nothing behind the error was written');
  Src.Position := 10;
  Dst.Clear;
  Expect('EConvertError', 'Invalid Base64 character at position 69990');
  try Strict.Decode(Src, Dst); except on E: Exception do CheckRaises('positions count the bytes read', E.ClassName, E.Message); end;
  Strict.Free;
  Src.Free;
  Dst.Free;
  Data := nil;
end;

var
  Before: NativeInt;
begin
  { the shared instances and the UTF-8 encoding live until the program ends: they exist before the count is taken }
  TNetEncoding.Base64.Decode(TNetEncoding.Base64String.Encode(TNetEncoding.Base64URL.Encode('x')));
  Before := _LiveAllocCount;
  Run;
  CheckEqI(Before, _LiveAllocCount, 'no block is left');
  Done;
end.
