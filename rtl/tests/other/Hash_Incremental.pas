// phase: 8
// covers: RTL-1234, RTL-1235, RTL-1236, RTL-1247
program Hash_Incremental;
{ Hash, incremental use of the eight digest functions (rtl/phase8/encodings-and-hashes.md section 4.2, vectors
  6.3 B): 4099 bytes added in pieces of 1, 63, 64, 65, 127, 128 and 129 bytes through the untyped and the byte-array
  overload give the digest of one call; a digest may be read while data is still being added; a copy made by
  assignment continues on its own; Reset; empty input changes nothing; Update allocates nothing; streams are read
  from their position to their end through one 64 KiB buffer, also when Read returns less than asked; argument
  errors with class and message. }
uses
  SysUtils, Classes, Hash, HashKit, TestKit;

const
  { the tables are indexed like HashNames: 0 = MD5, 1 = SHA-1, 2 to 7 = the versions of THashSHA2 }
  Pieces: array[0..6] of Integer = (1, 63, 64, 65, 127, 128, 129);
  { digests of P(4099) }
  Whole: array[0..7] of string = (
    '6ae5e473e2f5f0d2cc8df44275653e39',
    'e19a1105976b5abe75c86376bf4e1f6e4f19bafd',
    '884237a170f233185d0ee1eee2097298bd4c52e47cdc32c03b662e6f',
    '5bfe486adf0afd901e2fb5cc72f10fa382239697a82f04e2e8de9b3d789b5771',
    '9c754e8d1057b94e8fe200fdf3a16f799891ba315b9bc9e5c8cce00bc98580e4dcfadc14cb79a718e5021f4c9d6d8421',
    '9db319e1678ce4f4a78ec862ae0549eb4a2ba80a5f846886cae26197403366d570ac13b496c7f63cd71e2df4eb626dbeafa0ce05bf26db07724bc8e688d76ec2',
    '900eb1939c262fdac0fd6da3c043b1ff5bf4a4cdf5315b55ab9b2112',
    '7d9022ec98e974cf5245b47731670ce6fec0f79bde0eb0eeee922431f620829b');
  { digests of no data, of "ab" and of "abc" }
  Empty: array[0..7] of string = (
    'd41d8cd98f00b204e9800998ecf8427e',
    'da39a3ee5e6b4b0d3255bfef95601890afd80709',
    'd14a028c2a3a2bc9476102bb288234c415a2b01f828ea62ac5b3e42f',
    'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
    '38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da274edebfe76f65fbd51ad2f14898b95b',
    'cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e',
    '6ed0dd02806fa89e25de060c19d3ac86cabb87d6a0ddd05c333b84f4',
    'c672b8d1ef56ed28ab87c3622c5114069bdd3ad7b8f9737498d0c01ecef0967a');
  Ab: array[0..7] of string = (
    '187ef4436122d1cc2f40dc2b92f0eba0',
    'da23614e02469a0d7c7bd1bdab5c9c474b1904dc',
    'db3cda86d4429a1d39c148989566b38f7bda0156296bd364ba2f878b',
    'fb8e20fc2e4c3f248c60c39bd652f3c1347298bb977b8b4d5903b85055620603',
    'c7be03ba5bcaa384727076db0018e99248e1a6e8bd1b9ef58a9ec9dd4eeebb3f48b836201221175befa74ddc3d35afdd',
    '2d408a0717ec188158278a796c689044361dc6fdde28d6f04973b80896e1823975cdbf12eb63f9e0591328ee235d80e9b5bf1aa6a44f4617ff3caf6400eb172d',
    'b35878d07bfedf39fc638af08547eb5d1072d8546319f247b442fbf5',
    '22d4d37ec6370571af7109fb12eae79673d5f7c83e6e677083faa3cfac3b2c14');
  Abc: array[0..7] of string = (
    '900150983cd24fb0d6963f7d28e17f72',
    'a9993e364706816aba3e25717850c26c9cd0d89d',
    '23097d223405d8228642a477bda255b32aadbce4bda0b3f7e36c9da7',
    'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad',
    'cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7',
    'ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f',
    '4634270f707b6a54daae7530460842e20e37ed265ceee9a43e8924aa',
    '53048e2681941ef99b2e29b76b4c7dabe4c2d0c634fc6d46e0e2f13107e7af23');
  { digests of 1 000 000 x "a" and of P(1 048 576) }
  MillionMD5 = '7707d6ae4e027c70eea2a935c2296f21';
  MillionSHA1 = '34aa973cd4c4daa4f61eeb2bdbad27316534016f';
  MillionSHA256 = 'cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0';
  MillionSHA512 = 'e718483d0ce769644e2e42c7bc15b4638e1f98b13b2044285632a803afa973ebde0ff244877ea60a4cb0432ce577c31beb009c5c2c49aa2e4eadb217ad8cc09b';
  MegaMD5 = '8f293a2f6c19b345152f7a49bb4c643c';
  MegaSHA256 = '631b84027d6b9e52b539c4e8373622d23032dfadc64d60af87339c9037e4f769';

var
  Prev: TMemoryManager;
  Gets: Integer;

function CountGetMem(Size: NativeInt): Pointer;
begin
  Gets := Gets + 1;
  Result := Prev.GetMem(Size);
end;

function CountAllocMem(Size: NativeInt): Pointer;
begin
  Gets := Gets + 1;
  Result := Prev.AllocMem(Size);
end;

function CountReallocMem(P: Pointer; Size: NativeInt): Pointer;
begin
  Gets := Gets + 1;
  Result := Prev.ReallocMem(P, Size);
end;

procedure RunPieces;
var
  A: TAnyHash;
  Data, Piece: TBytes;
  K, J, Offset, N: Integer;
begin
  Section('4099 bytes in one call and in pieces (RTL-1235)');
  Data := Pattern(4099);
  for K := 0 to 7 do
  begin
    A.Start(K);
    A.Add(Data[0], 4099);
    CheckEqS(Whole[K], A.Text, HashNames[K] + ': one call');
    for J := 0 to 6 do
    begin
      A.Start(K);
      Offset := 0;
      while Offset < 4099 do
      begin
        N := Pieces[J];
        if N > 4099 - Offset then
          N := 4099 - Offset;
        A.Add(Data[Offset], N);
        Offset := Offset + N;
      end;
      CheckEqS(Whole[K], A.Text, HashNames[K] + ': pieces of ' + IntToStr(Pieces[J]) + ', untyped data');
      { the array is longer than the piece wherever data is left: only the first N bytes count }
      A.Reset;
      Offset := 0;
      while Offset < 4099 do
      begin
        N := Pieces[J];
        if N > 4099 - Offset then
          N := 4099 - Offset;
        Piece := Copy(Data, Offset, N + 7);
        A.AddBytes(Piece, N);
        Offset := Offset + N;
      end;
      CheckEqS(Whole[K], A.Text, HashNames[K] + ': pieces of ' + IntToStr(Pieces[J]) + ', byte array with a length');
    end;
  end;
end;

procedure RunStates;
var
  A, B: TAnyHash;
  Data: TBytes;
  Mine: TMemoryManager;
  K: Integer;
  U: string;
begin
  Data := Pattern(300);
  for K := 0 to 7 do
  begin
    Section(HashNames[K] + ': running digest, copy, Reset, empty input (RTL-1234, RTL-1235, RTL-1236)');
    A.Start(K);
    A.AddText('ab');
    CheckEqS(Ab[K], A.Text, 'the digest of "ab" while data is still being added');
    B := A;
    A.AddText('c');
    CheckEqS(Abc[K], A.Text, 'Update("c") continues: "abc"');
    CheckEqS(Abc[K], A.Text, 'the same result again');
    CheckEqS(Ab[K], B.Text, 'the copy made before still holds "ab"');
    B.AddText('c');
    CheckEqS(Abc[K], B.Text, 'and continues on its own');
    A.Add(Data[0], 0);
    A.Add(PByte(nil)^, 0);
    A.AddBytes(nil, 0);
    A.AddText('');
    CheckEqS(Abc[K], A.Text, 'a length of 0 (also at nil), an empty array and an empty string add nothing');
    A.AddBytes(Data, 0);
    A.Reset;
    CheckEqS(Empty[K], A.Text, 'Reset gives the digest of no data');
    A.AddText('abc');
    CheckEqS(Abc[K], A.Text, 'the record is usable after Reset');

    { no request to the memory manager: 300 bytes cross a block boundary of every function }
    GetMemoryManager(Prev);
    Mine := Prev;
    Mine.GetMem := CountGetMem;
    Mine.AllocMem := CountAllocMem;
    Mine.ReallocMem := CountReallocMem;
    Gets := 0;
    SetMemoryManager(Mine);
    A.Start(K);
    A.Add(Data[0], 300);
    A.AddBytes(Data, 0);
    A.AddBytes(Data, 77);
    B := A;
    B.Reset;
    SetMemoryManager(Prev);
    CheckEqI(0, Gets, 'Create, Update of untyped data and of a byte array, assignment and Reset allocate nothing');
  end;

  Section('a digest after every Update (RTL-1236)');
  A.Start(0);
  A.AddText('a');
  CheckEqS('0cc175b9c0f1b6a831c399e269772661', A.Text, 'MD5: Update("a")');
  A.AddText('bc');
  CheckEqS(Abc[0], A.Text, 'MD5: Update("bc") gives "abc"');
  CheckEqS(Abc[0], A.Text, 'MD5: and again');
  A.Start(3);
  A.AddText('a');
  CheckEqS('ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb', A.Text, 'SHA256: Update("a")');
  A.AddText('bc');
  CheckEqS(Abc[3], A.Text, 'SHA256: Update("bc") gives "abc"');
  CheckEqS(Abc[3], A.Text, 'SHA256: and again');

  Section('the UTF-16 units of a string are hashed by passing its memory (RTL-1235)');
  U := #$00E9;
  A.Start(0);
  A.Add(PChar(U)^, Length(U) * SizeOf(Char));
  CheckEqS('ed71e8ffd3d8c47c1a2e22c53cd384aa', A.Text, 'MD5 of the bytes E9 00');
  A.Start(3);
  A.Add(PChar(U)^, Length(U) * SizeOf(Char));
  CheckEqS('63e3c807f93f669a2625f37ee673726c36ef3e99b6b7db02c910be25087e0f9c', A.Text, 'SHA256 of the bytes E9 00');
end;

procedure RunStreams;
var
  A: TAnyHash;
  Stream: TSlowStream;
  Data: TBytes;
  K: Integer;
begin
  Section('streams (RTL-1235, RTL-1240)');
  Stream := TextStream(StringOfChar('a', 1000000), 0);
  CheckEqS(MillionMD5, THashMD5.GetHashString(Stream), 'MD5 of a stream holding 1 000 000 x a');
  CheckEqI(1000000, Stream.Position, 'the stream is read to its end');
  CheckEqI(65536, Stream.MaxAsked, 'through a buffer of 65 536 bytes');
  Stream.Position := 0;
  CheckEqS(MillionSHA1, THashSHA1.GetHashString(Stream), 'SHA-1 of the same stream');
  Stream.Position := 0;
  CheckEqS(MillionSHA256, THashSHA2.GetHashString(Stream), 'SHA256 (the default version)');
  Stream.Position := 0;
  CheckEqS(MillionSHA512, THashSHA2.GetHashString(Stream, THashSHA2.TSHA2Version.SHA512), 'SHA512');
  CheckEqI(65536, Stream.MaxAsked, 'no larger block was asked for');
  CheckEqS(Empty[0], THashMD5.GetHashString(Stream), 'a stream at its end adds nothing');
  Stream.Position := 0;
  Stream.Limit := 1000;
  CheckEqS(MillionSHA1, THash.DigestAsString(THashSHA1.GetHashBytes(Stream)), 'a stream that returns less than asked is read until Read returns 0');
  Stream.Free;

  Data := Pattern(1048576);
  Stream := TSlowStream.Create;
  Stream.WriteBuffer(Data[0], Length(Data));
  Stream.Position := 0;
  CheckEqS(MegaSHA256, THash.DigestAsString(THashSHA2.GetHashBytes(Stream)), 'SHA256 of a stream holding P(1 048 576)');
  Stream.Position := 0;
  CheckEqS(MegaMD5, THash.DigestAsString(THashMD5.GetHashBytes(Stream)), 'MD5 of the same stream');
  CheckEqS(MegaSHA256, THashSHA2.GetHashString(Data), 'SHA256 of the same bytes as an array');
  CheckEqS(MegaMD5, THashMD5.GetHashString(Data), 'MD5 of the same bytes as an array');
  Stream.Free;

  for K := 0 to 7 do
  begin
    Stream := TextStream('xxabc', 2);
    A.Start(K);
    A.AddStream(Stream);
    CheckEqS(Abc[K], A.Text, HashNames[K] + ': Update(stream) starts at the position of the stream');
    CheckEqI(5, Stream.Position, HashNames[K] + ': and leaves it at the end');
    Stream.Free;
    Stream := TextStream('bc', 0);
    A.Start(K);
    A.AddText('a');
    A.AddStream(Stream);
    CheckEqS(Abc[K], A.Text, HashNames[K] + ': Update("a"), then a stream holding "bc"');
    Stream.Free;
  end;
  Stream := TextStream('xxabc', 2);
  CheckEqS(Abc[0], THashMD5.GetHashString(Stream), 'GetHashString(stream) starts at the position');
  CheckEqI(5, Stream.Position, 'and leaves it at the end');
  Stream.Free;
end;

procedure RunErrors;
var
  A: TAnyHash;
  Data: TBytes;
  K: Integer;
begin
  Section('arguments (RTL-1247)');
  Data := Pattern(10);
  for K := 0 to 7 do
  begin
    A.Start(K);
    A.AddText('abc');
    Expect('EArgumentOutOfRangeException', 'Argument out of range');
    try A.AddBytes(Data, 11); except on E: Exception do CheckRaises(HashNames[K] + ': Update(bytes, Length + 1)', E.ClassName, E.Message); end;
    try A.AddBytes(nil, 1); except on E: Exception do CheckRaises(HashNames[K] + ': Update(nil, 1)', E.ClassName, E.Message); end;
    Expect('EArgumentNilException', 'Argument must not be nil');
    try A.AddStream(nil); except on E: Exception do CheckRaises(HashNames[K] + ': Update(nil stream)', E.ClassName, E.Message); end;
    CheckEqS(Abc[K], A.Text, HashNames[K] + ': the record is unchanged');
  end;
  try THashMD5.GetHashBytes(TStream(nil)); except on E: Exception do CheckRaises('THashMD5.GetHashBytes(nil stream)', E.ClassName, E.Message); end;
  try THashSHA1.GetHashString(TStream(nil)); except on E: Exception do CheckRaises('THashSHA1.GetHashString(nil stream)', E.ClassName, E.Message); end;
  try THashSHA2.GetHashBytes(TStream(nil)); except on E: Exception do CheckRaises('THashSHA2.GetHashBytes(nil stream)', E.ClassName, E.Message); end;
  try THashSHA2.GetHashString(TStream(nil), THashSHA2.TSHA2Version.SHA384); except on E: Exception do CheckRaises('THashSHA2.GetHashString(nil stream, SHA384)', E.ClassName, E.Message); end;
end;

var
  Before: NativeInt;
begin
  { the UTF-8 encoding object lives until the program ends: it exists before the count is taken }
  THashMD5.GetHashString('x');
  Before := _LiveAllocCount;
  RunPieces;
  RunStates;
  RunStreams;
  RunErrors;
  CheckEqI(Before, _LiveAllocCount, 'no block is left');
  Done;
end.
