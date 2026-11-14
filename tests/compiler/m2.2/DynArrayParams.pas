// covers: LANG-257, ABI-031, ABI-032, ABI-080, ABI-081, CMP-325
program DynArrayParams;
// Dynamic arrays as value, const, var and out parameters and as function results: a value parameter shares the caller's
// block (element writes are seen, reassignment is not), var/out replace the caller's array, results arrive through the
// hidden Result, nested routines reach an outer array.
type
  TInts = array of Integer;

procedure Fill(var X: TInts; N: Integer);
var
  K: Integer;
begin
  SetLength(X, N);
  for K := 0 to N - 1 do
    X[K] := K + 1;
end;

function Total(const X: TInts): Integer;
var
  K: Integer;
begin
  Result := 0;
  for K := 0 to High(X) do
    Result := Result + X[K];
end;

procedure ByValue(X: TInts);
begin
  X[0] := 100;
  SetLength(X, 1);
  X[0] := -1;
  X := nil;
end;

procedure MakeOut(out X: TInts);
begin
  Writeln('out in: ', Length(X));
  SetLength(X, 2);
  X[1] := 5;
end;

function Range(Low, High: Integer): TInts;
var
  K: Integer;
begin
  SetLength(Result, High - Low + 1);
  for K := Low to High do
    Result[K - Low] := K;
end;

function Doubled(const X: TInts): TInts;
var
  K: Integer;
begin
  Result := Copy(X);
  for K := 0 to High(Result) do
    Result[K] := Result[K] * 2;
end;

function Outer: Integer;
var
  Local: TInts;

  procedure Grow;
  begin
    SetLength(Local, Length(Local) + 1);
    Local[High(Local)] := Length(Local);
  end;

begin
  Grow;
  Grow;
  Grow;
  Result := Total(Local);
end;

var
  A, B: TInts;
begin
  Fill(A, 4);
  Writeln(Length(A), ' ', Total(A));
  ByValue(A);
  Writeln(Length(A), ' ', A[0], ' ', Total(A));
  MakeOut(A);
  Writeln(Length(A), ' ', A[0], ' ', A[1]);
  B := Range(3, 7);
  Writeln(Length(B), ' ', Total(B), ' ', B[0], ' ', B[4]);
  Writeln(Total(Range(1, 10)), ' ', Total(Doubled(B)), ' ', Total(B));
  B := Doubled(B);
  Writeln(B[0], ' ', B[4]);
  A := Range(5, 4);
  Writeln(Length(A), ' ', A = nil);
  Writeln(Outer);
end.
