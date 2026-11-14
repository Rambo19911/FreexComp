// covers: LANG-361, LANG-240
program ForLoopChar;
var
  C: Char;
  B: Boolean;
  N: Integer;
begin
  for C := 'a' to 'e' do
    Write(C);
  Writeln('|');
  for C := 'E' downto 'A' do
    Write(C);
  Writeln('|');
  N := 0;
  for C := #0 to #$FFFF do
    N := N + 1;
  Writeln(N);
  for B := False to True do
    Write(B, ' ');
  Writeln('|');
  for C := 'x' to 'w' do
    Write('never');
  Writeln('end');
end.
