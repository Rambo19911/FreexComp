// covers: LANG-255, CMP-308
program StaticArrayEnumIndex;
type
  TColor = (Red, Green, Blue);
  TWeights = array[TColor] of Integer;
var
  Weight: TWeights;
  Seen: array[Boolean] of Integer;
  Count: array[Char] of Byte;
  Letters: array['a'..'e'] of Char;
  Bright: array[Green..Blue] of Integer;
  C: TColor;
  Ch: Char;
  I: Integer;
begin
  Weight[Red] := 3;
  Weight[Green] := 5;
  Weight[Blue] := 7;
  for C := Low(TColor) to High(TColor) do
    Write(Ord(C), '=', Weight[C], ' ');
  Writeln('|');
  Writeln(Length(Weight), ' ', SizeOf(Weight), ' ', Ord(Low(Weight)), ' ', Ord(High(Weight)));
  Seen[False] := 0;
  Seen[True] := 0;
  for I := 1 to 10 do
    Inc(Seen[Odd(I)]);
  Writeln(Seen[False], ' ', Seen[True], ' ', Low(Seen), ' ', High(Seen));
  for I := 1 to 3 do
    Inc(Count['x']);
  Ch := 'q';
  Count[Ch] := 42;
  Writeln(Count['x'], ' ', Count['q'], ' ', Count['a'], ' ', SizeOf(Count));
  for Ch := 'a' to 'e' do
    Letters[Ch] := Chr(Ord(Ch) - 32);
  for Ch := Low(Letters) to High(Letters) do
    Write(Letters[Ch]);
  Writeln;
  Bright[Green] := 1;
  Bright[Blue] := 2;
  Writeln(Bright[Green] + Bright[Blue], ' ', Length(Bright), ' ', Ord(Low(Bright)));
end.
