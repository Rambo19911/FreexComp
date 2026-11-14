// covers: LANG-257, ABI-043, CMP-330
program DynArray2D;
// Arrays of arrays: SetLength(a, 3, 4) sizes every row, a[i, j] = a[i][j], rows can be resized and shared one by one.
type
  TRow = array of Integer;
  TGrid = array of TRow;
var
  G: TGrid;
  M: array of array of Char;
  R: TRow;
  I, J: Integer;
begin
  SetLength(G, 3, 4);
  Writeln(Length(G), ' ', Length(G[0]), ' ', Length(G[2]));
  for I := 0 to High(G) do
    for J := 0 to High(G[I]) do
      G[I, J] := I * 10 + J;
  Writeln(G[2][3], ' ', G[1, 2], ' ', G[0, 0]);
  SetLength(G[1], 2);
  Writeln(Length(G[1]), ' ', G[1, 1], ' ', Length(G[0]));
  R := G[2];
  R[0] := 99;
  Writeln(G[2, 0]);
  SetLength(G, 4);
  Writeln(Length(G), ' ', Length(G[3]), ' ', G[3] = nil, ' ', G[2, 0]);
  SetLength(G[3], 1);
  G[3, 0] := 7;
  for I := 0 to High(G) do
  begin
    for J := 0 to High(G[I]) do
      Write(G[I, J], ' ');
    Writeln('|');
  end;
  SetLength(M, 2, 3);
  for I := 0 to 1 do
    for J := 0 to 2 do
      M[I, J] := Chr(Ord('a') + I * 3 + J);
  Writeln(M[0, 0], M[0, 2], M[1, 0], M[1, 2]);
  G := nil;
  Writeln(Length(R), ' ', R[0]);
end.
