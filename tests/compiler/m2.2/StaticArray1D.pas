// covers: LANG-255, LANG-258, CMP-308
program StaticArray1D;
type
  TSquares = array[1..5] of Integer;
var
  Squares: TSquares;
  Offsets: array[-3..3] of Int64;
  Bytes: array[10..14] of Byte;
  I, Sum: Integer;

procedure Show(var A: TSquares);
var
  I: Integer;
begin
  for I := Low(A) to High(A) do
    Write(A[I], ' ');
  Writeln('|');
end;

begin
  for I := 1 to 5 do
    Squares[I] := I * I;
  Show(Squares);
  Writeln(Low(Squares), ' ', High(Squares), ' ', Length(Squares), ' ', SizeOf(Squares));
  Squares[3] := -9;
  Inc(Squares[5], 100);
  Show(Squares);
  Sum := 0;
  for I := Low(Squares) to High(Squares) do
    Sum := Sum + Squares[I];
  Writeln(Sum);
  for I := -3 to 3 do
    Offsets[I] := Int64(I) * 1000000000;
  Writeln(Offsets[-3], ' ', Offsets[0], ' ', Offsets[3]);
  Writeln(Low(Offsets), ' ', High(Offsets), ' ', Length(Offsets), ' ', SizeOf(Offsets));
  for I := 10 to 14 do
    Bytes[I] := 250 + I - 10;
  Writeln(Bytes[10], ' ', Bytes[14], ' ', SizeOf(Bytes));
  I := 12;
  Bytes[I] := Bytes[I] + 10;
  Writeln(Bytes[12]);
end.
