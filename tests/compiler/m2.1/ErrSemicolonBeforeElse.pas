// covers: LANG-180
program ErrSemicolonBeforeElse;
var
  X: Integer;
begin
  X := 1;
  if X > 0 then
    Writeln('positive');
  else
    Writeln('not positive');
end.
