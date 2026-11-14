// covers: LANG-361
program ForLoopDown;
var
  I: Integer;
  B: Byte;
  X: Int64;
begin
  for I := 5 downto 1 do
    Write(I, ' ');
  Writeln('|');
  for I := 1 downto 2 do
    Writeln('never');
  Writeln('empty');
  for I := 0 downto -3 do
    Write(I, ' ');
  Writeln('|');
  for B := 3 downto 0 do
    Write(B, ' ');
  Writeln('|');
  for X := 5000000002 downto 5000000000 do
    Write(X, ' ');
  Writeln('|');
  for I := Low(Integer) + 2 downto Low(Integer) do
    Write(I, ' ');
  Writeln('|');
end.
