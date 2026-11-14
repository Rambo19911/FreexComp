// covers: LANG-432
program OpenArrayValueCopy;

procedure Modify(A: array of Integer);
var
  I: Integer;
begin
  for I := 0 to High(A) do
    A[I] := A[I] + 100;
  Writeln('inside ', A[0], ' ', A[High(A)]);
end;

procedure ModifyStrings(A: array of string);
begin
  A[0] := A[0] + '!';
  Writeln('inside ', A[0], ' ', A[1], ' ', Length(A));
end;

procedure Big(A: array of Int64);
begin
  A[999] := -1;
  Writeln('big inside ', A[0], ' ', A[999]);
end;

procedure Empty(A: array of string);
begin
  Writeln('empty ', Length(A));
end;

var
  S: array[0..2] of Integer;
  D: array of string;
  B: array of Int64;
  I: Integer;
  Before: NativeInt;
begin
  S[0] := 1;
  S[1] := 2;
  S[2] := 3;
  Modify(S);
  Writeln('outside ', S[0], ' ', S[2]);
  Modify([5]);
  SetLength(D, 2);
  D[0] := 'x';
  D[1] := Copy('yz', 1, 1);
  Before := _LiveAllocCount;
  ModifyStrings(D);
  Writeln('outside ', D[0], ' ', D[1]);
  Writeln('leaked: ', _LiveAllocCount - Before);
  SetLength(B, 1000);
  for I := 0 to 999 do
    B[I] := I;
  Before := _LiveAllocCount;
  Big(B);
  Writeln('big outside ', B[0], ' ', B[999]);
  Writeln('leaked: ', _LiveAllocCount - Before);
  Empty([]);
end.
