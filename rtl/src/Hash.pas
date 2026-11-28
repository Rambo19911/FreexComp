// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ Hash - message digests (docs: rtl/other-units.md section 13, RTL-1230): MD5 as RFC 1321 defines it, SHA-1 and the
  six SHA-2 functions as FIPS 180-4 defines them. Every function is computed here, in Object Pascal, from its
  standard: nothing in this unit calls the operating system.
  A state record takes data in any number of Update calls; HashAsBytes and HashAsString give the digest of what was
  added so far and leave the record as it was, so more data may follow. A record holds its whole state in itself:
  assigning it to another variable gives a second, independent state, and no record needs to be released. One
  variable must not be used by two threads at the same time. }
{$Q-,R-}
unit Hash;

interface

uses
  SysUtils, Classes;

{ MD5 and SHA-1 are provided for compatibility with existing file formats and protocols only. They are not suitable
  for security purposes - signatures, certificates, password storage, or protecting data against deliberate
  modification - because collisions can be produced for both. Use THashSHA2 where security matters. CRC-32, the Bob
  Jenkins hash and FNV-1a are checksums and hash-table functions and give no protection against deliberate
  modification. }

type
  EHashException = class(Exception);

  { Helpers for digests as returned by HashAsBytes and GetHashBytes. }
  THash = record
    { The 4 bytes of ADigest read as one number, first byte most significant; the bit pattern is kept, so the result
      may be negative. Any other length raises EHashException. }
    class function DigestAsInteger(const ADigest: TBytes): Integer; static;
    { Two lower-case hexadecimal digits per byte, in the order of the array. }
    class function DigestAsString(const ADigest: TBytes): string; static;
  end;

  { MD5 (RFC 1321): 16-byte digest, 64-byte blocks. For existing formats and protocols only: MD5 is not suitable for
    security purposes. }
  THashMD5 = record
  private
    FState: array[0..3] of Cardinal;
    FBuffer: array[0..63] of Byte;   { the bytes of the block begun: FCount mod 64 of them }
    FCount: UInt64;                  { bytes added so far }
  public
    { A record in the initial state. A variable that was neither returned by Create nor Reset is not usable. }
    class function Create: THashMD5; static;
    procedure Reset;
    { Adds ALength bytes starting at AData. }
    procedure Update(const AData; ALength: Cardinal); overload;
    { Adds the first ALength bytes of AData; 0 adds the whole array. More than the array holds raises
      EArgumentOutOfRangeException. }
    procedure Update(const AData: TBytes; ALength: Cardinal = 0); overload;
    { Adds the UTF-8 form of Input. }
    procedure Update(const Input: string); overload;
    { Adds what AStream returns from its current position until Read returns 0. A nil stream raises
      EArgumentNilException. }
    procedure Update(const AStream: TStream); overload;
    function GetBlockSize: Integer;
    function GetHashSize: Integer;
    { The digest of everything added so far; the record is not changed. }
    function HashAsBytes: TBytes;
    function HashAsString: string;
    { Create, one Update, HashAsBytes. A string is hashed in its UTF-8 form, a stream from its current position to
      its end. GetHashString is the same digest in lower-case hexadecimal digits. }
    class function GetHashBytes(const AData: string): TBytes; overload; static;
    class function GetHashBytes(const AData: TBytes): TBytes; overload; static;
    class function GetHashBytes(const AStream: TStream): TBytes; overload; static;
    class function GetHashString(const AString: string): string; overload; static;
    class function GetHashString(const AData: TBytes): string; overload; static;
    class function GetHashString(const AStream: TStream): string; overload; static;
  end;

  { SHA-1 (FIPS 180-4): 20-byte digest, 64-byte blocks; the members are those of THashMD5. For existing formats and
    protocols only: SHA-1 is not suitable for security purposes. }
  THashSHA1 = record
  private
    FState: array[0..4] of Cardinal;
    FBuffer: array[0..63] of Byte;
    FCount: UInt64;
  public
    class function Create: THashSHA1; static;
    procedure Reset;
    procedure Update(const AData; ALength: Cardinal); overload;
    procedure Update(const AData: TBytes; ALength: Cardinal = 0); overload;
    procedure Update(const Input: string); overload;
    procedure Update(const AStream: TStream); overload;
    function GetBlockSize: Integer;
    function GetHashSize: Integer;
    function HashAsBytes: TBytes;
    function HashAsString: string;
    class function GetHashBytes(const AData: string): TBytes; overload; static;
    class function GetHashBytes(const AData: TBytes): TBytes; overload; static;
    class function GetHashBytes(const AStream: TStream): TBytes; overload; static;
    class function GetHashString(const AString: string): string; overload; static;
    class function GetHashString(const AData: TBytes): string; overload; static;
    class function GetHashString(const AStream: TStream): string; overload; static;
  end;

  { The SHA-2 functions of FIPS 180-4; the version is chosen at Create and kept by Reset. SHA224 and SHA256 work on
    64-byte blocks, the other four on 128-byte blocks; the digest has 28, 32, 48, 64, 28 and 32 bytes. The members
    are those of THashMD5; each one-call function takes the version as its last parameter. }
  THashSHA2 = record
  public
    type
      TSHA2Version = (SHA224, SHA256, SHA384, SHA512, SHA512_224, SHA512_256);
  private
    FVersion: TSHA2Version;
    FState: array[0..7] of UInt64;   { SHA224, SHA256: eight 32-bit words in the first 32 bytes }
    FBuffer: array[0..127] of Byte;
    FCount: UInt64;
  public
    class function Create(AHashVersion: TSHA2Version = TSHA2Version.SHA256): THashSHA2; static;
    procedure Reset;
    procedure Update(const AData; ALength: Cardinal); overload;
    procedure Update(const AData: TBytes; ALength: Cardinal = 0); overload;
    procedure Update(const Input: string); overload;
    procedure Update(const AStream: TStream); overload;
    function GetBlockSize: Integer;
    function GetHashSize: Integer;
    function HashAsBytes: TBytes;
    function HashAsString: string;
    class function GetHashBytes(const AData: string;
      AHashVersion: TSHA2Version = TSHA2Version.SHA256): TBytes; overload; static;
    class function GetHashBytes(const AData: TBytes;
      AHashVersion: TSHA2Version = TSHA2Version.SHA256): TBytes; overload; static;
    class function GetHashBytes(const AStream: TStream;
      AHashVersion: TSHA2Version = TSHA2Version.SHA256): TBytes; overload; static;
    class function GetHashString(const AString: string;
      AHashVersion: TSHA2Version = TSHA2Version.SHA256): string; overload; static;
    class function GetHashString(const AData: TBytes;
      AHashVersion: TSHA2Version = TSHA2Version.SHA256): string; overload; static;
    class function GetHashString(const AStream: TStream;
      AHashVersion: TSHA2Version = TSHA2Version.SHA256): string; overload; static;
  end;

implementation

uses
  RTLConsts;

const
  StreamBufferSize = 65536;

  { RFC 1321 section 3.4: T[i] = the integer part of 4294967296 x abs(sin(i)), i = 1 to 64 in radians, and the
    rotation of each step (four per round) }
  MD5Shift: array[0..15] of Byte = (7, 12, 17, 22, 5, 9, 14, 20, 4, 11, 16, 23, 6, 10, 15, 21);
  MD5T: array[0..63] of Cardinal = (
    $D76AA478, $E8C7B756, $242070DB, $C1BDCEEE, $F57C0FAF, $4787C62A, $A8304613, $FD469501,
    $698098D8, $8B44F7AF, $FFFF5BB1, $895CD7BE, $6B901122, $FD987193, $A679438E, $49B40821,
    $F61E2562, $C040B340, $265E5A51, $E9B6C7AA, $D62F105D, $02441453, $D8A1E681, $E7D3FBC8,
    $21E1CDE6, $C33707D6, $F4D50D87, $455A14ED, $A9E3E905, $FCEFA3F8, $676F02D9, $8D2A4C8A,
    $FFFA3942, $8771F681, $6D9D6122, $FDE5380C, $A4BEEA44, $4BDECFA9, $F6BB4B60, $BEBFBC70,
    $289B7EC6, $EAA127FA, $D4EF3085, $04881D05, $D9D4D039, $E6DB99E5, $1FA27CF8, $C4AC5665,
    $F4292244, $432AFF97, $AB9423A7, $FC93A039, $655B59C3, $8F0CCC92, $FFEFF47D, $85845DD1,
    $6FA87E4F, $FE2CE6E0, $A3014314, $4E0811A1, $F7537E82, $BD3AF235, $2AD7D2BB, $EB86D391);

  { FIPS 180-4 section 4.2.2: the first 32 bits of the fractional parts of the cube roots of the first 64 primes }
  SHA256K: array[0..63] of Cardinal = (
    $428A2F98, $71374491, $B5C0FBCF, $E9B5DBA5, $3956C25B, $59F111F1, $923F82A4, $AB1C5ED5,
    $D807AA98, $12835B01, $243185BE, $550C7DC3, $72BE5D74, $80DEB1FE, $9BDC06A7, $C19BF174,
    $E49B69C1, $EFBE4786, $0FC19DC6, $240CA1CC, $2DE92C6F, $4A7484AA, $5CB0A9DC, $76F988DA,
    $983E5152, $A831C66D, $B00327C8, $BF597FC7, $C6E00BF3, $D5A79147, $06CA6351, $14292967,
    $27B70A85, $2E1B2138, $4D2C6DFC, $53380D13, $650A7354, $766A0ABB, $81C2C92E, $92722C85,
    $A2BFE8A1, $A81A664B, $C24B8B70, $C76C51A3, $D192E819, $D6990624, $F40E3585, $106AA070,
    $19A4C116, $1E376C08, $2748774C, $34B0BCB5, $391C0CB3, $4ED8AA4A, $5B9CCA4F, $682E6FF3,
    $748F82EE, $78A5636F, $84C87814, $8CC70208, $90BEFFFA, $A4506CEB, $BEF9A3F7, $C67178F2);

  { FIPS 180-4 section 4.2.3: the first 64 bits of the fractional parts of the cube roots of the first 80 primes }
  SHA512K: array[0..79] of UInt64 = (
    $428A2F98D728AE22, $7137449123EF65CD, $B5C0FBCFEC4D3B2F, $E9B5DBA58189DBBC,
    $3956C25BF348B538, $59F111F1B605D019, $923F82A4AF194F9B, $AB1C5ED5DA6D8118,
    $D807AA98A3030242, $12835B0145706FBE, $243185BE4EE4B28C, $550C7DC3D5FFB4E2,
    $72BE5D74F27B896F, $80DEB1FE3B1696B1, $9BDC06A725C71235, $C19BF174CF692694,
    $E49B69C19EF14AD2, $EFBE4786384F25E3, $0FC19DC68B8CD5B5, $240CA1CC77AC9C65,
    $2DE92C6F592B0275, $4A7484AA6EA6E483, $5CB0A9DCBD41FBD4, $76F988DA831153B5,
    $983E5152EE66DFAB, $A831C66D2DB43210, $B00327C898FB213F, $BF597FC7BEEF0EE4,
    $C6E00BF33DA88FC2, $D5A79147930AA725, $06CA6351E003826F, $142929670A0E6E70,
    $27B70A8546D22FFC, $2E1B21385C26C926, $4D2C6DFC5AC42AED, $53380D139D95B3DF,
    $650A73548BAF63DE, $766A0ABB3C77B2A8, $81C2C92E47EDAEE6, $92722C851482353B,
    $A2BFE8A14CF10364, $A81A664BBC423001, $C24B8B70D0F89791, $C76C51A30654BE30,
    $D192E819D6EF5218, $D69906245565A910, $F40E35855771202A, $106AA07032BBD1B8,
    $19A4C116B8D2D0C8, $1E376C085141AB53, $2748774CDF8EEB99, $34B0BCB5E19B48A8,
    $391C0CB3C5C95A63, $4ED8AA4AE3418ACB, $5B9CCA4F7763E373, $682E6FF3D6B2B8A3,
    $748F82EE5DEFB2FC, $78A5636F43172F60, $84C87814A1F0AB72, $8CC702081A6439EC,
    $90BEFFFA23631E28, $A4506CEBDE82BDE9, $BEF9A3F7B2C67915, $C67178F2E372532B,
    $CA273ECEEA26619C, $D186B8C721C0C207, $EADA7DD6CDE0EB1E, $F57D4F7FEE6ED178,
    $06F067AA72176FBA, $0A637DC5A2C898A6, $113F9804BEF90DAE, $1B710B35131C471B,
    $28DB77F523047D84, $32CAAB7B40C72493, $3C9EBE0A15C9BEBC, $431D67C49C100D4C,
    $4CC5D4BECB3E42B6, $597F299CFC657E2A, $5FCB6FAB3AD6FAEC, $6C44198C4A475817);

  { initial values as printed in FIPS 180-4 section 5.3.2 (SHA-224) and 5.3.3 (SHA-256) }
  SHA256Init: array[0..1, 0..7] of Cardinal = (
    ($C1059ED8, $367CD507, $3070DD17, $F70E5939, $FFC00B31, $68581511, $64F98FA7, $BEFA4FA4),
    ($6A09E667, $BB67AE85, $3C6EF372, $A54FF53A, $510E527F, $9B05688C, $1F83D9AB, $5BE0CD19));

  { and in section 5.3.4 (SHA-384), 5.3.5 (SHA-512), 5.3.6.1 (SHA-512/224) and 5.3.6.2 (SHA-512/256) }
  SHA512Init: array[0..3, 0..7] of UInt64 = (
    ($CBBB9D5DC1059ED8, $629A292A367CD507, $9159015A3070DD17, $152FECD8F70E5939,
     $67332667FFC00B31, $8EB44A8768581511, $DB0C2E0D64F98FA7, $47B5481DBEFA4FA4),
    ($6A09E667F3BCC908, $BB67AE8584CAA73B, $3C6EF372FE94F82B, $A54FF53A5F1D36F1,
     $510E527FADE682D1, $9B05688C2B3E6C1F, $1F83D9ABFB41BD6B, $5BE0CD19137E2179),
    ($8C3D37C819544DA2, $73E1996689DCD4D6, $1DFAB7AE32FF9C82, $679DD514582F9FCF,
     $0F6D2B697BD44DA8, $77E36F7304C48942, $3F9D85A86A1D36C8, $1112E6AD91D692A1),
    ($22312194FC2BF72C, $9F555FA3C84C64C2, $2393B86B6F53B151, $963877195940EABD,
     $96283EE2A88EFFE3, $BE5E1E2553863992, $2B0199FC2C85B8AA, $0EB72DDC81C52CA2));

type
  TBlock = array[0..127] of Byte;
  PBlock = ^TBlock;
  TWords32 = array[0..7] of Cardinal;
  PWords32 = ^TWords32;
  TWords64 = array[0..7] of UInt64;
  PWords64 = ^TWords64;
  { one compression step: the block at Block goes into the chaining value at State }
  TCompress = procedure(State: Pointer; Block: PBlock);

{ ------------------------------------------------------------------ compression functions }

{ RFC 1321 section 3.4. The words of the block are little-endian. The four state words change roles after every
  step: the step writes the word that plays "a", then that word plays "b". }
procedure MD5Compress(State: Pointer; Block: PBlock);
var
  S: PWords32;
  X: array[0..15] of Cardinal;
  A, B, C, D, F, T: Cardinal;
  I, K, N: Integer;
begin
  S := PWords32(State);
  for I := 0 to 15 do
    X[I] := Cardinal(Block^[4 * I]) or (Cardinal(Block^[4 * I + 1]) shl 8)
      or (Cardinal(Block^[4 * I + 2]) shl 16) or (Cardinal(Block^[4 * I + 3]) shl 24);
  A := S^[0];
  B := S^[1];
  C := S^[2];
  D := S^[3];
  for I := 0 to 63 do
  begin
    case I shr 4 of
      0:
        begin
          F := (B and C) or (not B and D);
          K := I;
        end;
      1:
        begin
          F := (B and D) or (C and not D);
          K := (5 * I + 1) and 15;
        end;
      2:
        begin
          F := B xor C xor D;
          K := (3 * I + 5) and 15;
        end;
    else
      begin
        F := C xor (B or not D);
        K := (7 * I) and 15;
      end;
    end;
    N := MD5Shift[(I shr 4) * 4 + (I and 3)];
    T := A + F + X[K] + MD5T[I];
    A := D;
    D := C;
    C := B;
    B := B + ((T shl N) or (T shr (32 - N)));
  end;
  S^[0] := S^[0] + A;
  S^[1] := S^[1] + B;
  S^[2] := S^[2] + C;
  S^[3] := S^[3] + D;
end;

{ FIPS 180-4 section 6.1.2, with the functions and constants of 4.1.1 and 4.2.1. Words are big-endian. }
procedure SHA1Compress(State: Pointer; Block: PBlock);
var
  S: PWords32;
  W: array[0..79] of Cardinal;
  A, B, C, D, E, F, K, T: Cardinal;
  I: Integer;
begin
  S := PWords32(State);
  for I := 0 to 15 do
    W[I] := (Cardinal(Block^[4 * I]) shl 24) or (Cardinal(Block^[4 * I + 1]) shl 16)
      or (Cardinal(Block^[4 * I + 2]) shl 8) or Cardinal(Block^[4 * I + 3]);
  for I := 16 to 79 do
  begin
    T := W[I - 3] xor W[I - 8] xor W[I - 14] xor W[I - 16];
    W[I] := (T shl 1) or (T shr 31);
  end;
  A := S^[0];
  B := S^[1];
  C := S^[2];
  D := S^[3];
  E := S^[4];
  for I := 0 to 79 do
  begin
    case I div 20 of
      0:
        begin
          F := (B and C) xor (not B and D);
          K := $5A827999;
        end;
      1:
        begin
          F := B xor C xor D;
          K := $6ED9EBA1;
        end;
      2:
        begin
          F := (B and C) xor (B and D) xor (C and D);
          K := $8F1BBCDC;
        end;
    else
      begin
        F := B xor C xor D;
        K := $CA62C1D6;
      end;
    end;
    T := ((A shl 5) or (A shr 27)) + F + E + K + W[I];
    E := D;
    D := C;
    C := (B shl 30) or (B shr 2);
    B := A;
    A := T;
  end;
  S^[0] := S^[0] + A;
  S^[1] := S^[1] + B;
  S^[2] := S^[2] + C;
  S^[3] := S^[3] + D;
  S^[4] := S^[4] + E;
end;

{ FIPS 180-4 section 6.2.2 (SHA-256; SHA-224 differs in the initial value and the digest length only), with the
  functions of 4.1.2: a rotation to the right by n is written (x shr n) or (x shl (32 - n)). }
procedure SHA256Compress(State: Pointer; Block: PBlock);
var
  S: PWords32;
  W: array[0..63] of Cardinal;
  A, B, C, D, E, F, G, H, T1, T2, X: Cardinal;
  I: Integer;
begin
  S := PWords32(State);
  for I := 0 to 15 do
    W[I] := (Cardinal(Block^[4 * I]) shl 24) or (Cardinal(Block^[4 * I + 1]) shl 16)
      or (Cardinal(Block^[4 * I + 2]) shl 8) or Cardinal(Block^[4 * I + 3]);
  for I := 16 to 63 do
  begin
    X := W[I - 15];
    T1 := ((X shr 7) or (X shl 25)) xor ((X shr 18) or (X shl 14)) xor (X shr 3);
    X := W[I - 2];
    T2 := ((X shr 17) or (X shl 15)) xor ((X shr 19) or (X shl 13)) xor (X shr 10);
    W[I] := T2 + W[I - 7] + T1 + W[I - 16];
  end;
  A := S^[0];
  B := S^[1];
  C := S^[2];
  D := S^[3];
  E := S^[4];
  F := S^[5];
  G := S^[6];
  H := S^[7];
  for I := 0 to 63 do
  begin
    T1 := H + (((E shr 6) or (E shl 26)) xor ((E shr 11) or (E shl 21)) xor ((E shr 25) or (E shl 7)))
      + ((E and F) xor (not E and G)) + SHA256K[I] + W[I];
    T2 := (((A shr 2) or (A shl 30)) xor ((A shr 13) or (A shl 19)) xor ((A shr 22) or (A shl 10)))
      + ((A and B) xor (A and C) xor (B and C));
    H := G;
    G := F;
    F := E;
    E := D + T1;
    D := C;
    C := B;
    B := A;
    A := T1 + T2;
  end;
  S^[0] := S^[0] + A;
  S^[1] := S^[1] + B;
  S^[2] := S^[2] + C;
  S^[3] := S^[3] + D;
  S^[4] := S^[4] + E;
  S^[5] := S^[5] + F;
  S^[6] := S^[6] + G;
  S^[7] := S^[7] + H;
end;

{ FIPS 180-4 section 6.4.2 (SHA-512; SHA-384, SHA-512/224 and SHA-512/256 differ in the initial value and the
  digest length only), with the functions of 4.1.3 on 64-bit words. }
procedure SHA512Compress(State: Pointer; Block: PBlock);
var
  S: PWords64;
  W: array[0..79] of UInt64;
  A, B, C, D, E, F, G, H, T1, T2, X: UInt64;
  I, J: Integer;
begin
  S := PWords64(State);
  for I := 0 to 15 do
  begin
    X := 0;
    for J := 0 to 7 do
      X := (X shl 8) or UInt64(Block^[8 * I + J]);
    W[I] := X;
  end;
  for I := 16 to 79 do
  begin
    X := W[I - 15];
    T1 := ((X shr 1) or (X shl 63)) xor ((X shr 8) or (X shl 56)) xor (X shr 7);
    X := W[I - 2];
    T2 := ((X shr 19) or (X shl 45)) xor ((X shr 61) or (X shl 3)) xor (X shr 6);
    W[I] := T2 + W[I - 7] + T1 + W[I - 16];
  end;
  A := S^[0];
  B := S^[1];
  C := S^[2];
  D := S^[3];
  E := S^[4];
  F := S^[5];
  G := S^[6];
  H := S^[7];
  for I := 0 to 79 do
  begin
    T1 := H + (((E shr 14) or (E shl 50)) xor ((E shr 18) or (E shl 46)) xor ((E shr 41) or (E shl 23)))
      + ((E and F) xor (not E and G)) + SHA512K[I] + W[I];
    T2 := (((A shr 28) or (A shl 36)) xor ((A shr 34) or (A shl 30)) xor ((A shr 39) or (A shl 25)))
      + ((A and B) xor (A and C) xor (B and C));
    H := G;
    G := F;
    F := E;
    E := D + T1;
    D := C;
    C := B;
    B := A;
    A := T1 + T2;
  end;
  S^[0] := S^[0] + A;
  S^[1] := S^[1] + B;
  S^[2] := S^[2] + C;
  S^[3] := S^[3] + D;
  S^[4] := S^[4] + E;
  S^[5] := S^[5] + F;
  S^[6] := S^[6] + G;
  S^[7] := S^[7] + H;
end;

{ ------------------------------------------------------------------ blocks, padding, arguments }

{ Adds Size bytes at Data to a function that works on blocks of BlockSize bytes (64 or 128). Buffer holds the bytes
  of the block begun - Count mod BlockSize of them - and Count the bytes added so far. Whole blocks are compressed
  straight from Data; nothing is allocated. }
procedure Absorb(Compress: TCompress; State: Pointer; Buffer: PBlock; BlockSize: Cardinal; var Count: UInt64;
  Data: PByte; Size: Cardinal);
var
  Used, Take: Cardinal;
begin
  if Size = 0 then
    Exit;   { Data is not looked at: it may be nil }
  Used := Cardinal(Count and (BlockSize - 1));
  Count := Count + Size;
  if Used > 0 then
  begin
    Take := BlockSize - Used;
    if Take > Size then
      Take := Size;
    Move(Data^, Buffer^[Used], Take);
    Inc(Data, Take);
    Size := Size - Take;
    if Used + Take < BlockSize then
      Exit;
    Compress(State, Buffer);
  end;
  while Size >= BlockSize do
  begin
    Compress(State, PBlock(Data));
    Inc(Data, BlockSize);
    Size := Size - BlockSize;
  end;
  if Size > 0 then
    Move(Data^, Buffer^[0], Size);
end;

{ Ends the message (RFC 1321 section 3.1 and 3.2; FIPS 180-4 section 5.1.1 and 5.1.2): one byte $80, zero bytes up
  to the length field at the end of a block, then the number of message bits - in 8 bytes for a 64-byte block, in 16
  bytes for a 128-byte block, least significant byte first for MD5 and most significant byte first otherwise. The
  bit count is Count x 8: the 3 bits that leave the 64-bit number are the lowest bits of the upper half of the
  16-byte field. }
procedure Complete(Compress: TCompress; State: Pointer; Buffer: PBlock; BlockSize: Integer; var Count: UInt64;
  LowByteFirst: Boolean);
var
  Pad: array[0..143] of Byte;   { at most one block and the length field }
  Field, Used, Fill, I: Integer;
  Bytes, Bits: UInt64;
begin
  Bytes := Count;
  Bits := Bytes shl 3;
  Field := BlockSize div 8;
  Used := Integer(Bytes and UInt64(BlockSize - 1));
  if Used < BlockSize - Field then
    Fill := BlockSize - Field - Used
  else
    Fill := 2 * BlockSize - Field - Used;
  FillChar(Pad, SizeOf(Pad), 0);
  Pad[0] := $80;
  for I := 0 to 7 do
    if LowByteFirst then
      Pad[Fill + I] := Byte(Bits shr (8 * I))
    else
      Pad[Fill + Field - 1 - I] := Byte(Bits shr (8 * I));
  if Field = 16 then
    Pad[Fill + 7] := Byte(Bytes shr 61);
  Absorb(Compress, State, Buffer, Cardinal(BlockSize), Count, @Pad[0], Cardinal(Fill + Field));
end;

{ The number of bytes Update(AData, ALength) adds: the whole array for 0 (RTL-1235); never more than the array
  holds (RTL-1247). }
function CountOf(const AData: TBytes; ALength: Cardinal): Cardinal;
begin
  if ALength > Cardinal(Length(AData)) then
    raise EArgumentOutOfRangeException.Create(SArgumentOutOfRange);
  if ALength = 0 then
    Result := Cardinal(Length(AData))
  else
    Result := ALength;
end;

{ The buffer the stream overloads read through (RTL-1235); a nil stream raises (RTL-1247). }
function StreamBuffer(AStream: TStream): TBytes;
begin
  if AStream = nil then
    raise EArgumentNilException.Create(SArgumentNil);
  SetLength(Result, StreamBufferSize);
end;

{ ------------------------------------------------------------------ THash }

class function THash.DigestAsInteger(const ADigest: TBytes): Integer;
begin
  if Length(ADigest) <> 4 then
    raise EHashException.CreateFmt(SHashDigestSize, [4]);
  Result := Integer((Cardinal(ADigest[0]) shl 24) or (Cardinal(ADigest[1]) shl 16)
    or (Cardinal(ADigest[2]) shl 8) or Cardinal(ADigest[3]));
end;

class function THash.DigestAsString(const ADigest: TBytes): string;
const
  Digits = '0123456789abcdef';
var
  I: Integer;
begin
  SetLength(Result, 2 * Length(ADigest));
  for I := 0 to Length(ADigest) - 1 do
  begin
    Result[2 * I + 1] := Digits[(ADigest[I] shr 4) + 1];
    Result[2 * I + 2] := Digits[(ADigest[I] and 15) + 1];
  end;
end;

{ ------------------------------------------------------------------ THashMD5 }

class function THashMD5.Create: THashMD5;
begin
  Result.Reset;
end;

{ RFC 1321 section 3.3 }
procedure THashMD5.Reset;
begin
  FState[0] := $67452301;
  FState[1] := $EFCDAB89;
  FState[2] := $98BADCFE;
  FState[3] := $10325476;
  FillChar(FBuffer, SizeOf(FBuffer), 0);
  FCount := 0;
end;

procedure THashMD5.Update(const AData; ALength: Cardinal);
begin
  Absorb(MD5Compress, @FState, @FBuffer, 64, FCount, @AData, ALength);
end;

procedure THashMD5.Update(const AData: TBytes; ALength: Cardinal);
var
  Count: Cardinal;
begin
  Count := CountOf(AData, ALength);
  if Count > 0 then
    Update(AData[0], Count);
end;

procedure THashMD5.Update(const Input: string);
begin
  Update(TEncoding.UTF8.GetBytes(Input));
end;

procedure THashMD5.Update(const AStream: TStream);
var
  Buffer: TBytes;
  Got: Integer;
begin
  Buffer := StreamBuffer(AStream);
  repeat
    Got := AStream.Read(Buffer[0], StreamBufferSize);
    if Got > 0 then
      Update(Buffer[0], Cardinal(Got));
  until Got <= 0;
end;

function THashMD5.GetBlockSize: Integer;
begin
  Result := 64;
end;

function THashMD5.GetHashSize: Integer;
begin
  Result := 16;
end;

{ RFC 1321 section 3.5: A, B, C, D, each with its low byte first }
function THashMD5.HashAsBytes: TBytes;
var
  Work: THashMD5;
  I: Integer;
begin
  Work := Self;
  Complete(MD5Compress, @Work.FState, @Work.FBuffer, 64, Work.FCount, True);
  SetLength(Result, 16);
  for I := 0 to 15 do
    Result[I] := Byte(Work.FState[I shr 2] shr (8 * (I and 3)));
end;

function THashMD5.HashAsString: string;
begin
  Result := THash.DigestAsString(HashAsBytes);
end;

class function THashMD5.GetHashBytes(const AData: string): TBytes;
var
  H: THashMD5;
begin
  H := THashMD5.Create;
  H.Update(AData);
  Result := H.HashAsBytes;
end;

class function THashMD5.GetHashBytes(const AData: TBytes): TBytes;
var
  H: THashMD5;
begin
  H := THashMD5.Create;
  H.Update(AData);
  Result := H.HashAsBytes;
end;

class function THashMD5.GetHashBytes(const AStream: TStream): TBytes;
var
  H: THashMD5;
begin
  H := THashMD5.Create;
  H.Update(AStream);
  Result := H.HashAsBytes;
end;

class function THashMD5.GetHashString(const AString: string): string;
begin
  Result := THash.DigestAsString(GetHashBytes(AString));
end;

class function THashMD5.GetHashString(const AData: TBytes): string;
begin
  Result := THash.DigestAsString(GetHashBytes(AData));
end;

class function THashMD5.GetHashString(const AStream: TStream): string;
begin
  Result := THash.DigestAsString(GetHashBytes(AStream));
end;

{ ------------------------------------------------------------------ THashSHA1 }

class function THashSHA1.Create: THashSHA1;
begin
  Result.Reset;
end;

{ FIPS 180-4 section 5.3.1 }
procedure THashSHA1.Reset;
begin
  FState[0] := $67452301;
  FState[1] := $EFCDAB89;
  FState[2] := $98BADCFE;
  FState[3] := $10325476;
  FState[4] := $C3D2E1F0;
  FillChar(FBuffer, SizeOf(FBuffer), 0);
  FCount := 0;
end;

procedure THashSHA1.Update(const AData; ALength: Cardinal);
begin
  Absorb(SHA1Compress, @FState, @FBuffer, 64, FCount, @AData, ALength);
end;

procedure THashSHA1.Update(const AData: TBytes; ALength: Cardinal);
var
  Count: Cardinal;
begin
  Count := CountOf(AData, ALength);
  if Count > 0 then
    Update(AData[0], Count);
end;

procedure THashSHA1.Update(const Input: string);
begin
  Update(TEncoding.UTF8.GetBytes(Input));
end;

procedure THashSHA1.Update(const AStream: TStream);
var
  Buffer: TBytes;
  Got: Integer;
begin
  Buffer := StreamBuffer(AStream);
  repeat
    Got := AStream.Read(Buffer[0], StreamBufferSize);
    if Got > 0 then
      Update(Buffer[0], Cardinal(Got));
  until Got <= 0;
end;

function THashSHA1.GetBlockSize: Integer;
begin
  Result := 64;
end;

function THashSHA1.GetHashSize: Integer;
begin
  Result := 20;
end;

{ the five words, each with its high byte first }
function THashSHA1.HashAsBytes: TBytes;
var
  Work: THashSHA1;
  I: Integer;
begin
  Work := Self;
  Complete(SHA1Compress, @Work.FState, @Work.FBuffer, 64, Work.FCount, False);
  SetLength(Result, 20);
  for I := 0 to 19 do
    Result[I] := Byte(Work.FState[I shr 2] shr (24 - 8 * (I and 3)));
end;

function THashSHA1.HashAsString: string;
begin
  Result := THash.DigestAsString(HashAsBytes);
end;

class function THashSHA1.GetHashBytes(const AData: string): TBytes;
var
  H: THashSHA1;
begin
  H := THashSHA1.Create;
  H.Update(AData);
  Result := H.HashAsBytes;
end;

class function THashSHA1.GetHashBytes(const AData: TBytes): TBytes;
var
  H: THashSHA1;
begin
  H := THashSHA1.Create;
  H.Update(AData);
  Result := H.HashAsBytes;
end;

class function THashSHA1.GetHashBytes(const AStream: TStream): TBytes;
var
  H: THashSHA1;
begin
  H := THashSHA1.Create;
  H.Update(AStream);
  Result := H.HashAsBytes;
end;

class function THashSHA1.GetHashString(const AString: string): string;
begin
  Result := THash.DigestAsString(GetHashBytes(AString));
end;

class function THashSHA1.GetHashString(const AData: TBytes): string;
begin
  Result := THash.DigestAsString(GetHashBytes(AData));
end;

class function THashSHA1.GetHashString(const AStream: TStream): string;
begin
  Result := THash.DigestAsString(GetHashBytes(AStream));
end;

{ ------------------------------------------------------------------ THashSHA2 }

class function THashSHA2.Create(AHashVersion: TSHA2Version): THashSHA2;
begin
  Result.FVersion := AHashVersion;
  Result.Reset;
end;

procedure THashSHA2.Reset;
var
  I: Integer;
begin
  FillChar(FState, SizeOf(FState), 0);
  for I := 0 to 7 do
    if FVersion >= TSHA2Version.SHA384 then
      FState[I] := SHA512Init[Ord(FVersion) - Ord(TSHA2Version.SHA384), I]
    else
      PWords32(@FState)^[I] := SHA256Init[Ord(FVersion), I];
  FillChar(FBuffer, SizeOf(FBuffer), 0);
  FCount := 0;
end;

procedure THashSHA2.Update(const AData; ALength: Cardinal);
begin
  if FVersion >= TSHA2Version.SHA384 then
    Absorb(SHA512Compress, @FState, @FBuffer, 128, FCount, @AData, ALength)
  else
    Absorb(SHA256Compress, @FState, @FBuffer, 64, FCount, @AData, ALength);
end;

procedure THashSHA2.Update(const AData: TBytes; ALength: Cardinal);
var
  Count: Cardinal;
begin
  Count := CountOf(AData, ALength);
  if Count > 0 then
    Update(AData[0], Count);
end;

procedure THashSHA2.Update(const Input: string);
begin
  Update(TEncoding.UTF8.GetBytes(Input));
end;

procedure THashSHA2.Update(const AStream: TStream);
var
  Buffer: TBytes;
  Got: Integer;
begin
  Buffer := StreamBuffer(AStream);
  repeat
    Got := AStream.Read(Buffer[0], StreamBufferSize);
    if Got > 0 then
      Update(Buffer[0], Cardinal(Got));
  until Got <= 0;
end;

function THashSHA2.GetBlockSize: Integer;
begin
  if FVersion >= TSHA2Version.SHA384 then
    Result := 128
  else
    Result := 64;
end;

function THashSHA2.GetHashSize: Integer;
begin
  case FVersion of
    TSHA2Version.SHA224, TSHA2Version.SHA512_224: Result := 28;
    TSHA2Version.SHA256, TSHA2Version.SHA512_256: Result := 32;
    TSHA2Version.SHA384: Result := 48;
  else
    Result := 64;
  end;
end;

{ the leftmost GetHashSize bytes of the eight words, each with its high byte first }
function THashSHA2.HashAsBytes: TBytes;
var
  Work: THashSHA2;
  I: Integer;
begin
  Work := Self;
  SetLength(Result, GetHashSize);
  if FVersion >= TSHA2Version.SHA384 then
  begin
    Complete(SHA512Compress, @Work.FState, @Work.FBuffer, 128, Work.FCount, False);
    for I := 0 to Length(Result) - 1 do
      Result[I] := Byte(Work.FState[I shr 3] shr (56 - 8 * (I and 7)));
  end
  else
  begin
    Complete(SHA256Compress, @Work.FState, @Work.FBuffer, 64, Work.FCount, False);
    for I := 0 to Length(Result) - 1 do
      Result[I] := Byte(PWords32(@Work.FState)^[I shr 2] shr (24 - 8 * (I and 3)));
  end;
end;

function THashSHA2.HashAsString: string;
begin
  Result := THash.DigestAsString(HashAsBytes);
end;

class function THashSHA2.GetHashBytes(const AData: string; AHashVersion: TSHA2Version): TBytes;
var
  H: THashSHA2;
begin
  H := THashSHA2.Create(AHashVersion);
  H.Update(AData);
  Result := H.HashAsBytes;
end;

class function THashSHA2.GetHashBytes(const AData: TBytes; AHashVersion: TSHA2Version): TBytes;
var
  H: THashSHA2;
begin
  H := THashSHA2.Create(AHashVersion);
  H.Update(AData);
  Result := H.HashAsBytes;
end;

class function THashSHA2.GetHashBytes(const AStream: TStream; AHashVersion: TSHA2Version): TBytes;
var
  H: THashSHA2;
begin
  H := THashSHA2.Create(AHashVersion);
  H.Update(AStream);
  Result := H.HashAsBytes;
end;

class function THashSHA2.GetHashString(const AString: string; AHashVersion: TSHA2Version): string;
begin
  Result := THash.DigestAsString(GetHashBytes(AString, AHashVersion));
end;

class function THashSHA2.GetHashString(const AData: TBytes; AHashVersion: TSHA2Version): string;
begin
  Result := THash.DigestAsString(GetHashBytes(AData, AHashVersion));
end;

class function THashSHA2.GetHashString(const AStream: TStream; AHashVersion: TSHA2Version): string;
begin
  Result := THash.DigestAsString(GetHashBytes(AStream, AHashVersion));
end;

end.
