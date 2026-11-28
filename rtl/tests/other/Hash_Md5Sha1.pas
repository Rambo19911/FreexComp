// phase: 8
// covers: RTL-1232, RTL-1234, RTL-1236, RTL-1237, RTL-1238, RTL-1240
program Hash_Md5Sha1;
{ Hash, MD5 and SHA-1 (rtl/phase8/encodings-and-hashes.md section 4, vectors 6.3 A): the values printed in RFC 1321
  appendix A.5, the SHA-1 example values of FIPS 180 and the four of RFC 3174 section 7.3 through the one-call
  functions for a string and for a byte array and through Create / Update / HashAsString; the longest message that
  fits one block with its padding, the first one that does not, and one exact block; block and digest sizes; a
  string is hashed in its UTF-8 form; THash.DigestAsString and THash.DigestAsInteger. The program has a variable
  named Hash: a declaration of the program hides the unit name, the types of the unit stay visible. }
uses
  SysUtils, Classes, Hash, HashKit, TestKit;

const
  M1 = 'abc';
  M2 = 'abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq';
  M3 = 'abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu';

{ S, N times }
function Times(const S: string; N: Integer): string;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to N do
    Result := Result + S;
end;

function Bytes4(A, B, C, D: Byte): TBytes;
begin
  SetLength(Result, 4);
  Result[0] := A;
  Result[1] := B;
  Result[2] := C;
  Result[3] := D;
end;

{ Text (ASCII) -> Digest through every one-call form and through a state record (RTL-1237, RTL-1240) }
procedure Md5(const Name, Text, Digest: string);
var
  Hash: THashMD5;
  D: TBytes;
begin
  CheckEqS(Digest, THashMD5.GetHashString(Text), 'MD5 ' + Name + ': GetHashString(string)');
  CheckEqS(Digest, THashMD5.GetHashString(ToBytes(Text)), 'MD5 ' + Name + ': GetHashString(bytes)');
  CheckEqS(Digest, THash.DigestAsString(THashMD5.GetHashBytes(Text)), 'MD5 ' + Name + ': GetHashBytes(string)');
  D := THashMD5.GetHashBytes(ToBytes(Text));
  CheckEqS(Digest, THash.DigestAsString(D), 'MD5 ' + Name + ': GetHashBytes(bytes)');
  Hash := THashMD5.Create;
  Hash.Update(Text);
  CheckEqS(Digest, Hash.HashAsString, 'MD5 ' + Name + ': Create, Update, HashAsString');
  D := Hash.HashAsBytes;
  Check((Length(D) = 16) and (THash.DigestAsString(D) = Digest), 'MD5 ' + Name + ': HashAsBytes, 16 bytes');
end;

{ the same for SHA-1 (RTL-1238) }
procedure Sha1(const Name, Text, Digest: string);
var
  Hash: THashSHA1;
  D: TBytes;
begin
  CheckEqS(Digest, THashSHA1.GetHashString(Text), 'SHA-1 ' + Name + ': GetHashString(string)');
  CheckEqS(Digest, THashSHA1.GetHashString(ToBytes(Text)), 'SHA-1 ' + Name + ': GetHashString(bytes)');
  CheckEqS(Digest, THash.DigestAsString(THashSHA1.GetHashBytes(Text)), 'SHA-1 ' + Name + ': GetHashBytes(string)');
  D := THashSHA1.GetHashBytes(ToBytes(Text));
  CheckEqS(Digest, THash.DigestAsString(D), 'SHA-1 ' + Name + ': GetHashBytes(bytes)');
  Hash := THashSHA1.Create;
  Hash.Update(Text);
  CheckEqS(Digest, Hash.HashAsString, 'SHA-1 ' + Name + ': Create, Update, HashAsString');
  D := Hash.HashAsBytes;
  Check((Length(D) = 20) and (THash.DigestAsString(D) = Digest), 'SHA-1 ' + Name + ': HashAsBytes, 20 bytes');
end;

procedure Run;
var
  Hash: THashMD5;
  Other: THashSHA1;
  M4: string;
  D: TBytes;
begin
  M4 := StringOfChar('a', 1000000);

  Section('MD5: RFC 1321 appendix A.5 and the messages of 6.3 A (RTL-1237)');
  Md5('M0', '', 'd41d8cd98f00b204e9800998ecf8427e');
  Md5('"a"', 'a', '0cc175b9c0f1b6a831c399e269772661');
  Md5('M1', M1, '900150983cd24fb0d6963f7d28e17f72');
  Md5('"message digest"', 'message digest', 'f96b697d7cb7938d525a2f31aaf161d0');
  Md5('a to z', 'abcdefghijklmnopqrstuvwxyz', 'c3fcd3d76192e4007dfb496cca67e13b');
  Md5('letters and digits', 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789',
    'd174ab98d277d9f5a5611c2c9f419d9f');
  Md5('8 x "1234567890"', Times('1234567890', 8), '57edf4a22be3c955ac49da2e2107b67a');
  Md5('M2', M2, '8215ef0796a20bcaaae116d3876c664a');
  Md5('M3', M3, '03dd8807a93175fb062dfb55dc7d359c');
  Md5('M4', M4, '7707d6ae4e027c70eea2a935c2296f21');
  Md5('55 x a', StringOfChar('a', 55), 'ef1772b6dff9a122358552954ad0df65');
  Md5('56 x a', StringOfChar('a', 56), '3b0c8ac703f828b04c6c197006d17218');
  Md5('64 x a', StringOfChar('a', 64), '014842d480b571495a4a0363793f7367');

  Section('SHA-1: the example values and the messages of 6.3 A (RTL-1238)');
  Sha1('M0', '', 'da39a3ee5e6b4b0d3255bfef95601890afd80709');
  Sha1('M1', M1, 'a9993e364706816aba3e25717850c26c9cd0d89d');
  Sha1('M2', M2, '84983e441c3bd26ebaae4aa1f95129e5e54670f1');
  Sha1('M3', M3, 'a49b2446a02c645bf419f995b67091253a04a259');
  Sha1('M4', M4, '34aa973cd4c4daa4f61eeb2bdbad27316534016f');
  Sha1('80 x "01234567" (ten blocks)', Times('01234567', 80), 'dea356a2cddd90c7a7ecedc5ebb563934f460452');
  Sha1('55 x a', StringOfChar('a', 55), 'c1c8bbdc22796e28c0e15163d20899b65621d65a');
  Sha1('56 x a', StringOfChar('a', 56), 'c2db330f6083854c99d4b5bfb6e8f29f201be699');
  Sha1('64 x a', StringOfChar('a', 64), '0098ba824b5c16427bd7a1122a5a442a25ec644d');

  Section('sizes, initial state, repeated results (RTL-1234, RTL-1236)');
  Hash := THashMD5.Create;
  CheckEqI(64, Hash.GetBlockSize, 'MD5 block size');
  CheckEqI(16, Hash.GetHashSize, 'MD5 digest size');
  CheckEqS('d41d8cd98f00b204e9800998ecf8427e', Hash.HashAsString, 'MD5: Create gives the initial state');
  Hash.Update(M1);
  CheckEqS('900150983cd24fb0d6963f7d28e17f72', Hash.HashAsString, 'MD5: a result');
  CheckEqS('900150983cd24fb0d6963f7d28e17f72', Hash.HashAsString, 'MD5: the same result again');
  CheckEqI(16, Length(Hash.HashAsBytes), 'MD5: HashAsBytes has 16 bytes');
  Hash.Reset;
  CheckEqS('d41d8cd98f00b204e9800998ecf8427e', Hash.HashAsString, 'MD5: Reset gives the initial state');
  Other := THashSHA1.Create;
  CheckEqI(64, Other.GetBlockSize, 'SHA-1 block size');
  CheckEqI(20, Other.GetHashSize, 'SHA-1 digest size');
  CheckEqS('da39a3ee5e6b4b0d3255bfef95601890afd80709', Other.HashAsString, 'SHA-1: Create gives the initial state');
  Other.Update(M1);
  CheckEqS('a9993e364706816aba3e25717850c26c9cd0d89d', Other.HashAsString, 'SHA-1: a result');
  CheckEqS('a9993e364706816aba3e25717850c26c9cd0d89d', Other.HashAsString, 'SHA-1: the same result again');
  CheckEqI(20, Length(Other.HashAsBytes), 'SHA-1: HashAsBytes has 20 bytes');
  Other.Reset;
  CheckEqS('da39a3ee5e6b4b0d3255bfef95601890afd80709', Other.HashAsString, 'SHA-1: Reset gives the initial state');

  Section('a string is hashed in its UTF-8 form (RTL-1235, RTL-1240)');
  SetLength(D, 2);
  D[0] := $C3;
  D[1] := $A9;
  CheckEqS('66ddcd97cfdeabb2f6fb8a999b4bc76f', THashMD5.GetHashString(#$00E9), 'MD5 of U+00E9');
  CheckEqS('66ddcd97cfdeabb2f6fb8a999b4bc76f', THashMD5.GetHashString(D), 'is the MD5 of the bytes C3 A9');
  CheckEqS('bf15be717ac1b080b4f1c456692825891ff5073d', THashSHA1.GetHashString(#$00E9), 'SHA-1 of U+00E9');
  CheckEqS('bf15be717ac1b080b4f1c456692825891ff5073d', THashSHA1.GetHashString(D), 'is the SHA-1 of the bytes C3 A9');
  Hash := THashMD5.Create;
  Hash.Update(#$00E9);
  CheckEqS('66ddcd97cfdeabb2f6fb8a999b4bc76f', Hash.HashAsString, 'Update(string) adds the UTF-8 form');
  CheckEqS('2a02eac39d716a70ecf37579185927b6', THashMD5.GetHashString(#$D83D#$DE00), 'a surrogate pair is one 4-byte sequence');
  CheckEqS('9b759040321a408a5c7768b4511287a6', THashMD5.GetHashString(#$D800), 'a lone surrogate is EF BF BD');

  Section('THash.DigestAsString, THash.DigestAsInteger (RTL-1232)');
  CheckEqS('', THash.DigestAsString(nil), 'DigestAsString of an empty array');
  CheckEqS('000fa0ff', THash.DigestAsString(Bytes4($00, $0F, $A0, $FF)), 'two lower-case digits per byte, in array order');
  CheckEqS('abcdef', THash.DigestAsString(Copy(Bytes4($AB, $CD, $EF, $00), 0, 3)), 'lower-case a to f');
  CheckEqI($12345678, THash.DigestAsInteger(Bytes4($12, $34, $56, $78)), 'DigestAsInteger reads big-endian');
  CheckEqI(-873187034, THash.DigestAsInteger(Bytes4($CB, $F4, $39, $26)), 'the bit pattern is kept ($CBF43926)');
  CheckEqI(-1, THash.DigestAsInteger(Bytes4($FF, $FF, $FF, $FF)), 'four FF bytes are -1');
  CheckEqI(0, THash.DigestAsInteger(Bytes4(0, 0, 0, 0)), 'four zero bytes are 0');
  Expect('EHashException', 'Digest must be 4 bytes long');
  try THash.DigestAsInteger(nil); except on E: Exception do CheckRaises('DigestAsInteger of an empty array', E.ClassName, E.Message); end;
  try THash.DigestAsInteger(Copy(Bytes4(1, 2, 3, 4), 0, 3)); except on E: Exception do CheckRaises('of 3 bytes', E.ClassName, E.Message); end;
  try THash.DigestAsInteger(THashMD5.GetHashBytes(M1)); except on E: Exception do CheckRaises('of an MD5 digest', E.ClassName, E.Message); end;
  Check(EHashException.InheritsFrom(Exception), 'EHashException is an Exception');
end;

var
  Before: NativeInt;
begin
  { the UTF-8 encoding object lives until the program ends: it exists before the count is taken }
  THashMD5.GetHashString('x');
  Before := _LiveAllocCount;
  Run;
  CheckEqI(Before, _LiveAllocCount, 'no block is left');
  Done;
end.
