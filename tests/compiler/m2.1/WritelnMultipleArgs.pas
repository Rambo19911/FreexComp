// covers: LANG-910, LANG-911, LANG-920, LANG-921, LANG-922
program WritelnMultipleArgs;
var
  I: Integer;
  C: Char;
  B: Boolean;
  W: Word;
begin
  I := -5;
  C := 'z';
  B := True;
  W := 65535;
  Writeln('a=', 1, ' b=', True, ' c=', 'x', ' ', I);
  Writeln(I, C, B, W);
  Writeln(1, 2, 3);
  Writeln('i=', I, '; c=', C, '; b=', B, '; w=', W, '; f=', False);
end.
