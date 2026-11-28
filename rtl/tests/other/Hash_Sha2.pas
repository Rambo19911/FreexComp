// phase: 8
// covers: RTL-1239, RTL-1240
program Hash_Sha2;
{ Hash, the six SHA-2 functions of THashSHA2 (rtl/phase8/encodings-and-hashes.md section 4.3, vectors 6.3 A): the
  five messages M0 to M4 (empty, "abc", the two-block messages of FIPS 180 and 1 000 000 x "a"), the longest
  message that fits one block with its padding, the first one that does not, and one exact block - through the
  one-call functions for a string and for a byte array and through Create / Update / HashAsString; block and digest
  sizes; Reset keeps the version; the default version is SHA256; the ordinals of the six versions. }
uses
  SysUtils, Classes, Hash, HashKit, TestKit;

const
  M1 = 'abc';
  M2 = 'abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq';
  M3 = 'abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu';

var
  M4: string;

{ Text (ASCII) -> Digest with version V through the one-call forms and through a state record }
procedure Row(V: THashSHA2.TSHA2Version; const Name, Text, Digest: string);
var
  H: THashSHA2;
  D: TBytes;
begin
  CheckEqS(Digest, THashSHA2.GetHashString(Text, V), Name + ': GetHashString(string)');
  CheckEqS(Digest, THashSHA2.GetHashString(ToBytes(Text), V), Name + ': GetHashString(bytes)');
  CheckEqS(Digest, THash.DigestAsString(THashSHA2.GetHashBytes(Text, V)), Name + ': GetHashBytes(string)');
  D := THashSHA2.GetHashBytes(ToBytes(Text), V);
  CheckEqS(Digest, THash.DigestAsString(D), Name + ': GetHashBytes(bytes)');
  H := THashSHA2.Create(V);
  H.Update(Text);
  CheckEqS(Digest, H.HashAsString, Name + ': Create, Update, HashAsString');
  CheckEqS(Digest, H.HashAsString, Name + ': the same result again');
end;

{ One version: the messages M0 to M4, then Fit x "a" (the longest message whose padding fits its block), Fit + 1
  (the padding needs a second block) and Block x "a"; the sizes; Reset keeps the version. }
procedure Version(V: THashSHA2.TSHA2Version; const Name: string; Block, Size: Integer;
  const D0, D1, D2, D3, D4, DFit, DNext, DBlock: string);
var
  H: THashSHA2;
  Fit: Integer;
begin
  Section(Name + ' (RTL-1239)');
  Fit := Block - Block div 8 - 1;
  Row(V, Name + ' M0', '', D0);
  Row(V, Name + ' M1', M1, D1);
  Row(V, Name + ' M2', M2, D2);
  Row(V, Name + ' M3', M3, D3);
  Row(V, Name + ' M4', M4, D4);
  Row(V, Name + ' ' + IntToStr(Fit) + ' x a', StringOfChar('a', Fit), DFit);
  Row(V, Name + ' ' + IntToStr(Fit + 1) + ' x a', StringOfChar('a', Fit + 1), DNext);
  Row(V, Name + ' ' + IntToStr(Block) + ' x a', StringOfChar('a', Block), DBlock);
  H := THashSHA2.Create(V);
  CheckEqI(Block, H.GetBlockSize, Name + ': block size');
  CheckEqI(Size, H.GetHashSize, Name + ': digest size');
  CheckEqS(D0, H.HashAsString, Name + ': Create gives the initial state');
  H.Update(M1);
  CheckEqI(Size, Length(H.HashAsBytes), Name + ': HashAsBytes has the digest size');
  CheckEqI(Size * 2, Length(H.HashAsString), Name + ': HashAsString has two digits per byte');
  H.Reset;
  CheckEqI(Size, H.GetHashSize, Name + ': Reset keeps the version');
  CheckEqS(D0, H.HashAsString, Name + ': Reset gives the initial state');
  H.Update(M1);
  CheckEqS(D1, H.HashAsString, Name + ': and the record is usable again');
end;

procedure Run;
var
  H: THashSHA2;
  D: TBytes;
begin
  M4 := StringOfChar('a', 1000000);

  Version(THashSHA2.TSHA2Version.SHA224, 'SHA224', 64, 28,
    'd14a028c2a3a2bc9476102bb288234c415a2b01f828ea62ac5b3e42f',
    '23097d223405d8228642a477bda255b32aadbce4bda0b3f7e36c9da7',
    '75388b16512776cc5dba5da1fd890150b0c6455cb4f58b1952522525',
    'c97ca9a559850ce97a04a96def6d99a9e0e0e2ab14e6b8df265fc0b3',
    '20794655980c91d8bbb4c1ea97618a4bf03f42581948b2ee4ee7ad67',
    'fb0bd626a70c28541dfa781bb5cc4d7d7f56622a58f01a0b1ddd646f',
    'd40854fc9caf172067136f2e29e1380b14626bf6f0dd06779f820dcd',
    'a88cd5cde6d6fe9136a4e58b49167461ea95d388ca2bdb7afdc3cbf4');
  Version(THashSHA2.TSHA2Version.SHA256, 'SHA256', 64, 32,
    'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
    'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad',
    '248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1',
    'cf5b16a778af8380036ce59e7b0492370b249b11e8f07a51afac45037afee9d1',
    'cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0',
    '9f4390f8d30c2dd92ec9f095b65e2b9ae9b0a925a5258e241c9f1e910f734318',
    'b35439a4ac6f0948b6d6f9e3c6af0f5f590ce20f1bde7090ef7970686ec6738a',
    'ffe054fe7ae0cb6dc65c3af9b61d5209f439851db43d0ba5997337df154668eb');
  Version(THashSHA2.TSHA2Version.SHA384, 'SHA384', 128, 48,
    '38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da274edebfe76f65fbd51ad2f14898b95b',
    'cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7',
    '3391fdddfc8dc7393707a65b1b4709397cf8b1d162af05abfe8f450de5f36bc6b0455a8520bc4e6f5fe95b1fe3c8452b',
    '09330c33f71147e83d192fc782cd1b4753111b173b3b05d22fa08086e3b0f712fcc7c71a557e2db966c3e9fa91746039',
    '9d0e1809716474cb086e834e310a4a1ced149e9c00f248527972cec5704c2a5b07b8b3dc38ecc4ebae97ddd87f3d8985',
    '3c37955051cb5c3026f94d551d5b5e2ac38d572ae4e07172085fed81f8466b8f90dc23a8ffcdea0b8d8e58e8fdacc80a',
    '187d4e07cb306103c69967bf544d0dfbe9042577599c73c330abc0cb64c61236d5ed565ee19119d8c31779a38f791fcd',
    'edb12730a366098b3b2beac75a3bef1b0969b15c48e2163c23d96994f8d1bef760c7e27f3c464d3829f56c0d53808b0b');
  Version(THashSHA2.TSHA2Version.SHA512, 'SHA512', 128, 64,
    'cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e',
    'ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f',
    '204a8fc6dda82f0a0ced7beb8e08a41657c16ef468b228a8279be331a703c33596fd15c13b1b07f9aa1d3bea57789ca031ad85c7a71dd70354ec631238ca3445',
    '8e959b75dae313da8cf4f72814fc143f8f7779c6eb9f7fa17299aeadb6889018501d289e4900f7e4331b99dec4b5433ac7d329eeb6dd26545e96e55b874be909',
    'e718483d0ce769644e2e42c7bc15b4638e1f98b13b2044285632a803afa973ebde0ff244877ea60a4cb0432ce577c31beb009c5c2c49aa2e4eadb217ad8cc09b',
    'fa9121c7b32b9e01733d034cfc78cbf67f926c7ed83e82200ef86818196921760b4beff48404df811b953828274461673c68d04e297b0eb7b2b4d60fc6b566a2',
    'c01d080efd492776a1c43bd23dd99d0a2e626d481e16782e75d54c2503b5dc32bd05f0f1ba33e568b88fd2d970929b719ecbb152f58f130a407c8830604b70ca',
    'b73d1929aa615934e61a871596b3f3b33359f42b8175602e89f7e06e5f658a243667807ed300314b95cacdd579f3e33abdfbe351909519a846d465c59582f321');
  Version(THashSHA2.TSHA2Version.SHA512_224, 'SHA512_224', 128, 28,
    '6ed0dd02806fa89e25de060c19d3ac86cabb87d6a0ddd05c333b84f4',
    '4634270f707b6a54daae7530460842e20e37ed265ceee9a43e8924aa',
    'e5302d6d54bb242275d1e7622d68df6eb02dedd13f564c13dbda2174',
    '23fec5bb94d60b23308192640b0c453335d664734fe40e7268674af9',
    '37ab331d76f0d36de422bd0edeb22a28accd487b7a8453ae965dd287',
    '3ebe1b48e8c66acb9ae014db95b4bec93de7e9572bff41cf566bd7d0',
    '79b41fef2a0439d2705724a67615f7bcbcd2bf5664a7774b80818eb6',
    '261b94bcba554264b3b738e9e09e7dc68ac8e0b4c8517fe9bb7c3617');
  Version(THashSHA2.TSHA2Version.SHA512_256, 'SHA512_256', 128, 32,
    'c672b8d1ef56ed28ab87c3622c5114069bdd3ad7b8f9737498d0c01ecef0967a',
    '53048e2681941ef99b2e29b76b4c7dabe4c2d0c634fc6d46e0e2f13107e7af23',
    'bde8e1f9f19bb9fd3406c90ec6bc47bd36d8ada9f11880dbc8a22a7078b6a461',
    '3928e184fb8690f840da3988121d31be65cb9d3ef83ee6146feac861e19b563a',
    '9a59a052930187a97038cae692f30708aa6491923ef5194394dc68d56c74fb21',
    '0239e429f98d0ed61ee8e2a7c30afe98c1c3a80ce5dff62a107e9c538f7632ce',
    '9216b5303edb66504570bee90e48ea5beaa5e9fe9f760bbd3e0460559fc005f6',
    'b88f97e274f9c1d49f181c8cbd01a9c74930ad055a46ac4499a1d601f1c80bf2');

  Section('the default version is SHA256 (RTL-1239, RTL-1240)');
  H := THashSHA2.Create;
  CheckEqI(32, H.GetHashSize, 'Create without a version: 32 bytes');
  CheckEqI(64, H.GetBlockSize, 'and a block of 64 bytes');
  H.Update(M1);
  CheckEqS('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad', H.HashAsString, 'Create, Update(abc)');
  CheckEqS('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad', THashSHA2.GetHashString(M1), 'GetHashString(string)');
  CheckEqS('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad', THashSHA2.GetHashString(ToBytes(M1)), 'GetHashString(bytes)');
  CheckEqI(32, Length(THashSHA2.GetHashBytes(M1)), 'GetHashBytes(string)');
  CheckEqI(32, Length(THashSHA2.GetHashBytes(ToBytes(M1))), 'GetHashBytes(bytes)');

  Section('the ordinals of the versions (RTL-1239)');
  CheckEqI(0, Ord(THashSHA2.TSHA2Version.SHA224), 'SHA224');
  CheckEqI(1, Ord(THashSHA2.TSHA2Version.SHA256), 'SHA256');
  CheckEqI(2, Ord(THashSHA2.TSHA2Version.SHA384), 'SHA384');
  CheckEqI(3, Ord(THashSHA2.TSHA2Version.SHA512), 'SHA512');
  CheckEqI(4, Ord(THashSHA2.TSHA2Version.SHA512_224), 'SHA512_224');
  CheckEqI(5, Ord(THashSHA2.TSHA2Version.SHA512_256), 'SHA512_256');
  CheckEqI(5, Ord(High(THashSHA2.TSHA2Version)), 'six versions');

  Section('a string is hashed in its UTF-8 form (RTL-1240)');
  SetLength(D, 2);
  D[0] := $C3;
  D[1] := $A9;
  CheckEqS('4a99557e4033c3539de2eb65472017cad5f9557f7a0625a09f1c3f6e2ba69c4c', THashSHA2.GetHashString(#$00E9), 'SHA256 of U+00E9');
  CheckEqS('4a99557e4033c3539de2eb65472017cad5f9557f7a0625a09f1c3f6e2ba69c4c', THashSHA2.GetHashString(D), 'is the SHA256 of the bytes C3 A9');
  CheckEqS('f0443a342c5ef54783a111b51ba56c938e474c32324d90c3a60c9c8e3a37e2d9', THashSHA2.GetHashString(#$D83D#$DE00),
    'a surrogate pair is one 4-byte sequence');
end;

var
  Before: NativeInt;
begin
  { the UTF-8 encoding object lives until the program ends: it exists before the count is taken }
  THashSHA2.GetHashString('x');
  Before := _LiveAllocCount;
  Run;
  M4 := '';
  CheckEqI(Before, _LiveAllocCount, 'no block is left');
  Done;
end.
