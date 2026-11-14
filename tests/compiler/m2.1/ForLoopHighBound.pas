// covers: LANG-361
program ForLoopHighBound;
var
  B: Byte;
  I, Count: Integer;
  W: Word;
  C: Cardinal;
  U: UInt64;
  S: ShortInt;
begin
  for B := 250 to 255 do
    Write(B, ' ');
  Writeln('|');
  Count := 0;
  for I := High(Integer) - 3 to High(Integer) do
    Count := Count + 1;
  Writeln('Integer ', Count);
  Count := 0;
  for W := 65530 to High(Word) do
    Count := Count + 1;
  Writeln('Word ', Count);
  Count := 0;
  for C := High(Cardinal) - 1 to High(Cardinal) do
    Count := Count + 1;
  Writeln('Cardinal ', Count);
  Count := 0;
  for U := High(UInt64) - 2 to High(UInt64) do
    Count := Count + 1;
  Writeln('UInt64 ', Count);
  for S := 125 to High(ShortInt) do
    Write(S, ' ');
  Writeln('|');
  Count := 0;
  for B := 0 to 255 do
    Count := Count + 1;
  Writeln('all bytes ', Count);
  for B := 2 downto Low(Byte) do
    Write(B, ' ');
  Writeln('|');
end.
