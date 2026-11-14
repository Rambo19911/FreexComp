// covers: LANG-255, ABI-062, CMP-308
program StaticArray2D;
type
  TMatrix = array[1..3, 1..4] of Integer;
var
  M: TMatrix;
  Cube: array[0..1, 0..1, 0..1] of Byte;
  I, J, K: Integer;
begin
  for I := 1 to 3 do
    for J := 1 to 4 do
      M[I, J] := I * 10 + J;
  for I := 1 to 3 do
  begin
    for J := 1 to 4 do
      Write(M[I][J], ' ');
    Writeln('|');
  end;
  Writeln(M[2, 3], ' ', M[2][3], ' ', M[3, 4]);
  M[1][2] := 0;
  M[3, 1] := M[1, 2] + M[2, 2];
  Writeln(M[1, 2], ' ', M[3, 1]);
  Writeln(SizeOf(M), ' ', SizeOf(M[1]), ' ', Length(M), ' ', Length(M[2]), ' ', Low(M[1]), ' ', High(M[3]));
  for I := 0 to 1 do
    for J := 0 to 1 do
      for K := 0 to 1 do
        Cube[I, J, K] := I * 4 + J * 2 + K;
  Writeln(Cube[1, 0, 1], ' ', Cube[0][1][1], ' ', Cube[1, 1][0], ' ', SizeOf(Cube));
end.
