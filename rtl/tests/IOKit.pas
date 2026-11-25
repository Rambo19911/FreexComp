{ Helpers of the IOUtils test programs: bytes as hex text, path lists as sorted names, raw stream writes. }
unit IOKit;

interface

uses
  SysUtils, Classes, Types;

function Hex(const B: TBytes): string;
{ the names of a path list without the directory part Base, sorted, joined with '|' }
function Names(const A: TStringDynArray; const Base: string): string;
{ count:[item][item]... }
function Joined(const A: TStringDynArray): string;
{ the ASCII bytes of Text }
procedure WriteRaw(S: TStream; const Text: string);

implementation

function Hex(const B: TBytes): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Length(B) - 1 do
    Result := Result + IntToHex(B[I], 2);
end;

function Names(const A: TStringDynArray; const Base: string): string;
var
  L: TStringList;
  I: Integer;
begin
  L := TStringList.Create;
  try
    for I := 0 to Length(A) - 1 do
      if Copy(A[I], 1, Length(Base)) = Base then
        L.Add(Copy(A[I], Length(Base) + 1, Length(A[I])))
      else
        L.Add('?' + A[I]);
    L.Sort;
    Result := '';
    for I := 0 to L.Count - 1 do
    begin
      if I > 0 then
        Result := Result + '|';
      Result := Result + L[I];
    end;
  finally
    L.Free;
  end;
end;

function Joined(const A: TStringDynArray): string;
var
  I: Integer;
begin
  Result := IntToStr(Length(A)) + ':';
  for I := 0 to Length(A) - 1 do
    Result := Result + '[' + A[I] + ']';
end;

procedure WriteRaw(S: TStream; const Text: string);
var
  B: TBytes;
begin
  B := TEncoding.ASCII.GetBytes(Text);
  S.WriteBuffer(B[0], Length(B));
end;

end.
