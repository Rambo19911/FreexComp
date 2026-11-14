// covers: LANG-180, LANG-181
program ErrMissingSemicolon;
var
  X: Integer;
begin
  X := 1
  Writeln(X);
end.
