// covers: RTL-090
program Sys_WriteWidth;
{ system.md section 10.2 / LANG-915: right-aligned, spaces on the left, width counted in UTF-16 code units;
  a zero or negative width pads nothing; a value wider than the width is never truncated. '|' marks the edges. }
var
  W: Integer;
  U: Cardinal;
  C: Char;
  A: AnsiChar;
begin
  Writeln('|', 42:6, '|', -42:6, '|', 42:2, '|', 12345:3, '|', 7:0, '|', 7:-5, '|');
  U := 4000000000;
  Writeln('|', U:12, '|', Low(Int64):25, '|', Low(Int64):1, '|');
  Writeln('|', True:6, '|', False:6, '|', True:2, '|', False:-1, '|');
  C := 'x';
  A := 'y';
  Writeln('|', C:3, '|', A:3, '|', C:1, '|', C:0, '|', 'z':-3, '|');
  Writeln('|', 'abc':5, '|', 'abc':3, '|', 'abc':1, '|', '':4, '|', 'ä':3, '|');
  Writeln('|', #$D83D#$DE00:3, '|', #$D83D#$DE00:2, '|');     { a pair is two code units }
  for W := -1 to 3 do
    Write('|', 5:W);
  Writeln('|');
end.
