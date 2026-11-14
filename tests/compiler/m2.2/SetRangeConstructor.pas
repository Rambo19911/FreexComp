// covers: LANG-251, LANG-252
program SetRangeConstructor;
type
  TSet = set of 0..31;
  TWide = set of 0..63;
  TColor = (Red, Green, Blue, Cyan, Magenta);
  TColors = set of TColor;
var
  S: TSet;
  W: TWide;
  Lo, Hi: Integer;
  C: TColors;
  X, Y: TColor;

procedure Show(S: TSet);
var
  I: Integer;
begin
  Write('{');
  for I := 0 to 31 do
    if I in S then
      Write(' ', I);
  Writeln(' }');
end;

procedure ShowColors(S: TColors);
var
  C: TColor;
begin
  Write('[');
  for C := Red to Magenta do
    if C in S then
      Write(' ', Ord(C));
  Writeln(' ]');
end;

function Count(S: TWide): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to 63 do
    if I in S then
      Inc(Result);
end;

begin
  Lo := 3;
  Hi := 7;
  S := [Lo..Hi];
  Show(S);
  S := [Hi..Lo];
  Show(S);
  S := [Lo..Lo];
  Show(S);
  S := [0..Lo, Hi..Hi + 2, 20];
  Show(S);
  Hi := 40;
  S := [Lo + 20..Hi];
  Show(S);
  Lo := -5;
  Hi := 2;
  S := [Lo..Hi];
  Show(S);
  Lo := 0;
  Hi := 63;
  W := [Lo..Hi];
  Writeln(Count(W));
  W := [Lo + 1..Hi - 1];
  Writeln(Count(W), ' ', 0 in W, ' ', 1 in W, ' ', 62 in W, ' ', 63 in W);
  X := Green;
  Y := Magenta;
  C := [X..Y];
  ShowColors(C);
  C := [Y..X];
  ShowColors(C);
  C := [Red, X..Pred(Y)];
  ShowColors(C);
end.
