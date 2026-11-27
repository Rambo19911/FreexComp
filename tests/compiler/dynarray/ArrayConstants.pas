// covers: LANG-274, CMP-1416
program ArrayConstants;

{ Typed dynamic-array constants at unit, routine and inline level: integers, strings, records of strings, nested arrays and
  the empty array. A variable never shares a constant's storage: assigning the constant and then writing the variable (also
  after SetLength) leaves the constant unchanged. }

type
  TInts = array of Integer;
  TGrid = array of TInts;
  TStrs = array of string;
  TPair = record
    N: Integer;
    S: string;
  end;
  TPairs = array of TPair;

const
  Primes: TInts = [2, 3, 5, 7];
  None: TInts = [];
  Names: TStrs = ['alpha', '', 'gamma'];
  Pairs: TPairs = [(N: 1; S: 'one'), (N: 2; S: 'two'), (N: 3)];
  Grid: TGrid = [[1], [2, 3], [], [4, 5, 6]];
  Reals: array of Double = [1.5, -2.25];
  Bytes: array of Byte = [255, 0, 7];

procedure Show(const Title: string; const A: TInts);
var
  I: Integer;
begin
  Write(Title, ' (', Length(A), '):');
  for I := 0 to High(A) do
    Write(' ', A[I]);
  WriteLn;
end;

function SumByValue(A: TInts): Integer;
var
  X: Integer;
begin
  Result := 0;
  for X in A do
    Inc(Result, X);
end;

function SumByConst(const A: TInts): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := Low(A) to High(A) do
    Inc(Result, A[I]);
end;

function Copied: TInts;
begin
  Result := Primes;
  Result[0] := -1;
end;

procedure RoutineLevel;
const
  Local: TInts = [10, 20, 30];
  Words: TStrs = ['x', 'yz'];
var
  B: TInts;
begin
  B := Local;
  B[1] := 0;
  Show('Local', Local);
  Show('B', B);
  WriteLn('Words ', Words[0], Words[1], ' ', Length(Words));
end;

procedure InlineLevel;
begin
  const C: TInts = [7, 8, 9];
  const G: TGrid = [[1, 1], []];
  var D := C;
  D[0] := 70;
  Show('C', C);
  Show('D', D);
  WriteLn('G ', Length(G), ' ', Length(G[0]), ' ', Length(G[1]), ' ', G[0][1]);
  for var X in C do
    Write(' ', X * 2);
  WriteLn;
end;

var
  B: TInts;
  G: TGrid;
  P: TPairs;
  S: TStrs;
  I: Integer;
  X: Integer;
begin
  Show('Primes', Primes);
  Show('None', None);
  WriteLn('Assigned(None) ', Assigned(None), ' Length ', Length(None), ' High ', High(None));
  WriteLn('Length ', Length(Primes), ' High ', High(Primes), ' Low ', Low(Primes));

  { assignment, then a write: a copy }
  B := Primes;
  B[0] := 99;
  Show('B', B);
  Show('Primes', Primes);

  { assignment, then SetLength and a write: a copy }
  B := Primes;
  SetLength(B, 5);
  B[0] := 99;
  B[4] := 11;
  Show('B', B);
  Show('Primes', Primes);
  Show('Copied', Copied);
  Show('Primes', Primes);
  Show('Copy', Copy(Primes, 1, 2));

  { for-in, by value and const parameters }
  for var Y in Primes do
    Write(' ', Y);
  WriteLn;
  WriteLn('Sums ', SumByValue(Primes), ' ', SumByConst(Primes), ' ', SumByValue(None));

  { strings }
  for I := 0 to High(Names) do
    Write('[', Names[I], ']');
  WriteLn(' ', Length(Names));
  S := Names;
  S[1] := 'beta';
  WriteLn(S[0], ' ', S[1], ' ', S[2], ' / ', Names[1] = '');

  { records of strings }
  for I := 0 to High(Pairs) do
    Write(' ', Pairs[I].N, '=', Pairs[I].S, ';');
  WriteLn;
  P := Pairs;
  P[2].S := 'three';
  WriteLn(P[2].S, ' ', Pairs[2].S = '');

  { nested arrays }
  for I := 0 to High(Grid) do
  begin
    Write('Row ', I, ':');
    for X in Grid[I] do
      Write(' ', X);
    WriteLn;
  end;
  G := Grid;
  G[1][0] := 20;
  WriteLn(G[1][0], ' ', Grid[1][0]);
  B := Grid[3];
  B[2] := 60;
  WriteLn(B[2], ' ', Grid[3][2]);

  { other element types }
  WriteLn(Reals[0]:0:2, ' ', Reals[1]:0:2, ' ', Bytes[0], ' ', Bytes[1], ' ', Bytes[2]);

  { equality is a reference comparison }
  B := Primes;
  WriteLn('Same ', Primes = Primes, ' ', B = Primes, ' ', None = nil);

  RoutineLevel;
  RoutineLevel;
  InlineLevel;
end.
