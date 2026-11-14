// covers: LANG-344, LANG-259, LANG-522, CMP-341
program TypedConstArray;
type
  TColor = (Red, Green, Blue);
  TVec = array[1..4] of Integer;
  TGrid = array[0..1, 0..2] of Byte;
const
  Primes: array[0..5] of Word = (2, 3, 5, 7, 11, 13);
  Names: array[TColor] of Char = ('r', 'g', 'b');
  Grid: TGrid = ((1, 2, 3), (4, 5, 6));
  Flags: array[Boolean] of Boolean = (True, False);
  One: array[1..1] of Integer = (42);
  Big: array[1..3] of Int64 = (-1, 5000000000, High(Int64));
  Digits: set of 0..9 = [1, 3, 5];
  Count: Integer = 3;
  Letters: array[1..2] of set of Char = (['a'..'c'], []);
var
  V: TVec = (10, 20, 30, 40);
  W: TVec;
  G: TGrid;
  I: Integer;
  Sum: Integer;
  C: TColor;

function Weekday(N: Integer): Char;
const
  Days: array[0..6] of Char = ('M', 'T', 'W', 'T', 'F', 'S', 'S');
begin
  Result := Days[N mod 7];
end;

begin
  Sum := 0;
  for I := Low(Primes) to High(Primes) do
    Sum := Sum + Primes[I];
  Writeln(Sum, ' ', Length(Primes), ' ', SizeOf(Primes));
  for C := Red to Blue do
    Write(Names[C]);
  Writeln;
  Writeln(Grid[0, 2], ' ', Grid[1][0], ' ', Grid[1, 2]);
  Writeln(Flags[True], ' ', Flags[False], ' ', One[1]);
  Writeln(Big[1], ' ', Big[2], ' ', Big[3]);
  Writeln(3 in Digits, ' ', 4 in Digits, ' ', Count);
  Writeln('b' in Letters[1], ' ', 'b' in Letters[2]);
  W := V;
  V[2] := 0;
  Writeln(V[1], ' ', V[2], ' ', W[2], ' ', V[4]);
  G := Grid;
  G[0, 0] := 9;
  Writeln(G[0, 0], ' ', Grid[0, 0]);
  Writeln(Weekday(0), Weekday(4), Weekday(13));
end.
