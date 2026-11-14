// covers: LANG-911, LANG-915
program WriteWidthInt;
var
  I, N: Integer;
  C: Char;
  B: Boolean;
begin
  I := 42;
  C := 'x';
  B := True;
  Writeln('[', I:5, ']');
  Writeln('[', I:2, ']');
  Writeln('[', I:1, ']');
  Writeln('[', -7:4, ']');
  Writeln('[', C:3, ']');
  Writeln('[', B:6, ']');
  Writeln('[', False:7, ']');
  Writeln('[', 'ab':5, ']');
  Writeln('[', 'abc':0, ']');
  Writeln('[', 123:-3, ']');
  N := 8;
  Writeln('[', 5:N, ']');
  Writeln('[', '':3, ']');
  Writeln('[', 'q':2, ']');
  Writeln('[', I:N - 4, I:3, ']');
  Writeln('[', 18446744073709551615:22, ']');
end.
