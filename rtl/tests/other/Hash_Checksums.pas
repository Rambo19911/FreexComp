// phase: 8
// covers: RTL-1243, RTL-1244, RTL-1245, RTL-1246, RTL-1235, RTL-1234, RTL-1240, RTL-1247
program Hash_Checksums;
{ Hash, checksums and fast hashes (rtl/phase8/encodings-and-hashes.md RTL-1243 to RTL-1246, vectors 6.3 D):
  THashCRC32 (check value of the bytes "123456789", also in pieces and from a stream), THashFNV1a32 / THashFNV1a64
  (test values of RFC 9923), THashBobJenkins (the self-test values of lookup3, lengths around its 12-byte block, chained
  Update calls). Byte input goes through the untyped and the byte-array overloads; a string is hashed as its UTF-16
  units as stored, which differs from its UTF-8 bytes; results most significant byte first; argument errors. }
uses
  SysUtils, Classes, Hash, HashKit, TestKit;

const
  Four = 'Four score and seven years ago';
  { Create without Update; columns here and below: SumNames }
  Initial: array[0..3] of string = ('00000000', '811c9dc5', 'cbf29ce484222325', '00000000');
  { the strings '', 'abc', '123456789', U+00E9, 'a' U+00E9 U+1F600 and a lone U+D83D as UTF-16 units }
  Units: array[0..5, 0..3] of string = (
    ('00000000', '811c9dc5', 'cbf29ce484222325', 'deadbeef'),
    ('ad957ab0', 'ae1e997d', 'cec64e155111225d', 'fc0815e0'),
    ('a290e877', 'd14e91e4', '6996fb63188fc884', 'f201f6a5'),
    ('ce655a5a', '59fb6f0c', '0a6a1207b6cd9fac', '37c6e89f'),
    ('a8988e21', 'c529e37a', 'b773c61ee746519a', '031eaf60'),
    ('a33900a7', 'ebf56c10', '0805a207b4c4ae30', 'a539ad5b'));
  { strings 1, 3 and 4 as UTF-8 bytes: the values a string overload must not give }
  Utf8: array[0..2, 0..3] of string = (
    ('352441c2', '1a47e90b', 'e71fa2190541574b', '0e397631'),
    ('0e048d3e', '1e9de8c1', '0ac21707b7181e01', 'e93a01fc'),
    ('7eef638a', 'd86197e1', '904cc0a5e4793b01', '7be0843c'));
  Utf8Row: array[0..2] of Integer = (1, 3, 4);
  Pieces: array[0..2] of Integer = (1, 7, 4096);

{ byte input: the untyped overload, the byte-array overload and GetHashValue give Expected (RTL-1235, RTL-1240);
  HashAsBytes holds the value most significant byte first and no result changes the record (RTL-1246) }
procedure Row(Kind: Integer; const Name: string; const Data: TBytes; const Expected: string);
var
  A: TAnySum;
  D: TBytes;
  Title: string;
begin
  Title := SumNames[Kind] + ' of ' + Name;
  A.Start(Kind);
  A.Add(PByte(Data)^, Length(Data));
  CheckEqS(Expected, A.Text, Title + ': untyped data');
  D := A.Bytes;
  Check((2 * Length(D) = Length(Expected)) and (THash.DigestAsString(D) = Expected), Title + ': HashAsBytes, high byte first');
  Check((SumHex(Kind, A.Value) = Expected) and (A.Text = Expected), Title + ': HashAsInteger; the record is unchanged');
  if Kind <> 2 then
    CheckEqI(A.Value, THash.DigestAsInteger(D), Title + ': DigestAsInteger(HashAsBytes) = HashAsInteger');
  A.Start(Kind);
  A.AddBytes(Data, 0);
  CheckEqS(Expected, A.Text, Title + ': byte array');
  CheckEqS(Expected, SumHex(Kind, SumValueOf(Kind, PByte(Data)^, Length(Data))), Title + ': GetHashValue');
end;

{ the same bytes through CRC-32, FNV-1a 32, FNV-1a 64 and lookup3 }
procedure Rows(const Name: string; const Data: TBytes; const E0, E1, E2, E3: string);
begin
  Row(0, Name, Data, E0);
  Row(1, Name, Data, E1);
  Row(2, Name, Data, E2);
  Row(3, Name, Data, E3);
end;

procedure RunBytes;
var
  A: TAnySum;
  Data, Part: TBytes;
  Stream: TSlowStream;
  C: THashCRC32;
  I, N, Kind: Integer;
begin
  Section('byte input (RTL-1243, RTL-1244, RTL-1245, RTL-1246)');
  Rows('""', nil, '00000000', '811c9dc5', 'cbf29ce484222325', 'deadbeef');
  Rows('"a"', ToBytes('a'), 'e8b7be43', 'e40c292c', 'af63dc4c8601ec8c', '58d68708');
  Rows('"abc"', ToBytes('abc'), '352441c2', '1a47e90b', 'e71fa2190541574b', '0e397631');
  Rows('"123456789"', ToBytes('123456789'), 'cbf43926', 'bb86b11c', '06d5573923c6cdfc', '845d9a96');
  Rows('61 00 62 00 63 00', ToBytes('a'#0'b'#0'c'#0), 'ad957ab0', 'ae1e997d', 'cec64e155111225d', 'fc0815e0');
  Rows('P(4099)', Pattern(4099), '48d1721d', '71b90358', 'c4ac6edf004a2f58', '1a503c35');
  Row(0, 'the quick brown fox', ToBytes('The quick brown fox jumps over the lazy dog'), '414fa339');
  Row(0, '32 x 00', ToBytes(StringOfChar(#0, 32)), '190a55ad');
  Row(0, '32 x FF', ToBytes(StringOfChar(#$FF, 32)), 'ff6cab0b');
  Row(0, 'the bytes 00 to 1F', Pattern(32), '91267e8a');
  Row(0, 'M4', ToBytes(StringOfChar('a', 1000000)), 'dc25bfbc');
  Row(1, '"foobar"', ToBytes('foobar'), 'bf9cf968');
  Row(2, '"foobar"', ToBytes('foobar'), '85944171f73967e8');
  Row(1, '"Hello!" 01 FF ED', ToBytes('Hello!'#1#$FF#$ED), 'fd9d3881');
  Row(2, '"Hello!" 01 FF ED', ToBytes('Hello!'#1#$FF#$ED), 'bd51ea7094ee6fa1');
  Row(1, 'M4', ToBytes(StringOfChar('a', 1000000)), '8569d985');
  Row(2, 'M4', ToBytes(StringOfChar('a', 1000000)), '24c638d05c2865e5');
  Row(3, '"abcd"', ToBytes('abcd'), 'b5f4889c');
  Row(3, '"abcdefgh"', ToBytes('abcdefgh'), '2995c3be');
  Row(3, '11 bytes', ToBytes('abcdefghijk'), '5f61edf8');
  Row(3, '12 bytes, one last block', ToBytes('abcdefghijkl'), '4012f87b');
  Row(3, '13 bytes', ToBytes('abcdefghijklm'), '928128f9');
  Row(3, '24 bytes', ToBytes('abcdefghijklmnopqrstuvwx'), '1b631fea');
  Row(3, '25 bytes', ToBytes('abcdefghijklmnopqrstuvwxy'), '6c29c5e2');
  Row(3, 'Four score', ToBytes(Four), '17770551');

  Section('pieces, a stream, the one-call forms of CRC-32 for bytes (RTL-1235, RTL-1240, RTL-1243)');
  Data := Pattern(4099);
  for Kind := 0 to 2 do
    for N := 0 to 2 do
    begin
      A.Start(Kind);
      I := 0;
      while I < 4099 do
      begin
        Part := Copy(Data, I, Pieces[N]);
        A.AddBytes(Part, 0);
        I := I + Length(Part);
      end;
      CheckEqS(SumHex(Kind, SumValueOf(Kind, Data[0], 4099)), A.Text,
        SumNames[Kind] + ' of P(4099) in pieces of ' + IntToStr(Pieces[N]) + ': as in one call');
    end;
  Stream := TSlowStream.Create;
  Stream.WriteBuffer(Data[0], 4099);
  Stream.Position := 0;
  Stream.Limit := 1000;
  C := THashCRC32.Create;
  C.Update(Stream);
  CheckEqS('48d1721d', C.HashAsString, 'CRC-32 of a stream holding P(4099) that returns 1000 bytes per Read');
  CheckEqI(65536, Stream.MaxAsked, 'read through a buffer of 65 536 bytes');
  Stream.Free;
  Stream := TextStream('xxabc', 2);
  C.Reset;
  C.Update(Stream);
  Check((C.HashAsString = '352441c2') and (Stream.Position = 5), 'after Reset: a stream from its position to its end');
  Stream.Position := 3;
  C := THashCRC32.Create;
  C.Update(ToBytes('a'));
  C.Update(Stream);
  CheckEqS('352441c2', C.HashAsString, 'Update(bytes), then Update(stream)');
  Stream.Free;
  Data := ToBytes('123456789');
  CheckEqS('cbf43926', THashCRC32.GetHashString(Data), 'THashCRC32.GetHashString(bytes): the check value');
  CheckEqS('cbf43926', THash.DigestAsString(THashCRC32.GetHashBytes(Data)), 'GetHashBytes(bytes)');
  CheckEqI(-873187034, THashCRC32.GetHashValue(Data), 'GetHashValue(bytes) = Integer($CBF43926): negative');
  Data := ToBytes('a');
  CheckEqI(-5808556873153909620, THashFNV1a64.GetHashValue(Data[0], 1), 'FNV-1a 64 of "a" = Int64($AF63DC4C8601EC8C)');
end;

procedure RunStrings;
var
  S: array[0..5] of string;
  A: TAnySum;
  R, Kind: Integer;
  Name: string;
begin
  Section('a string is hashed as its UTF-16 units as stored, not as its UTF-8 bytes (RTL-1235, RTL-1240)');
  S[0] := '';
  S[1] := 'abc';
  S[2] := '123456789';
  S[3] := #$00E9;
  S[4] := 'a'#$00E9#$D83D#$DE00;
  S[5] := #$D83D;
  for R := 0 to 5 do
    for Kind := 0 to 3 do
    begin
      Name := SumNames[Kind] + ', string ' + IntToStr(R);
      A.Start(Kind);
      A.AddText(S[R]);
      CheckEqS(Units[R, Kind], A.Text, Name + ': Update(string)');
      CheckEqS(Units[R, Kind], SumString(Kind, S[R]), Name + ': GetHashString(string)');
      CheckEqS(Units[R, Kind], THash.DigestAsString(SumBytes(Kind, S[R])), Name + ': GetHashBytes(string)');
      CheckEqS(Units[R, Kind], SumHex(Kind, SumValue(Kind, S[R])), Name + ': GetHashValue(string)');
      CheckEqS(Units[R, Kind], SumHex(Kind, SumValueOf(Kind, PChar(S[R])^, Length(S[R]) * SizeOf(Char))),
        Name + ': its units as untyped data');
    end;
  for R := 0 to 2 do
    for Kind := 0 to 3 do
    begin
      A.Start(Kind);
      A.AddBytes(TEncoding.UTF8.GetBytes(S[Utf8Row[R]]), 0);
      CheckEqS(Utf8[R, Kind], A.Text, SumNames[Kind] + ', string ' + IntToStr(Utf8Row[R]) + ': its UTF-8 bytes as a byte array');
    end;
end;

procedure RunJenkins;
var
  J, K: THashBobJenkins;
  D, P, Q: TBytes;
begin
  Section('lookup3: initial value, running value, chained Update calls (RTL-1244)');
  D := ToBytes(Four);
  CheckEqS('deadbeef', SumHex(3, THashBobJenkins.GetHashValue(D[0], 0, 0)), 'L("", 0, 0)');
  CheckEqS('bd5b7dde', SumHex(3, THashBobJenkins.GetHashValue(D[0], 0, Integer($DEADBEEF))), 'L("", 0, $DEADBEEF)');
  CheckEqS('17770551', SumHex(3, THashBobJenkins.GetHashValue(D[0], 30)), 'L(Four score, 30): the initial value is 0');
  CheckEqS('cd628161', SumHex(3, THashBobJenkins.GetHashValue(D[0], 30, 1)), 'L(Four score, 30, 1)');
  P := ToBytes('abcdefghijkl');
  CheckEqS('a0c525be', SumHex(3, THashBobJenkins.GetHashValue(P[0], 12, 1)), 'L(12 bytes, 12, 1)');
  CheckEqS('da5c72f0', SumHex(3, THashBobJenkins.GetHashValue(P[0], 3, -1)), 'L("abc", 3, -1)');
  J := THashBobJenkins.Create;
  Check((J.HashAsInteger = 0) and (J.HashAsString = '00000000'), 'Create without Update: 0');
  J.Update('');
  CheckEqS('deadbeef', J.HashAsString, 'Update of an empty string applies the function to no data');
  J.Update(D[0], 0);
  CheckEqS('bd5b7dde', J.HashAsString, 'and so does a further call of length 0');
  Q := nil;
  J.Update(Q);
  CheckEqS('9c093ccd', J.HashAsString, 'and one with an empty array');
  J.Reset(1);
  J.Update(D[0], 30);
  CheckEqS('cd628161', J.HashAsString, 'Reset(1), Update(30 bytes)');
  J.Reset;
  CheckEqI(0, J.HashAsInteger, 'Reset without a value: 0');
  J.Update(D[0], 15);
  K := J;
  J.Update(D[15], 15);
  CheckEqS('7c89156a', J.HashAsString, 'two calls of 15 bytes are chained: not the value of one call');
  K.Update(Copy(D, 15, 15));
  CheckEqS('7c89156a', K.HashAsString, 'a copy made after the first call continues alike');
  J := THashBobJenkins.Create;
  J.Update(D[0], 1);
  J.Update(D[1], 29);
  CheckEqS('70b40d3b', J.HashAsString, 'one byte, then the other 29');
  J := THashBobJenkins.Create;
  J.Update(Four);
  CheckEqS('902fc405', J.HashAsString, 'the string of 30 characters (60 bytes) in one call');
  J := THashBobJenkins.Create;
  J.Update('Four score and ');
  J.Update('seven years ago');
  CheckEqS('6b39f6d5', J.HashAsString, 'the string in two calls');
  P := Pattern(4099);
  Q := Copy(P, 1, 4098);
  CheckEqI(THashBobJenkins.GetHashValue(Q[0], 4098), THashBobJenkins.GetHashValue(P[1], 4098), 'the same bytes at an odd address');
end;

procedure RunRules;
var
  A, B: TAnySum;
  Data, Foo, Bar: TBytes;
  Kind: Integer;
  S: string;
  V: Int64;
  C: THashCRC32;
begin
  Section('state records (RTL-1234), continuing a value and the constants (RTL-1245)');
  Data := Pattern(300);
  Foo := ToBytes('foo');
  Bar := ToBytes('bar');
  S := StringOfChar('x', 40);
  V := 0;
  for Kind := 0 to 3 do
  begin
    A.Start(Kind);
    CheckEqS(Initial[Kind], A.Text, SumNames[Kind] + ': Create');
    A.AddBytes(Foo, 0);
    B := A;
    A.AddBytes(Bar, 0);
    B.AddBytes(Bar, 2);
    B.AddBytes(Copy(Bar, 2, 1), 1);
    if Kind < 3 then
      Check((A.Text = B.Text) and (A.Value = SumValueOf(Kind, PByte(ToBytes('foobar'))^, 6)),
        SumNames[Kind] + ': a copy continues on its own; the split of the data does not matter');
    A.Reset;
    CheckEqS(Initial[Kind], A.Text, SumNames[Kind] + ': Reset');
    BeginCount;
    A.Start(Kind);
    A.Add(Data[0], 300);
    A.AddBytes(Data, 0);
    A.AddBytes(Data, 77);
    A.AddText(S);
    B := A;
    B.Reset;
    V := V xor A.Value xor SumValue(Kind, S) xor SumValueOf(Kind, Data[0], 300);
    CheckEqI(0, EndCount, SumNames[Kind] + ': Create, Update (also of a string), HashAsInteger and GetHashValue allocate nothing');
  end;
  Check(V <> 0, 'the values were computed');
  CheckEqS('bf9cf968', SumHex(1, THashFNV1a32.GetHashValue(Bar[0], 3, Cardinal(THashFNV1a32.GetHashValue(Foo[0], 3)))),
    'FNV-1a 32: GetHashValue("bar", 3, value of "foo") is the value of "foobar"');
  CheckEqS('85944171f73967e8', SumHex(2, THashFNV1a64.GetHashValue(Bar[0], 3, UInt64(THashFNV1a64.GetHashValue(Foo[0], 3)))),
    'FNV-1a 64: the same');
  Check((THashFNV1a32.FNV_PRIME = $01000193) and (THashFNV1a32.FNV_SEED = $811C9DC5), 'THashFNV1a32.FNV_PRIME, FNV_SEED');
  Check((THashFNV1a64.FNV_PRIME = $100000001B3) and (IntToHex(THashFNV1a64.FNV_SEED, 16) = 'CBF29CE484222325'),
    'THashFNV1a64.FNV_PRIME, FNV_SEED');

  Section('arguments (RTL-1247)');
  for Kind := 0 to 3 do
  begin
    A.Start(Kind);
    Expect('EArgumentOutOfRangeException', 'Argument out of range');
    try A.AddBytes(Data, 301); except on E: Exception do CheckRaises(SumNames[Kind] + ': Update(bytes, one more than the array holds)', E.ClassName, E.Message); end;
    CheckEqS(Initial[Kind], A.Text, SumNames[Kind] + ': and nothing was added');
  end;
  try THashBobJenkins.GetHashValue(Data[0], -1); except on E: Exception do CheckRaises('THashBobJenkins.GetHashValue with a negative length', E.ClassName, E.Message); end;
  Expect('EArgumentNilException', 'Argument must not be nil');
  C := THashCRC32.Create;
  try C.Update(TStream(nil)); except on E: Exception do CheckRaises('THashCRC32.Update(nil stream)', E.ClassName, E.Message); end;
end;

var
  Before: NativeInt;
begin
  { the UTF-8 encoding object lives until the program ends: it exists before the count is taken }
  THashMD5.GetHashString('x');
  Before := _LiveAllocCount;
  RunBytes;
  RunStrings;
  RunJenkins;
  RunRules;
  CheckEqI(Before, _LiveAllocCount, 'no block is left');
  Done;
end.
