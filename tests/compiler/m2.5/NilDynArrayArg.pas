// covers: LANG-266, LANG-420 (C-209)
program NilDynArrayArg;

{ The nil literal passed for a dynamic-array parameter (by value, const and of a named type) is the empty array. }

type
  TBytes = array of Byte;
  TInts = array of Integer;

function Len(const B: TBytes): Integer;
begin
  Result := Length(B);
end;

function Sum(A: TInts): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to High(A) do
    Inc(Result, A[I]);
end;

procedure Show(const Name: string; const B: TBytes);
begin
  Writeln(Name, ': ', Length(B), ' ', B = nil);
end;

var
  B: TBytes;
begin
  Writeln(Len(nil));
  Writeln(Sum(nil));
  Show('nil', nil);
  SetLength(B, 3);
  Show('three', B);
  B[0] := 4; B[2] := 5;
  Writeln(Len(B));
end.
