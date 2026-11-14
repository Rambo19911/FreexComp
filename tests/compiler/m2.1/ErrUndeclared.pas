// covers: LANG-310, CMP-507
program ErrUndeclared;
var
  X: Integer;

procedure P;
begin
  Missing := 1;
  X := Missing;
end;

begin
  X := Y + 1;
  Y := 2;
  Writeln(Undef);
  Missing := 3;
end.
