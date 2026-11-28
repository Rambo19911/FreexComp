// phase: 8
// covers: RTL-1241
// opc: -O2
// opc-run: tempdir
program Hash_File;
{ Hash, files (rtl/phase8/encodings-and-hashes.md RTL-1241, vectors 6.3 E): GetHashStringFromFile and
  GetHashBytesFromFile of the three digest types. The program writes a file of 104 857 600 bytes (byte I = I mod 251)
  into the temp folder given as the first argument, piece by piece; its SHA-256 and MD5 digests are the published
  ones, equal the digest of a TFileStream on the file, and the call asks the memory manager for less than 1 MiB.
  Small files: empty, the three types, a version of THashSHA2, a name outside ASCII. The file is opened for reading
  with writers denied and is closed when the function returns; a file that cannot be opened raises EFOpenError.
  Compiled with -O2: three passes over 100 MB. Every file is deleted again. }
uses
  SysUtils, Classes, Hash, HashKit, TestKit;

const
  BigSize = 104857600;
  BigSHA256 = '85a38859acdd54fd3381d9f1e0d4c8ad8158f2c66c0a496d1756585056ebed76';
  BigMD5 = '414000f5c3c7fbc2745dee0de1e4cf94';
  AbcMD5 = '900150983cd24fb0d6963f7d28e17f72';
  AbcSHA1 = 'a9993e364706816aba3e25717850c26c9cd0d89d';
  AbcSHA256 = 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad';
  AbcSHA512_256 = '53048e2681941ef99b2e29b76b4c7dabe4c2d0c634fc6d46e0e2f13107e7af23';

var
  Dir: string;

{ a file holding the characters of Text, one byte each }
function Small(const Name, Text: string): string;
var
  F: TFileStream;
  D: TBytes;
begin
  Result := Dir + Name;
  D := ToBytes(Text);
  F := TFileStream.Create(Result, fmCreate);
  try
    if Length(D) > 0 then
      F.WriteBuffer(D[0], Length(D));
  finally
    F.Free;
  end;
end;

{ the big file: whole periods of the pattern per piece, so that every piece starts with byte 0 }
function Big: string;
var
  F: TFileStream;
  Piece: TBytes;
  Left, N: Integer;
begin
  Result := Dir + 'big.bin';
  Piece := Pattern(251 * 256);
  F := TFileStream.Create(Result, fmCreate);
  try
    Left := BigSize;
    while Left > 0 do
    begin
      N := Length(Piece);
      if N > Left then
        N := Left;
      F.WriteBuffer(Piece[0], N);
      Left := Left - N;
    end;
    CheckEqI(BigSize, F.Size, 'the file has 104 857 600 bytes');
  finally
    F.Free;
  end;
end;

procedure RunBig;
var
  Name, Digest: string;
  F: TFileStream;
  Requests: Integer;
begin
  Section('a file of 100 MiB (RTL-1241)');
  Name := Big;
  BeginCount;
  Digest := THashSHA2.GetHashStringFromFile(Name);
  Requests := EndCount;
  CheckEqS(BigSHA256, Digest, 'THashSHA2.GetHashStringFromFile');
  Check((Requests > 0) and (CountedBytes >= 65536), 'the call was watched: it asked for its 64 KiB buffer');
  Check(CountedBytes < 1048576, 'and for less than 1 MiB of memory in total');
  CheckEqS(BigMD5, THashMD5.GetHashStringFromFile(Name), 'THashMD5.GetHashStringFromFile');
  F := TFileStream.Create(Name, fmOpenRead or fmShareDenyWrite);
  try
    CheckEqS(Digest, THashSHA2.GetHashString(F), 'equals THashSHA2.GetHashString of a TFileStream on the file');
    CheckEqI(BigSize, F.Position, 'which was read to its end');
  finally
    F.Free;
  end;
  Check(DeleteFile(Name), 'the file can be deleted: every stream on it was closed');
  Check(not FileExists(Name), 'and is gone');
end;

procedure RunSmall;
var
  Name: string;
  Typed: TFileName;
  F: TFileStream;
begin
  Section('small files, the three types (RTL-1241)');
  Name := Small('abc.txt', 'abc');
  CheckEqS(AbcMD5, THashMD5.GetHashStringFromFile(Name), 'THashMD5.GetHashStringFromFile');
  CheckEqS(AbcMD5, THash.DigestAsString(THashMD5.GetHashBytesFromFile(Name)), 'THashMD5.GetHashBytesFromFile');
  CheckEqS(AbcSHA1, THashSHA1.GetHashStringFromFile(Name), 'THashSHA1.GetHashStringFromFile');
  CheckEqS(AbcSHA1, THash.DigestAsString(THashSHA1.GetHashBytesFromFile(Name)), 'THashSHA1.GetHashBytesFromFile');
  CheckEqS(AbcSHA256, THashSHA2.GetHashStringFromFile(Name), 'THashSHA2.GetHashStringFromFile: SHA256 without a version');
  CheckEqS(AbcSHA256, THash.DigestAsString(THashSHA2.GetHashBytesFromFile(Name)), 'THashSHA2.GetHashBytesFromFile');
  CheckEqS(AbcSHA512_256, THashSHA2.GetHashStringFromFile(Name, THashSHA2.TSHA2Version.SHA512_256), 'with a version');
  CheckEqS(AbcSHA512_256, THash.DigestAsString(THashSHA2.GetHashBytesFromFile(Name, THashSHA2.TSHA2Version.SHA512_256)),
    'GetHashBytesFromFile with a version');
  Typed := Name;
  CheckEqS(AbcMD5, THashMD5.GetHashStringFromFile(Typed), 'a TFileName variable as the name');

  { opened for reading, writers denied: a second reader is no obstacle, a writer is }
  F := TFileStream.Create(Name, fmOpenRead or fmShareDenyNone);
  try
    CheckEqS(AbcMD5, THashMD5.GetHashStringFromFile(Name), 'while another stream reads the file');
  finally
    F.Free;
  end;
  F := TFileStream.Create(Name, fmOpenReadWrite or fmShareDenyNone);
  try
    try
      THashMD5.GetHashStringFromFile(Name);
    except
      on E: Exception do
        Check((E.ClassName = 'EFOpenError') and (Pos('Cannot open file "', E.Message) = 1), 'while another stream may write: EFOpenError');
    end;
  finally
    F.Free;
  end;
  Check(DeleteFile(Name), 'the file is closed after each call: it can be deleted');

  Name := Small('empty.bin', '');
  CheckEqS('d41d8cd98f00b204e9800998ecf8427e', THashMD5.GetHashStringFromFile(Name), 'an empty file: MD5 of no data');
  CheckEqS('e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855', THashSHA2.GetHashStringFromFile(Name),
    'an empty file: SHA256 of no data');
  Check(DeleteFile(Name), 'deleted');
  Name := Small('caf'#$00E9' '#$4E2D'.txt', 'abc');
  CheckEqS(AbcSHA1, THashSHA1.GetHashStringFromFile(Name), 'a file name with characters outside ASCII');
  Check(DeleteFile(Name), 'deleted');

  Section('a file that cannot be opened (RTL-1241)');
  Name := Dir + 'missing.bin';
  try
    THashSHA2.GetHashStringFromFile(Name);
  except
    on E: Exception do
      Check((E.ClassName = 'EFOpenError') and (Pos('Cannot open file "', E.Message) = 1) and (Pos('missing.bin', E.Message) > 0),
        'THashSHA2.GetHashStringFromFile of a missing file: EFOpenError, Cannot open file "..."');
  end;
  try
    THashMD5.GetHashBytesFromFile(Name);
  except
    on E: EFOpenError do
      Check(True, 'THashMD5.GetHashBytesFromFile: EFOpenError');
  end;
  try
    THashSHA1.GetHashBytesFromFile(Dir + 'no-such-folder\x.bin');
  except
    on E: EFOpenError do
      Check(True, 'THashSHA1.GetHashBytesFromFile, missing folder: EFOpenError');
  end;
  try
    THashSHA2.GetHashBytesFromFile(Dir);
  except
    on E: EFOpenError do
      Check(True, 'THashSHA2.GetHashBytesFromFile of a folder: EFOpenError');
  end;
end;

var
  Before: NativeInt;
begin
  Dir := IncludeTrailingPathDelimiter(ParamStr(1));
  { the UTF-8 encoding object lives until the program ends: it exists before the count is taken }
  THashMD5.GetHashString('x');
  Before := _LiveAllocCount;
  RunBig;
  RunSmall;
  CheckEqI(Before, _LiveAllocCount, 'no block is left');
  Done;
end.
