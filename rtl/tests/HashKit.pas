{ Helpers of the Hash test programs: the eight digest functions behind one record, the four checksum and fast-hash
  types behind another, test data, a stream that records how it is read, a count of the memory requests. }
unit HashKit;

interface

uses
  SysUtils, Classes, Hash;

const
  { 0 = MD5, 1 = SHA-1, 2 to 7 = the versions of THashSHA2 in declared order }
  HashNames: array[0..7] of string = ('MD5', 'SHA-1', 'SHA224', 'SHA256', 'SHA384', 'SHA512', 'SHA512_224', 'SHA512_256');
  { 0 = THashCRC32, 1 = THashFNV1a32, 2 = THashFNV1a64, 3 = THashBobJenkins }
  SumNames: array[0..3] of string = ('CRC-32', 'FNV-1a 32', 'FNV-1a 64', 'lookup3');

type
  { one of the eight functions, chosen by the index of HashNames; a copy by assignment copies the whole state }
  TAnyHash = record
    Kind: Integer;
    M: THashMD5;
    S: THashSHA1;
    T: THashSHA2;
    procedure Start(AKind: Integer);
    procedure Reset;
    procedure Add(const Data; Count: Cardinal);
    procedure AddBytes(const Data: TBytes; Count: Cardinal);
    procedure AddText(const Value: string);
    procedure AddStream(Stream: TStream);
    function Text: string;
  end;

  { one of the four checksum and fast-hash types, chosen by the index of SumNames }
  TAnySum = record
    Kind: Integer;
    C: THashCRC32;
    F: THashFNV1a32;
    G: THashFNV1a64;
    J: THashBobJenkins;
    procedure Start(AKind: Integer);
    procedure Reset;
    procedure Add(const Data; Count: Cardinal);
    procedure AddBytes(const Data: TBytes; Count: Cardinal);
    procedure AddText(const Value: string);
    function Text: string;      { HashAsString }
    function Bytes: TBytes;     { HashAsBytes }
    function Value: Int64;      { HashAsInteger }
  end;

  { a stream that hands out at most Limit bytes per call (0 = no limit) and records the largest count asked for }
  TSlowStream = class(TMemoryStream)
  public
    Limit, MaxAsked: Integer;
    function Read(var Buffer; Count: Integer): Integer; override;
  end;

{ the one-call functions of type Kind (index of SumNames) for a string and for untyped data }
function SumString(Kind: Integer; const S: string): string;
function SumBytes(Kind: Integer; const S: string): TBytes;
function SumValue(Kind: Integer; const S: string): Int64;
function SumValueOf(Kind: Integer; const Data; Count: Cardinal): Int64;
{ a value of type Kind in lower-case hexadecimal digits: 8, or 16 for the 64-bit type }
function SumHex(Kind: Integer; V: Int64): string;
{ From BeginCount to EndCount every request to the memory manager is counted and passed on: EndCount returns the
  number of GetMem, AllocMem and ReallocMem calls, CountedBytes holds the sum of the sizes they asked for. }
procedure BeginCount;
function EndCount: Integer;
{ one byte per character (codes below 256) }
function ToBytes(const S: string): TBytes;
{ P(N): N bytes, byte I = I mod 251 }
function Pattern(N: Integer): TBytes;
{ a stream holding the characters of S, one byte each, positioned at Position }
function TextStream(const S: string; Position: Integer): TSlowStream;

var
  CountedBytes: NativeInt;

implementation

var
  Prev: TMemoryManager;
  Gets: Integer;

function CountGetMem(Size: NativeInt): Pointer;
begin
  Gets := Gets + 1;
  CountedBytes := CountedBytes + Size;
  Result := Prev.GetMem(Size);
end;

function CountAllocMem(Size: NativeInt): Pointer;
begin
  Gets := Gets + 1;
  CountedBytes := CountedBytes + Size;
  Result := Prev.AllocMem(Size);
end;

function CountReallocMem(P: Pointer; Size: NativeInt): Pointer;
begin
  Gets := Gets + 1;
  CountedBytes := CountedBytes + Size;
  Result := Prev.ReallocMem(P, Size);
end;

procedure BeginCount;
var
  Mine: TMemoryManager;
begin
  GetMemoryManager(Prev);
  Mine := Prev;
  Mine.GetMem := CountGetMem;
  Mine.AllocMem := CountAllocMem;
  Mine.ReallocMem := CountReallocMem;
  Gets := 0;
  CountedBytes := 0;
  SetMemoryManager(Mine);
end;

function EndCount: Integer;
begin
  SetMemoryManager(Prev);
  Result := Gets;
end;

procedure TAnyHash.Start(AKind: Integer);
begin
  Kind := AKind;
  M := THashMD5.Create;
  S := THashSHA1.Create;
  if AKind >= 2 then
    T := THashSHA2.Create(THashSHA2.TSHA2Version(AKind - 2))
  else
    T := THashSHA2.Create;
end;

procedure TAnyHash.Reset;
begin
  M.Reset;
  S.Reset;
  T.Reset;
end;

procedure TAnyHash.Add(const Data; Count: Cardinal);
begin
  case Kind of
    0: M.Update(Data, Count);
    1: S.Update(Data, Count);
  else
    T.Update(Data, Count);
  end;
end;

procedure TAnyHash.AddBytes(const Data: TBytes; Count: Cardinal);
begin
  case Kind of
    0: M.Update(Data, Count);
    1: S.Update(Data, Count);
  else
    T.Update(Data, Count);
  end;
end;

procedure TAnyHash.AddText(const Value: string);
begin
  case Kind of
    0: M.Update(Value);
    1: S.Update(Value);
  else
    T.Update(Value);
  end;
end;

procedure TAnyHash.AddStream(Stream: TStream);
begin
  case Kind of
    0: M.Update(Stream);
    1: S.Update(Stream);
  else
    T.Update(Stream);
  end;
end;

function TAnyHash.Text: string;
begin
  case Kind of
    0: Result := M.HashAsString;
    1: Result := S.HashAsString;
  else
    Result := T.HashAsString;
  end;
end;

procedure TAnySum.Start(AKind: Integer);
begin
  Kind := AKind;
  C := THashCRC32.Create;
  F := THashFNV1a32.Create;
  G := THashFNV1a64.Create;
  J := THashBobJenkins.Create;
end;

procedure TAnySum.Reset;
begin
  C.Reset;
  F.Reset;
  G.Reset;
  J.Reset;
end;

procedure TAnySum.Add(const Data; Count: Cardinal);
begin
  case Kind of
    0: C.Update(Data, Count);
    1: F.Update(Data, Count);
    2: G.Update(Data, Count);
  else
    J.Update(Data, Count);
  end;
end;

procedure TAnySum.AddBytes(const Data: TBytes; Count: Cardinal);
begin
  case Kind of
    0: C.Update(Data, Count);
    1: F.Update(Data, Count);
    2: G.Update(Data, Count);
  else
    J.Update(Data, Count);
  end;
end;

procedure TAnySum.AddText(const Value: string);
begin
  case Kind of
    0: C.Update(Value);
    1: F.Update(Value);
    2: G.Update(Value);
  else
    J.Update(Value);
  end;
end;

function TAnySum.Text: string;
begin
  case Kind of
    0: Result := C.HashAsString;
    1: Result := F.HashAsString;
    2: Result := G.HashAsString;
  else
    Result := J.HashAsString;
  end;
end;

function TAnySum.Bytes: TBytes;
begin
  case Kind of
    0: Result := C.HashAsBytes;
    1: Result := F.HashAsBytes;
    2: Result := G.HashAsBytes;
  else
    Result := J.HashAsBytes;
  end;
end;

function TAnySum.Value: Int64;
begin
  case Kind of
    0: Result := C.HashAsInteger;
    1: Result := F.HashAsInteger;
    2: Result := G.HashAsInteger;
  else
    Result := J.HashAsInteger;
  end;
end;

function SumString(Kind: Integer; const S: string): string;
begin
  case Kind of
    0: Result := THashCRC32.GetHashString(S);
    1: Result := THashFNV1a32.GetHashString(S);
    2: Result := THashFNV1a64.GetHashString(S);
  else
    Result := THashBobJenkins.GetHashString(S);
  end;
end;

function SumBytes(Kind: Integer; const S: string): TBytes;
begin
  case Kind of
    0: Result := THashCRC32.GetHashBytes(S);
    1: Result := THashFNV1a32.GetHashBytes(S);
    2: Result := THashFNV1a64.GetHashBytes(S);
  else
    Result := THashBobJenkins.GetHashBytes(S);
  end;
end;

function SumValue(Kind: Integer; const S: string): Int64;
begin
  case Kind of
    0: Result := THashCRC32.GetHashValue(S);
    1: Result := THashFNV1a32.GetHashValue(S);
    2: Result := THashFNV1a64.GetHashValue(S);
  else
    Result := THashBobJenkins.GetHashValue(S);
  end;
end;

function SumValueOf(Kind: Integer; const Data; Count: Cardinal): Int64;
begin
  case Kind of
    0: Result := THashCRC32.GetHashValue(Data, Count);
    1: Result := THashFNV1a32.GetHashValue(Data, Count);
    2: Result := THashFNV1a64.GetHashValue(Data, Count);
  else
    Result := THashBobJenkins.GetHashValue(Data, Integer(Count));
  end;
end;

function SumHex(Kind: Integer; V: Int64): string;
begin
  if Kind = 2 then
    Result := LowerCase(IntToHex(V, 16))
  else
    Result := LowerCase(IntToHex(V and $FFFFFFFF, 8));
end;

function TSlowStream.Read(var Buffer; Count: Integer): Integer;
begin
  if Count > MaxAsked then
    MaxAsked := Count;
  if (Limit > 0) and (Count > Limit) then
    Count := Limit;
  Result := inherited Read(Buffer, Count);
end;

function ToBytes(const S: string): TBytes;
var
  I: Integer;
begin
  SetLength(Result, Length(S));
  for I := 1 to Length(S) do
    Result[I - 1] := Byte(Ord(S[I]));
end;

function Pattern(N: Integer): TBytes;
var
  I: Integer;
begin
  SetLength(Result, N);
  for I := 0 to N - 1 do
    Result[I] := Byte(I mod 251);
end;

function TextStream(const S: string; Position: Integer): TSlowStream;
var
  D: TBytes;
begin
  Result := TSlowStream.Create;
  D := ToBytes(S);
  if Length(D) > 0 then
    Result.WriteBuffer(D[0], Length(D));
  Result.Position := Position;
end;

end.
