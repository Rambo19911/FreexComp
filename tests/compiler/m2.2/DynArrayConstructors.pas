// covers: LANG-257, LANG-259, LANG-280
program DynArrayConstructors;
type
  TInts = array of Integer;
  TGrid = array of TInts;
  TStrs = array of string;

var
  Before: NativeInt;

function Sum(const X: TInts): Integer;
var
  K: Integer;
begin
  Result := 0;
  for K := 0 to High(X) do
    Result := Result + X[K];
end;

function Make(N: Integer): TStrs;
begin
  Result := ['n', 'm'];
  if N > 2 then
    Exit(['a', 'b', 'c']);
end;

procedure Scenario;
var
  A: TInts;
  G: TGrid;
  S: TStrs;
  B: Boolean;
  I: Integer;
begin
  B := Assigned(A);
  A := [1, 2, 3];
  Writeln(B, ' ', Assigned(A), ' ', Length(A), ' ', A[2]);
  A := [A[2], A[1], A[0]];
  Writeln(A[0], A[1], A[2]);
  S := ['x', 'yz'];
  Writeln(S[0], S[1], Length(S));
  G := [[1, 2], [], [3]];
  Writeln(Length(G), Length(G[0]), Length(G[1]), Assigned(G[1]), G[2][0]);
  Writeln(Sum([4, 5, 6]), ' ', Length(Make(1)), Length(Make(3)), Make(3)[2]);
  for I := 1 to 3 do
    A := [I];
  Writeln(A[0]);
  A := [];
  Writeln(Assigned(A));
end;

begin
  Before := _LiveAllocCount;
  Scenario;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
