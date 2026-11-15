// phase: 7
// covers: RTL-010
program Sys_GenericArray;
{ system.md section 1 (RTL-010): TArray<T> = array of T is declared in System from M2.6 and is visible without a
  uses clause; instances are ordinary dynamic arrays (assignment-compatible with array of T variables of the same
  instance, reference semantics, managed elements). }
uses
  TestKit;

function Sum(const A: TArray<Integer>): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to High(A) do
    Result := Result + A[I];
end;

var
  A, B: TArray<Integer>;
  S: TArray<string>;
begin
  Section('TArray<T> (RTL-010)');
  SetLength(A, 3);
  A[0] := 1;
  A[1] := 2;
  A[2] := 4;
  CheckEqI(3, Length(A), 'Length');
  CheckEqI(7, Sum(A), 'passed as TArray<Integer>');
  B := A;
  B[0] := 10;
  CheckEqI(10, A[0], 'reference semantics');
  SetLength(S, 2);
  S[0] := 'x';
  S[1] := S[0] + 'y';
  CheckEqS('xy', S[1], 'managed elements');
  S := nil;
  CheckEqI(0, Length(S), 'nil');
  Done;
end.
