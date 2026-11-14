// covers: CMP-412
program MoveFillNegativeCount;
var
  A, B: array[0..3] of Byte;
  N, I: Integer;
  M: NativeInt;

procedure Show;
var
  K: Integer;
begin
  for K := 0 to 3 do
    Write(A[K], ' ');
  for K := 0 to 3 do
    Write(B[K]);
  Writeln;
end;

begin
  for I := 0 to 3 do
  begin
    A[I] := I + 1;
    B[I] := 9;
  end;
  Show;
  { a count <= 0 does nothing (no huge memset/memmove) }
  N := -1;
  FillChar(A, N, 0);
  Move(A, B, N);
  Show;
  N := 0;
  FillChar(A, N, 0);
  Move(A, B, N);
  Show;
  M := -100000;
  FillChar(B, M, 0);
  Move(B, A, M);
  Show;
  FillChar(A, -5, 0);
  Move(A, B, -5);
  Show;
  { a positive variable count still works }
  N := 2;
  Move(A, B, N);
  FillChar(A, N, 7);
  Show;
end.
