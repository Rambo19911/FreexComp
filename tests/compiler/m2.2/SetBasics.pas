// covers: LANG-250, LANG-251, LANG-252, LANG-253
program SetBasics;
type
  TColor = (Red, Green, Blue, Yellow);
  TColors = set of TColor;
  TDigits = set of 0..9;
var
  S: TColors;
  D: TDigits;
  C: TColor;
  I: Integer;

procedure ShowColors(const S: TColors);
var
  C: TColor;
begin
  Write('[');
  for C := Low(TColor) to High(TColor) do
    if C in S then
      Write(' ', Ord(C));
  Writeln(' ]');
end;

procedure ShowDigits(D: TDigits);
var
  I: Integer;
begin
  Write('{');
  for I := 0 to 9 do
    if I in D then
      Write(' ', I);
  Writeln(' }');
end;

begin
  S := [];
  ShowColors(S);
  S := [Red, Blue];
  ShowColors(S);
  Writeln(Red in S, ' ', Green in S, ' ', Blue in S);
  C := Green;
  S := [C];
  ShowColors(S);
  Include(S, Yellow);
  Include(S, Green);
  ShowColors(S);
  Exclude(S, Green);
  Exclude(S, Red);
  ShowColors(S);
  D := [1, 3, 5, 7, 9];
  ShowDigits(D);
  I := 4;
  D := [I, I + 1, 0];
  ShowDigits(D);
  Include(D, 9);
  Exclude(D, I);
  ShowDigits(D);
  Writeln(5 in D, ' ', 4 in D, ' ', 100 in D, ' ', -1 in D);
  I := 100;
  Writeln(I in D);
  Writeln(SizeOf(S), ' ', SizeOf(D));
end.
