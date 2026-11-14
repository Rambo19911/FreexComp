// covers: LANG-257, ABI-042, CMP-330
program DynArrayRefSemantics;
// Assignment shares the block (an element write is seen through both), Copy makes a unique copy (with index and count,
// clamped), SetLength makes the array unique; = and <> compare references.
type
  TInts = array of Integer;
var
  A, B, C: TInts;
  I: Integer;

procedure Show(const Title: string; const X: TInts);
var
  K: Integer;
begin
  Write(Title, ':');
  for K := 0 to High(X) do
    Write(' ', X[K]);
  Writeln;
end;

begin
  SetLength(A, 4);
  for I := 0 to 3 do
    A[I] := I + 1;
  B := A;
  B[0] := 10;
  Show('A', A);
  Writeln(A = B, ' ', A <> B);
  C := Copy(A);
  C[1] := 20;
  Show('A', A);
  Show('C', C);
  Writeln(A = C);
  SetLength(B, 4);
  B[2] := 30;
  Show('A', A);
  Show('B', B);
  Writeln(A = B);
  C := Copy(A, 1, 2);
  Show('C', C);
  C := Copy(A, 2);
  Show('C', C);
  C := Copy(A, -5, 2);
  Show('C', C);
  C := Copy(A, 9, 2);
  Show('C', C);
  Writeln(C = nil, ' ', Length(C));
  A := Copy(A, 1, 100);
  Show('A', A);
  B := A;
  A := nil;
  Show('B', B);
end.
