// phase: 8
// covers: RTL-1242, RTL-1233
program Hash_Hmac;
{ Hash, keyed hashes (rtl/phase8/encodings-and-hashes.md RTL-1242, RTL-1233, vectors 6.3 C): the seven test cases of
  RFC 2202 for HMAC-MD5 and HMAC-SHA-1 and of RFC 4231 for HMAC-SHA-224 / 256 / 384 / 512 through the byte-array
  form, through the string forms wherever the case's data or key is text, and through GetHMAC; keys of exactly one
  block and of one byte more; empty key and data; a string stands for its UTF-8 form; the default version of
  THashSHA2; THash.DigestsEqual. }
uses
  SysUtils, Classes, Hash, HashKit, TestKit;

const
  { rows: the first six functions of HashNames; columns: test cases 1 to 7 (RFC 2202 section 2 and 3, RFC 4231
    section 4.2 to 4.8; case 5 of RFC 4231 is printed there with its first 16 bytes only) }
  Rfc: array[0..5, 1..7] of string = (
    ('9294727a3638bb1c13f48ef8158bfc9d', '750c783e6ab0b503eaa86e310a5db738', '56be34521d144c88dbb8c733f0e8b3f6',
     '697eaf0aca3a3aea3a75164746ffaa79', '56461ef2342edc00f9bab995690efd4c', '6b1ab7fe4bd7bf8f0b62e6ce61b9d0cd',
     '6f630fad67cda0ee1fb1f562db3aa53e'),
    ('b617318655057264e28bc0b6fb378c8ef146be00', 'effcdf6ae5eb2fa2d27416d5f184df9c259a7c79',
     '125d7342b9ac11cd91a39af48aa17b4f63f175d3', '4c9007f4026250c6bc8414f9bf50c86c2d7235da',
     '4c1a03424b55e07fe7f27be1d58bb9324a9a5a04', 'aa4ae5e15272d00e95705637ce8a3b55ed402112',
     'e8e99d0f45237d786d6bbaa7965c7808bbff1a91'),
    ('896fb1128abbdf196832107cd49df33f47b4b1169912ba4f53684b22', 'a30e01098bc6dbbf45690f3a7e9e6d0f8bbea2a39e6148008fd05e44',
     '7fb3cb3588c6c1f6ffa9694d7d6ad2649365b0c1f65d69d1ec8333ea', '6c11506874013cac6a2abc1bb382627cec6a90d86efc012de7afec5a',
     '0e2aea68a90c8d37c988bcdb9fca6fa8099cd857c7ec4a1815cac54c', '95e9a0db962095adaebe9b2d6f0dbce2d499f112f2d2b7273fa6870e',
     '3a854166ac5d9f023f54d517d0b39dbd946770db9c2b95c9f6f565d1'),
    ('b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7',
     '5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843',
     '773ea91e36800e46854db8ebd09181a72959098b3ef8c122d9635514ced565fe',
     '82558a389a443c0ea4cc819899f2083a85f0faa3e578f8077a2e3ff46729665b',
     'a3b6167473100ee06e0c796c2955552bfa6f7c0a6a8aef8b93f860aab0cd20c5',
     '60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54',
     '9b09ffa71b942fcb27635fbcd5b0e944bfdc63644f0713938a7f51535c3a35e2'),
    ('afd03944d84895626b0825f4ab46907f15f9dadbe4101ec682aa034c7cebc59cfaea9ea9076ede7f4af152e8b2fa9cb6',
     'af45d2e376484031617f78d2b58a6b1b9c7ef464f5a01b47e42ec3736322445e8e2240ca5e69e2c78b3239ecfab21649',
     '88062608d3e6ad8a0aa2ace014c8a86f0aa635d947ac9febe83ef4e55966144b2a5ab39dc13814b94e3ab6e101a34f27',
     '3e8a69b7783c25851933ab6290af6ca77a9981480850009cc5577c6e1f573b4e6801dd23c4a7d679ccf8a386c674cffb',
     '3abf34c3503b2a23a46efc619baef897f4c8e42c934ce55ccbae9740fcbc1af4ca62269e2a37cd88ba926341efe4aeea',
     '4ece084485813e9088d2c63a041bc5b44f9ef1012a2b588f3cd11f05033ac4c60c2ef6ab4030fe8296248df163f44952',
     '6617178e941f020d351e2f254e8fd32c602420feb0b8fb9adccebb82461e99c5a678cc31e799176d3860e6110c46523e'),
    ('87aa7cdea5ef619d4ff0b4241a1d6cb02379f4e2ce4ec2787ad0b30545e17cdedaa833b7d6b8a702038b274eaea3f4e4be9d914eeb61f1702e696c203a126854',
     '164b7a7bfcf819e2e395fbe73b56e0a387bd64222e831fd610270cd7ea2505549758bf75c05a994a6d034f65f8f0e6fdcaeab1a34d4a6b4b636e070a38bce737',
     'fa73b0089d56a284efb0f0756c890be9b1b5dbdd8ee81a3655f83e33b2279d39bf3e848279a722c806b485a47e67c807b946a337bee8942674278859e13292fb',
     'b0ba465637458c6990e5a8c5f61d4af7e576d97ff94b872de76f8050361ee3dba91ca5c11aa25eb4d679275cc5788063a5f19741120c4f2de2adebeb10a298dd',
     '415fad6271580a531d4179bc891d87a650188707922a4fbb36663a1eb16da008711c5b50ddd0fc235084eb9d3364a1454fb2ef67cd1d29fe6773068ea266e96b',
     '80b24263c7c1a3ebb71493c1dd7be8b49b46d1f41b4aeec1121b013783f8f3526b56d037e05f2598bd0fd2215d6a1e5295e64f73f63f0aec8b915a985d786598',
     'e37b6a775dc87dbaa4dfa9f96e5e3ffddebd71f8867289865df5a32d20cdc944b6022cac3c4982b10d5eeb55c3e4de15134676fb6de0446065c97440fa8c6a58'));
  { key of one block of 55 hex (64 or 128 bytes) and of one byte more, data "abc": MD5, SHA-1, SHA256, SHA512 }
  Edge: array[0..3, 0..1] of string = (
    ('99b83e6d5030bbbefc2c25e73db7344a', '399f5e924f3d462ddac49797681d50ce'),
    ('bc707d18bf34130ec2d48254aec79904b5a384f6', '2ca0514141d82110dd5a7a4e3a83a53077499d8f'),
    ('cf56e9e5fcfcad1fc044f8860c6f3e8cd5f9570043afe5302d2e4fb08c43f9bf',
     '93ab563b6ae456190ccf465b33c5372562c767256c4af51b5e83a26b80610c17'),
    ('6fbfdda56df495f05cd212ecae1cdb2ca42d76fe157cc0533776155e6e76e19638568def53397298c7decf2e55dc28a90f6d7a3a2f1e16b63928be6c5addd4ac',
     '4d2c22b9d199f1371071ea7bf3291790d6e059336502d3d31180522ab9bcf55a29651dc8667984c777f17f4e2c6c1f575f8ddc1a4802c150a54e685d63662bd1'));
  EdgeKind: array[0..3] of Integer = (0, 1, 3, 5);
  D2 = 'what do ya want for nothing?';
  D6 = 'Test Using Larger Than Block-Size Key - Hash Key First';
  D7 = 'Test Using Larger Than Block-Size Key and Larger Than One Block-Size Data';
  D7Long = 'This is a test using a larger than block-size key and a larger than block-size data. '
    + 'The key needs to be hashed before being used by the HMAC algorithm.';

{ Function Kind (index of HashNames) through one of the five forms. Data and Key hold one byte per character; the
  forms that take a string get them as they are, which is the same for ASCII text.
  0: bytes, bytes   1: string data, bytes key   2: bytes data, string key   3: string, string   4: GetHMAC }
function Mac(Kind, Form: Integer; const Data, Key: string): string;
var
  D, K, R: TBytes;
  V: THashSHA2.TSHA2Version;
begin
  D := ToBytes(Data);
  K := ToBytes(Key);
  V := THashSHA2.TSHA2Version.SHA256;
  if Kind >= 2 then
    V := THashSHA2.TSHA2Version(Kind - 2);
  case Kind * 5 + Form of
    0: R := THashMD5.GetHMACAsBytes(D, K);
    1: R := THashMD5.GetHMACAsBytes(Data, K);
    2: R := THashMD5.GetHMACAsBytes(D, Key);
    3: R := THashMD5.GetHMACAsBytes(Data, Key);
    4: Exit(THashMD5.GetHMAC(Data, Key));
    5: R := THashSHA1.GetHMACAsBytes(D, K);
    6: R := THashSHA1.GetHMACAsBytes(Data, K);
    7: R := THashSHA1.GetHMACAsBytes(D, Key);
    8: R := THashSHA1.GetHMACAsBytes(Data, Key);
    9: Exit(THashSHA1.GetHMAC(Data, Key));
  else
    case Form of
      0: R := THashSHA2.GetHMACAsBytes(D, K, V);
      1: R := THashSHA2.GetHMACAsBytes(Data, K, V);
      2: R := THashSHA2.GetHMACAsBytes(D, Key, V);
      3: R := THashSHA2.GetHMACAsBytes(Data, Key, V);
    else
      Exit(THashSHA2.GetHMAC(Data, Key, V));
    end;
  end;
  Result := THash.DigestAsString(R);
end;

{ One case. Text = 0: key and data are bytes; 1: the data is text; 2: data and key are text (every form). }
procedure Row(Kind: Integer; const Name, Data, Key, Expected: string; Text: Integer);
begin
  CheckEqS(Expected, Mac(Kind, 0, Data, Key), Name + ': GetHMACAsBytes(bytes, bytes)');
  if Text >= 1 then
    CheckEqS(Expected, Mac(Kind, 1, Data, Key), Name + ': GetHMACAsBytes(string, bytes)');
  if Text = 2 then
  begin
    CheckEqS(Expected, Mac(Kind, 2, Data, Key), Name + ': GetHMACAsBytes(bytes, string)');
    CheckEqS(Expected, Mac(Kind, 3, Data, Key), Name + ': GetHMACAsBytes(string, string)');
    CheckEqS(Expected, Mac(Kind, 4, Data, Key), Name + ': GetHMAC, lower-case');
  end;
end;

function FromHex(const S: string): TBytes;
var
  I: Integer;
begin
  SetLength(Result, Length(S) div 2);
  for I := 0 to Length(Result) - 1 do
    Result[I] := Byte(StrToInt('$' + Copy(S, 2 * I + 1, 2)));
end;

procedure RunCases;
const
  Sizes: array[0..7] of Integer = (16, 20, 28, 32, 48, 64, 28, 32);
var
  Kind, Small, Large, I: Integer;
  K4, Long: string;
  K, Seen: TBytes;
begin
  K4 := '';
  for I := 1 to 25 do
    K4 := K4 + Chr(I);
  for Kind := 0 to 5 do
  begin
    Section('HMAC-' + HashNames[Kind] + ': test cases 1 to 7 (RTL-1242)');
    Small := 20;
    Large := 131;
    Long := D7Long;
    if Kind = 0 then
      Small := 16;
    if Kind < 2 then
    begin
      Large := 80;
      Long := D7;
    end;
    Row(Kind, '1', 'Hi There', StringOfChar(#$0B, Small), Rfc[Kind, 1], 1);
    Row(Kind, '2', D2, 'Jefe', Rfc[Kind, 2], 2);
    Row(Kind, '3', StringOfChar(#$DD, 50), StringOfChar(#$AA, Small), Rfc[Kind, 3], 0);
    Row(Kind, '4', StringOfChar(#$CD, 50), K4, Rfc[Kind, 4], 0);
    Row(Kind, '5', 'Test With Truncation', StringOfChar(#$0C, Small), Rfc[Kind, 5], 1);
    Row(Kind, '6, key longer than a block', D6, StringOfChar(#$AA, Large), Rfc[Kind, 6], 1);
    Row(Kind, '7, key and data longer than a block', Long, StringOfChar(#$AA, Large), Rfc[Kind, 7], 1);
  end;

  Section('the other versions, key lengths around one block, empty key and data (RTL-1242)');
  Row(6, 'SHA512_224, case 2', D2, 'Jefe', '4a530b31a79ebcce36916546317c45f247d83241dfb818fd37254bde', 2);
  Row(7, 'SHA512_256, case 2', D2, 'Jefe', '6df7b24630d5ccb2ee335407081a87188c221489768fa2020513b2d593359456', 2);
  for I := 0 to 3 do
  begin
    Kind := EdgeKind[I];
    Small := 64;
    if Kind = 5 then
      Small := 128;
    Row(Kind, HashNames[Kind] + ', key of one block', 'abc', StringOfChar('U', Small), Edge[I, 0], 2);
    Row(Kind, HashNames[Kind] + ', key of one block and a byte', 'abc', StringOfChar('U', Small + 1), Edge[I, 1], 2);
  end;
  Row(0, 'MD5, empty key and data', '', '', '74e6f7298a9c2d168935f58c001bad88', 2);
  Row(1, 'SHA-1, empty key and data', '', '', 'fbdb1d1b18aa6c08324b7d64b71fb76370690e1d', 2);
  Row(3, 'SHA256, empty key and data', '', '', 'b613679a0814d9ec772f95d778c35fc5ff1697c493715653c6c712144292c5ad', 2);
  Row(1, 'SHA-1, empty key', 'abc', '', '9b4a918f398d74d3e367970aba3cbe54e4d2b5d9', 2);
  Row(3, 'SHA256, empty data', '', 'abc', 'e2636077506729a8f61aff2441332e40e844a8ad44489efd80210ea6d1f51088', 2);

  Section('full digest, default version, UTF-8 form of a string, arguments unchanged (RTL-1242)');
  for Kind := 0 to 7 do
    CheckEqI(2 * Sizes[Kind], Length(Mac(Kind, 0, 'abc', 'key')), HashNames[Kind] + ': the whole digest, no truncation');
  CheckEqS(Rfc[3, 2], THashSHA2.GetHMAC(D2, 'Jefe'), 'THashSHA2.GetHMAC without a version is SHA256');
  CheckEqS(Rfc[3, 2], THash.DigestAsString(THashSHA2.GetHMACAsBytes(D2, 'Jefe')), 'GetHMACAsBytes(string, string) too');
  CheckEqS(Rfc[3, 2], THash.DigestAsString(THashSHA2.GetHMACAsBytes(D2, ToBytes('Jefe'))), '(string, bytes) too');
  CheckEqS(Rfc[3, 2], THash.DigestAsString(THashSHA2.GetHMACAsBytes(ToBytes(D2), 'Jefe')), '(bytes, string) too');
  CheckEqS(Rfc[3, 2], THash.DigestAsString(THashSHA2.GetHMACAsBytes(ToBytes(D2), ToBytes('Jefe'))), '(bytes, bytes) too');
  K := FromHex('c3a9');
  CheckEqS('4457f8a129759e749d69b9d6ed42b4b1', THashMD5.GetHMAC(#$00E9, #$00E9), 'MD5: data and key U+00E9');
  CheckEqS('4457f8a129759e749d69b9d6ed42b4b1', THash.DigestAsString(THashMD5.GetHMACAsBytes(K, K)), 'are the bytes C3 A9');
  CheckEqS('4457f8a129759e749d69b9d6ed42b4b1', THash.DigestAsString(THashMD5.GetHMACAsBytes(#$00E9, K)), 'as data');
  CheckEqS('4457f8a129759e749d69b9d6ed42b4b1', THash.DigestAsString(THashMD5.GetHMACAsBytes(K, #$00E9)), 'and as key');
  CheckEqS('cfb9f5aecd21b357efb5291b1e33225a3561c75ba5e45f73b40e2e8d1ca992d6', THashSHA2.GetHMAC(#$00E9, #$00E9),
    'SHA256: data and key U+00E9');
  K := ToBytes(StringOfChar(#$AA, 131));
  Seen := Copy(K);
  CheckEqS(Rfc[3, 6], THash.DigestAsString(THashSHA2.GetHMACAsBytes(ToBytes(D6), K)), 'a long key once more');
  Check((Length(K) = 131) and THash.DigestsEqual(K, Seen), 'the key array of the caller is not changed');
end;

procedure RunEqual;
var
  A, B, E: TBytes;
  I, Bit, Unequal: Integer;
begin
  Section('THash.DigestsEqual (RTL-1233)');
  A := FromHex(Rfc[3, 2]);
  B := Copy(A);
  Check(THash.DigestsEqual(A, B), 'two arrays with the same 32 bytes');
  Check(THash.DigestsEqual(A, A), 'an array and itself');
  B[0] := B[0] xor 1;
  Check(not THash.DigestsEqual(A, B), 'a difference in the first byte');
  Check(not THash.DigestsEqual(B, A), 'in either order');
  B := Copy(A);
  B[31] := B[31] xor $80;
  Check(not THash.DigestsEqual(A, B), 'a difference in the last byte');
  B := Copy(A);
  B[16] := not B[16];
  Check(not THash.DigestsEqual(A, B), 'a difference in the middle');
  Unequal := 0;
  for I := 0 to 31 do
    for Bit := 0 to 7 do
    begin
      B := Copy(A);
      B[I] := B[I] xor Byte(1 shl Bit);
      if not THash.DigestsEqual(A, B) then
        Unequal := Unequal + 1;
    end;
  CheckEqI(256, Unequal, 'every single bit of the 32 bytes counts');
  Check(not THash.DigestsEqual(A, Copy(A, 0, 31)), 'different lengths: a prefix is not equal');
  Check(not THash.DigestsEqual(Copy(A, 0, 31), A), 'in either order');
  SetLength(E, 0);
  Check(THash.DigestsEqual(nil, nil), 'two empty arrays are equal');
  Check(THash.DigestsEqual(E, nil), 'also an emptied array and nil');
  Check(not THash.DigestsEqual(nil, A), 'an empty array and a digest are not');
  Check(not THash.DigestsEqual(A, nil), 'in either order');
  Check(THash.DigestsEqual(FromHex('00'), FromHex('00')), 'one byte: 00 and 00');
  Check(not THash.DigestsEqual(FromHex('00'), FromHex('ff')), 'one byte: 00 and FF');
  Check(not THash.DigestsEqual(FromHex('0100'), FromHex('0001')), 'the same bytes in another order');
  Check(THash.DigestsEqual(THashSHA2.GetHMACAsBytes(D2, 'Jefe'), A), 'verifies a received HMAC value');
  Check(not THash.DigestsEqual(THashSHA2.GetHMACAsBytes(D2, 'jefe'), A), 'and rejects it under another key');
  Check(not THash.DigestsEqual(THashSHA2.GetHMACAsBytes(D2 + ' ', 'Jefe'), A), 'or for other data');
  Check(THash.DigestsEqual(THashMD5.GetHashBytes('abc'), FromHex('900150983cd24fb0d6963f7d28e17f72')), 'compares a digest');
end;

var
  Before: NativeInt;
begin
  { the UTF-8 encoding object lives until the program ends: it exists before the count is taken }
  THashMD5.GetHashString('x');
  Before := _LiveAllocCount;
  RunCases;
  RunEqual;
  CheckEqI(Before, _LiveAllocCount, 'no block is left');
  Done;
end.
