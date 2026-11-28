{ Helpers of the Hash test programs: the eight digest functions behind one record, test data, a stream that
  records how it is read. }
unit HashKit;

interface

uses
  SysUtils, Classes, Hash;

const
  { 0 = MD5, 1 = SHA-1, 2 to 7 = the versions of THashSHA2 in declared order }
  HashNames: array[0..7] of string = ('MD5', 'SHA-1', 'SHA224', 'SHA256', 'SHA384', 'SHA512', 'SHA512_224', 'SHA512_256');

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

  { a stream that hands out at most Limit bytes per call (0 = no limit) and records the largest count asked for }
  TSlowStream = class(TMemoryStream)
  public
    Limit, MaxAsked: Integer;
    function Read(var Buffer; Count: Integer): Integer; override;
  end;

{ one byte per character (codes below 256) }
function ToBytes(const S: string): TBytes;
{ P(N): N bytes, byte I = I mod 251 }
function Pattern(N: Integer): TBytes;
{ a stream holding the characters of S, one byte each, positioned at Position }
function TextStream(const S: string; Position: Integer): TSlowStream;

implementation

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
