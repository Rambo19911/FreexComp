// covers: LANG-257, LANG-258, ABI-040, ABI-041, ABI-042, CMP-330, CMP-331
program DynArrayBasics;
// SetLength (new elements zero, kept ones preserved), 0-based indexing, Length/High/Low, nil as the empty array, and the
// {$R+} index check against the length (runtime error 201).
type
  TInts = array of Integer;
var
  A: TInts;
  D: array of Double;
  I, Sum: Integer;
begin
  Writeln(Length(A), ' ', High(A), ' ', Low(A), ' ', A = nil);
  SetLength(A, 5);
  Writeln(Length(A), ' ', High(A), ' ', A = nil, ' ', A[0], A[4]);
  for I := 0 to High(A) do
    A[I] := I * I;
  Sum := 0;
  for I := Low(A) to High(A) do
    Sum := Sum + A[I];
  Writeln(Sum);
  SetLength(A, 3);
  Writeln(Length(A), ' ', A[2]);
  SetLength(A, 6);
  Writeln(A[2], ' ', A[3], ' ', A[5]);
  Inc(A[5], 7);
  A[1] := A[1] + A[5];
  Writeln(A[1], ' ', A[5]);
  SetLength(D, 2);
  D[0] := 1.5;
  D[1] := D[0] * 2;
  Writeln(D[1]:0:1, ' ', Length(D));
  A := nil;
  Writeln(Length(A), ' ', A = nil, ' ', A <> nil);
  SetLength(A, 0);
  Writeln(Length(A));
  SetLength(A, 2);
  {$R+}
  I := 1;
  A[I] := 5;
  Writeln(A[I]);
  I := 2;
  A[I] := 6;
  Writeln('never');
end.
