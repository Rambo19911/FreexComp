// covers: LANG-256, LANG-207, CMP-326
program StaticArrayCopy;
type
  TRow = array[1..4] of Integer;
  TMatrix = array[1..3] of TRow;
  TPair = array[0..1] of Word;
var
  A, B: TRow;
  M: TMatrix;
  P, Q: TPair;
  I: Integer;

procedure Show(var R: TRow);
var
  I: Integer;
begin
  for I := 1 to 4 do
    Write(R[I], ' ');
  Writeln('|');
end;

procedure Change(var R: TRow);
var
  Local: TRow;
begin
  Local := R;
  Local[1] := 999;
  Show(Local);
  R[4] := -1;
end;

begin
  for I := 1 to 4 do
    A[I] := I;
  B := A;
  B[2] := 20;
  Show(A);
  Show(B);
  Change(A);
  Show(A);
  M[1] := A;
  M[2] := B;
  M[3] := M[1];
  M[1][1] := 100;
  Show(M[1]);
  Show(M[3]);
  M[2] := M[2];
  Show(M[2]);
  A := M[2];
  Show(A);
  P[0] := 7;
  P[1] := 65535;
  Q := P;
  P[1] := 0;
  Writeln(Q[0], ' ', Q[1], ' ', P[1]);
end.
