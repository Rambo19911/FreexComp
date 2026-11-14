// covers: LANG-362
program ForInArrayStringSet;

type
  TColor = (Red, Green, Blue, Black);
  TColors = set of TColor;
  TCounts = array[TColor] of Integer;
  TNames = array of string;

var
  Calls: Integer;

function Names: TNames;
begin
  Inc(Calls);
  SetLength(Result, 2);
  Result[0] := 'x';
  Result[1] := 'y';
end;

procedure Open(const A: array of Integer);
var
  V: Integer;
begin
  for V in A do
    Write(V, ';');
  Writeln;
end;

procedure Locals;
var
  S: string;
  C: Char;
  N: Integer;
begin
  S := 'loc';
  N := 0;
  for C in S do
  begin
    if C = 'o' then
      Continue;
    Inc(N);
    S := 'changed';
  end;
  Writeln(N, ' ', S);
end;

var
  Fixed: array[1..4] of Integer;
  Dyn: array of string;
  Counts: TCounts;
  I, Sum: Integer;
  S: string;
  C: Char;
  Color: TColor;
  Colors: TColors;
  Letters: set of Char;
  Small: set of 0..20;
begin
  for I := 1 to 4 do
    Fixed[I] := I * I;
  Sum := 0;
  for I in Fixed do
    Sum := Sum + I;
  Writeln(Sum);
  SetLength(Dyn, 3);
  Dyn[0] := 'a';
  Dyn[1] := 'b';
  Dyn[2] := 'c';
  for S in Dyn do
  begin
    Write(S);
    Dyn := nil;
  end;
  Writeln(' ', Length(Dyn));
  for C in 'hey' do
    Write(Ord(C), ' ');
  Writeln;
  Colors := [Blue, Red, Black];
  for Color in Colors do
    Write(Ord(Color));
  Writeln;
  Letters := ['z', 'a', 'm'];
  for C in Letters do
    Write(C);
  Writeln;
  Small := [20, 3, 0];
  for I in Small do
  begin
    if I = 20 then
      Break;
    Write(I, ',');
  end;
  Writeln;
  Counts[Green] := 5;
  for I in Counts do
    Write(I);
  Writeln;
  Calls := 0;
  for S in Names do
    Write(S);
  Writeln(' ', Calls);
  Open([7, 8, 9]);
  Open([]);
  Locals;
end.
